using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The issue register, and the four doors into it.
    ///
    /// <b>Why it is its own app.</b> Issues used to be a thing the clash board grew: you could
    /// raise one, but only beside a clash, only while the board was loaded, and only about clashes.
    /// Everything else a coordinator has to track — what the clearance check found, what a
    /// consultant sent back as BCF, what somebody saw on a site walk — lived in a spreadsheet
    /// beside the tool. So the register is now the product, and the board is gone: an issue is a
    /// first-class record with its own list, its own detail and its own file formats, and clash
    /// results and clearance findings are two of the ways one gets raised.
    ///
    /// <b>The shape is the one everybody already knows</b> — the issue list of a common-data
    /// environment: a filter rail, a sortable register, and a detail panel that opens on the row
    /// you picked and edits in place.
    /// </summary>
    public static class IssueViews
    {
        private const string AnyOne = "Anyone";
        private const string AnyType = "Any type";
        private const string AnySource = "Any source";

        /// <summary>Which issue the detail panel is open on, across tab switches.</summary>
        private static string? _open;

        /// <summary>The register list.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Register(Session session) => new Screen(session).Build();

        /// <summary>The import surface.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Import(Session session) => new ImportScreen(session).Build();

        /// <summary>The export surface.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Export(Session session) => new ExportScreen(session).Build();

        // -----------------------------------------------------------------------------------
        // The register, as everything else reaches it
        // -----------------------------------------------------------------------------------

        /// <summary>This project's issues.</summary>
        /// <param name="session">The open document.</param>
        internal static IssueRegister Read(Session session) =>
            IssueRegister.FromJson(session.Store.Section(ProjectStore.IssuesSection)["register"]);

        /// <summary>Write the register back, and say what stopped it when it would not go.</summary>
        /// <param name="session">The open document.</param>
        /// <param name="register">The register to keep.</param>
        internal static string? Write(Session session, IssueRegister register)
        {
            session.Store.Section(ProjectStore.IssuesSection).Set("register", register.ToJson());

            return session.Store.Save()
                ? null
                : session.Store.LastSaveProblem ?? "Could not write the project file.";
        }

        /// <summary>
        /// Raise issues from somewhere else in the product, and say how it went.
        ///
        /// The one entry point for "make an issue out of this": the clash grid's right-click, the
        /// clearance grid's, and anything added later. Numbering, stamping and saving live here so
        /// that no caller has to know how a register is kept.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="make">Called with the register and the id to use, once per issue.</param>
        /// <param name="count">How many issues to make.</param>
        internal static (int Made, string? Problem) Raise(Session session, Func<string, Issue?> make,
                                                          int count)
        {
            var register = Read(session);
            var now = DateTime.UtcNow.Ticks;
            var made = 0;

            for (var n = 0; n < count; n++)
            {
                var issue = make(register.NextId());
                if (issue == null) continue;

                issue.Author = issue.Author ?? Environment.UserName ?? "coordinator";
                if (issue.CreatedTicks == 0) issue.CreatedTicks = now;
                issue.ModifiedTicks = now;

                register.Put(issue);
                made++;
            }

            if (made == 0) return (0, null);

            var problem = Write(session, register);

            if (problem == null)
                session.Record(ActivityKind.Regroup, "raised " + Ui.Count(made, "issue"));

            return (made, problem);
        }

        /// <summary>Open the register on one issue, from anywhere.</summary>
        /// <param name="id">The issue id, or null to open on nothing.</param>
        internal static void Open(string? id)
        {
            _open = id;
            Ui.Told(Shell.AppWindow.Show("issuemgr", "issuelist"));
        }

        /// <summary>How an issue's origin reads in a column.</summary>
        internal static string Origin(IssueOrigin origin)
        {
            switch (origin)
            {
                case IssueOrigin.Clash: return "Clash";
                case IssueOrigin.Clearance: return "Clearance";
                case IssueOrigin.Imported: return "Imported";
                case IssueOrigin.Compare: return "Compare";
                case IssueOrigin.Health: return "Health";
                case IssueOrigin.Ids: return "IDS";
                default: return "Raised";
            }
        }

        /// <summary>A date, as a person reads one, or a dash.</summary>
        internal static string Day(long ticks) =>
            ticks <= 0
                ? "—"
                : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime()
                    .ToString("d MMM yyyy", CultureInfo.CurrentCulture);

        /// <summary>Read a typed date back, or zero when it is not one.</summary>
        internal static long ReadDay(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var day)
                ? DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime().Ticks
                : 0;
        }

        // ===================================================================================
        // The register screen
        // ===================================================================================

        private sealed class Screen
        {
            private readonly Session _session;
            private readonly IssueRegister _register;

            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _detail = Ui.Stack();
            private readonly StackPanel _strip = Ui.Stack();

            private readonly HashSet<string> _show;
            private readonly HashSet<string> _pickedIds = new HashSet<string>(StringComparer.Ordinal);

            private readonly TextBox _find;

            private string _who = AnyOne;
            private string _type = AnyType;
            private string _source = AnySource;
            private bool _onlyOverdue;
            private bool _onlyMine;

            private string _mark = Issue.ClosedStatus;

            internal Screen(Session session)
            {
                _session = session;
                _register = Read(session);

                // Everything but the ones somebody has already finished with: the register opens
                // on the work, not on the archive.
                _show = new HashSet<string>(Issue.Statuses.Where(s => s != Issue.ClosedStatus),
                                            StringComparer.OrdinalIgnoreCase);

                _find = Ui.Text(string.Empty, null, 150);
                _find.TextChanged += (s, e) => Redraw();
            }

            private long Now => DateTime.UtcNow.Ticks;

            internal UIElement Build()
            {
                var stage = Ui.Stack(_strip, _log, _list, _detail);

                if (_register.Unreadable > 0)
                    _log.Children.Add(Ui.Note(Ui.Count(_register.Unreadable, "saved issue")
                        + " could not be read back and " + (_register.Unreadable == 1 ? "was" : "were")
                        + " left out of the list. The project file has them; this build cannot "
                        + "read that record."));

                RedrawStrip();
                Redraw();

                return Bench.Surface(null, Foot(), stage);
            }

            /// <summary>
            /// The register's own filters, above the list.
            ///
            /// All of them, on the surface. Status, mine and overdue were here; who it is assigned
            /// to, what type it is and what raised it were behind a symbol that opened a window,
            /// and the window was the same one a second symbol opened - so neither symbol said
            /// what it had done. A filter is read at a glance and changed mid-thought; it does not
            /// survive being a press away.
            /// </summary>
            private void RedrawStrip()
            {
                _strip.Children.Clear();

                var row = new WrapPanel();

                row.Children.Add(Ui.Icon(Glyphs.Find, "Search the register", () => _find.Focus()));

                // Loose first: the box is kept in a field so what was typed in it survives a
                // redraw, and a kept control still belongs to the row that was thrown away.
                row.Children.Add(Ui.Loose(_find));
                row.Children.Add(new Border { Width = 14 });

                foreach (var status in Issue.Statuses)
                {
                    var here = status;

                    row.Children.Add(new Border { Width = 8 });
                    row.Children.Add(Ui.Check(here, _show.Contains(here), on =>
                    {
                        if (on) _show.Add(here); else _show.Remove(here);
                        Redraw();
                    }));
                }

                row.Children.Add(new Border { Width = 14 });
                row.Children.Add(Ui.Check("Only mine", _onlyMine, on => { _onlyMine = on; Redraw(); }));
                row.Children.Add(new Border { Width = 8 });
                row.Children.Add(Ui.Check("Only overdue", _onlyOverdue, on => { _onlyOverdue = on; Redraw(); }));

                _strip.Children.Add(row);
                _strip.Children.Add(NarrowRow());

                _strip.Children.Add(Ui.Across(
                    Ui.Figure(Skin.Num(_register.Open), "open"),
                    Ui.Figure(Skin.Num(_register.Overdue(Now)), "overdue",
                              _register.Overdue(Now) > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(Skin.Num(_register.Of(Issue.ClosedStatus)), "closed", Tone.Good)));
            }

            /// <summary>The action row: raise one, then the windows the rest lives in.</summary>
            private UIElement Foot()
            {
                var row = new WrapPanel();

                row.Children.Add(Ui.Primary("New issue", NewIssue));
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Icon(Glyphs.Read, "Import BCF",
                                         () => Ui.Told(Shell.AppWindow.Show("issuemgr", "issuein"))));
                row.Children.Add(Ui.Icon(Glyphs.Onward, "Export BCF",
                                         () => Ui.Told(Shell.AppWindow.Show("issuemgr", "issueout"))));
                row.Children.Add(Ui.Icon(Glyphs.Report, "Write the register to a spreadsheet", Sheet));

                return row;
            }

            /// <summary>
            /// Who it is assigned to, what type it is and what raised it - the three narrowings
            /// that used to live in a window.
            /// </summary>
            private UIElement NarrowRow()
            {
                var people = new List<string> { AnyOne };
                people.AddRange(Assignees());

                var types = new List<string> { AnyType };
                types.AddRange(_register.Issues.Select(i => i.Type)
                                       .Concat(Issue.Types)
                                       .Where(t => !string.IsNullOrWhiteSpace(t))
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

                if (!people.Contains(_who)) _who = AnyOne;
                if (!types.Contains(_type)) _type = AnyType;

                var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

                row.Children.Add(Ui.Tally("Assigned to"));
                row.Children.Add(Ui.Choice(people, _who, pick => { _who = pick; Redraw(); }, 150));
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Tally("Type"));
                row.Children.Add(Ui.Choice(types, _type, pick => { _type = pick; Redraw(); }, 150));
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Tally("Raised by"));
                row.Children.Add(Ui.Choice(
                    new[] { AnySource, "Raised", "Clash", "Clearance", "Imported" },
                    _source, pick => { _source = pick; Redraw(); }, 150));

                return row;
            }

            private List<string> Assignees()
            {
                var names = new List<string>();

                foreach (var party in _session.Profile.Parties)
                    if (!names.Contains(party, StringComparer.OrdinalIgnoreCase)) names.Add(party);

                foreach (var issue in _register.Issues)
                    if (!string.IsNullOrWhiteSpace(issue.AssignedTo)
                        && !names.Contains(issue.AssignedTo!, StringComparer.OrdinalIgnoreCase))
                        names.Add(issue.AssignedTo!);

                return names;
            }

            // -------------------------------------------------------------------------------
            // The list
            // -------------------------------------------------------------------------------

            private List<Issue> Filtered()
            {
                var wanted = _find.Text.Trim();
                var me = Environment.UserName ?? string.Empty;

                bool Has(string? value) =>
                    value != null && value.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

                return _register.Issues
                    .Where(i => _show.Contains(i.Status))
                    .Where(i => !_onlyOverdue || i.IsOverdue(Now))
                    .Where(i => !_onlyMine
                                || string.Equals(i.AssignedTo, me, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(i.Author, me, StringComparison.OrdinalIgnoreCase))
                    .Where(i => _who == AnyOne
                                || string.Equals(i.AssignedTo, _who, StringComparison.OrdinalIgnoreCase))
                    .Where(i => _type == AnyType
                                || string.Equals(i.Type, _type, StringComparison.OrdinalIgnoreCase))
                    .Where(i => _source == AnySource
                                || string.Equals(Origin(i.Origin), _source, StringComparison.OrdinalIgnoreCase))
                    .Where(i => wanted.Length == 0
                                || Has(i.Title) || Has(i.Description) || Has(i.Id)
                                || Has(i.AssignedTo) || Has(i.Author) || Has(i.Level))
                    .ToList();
            }

            private void Redraw()
            {
                _list.Children.Clear();

                var rows = Filtered();

                _list.Children.Add(Ui.Eyebrow(Ui.Count(rows.Count, "issue") + " of "
                    + Ui.Count(_register.Issues.Count, "on this project")));

                if (_register.Issues.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("No issues yet.",
                        "Press \"New issue\" to raise one, right-click results in the Clash "
                        + "Manager or the Clearance Check to make issues out of them, or import a "
                        + "BCF file."));

                    RedrawDetail();
                    return;
                }

                if (rows.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("Nothing matches.",
                        "Every issue is filtered out. Tick another status on the left, or clear "
                        + "the search."));

                    RedrawDetail();
                    return;
                }

                var table = Ui.Table(rows.Select(Line).ToList(),
                    ("Number", 80), ("Title", 260), ("Status", 90), ("Priority", 75),
                    ("Assigned", 110), ("Due", 100), ("Type", 100), ("Raised by", 85),
                    ("Comments", 80), ("Level", 0));

                table.SelectionChanged += (s, e) =>
                {
                    _pickedIds.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is Issue issue) _pickedIds.Add(issue.Id);

                    // The detail panel follows the last row touched, the way a register reads:
                    // pick one to work it, pick many to act on them all at once.
                    if (table.Picked() is Issue one) _open = one.Id;

                    RedrawDetail();
                };

                var setTo = Ui.Across(
                    Ui.Tally("Set to"),
                    Ui.Choice(Issue.Statuses, _mark, pick => { _mark = pick; Redraw(); }, 120));

                _list.Children.Add(Ui.Picking(table, setTo,
                    new Ui.OnPicked("Show {n}", ShowPicked, primary: true),
                    new Ui.OnPicked("Set {n} " + _mark, MarkPicked),
                    new Ui.OnPicked("Assign {n}", AssignPicked),
                    new Ui.OnPicked("Comment {n}", CommentPicked),
                    new Ui.OnPicked("Delete {n}", DeletePicked, danger: true),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "issue"))));

                RedrawDetail();
            }

            private TableRow Line(Issue issue) =>
                new TableRow(issue,
                    issue.Id,
                    issue.Title,
                    issue.Status,
                    issue.Priority,
                    string.IsNullOrWhiteSpace(issue.AssignedTo) ? "—" : issue.AssignedTo,
                    Day(issue.DueTicks),
                    issue.Type,
                    Origin(issue.Origin),
                    issue.Comments.Count == 0
                        ? string.Empty
                        : Skin.Num(issue.Comments.Count),
                    issue.Level ?? string.Empty)
                {
                    Tone = issue.IsOverdue(Now) ? Tone.Bad
                         : !issue.IsOpen ? Tone.Good
                         : string.Equals(issue.Priority, "Critical", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(issue.Priority, "High", StringComparison.OrdinalIgnoreCase)
                             ? Tone.Warn
                             : Tone.Plain,
                };

            // -------------------------------------------------------------------------------
            // The detail panel
            // -------------------------------------------------------------------------------

            private void RedrawDetail()
            {
                _detail.Children.Clear();

                var issue = _register.Find(_open);
                if (issue == null) return;

                _detail.Children.Add(Ui.Rule());
                _detail.Children.Add(Ui.Heading(issue.Id + " · " + issue.Title));

                var says = Ui.Stack();

                var title = Ui.Text(issue.Title, null, 320);
                var description = Ui.Text(issue.Description ?? string.Empty, null, 320);
                description.AcceptsReturn = true;
                description.TextWrapping = TextWrapping.Wrap;
                description.MinHeight = 60;

                var status = Ui.Choice(Statuses(issue), issue.Status, null, 150);
                var type = Ui.Choice(Types(issue), issue.Type, null, 150);
                var priority = Ui.Choice(Issue.Priorities, issue.Priority, null, 130);
                var cause = Ui.Choice(Causes(issue), issue.RootCause ?? string.Empty, null, 150);
                var who = Suggesting(Assignees(), issue.AssignedTo);
                var due = Ui.Text(issue.DueTicks > 0 ? Day(issue.DueTicks) : string.Empty, null, 130);
                var level = Ui.Text(issue.Level ?? string.Empty, null, 150);
                var zone = Ui.Text(issue.Zone ?? string.Empty, null, 150);

                var facts = Ui.Stack(
                    Ui.Field("Title", title),
                    Ui.Field("Description", description),
                    Ui.Pair(Ui.Field("Status", status), Ui.Field("Type", type)),
                    Ui.Pair(Ui.Field("Priority", priority), Ui.Field("Assigned to", who)),
                    Ui.Pair(Ui.Field("Due", due, "a date, or blank"),
                            Ui.Field("Root cause", cause)),
                    Ui.Pair(Ui.Field("Level", level), Ui.Field("Zone", zone)),
                    Ui.Actions(
                        Ui.Primary("Save", () =>
                        {
                            var was = issue.Status;

                            issue.Title = title.Text.Trim().Length == 0 ? issue.Title : title.Text.Trim();
                            issue.Description = description.Text.Trim();
                            issue.Status = status.Text;
                            issue.Type = type.Text;
                            issue.Priority = priority.Text;
                            issue.AssignedTo = (who.Text ?? string.Empty).Trim();
                            issue.RootCause = cause.Text;
                            issue.Level = level.Text.Trim();
                            issue.Zone = zone.Text.Trim();
                            issue.DueTicks = ReadDay(due.Text);
                            issue.ModifiedTicks = Now;

                            var problem = Write(_session, _register);

                            says.Children.Clear();
                            says.Children.Add(problem == null
                                ? Ui.Done("Saved.")
                                : Ui.Problem(problem));

                            if (problem != null) return;

                            // Asked, not done. Closing an issue usually means its clashes are
                            // dealt with, and usually is not always: one closed because the RFI
                            // was answered says nothing about whether the duct moved. An
                            // automatic rule would mark eighteen thousand findings resolved off
                            // one dropdown and turn the board green over a building that had not
                            // changed. So the offer is made, with the count, and somebody says.
                            if (!string.Equals(was, issue.Status, StringComparison.OrdinalIgnoreCase))
                                Offer(issue, says);

                            Redraw();
                        }),
                        Ui.Quiet("Show", () =>
                        {
                            says.Children.Clear();
                            says.Children.Add(Show(issue));
                        })),
                    says);

                _detail.Children.Add(Ui.Card("The issue", Where(issue), facts));
                _detail.Children.Add(Attached(issue));

                // The conversation, oldest first, then the box to add to it. A trail that reads
                // top-down is the one a person catches up on without scrolling backwards.
                var trail = Ui.Stack();

                foreach (var comment in issue.Comments)
                    trail.Children.Add(Ui.Stack(
                        Ui.Small(comment.Author + " · " + Day(comment.WhenTicks)),
                        Ui.Line(comment.Body)));

                if (issue.Comments.Count == 0)
                    trail.Children.Add(Ui.Small("Nothing said yet."));

                var body = Ui.Text(string.Empty, null, 320);
                body.AcceptsReturn = true;
                body.TextWrapping = TextWrapping.Wrap;
                body.MinHeight = 50;

                var said = Ui.Stack();

                _detail.Children.Add(Ui.Card("Comments", null,
                    trail,
                    Ui.Field("Say something", body),
                    Ui.Actions(Ui.Quiet("Comment", () =>
                    {
                        var text = body.Text.Trim();

                        said.Children.Clear();

                        if (text.Length == 0)
                        {
                            said.Children.Add(Ui.Problem("Type something first."));
                            return;
                        }

                        issue.Say(Environment.UserName ?? "coordinator", text, Now);

                        var problem = Write(_session, _register);

                        if (problem != null)
                        {
                            said.Children.Add(Ui.Problem(problem));
                            return;
                        }

                        body.Text = string.Empty;
                        Redraw();
                    })),
                    said));
            }

            /// <summary>
            /// Offer the cascade the new status implies, if it implies one.
            ///
            /// Nothing happens unless somebody presses it. See the remarks on
            /// <see cref="IssueFindings"/> for why this is an offer rather than an invariant.
            /// </summary>
            private void Offer(Issue issue, Panel says)
            {
                var cascade = IssueFindings.Proposed(issue, _session.Findings.Findings);

                if (cascade.IsEmpty) return;

                says.Children.Add(Ui.Info(cascade.Ask(),
                    "Mark " + (cascade.Findings.Count == 1 ? "it" : "them") + " too",
                    () =>
                    {
                        var changed = cascade.Apply();

                        says.Children.Clear();

                        if (!_session.Findings.Save())
                        {
                            says.Children.Add(Ui.Problem(
                                "They are marked here but could not be kept: "
                                + (_session.Findings.LastSaveProblem
                                   ?? "there was nowhere to write.")));

                            return;
                        }

                        _session.Record(ActivityKind.Regroup,
                            "marked " + Ui.Count(changed, "finding") + " "
                            + FindingStatusLattice.ToName(cascade.To) + " with " + issue.Id);

                        says.Children.Add(Ui.Done(Ui.Count(changed, "finding") + " marked "
                            + FindingStatusLattice.ToName(cascade.To) + "."));
                    }));
            }

            /// <summary>
            /// The findings this issue is about, with the way to take one off.
            ///
            /// Shown as a card rather than a line, because "which clashes is this issue actually
            /// about" is the question somebody opens an issue to answer.
            /// </summary>
            private UIElement Attached(Issue issue)
            {
                var findings = IssueFindings.Attached(issue, _session.Findings.Findings);

                if (findings.Count == 0)
                {
                    var said = issue.Clashes.Count > 0
                        ? Ui.Small(Ui.Count(issue.Clashes.Count, "clash result")
                            + " raised before findings existed. Read the clash results in the "
                            + "Clash Manager and they will join this issue by name.")
                        : Ui.Small("Nothing attached. Pick results on any findings grid and "
                                   + "\"Attach to an issue\".");

                    return Ui.Card("Findings", null, said);
                }

                var table = Ui.Table(
                    findings.Select(f => new TableRow(f, f.Rule, f.Title,
                                                      FindingStatusLattice.ToName(f.Status),
                                                      f.IsPresent ? string.Empty : "gone",
                                                      f.Detail ?? string.Empty)),
                    ("Check", 150), ("Finding", 220), ("Status", 90), ("Still there", 90),
                    ("Detail", 0));

                var told = Ui.Stack();

                return Ui.Card("Findings",
                    Ui.Count(findings.Count, "finding")
                    + (findings.Any(f => !f.IsPresent)
                        ? ", some no longer reported by the latest run"
                        : string.Empty),
                    table,
                    Ui.Picking(table,
                        new Ui.OnPicked("Take {n} off", (picked, say) =>
                        {
                            say.Children.Clear();

                            var gone = IssueFindings.Detach(issue,
                                picked.OfType<Finding>().Select(f => f.Id).ToList());

                            if (gone == 0) return;

                            issue.ModifiedTicks = Now;

                            var problem = Write(_session, _register);

                            if (problem != null)
                            {
                                say.Children.Add(Ui.Problem(problem));
                                return;
                            }

                            say.Children.Add(Ui.Done(Ui.Count(gone, "finding")
                                + " taken off this issue. The findings themselves are untouched."));

                            Redraw();
                        })),
                    told);
            }

            /// <summary>What the detail card's subtitle says about where an issue came from.</summary>
            private string Where(Issue issue)
            {
                var from = Origin(issue.Origin) switch
                {
                    "Clash" => "Made from " + Ui.Count(issue.Findings.Count + issue.Clashes.Count,
                                                       "clash result"),
                    "Clearance" => "Made from a clearance finding",
                    "Compare" => "Made from what changed between two revisions",
                    "Health" => "Made from a model review finding",
                    "IDS" => "Made from a specification the model failed",
                    "Imported" => "Imported" + (issue.Source == null ? string.Empty : " from " + issue.Source),
                    _ => "Raised by hand",
                };

                return from + " · raised " + Day(issue.CreatedTicks)
                       + (issue.Author == null ? string.Empty : " by " + issue.Author);
            }

            private static IReadOnlyList<string> Statuses(Issue issue)
            {
                var list = Issue.Statuses.ToList();

                // A status another tool spelled its own way is kept; the picker has to be able to
                // show what the record actually says, or saving would silently change it.
                if (!list.Contains(issue.Status, StringComparer.OrdinalIgnoreCase)) list.Insert(0, issue.Status);

                return list;
            }

            private static IReadOnlyList<string> Types(Issue issue)
            {
                var list = Issue.Types.ToList();
                if (!list.Contains(issue.Type, StringComparer.OrdinalIgnoreCase)) list.Insert(0, issue.Type);
                return list;
            }

            private static IReadOnlyList<string> Causes(Issue issue)
            {
                var list = new List<string> { string.Empty };
                list.AddRange(Issue.RootCauses);

                if (!string.IsNullOrEmpty(issue.RootCause)
                    && !list.Contains(issue.RootCause!, StringComparer.OrdinalIgnoreCase))
                    list.Insert(1, issue.RootCause!);

                return list;
            }

            private static ComboBox Suggesting(IReadOnlyList<string> options, string? current)
            {
                var combo = new ComboBox
                {
                    IsEditable = true,
                    Width = 170,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Text = current ?? string.Empty,
                };

                foreach (var option in options) combo.Items.Add(option);

                return combo;
            }

            // -------------------------------------------------------------------------------
            // Verbs
            // -------------------------------------------------------------------------------

            private List<Issue> Picked(IReadOnlyList<object> picked) =>
                picked.OfType<Issue>().ToList();

            /// <summary>
            /// Put the model where the issue is.
            ///
            /// The elements first, because selecting the things somebody has to move is the whole
            /// point; the recorded point only when nothing it names is still here, which is what
            /// an imported topic usually gives us.
            /// </summary>
            private UIElement Show(Issue issue)
            {
                var keys = issue.Elements.Where(k => !k.IsEmpty).ToList();

                if (keys.Count > 0)
                {
                    var live = keys.Where(k => _session.Model.Resolve(k) != null).ToList();

                    if (live.Count > 0)
                    {
                        _session.Model.Select(live);
                        _session.View.ZoomTo(live, 1.0);

                        return Ui.Done("Selected and framed " + Ui.Count(live.Count, "element") + ".");
                    }
                }

                if (!issue.HasPoint)
                    return Ui.Problem("This issue names no elements this federation has, and "
                        + "carries no point — there is nothing to frame.");

                const double margin = 2.5;

                _session.View.FrameBox(new BoundingBox(
                    issue.X - margin, issue.Y - margin, issue.Z - margin,
                    issue.X + margin, issue.Y + margin, issue.Z + margin));

                return Ui.Done("Framed where the issue was raised. Nothing it names is in this "
                    + "federation, so nothing is selected.");
            }

            private void ShowPicked(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var issues = Picked(picked);
                if (issues.Count == 0) return;

                if (issues.Count == 1)
                {
                    say.Children.Add(Show(issues[0]));
                    return;
                }

                var keys = issues.SelectMany(i => i.Elements)
                                 .Where(k => !k.IsEmpty && _session.Model.Resolve(k) != null)
                                 .ToList();

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Those issues name no elements this federation has."));
                    return;
                }

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done("Selected and framed " + Ui.Count(keys.Count, "element")
                    + " across " + Ui.Count(issues.Count, "issue") + "."));
            }

            private void MarkPicked(IReadOnlyList<object> picked, Panel say) =>
                Edit(picked, say, issue => issue.Status = _mark,
                     count => "Set " + count + " to " + _mark + ".");

            private void AssignPicked(IReadOnlyList<object> picked, Panel say)
            {
                var who = _who == AnyOne ? string.Empty : _who;

                Edit(picked, say, issue => issue.AssignedTo = who,
                     count => who.Length == 0
                        ? "Cleared the assignment on " + count + "."
                        : "Assigned " + count + " to " + who
                          + ". (The name comes from \"Assigned to\" on the left.)");
            }

            private void CommentPicked(IReadOnlyList<object> picked, Panel say)
            {
                var issues = Picked(picked);
                if (issues.Count == 0) return;

                say.Children.Clear();

                // One issue is the detail panel's job, and it does it better - with the trail in
                // view. More than one is the thing the panel cannot do, so that is what this is.
                if (issues.Count == 1)
                {
                    _open = issues[0].Id;
                    RedrawDetail();
                    say.Children.Add(Ui.Done("Opened it below — the comment box is under the trail."));
                    return;
                }

                var text = Ui.Text(string.Empty, null, 240);
                var done = Ui.Stack();

                say.Children.Add(Ui.Stack(
                    Ui.Field("Say on " + Ui.Count(issues.Count, "issue"), text),
                    Ui.Actions(Ui.Quiet("Comment", () =>
                    {
                        var body = text.Text.Trim();

                        done.Children.Clear();

                        if (body.Length == 0)
                        {
                            done.Children.Add(Ui.Problem("Type something first."));
                            return;
                        }

                        var author = Environment.UserName ?? "coordinator";

                        foreach (var issue in issues) issue.Say(author, body, Now);

                        var problem = Write(_session, _register);

                        done.Children.Add(problem == null
                            ? Ui.Done("Commented on " + Ui.Count(issues.Count, "issue") + ".")
                            : Ui.Problem(problem));

                        if (problem == null) Redraw();
                    })),
                    done));
            }

            /// <summary>
            /// Delete, behind a second press.
            ///
            /// Closing an issue keeps the record and is what somebody almost always means; deleting
            /// takes the sentence and the whole conversation off the project, so it asks once.
            /// </summary>
            private void DeletePicked(IReadOnlyList<object> picked, Panel say)
            {
                var issues = Picked(picked);
                if (issues.Count == 0) return;

                say.Children.Clear();

                say.Children.Add(Ui.Line("Deleting takes " + Ui.Count(issues.Count, "issue")
                    + " off this project for good, comments and all. Closing one keeps the record.",
                    0.85));

                var done = Ui.Stack();

                say.Children.Add(Ui.Actions(Ui.Danger("Delete " + Ui.Count(issues.Count, "issue"), () =>
                {
                    foreach (var issue in issues)
                    {
                        _register.Remove(issue.Id);
                        if (_open == issue.Id) _open = null;
                    }

                    var problem = Write(_session, _register);

                    if (problem != null)
                    {
                        done.Children.Add(Ui.Problem(problem));
                        return;
                    }

                    _session.Record(ActivityKind.Regroup, "deleted " + Ui.Count(issues.Count, "issue"));

                    Redraw();

                    _log.Children.Clear();
                    _log.Children.Add(Ui.Done("Deleted " + Ui.Count(issues.Count, "issue") + "."));
                })));

                say.Children.Add(done);
            }

            private void Edit(IReadOnlyList<object> picked, Panel say, Action<Issue> edit,
                              Func<string, string> said)
            {
                var issues = Picked(picked);
                if (issues.Count == 0) return;

                say.Children.Clear();

                foreach (var issue in issues)
                {
                    edit(issue);
                    issue.ModifiedTicks = Now;
                }

                var problem = Write(_session, _register);

                if (problem != null)
                {
                    say.Children.Add(Ui.Problem(problem));
                    return;
                }

                var message = said(Ui.Count(issues.Count, "issue"));

                Redraw();

                _log.Children.Clear();
                _log.Children.Add(Ui.Done(message));
            }

            private void NewIssue()
            {
                var selected = _session.Model.SelectedKeys();

                var issue = new Issue(_register.NextId(), "New issue")
                {
                    Author = Environment.UserName ?? "coordinator",
                    CreatedTicks = Now,
                    ModifiedTicks = Now,
                };

                // Whatever is selected in the host is what somebody is looking at while they press
                // it, so the issue is about that until they say otherwise.
                foreach (var key in selected.Take(200)) issue.Elements.Add(key);

                _register.Put(issue);

                var problem = Write(_session, _register);

                _log.Children.Clear();

                if (problem != null)
                {
                    _register.Remove(issue.Id);
                    _log.Children.Add(Ui.Problem(problem));
                    return;
                }

                _session.Record(ActivityKind.Regroup, "raised " + issue.Id);

                _open = issue.Id;
                Redraw();

                _log.Children.Add(Ui.Done(issue.Id + " raised"
                    + (selected.Count > 0
                        ? " about " + Ui.Count(Math.Min(selected.Count, 200), "selected element")
                        : string.Empty)
                    + ". Give it a title below."));
            }

            /// <summary>The register as it is filtered, to a spreadsheet.</summary>
            private void Sheet()
            {
                var rows = Filtered();

                if (rows.Count == 0)
                {
                    _log.Children.Clear();
                    _log.Children.Add(Ui.Problem("There is nothing on the list to write."));
                    return;
                }

                var block = new TableBlock("Number", "Title", "Description", "Status", "Type",
                                           "Priority", "Assigned to", "Due", "Raised by", "Author",
                                           "Level", "Zone", "Root cause", "Comments",
                                           "Latest comment", "Raised", "Source");

                foreach (var issue in rows)
                    block.Row(issue.Id, issue.Title, issue.Description ?? string.Empty, issue.Status,
                              issue.Type, issue.Priority, issue.AssignedTo ?? string.Empty,
                              Day(issue.DueTicks), Origin(issue.Origin), issue.Author ?? string.Empty,
                              issue.Level ?? string.Empty, issue.Zone ?? string.Empty,
                              issue.RootCause ?? string.Empty,
                              issue.Comments.Count.ToString(CultureInfo.InvariantCulture),
                              issue.LastComment?.Body ?? string.Empty,
                              Day(issue.CreatedTicks), issue.Source ?? string.Empty);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the register to a spreadsheet",
                    Filter = "Excel|*.xlsx",
                    FileName = "issues.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                };

                if (!Ui.Asked(dialog)) return;

                _log.Children.Clear();

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName, XlsxWriter.Write("Issues", block));
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the issue register",
                                Ui.Count(rows.Count, "issue") + " to " + dialog.FileName);

                _log.Children.Add(Ui.Done("Wrote " + Ui.Count(rows.Count, "issue") + " to "
                                          + dialog.FileName + "."));
            }
        }

        // ===================================================================================
        // Import
        // ===================================================================================

        private sealed class ImportScreen
        {
            private readonly Session _session;
            private readonly StackPanel _log = Ui.Stack();
            private readonly TextBox _path;

            internal ImportScreen(Session session)
            {
                _session = session;
                _path = Ui.Text(string.Empty, null, 190);
            }

            internal UIElement Build()
            {
                var setup = Ui.Stack(
                    Bench.Group("The file",
                        Ui.PathField("BCF", _path, PathKind.Open,
                                     "BCF|*.bcf;*.bcfzip|All files|*.*",
                                     "a returned BCF file")),
                    Bench.Group(string.Empty,
                        Ui.Small("Reading the same file again updates the issues it already "
                                 + "brought in and adds only what is new — statuses, replies and "
                                 + "due dates — so a long-running exchange stays one list.")),
                    Bench.Group(string.Empty,
                        Ui.Small("A topic answering a finding this project sent out goes back "
                                 + "onto that finding rather than arriving as a new issue: it is "
                                 + "the same clash, and the board should say what the reviewer "
                                 + "said about it. Everything else becomes an issue, which is "
                                 + "what it already was.")));

                var stage = Ui.Stack(
                    Bench.Ready("Reads a BCF file into this project's issue register.",
                                "Everything that comes in is an ordinary issue afterwards: it "
                                + "filters, sorts, assigns, comments and exports beside the ones "
                                + "raised here."),
                    _log);

                return Bench.Surface(setup, Ui.Runner("Read", _log, Read), stage);
            }

            private void Read(Job job)
            {
                var path = _path.Text.Trim();

                if (path.Length == 0)
                {
                    _log.Children.Add(Ui.Problem("Pick the BCF file first."));
                    return;
                }

                BcfReadResult read;

                try
                {
                    using (var stream = System.IO.File.OpenRead(path))
                        read = BcfReader.Read(stream);
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                job.Say("Merging into the register...");

                var named = System.IO.Path.GetFileName(path);

                // The findings first. A topic this project sent out is an answer about a clash or
                // a breach somebody here already knows about, and letting it in as a fresh issue
                // would put a second row beside the one it answers - which is the failure the
                // whole round trip exists to avoid.
                var (answered, strangers) =
                    FindingBcf.Merge(_session.Findings.Findings, read.Topics, named);

                if (answered > 0 && !_session.Findings.Save())
                    _log.Children.Add(Ui.Note("The answers are on the board but could not be "
                        + "kept: " + (_session.Findings.LastSaveProblem
                                      ?? "there was nowhere to write.")));

                var register = IssueViews.Read(_session);
                var result = IssueBcf.Merge(register, strangers, named);

                var problem = Write(_session, register);

                if (problem != null)
                {
                    _log.Children.Add(Ui.Problem(problem));
                    return;
                }

                _session.Record(ActivityKind.Exchange, "read issues from a BCF file",
                                result.Explain() + " from " + path);

                foreach (var warning in read.Warnings) _log.Children.Add(Ui.Note(warning));
                foreach (var note in result.Notes) _log.Children.Add(Ui.Note(note));

                _log.Children.Add(Ui.Info(read.ToString() + " " + result.Explain()
                    + (answered > 0
                        ? " " + Ui.Count(answered, "finding") + " this project sent out came back "
                          + "answered, and " + (answered == 1 ? "it is" : "they are")
                          + " on the board rather than in the register."
                        : string.Empty),
                    "Open the register", () => Open(null)));
            }
        }

        // ===================================================================================
        // Export
        // ===================================================================================

        private sealed class ExportScreen
        {
            private readonly Session _session;
            private readonly IssueRegister _register;
            private readonly StackPanel _log = Ui.Stack();

            private readonly HashSet<string> _show;

            private bool _openOnly = true;
            private bool _withFindings = true;

            internal ExportScreen(Session session)
            {
                _session = session;
                _register = IssueViews.Read(session);
                _show = new HashSet<string>(Issue.Statuses, StringComparer.OrdinalIgnoreCase);
            }

            internal UIElement Build()
            {
                var ticks = Issue.Statuses.Select(status => (UIElement)Ui.Check(status,
                    _show.Contains(status), on =>
                    {
                        if (on) _show.Add(status); else _show.Remove(status);
                    })).ToList();

                var setup = Ui.Stack(
                    Bench.Group("What goes out",
                        Ui.Check("Only the ones that are still open", _openOnly, on => _openOnly = on),
                        Ui.Stack(ticks.ToArray())),
                    Bench.Group("The findings behind them",
                        Ui.Check("Send the findings each issue is about", _withFindings,
                                 on => _withFindings = on),
                        Ui.Small("An issue is the conversation and a finding is the thing it is "
                                 + "about. Sending only the issues makes the file half a story: "
                                 + "eleven clashes arrive as one line with no geometry, and the "
                                 + "reader has nothing to look at. Each finding goes out as its "
                                 + "own topic, related to its issue both ways round, which is "
                                 + "what BCF's relation is for and what every tool that reads "
                                 + "BCF understands.")),
                    Bench.Group(string.Empty,
                        Ui.Small("One topic per issue, carrying its status, type, priority, "
                                 + "assignee, due date, description and every comment. The "
                                 + "register's own number travels as a label, so a reply can be "
                                 + "matched back to the issue it answers.")));

                var stage = Ui.Stack(
                    Bench.Ready("Writes the register out as BCF, for whoever has to act on it.",
                                Ui.Count(_register.Issues.Count, "issue") + " on this project, "
                                + _register.Open + " still open."),
                    _log);

                return Bench.Surface(setup, Ui.Runner("Write", _log, Write), stage);
            }

            private void Write(Job job)
            {
                var going = _register.Issues
                    .Where(i => _show.Contains(i.Status))
                    .Where(i => !_openOnly || i.IsOpen)
                    .ToList();

                if (going.Count == 0)
                {
                    _log.Children.Add(Ui.Problem("Nothing matches what you asked for, so there is "
                        + "nothing to write."));
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the issues as BCF",
                    Filter = "BCF|*.bcf",
                    FileName = "issues.bcf",
                    AddExtension = true,
                    DefaultExt = ".bcf",
                };

                if (!Ui.Asked(dialog)) return;

                job.Say("Building topics...");

                var topics = going.Select(IssueBcf.ToTopic).ToList();
                var findings = 0;

                if (_withFindings)
                {
                    var register = _session.Findings.Findings;

                    var attached = going
                        .SelectMany(i => i.Findings)
                        .Distinct(StringComparer.Ordinal)
                        .Select(register.Find)
                        .Where(f => f != null)
                        .Select(f => f!)
                        .ToList();

                    var byGuid = topics.ToDictionary(t => t.Guid, t => t, StringComparer.Ordinal);

                    var whose = going.ToDictionary(i => IssueBcf.ToTopic(i).Guid,
                                                   i => (IEnumerable<string>)i.Findings,
                                                   StringComparer.Ordinal);

                    topics = FindingBcf.Together(attached, topics,
                        topic => whose.TryGetValue(topic.Guid, out var ids)
                            ? ids
                            : Array.Empty<string>()).ToList();

                    findings = attached.Count;

                    // The findings are about to travel under the topic guids stamped on them, so
                    // the register has to remember which - otherwise a file coming back names
                    // topics nothing here answers to and every reply lands as a new issue.
                    if (findings > 0 && !_session.Findings.Save())
                        _log.Children.Add(Ui.Note("The findings went out, but which topics they "
                            + "went out as could not be kept: "
                            + (_session.Findings.LastSaveProblem
                               ?? "there was nowhere to write.")
                            + " A reply may come home as new issues rather than as answers."));
                }

                BcfWriteResult written;

                try
                {
                    using (var stream = System.IO.File.Create(dialog.FileName))
                        written = BcfWriter.Write(stream, topics, BcfVersion.V21);
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Exchange, "wrote the issues as BCF",
                                written.ToString() + " to " + dialog.FileName);

                foreach (var note in written.Notes) _log.Children.Add(Ui.Note(note));

                _log.Children.Add(Ui.Done(written.ToString()
                    + (findings > 0
                        ? " " + Ui.Count(findings, "finding") + " went with them, each related to "
                          + "the issue it is about."
                        : string.Empty)
                    + " Written to " + dialog.FileName + "."));
            }
        }
    }
}
