using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Project;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The front door: the week, the five desks, and what has happened lately.
    ///
    /// A coordinator's week has a shape — bring in the new models, run the rules over what that
    /// broke, send out the result — and a product whose front page is a menu makes them rebuild
    /// that shape in their head every Monday. This screen is the shape with the state filled in,
    /// and under it the five desks, each with the one sentence that says which job it is.
    ///
    /// It is what the hub window shows. It is a place to
    /// leave from: no tool lives here.
    /// </summary>
    public static class HomeView
    {
        /// <summary>
        /// Build the screen.
        /// </summary>
        /// <param name="goTo">Open a tool by its id.</param>
        public static UIElement Build(Action<string> goTo)
        {
            var session = Host.Current;

            var stack = Ui.Stack();

            if (session == null)
            {
                stack.Children.Add(Ui.Empty("No model is open.", Host.NoModel));
                stack.Children.Add(Ui.Rule());
                stack.Children.Add(DeskList());
                return Ui.Scroll(stack);
            }

            stack.Children.Add(Header(session));
            stack.Children.Add(Cycle(session, goTo));
            stack.Children.Add(Ui.Rule());
            stack.Children.Add(DeskList());
            stack.Children.Add(Ui.Rule());
            stack.Children.Add(Recent(session));

            return Ui.Scroll(stack);
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

            var body = Ui.Stack(name, chips);

            // Read-only is the one store problem that stops every button in the product, so it is
            // the one that is said here rather than on Project Settings.
            if (session.Store.IsReadOnly)
                body.Children.Add(Ui.Note(session.Store.ReadOnlyReason ?? string.Empty));

            return body;
        }

        /// <summary>
        /// The three steps of the week, with when each last happened.
        ///
        /// Only the first undone step leads: on a project's first day all three are undone, and
        /// three blue buttons is the same as none.
        /// </summary>
        private static UIElement Cycle(Session session, Action<string> goTo)
        {
            var rows = Ui.Stack();

            var label = Ui.Eyebrow("The week");
            label.Margin = new Thickness(0, 12, 0, 4);
            rows.Children.Add(label);

            var step = 0;
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

            return rows;
        }

        /// <summary>The step's number, in the colour of the desk it opens.</summary>
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
            _ => "Coordinate",
        };

        /// <summary>Which tool the step opens.</summary>
        private static string RouteFor(string kind) => kind switch
        {
            ActivityKind.Reconcile => "health",
            ActivityKind.Regroup => "issuelist",
            _ => "clashreport",
        };

        /// <summary>What the step's button says.</summary>
        private static string GoLabel(string kind) => kind switch
        {
            ActivityKind.Reconcile => "Check the models",
            ActivityKind.Regroup => "Open the board",
            _ => "Write the report",
        };

        /// <summary>
        /// The suite: every app, the job it is, and the way to it.
        /// </summary>
        private static UIElement DeskList()
        {
            var stack = Ui.Stack();

            var label = Ui.Eyebrow("The apps");
            label.Margin = new Thickness(0, 12, 0, 4);
            stack.Children.Add(label);

            foreach (var app in Apps.All)
            {
                var here = app;

                var marker = new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(0, 2, 9, 2),
                };

                marker.SetResourceReference(Border.BackgroundProperty, "Fam." + here.Family);

                var text = Ui.Stack(
                    Ui.Line(here.Title, 1, true),
                    Ui.Line(here.Job, 0.7));

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(marker);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                var button = new Button
                {
                    Content = row,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 2),
                };

                button.Wearing("RailRow");
                button.Click += (s, e) => Ui.Told(AppWindow.Show(here.Id));

                stack.Children.Add(button);
            }

            stack.Children.Add(Ui.Small("Every tool is a tab of one of these, and the search above "
                                        + "knows them all by what they do."));

            return stack;
        }

        private static UIElement Recent(Session session)
        {
            var entries = session.Store.Activity.Entries.Take(12).ToList();

            var stack = Ui.Stack();

            var label = Ui.Eyebrow("Recent");
            label.Margin = new Thickness(0, 12, 0, 4);
            stack.Children.Add(label);

            if (entries.Count == 0)
            {
                stack.Children.Add(Ui.Empty("Nothing has happened on this project yet.",
                    "CamelWorks writes a line here every time it changes the model, produces a "
                    + "report or runs the rules — so a project handed over mid-job says what was "
                    + "done to it."));

                return stack;
            }

            var rows = entries.Select(e => new TableRow(e,
                ActivityKind.Title(e.Kind),
                e.Summary,
                ActivityLog.Ago(e.WhenTicks, session.NowTicks))).ToList();

            var recent = Ui.Table(rows, ("What", 110), ("Happened", 0), ("When", 110));

            stack.Children.Add(Ui.Picking(recent,
                new Ui.OnPicked("Copy {n}",
                    (picked, say) => Ui.Copy(Ui.Lines(recent, picked), say, "entry"))));

            return stack;
        }
    }
}
