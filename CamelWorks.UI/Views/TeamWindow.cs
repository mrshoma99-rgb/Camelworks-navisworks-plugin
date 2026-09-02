using System;
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
                Width = 620,
                Height = 560,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var list = Ui.Stack();
            var said = Ui.Stack();

            var name = Ui.Text(string.Empty, "who", null, 150);
            var role = Ui.Text(string.Empty, "what they do", null, 140);
            var team = Ui.Text(string.Empty, "which team", null, 90);
            var company = Ui.Text(string.Empty, "which company", null, 140);
            var email = Ui.Text(string.Empty, "where a BCF reaches them", null, 190);

            void Kept()
            {
                changed = true;
                said.Children.Clear();

                var problem = Keep(session, roster);

                if (problem != null) said.Children.Add(Ui.Problem(problem));
            }

            void Fill()
            {
                list.Children.Clear();

                foreach (var member in roster.Members
                             .OrderBy(m => m.Name, StringComparer.CurrentCulture))
                {
                    var here = member;
                    var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 3, 0, 3) };

                    var off = Ui.Icon(Glyphs.Bin, "Take them off the team", () =>
                    {
                        roster.Remove(here.Id);
                        Fill();
                        Kept();
                    });

                    DockPanel.SetDock(off, Dock.Right);
                    row.Children.Add(off);

                    var words = new StackPanel { Orientation = Orientation.Horizontal };

                    words.Children.Add(Ui.Glyph(Glyphs.Person, 13, "Muted", new Thickness(0, 0, 8, 0)));

                    var who = new TextBlock
                    {
                        Text = here.Name,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 150,
                    };

                    words.Children.Add(who.Wearing("Body"));

                    var under = here.Under();

                    words.Children.Add(new TextBlock
                    {
                        Text = under.Length == 0 ? "no role, team or company yet" : under,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(8, 0, 0, 0),
                    }.Wearing("Hint"));

                    row.Children.Add(words);
                    list.Children.Add(row);
                }

                if (roster.Members.Count == 0)
                    list.Children.Add(Ui.Small("Nobody yet. Until there is somebody here, a clash "
                        + "can still be assigned to whatever name is typed on the board — this is "
                        + "what turns those names into people, roles, companies and teams."));
            }

            void Add()
            {
                var who = name.Text.Trim();

                if (who.Length == 0)
                {
                    said.Children.Clear();
                    said.Children.Add(Ui.Problem("Somebody needs a name."));
                    return;
                }

                var member = roster.Add(who, company.Text.Trim(), email.Text.Trim());

                member.Role = Or(role.Text);
                member.Team = Or(team.Text);

                name.Text = string.Empty;
                role.Text = string.Empty;
                team.Text = string.Empty;
                company.Text = string.Empty;
                email.Text = string.Empty;

                Fill();
                Kept();

                name.Focus();
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

            var adding = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };

            foreach (var box in new[] { name, role, team, company, email })
            {
                box.Margin = new Thickness(0, 0, 8, 6);
                adding.Children.Add(box);
            }

            adding.Children.Add(Ui.Primary("Add", Add));

            DockPanel.SetDock(adding, Dock.Top);
            body.Children.Add(adding);

            var foot = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };

            var close = Ui.Primary("Done", () => window.Close());
            close.MinWidth = 92;

            DockPanel.SetDock(close, Dock.Right);
            foot.Children.Add(close);

            // Handing the team to somebody else, and taking theirs. A roster is typed once and
            // then wanted by four people, and the way that happens today is a screenshot.
            var send = Ui.Quiet("Save to a file", () =>
            {
                var path = Ui.AskToSave("Save the team", "Team|*.json", "team.json");

                if (string.IsNullOrWhiteSpace(path)) return;

                said.Children.Clear();

                said.Children.Add(roster.KeepAt(path)
                    ? Ui.Done("Saved. Send them that file.")
                    : Ui.Problem(roster.LastProblem ?? "It could not be written."));
            });

            var take = Ui.Quiet("Open a team file", () =>
            {
                var path = Ui.AskToOpen("Open a team file", "Team|*.json");

                if (string.IsNullOrWhiteSpace(path)) return;

                var read = TeamRoster.ReadFrom(path);

                foreach (var member in read.Members)
                {
                    var added = roster.Add(member.Name, member.Party, member.Email);

                    added.Role = member.Role ?? added.Role;
                    added.Team = member.Team ?? added.Team;
                }

                Fill();
                Kept();
            });

            send.Margin = new Thickness(0, 0, 8, 0);

            var ways = new StackPanel { Orientation = Orientation.Horizontal };

            ways.Children.Add(send);
            ways.Children.Add(take);

            foot.Children.Add(ways);

            DockPanel.SetDock(foot, Dock.Bottom);
            body.Children.Add(foot);

            DockPanel.SetDock(said, Dock.Bottom);
            body.Children.Add(said);

            body.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 6, 0, 0),
            });

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;
            window.Loaded += (s, e) => name.Focus();

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            if (changed) onChanged?.Invoke();
        }

        private static string? Or(string? text)
        {
            var value = (text ?? string.Empty).Trim();

            return value.Length == 0 ? null : value;
        }
    }
}
