using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Coordinate workspace: the board, the tests behind it, the rules that shape it, and the
    /// two things that leave the building — a report and a BCF file.
    /// </summary>
    public static class CoordinateViews
    {
        /// <summary>
        /// The last board that was built, so Report and BCF work on what the user is looking at.
        ///
        /// Held rather than rebuilt because rebuilding would silently disagree: the report would be
        /// of a board with different grouping from the one on screen if a rule had been edited in
        /// between, and nothing would say so.
        /// </summary>
        internal static ClashPipelineResult? LastBoard { get; private set; }

        /// <summary>What the last board was of, for a report's cover.</summary>
        internal static string? LastBoardLabel { get; private set; }

        /// <summary>How many of a group's results are listed when it is opened.</summary>
        private const int DetailCap = 300;

        /// <summary>How many elements a raised issue records.</summary>
        private const int IssueElementCap = 200;

        /// <summary>
        /// The results the last run found back again, by identity.
        ///
        /// A regression is not a status. Carry-over sets a returning result to Active, since it has
        /// a history and New would be a lie, and records it in the delta instead — so the only way
        /// to know a result came back is to hold the delta's list. The board's items and the
        /// delta's are the same objects, which is what makes identity the right test.
        /// </summary>
        internal static HashSet<ClashItem> Regressed()
        {
            var regressed = new HashSet<ClashItem>();
            var delta = LastDelta;

            if (delta != null) foreach (var item in delta.Regressed) regressed.Add(item);

            return regressed;
        }

        /// <summary>
        /// Whether a group holds anything the last run had not already seen.
        ///
        /// Shared by Triage and the report on purpose. Two screens with their own idea of what
        /// "changed" means is how somebody filters a board to eleven groups, reports the same
        /// filter, and gets nine.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="regressed">What came back, from <see cref="Regressed"/>.</param>
        internal static bool Changed(ClashGroup group, ICollection<ClashItem> regressed) =>
            group.Items.Any(i => i.Status == FindingStatus.New || regressed.Contains(i));

        /// <summary>
        /// What changed between the last board and the one before it.
        ///
        /// Held beside the board rather than recomputed, because carry-over runs against the
        /// snapshot the run then overwrites — ask again afterwards and every answer is "unchanged".
        /// </summary>
        internal static ClashDelta? LastDelta { get; private set; }

        // ===================================================================================
        // Building the board
        // ===================================================================================

        /// <summary>
        /// Read the clash engine and run the rules over it.
        ///
        /// Every coordinate is scaled to metres here, once, because every threshold above this line
        /// — a grouping distance, a tolerance, a proximity band — is stated in metres, and a
        /// millimetre model would otherwise group everything within five millimetres and report one
        /// group per clash.
        /// </summary>
        internal static ClashPipelineResult? Build(Session session, Job job, ICollection<string> notes)
        {
            if (!session.Clash.IsAvailable) return Hydrate(session, job, notes);

            var scale = session.MetresPerUnit;
            var items = new List<ClashItem>();
            var levels = Levels(session, job, scale);

            foreach (var test in session.Clash.Tests())
            {
                if (!job.Step(items.Count, "results read")) return null;

                foreach (var result in session.Clash.Results(test.Id))
                {
                    var item = new ClashItem(result.Key(), test.Name)
                    {
                        TestFolder = test.Folder,
                        X = result.PointX * scale,
                        Y = result.PointY * scale,
                        Z = result.PointZ * scale,
                        ModelA = ModelOf(session, result.A),
                        ModelB = ModelOf(session, result.B),
                        DisciplineA = session.Profile.DisciplineOf(result.A.ModelScope),
                        DisciplineB = session.Profile.DisciplineOf(result.B.ModelScope),
                        CategoryA = CategoryOf(session, result.A),
                        CategoryB = CategoryOf(session, result.B),
                        Level = levels.LevelAt(result.PointZ * scale),
                    };

                    if (result.HostAssignedTo != null) item.Properties["Host/AssignedTo"] = result.HostAssignedTo;
                    if (result.HostStatus != null) item.Properties["Host/Status"] = result.HostStatus;

                    items.Add(item);
                }
            }

            if (items.Count == 0)
            {
                notes.Add("The clash engine has tests but no results. Run them in Clash Detective first.");
                return null;
            }

            // Cross-test duplicates go first, before anything counts or carries over. Two tests
            // that both report one pipe through one wall are one problem; leaving both on the board
            // means the same conflict is assigned twice, reported twice, and closed once.
            job.Say("Collapsing duplicates...");

            var collapse = DuplicateCollapse.Across(items, session.Profile.DuplicateBand);
            var survivors = collapse.Kept;

            job.Say("Carrying over the last run...");

            var rules = ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);
            var snapshot = Snapshot(session);
            var delta = ClashCarryOver.Apply(survivors, snapshot);

            job.Say("Applying rules...");

            var options = rules.ToOptions(session.Profile.ClashProximity);
            options.CollapsedDuplicates = collapse.Count;

            // Read from the file rather than from the snapshot. A hand assignment is a decision
            // about a group, not about a run — reading it off the previous run would mean losing
            // every assignment the first time somebody opens the board on a machine that has not
            // run it before.
            options.Decisions = Decisions(session);
            options.PinnedGroups = Pinned(session);

            // After carry-over, never before. A raised issue is not something the clash engine
            // reported, so comparing it against the previous run would find it missing from the
            // engine's results and call it resolved every single week.
            var everything = new List<ClashItem>(survivors);
            var raised = Issues(session).Open();
            everything.AddRange(raised);

            var board = ClashPipeline.Run(everything, options);

            if (raised.Count > 0)
                notes.Add(Ui.Count(raised.Count, "issue") + " raised by hand "
                          + (raised.Count == 1 ? "is" : "are") + " on this board alongside the engine's "
                          + "results. They group, assign, report and export like anything else; the "
                          + "test column says where they came from.");

            notes.Add(delta.ToString());

            if (collapse.Count > 0)
                notes.Add(Ui.Count(collapse.Count, "result")
                          + " were the same conflict found by more than one test, and were folded "
                          + "into the one the funnel keeps. They are counted, not hidden.");
            if (rules.IsDefault) notes.Add("Nothing is configured, so the built-in rules ran: " + board.Funnel);

            LastBoard = board;
            LastBoardLabel = session.Profile.ProjectName;
            LastDelta = delta;

            Save(session, board);
            return board;
        }

        /// <summary>
        /// Rebuild the board from the project file when there is no clash engine to read.
        ///
        /// This is the promise that makes CamelWorks worth installing on a Simulate seat, which is
        /// most of the seats on most jobs. Simulate has no Clash Detective, so tests cannot be read
        /// and cannot be run — but triaging a board, assigning it, reporting on it and issuing BCF
        /// are not clash detection, and the project file already carries enough to reconstruct what
        /// a Manage seat last ran.
        ///
        /// The result is labelled as a record rather than a reading, every time, because the one
        /// way this feature could do harm is by looking like a live board. It is not saved back
        /// either: writing a lower-fidelity copy over the record it came from would lose the
        /// coordinates the next Manage run needs to carry status across.
        /// </summary>
        private static ClashPipelineResult? Hydrate(Session session, Job job, ICollection<string> notes)
        {
            job.Say("Reading the last recorded run...");

            var snapshot = Snapshot(session);
            var hydrated = ClashHydration.From(snapshot, session.MetresPerUnit);

            if (hydrated.IsEmpty)
            {
                notes.Add(session.ClashProblem ?? "There is no clash engine in this Navisworks edition.");
                notes.Add("There is no recorded run in this project either, so there is nothing to open. "
                          + "A board comes from a Navisworks Manage seat running the tests once; after "
                          + "that this seat can triage, assign, report and issue it.");
                return null;
            }

            var scale = session.MetresPerUnit;
            var levels = Levels(session, job, scale);

            job.Say("Describing " + Ui.Count(hydrated.Items.Count, "result") + "...");

            var read = 0;

            foreach (var item in hydrated.Items)
            {
                if (read++ % 500 == 0 && !job.Step(read, "results described")) return null;

                // Everything except the coordinates is re-derived from the elements themselves,
                // which Simulate reads perfectly well. Only the clash engine is missing.
                item.ModelA = ModelOf(session, item.Key.A);
                item.ModelB = ModelOf(session, item.Key.B);
                item.DisciplineA = session.Profile.DisciplineOf(item.Key.A.ModelScope);
                item.DisciplineB = session.Profile.DisciplineOf(item.Key.B.ModelScope);
                item.CategoryA = CategoryOf(session, item.Key.A);
                item.CategoryB = CategoryOf(session, item.Key.B);
                item.Level = levels.LevelAt(item.Z);
            }

            job.Say("Applying rules...");

            var rules = ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);
            var options = rules.ToOptions(session.Profile.ClashProximity);
            options.Decisions = Decisions(session);
            options.PinnedGroups = Pinned(session);

            var everything = new List<ClashItem>(hydrated.Items);
            everything.AddRange(Issues(session).Open());

            var board = ClashPipeline.Run(everything, options);

            notes.Add(hydrated.Explain());
            notes.Add("This board was rebuilt from what the project file recorded, because this "
                      + "Navisworks edition has no clash engine. Everything on it can be triaged, "
                      + "assigned, reported and issued as BCF. Two things it cannot do: run a test, "
                      + "and notice a conflict that has appeared since the run it came from.");

            if (hydrated.Unusable > 0)
                notes.Add(Ui.Count(hydrated.Unusable, "record")
                          + " in the project file could not be read back and are missing from this board.");

            LastBoard = board;
            LastBoardLabel = hydrated.Label ?? session.Profile.ProjectName;

            // No delta and no save. There was no run to compare against, and writing this board
            // back over the record it came from would replace real clash points with cell centres.
            LastDelta = null;

            return board;
        }

        private static LevelSet Levels(Session session, Job job, double scale)
        {
            job.Say("Working out levels...");

            var elevations = new List<double>();

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!item.HasGeometry) continue;
                elevations.Add(item.Bounds.MinZ * scale);

                if (elevations.Count % 5000 == 0 && !job.Step(elevations.Count, "elements measured"))
                    return LevelSet.Empty;
            }

            return LevelSet.Derive(elevations);
        }

        private static string? ModelOf(Session session, ElementKey key) =>
            session.Model.Models.FirstOrDefault(
                m => string.Equals(m.Scope, key.ModelScope, StringComparison.Ordinal))?.DisplayName;

        private static string? CategoryOf(Session session, ElementKey key) =>
            session.Model.Resolve(key)?.Category;

        private static ClashSnapshot? Snapshot(Session session)
        {
            var saved = session.Store.Section(ProjectStore.ClashSection)["snapshot"];
            if (saved.Kind != JsonKind.Array) return null;

            var records = new List<ClashRecord>();

            foreach (var entry in saved.Items)
            {
                if (!ClashKey.TryParse(entry["key"].AsString(), out var key)) continue;

                var status = Enum.TryParse<FindingStatus>(entry["status"].AsString(), out var parsed)
                    ? parsed
                    : FindingStatus.New;

                records.Add(new ClashRecord(key, status, entry["group"].AsString(),
                    entry["pinned"].AsString() == "yes", entry["test"].AsString()));
            }

            return new ClashSnapshot(records, Decisions(session));
        }

        /// <summary>
        /// The groups people made by hand, which the rules must never re-derive.
        ///
        /// Read from the project file rather than from the last snapshot, for the same reason
        /// assignments are: a pin is a decision about the work, not about a run, and reading it off
        /// the previous run would mean losing every one the first time somebody opens the board on
        /// a machine that has not run it before.
        /// </summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Pinned(Session session)
        {
            var pinned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var saved = session.Store.Section(ProjectStore.ClashSection)["pinned"];

            foreach (var name in saved.Keys)
            {
                var keys = saved[name].Items
                    .Select(k => k.AsString())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .Select(k => k!)
                    .ToList();

                if (keys.Count > 0) pinned[name] = keys;
            }

            return pinned;
        }

        /// <summary>Put results into a hand-made group, and write it down.</summary>
        private static void Pin(Session session, string name, IEnumerable<ClashItem> items)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var saved = section["pinned"];

            if (saved.Kind != JsonKind.Object)
            {
                saved = JsonValue.Object();
                section.Set("pinned", saved);
            }

            var keys = new List<string>();
            var moving = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                keys.Add(item.Key.ToString());
                moving.Add(item.Key.ToString());
            }

            // A result belongs to exactly one hand-made group. Leaving it in its old one as well
            // would make which group it lands in depend on dictionary order, which is the kind of
            // thing that looks like the board losing track of a decision.
            foreach (var existing in saved.Keys.ToList())
            {
                if (string.Equals(existing, name, StringComparison.Ordinal)) continue;

                var left = saved[existing].Items
                    .Select(k => k.AsString())
                    .Where(k => !string.IsNullOrEmpty(k) && !moving.Contains(k!))
                    .Select(k => k!)
                    .ToList();

                if (left.Count == 0) saved.Remove(existing);
                else saved.Set(existing, JsonValue.Array(left.Select(JsonValue.String)));
            }

            if (saved[name].Kind == JsonKind.Array)
                foreach (var already in saved[name].Items)
                {
                    var text = already.AsString();
                    if (text != null && !moving.Contains(text)) keys.Add(text);
                }

            saved.Set(name, JsonValue.Array(keys.Select(JsonValue.String)));
        }

        /// <summary>Stop treating a group as hand-made, so the rules decide it again.</summary>
        private static void Unpin(Session session, string name)
        {
            var saved = session.Store.Section(ProjectStore.ClashSection)["pinned"];
            if (saved.Kind == JsonKind.Object) saved.Remove(name);
        }

        /// <summary>Every issue somebody raised on this project, from the project file.</summary>
        internal static ManualIssueList Issues(Session session) =>
            ManualIssueList.FromJson(session.Store.Section(ProjectStore.ClashSection)["issues"]);

        /// <summary>Write the issue list back.</summary>
        private static void SaveIssues(Session session, ManualIssueList issues) =>
            session.Store.Section(ProjectStore.ClashSection).Set("issues", issues.ToJson());

        /// <summary>The assignments made by hand, which outrank the rules and survive a re-run.</summary>
        private static IReadOnlyDictionary<string, GroupDecision> Decisions(Session session)
        {
            var decisions = new Dictionary<string, GroupDecision>(StringComparer.Ordinal);
            var saved = session.Store.Section(ProjectStore.ClashSection)["decisions"];

            foreach (var name in saved.Keys)
                decisions[name] = new GroupDecision(saved[name]["party"].AsString(),
                                                   saved[name]["priority"].AsString());

            return decisions;
        }

        /// <summary>
        /// Write the board down, keeping the version it replaces.
        ///
        /// One step of history, not a stack. Every board edit is small and immediately visible, so
        /// the mistake people actually make is the last one — and a product that offered ten steps
        /// of undo over a file two people share would be promising something it cannot honour the
        /// moment the other person saves.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="board">The board to record.</param>
        /// <param name="summary">What to write in the activity log.</param>
        private static void Save(Session session, ClashPipelineResult board, string? summary = null)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var previous = section["snapshot"];

            if (previous.Kind == JsonKind.Array) section.Set("previous", previous);

            var snapshot = ClashSnapshot.Of(board);

            // Raised issues are kept in their own list and left out of the snapshot. The snapshot
            // is what the next run's carry-over compares the ENGINE's results against, and a record
            // the engine can never produce would read as resolved on every single run.
            var array = JsonValue.Array(snapshot.Records
                .Where(r => !string.Equals(r.TestName, ManualIssue.RaisedByHand, StringComparison.Ordinal))
                .Select(r =>
            {
                var json = JsonValue.Object()
                    .Set("key", JsonValue.String(r.Key.ToString()))
                    .Set("status", JsonValue.String(r.Status.ToString()));

                if (r.GroupName != null) json.Set("group", JsonValue.String(r.GroupName));
                if (r.GroupWasPinned) json.Set("pinned", JsonValue.String("yes"));
                if (r.TestName != null) json.Set("test", JsonValue.String(r.TestName));
                return json;
            }));

            section.Set("snapshot", array);
            session.Record(ActivityKind.Regroup, summary ?? "ran the rules: " + board.Funnel);
        }

        /// <summary>True when there is a version of the board to go back to.</summary>
        private static bool CanRewind(Session session) =>
            session.Store.Section(ProjectStore.ClashSection)["previous"].Kind == JsonKind.Array;

        /// <summary>
        /// Put back the board as it was before the last edit.
        ///
        /// Swaps rather than pops, so pressing Undo twice puts you back where you started. That is
        /// what a single-step undo has to do — one that only went one way would leave somebody who
        /// undid the wrong thing with no way forward.
        /// </summary>
        private static bool Rewind(Session session)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var previous = section["previous"];

            if (previous.Kind != JsonKind.Array) return false;

            var current = section["snapshot"];

            section.Set("snapshot", previous);
            if (current.Kind == JsonKind.Array) section.Set("previous", current);
            else section.Remove("previous");

            return session.Store.Save();
        }

        // ===================================================================================
        // Triage
        // ===================================================================================

        /// <summary>The board.</summary>
        public static UIElement Triage() => new TriageScreen().Build();

        /// <summary>
        /// The board, held as an object because it filters without reloading.
        ///
        /// Reading the clash engine and running the rules over a federation is the expensive part,
        /// and a board that had to redo it to answer "just show me the unassigned ones" would make
        /// the filter something you use once and then stop using.
        /// </summary>
        private sealed class TriageScreen
        {
            /// <summary>Everything.</summary>
            private const string All = "Everything";

            /// <summary>Only groups nobody owns.</summary>
            private const string Unassigned = "Unassigned";

            /// <summary>Only groups the last run had not seen, or that came back.</summary>
            private const string Changed = "New or back again";

            /// <summary>Only groups assigned to whoever is sitting here.</summary>
            private const string Mine = "Mine";

            private readonly StackPanel _summary = Ui.Stack();
            private readonly StackPanel _rows = Ui.Stack();
            private readonly StackPanel _detail = Ui.Stack();
            private readonly TextBox _search;
            private readonly ComboBox _preset;

            /// <summary>
            /// The results this run found back again, by reference.
            ///
            /// A regression is not a status. Carry-over sets a returning result to Active — it has
            /// a history, so New would be a lie — and records it in the delta instead, which means
            /// the only way to know a row came back is to hold the delta's list. The board's items
            /// and the delta's are the same objects, so identity is the right test.
            /// </summary>
            private HashSet<ClashItem> _regressed = new HashSet<ClashItem>();

            private ClashPipelineResult? _board;
            private ClashDelta? _delta;
            private Session? _session;

            internal TriageScreen()
            {
                _search = Ui.Text(string.Empty, _ => Redraw(), 220);
                _preset = Ui.Choice(new[] { All, Unassigned, Changed, Mine }, All, _ => Redraw(), 170);
            }

            internal UIElement Build()
            {
                var run = Ui.Runner("Load the board", _summary, job =>
                {
                    _session = Host.Current;
                    _rows.Children.Clear();
                    _detail.Children.Clear();

                    if (_session == null) { _summary.Children.Add(Ui.Line(Host.NoModel)); return; }

                    var notes = new List<string>();
                    _board = CoordinateViews.Build(_session, job, notes);
                    _delta = LastDelta;

                    _regressed = Regressed();

                    foreach (var note in notes) _summary.Children.Add(Ui.Note(note));

                    if (_board == null) return;

                    Summarise();
                    Redraw();
                });

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Triage"),
                    Ui.Sub("The engine's results, grouped into work. The funnel says how a count of several "
                           + "thousand became this one — every row removed is accounted for rather than "
                           + "quietly dropped."),
                    run,
                    _summary,
                    Ui.Across(Ui.Field("Show", _preset), Ui.Field("Find", _search)),
                    _rows,
                    _detail,
                    new RaiseScreen().Build(),
                    HeadroomCard()));
            }

            /// <summary>
            /// The funnel, and what changed since the last run.
            ///
            /// The delta goes above the board rather than in a column, because it is the first
            /// question of the week and the last one anybody wants to derive by scrolling: what is
            /// new, and what did we already fix and has come back.
            /// </summary>
            private void Summarise()
            {
                var board = _board!;
                var funnel = board.Funnel;

                _summary.Children.Add(Ui.Across(
                    Ui.Figure(funnel.Input.ToString("N0", CultureInfo.InvariantCulture), "from the engine"),
                    Ui.Figure(funnel.Groups.ToString("N0", CultureInfo.InvariantCulture), "groups"),
                    Ui.Figure(funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture), "unassigned",
                        funnel.Unassigned > 0 ? Tone.Warn : Tone.Good),
                    Ui.Figure(funnel.Suppressed.ToString("N0", CultureInfo.InvariantCulture), "suppressed")));

                if (_delta != null)
                {
                    var regressed = _delta.Regressed.Count;

                    _summary.Children.Add(Ui.Across(
                        Ui.Figure(_delta.New.Count.ToString("N0", CultureInfo.InvariantCulture), "new",
                            _delta.New.Count > 0 ? Tone.Warn : Tone.Plain),
                        Ui.Figure(_delta.Persisting.Count.ToString("N0", CultureInfo.InvariantCulture), "still there"),
                        Ui.Figure(_delta.Resolved.Count.ToString("N0", CultureInfo.InvariantCulture), "gone",
                            _delta.Resolved.Count > 0 ? Tone.Good : Tone.Plain),
                        Ui.Figure(regressed.ToString("N0", CultureInfo.InvariantCulture), "came back",
                            regressed > 0 ? Tone.Bad : Tone.Plain)));

                    if (regressed > 0)
                        _summary.Children.Add(Ui.Problem(
                            Ui.Count(regressed, "clash") + " that a previous run recorded as resolved is "
                            + "back. Something that was fixed has been undone, or a model went backwards — "
                            + "either way it is the first thing to look at this week."));

                    if (_delta.WeakMatches > 0)
                        _summary.Children.Add(Ui.Note(
                            Ui.Count(_delta.WeakMatches, "result")
                            + " matched the last run only on a weak key, because the elements involved "
                            + "carry no stable identity. Their history is a best guess."));
                }

                _summary.Children.Add(Ui.Line(funnel.ToString(), 0.8));

                if (funnel.RulesApplied.Count > 0)
                    _summary.Children.Add(Ui.Line("Rules: " + string.Join(", ", funnel.RulesApplied), 0.6));
            }

            /// <summary>Which groups the preset and the search box leave.</summary>
            private IReadOnlyList<ClashGroup> Filtered()
            {
                if (_board == null) return Array.Empty<ClashGroup>();

                var preset = _preset.SelectedItem as string ?? All;
                var query = _search.Text.Trim();
                var me = _session?.Profile.Author;

                return _board.Groups.Where(group =>
                    Passes(group, preset, me) && Matches(group, query)).ToList();
            }

            private bool Passes(ClashGroup group, string preset, string? me) => preset switch
            {
                Unassigned => group.AssignedTo == null,
                Changed => CoordinateViews.Changed(group, _regressed),
                Mine => me != null && string.Equals(group.AssignedTo, me, StringComparison.OrdinalIgnoreCase),
                _ => true,
            };

            /// <summary>
            /// Free text against everything on the row and everything in it.
            ///
            /// Searching the items as well as the group is the point: "level 3" and "AHU-02" are
            /// how people describe where a problem is, and neither is necessarily in the group's
            /// name.
            /// </summary>
            private static bool Matches(ClashGroup group, string query)
            {
                if (query.Length == 0) return true;

                if (Has(group.Name, query) || Has(group.AssignedTo, query) || Has(group.Priority, query))
                    return true;

                foreach (var item in group.Items)
                    if (Has(item.TestName, query) || Has(item.Level, query) || Has(item.ModelPair, query)
                        || Has(item.CategoryA, query) || Has(item.CategoryB, query)
                        || Has(item.Grid, query) || Has(item.Zone, query))
                        return true;

                return false;
            }

            private static bool Has(string? text, string query) =>
                text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            private void Redraw()
            {
                _rows.Children.Clear();

                if (_board == null) return;

                if (_board.Groups.Count == 0)
                {
                    _rows.Children.Add(Ui.Empty("Nothing on the board.",
                        "Every result was suppressed by a rule. The funnel above says by which."));
                    return;
                }

                var groups = Filtered();

                if (groups.Count == 0)
                {
                    _rows.Children.Add(Ui.Empty("Nothing matches.",
                        "The board has " + Ui.Count(_board.Groups.Count, "group")
                        + "; none of them match this filter."));
                    return;
                }

                if (groups.Count < _board.Groups.Count)
                    _rows.Children.Add(Ui.Line("Showing " + groups.Count + " of "
                                               + _board.Groups.Count + " groups.", 0.6));

                var rows = groups.Select(g => new TableRow(g,
                    g.Name,
                    g.Items.Count.ToString("N0", CultureInfo.InvariantCulture),
                    Change(g),
                    g.AssignedTo ?? "-",
                    g.Priority ?? string.Empty,
                    g.IsPinned ? "pinned" : string.Empty)
                {
                    Tone = ToneOf(g),
                }).ToList();

                _rows.Children.Add(Ui.Table(rows,
                    ("Group", 210), ("Clashes", 70), ("Since last run", 120),
                    ("Assigned to", 110), ("Priority", 80), ("", 0))
                    .OnPick(item =>
                    {
                        _detail.Children.Clear();
                        if (item is ClashGroup group && _session != null)
                            _detail.Children.Add(Detail(_session, group));
                    }));
            }

            /// <summary>What happened to a group since the last run, on one line.</summary>
            private string Change(ClashGroup group)
            {
                if (_delta == null) return string.Empty;

                var fresh = group.Items.Count(i => i.Status == FindingStatus.New);
                var back = group.Items.Count(i => _regressed.Contains(i));

                if (back > 0 && fresh > 0) return back + " back, " + fresh + " new";
                if (back > 0) return back + " back";
                if (fresh == group.Items.Count) return "all new";
                if (fresh > 0) return fresh + " new";

                return "unchanged";
            }

            private Tone ToneOf(ClashGroup group)
            {
                if (group.Items.Any(i => _regressed.Contains(i))) return Tone.Bad;
                if (group.AssignedTo == null) return Tone.Warn;
                return Tone.Plain;
            }
        }

        // ===================================================================================
        // Headroom
        // ===================================================================================

        /// <summary>
        /// Clear height between a walkable surface and whatever is above it.
        ///
        /// It lives on the board because that is where it belongs: a duct 1.9 m over a corridor is
        /// a coordination problem in exactly the way a clash is, and it is the one no clash test at
        /// any tolerance will ever find — there is air between them.
        /// </summary>
        private static UIElement HeadroomCard()
        {
            var results = Ui.Stack();

            var floors = "Floors and slabs";
            var targets = "Everything else";
            var minimum = Ui.Text("2.100", null, 80);

            var floorPicker = Ui.Choice(new[] { "Floors and slabs", "Current selection", "Everything" },
                                        floors, v => floors = v);

            var targetPicker = Ui.Choice(new[] { "Everything else", "Current selection", "Everything" },
                                         targets, v => targets = v);

            var run = Ui.Runner("Check clear height", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                if (!double.TryParse(minimum.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                     out var height) || height <= 0)
                {
                    results.Children.Add(Ui.Line("Type a height in metres, like 2.100.", 0.8));
                    return;
                }

                var scale = session.MetresPerUnit;
                var selected = new HashSet<ElementKey>(session.Model.SelectedKeys());

                var surfaces = new List<HeadroomElement>();
                var above = new List<HeadroomElement>();
                var byId = new Dictionary<string, ElementKey>(StringComparer.Ordinal);
                var read = 0;

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(read++, "elements read")) return;
                    if (!item.HasGeometry) continue;

                    var bounds = item.Bounds;
                    var id = item.Key.ToString();

                    byId[id] = item.Key;

                    var element = new HeadroomElement(id, item.DisplayName)
                    {
                        MinX = bounds.MinX * scale, MaxX = bounds.MaxX * scale,
                        MinY = bounds.MinY * scale, MaxY = bounds.MaxY * scale,
                        MinZ = bounds.MinZ * scale, MaxZ = bounds.MaxZ * scale,
                    };

                    var walkable = IsWalkable(item);
                    var isSelected = selected.Contains(item.Key);

                    if (floors == "Everything"
                        || (floors == "Current selection" && isSelected)
                        || (floors == "Floors and slabs" && walkable))
                        surfaces.Add(element);

                    if (targets == "Everything"
                        || (targets == "Current selection" && isSelected)
                        || (targets == "Everything else" && !walkable))
                        above.Add(element);
                }

                job.Say("Measuring...");
                var found = Headroom.Check(surfaces, above, height);

                results.Children.Add(Ui.Line(found.ToString(), 1, true));

                if (surfaces.Count == 0)
                {
                    results.Children.Add(Ui.Empty("No walkable surfaces found.",
                        floors == "Floors and slabs"
                            ? "Nothing loaded has a category that reads as a floor or a slab. Select the "
                              + "surfaces you mean and run it against the current selection instead."
                            : "Nothing is selected."));
                    return;
                }

                if (found.Spans.Count == 0)
                {
                    results.Children.Add(Ui.Empty("Nothing is too low.",
                        "Every surface has at least " + height.ToString("0.000", CultureInfo.InvariantCulture)
                        + " m of clear height above it."));
                    return;
                }

                var rows = found.Spans.Take(300).Select(span => new TableRow(span,
                    span.Clear.ToString("0.000", CultureInfo.InvariantCulture),
                    span.Obstruction.Name,
                    span.Floor.Name,
                    span.X.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                        + span.Y.ToString("0.0", CultureInfo.InvariantCulture))
                {
                    Tone = span.Clear < height * 0.8 ? Tone.Bad : Tone.Warn,
                }).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Clear (m)", 90), ("Under", 190), ("Over", 190), ("At", 0))
                    .OnPick(item =>
                    {
                        if (!(item is HeadroomSpan span)) return;

                        var keys = new List<ElementKey>();
                        if (byId.TryGetValue(span.Floor.Id, out var a)) keys.Add(a);
                        if (byId.TryGetValue(span.Obstruction.Id, out var b)) keys.Add(b);

                        if (keys.Count == 0) return;

                        session.Model.Select(keys);
                        session.View.ZoomTo(keys, 2.0);
                    }));

                session.Record(ActivityKind.Regroup, "checked clear height: " + found);
            });

            return Ui.Card("Headroom",
                "The check a clash test cannot do: a duct 1.9 m above a corridor clashes with nothing.",
                Ui.Field("Surfaces", floorPicker),
                Ui.Field("Look above at", targetPicker),
                Ui.Field("Minimum clear height (m)", minimum),
                run,
                results);
        }

        /// <summary>
        /// Whether an element reads as something people walk on.
        ///
        /// A guess, and a stated one — the alternative is asking the user to build a set before
        /// they can run the check at all, which is the setup tax this product exists to not charge.
        /// </summary>
        private static bool IsWalkable(IModelItem item)
        {
            var category = item.Category ?? string.Empty;
            var type = item.TypeName ?? string.Empty;

            foreach (var word in new[] { "floor", "slab", "stair", "landing", "ramp", "platform", "deck" })
                if (category.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        /// <summary>
        /// One group, opened: what is in it, and the four things a coordinator does to it.
        ///
        /// Assign it, group some of it differently, mark what has been dealt with, and change your
        /// mind. Until now the board could be read and filtered but not edited, which meant the
        /// statuses carry-over exists to preserve had no way of ever being set — the whole
        /// human-judgement half of the pipeline was unreachable from the product.
        /// </summary>
        private static UIElement Detail(Session session, ClashGroup group)
        {
            var status = Ui.Stack();

            var rows = group.Items.Take(DetailCap).Select(item => new TableRow(item,
                item.TestName,
                item.Level ?? "-",
                item.ModelPair,
                (item.CategoryA ?? "?") + " v " + (item.CategoryB ?? "?"),
                item.Status.ToString())).ToList();

            var table = Ui.Table(rows,
                ("Test", 130), ("Level", 90), ("Models", 170), ("What", 170), ("Status", 90))
                .Multi()
                .OnPick(item =>
                {
                    if (!(item is ClashItem clash)) return;

                    var keys = new[] { clash.Key.A, clash.Key.B };
                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);
                });

            var parties = session.Profile.Parties.ToList();
            if (parties.Count == 0) parties.Add("Unassigned");

            var party = parties[0];
            var picker = Ui.Choice(parties, party, p => party = p);

            var assign = Ui.Button("Assign this group", () =>
            {
                status.Children.Clear();
                group.AssignByHand(party);

                var section = session.Store.Section(ProjectStore.ClashSection);
                var decisions = section["decisions"];

                if (decisions.Kind != JsonKind.Object)
                {
                    decisions = JsonValue.Object();
                    section.Set("decisions", decisions);
                }

                decisions.Set(group.Name, JsonValue.Object().Set("party", JsonValue.String(party)));

                status.Children.Add(session.Store.Save()
                    ? Ui.Line("\"" + group.Name + "\" goes to " + party
                              + ". A hand assignment outranks the rules and survives the next run.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
            });

            var newGroup = Ui.Text(group.Name + " - split", null, 200);

            var regroup = Ui.Button("Move the picked ones", () =>
            {
                status.Children.Clear();

                var picked = table.PickedMany().OfType<ClashItem>().ToList();
                var name = newGroup.Text.Trim();

                if (picked.Count == 0)
                {
                    status.Children.Add(Ui.Line("Pick the results to move first. "
                        + "Shift-click and control-click work here the way they do everywhere else.", 0.8));
                    return;
                }

                if (name.Length == 0)
                {
                    status.Children.Add(Ui.Line("Give the group a name first.", 0.8));
                    return;
                }

                Pin(session, name, picked);

                status.Children.Add(session.Store.Save()
                    ? Ui.Line(Ui.Count(picked.Count, "result") + " will be in \"" + name
                              + "\" from the next load. A hand-made group is never re-derived, so the "
                              + "rules will leave it alone.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
            });

            var release = group.IsPinned
                ? Ui.Button("Let the rules decide this again", () =>
                {
                    status.Children.Clear();
                    Unpin(session, group.Name);

                    status.Children.Add(session.Store.Save()
                        ? Ui.Line("\"" + group.Name + "\" is no longer hand-made. The next load will group "
                                  + "its results by the rule stack.", 0.8)
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
                })
                : null;

            var mark = FindingStatus.Reviewed;
            var marks = new[] { "Reviewed", "Approved", "Resolved", "Active" };

            var marker = Ui.Choice(marks, "Reviewed", pick =>
                mark = Enum.TryParse<FindingStatus>(pick, out var parsed) ? parsed : FindingStatus.Reviewed);

            var apply = Ui.Button("Mark them", () =>
            {
                status.Children.Clear();

                var board = LastBoard;
                if (board == null) { status.Children.Add(Ui.Line("Load the board first.", 0.8)); return; }

                var picked = table.PickedMany().OfType<ClashItem>().ToList();
                if (picked.Count == 0) picked = group.Items.ToList();

                foreach (var item in picked) item.Status = mark;

                Save(session, board, "marked " + Ui.Count(picked.Count, "result") + " as " + mark);

                status.Children.Add(session.Store.Save()
                    ? Ui.Line(Ui.Count(picked.Count, "result") + " marked " + mark
                              + ". Reviewed and Approved are the two the next run will not recompute — "
                              + "they are the only judgements the engine cannot make for itself.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                Redraw(table, group);
            });

            var undo = CanRewind(session)
                ? Ui.Button("Undo the last board edit", () =>
                {
                    status.Children.Clear();

                    status.Children.Add(Rewind(session)
                        ? Ui.Line("Put back. Load the board again to see it — and pressing this a second "
                                  + "time brings the edit back, so an undo of the wrong thing is not a "
                                  + "one-way door.", 0.8)
                        : Ui.Problem(session.Store.LastSaveProblem
                                     ?? "There is no earlier version of the board to go back to."));
                })
                : null;

            return Ui.Card(group.Name,
                Ui.Count(group.Items.Count, "clash")
                + (group.Items.Count > DetailCap ? ", first " + DetailCap + " shown" : string.Empty)
                + (group.IsPinned ? " - made by hand" : string.Empty),
                Ui.Field("Assign to", picker),
                Ui.Across(assign, release, undo),
                Ui.Field("Move picked into", newGroup),
                Ui.Across(regroup),
                Ui.Field("Mark picked as", marker),
                Ui.Across(apply),
                Ui.Note("Marking changes the board, not the model. Navisworks keeps its own status on "
                        + "each clash result and CamelWorks does not write into it: two tools disagreeing "
                        + "about whether something is resolved is worse than one of them not knowing."),
                status,
                table);
        }

        /// <summary>Repaint the status column in place, so a mark shows without reloading the board.</summary>
        private static void Redraw(ListView table, ClashGroup group)
        {
            table.ItemsSource = group.Items.Take(DetailCap).Select(item => new TableRow(item,
                item.TestName,
                item.Level ?? "-",
                item.ModelPair,
                (item.CategoryA ?? "?") + " v " + (item.CategoryB ?? "?"),
                item.Status.ToString())).ToList();
        }

        /// <summary>
        /// Raising a coordination problem the clash engine cannot see.
        ///
        /// A door swinging into an escape route, a valve with nowhere to be pulled to, an access
        /// route that stops at a wall, a riser with no builder's work opening. None of them are
        /// intersections, all of them are coordination, and none of them can be raised in a tool
        /// whose board only knows what the engine said — so they end up in a spreadsheet beside the
        /// board, reported separately and closed where nobody can see them next to the work they
        /// belong with.
        ///
        /// The elements come from the current selection, which is where the person already is when
        /// they notice. Nothing else is required: a title is enough, and every other box has an
        /// answer already.
        /// </summary>
        private sealed class RaiseScreen
        {
            private readonly TextBox _title = Ui.Text(string.Empty, null, 260);
            private readonly TextBox _note = Ui.Text(string.Empty, null, 340);
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _list = Ui.Stack();

            private string? _party;
            private string _priority = "Medium";

            internal UIElement Build()
            {
                var session = Host.Current;
                if (session == null) return Ui.Stack();

                var parties = new List<string> { "Unassigned" };
                parties.AddRange(session.Profile.Parties);
                _party = parties[0];

                var who = Ui.Choice(parties, _party, p => _party = p);
                var how = Ui.Choice(new[] { "High", "Medium", "Low" }, _priority, p => _priority = p);

                Redraw(session);

                return Ui.Card("Raise an issue",
                    "For what the clash engine cannot see. It joins the board and behaves like "
                    + "everything else on it.",
                    Ui.Field("What is wrong", _title),
                    Ui.Field("More", _note),
                    Ui.Field("Who", who),
                    Ui.Field("Priority", how),
                    Ui.Across(Ui.Button("Raise it against the selection", () => Raise(session))),
                    Ui.Note("The elements come from what is selected in the model right now. Select the "
                            + "door and the corridor, not the whole level — a viewpoint that opens on "
                            + "half a building tells the reader nothing."),
                    _status,
                    _list);
            }

            private void Raise(Session session)
            {
                _status.Children.Clear();

                var title = _title.Text.Trim();

                if (title.Length == 0)
                {
                    _status.Children.Add(Ui.Problem("Say what is wrong first. One line is enough."));
                    return;
                }

                var selected = session.Model.SelectedKeys();

                if (selected.Count == 0)
                {
                    _status.Children.Add(Ui.Problem(
                        "Nothing is selected. An issue with no elements is a note nobody can find "
                        + "again, so CamelWorks will not make one."));
                    return;
                }

                // Two participants because that is what a board identity is made of. One selected
                // element pairs with itself, which is a real pair and a stable key.
                var first = selected[0];
                var second = selected.Count > 1 ? selected[1] : first;

                double x = 0, y = 0, z = 0;
                var counted = 0;

                foreach (var key in selected)
                {
                    var item = session.Model.Resolve(key);
                    if (item == null) continue;

                    var bounds = item.Bounds;
                    x += bounds.CentreX; y += bounds.CentreY; z += bounds.CentreZ;
                    counted++;
                }

                if (counted > 0) { x /= counted; y /= counted; z /= counted; }

                var issues = Issues(session);
                var scale = session.MetresPerUnit;

                // The key is made from the raw host-unit centroid, the way a clash key is, and then
                // kept forever. The metres go on the issue for everything that reads coordinates.
                var issue = new ManualIssue(issues.NextId(), ClashKey.Create(first, second, x, y, z), title)
                {
                    Note = _note.Text.Trim().Length == 0 ? null : _note.Text.Trim(),
                    AssignedTo = _party == "Unassigned" ? null : _party,
                    Priority = _priority,
                    Level = LevelOf(session, selected),
                    Author = session.Profile.Author,
                    RaisedTicks = DateTime.UtcNow.Ticks,
                    X = x * scale,
                    Y = y * scale,
                    Z = z * scale,
                };

                foreach (var key in selected.Take(IssueElementCap)) issue.Elements.Add(key);

                issues.Put(issue);
                SaveIssues(session, issues);

                if (issue.AssignedTo != null) Decide(session, issue.Title, issue.AssignedTo, issue.Priority);

                _status.Children.Add(session.Store.Save()
                    ? Ui.Line("Raised against " + Ui.Count(selected.Count, "element")
                              + ". Load the board to see it — it will be its own group, named after "
                              + "the title, and the rules will leave it alone.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                session.Record(ActivityKind.Regroup, "raised an issue: " + title);

                _title.Text = string.Empty;
                _note.Text = string.Empty;

                Redraw(session);
            }

            /// <summary>What is already raised, and a way to close or delete one.</summary>
            private void Redraw(Session session)
            {
                _list.Children.Clear();

                var issues = Issues(session);

                if (issues.Unreadable > 0)
                    _list.Children.Add(Ui.Note(Ui.Count(issues.Unreadable, "issue")
                        + " in the project file could not be read back. They are still in the file; "
                        + "CamelWorks has not deleted them."));

                if (issues.Issues.Count == 0) return;

                var open = issues.Issues.Count(i => i.Status != FindingStatus.Resolved);

                _list.Children.Add(Ui.Line(Ui.Count(open, "issue") + " open, "
                    + (issues.Issues.Count - open).ToString(CultureInfo.InvariantCulture)
                    + " closed and kept as a record.", 0.7));

                var rows = issues.Issues.Select(i => new TableRow(i,
                    i.Title,
                    i.AssignedTo ?? "unassigned",
                    i.Priority ?? string.Empty,
                    i.Status.ToString(),
                    i.RaisedTicks > 0
                        ? new DateTime(i.RaisedTicks).ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture)
                        : string.Empty)
                {
                    Tone = i.Status == FindingStatus.Resolved ? Tone.Good : Tone.Warn,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Issue", 230), ("Who", 110), ("Priority", 80), ("Status", 90), ("Raised", 70));

                ManualIssue? picked = null;
                table.OnPick(item => picked = item as ManualIssue);

                _list.Children.Add(table);

                _list.Children.Add(Ui.Across(
                    Ui.Button("Show me", () =>
                    {
                        if (picked == null || picked.Elements.Count == 0) return;

                        // Materialised once: the seam takes IReadOnlyList, which IList is not, and
                        // both calls want the same list rather than two copies of it.
                        var keys = picked.Elements.ToList();

                        session.Model.Select(keys);
                        session.View.ZoomTo(keys, 2.0);
                    }),
                    Ui.Button("Close it", () => Close(session, picked)),
                    Ui.Button("Delete it", () => Delete(session, picked))));
            }

            private void Close(Session session, ManualIssue? picked)
            {
                _status.Children.Clear();

                if (picked == null) { _status.Children.Add(Ui.Line("Pick one first.", 0.8)); return; }

                var issues = Issues(session);
                var issue = issues.Find(picked.Id);

                if (issue == null) return;

                issue.Status = FindingStatus.Resolved;
                SaveIssues(session, issues);

                _status.Children.Add(session.Store.Save()
                    ? Ui.Line("Closed. It stays in this list as a record and leaves the board.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                session.Record(ActivityKind.Regroup, "closed an issue: " + issue.Title);
                Redraw(session);
            }

            /// <summary>
            /// Delete rather than close, for the one raised by mistake.
            ///
            /// Closing keeps the record because the record is the evidence the work happened.
            /// Deleting exists for the typo, and says which of the two it is doing.
            /// </summary>
            private void Delete(Session session, ManualIssue? picked)
            {
                _status.Children.Clear();

                if (picked == null) { _status.Children.Add(Ui.Line("Pick one first.", 0.8)); return; }

                var issues = Issues(session);
                issues.Remove(picked.Id);
                SaveIssues(session, issues);

                _status.Children.Add(session.Store.Save()
                    ? Ui.Line("Deleted. Closing keeps the record; deleting is for the one raised by "
                              + "mistake, and this was the second.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                session.Record(ActivityKind.Regroup, "deleted an issue: " + picked.Title);
                Redraw(session);
            }

            /// <summary>
            /// The level of the first element that carries one.
            ///
            /// Read off the element rather than worked out from the height, because building the
            /// level set means a pass over the whole federation and raising an issue should not
            /// take a minute.
            /// </summary>
            private static string? LevelOf(Session session, IReadOnlyList<ElementKey> keys)
            {
                foreach (var key in keys.Take(IssueElementCap))
                {
                    var item = session.Model.Resolve(key);
                    if (item == null) continue;

                    var level = item.Property("Element", "Level") ?? item.Property("Item", "Layer");
                    if (!string.IsNullOrWhiteSpace(level)) return level!.Trim();
                }

                return null;
            }

            /// <summary>Record the party against the group the issue will become.</summary>
            private static void Decide(Session session, string group, string party, string? priority)
            {
                var section = session.Store.Section(ProjectStore.ClashSection);
                var decisions = section["decisions"];

                if (decisions.Kind != JsonKind.Object)
                {
                    decisions = JsonValue.Object();
                    section.Set("decisions", decisions);
                }

                var decision = JsonValue.Object().Set("party", JsonValue.String(party));
                if (priority != null) decision.Set("priority", JsonValue.String(priority));

                decisions.Set(group, decision);
            }
        }

        // ===================================================================================
        // Tests
        // ===================================================================================

        /// <summary>What the clash engine holds, and when each test last ran.</summary>
        public static UIElement Tests() => new TestsScreen().Build();

        /// <summary>
        /// The engine's tests, and a way to stop making them by hand.
        ///
        /// Building a discipline matrix in Clash Detective is an afternoon: Add Test, pick two
        /// selections, set a tolerance, name it, thirty-six times. It is not difficult and that is
        /// exactly the problem — it is long enough to be done once and then never redone when a
        /// model is added, which is how a matrix quietly stops covering the federation it was
        /// drawn for.
        /// </summary>
        private sealed class TestsScreen
        {
            private const string ByDiscipline = "Discipline";
            private const string ByModel = "Model";

            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _grid = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();

            /// <summary>
            /// Where a run reports, kept apart from where creating reports.
            ///
            /// Not tidiness: a Runner clears its own output panel when pressed, so a "Run them"
            /// button living in the panel it writes to would delete itself on the way in.
            /// </summary>
            private readonly StackPanel _runs = Ui.Stack();

            private readonly TextBox _pattern = Ui.Text("{a} vs {b}", null, 200);
            private readonly TextBox _tolerance = Ui.Text("0.010", null, 80);
            private readonly TextBox _folder = Ui.Text(string.Empty, null, 150);

            private string _rows = ByDiscipline;
            private string _columns = ByDiscipline;
            private bool _selfPairs;

            private Session? _session;
            private MatrixPlan? _plan;

            internal UIElement Build()
            {
                _session = Host.Current;

                if (_session == null)
                    return Ui.Scroll(Ui.Stack(Ui.Heading("Tests"), Ui.Sub(Host.NoModel)));

                if (!_session.Clash.IsAvailable)
                    return Ui.Scroll(Ui.Stack(
                        Ui.Heading("Tests"),
                        Ui.Note(_session.ClashProblem ?? "There is no clash engine in this Navisworks edition.")));

                Redraw();

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Tests"),
                    Ui.Sub("The clash engine's own tests. A test that has never run contributes nothing to "
                           + "the board, which is worth seeing before wondering where the clashes went."),
                    _list,
                    Matrix(),
                    _results,
                    _runs));
            }

            /// <summary>The tests as they stand, and the one action worth having beside them.</summary>
            private void Redraw()
            {
                _list.Children.Clear();

                var session = _session;
                if (session == null) return;

                var tests = session.Clash.Tests();

                if (tests.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("No clash tests in this document.",
                        "Build a matrix below, or set them up in Clash Detective. CamelWorks reads the "
                        + "results and turns them into work; it does not replace the engine that "
                        + "produces them."));
                    return;
                }

                var rows = tests.Select(t => new TableRow(t,
                    t.Name,
                    t.Folder ?? string.Empty,
                    t.ResultCount.ToString("N0", CultureInfo.InvariantCulture),
                    t.LastRunTicks > 0
                        ? new DateTime(t.LastRunTicks).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                        : "never run")
                {
                    Tone = t.LastRunTicks > 0 ? Tone.Plain : Tone.Warn,
                }).ToList();

                var stale = tests.Where(t => t.LastRunTicks <= 0).ToList();

                _list.Children.Add(Ui.Across(
                    Ui.Pill(Ui.Count(tests.Count, "test")),
                    Ui.Pill(Ui.Count(tests.Sum(t => t.ResultCount), "result")),
                    stale.Count > 0 ? Ui.Pill(stale.Count + " never run", Tone.Warn) : null));

                _list.Children.Add(Ui.Table(rows,
                    ("Test", 200), ("Folder", 120), ("Results", 90), ("Last run", 150)));

                if (stale.Count > 0 && session.Clash.CanEdit)
                    _list.Children.Add(Ui.Runner("Run the " + stale.Count + " that never have", _runs,
                        job => Run(stale, job)));
            }

            /// <summary>The builder.</summary>
            private UIElement Matrix()
            {
                var axes = new[] { ByDiscipline, ByModel };

                var rows = Ui.Choice(axes, _rows, pick => _rows = pick);
                var columns = Ui.Choice(axes, _columns, pick => _columns = pick);
                var diagonal = Ui.Check("Include each side against itself", _selfPairs, v => _selfPairs = v);

                var build = Ui.Runner("Show me the grid", _grid, job =>
                {
                    _plan = null;

                    var session = _session;
                    if (session == null) return;

                    var a = Sides(session, _rows);
                    var b = Sides(session, _columns);

                    if (a.Count == 0 || b.Count == 0)
                    {
                        _grid.Children.Add(Ui.Empty("Nothing to pair up.",
                            "No models are loaded, so there is nothing on either axis."));
                        return;
                    }

                    var plan = ClashMatrix.Build(a, b, new MatrixOptions
                    {
                        Tolerance = Metres(_tolerance.Text),
                        SelfPairs = _selfPairs,
                        NamePattern = _pattern.Text,
                        Existing = session.Clash.Tests().Select(t => t.Name).ToList(),
                    });

                    _plan = plan;
                    Show(plan);
                });

                return Ui.Card("Build a matrix",
                    "Two lists, every pairing between them, made in the engine in one go. Nothing has to "
                    + "be set up first: the disciplines come from what CamelWorks already worked out about "
                    + "the models that are loaded.",
                    Ui.Field("Rows", rows),
                    Ui.Field("Columns", columns),
                    Ui.Field("Name pattern", _pattern),
                    Ui.Field("Tolerance (m)", _tolerance),
                    Ui.Field("Put them in folder", _folder),
                    diagonal,
                    build,
                    _grid);
            }

            /// <summary>The grid, with a tick on every row that is going to be made.</summary>
            private void Show(MatrixPlan plan)
            {
                _grid.Children.Add(Ui.Line(plan.Explain(), 1, true));

                foreach (var test in plan.Tests)
                {
                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var proposed = test;

                    row.Children.Add(Ui.Check(string.Empty, test.IsSelected, v => proposed.IsSelected = v));
                    row.Children.Add(Ui.Line(test.Name, 1, true));
                    row.Children.Add(Ui.Line(test.A + "  x  " + test.B, 0.7));

                    if (test.Note != null) row.Children.Add(Ui.Pill(test.Note, Tone.Warn));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 8, 0);

                    _grid.Children.Add(row);
                }

                var session = _session;

                if (session != null && !session.Clash.CanEdit)
                {
                    _grid.Children.Add(Ui.Problem(
                        "This document will not take new clash tests — it is open read-only, or the host "
                        + "is holding its clash data. The grid above is still the right answer; it just "
                        + "cannot be created from here."));
                    return;
                }

                _grid.Children.Add(Ui.Runner("Create the ticked ones", _results, Create));
            }

            private void Create(Job job)
            {
                var session = _session;
                var plan = _plan;

                if (session == null || plan == null) return;

                var wanted = plan.Tests.Where(t => t.IsSelected).ToList();

                if (wanted.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing is ticked.",
                        "Every row was either already in the document or has an empty side. Tick the ones "
                        + "you want anyway, or change the axes."));
                    return;
                }

                var folder = _folder.Text.Trim();
                var made = new List<ClashTestInfo>();
                var refused = new List<string>();

                foreach (var test in wanted)
                {
                    if (!job.Step(made.Count, "tests made")) break;

                    var created = session.Clash.Create(test.ToSpec(folder.Length == 0 ? null : folder));

                    if (created != null) made.Add(created.Value);
                    else refused.Add(test.Name + " — " + (session.Clash.LastProblem ?? "the host refused it"));
                }

                _results.Children.Add(Ui.Line("Made " + Ui.Count(made.Count, "test") + ".", 1, true));

                // Named individually rather than counted. A matrix where four of thirty-six failed is
                // a matrix with four holes in it, and a count does not say which four.
                foreach (var problem in refused) _results.Children.Add(Ui.Problem(problem));

                if (made.Count > 0)
                {
                    session.Record(ActivityKind.Regroup,
                        "built a clash matrix: " + Ui.Count(made.Count, "test"));

                    _results.Children.Add(Ui.Runner("Run them", _runs, inner => Run(made, inner)));
                }

                Redraw();
            }

            /// <summary>
            /// Run tests one at a time, reporting as it goes.
            ///
            /// One at a time rather than the host's run-everything, because running everything also
            /// re-runs the tests somebody else is halfway through triaging, and a matrix build is
            /// not a reason to throw away their afternoon.
            /// </summary>
            private void Run(IReadOnlyList<ClashTestInfo> tests, Job job)
            {
                var session = _session;
                if (session == null) return;

                var ran = 0;
                var refused = new List<string>();

                foreach (var test in tests)
                {
                    job.Say("Running " + test.Name + "...");
                    if (!job.Step(ran, "tests run")) break;

                    if (session.Clash.Run(test.Id)) ran++;
                    else refused.Add(test.Name + " — " + (session.Clash.LastProblem ?? "the host refused it"));
                }

                _runs.Children.Add(Ui.Line("Ran " + Ui.Count(ran, "test") + ".", 1, true));

                foreach (var problem in refused) _runs.Children.Add(Ui.Problem(problem));

                if (ran > 0)
                {
                    _runs.Children.Add(Ui.Note("Load the board in Triage to see what they found. "
                        + "CamelWorks does not read results automatically — a run that takes ten minutes "
                        + "should not also decide when you look at it."));

                    session.Record(ActivityKind.Regroup, "ran " + Ui.Count(ran, "test"));
                }

                Redraw();
            }

            /// <summary>
            /// One axis, worked out from what is already known rather than from a form.
            ///
            /// The discipline of a model is something the profile already derived from its filename
            /// on the day it was loaded, so a discipline matrix needs nothing configured — which is
            /// the whole difference between a builder somebody uses and one they mean to set up
            /// properly one day.
            /// </summary>
            private static IReadOnlyList<MatrixSide> Sides(Session session, string axis)
            {
                var models = session.Model.Models;

                if (axis == ByModel)
                    return models
                        .Select(m => new MatrixSide(m.DisplayName, ClashSide.Model(m.Scope)))
                        .ToList();

                var byDiscipline = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var model in models)
                {
                    var discipline = session.Profile.DisciplineOf(model.Scope) ?? "Unassigned";

                    if (!byDiscipline.TryGetValue(discipline, out var scopes))
                        byDiscipline[discipline] = scopes = new List<string>();

                    scopes.Add(model.Scope);
                }

                return byDiscipline
                    .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new MatrixSide(d.Key, ClashSide.Models(d.Value)))
                    .ToList();
            }

            /// <summary>Read the tolerance box, falling back rather than refusing.</summary>
            private static double Metres(string? text) =>
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value >= 0
                    ? value
                    : MatrixOptions.DefaultTolerance;
        }

        // ===================================================================================
        // Rules
        // ===================================================================================

        /// <summary>Suppress and flag, then group, then assign — one pipeline, saved with the project.</summary>
        public static UIElement Rules() => new RulesScreen().Build();

        private sealed class RulesScreen
        {
            private ClashRuleSet _rules = new ClashRuleSet();
            private readonly StackPanel _filters = Ui.Stack();
            private readonly StackPanel _grouping = Ui.Stack();
            private readonly StackPanel _assigns = Ui.Stack();
            private readonly StackPanel _status = Ui.Stack();

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Ui.Scroll(Ui.Stack(Ui.Heading("Rules"), Ui.Sub(Host.NoModel)));

                _rules = ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);

                Redraw(session);

                var save = Ui.Button("Save rules", () =>
                {
                    _status.Children.Clear();

                    session.Store.Section(ProjectStore.ClashSection).Set("rules", _rules.ToJson());

                    _status.Children.Add(session.Store.Save()
                        ? Ui.Line("Saved. The board uses these the next time it loads.", 0.8)
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
                });

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Rules"),
                    Ui.Sub("Suppress and flag first, then group, then assign. The order is fixed because it is "
                           + "the only one that makes sense: assigning a group is only meaningful once the "
                           + "group exists, and grouping noise wastes the grouping."),
                    Ui.Note("With nothing here, the board still works: it groups by model pair, then level, "
                            + "then proximity, and assigns nothing. These rules are how you narrow that, not "
                            + "how you switch it on."),
                    Ui.Card("Suppress and flag", "Applied to every result before anything else.", _filters),
                    Ui.Card("Group", "Outermost rule first. Empty means the built-in stack.", _grouping),
                    Ui.Card("Assign", "First match wins.", _assigns),
                    Ui.Across(save),
                    _status));
            }

            private void Redraw(Session session)
            {
                _filters.Children.Clear();
                _grouping.Children.Clear();
                _assigns.Children.Clear();

                foreach (var filter in _rules.Filters.ToList())
                {
                    var row = new WrapPanel();
                    row.Children.Add(Ui.Check(string.Empty, filter.IsEnabled, v => filter.IsEnabled = v));
                    row.Children.Add(Ui.Pill(filter.Suppress ? "suppress" : "flag",
                        filter.Suppress ? Tone.Warn : Tone.Plain));
                    row.Children.Add(Ui.Line(filter.Name, 1, true));
                    row.Children.Add(Ui.Line(filter.When.Describe(), 0.75));

                    var drop = Ui.Button("remove", () => { _rules.Filters.Remove(filter); Redraw(session); });
                    drop.MinWidth = 60;
                    row.Children.Add(drop);

                    Space(row);
                    _filters.Children.Add(row);
                }

                _filters.Children.Add(NewFilter(session));

                foreach (var group in _rules.Grouping.ToList())
                {
                    var row = new WrapPanel();
                    row.Children.Add(Ui.Line(group.Describe(), 1));

                    var drop = Ui.Button("remove", () => { _rules.Grouping.Remove(group); Redraw(session); });
                    drop.MinWidth = 60;
                    row.Children.Add(drop);

                    Space(row);
                    _grouping.Children.Add(row);
                }

                _grouping.Children.Add(NewGrouping(session));

                foreach (var assign in _rules.Assigns.ToList())
                {
                    var row = new WrapPanel();
                    row.Children.Add(Ui.Check(string.Empty, assign.IsEnabled, v => assign.IsEnabled = v));
                    row.Children.Add(Ui.Line(assign.Party, 1, true));
                    row.Children.Add(Ui.Line("gets " + assign.When.Describe(), 0.75));

                    var drop = Ui.Button("remove", () => { _rules.Assigns.Remove(assign); Redraw(session); });
                    drop.MinWidth = 60;
                    row.Children.Add(drop);

                    Space(row);
                    _assigns.Children.Add(row);
                }

                _assigns.Children.Add(NewAssign(session));
            }

            private static void Space(Panel row)
            {
                foreach (var child in row.Children.OfType<FrameworkElement>())
                    child.Margin = new Thickness(0, 2, 7, 2);
            }

            private UIElement NewFilter(Session session)
            {
                var name = Ui.Text("New rule", null, 140);
                var suppress = Ui.Choice(new[] { "suppress", "flag" }, "suppress", null, 100);
                var editor = new SpecEditor(PredicateSpec.Kinds);

                var add = Ui.Button("Add", () =>
                {
                    var spec = editor.Spec();
                    if (spec == null) return;

                    _rules.Filters.Add(new FilterSpec(
                        name.Text.Trim().Length == 0 ? spec.Describe() : name.Text.Trim(),
                        spec,
                        (suppress.SelectedItem as string) != "flag"));

                    Redraw(session);
                });

                return Ui.Stack(Ui.Across(name, suppress), editor.View, Ui.Across(add));
            }

            private UIElement NewGrouping(Session session)
            {
                var editor = new SpecEditor(GroupingSpec.Kinds);

                var add = Ui.Button("Add", () =>
                {
                    var kind = editor.Kind();
                    if (kind == null) return;

                    _rules.Grouping.Add(new GroupingSpec(kind.Id)
                    {
                        A = editor.Number(),
                        Name = editor.Name(),
                    });

                    Redraw(session);
                });

                return Ui.Stack(editor.View, Ui.Across(add));
            }

            private UIElement NewAssign(Session session)
            {
                var parties = session.Profile.Parties.ToList();
                if (parties.Count == 0) parties.Add("Unassigned");

                var party = parties[0];
                var picker = Ui.Choice(parties, party, p => party = p);
                var editor = new SpecEditor(PredicateSpec.Kinds);

                var add = Ui.Button("Add", () =>
                {
                    var spec = editor.Spec();
                    if (spec == null) return;

                    _rules.Assigns.Add(new AssignSpec(spec, party));
                    Redraw(session);
                });

                return Ui.Stack(Ui.Across(picker), editor.View, Ui.Across(add));
            }
        }

        /// <summary>
        /// One editor that can draw every rule, because every rule says what it needs.
        ///
        /// Without this each of the thirteen predicates and ten grouping rules would need its own
        /// row of controls, and adding a rule to Core would mean remembering to add one here.
        /// </summary>
        private sealed class SpecEditor
        {
            private readonly ComboBox _kind;
            private readonly TextBox _first = Ui.Text(string.Empty, null, 110);
            private readonly TextBox _second = Ui.Text(string.Empty, null, 110);
            private readonly TextBlock _hint = Ui.Line(string.Empty, 0.6);
            private readonly IReadOnlyList<SpecKind> _kinds;

            internal SpecEditor(IReadOnlyList<SpecKind> kinds)
            {
                _kinds = kinds;

                _kind = Ui.Choice(kinds.Select(k => k.Title), kinds[0].Title, _ => Update(), 190);
                Update();

                View = Ui.Across(_kind, _first, _second, _hint);
            }

            internal UIElement View { get; }

            internal SpecKind? Kind()
            {
                var title = _kind.SelectedItem as string;
                return _kinds.FirstOrDefault(k => string.Equals(k.Title, title, StringComparison.Ordinal));
            }

            internal double Number() =>
                double.TryParse(_first.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

            internal string? Name() => _first.Text.Trim().Length == 0 ? null : _first.Text.Trim();

            internal PredicateSpec? Spec()
            {
                var kind = Kind();
                if (kind == null) return null;

                var spec = new PredicateSpec(kind.Id);

                switch (kind.Input)
                {
                    case SpecInput.Number:
                        spec.A = Number();
                        break;

                    case SpecInput.Range:
                        spec.A = Number();
                        spec.B = double.TryParse(_second.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                                 out var b) ? b : 0;
                        break;

                    case SpecInput.Text:
                        spec.Name = Name();
                        break;

                    case SpecInput.NameAndValue:
                        spec.Name = Name();
                        spec.Value = _second.Text;
                        break;
                }

                return spec;
            }

            private void Update()
            {
                var kind = Kind();
                if (kind == null) return;

                _hint.Text = kind.Hint;
                _first.Visibility = kind.Input == SpecInput.None || kind.Input == SpecInput.Nested
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                _second.Visibility = kind.Input == SpecInput.Range || kind.Input == SpecInput.NameAndValue
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        // ===================================================================================
        // Report
        // ===================================================================================

        /// <summary>PDF, XLSX or HTML from the board, with nothing to configure first.</summary>
        public static UIElement Report() => new ReportScreen().Build();

        /// <summary>
        /// The report, and who it is for.
        ///
        /// A single report of the whole board is the one nobody sends. What actually leaves the
        /// office on a Friday is one list per subcontractor, holding their groups and nothing else,
        /// and producing those by hand from a full report is an afternoon of filtering and saving.
        /// So the audience is part of writing it, and "one file each" is a checkbox rather than a
        /// morning.
        /// </summary>
        private sealed class ReportScreen
        {
            /// <summary>The audience that means everybody.</summary>
            private const string Everyone = "Everyone";

            /// <summary>The audience that means the groups nobody owns yet.</summary>
            private const string Nobody = "Only the unassigned";

            private readonly StackPanel _results = Ui.Stack();
            private readonly TextBox _path = Ui.Text(string.Empty, null, 340);

            private string _format = "PDF";
            private string _audience = Everyone;
            private bool _changedOnly;
            private bool _pictures;
            private bool _perParty;

            internal UIElement Build()
            {
                var session = Host.Current;

                var parties = new List<string> { Everyone, Nobody };
                if (session != null) parties.AddRange(session.Profile.Parties);

                var format = Ui.Choice(new[] { "PDF", "XLSX", "HTML" }, _format, f => _format = f);
                var audience = Ui.Choice(parties, _audience, a => _audience = a);

                var changed = Ui.Check("Only what the last run had not seen", _changedOnly, v => _changedOnly = v);
                var pictures = Ui.Check("A picture of each group", _pictures, v => _pictures = v);
                var split = Ui.Check("One file per party, not one file", _perParty, v => _perParty = v);

                var write = Ui.Runner("Write the report", _results, Write);

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Report"),
                    Ui.Sub("Of the board as it stands. Nothing to lay out first — the template is built in, "
                           + "and the numbers on the cover are the funnel's, so the report and the screen "
                           + "agree."),
                    Ui.Field("Format", format),
                    Ui.Field("For", audience),
                    Ui.Field("Write to", _path),
                    changed,
                    pictures,
                    split,
                    Ui.Note("A picture moves your view while it runs and puts it back afterwards. On a "
                            + "board of two hundred groups that is two hundred renders, so it is off "
                            + "until you ask for it."),
                    write,
                    _results));
            }

            private void Write(Job job)
            {
                var session = Host.Current;
                if (session == null) { _results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var board = LastBoard;

                if (board == null)
                {
                    var notes = new List<string>();

                    // Qualified: this class has its own Build(), and an unqualified call binds to
                    // it rather than to the board builder.
                    board = CoordinateViews.Build(session, job, notes);

                    foreach (var note in notes) _results.Children.Add(Ui.Note(note));
                }

                if (board == null) return;

                var regressed = Regressed();

                if (_perParty)
                {
                    WriteEach(session, board, regressed, job);
                    return;
                }

                var groups = Chosen(board, _audience, regressed);

                if (groups.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing to report.",
                        "No group on the board matches that filter. That is a real answer — it is what "
                        + "an empty week looks like — but check the audience if you were expecting one."));
                    return;
                }

                var file = Where(session, _audience);

                job.Say("Building the report...");
                var document = Document(session, board, groups, _audience, regressed, Pictures(session, groups, job));

                job.Say("Writing...");
                Emit(file, document, groups);

                _results.Children.Add(Ui.Line("Wrote " + file, 1, true));
                session.Record(ActivityKind.Report, "wrote a " + _format + " clash report", file);
            }

            /// <summary>
            /// One file per party, which is what a coordination meeting actually ends with.
            ///
            /// A party with nothing on the board gets no file rather than an empty one. Sending
            /// somebody a report that says nothing is how a report stops being read.
            /// </summary>
            private void WriteEach(Session session, ClashPipelineResult board,
                                   ICollection<ClashItem> regressed, Job job)
            {
                var parties = board.Groups
                    .Select(g => g.AssignedTo)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var unassigned = Chosen(board, Nobody, regressed);
                if (unassigned.Count > 0) parties.Add(Nobody);

                if (parties.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nobody to write to.",
                        "No group on the board is assigned, and there are no unassigned ones either."));
                    return;
                }

                var written = 0;

                foreach (var party in parties)
                {
                    if (!job.Step(written, "files written")) break;

                    var groups = Chosen(board, party, regressed);
                    if (groups.Count == 0) continue;

                    var file = Where(session, party);

                    job.Say("Writing " + party + "...");

                    var document = Document(session, board, groups, party, regressed,
                                            Pictures(session, groups, job));

                    Emit(file, document, groups);

                    _results.Children.Add(Ui.Line(party + " - " + Ui.Count(groups.Count, "group")
                        + " - " + file, 0.8));

                    written++;
                }

                _results.Children.Add(Ui.Line("Wrote " + Ui.Count(written, "file") + ".", 1, true));

                session.Record(ActivityKind.Report,
                    "wrote " + Ui.Count(written, "clash report") + ", one per party");
            }

            /// <summary>Which groups belong in a report for this audience.</summary>
            private IReadOnlyList<ClashGroup> Chosen(ClashPipelineResult board, string audience,
                                                     ICollection<ClashItem> regressed)
            {
                var groups = board.Groups.Where(g =>
                    audience == Everyone
                    || (audience == Nobody && g.AssignedTo == null)
                    || string.Equals(g.AssignedTo, audience, StringComparison.OrdinalIgnoreCase));

                if (_changedOnly) groups = groups.Where(g => Changed(g, regressed));

                return groups.ToList();
            }

            /// <summary>Where the file goes, made from the audience so two reports never collide.</summary>
            private string Where(Session session, string audience)
            {
                var typed = _path.Text.Trim();
                var extension = "." + _format.ToLowerInvariant();

                var stem = session.Profile.ProjectName + " clash report"
                         + (audience == Everyone ? string.Empty : " - " + Safe(audience));

                if (typed.Length == 0)
                {
                    var made = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        stem + extension);

                    if (!_perParty) _path.Text = made;
                    return made;
                }

                if (!_perParty) return typed;

                // One box, several files: the typed path becomes the folder and the stem, and each
                // party's name is added. Otherwise the last party written would be the only file
                // left on disk, which is the kind of thing somebody discovers on Monday.
                //
                // System.IO.Path here rather than Core's separator-agnostic helper: this assembly
                // only ever runs on Windows, where Path is the one that is right about backslashes.
                var directory = System.IO.Path.GetDirectoryName(typed) ?? string.Empty;
                var name = System.IO.Path.GetFileNameWithoutExtension(typed);

                return System.IO.Path.Combine(directory, name + " - " + Safe(audience) + extension);
            }

            /// <summary>A party name that is safe in a filename.</summary>
            private static string Safe(string text)
            {
                var clean = new char[text.Length];
                var at = 0;

                foreach (var c in text)
                    clean[at++] = c == '/' || c == '\\' || c == ':' || c == '*' || c == '?'
                                  || c == '"' || c == '<' || c == '>' || c == '|' ? '-' : c;

                return new string(clean, 0, at).Trim();
            }

            /// <summary>
            /// One rendered view per group, or nothing when they were not asked for.
            ///
            /// The camera is borrowed once for the whole run and put back at the end. Doing it per
            /// group would still be correct and would leave the view visibly jumping back and forth
            /// for as long as the report takes.
            /// </summary>
            private IReadOnlyDictionary<string, byte[]> Pictures(Session session,
                                                                 IReadOnlyList<ClashGroup> groups, Job job)
            {
                var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);

                if (!_pictures) return images;

                using (session.View.BorrowCamera())
                {
                    var done = 0;

                    foreach (var group in groups)
                    {
                        if (!job.Step(done++, "views rendered")) break;

                        job.Say("Rendering " + group.Name + "...");

                        var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();
                        if (points.Count == 0) continue;

                        session.View.FrameBox(BoxOf(points));

                        var png = session.View.Capture(1280, 720);
                        if (png != null && png.Length > 0) images[group.Name] = png;
                    }
                }

                return images;
            }

            private void Emit(string file, ReportDocument document, IReadOnlyList<ClashGroup> groups)
            {
                switch (_format)
                {
                    case "XLSX":
                        File.WriteAllBytes(file, XlsxWriter.Write("Clashes", Table(groups)));
                        break;

                    case "HTML":
                        File.WriteAllText(file, HtmlReportWriter.Write(document));
                        break;

                    default:
                    {
                        using (var stream = File.Create(file)) PdfReportWriter.Write(stream, document);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The report itself: the funnel, what changed, the groups and every clash.
        ///
        /// The funnel is always of the whole board even when the report is of one party's share of
        /// it, and says so. A report that reproduced the funnel over a filtered subset would show a
        /// subcontractor a number that does not match the coordinator's screen, and the argument
        /// that follows is about the tool rather than the building.
        /// </summary>
        private static ReportDocument Document(Session session, ClashPipelineResult board,
                                               IReadOnlyList<ClashGroup> groups, string audience,
                                               ICollection<ClashItem> regressed,
                                               IReadOnlyDictionary<string, byte[]> images)
        {
            var whole = groups.Count == board.Groups.Count;

            var document = new ReportDocument(session.Profile.ProjectName + " — clash report")
            {
                Subtitle = whole ? "Produced by CamelWorks" : "For " + audience + ", produced by CamelWorks",
            };

            document.Fact("Project", session.Profile.ProjectName);
            document.Fact("Author", session.Profile.Author);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            if (!whole) document.Fact("For", audience);
            document.Fact("From the engine", board.Funnel.Input.ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("In this report", groups.Sum(g => g.Items.Count).ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("Groups", groups.Count.ToString("N0", CultureInfo.InvariantCulture));

            if (whole)
                document.Fact("Unassigned", board.Funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture));

            document.Heading("How this list was produced");
            document.Paragraph(board.Funnel.ToString());

            document.Paragraph(
                "Every result the engine produced is accounted for above: what was removed, by which rule, "
                + "and what remains. A report that quietly showed a smaller number than the engine found "
                + "would be worse than no report.");

            if (!whole)
                document.Paragraph(
                    "Those figures are of the whole board. This report covers " + audience.ToLowerInvariant()
                    + " — " + Ui.Count(groups.Count, "group") + " of "
                    + board.Groups.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + " — so that the numbers here and on the coordinator's screen are the same numbers.");

            Since(document, groups, regressed);

            document.Heading("Groups");
            document.Add(Summary(groups));

            if (images.Count > 0) Pictures(document, groups, images);

            document.PageBreak();
            document.Heading("Every clash");
            document.Add(Table(groups));

            return document;
        }

        /// <summary>
        /// What changed since the last run, when there is a last run to compare against.
        ///
        /// First after the funnel, because it is the first question of every coordination meeting
        /// and the one a reader should not have to derive by comparing two PDFs.
        /// </summary>
        private static void Since(ReportDocument document, IReadOnlyList<ClashGroup> groups,
                                  ICollection<ClashItem> regressed)
        {
            var delta = LastDelta;
            if (delta == null) return;

            var fresh = groups.Sum(g => g.Items.Count(i => i.Status == FindingStatus.New));
            var back = groups.Sum(g => g.Items.Count(i => regressed.Contains(i)));

            document.Heading("Since the last run");

            document.Paragraph(fresh.ToString("N0", CultureInfo.InvariantCulture) + " new, "
                + back.ToString("N0", CultureInfo.InvariantCulture) + " back again, "
                + delta.Resolved.Count.ToString("N0", CultureInfo.InvariantCulture)
                + " gone from the model since the previous run.");

            if (back > 0)
                document.Paragraph(
                    "Something recorded as resolved is in this list again. Either a fix was undone or a "
                    + "model went backwards, and it is the first thing to look at — native clash detection "
                    + "cannot tell you this at all, because the result simply reappears looking new.");

            if (delta.WeakMatches > 0)
                document.Paragraph(delta.WeakMatches.ToString("N0", CultureInfo.InvariantCulture)
                    + " of these matched the previous run only on a geometry-derived key, because the "
                    + "elements involved carry no stable identity. Their history is a best guess.");
        }

        /// <summary>A rendered view of each group that produced one, with its own heading.</summary>
        private static void Pictures(ReportDocument document, IReadOnlyList<ClashGroup> groups,
                                     IReadOnlyDictionary<string, byte[]> images)
        {
            document.PageBreak();
            document.Heading("Where they are");

            foreach (var group in groups)
            {
                if (!images.TryGetValue(group.Name, out var png)) continue;

                document.Heading(group.Name, 2);
                document.Add(new ImageBlock(png,
                    Ui.Count(group.Items.Count, "clash") + " - " + (group.AssignedTo ?? "unassigned")));
            }
        }

        private static TableBlock Summary(IReadOnlyList<ClashGroup> groups)
        {
            var table = new TableBlock("Group", "Clashes", "Assigned to", "Priority");

            foreach (var group in groups)
                table.Row(group.Name,
                    group.Items.Count.ToString("N0", CultureInfo.InvariantCulture),
                    group.AssignedTo ?? "unassigned",
                    group.Priority ?? string.Empty);

            return table;
        }

        private static TableBlock Table(IReadOnlyList<ClashGroup> groups)
        {
            var table = new TableBlock("Group", "Test", "Level", "Models", "What", "Status", "X", "Y", "Z");

            foreach (var group in groups)
                foreach (var item in group.Items)
                    table.Row(group.Name,
                        item.TestName,
                        item.Level ?? string.Empty,
                        item.ModelPair,
                        (item.CategoryA ?? "?") + " v " + (item.CategoryB ?? "?"),
                        item.Status.ToString(),
                        item.X.ToString("0.00", CultureInfo.InvariantCulture),
                        item.Y.ToString("0.00", CultureInfo.InvariantCulture),
                        item.Z.ToString("0.00", CultureInfo.InvariantCulture));

            return table;
        }

        // ===================================================================================
        // BCF
        // ===================================================================================

        /// <summary>Export the board as BCF, or read somebody else's back in.</summary>
        public static UIElement Bcf()
        {
            var results = Ui.Stack();
            var path = Ui.Text(string.Empty, null, 340);
            var version = "2.1";
            var picker = Ui.Choice(new[] { "2.1", "3.0" }, version, v => version = v);

            var snapshots = true;
            var snapshotBox = Ui.Check("Include a snapshot of each group", snapshots, v => snapshots = v);

            var export = Ui.Runner("Export the board", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var board = LastBoard;

                if (board == null)
                {
                    var notes = new List<string>();
                    board = Build(session, job, notes);
                    foreach (var note in notes) results.Children.Add(Ui.Note(note));
                }

                if (board == null) return;

                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    file = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        session.Profile.ProjectName + ".bcfzip");

                    path.Text = file;
                }

                job.Say("Building topics...");
                var topics = Topics(session, board, snapshots, job);

                job.Say("Writing...");

                using (var stream = File.Create(file))
                {
                    var result = BcfWriter.Write(stream, topics,
                        version == "3.0" ? BcfVersion.V30 : BcfVersion.V21,
                        new BcfProject(session.Profile.ProjectName, session.Profile.ProjectName));

                    results.Children.Add(Ui.Line(result.ToString(), 1, true));

                    foreach (var note in result.Notes) results.Children.Add(Ui.Note(note));

                    if (result.UnresolvableComponents > 0)
                        results.Children.Add(Ui.Note(
                            Ui.Count(result.UnresolvableComponents, "element")
                            + " could not be named by an IFC GlobalId, because the model they came from "
                            + "did not carry one. They are written with CamelWorks' own identity instead, "
                            + "so the topic still says which elements it means — a receiving tool just "
                            + "cannot select them for you."));

                    if (snapshots && result.Snapshots == 0 && result.Viewpoints > 0)
                        results.Children.Add(Ui.Note(
                            "Navisworks would not render the snapshots, so the file carries cameras "
                            + "without images. Every viewpoint still opens on its clash."));
                }

                RememberSent(session, board);
                session.Store.Save();

                results.Children.Add(Ui.Line("Wrote " + file, 0.8));
                session.Record(ActivityKind.Exchange, "exported BCF " + version, file);
            });

            var import = Ui.Runner("Read a BCF file", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var file = path.Text.Trim();

                if (!File.Exists(file))
                {
                    results.Children.Add(Ui.Problem("There is no file at " + file + "."));
                    return;
                }

                using (var stream = File.OpenRead(file))
                {
                    var read = BcfReader.Read(stream);

                    results.Children.Add(Ui.Line(read.ToString(), 1, true));

                    foreach (var warning in read.Warnings) results.Children.Add(Ui.Note(warning));

                    if (read.Topics.Count == 0)
                    {
                        results.Children.Add(Ui.Empty("No topics in that file.",
                            "It read as a BCF archive but carried nothing."));
                        return;
                    }

                    // The conflict preview. A BCF file that arrives twice — because somebody
                    // re-sent it, or because two people exported the same board — must not read as
                    // a fresh set of issues, and the only thing that can tell them apart is the
                    // topic GUID the schema requires.
                    var known = Known(session);
                    var fresh = read.Topics.Count(t => !known.Contains(t.Guid));

                    results.Children.Add(Ui.Across(
                        Ui.Pill(fresh + " new", fresh > 0 ? Tone.Good : Tone.Plain),
                        Ui.Pill((read.Topics.Count - fresh) + " already seen")));

                    var rows = read.Topics.Select(t => new TableRow(t,
                        known.Contains(t.Guid) ? "seen" : "new",
                        t.Title,
                        t.TopicStatus,
                        t.AssignedTo ?? "-",
                        t.CreationAuthor,
                        Ui.Count(t.Comments.Count, "comment"))
                    {
                        Tone = known.Contains(t.Guid) ? Tone.Plain : Tone.Good,
                    }).ToList();

                    results.Children.Add(Ui.Table(rows,
                        ("", 50), ("Topic", 220), ("Status", 80), ("Assigned to", 110),
                        ("Author", 120), ("", 0)));

                    Remember(session, read.Topics.Select(t => t.Guid));

                    results.Children.Add(Ui.Note("CamelWorks has recorded which topics it has seen, so the "
                        + "same file read again reports nothing new. It does not write them into the model: "
                        + "somebody else's issue list is theirs until you decide what to do with it."));

                    Reply(session, read.Topics, results);
                }

                session.Record(ActivityKind.Exchange, "read a BCF file", file);
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("BCF"),
                Ui.Sub("One neutral model, both versions. 2.1 is what most tools still read; 3.0 keeps the "
                       + "camera's aspect ratio, which 2.1 has nowhere to put."),
                Ui.Field("Version", picker),
                Ui.Field("File", path),
                snapshotBox,
                Ui.Note("A topic with no viewpoint is a sentence about a problem somewhere in a "
                        + "federation. Every topic CamelWorks writes carries a camera and the elements "
                        + "involved; the snapshot is the part that moves your view while it runs, and "
                        + "your camera is put back afterwards."),
                Ui.Across(export, import),
                results));
        }

        /// <summary>Topic GUIDs this project has already been shown.</summary>
        /// <summary>
        /// The half of BCF that makes it worth having: what a returned file says about this board.
        ///
        /// An issue that goes out and never comes back is a spreadsheet with extra steps. A reply
        /// carries somebody else's opinion of statuses this board already holds, and the whole
        /// difficulty is reconciling the two without either side quietly losing a decision.
        ///
        /// Nothing is applied until somebody presses the button, and what the button would do is on
        /// the screen above it, row by row.
        /// </summary>
        private static void Reply(Session session, IReadOnlyList<BcfTopic> topics, Panel results)
        {
            var board = LastBoard;

            if (board == null)
            {
                results.Children.Add(Ui.Note("Load the board in Triage and read this file again to see "
                    + "what it says about it. Without a board there is nothing to reconcile against."));
                return;
            }

            // The record of what was sent, with the current board laid over it: the record survives
            // a re-run that renamed a group, and the board is right about what it is called today.
            var sent = Sent(session);
            foreach (var group in board.Groups) sent[BcfGuid.For(group.Id)] = group.Name;

            var current = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);

            foreach (var group in board.Groups)
            {
                // The least-progressed member. A group is only resolved when everything in it is,
                // and calling it resolved because one row was would close work that is still open.
                current[group.Name] = group.Items.Count == 0
                    ? FindingStatus.Active
                    : group.Items.Min(i => i.Status);
            }

            var report = BcfResponse.Read(topics, sent, current);

            if (report.Matched.Count == 0)
            {
                results.Children.Add(Ui.Note("None of these topics came from this project, so there is "
                    + "nothing on the board to reconcile. Read them as somebody else's list."));
                return;
            }

            results.Children.Add(Ui.Line("What this reply says about the board", 1, true));
            results.Children.Add(Ui.Line(report.Explain(), 0.8));

            var rows = report.Matched.Select(m => new TableRow(m,
                m.GroupName,
                m.Topic.TopicStatus,
                FindingStatusLattice.ToName(m.Current),
                m.Changes ? FindingStatusLattice.ToName(m.Merged) : "-",
                m.UnknownStatus != null ? "not a status we know"
                    : m.Refused ? "kept where it is"
                    : m.IsReopen && m.Changes ? "re-opened"
                    : string.Empty)
            {
                Tone = m.UnknownStatus != null ? Tone.Warn
                     : m.IsReopen && m.Changes ? Tone.Bad
                     : m.Changes ? Tone.Good
                     : Tone.Plain,
            }).ToList();

            results.Children.Add(Ui.Table(rows,
                ("Group", 200), ("The file says", 120), ("The board says", 110),
                ("Would become", 110), ("", 0)));

            if (report.Refused > 0)
                results.Children.Add(Ui.Note(Ui.Count(report.Refused, "topic")
                    + " asked for a status lower than the board holds and will stay where they are. "
                    + "The file was exported before those decisions were made, and a stale \"Open\" "
                    + "must not undo an approval nobody has disputed. A deliberate re-open is "
                    + "different and is honoured."));

            if (report.Unknown > 0)
                results.Children.Add(Ui.Note(Ui.Count(report.Unknown, "topic")
                    + " carry a status word CamelWorks does not recognise. BCF lets every project "
                    + "define its own, so those are shown and left alone rather than mapped onto a "
                    + "guess."));

            if (report.Changes == 0)
            {
                results.Children.Add(Ui.Empty("Nothing to apply.",
                    "The board already agrees with everything in this file that CamelWorks can act on."));
                return;
            }

            results.Children.Add(Ui.Button("Apply " + Ui.Count(report.Changes, "change") + " to the board",
                () => Apply(session, board, report, results)));
        }

        /// <summary>Write the reply into the board, one group at a time.</summary>
        private static void Apply(Session session, ClashPipelineResult board,
                                  BcfResponseReport report, Panel results)
        {
            var moved = 0;
            var groups = 0;

            foreach (var response in report.Matched.Where(m => m.Changes))
            {
                var group = board.Groups.FirstOrDefault(
                    g => string.Equals(g.Name, response.GroupName, StringComparison.Ordinal));

                if (group == null) continue;

                groups++;

                foreach (var item in group.Items)
                {
                    // Per item, and promote-only unless the file re-opened it. Applying the group's
                    // merged status flat would demote the rows somebody had already taken further.
                    var next = response.IsReopen
                        ? response.Merged
                        : FindingStatusLattice.Merge(item.Status, response.Merged);

                    if (next == item.Status) continue;

                    item.Status = next;
                    moved++;
                }

                if (response.AssignedTo != null && group.AssignedTo == null)
                    group.AssignByHand(response.AssignedTo);
            }

            Save(session, board, "applied a BCF reply to " + Ui.Count(groups, "group"));

            results.Children.Add(session.Store.Save()
                ? Ui.Line("Applied to " + Ui.Count(groups, "group") + ", "
                          + Ui.Count(moved, "result") + " moved. Load the board again to see it.", 1, true)
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            results.Children.Add(Ui.Note("The board changed, not the model. Undo is on the group panel "
                + "in Triage, and it puts back the board as it was before this."));

            session.Record(ActivityKind.Exchange,
                "applied a BCF reply: " + Ui.Count(groups, "group") + " moved");
        }

        private static HashSet<string> Known(Session session)
        {
            var saved = session.Store.Section(ProjectStore.ClashSection)["bcfTopics"];

            return new HashSet<string>(
                saved.Items.Select(i => i.AsString() ?? string.Empty).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static void Remember(Session session, IEnumerable<string> guids)
        {
            var known = Known(session);
            foreach (var guid in guids) known.Add(guid);

            session.Store.Section(ProjectStore.ClashSection)
                   .Set("bcfTopics", JsonValue.Array(known.OrderBy(g => g, StringComparer.Ordinal)
                                                          .Select(JsonValue.String)));

            session.Store.Save();
        }

        /// <summary>
        /// Which board group each topic this project has exported came from.
        ///
        /// Written at export and read when a file comes back. The topic GUID is derived from the
        /// group id, so it could be recomputed from the board — but only while the board still
        /// holds that group under that id, and a reply usually arrives after a re-run has moved
        /// things. The record is what makes a reply readable a fortnight later.
        /// </summary>
        private static Dictionary<string, string> Sent(Session session)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var saved = session.Store.Section(ProjectStore.ClashSection)["bcfGroups"];

            foreach (var guid in saved.Keys)
            {
                var group = saved[guid].AsString();
                if (!string.IsNullOrEmpty(group)) map[guid] = group!;
            }

            return map;
        }

        /// <summary>Record what this export sent, so the reply can be matched to it.</summary>
        private static void RememberSent(Session session, ClashPipelineResult board)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var saved = section["bcfGroups"];

            if (saved.Kind != JsonKind.Object)
            {
                saved = JsonValue.Object();
                section.Set("bcfGroups", saved);
            }

            foreach (var group in board.Groups)
                saved.Set(BcfGuid.For(group.Id), JsonValue.String(group.Name));
        }

        /// <summary>How many components one viewpoint will name before it stops.</summary>
        private const int ComponentCap = 400;

        /// <summary>How big a snapshot is. Big enough to read, small enough that a hundred fit in a file.</summary>
        private const int SnapshotWidth = 1280;

        /// <summary>How big a snapshot is.</summary>
        private const int SnapshotHeight = 720;

        private static IReadOnlyList<BcfTopic> Topics(Session session, ClashPipelineResult board,
                                                      bool snapshots, Job job)
        {
            var topics = new List<BcfTopic>();
            var author = session.Profile.Author;
            var now = new DateTimeOffset(DateTime.UtcNow);

            // The camera is moved once per group when snapshots are on, and put back at the end
            // whatever happens. Leaving a coordinator looking at the last clash of a hundred is a
            // small betrayal they would remember.
            using (var camera = snapshots ? session.View.BorrowCamera() : null)
            {
                foreach (var group in board.Groups)
                {
                    if (!job.Step(topics.Count, "topics built")) break;

                    // One topic per group, not per clash. A group is the unit somebody fixes; a BCF
                    // file with one topic per penetration is the export everybody complains about.
                    var topic = new BcfTopic(BcfGuid.For(group.Id), group.Name, now, author)
                    {
                        TopicType = "Clash",
                        TopicStatus = "Open",
                        AssignedTo = group.AssignedTo,
                        Priority = group.Priority,
                        Description = Ui.Count(group.Items.Count, "clash") + " in " + group.Name + ". " + board.Funnel,
                    };

                    foreach (var label in new[] { group.Items.FirstOrDefault()?.Level,
                                                  group.Items.FirstOrDefault()?.ModelPair })
                        if (!string.IsNullOrEmpty(label)) topic.Labels.Add(label!);

                    topic.Viewpoints.Add(Viewpoint(session, group, snapshots));
                    topics.Add(topic);
                }
            }

            return topics;
        }

        /// <summary>
        /// The viewpoint that makes a topic worth receiving.
        ///
        /// A topic with no viewpoint is a sentence about a problem somewhere in a federation. The
        /// camera says where to stand, and the components say which two things to look at — without
        /// both, the person opening it in another tool has to go and find the clash themselves,
        /// which is the entire job the file was supposed to save them.
        /// </summary>
        private static BcfViewpoint Viewpoint(Session session, ClashGroup group, bool snapshot)
        {
            var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();
            var framing = BcfFraming.Around(points);

            var viewpoint = new BcfViewpoint(group.Id) { Camera = framing.ToCamera() };

            var named = 0;

            foreach (var item in group.Items)
            {
                if (named >= ComponentCap) break;

                viewpoint.Selection.Add(Component(session, item.Key.A));
                viewpoint.Selection.Add(Component(session, item.Key.B));
                named += 2;
            }

            if (!snapshot) return viewpoint;

            session.View.FrameBox(BoxOf(points));
            viewpoint.Snapshot = session.View.Capture(SnapshotWidth, SnapshotHeight);

            return viewpoint;
        }

        /// <summary>
        /// The box a group occupies, in metres, never degenerate.
        ///
        /// Shared with the Viewpoints tab so a saved walkthrough frames its issue exactly the way
        /// the BCF viewpoints and the report's pictures do. Three framings that drifted apart would
        /// be three different opinions about where a problem is.
        /// </summary>
        /// <param name="points">Every clash point in the group, in metres.</param>
        internal static BoundingBox BoxOf(IReadOnlyList<(double X, double Y, double Z)> points)
        {
            if (points.Count == 0) return new BoundingBox(-1, -1, -1, 1, 1, 1);

            double minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY,
                   minZ = points[0].Z, maxZ = minZ;

            foreach (var (x, y, z) in points)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }

            // A group of one clash is a single point, and a zero-size box frames nothing.
            const double margin = 1.5;

            return new BoundingBox(minX - margin, minY - margin, minZ - margin,
                                   maxX + margin, maxY + margin, maxZ + margin);
        }

        /// <summary>
        /// One element, as BCF names elements.
        ///
        /// BCF identifies by IFC GlobalId, which a federation only has where a model came from IFC.
        /// Where it does not, the component is written with CamelWorks' own key as the authoring
        /// tool id and no GlobalId — which the writer counts and the export reports, rather than
        /// writing a Revit element id into a field a viewer will look up and not find.
        /// </summary>
        private static BcfComponent Component(Session session, ElementKey key)
        {
            var item = session.Model.Resolve(key);

            var guid = item == null ? null : IfcGuid.OrNull(
                item.Property("IFC", "GlobalId")
                ?? item.Property("Element", "IFC GUID")
                ?? item.Property("Item", "GUID")
                ?? item.Property("Element", "GUID"));

            return new BcfComponent(guid, key.ToString(), "CamelWorks");
        }

    }
}
