using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Project;


namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The front door: the weekly cycle, when each step last happened, and the way to each.
    ///
    /// A coordinator's week has a shape — bring in the new models, run the rules over what that
    /// broke, send out the result — and a product whose front page is a menu makes them rebuild
    /// that shape in their head every Monday. This screen is the shape, with the state filled in.
    /// </summary>
    public static class HomeView
    {
        /// <summary>Build the screen.</summary>
        /// <param name="goTo">Navigate the pane to a "workspace/tab" route.</param>
        public static UIElement Build(Action<string> goTo)
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("CamelWorks"),
                    Ui.Sub(Host.NoModel)));

            var body = Ui.Stack(
                Header(session),
                Cycle(session, goTo),
                Recent(session));

            return Ui.Scroll(body);
        }

        private static UIElement Header(Session session)
        {
            var chips = Ui.Across(
                Ui.Pill(Ui.Count(session.Model.Models.Count, "model")),
                Ui.Pill(session.Document.Units.ToString()),
                Ui.Pill(session.Clash.IsAvailable
                    ? Ui.Count(session.Clash.Tests().Count, "clash test")
                    : "no clash engine", session.Clash.IsAvailable ? Tone.Plain : Tone.Warn));

            var where = Ui.Stack(
                new TextBlock { Text = "PROJECT FILE" }.Wearing("Caps"),
                Ui.Code(session.Store.Where));

            where.Margin = new Thickness(0, 8, 0, 0);

            var body = Ui.Stack(chips, where);

            // A seat without the clash engine is not a seat without the coordination work, and the
            // one place somebody looks to find that out is the chip that just told them so.
            if (!session.Clash.IsAvailable)
                body.Children.Add(Ui.Note("This Navisworks edition has no Clash Detective, so tests "
                    + "cannot be run here. The board still opens: CamelWorks rebuilds it from what "
                    + "the project file recorded the last time a Manage seat ran, and triage, "
                    + "assignment, reports and BCF all work on it."));

            if (session.Store.UsedBackup)
                body.Children.Add(Ui.Note("The project file could not be read and its backup was used instead. "
                                          + "Nothing is lost that was saved before the last write."));

            if (session.Store.IsReadOnly)
                body.Children.Add(Ui.Note(session.Store.ReadOnlyReason ?? string.Empty));

            if (session.Store.ConcurrencyNote.Length > 0)
                body.Children.Add(Ui.Note(session.Store.ConcurrencyNote));

            return Ui.Marked(Glyphs.Model, session.Profile.ProjectName, null, body);
        }

        /// <summary>
        /// The three steps of the week, as three steps.
        ///
        /// This was three stacked paragraphs with a small grey button at the end of each: the same
        /// information, arranged so that nothing on the front door of the product looked more
        /// important than anything else. A numbered marker in the step's own colour, the state as a
        /// chip rather than a sentence, and one weighted action per row is the difference between
        /// a page somebody reads and a page somebody uses.
        /// </summary>
        private static UIElement Cycle(Session session, Action<string> goTo)
        {
            var rows = Ui.Stack();
            var step = 0;

            foreach (var kind in ActivityKind.Cycle)
            {
                step++;

                var latest = session.Store.Activity.Latest(kind);
                var never = latest == null;

                var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                line.Children.Add(Marker(step, kind));

                var left = Ui.Stack(
                    Ui.Line(ActivityKind.Title(kind), 1, true),
                    Ui.Line(never ? ActivityKind.Purpose(kind) : latest!.Summary, 0.75),
                    Ui.Across(never
                        ? Ui.Pill("Not done yet", Tone.Warn)
                        : Ui.Pill(ActivityLog.Ago(latest!.WhenTicks, session.NowTicks), Tone.Good)));

                left.Margin = new Thickness(11, 0, 10, 0);
                Grid.SetColumn(left, 1);
                line.Children.Add(left);

                // The step nobody has done yet is the one that should look like the thing to press.
                // Once it has been done, it steps back and lets the next one lead.
                var button = never
                    ? Ui.Primary(GoLabel(kind), () => goTo(RouteFor(kind)))
                    : Ui.Button(GoLabel(kind), () => goTo(RouteFor(kind)));

                button.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(button, 2);
                line.Children.Add(button);

                rows.Children.Add(line);
                if (step < ActivityKind.Cycle.Count) rows.Children.Add(Ui.Rule());
            }

            return Ui.Marked(Glyphs.Clock, "The week",
                "Reconcile, regroup, report. Each opens where the work actually happens.", rows);
        }

        /// <summary>The step's number, in the colour of the workspace it opens.</summary>
        private static UIElement Marker(int step, string kind)
        {
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock
                {
                    Text = step.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            badge.SetResourceReference(Border.BackgroundProperty, "Fam." + FamilyOf(kind));
            return badge;
        }

        private static string FamilyOf(string kind) => kind switch
        {
            ActivityKind.Reconcile => "Project",
            ActivityKind.Regroup => "Coordinate",
            _ => "Coordinate",
        };

        private static string RouteFor(string kind) => kind switch
        {
            ActivityKind.Reconcile => "Project/Health",
            ActivityKind.Regroup => "Coordinate/Triage",
            _ => "Coordinate/Report",
        };

        private static string GoLabel(string kind) => kind switch
        {
            ActivityKind.Reconcile => "Models",
            ActivityKind.Regroup => "Board",
            _ => "Reports",
        };

        private static UIElement Recent(Session session)
        {
            var entries = session.Store.Activity.Entries.Take(12).ToList();

            if (entries.Count == 0)
                return Ui.Marked(Glyphs.Report, "Recent", null,
                    Ui.Empty("Nothing has happened on this project yet.",
                        "CamelWorks writes a line here every time it changes the model, produces a "
                        + "report or runs the rules — so a project handed over mid-job says what was done to it."));

            var rows = entries.Select(e => new TableRow(e,
                ActivityKind.Title(e.Kind),
                e.Summary,
                ActivityLog.Ago(e.WhenTicks, session.NowTicks))).ToList();

            return Ui.Marked(Glyphs.Report, "Recent", null,
                Ui.Table(rows, ("What", 110), ("", 0), ("When", 110)));
        }
    }
}
