using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.People;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Everybody on the job, in one window, reachable from wherever somebody needs one of them.
    ///
    /// <b>One roster, two homes.</b> It is kept beside the person - so the same architect, the
    /// same fire engineer and the same three companies are there the moment a new model is
    /// opened, instead of being retyped per job - and it is written into the project as well, so
    /// somebody opening the job who has never met this roster still gets the team. Opening it
    /// merges what the project knows into what the person already had, which means a coordinator
    /// handing over a model hands over the team with it.
    ///
    /// <b>Roles, companies and teams are not lists.</b> They are what the people on the roster
    /// say they are, so there is nothing here to keep in step with anything: type "ARC" against
    /// two people and ARC becomes a thing that clashes can be given to. Take the last one off and
    /// it stops being one. A role nobody holds cannot be assigned to, which is the right answer
    /// and needs no rule of its own.
    /// </summary>
    internal static class TeamWindow
    {
        /// <summary>
        /// The roster as it stands: what this person carries, with the project's folded in.
        ///
        /// The person's own copy wins on a name they both hold, because it is the one they have
        /// been correcting. The project only fills in people they have never met.
        /// </summary>
        /// <param name="session">The session whose project may carry a roster.</param>
        internal static TeamRoster Read(Session? session)
        {
            var mine = TeamRoster.Mine();

            if (session == null) return mine;

            try
            {
                var theirs = TeamRoster.FromJson(
                    session.Store.Section(ProjectStore.ProfileSection)["roster"]);

                foreach (var member in theirs.Members)
                {
                    if (mine.Find(member.Id) != null) continue;

                    var added = mine.Add(member.Name, member.Party, member.Email);

                    added.Role = member.Role;
                    added.Team = member.Team;
                    added.Note = member.Note;
                    added.IsActive = member.IsActive;
                }
            }
            catch (Exception)
            {
                // A project that will not give up its roster is a person working from their own,
                // which is the more complete of the two anyway.
            }

            return mine;
        }

        /// <summary>Write it to both homes, and say where it did not reach.</summary>
        /// <param name="session">The session whose project also keeps a copy.</param>
        /// <param name="roster">The roster.</param>
        internal static string? Keep(Session? session, TeamRoster roster)
        {
            var carried = roster.Keep();

            if (session == null)
                return carried ? null : roster.LastProblem ?? "It could not be kept on this machine.";

            try
            {
                session.Store.Section(ProjectStore.ProfileSection).Set("roster", roster.ToJson());

                if (!session.Store.Save())
                    return "Kept on this machine, but not in the project: "
                           + (session.Store.LastSaveProblem ?? "the project file could not be written.");
            }
            catch (Exception e)
            {
                return "Kept on this machine, but not in the project: " + Ui.Describe(e);
            }

            return carried ? null : roster.LastProblem ?? "It could not be kept on this machine.";
        }

        /// <summary>
        /// Open it, and call back when somebody has changed something.
        ///
        /// The callback fires on close rather than on each edit: every screen that shows a name
        /// wants to redraw once, after the person is done, not eleven times while they type.
        /// </summary>
        /// <param name="near">The screen it belongs to; the window is owned by the host.</param>
        /// <param name="session">The session, for the project's copy.</param>
        /// <param name="onChanged">Called once, when something was changed.</param>
        internal static void Show(FrameworkElement near, Session? session, Action? onChanged)
        {
            _ = near;

            var roster = Read(session);
            var changed = false;

            var window = new Window
            {
                Title = "CamelWorks — the team",
                Width = 940,
                Height = 600,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var rows = Ui.Stack();
            var said = Ui.Stack();

            void Kept()
            {
                changed = true;
                said.Children.Clear();

                var problem = Keep(session, roster);

                if (problem != null) said.Children.Add(Ui.Problem(problem));
            }

            void Fill()
            {
                rows.Children.Clear();

                foreach (var member in roster.Members
                             .OrderBy(m => m.Name, StringComparer.CurrentCulture)
                             .ToList())
                    rows.Children.Add(Line(roster, member, Fill, Kept));

                if (roster.Members.Count == 0)
                    rows.Children.Add(Ui.Small("No members yet. Press “Add member” and complete "
                        + "the row. Every field is editable in place, and the roles, teams and "
                        + "companies used appear in the lists afterwards."));
            }

            Fill();

            var body = new DockPanel { Margin = new Thickness(18, 16, 18, 16), LastChildFill = true };

            var head = Ui.Stack(
                Ui.Eyebrow("The team"),
                Ui.Small("Kept beside you rather than inside one job, so the same people are here "
                         + "the next time you open a model — and written into this project as "
                         + "well, so whoever opens it next gets the team with it."));

            DockPanel.SetDock(head, Dock.Top);
            body.Children.Add(head);

            // The headings, once, over the rows - which is what makes it a table rather than a
            // list of controls. Every one of them is editable where it stands: the old window
            // had a row of boxes at the top and an Add button, and nothing already added could
            // be corrected at all.
            var headings = Columns();

            headings.Margin = new Thickness(0, 12, 0, 4);


            Put(headings, Ui.Eyebrow("Name"), 0);
            Put(headings, Ui.Eyebrow("Role"), 1);
            Put(headings, Ui.Eyebrow("Team"), 2);
            Put(headings, Ui.Eyebrow("Company"), 3);
            Put(headings, Ui.Eyebrow("Email"), 4);
            Put(headings, Ui.Eyebrow("BCF folder"), 5);

            DockPanel.SetDock(headings, Dock.Top);
            body.Children.Add(headings);

            var adding = Ui.Primary("Add member", () =>
            {
                var name = Fresh(roster);

                roster.Add(name);

                Fill();
                Kept();
            });

            adding.HorizontalAlignment = HorizontalAlignment.Left;
            adding.Margin = new Thickness(0, 8, 0, 0);

            DockPanel.SetDock(adding, Dock.Top);
            body.Children.Add(adding);

            var foot = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };

            var close = Ui.Primary("Done", () => window.Close());
            close.MinWidth = 92;

            DockPanel.SetDock(close, Dock.Right);
            foot.Children.Add(close);

            var ways = new StackPanel { Orientation = Orientation.Horizontal };

            var send = Ui.Quiet("Save to a file", () =>
            {
                var path = Ui.AskToSave("Save the team", "Team|*.json", "team.json");

                if (string.IsNullOrWhiteSpace(path)) return;

                said.Children.Clear();

                said.Children.Add(roster.KeepAt(path)
                    ? Ui.Done("Saved. Send them that file.")
                    : Ui.Problem(roster.LastProblem ?? "It could not be written."));
            });

            send.Margin = new Thickness(0, 0, 8, 0);
            ways.Children.Add(send);

            ways.Children.Add(Ui.Quiet("Open a team file", () =>
            {
                var path = Ui.AskToOpen("Open a team file", "Team|*.json");

                if (string.IsNullOrWhiteSpace(path)) return;

                var read = TeamRoster.ReadFrom(path);

                foreach (var member in read.Members)
                {
                    var added = roster.Add(member.Name, member.Party, member.Email);

                    added.Role = member.Role ?? added.Role;
                    added.Team = member.Team ?? added.Team;
                    added.Folder = member.Folder ?? added.Folder;
                }

                Fill();
                Kept();
            }));

            foot.Children.Add(ways);

            DockPanel.SetDock(foot, Dock.Bottom);
            body.Children.Add(foot);

            DockPanel.SetDock(said, Dock.Bottom);
            body.Children.Add(said);

            body.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(rows),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 6, 0, 0),
            });

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            if (changed) onChanged?.Invoke();
        }

        /// <summary>
        /// The one column layout the headings and every row share.
        ///
        /// <b>Shares, never fixed widths.</b> Six columns at their old fixed widths came to more
        /// than the window and the last of them - the folder and its Browse - was simply cut off
        /// at the right edge, with no scrollbar to say so. Shares cannot do that: a narrow window
        /// squeezes every column a little instead of losing one whole, and a wide one spends the
        /// room on the fields that hold long text rather than on the end of the row.
        /// </summary>
        private static Grid Columns()
        {
            var grid = new Grid();

            // Name, Role, Team, Company, Email, BCF folder.
            foreach (var share in new[] { 1.3, 1.1, 0.9, 1.1, 1.3, 1.6 })
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(share, GridUnitType.Star),
                    MinWidth = 74,
                });

            // The bin, hard right. A fixed width rather than Auto: every row is a grid of its
            // own, so an Auto column is as wide as that row's own bin and the headings - which
            // have no bin - would be a column narrower than everything under them.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            return grid;
        }

        private static void Put(Grid grid, UIElement child, int column)
        {
            if (child is FrameworkElement held)
            {
                held.Margin = new Thickness(0, 0, 6, 0);

                // Every cell centred on the row's own line. A box, an editable combo and a path
                // with a Browse beside it are three different heights, and left to themselves
                // they stretch to the tallest and sit at the top of it.
                held.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        /// <summary>
        /// One person, as a row of editable fields.
        ///
        /// <b>Suggested, never insisted on.</b> The role list starts with the roles this roster
        /// already uses and then the ones a job usually has; team and company are whatever the
        /// roster already holds. All three are editable, so a job with its own words for things
        /// types them once and finds them in the list from then on. That is the whole difference
        /// between a picker that helps and one that argues.
        /// </summary>
        /// <param name="roster">The team, for the lists the boxes offer.</param>
        /// <param name="member">The person.</param>
        /// <param name="onShape">Called when the list itself has to be drawn again.</param>
        /// <param name="onEdit">Called after every edit, to keep it.</param>
        private static UIElement Line(TeamRoster roster, TeamMember member,
                                      Action onShape, Action onEdit)
        {
            var grid = Columns();

            grid.Margin = new Thickness(0, 2, 0, 2);

            // The name is the identity, so changing it is a remove and an add rather than an
            // edit in place - and the list is drawn again because the order may have moved.
            var name = Ui.Text(member.Name, null, 74);

            name.LostFocus += (s, e) => Ui.Safely(() =>
            {
                var typed = name.Text.Trim();

                if (typed.Length == 0 || string.Equals(typed, member.Name, StringComparison.Ordinal))
                {
                    name.Text = member.Name;
                    return;
                }

                var moved = roster.Add(typed, member.Party, member.Email);

                moved.Role = member.Role;
                moved.Team = member.Team;
                moved.Folder = member.Folder;
                moved.Note = member.Note;

                roster.Remove(member.Id);

                onEdit();
                onShape();
            });

            Put(grid, name, 0);

            Put(grid, Suggesting(roster.RolesOffered, member.Role,
                                 typed => { member.Role = Or(typed); onEdit(); }), 1);

            Put(grid, Suggesting(roster.Teams, member.Team,
                                 typed => { member.Team = Or(typed); onEdit(); }), 2);

            Put(grid, Suggesting(roster.Parties, member.Party,
                                 typed => { member.Party = Or(typed); onEdit(); }), 3);

            var email = Ui.Text(member.Email ?? string.Empty, "nobody@", null, 74);

            email.LostFocus += (s, e) => Ui.Safely(() =>
            {
                member.Email = Or(email.Text);
                onEdit();
            });

            Put(grid, email, 4);

            // A folder, with a Browse, because a job that issues clashes by folder rather than
            // by email has nowhere else to put it - and typing a share path by hand is how the
            // wrong consultant gets somebody else's clashes.
            var folder = Ui.Text(member.Folder ?? string.Empty, "where they go", null, 74);

            folder.LostFocus += (s, e) => Ui.Safely(() =>
            {
                member.Folder = Or(folder.Text);
                onEdit();
            });

            // The row, not the field: a field puts a label over the box and a gap under it,
            // which in a table is a cell taller than the ones beside it holding a box that sits
            // lower than all of them.
            Put(grid, Ui.PathRow(folder, PathKind.Folder), 5);

            var off = Ui.Icon(Glyphs.Bin, "Take them off the team", () =>
            {
                roster.Remove(member.Id);
                onEdit();
                onShape();
            });

            off.Margin = new Thickness(0);

            Grid.SetColumn(off, 6);
            grid.Children.Add(off);

            return grid;
        }

        /// <summary>A box that offers what is already in use, and takes anything else.</summary>
        /// <param name="known">What to offer.</param>
        /// <param name="current">What it holds now.</param>
        /// <param name="onDone">Called with what it holds when the caret leaves it.</param>
        private static ComboBox Suggesting(IReadOnlyList<string> known, string? current,
                                           Action<string> onDone)
        {
            var box = new ComboBox
            {
                IsEditable = true,
                Text = current ?? string.Empty,
                MinWidth = 74,

                // The same height a text box takes, so a row of both reads as one line.
                MinHeight = 31,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            foreach (var one in known) box.Items.Add(one);

            box.LostFocus += (s, e) => Ui.Safely(() => onDone(box.Text));

            box.SelectionChanged += (s, e) => Ui.Safely(() =>
            {
                if (box.SelectedItem is string picked) onDone(picked);
            });

            return box;
        }

        /// <summary>A name nobody on the roster has yet, for a row that was just added.</summary>
        private static string Fresh(TeamRoster roster)
        {
            for (var at = 1; at < 500; at++)
            {
                var tried = at == 1
                    ? "New person"
                    : "New person " + at.ToString(CultureInfo.InvariantCulture);

                if (roster.Find(tried) == null) return tried;
            }

            return "New person";
        }

        private static string? Or(string? text)
        {
            var value = (text ?? string.Empty).Trim();

            return value.Length == 0 ? null : value;
        }
    }
}
