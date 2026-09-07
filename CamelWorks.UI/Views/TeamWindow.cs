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
    /// Everybody on the job, in one window, reachable from wherever one of them is needed.
    ///
    /// One roster, two homes. It is kept with the person, so the same architect, fire engineer and
    /// companies are present as soon as a new model is opened rather than retyped per job, and it is
    /// written into the project as well, so somebody opening the job who has not met this roster
    /// still gets the team. Opening it merges what the project knows into what the person already
    /// had, so a coordinator handing over a model hands over the team with it.
    ///
    /// Roles, companies and teams are not maintained lists. They are what the people on the roster
    /// say they are, so there is nothing to keep in step: type "ARC" against two people and ARC
    /// becomes something clashes can be assigned to; remove the last one and it stops being one. A
    /// role nobody holds cannot be assigned to, which needs no rule of its own.
    /// </summary>
    internal static class TeamWindow
    {
        /// <summary>
        /// The roster as it stands: what this person carries, with the project's folded in.
        ///
        /// The person's own copy wins on a name they both hold, because it is the one they have
        /// been correcting. The project only fills in people they have never met.
        /// </summary>
        /// <param name="session">The seat whose project may carry a roster.</param>
        internal static TeamRoster Read(ISeat? session)
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
        /// <param name="session">The seat whose project also keeps a copy.</param>
        /// <param name="roster">The roster.</param>
        internal static string? Keep(ISeat? session, TeamRoster roster)
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
        /// <param name="session">The seat, for the project's copy.</param>
        /// <param name="onChanged">Called once, when something was changed.</param>
        internal static void Show(FrameworkElement near, ISeat? session, Action? onChanged)
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

            var ticked = new HashSet<string>(StringComparer.Ordinal);
            Picking? picking = null;

            void Fill()
            {
                rows.Children.Clear();

                // Ticks on people who are no longer on the roster count towards nothing and would
                // make "everybody is picked" impossible to reach.
                ticked.IntersectWith(roster.Members.Select(one => one.Id));

                foreach (var member in roster.Members
                             .OrderBy(m => m.Name, StringComparer.CurrentCulture)
                             .ToList())
                    rows.Children.Add(Line(roster, member, Fill, Kept, ticked,
                                           () => picking?.Refresh()));

                if (roster.Members.Count == 0)
                    rows.Children.Add(Ui.Small("No members yet. Press “Add member” and complete "
                        + "the row. Every field is editable in place, and the roles, teams and "
                        + "companies used appear in the lists afterwards."));

                picking?.Refresh();
            }

            Fill();

            var body = new DockPanel { Margin = new Thickness(18, 16, 18, 16), LastChildFill = true };

            var head = Ui.Stack(
                Ui.Heading("The team"),
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


            Put(headings, Ui.Small("Name"), 1);
            Put(headings, Ui.Small("Role"), 2);
            Put(headings, Ui.Small("Team"), 3);
            Put(headings, Ui.Small("Company"), 4);
            Put(headings, Ui.Small("Email"), 5);
            Put(headings, Ui.Small("BCF folder"), 6);

            DockPanel.SetDock(headings, Dock.Top);
            body.Children.Add(headings);

            picking = new Picking(roster, ticked, headings, Fill, Kept, said);

            DockPanel.SetDock(picking.Bar, Dock.Bottom);
            body.Children.Add(picking.Bar);

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

            // Quiet, not filled. Done closes the window; the writes have already happened.
            var close = Ui.Quiet("Done", () => window.Close());
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
        /// The roster as a panel, for a tab that hosts it rather than a window that opens it.
        /// </summary>
        /// <param name="roster">The team, edited in place.</param>
        /// <param name="onEdit">Called after every edit, so the tab can save it.</param>
        internal static UIElement Panel(TeamRoster roster, Action onEdit)
        {
            var held = Ui.Stack();
            var rows = Ui.Stack();

            var ticked = new HashSet<string>(StringComparer.Ordinal);
            Picking? picking = null;

            void Fill()
            {
                rows.Children.Clear();

                ticked.IntersectWith(roster.Members.Select(one => one.Id));

                foreach (var member in roster.Members
                             .OrderBy(m => m.Name, StringComparer.CurrentCulture)
                             .ToList())
                    rows.Children.Add(Line(roster, member, Fill, onEdit, ticked,
                                           () => picking?.Refresh()));

                if (roster.Members.Count == 0)
                    rows.Children.Add(Ui.Empty(
                        "No members yet",
                        "Until the roster has members, an assignment is whatever name was typed "
                        + "on the board.",
                        "Add the first member", () => Ui.Safely(() =>
                        {
                            roster.Add(Fresh(roster));
                            onEdit();
                            Fill();
                        })));
            }

            var headings = Columns();

            headings.Margin = new Thickness(0, 0, 0, 4);

            Put(headings, Ui.Small("Name"), 1);
            Put(headings, Ui.Small("Role"), 2);
            Put(headings, Ui.Small("Team"), 3);
            Put(headings, Ui.Small("Company"), 4);
            Put(headings, Ui.Small("Email"), 5);
            Put(headings, Ui.Small("BCF folder"), 6);

            var said = Ui.Stack();

            picking = new Picking(roster, ticked, headings, Fill, onEdit, said);

            Fill();

            var adding = Ui.Button("Add member", () => Ui.Safely(() =>
            {
                roster.Add(Fresh(roster));
                onEdit();
                Fill();
            }));

            adding.HorizontalAlignment = HorizontalAlignment.Left;
            adding.Margin = new Thickness(0, 10, 0, 0);

            held.Children.Add(headings);
            held.Children.Add(rows);
            held.Children.Add(picking.Bar);
            held.Children.Add(said);
            held.Children.Add(adding);

            return held;
        }

        /// <summary>
        /// The one column layout the headings and every row share.
        ///
        /// Shares rather than fixed widths. Six columns at their previous fixed widths exceeded the
        /// window and the last of them — the folder and its Browse — was cut off at the right edge
        /// with no scrollbar. Shares squeeze every column a little in a narrow window instead of
        /// losing one entirely, and spend extra room on the fields that hold long text.
        /// </summary>
        private static Grid Columns()
        {
            var grid = new Grid();

            // The tick, hard left. Fixed rather than Auto for the same reason as the bin: every
            // row is a grid of its own, and an Auto column would be as wide as that row's own
            // tick while the headings, which carry one too, sized independently.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            // Name, Role, Team, Company, Email, BCF folder. Weighted by what each holds: an
            // address is the longest value on the row and a team is the shortest, and at even
            // shares five of the six columns elided their contents.
            foreach (var share in new[] { 1.2, 1.0, 0.8, 1.1, 1.7, 1.6 })
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
        /// The lists are suggestions. The role list starts with the roles this roster already uses
        /// and then the ones a job usually has; team and company are whatever the roster holds. All
        /// three are editable, so a job with its own vocabulary types it once and finds it in the
        /// list afterwards.
        /// </summary>
        /// <param name="roster">The team, for the lists the boxes offer.</param>
        /// <param name="member">The person.</param>
        /// <param name="ticked">Who is picked, by member id. The row's tick reads and writes it.</param>
        /// <param name="onPick">Called when the tick changes, so the bar redraws its count.</param>
        /// <param name="onShape">Called when the list itself has to be drawn again.</param>
        /// <param name="onEdit">Called after every edit, to keep it.</param>
        private static UIElement Line(TeamRoster roster, TeamMember member,
                                      Action onShape, Action onEdit,
                                      ISet<string> ticked, Action onPick)
        {
            var grid = Columns();

            grid.Margin = new Thickness(0, 2, 0, 2);

            var tick = new CheckBox
            {
                IsChecked = ticked.Contains(member.Id),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Pick " + member.Name,
            };

            tick.Checked += (s, e) => Ui.Safely(() => { ticked.Add(member.Id); onPick(); });
            tick.Unchecked += (s, e) => Ui.Safely(() => { ticked.Remove(member.Id); onPick(); });

            Grid.SetColumn(tick, 0);
            grid.Children.Add(tick);

            // The name is the identity, so changing it is a remove and an add rather than an
            // edit in place - and the list is drawn again because the order may have moved.
            var name = Ui.Text(member.Name, null, 0);

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

                // Carried with the rest of them. TeamRoster.Add treats an id collision as a
                // return to the job and sets this true, so correcting a typo in an off-the-job
                // person's name put them back into every assignment list on the board.
                moved.IsActive = member.IsActive;

                roster.Remove(member.Id);

                onEdit();
                onShape();
            });

            Put(grid, name, 1);

            Put(grid, Suggesting(roster.RolesOffered, member.Role,
                                 typed => { member.Role = Or(typed); onEdit(); }), 2);

            Put(grid, Suggesting(roster.Teams, member.Team,
                                 typed => { member.Team = Or(typed); onEdit(); }), 3);

            Put(grid, Suggesting(roster.Parties, member.Party,
                                 typed => { member.Party = Or(typed); onEdit(); }), 4);

            var email = Ui.Text(member.Email ?? string.Empty, "nobody@", null, 0);

            email.LostFocus += (s, e) => Ui.Safely(() =>
            {
                member.Email = Or(email.Text);
                onEdit();
            });

            Put(grid, email, 5);

            // A folder, with a Browse, because a job that issues clashes by folder rather than
            // by email has nowhere else to put it - and typing a share path by hand is how the
            // wrong consultant gets somebody else's clashes.
            var folder = Ui.Text(member.Folder ?? string.Empty, "where they go", null, 0);

            folder.LostFocus += (s, e) => Ui.Safely(() =>
            {
                member.Folder = Or(folder.Text);
                onEdit();
            });

            // The row, not the field: a field puts a label over the box and a gap under it,
            // which in a table is a cell taller than the ones beside it holding a box that sits
            // lower than all of them.
            Put(grid, Ui.PathRow(folder, PathKind.Folder), 6);

            // Off the job, said on the row. The flag decides who the board offers when a clash is
            // assigned, so a roster that does not show it is a list where two of the rows behave
            // differently from the rest for no visible reason.
            if (!member.IsActive)
            {
                grid.Opacity = 0.6;
                grid.ToolTip = member.Name + " is off the job. They keep the clashes already "
                             + "assigned to them and are not offered for new ones.";
            }

            var off = Ui.Icon(Glyphs.Bin, "Remove from the roster", () =>
            {
                ticked.Remove(member.Id);
                roster.Remove(member.Id);
                onEdit();
                onShape();
            });

            off.Margin = new Thickness(0);

            Grid.SetColumn(off, 7);
            grid.Children.Add(off);

            return grid;
        }


        /// <summary>
        /// The tick over the tick column, and the bar under the rows.
        ///
        /// The roster was a stack of editable rows with a bin on each one, so taking eleven people
        /// off a team was eleven presses. Tick what is going and press once; the count is in the
        /// caption, so what the press covers is the last thing read before it.
        /// </summary>
        private sealed class Picking
        {
            private readonly TeamRoster _roster;
            private readonly ISet<string> _ticked;
            private readonly CheckBox _every = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Pick every member, or none",
            };

            private readonly TextBlock _count = Ui.Small(string.Empty);
            private readonly Button _off;
            private readonly Button _job;
            private readonly Action _onShape;
            private bool _filling;

            /// <summary>Build one.</summary>
            /// <param name="roster">The team.</param>
            /// <param name="ticked">Who is picked, by member id. Shared with the rows.</param>
            /// <param name="headings">The heading row, which gets the all-or-none tick.</param>
            /// <param name="onShape">Redraws the rows.</param>
            /// <param name="onEdit">Called after a change, to keep it.</param>
            /// <param name="said">Where the bar asks before it removes anybody.</param>
            internal Picking(TeamRoster roster, ISet<string> ticked, Grid headings,
                             Action onShape, Action onEdit, Panel said)
            {
                _roster = roster;
                _ticked = ticked;
                _onShape = onShape;

                _count.VerticalAlignment = VerticalAlignment.Center;
                _count.Margin = new Thickness(0, 0, 10, 0);

                _off = Ui.Danger("Remove from the roster", () => { });

                // The non-destructive half of the same job. IsActive decides who appears in every
                // assignment list on the board, and nothing in the editor could see or set it: a
                // person who has left the job could only be deleted, which loses the clashes
                // already assigned to them.
                _job = Ui.Quiet("Take off the job", () => { });
                _job.Margin = new Thickness(0, 0, 8, 0);

                _job.Click += (s, e) => Ui.Safely(() =>
                {
                    var these = _roster.Members.Where(one => _ticked.Contains(one.Id)).ToList();

                    if (these.Count == 0) return;

                    // Back on the job only when every one of them is off it, so a mixed selection
                    // has one unambiguous outcome.
                    var back = these.All(one => !one.IsActive);

                    foreach (var one in these) one.IsActive = back;

                    onEdit();
                    onShape();
                });

                Grid.SetColumn(_every, 0);
                headings.Children.Add(Ui.Loose(_every));

                _every.Checked += (s, e) => Ui.Safely(() => All(true));
                _every.Unchecked += (s, e) => Ui.Safely(() => All(false));

                _off.Click += (s, e) => Ui.Safely(() =>
                {
                    var going = _roster.Members.Where(one => _ticked.Contains(one.Id)).ToList();

                    if (going.Count == 0) return;

                    Parts.Sure(said, "Remove " + Ui.Count(going.Count, "person")
                               + " from the roster?", "Remove", () =>
                    {
                        foreach (var one in going) _roster.Remove(one.Id);

                        _ticked.Clear();
                        onEdit();
                        onShape();
                    });
                });

                Bar = Ui.Across(_count, _job, _off);
                Bar.Margin = new Thickness(0, 6, 0, 0);

                Refresh();
            }

            /// <summary>The bar itself, for the caller to place.</summary>
            internal Panel Bar { get; }

            /// <summary>Say again how many are picked. Called whenever a row's tick moves.</summary>
            internal void Refresh()
            {
                var picked = _roster.Members.Count(one => _ticked.Contains(one.Id));

                _count.Text = picked == 0
                    ? "Tick a row to act on it"
                    : Ui.Count(picked, "person") + " selected";

                _off.IsEnabled = picked > 0;
                _off.Content = picked > 1
                    ? "Remove these " + picked.ToString(CultureInfo.CurrentCulture)
                      + " from the roster"
                    : "Remove from the roster";

                var these = _roster.Members.Where(one => _ticked.Contains(one.Id)).ToList();

                _job.IsEnabled = picked > 0;
                _job.Content = these.Count > 0 && these.All(one => !one.IsActive)
                    ? "Put back on the job"
                    : "Take off the job";

                _job.ToolTip = "A member off the job keeps the clashes already assigned to them "
                             + "and is not offered when new ones are assigned.";

                // Set, not raised: the box writes back through its own handlers, and a refresh
                // that fired them would tick or untick the roster as a side effect of counting it.
                _filling = true;
                _every.IsChecked = _roster.Members.Count > 0 && picked == _roster.Members.Count;
                _filling = false;
            }

            private void All(bool on)
            {
                if (_filling) return;

                _ticked.Clear();

                if (on) foreach (var one in _roster.Members) _ticked.Add(one.Id);

                _onShape();
            }
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

                // None of its own. Every cell on this row sits in a shared grid whose columns
                // carry the minimum; a cell that sets a second one stops the column shrinking.
                MinWidth = 0,

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
