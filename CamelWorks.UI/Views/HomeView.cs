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
            var name = Ui.Eyebrow(session.Profile.ProjectName);
            name.Margin = new Thickness(0, 0, 0, 6);

            var chips = Ui.Across(
                Ui.Pill(Ui.Count(session.Model.Models.Count, "model")),
                Ui.Pill(session.Document.Units.ToString()),
                Ui.Pill(session.Clash.IsAvailable
                    ? Ui.Count(session.Clash.Tests().Count, "clash test")
                    : "no clash engine", session.Clash.IsAvailable ? Tone.Plain : Tone.Warn));

            // The project file belongs where its problems are reported, which is Setup.
            var body = Ui.Stack(name, chips);

            // The warn-toned chip above already says there is no clash engine; a sixty-word banner
            // saying it again is the front door explaining itself instead of showing the week.
            // Backup-used and concurrency are facts about a file rather than about this week, and
            // they are reported on Setup, where the file is. Read-only is the one that stops every
            // button in the product, so it is the one that stays.
            if (session.Store.IsReadOnly)
                body.Children.Add(Ui.Note(session.Store.ReadOnlyReason ?? string.Empty));

            return body;
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

            // Only the first undone step leads. Set once, read by every row after it.
            var led = false;

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

                // The FIRST step nobody has done yet is the one that should look like the thing to
                // press. On a project's first day all three are undone, and three blue buttons is
                // the same as none.
                var lead = never && !led;
                if (never) led = true;

                var button = lead
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

        /// <summary>
        /// What the step's button says.
        ///
        /// The row is labelled with a verb — Reconcile, Regroup, Report — and the button used to
        /// be labelled with a place: Models, Board, Reports. Two vocabularies for one step, and
        /// the first of the three places is a tab called Health.
        /// </summary>
        private static string GoLabel(string kind) => kind switch
        {
            ActivityKind.Reconcile => "Check the models",
            ActivityKind.Regroup => "Open the board",
            _ => "Write the report",
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
                Ui.Table(rows, ("What", 110), ("Happened", 0), ("When", 110)));
        }
    }
}
