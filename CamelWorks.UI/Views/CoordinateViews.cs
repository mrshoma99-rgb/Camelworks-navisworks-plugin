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
using CamelWorks.Core.Measurement;
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
        /// <param name="session">The open document.</param>
        /// <param name="job">The run this is part of, for progress and for stopping.</param>
        /// <param name="notes">Anything the reader has to know, added as it is found.</param>
        /// <param name="trying">
        /// A rule set to use instead of the saved one, so the Rules tab can show what its edits
        /// would do before anybody commits them. Null reads the project file, which is what every
        /// other caller wants.
        /// </param>
        internal static ClashPipelineResult? Build(Session session, Job job, ICollection<string> notes,
                                                   ClashRuleSet? trying = null)
        {
            if (!session.Clash.IsAvailable) return Hydrate(session, job, notes, trying);

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

            var rules = trying
                        ?? ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);
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
            var issues = Issues(session);
            var raised = issues.Open();
            everything.AddRange(raised);

            var board = ClashPipeline.Run(everything, options);

            Instruct(board, issues);

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
        private static ClashPipelineResult? Hydrate(Session session, Job job, ICollection<string> notes,
                                                    ClashRuleSet? trying = null)
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
                read++;
                if (!job.Step(read, "results described")) return null;

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

            var rules = trying
                        ?? ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);
            var options = rules.ToOptions(session.Profile.ClashProximity);
            options.Decisions = Decisions(session);
            options.PinnedGroups = Pinned(session);

            var raised = Issues(session);

            var everything = new List<ClashItem>(hydrated.Items);
            everything.AddRange(raised.Open());

            var board = ClashPipeline.Run(everything, options);

            Instruct(board, raised);

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

                if (!job.Step(elevations.Count, "elements measured"))
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
        /// The board as it stood before the last run, for the per-party comparison.
        ///
        /// Only the two figures the ledger needs — the party counts and when they were taken. The
        /// records themselves are already read by <see cref="Snapshot"/> for carry-over, and
        /// reading them twice to count them a second way would be work for nothing.
        /// </summary>
        private static ClashSnapshot? Previous(Session session)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var parties = section["previousParties"];

            if (parties.Kind != JsonKind.Object || parties.Keys.Count == 0) return null;

            var open = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var party in parties.Keys) open[party] = (int)parties[party].AsLong();

            var taken = section["previousTaken"];

            return new ClashSnapshot(Array.Empty<ClashRecord>(), null, null, open,
                taken.Kind == JsonKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(taken.AsLong())
                    : (DateTimeOffset?)null);
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

        /// <summary>
        /// Carry an issue's own instruction onto the group it became.
        ///
        /// The decisions map still wins where it has one, so instructing the group afterwards is
        /// not undone by the issue's record the next time the board loads.
        /// </summary>
        /// <param name="board">The board that has just been built.</param>
        /// <param name="issues">Every issue raised on this project.</param>
        private static void Instruct(ClashPipelineResult board, ManualIssueList issues)
        {
            foreach (var issue in issues.Issues)
            {
                if (!issue.Instruction.IsStated) continue;

                var group = board.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, issue.Title, StringComparison.Ordinal));

                if (group != null && !group.Instruction.IsStated) group.Instruct(issue.Instruction);
            }
        }

        /// <summary>Every issue somebody raised on this project, from the project file.</summary>
        internal static ManualIssueList Issues(Session session) =>
            ManualIssueList.FromJson(session.Store.Section(ProjectStore.ClashSection)["issues"]);

        /// <summary>Write the issue list back.</summary>
        private static void SaveIssues(Session session, ManualIssueList issues) =>
            session.Store.Section(ProjectStore.ClashSection).Set("issues", issues.ToJson());

        /// <summary>The decisions made by hand, which outrank the rules and survive a re-run.</summary>
        private static IReadOnlyDictionary<string, GroupDecision> Decisions(Session session)
        {
            var decisions = new Dictionary<string, GroupDecision>(StringComparer.Ordinal);
            var saved = session.Store.Section(ProjectStore.ClashSection)["decisions"];

            foreach (var name in saved.Keys)
                decisions[name] = new GroupDecision(saved[name]["party"].AsString(),
                                                   saved[name]["priority"].AsString(),
                                                   Resolution.FromJson(saved[name]["instruction"]));

            return decisions;
        }

        /// <summary>
        /// Write one decision about a group into the project file, merging with what is there.
        ///
        /// Merging, not replacing: assigning a group and instructing it are two separate actions on
        /// two separate days, and a writer that replaced the record would silently drop whichever
        /// one was done first.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="group">The group's name, which is its identity across runs.</param>
        /// <param name="party">The responsible party, or null to leave it alone.</param>
        /// <param name="priority">The priority, or null to leave it alone.</param>
        /// <param name="instruction">What to do about it, or null to leave it alone.</param>
        private static void Remember(Session session, string group, string? party = null,
                                     string? priority = null, Resolution? instruction = null)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var decisions = section["decisions"];

            if (decisions.Kind != JsonKind.Object)
            {
                decisions = JsonValue.Object();
                section.Set("decisions", decisions);
            }

            var decision = decisions[group];

            if (decision.Kind != JsonKind.Object)
            {
                decision = JsonValue.Object();
                decisions.Set(group, decision);
            }

            if (party != null) decision.Set("party", JsonValue.String(party));
            if (priority != null) decision.Set("priority", JsonValue.String(priority));

            if (instruction == null) return;

            if (instruction.IsStated) decision.Set("instruction", instruction.ToJson());
            else decision.Remove("instruction");
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

            if (previous.Kind == JsonKind.Array)
            {
                section.Set("previous", previous);
                Shift(section, "parties", "previousParties");
                Shift(section, "taken", "previousTaken");
            }

            var taken = DateTimeOffset.UtcNow;
            var snapshot = ClashSnapshot.Of(board, takenUtc: taken);

            // Per-party open counts ride with the snapshot rather than being re-derived from it: a
            // record carries its group, not its party, and answering "what did Mechanical have open
            // last week" through this week's assignment rules would restate history every time
            // somebody re-assigned a group.
            var parties = JsonValue.Object();
            foreach (var entry in snapshot.OpenByParty) parties.Set(entry.Key, JsonValue.Number(entry.Value));

            section.Set("parties", parties);
            section.Set("taken", JsonValue.Number(taken.ToUnixTimeSeconds()));

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

            Swap(section, "parties", "previousParties");
            Swap(section, "taken", "previousTaken");

            return session.Store.Save();
        }

        /// <summary>Move one key's value to another, clearing the source.</summary>
        private static void Shift(JsonValue section, string from, string to)
        {
            var value = section[from];

            if (value.Kind == JsonKind.Null) section.Remove(to);
            else section.Set(to, value);
        }

        /// <summary>Exchange two keys, so an undo of an undo lands back where it started.</summary>
        private static void Swap(JsonValue section, string a, string b)
        {
            var first = section[a];
            var second = section[b];

            if (second.Kind == JsonKind.Null) section.Remove(a); else section.Set(a, second);
            if (first.Kind == JsonKind.Null) section.Remove(b); else section.Set(b, first);
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
            /// The two filters, hidden until there is something to filter.
            ///
            /// A pair of empty boxes over an empty list is the screen telling somebody to fill in a
            /// form before it will show them anything, which is precisely backwards: they are
            /// controls over a result, and until the result exists they are furniture.
            /// </summary>
            private readonly Border _filters;

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

                _filters = Ui.Form(Ui.Pair(Ui.Field("Show", _preset), Ui.Field("Find", _search)));
                _filters.Visibility = Visibility.Collapsed;
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

                // No card around the board. The board is what this tab is - wrapping it in a
                // titled box to sit beside three other titled boxes was what made four unrelated
                // things look like four equals.
                // The undo is for the whole board, not for a group, and it used to live inside the
                // panel that opens when you pick a row — so a user who applied a BCF reply, and
                // was told by BCF to come here for it, had to load the board and click an
                // arbitrary issue to find it.
                var undo = Ui.Button("Undo the last board edit", () =>
                {
                    var session = Host.Current;
                    if (session == null) return;

                    _summary.Children.Clear();

                    _summary.Children.Add(Rewind(session)
                        ? Ui.Done("Put back. Load the board again to see it — and pressing this a "
                                  + "second time brings the edit back, so an undo of the wrong thing "
                                  + "is not a one-way door.")
                        : Ui.Problem(session.Store.LastSaveProblem
                                     ?? "There is no earlier version of the board to go back to."));
                });

                return Ui.Scroll(Ui.Stack(
                    Ui.Actions(run, undo),
                    _summary,
                    _filters,
                    _rows,
                    _detail,
                    Ui.Rule(),
                    new RaiseScreen().Build(),
                    FreezeCard()));
            }

            /// <summary>
            /// What changed since the last run, and — folded away — how the board was arrived at.
            ///
            /// The four figures at the top are the four the meeting is about: what is new, what
            /// came back, what went away, and what nobody owns. The funnel that produced them —
            /// results in, rules applied, results suppressed — is provenance. It answers "why is
            /// this number what it is", which is a real question and never the first one, so it
            /// sits behind a disclosure instead of above the answer.
            /// </summary>
            private void Summarise()
            {
                var board = _board!;
                var funnel = board.Funnel;
                var unassigned = funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture);

                if (_delta != null)
                {
                    var regressed = _delta.Regressed.Count;

                    // Only the exception is coloured. With all four tinted, colour stopped
                    // discriminating and the one that is actually urgent — the regression — was
                    // the smallest glyph in the row, because a figure sizes by its digit count.
                    _summary.Children.Add(Ui.Across(
                        Ui.Figure(_delta.New.Count.ToString("N0", CultureInfo.InvariantCulture), "new"),
                        Ui.Figure(regressed.ToString("N0", CultureInfo.InvariantCulture), "came back",
                            regressed > 0 ? Tone.Bad : Tone.Plain),
                        Ui.Figure(_delta.Resolved.Count.ToString("N0", CultureInfo.InvariantCulture), "gone"),
                        Ui.Figure(unassigned, "unassigned",
                            funnel.Unassigned > 0 ? Tone.Warn : Tone.Plain)));

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
                else
                {
                    // First run on this project. Saying "0 new, 0 came back" would be four zeroes
                    // that read as good news, when what they mean is that there is nothing to
                    // compare against yet.
                    _summary.Children.Add(Ui.Across(
                        Ui.Figure(funnel.Groups.ToString("N0", CultureInfo.InvariantCulture), "issues"),
                        Ui.Figure(unassigned, "unassigned",
                            funnel.Unassigned > 0 ? Tone.Warn : Tone.Good)));

                    _summary.Children.Add(Ui.Info("This is the first board on this project, so there is "
                        + "nothing to compare it against. Run the tests again after the next issue of "
                        + "models and these figures become what changed."));
                }

                // Who owes what. The count of clashes is a fact about the model; this is a fact
                // about the week, and it is the one the meeting is actually about.
                var session = Host.Current;

                if (session != null)
                {
                    var ledger = PartyLedger.Of(board.Groups, Previous(session), LastBoardLabel,
                                                DateTimeOffset.UtcNow);

                    if (ledger.Lines.Count > 0)
                    {
                        _summary.Children.Add(Ui.Eyebrow("Who owes what"));

                        _summary.Children.Add(Ui.Line(string.Join("   ",
                            ledger.Lines.Take(6).Select(l => l.ToString())), 0.75));
                    }
                }

                _summary.Children.Add(Ui.Section("How this board was made",
                    funnel.Input.ToString("N0", CultureInfo.InvariantCulture) + " results in, "
                    + funnel.Groups.ToString("N0", CultureInfo.InvariantCulture) + " issues out", false,
                    Ui.Across(
                        Ui.Figure(funnel.Input.ToString("N0", CultureInfo.InvariantCulture), "from the engine"),
                        Ui.Figure(funnel.Suppressed.ToString("N0", CultureInfo.InvariantCulture), "suppressed"),
                        Ui.Figure(funnel.Groups.ToString("N0", CultureInfo.InvariantCulture), "grouped into"),
                        _delta == null ? null
                            : Ui.Figure(_delta.Persisting.Count.ToString("N0", CultureInfo.InvariantCulture),
                                        "still there")),
                    Ui.Line(funnel.ToString(), 0.8),
                    funnel.RulesApplied.Count == 0 ? null
                        : Ui.Line("Rules: " + string.Join(", ", funnel.RulesApplied), 0.7)));

                _filters.Visibility = Visibility.Visible;
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
        /// A duct 1.9 m over a corridor is a coordination problem in exactly the way a clash is,
        /// and it is the one no clash test at any tolerance will ever find — there is air between
        /// them. So it belongs beside the clash tests, which is where it is run from.
        /// </summary>
        /// <summary>The project's own length unit, or metres before a document is open.</summary>
        private static string Unit() => Host.Current?.Profile.Units.LengthUnit ?? "m";

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

                var units = session.Profile.Units;

                if (!units.TryRead(minimum.Text, out var height) || height <= 0)
                {
                    results.Children.Add(Ui.Line("Type a clear height, like " + units.Length(2.1)
                        + ". A bare number is read in " + units.LengthUnit + ".", 0.8));
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
                        "Every surface has at least " + units.Length(height) + " of clear height above it."));
                    return;
                }

                var rows = found.Spans.Take(300).Select(span => new TableRow(span,
                    units.Bare(span.Clear),
                    span.Obstruction.Name,
                    span.Floor.Name,
                    units.Bare(span.X) + ", " + units.Bare(span.Y))
                {
                    Tone = span.Clear < height * 0.8 ? Tone.Bad : Tone.Warn,
                }).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Clear (" + units.LengthUnit + ")", 90), ("Under", 190), ("Over", 190), ("At", 0))
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

            return Ui.Marked(Glyphs.Layers, "Clear height",
                "The check a clash test cannot do: a duct 1.9 m above a corridor clashes with nothing.",
                Ui.Form(Ui.Field("Minimum clear height (" + Unit() + ")", minimum)),
                run,
                Ui.Section("What counts as a floor", "Floors and slabs, under everything else", false,
                    Ui.Form(
                        Ui.Field("Surfaces", floorPicker),
                        Ui.Field("Look above at", targetPicker))),
                results);
        }

        /// <summary>
        /// Sign-off and fabrication release, and what has moved since.
        ///
        /// Two things a coordination board has to be able to say and cannot: this level was signed
        /// off on the fourteenth, and these hangers were released to fabrication on the twentieth.
        /// Both are the same act — somebody put their name to a state of the model — and both are
        /// worthless unless the tool can afterwards say what has changed since. So it is one
        /// mechanism, and the Change Notice is the half that earns it.
        ///
        /// The unfreeze is always visible. A mechanism that can only be entered is one people learn
        /// not to enter.
        /// </summary>
        private static UIElement FreezeCard()
        {
            // Two panels, not one. Ui.Runner clears its output on every press, and the record of
            // what is currently frozen — with the Unfreeze button under it — used to live in the
            // same panel the two runners write to. Pressing "Change notice" deleted the way out.
            // The comment on this method says the unfreeze is always visible; now it is.
            var state = Ui.Stack();
            var results = Ui.Stack();

            var scope = Ui.Text(string.Empty, "Level 04 services", null, 220);
            var signatory = Ui.Text(string.Empty, "who is putting their name to it", null, 180);
            var release = Ui.Text(string.Empty, "a works order, a drawing number", null, 140);

            var freeze = Ui.Runner("Freeze what is on the board", results,
                job => DoFreeze(results, scope, signatory, release, job));

            var notice = Ui.Runner("Change notice", results, primary: false,
                work: job => DoNotice(results, job));

            var session = Host.Current;

            if (session != null)
            {
                var held = Frozen(session);

                if (held != null)
                {
                    state.Children.Add(Ui.Line(held.Describe(), 1, true));

                    if (!held.IsIntact)
                        state.Children.Add(Ui.Problem(
                            "That record no longer hashes to its own contents. It has been edited in "
                            + "the project file, by hand or by a merge, and what it says was agreed "
                            + "is no longer what it says it was."));

                    state.Children.Add(Ui.Button("Unfreeze", () =>
                    {
                        state.Children.Clear();

                        session.Store.Section(ProjectStore.ClashSection).Remove("freeze");

                        state.Children.Add(session.Store.Save()
                            ? Ui.Done("Unfrozen. Nothing on the board changed - only the record that "
                                      + "it had been agreed.")
                            : Ui.Problem(session.Store.LastSaveProblem
                                         ?? "Could not write the project file."));

                        session.Record(ActivityKind.Regroup, "unfroze " + held.Scope);
                    }));
                }
            }

            return Ui.Marked(Glyphs.Save, "Freeze",
                "Sign-off and fabrication release are the same record, and both come with a change "
                + "notice.",
                state,
                Ui.Form(
                    Ui.Field("What is being frozen", scope),
                    Ui.Pair(Ui.Field("Signed by", signatory), Ui.Field("Release reference", release))),
                Ui.Across(freeze, notice),
                Ui.Small("A freeze records where every element on the board is, right now. Nothing "
                         + "edits it afterwards: the change notice is the difference between that "
                         + "record and the model as it stands, and it is the thing that stops a "
                         + "hanger being made to a drawing three weeks old."),
                results);
        }

        /// <summary>Record where everything on the board is, right now.</summary>
        private static void DoFreeze(Panel results, TextBox scope, TextBox signatory, TextBox release,
                                     Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var board = LastBoard;

            if (board == null)
            {
                results.Children.Add(Ui.Note("Load the board first. A freeze is of the board, and "
                    + "without one there is nothing to agree to."));
                return;
            }

            var elements = new List<FrozenElement>();
            var groups = new List<string>();
            var missing = 0;
            var read = 0;

            var metres = session.MetresPerUnit;

            foreach (var group in board.Groups)
            {
                groups.Add(group.Name);

                foreach (var item in group.Items)
                {
                    foreach (var key in new[] { item.Key.A, item.Key.B })
                    {
                        if (!job.Step(read++, "elements read")) return;

                        var element = session.Model.Resolve(key);

                        if (element == null) { missing++; continue; }

                        var b = element.Bounds;

                        elements.Add(new FrozenElement(key, new BoundingBox(
                            b.MinX * metres, b.MinY * metres, b.MinZ * metres,
                            b.MaxX * metres, b.MaxY * metres, b.MaxZ * metres)));
                    }
                }
            }

            var record = Freeze.Of(
                scope.Text.Trim().Length == 0 ? LastBoardLabel : scope.Text,
                Environment.UserName,
                DateTimeOffset.UtcNow,
                groups,
                elements,
                signatory.Text,
                release.Text);

            session.Store.Section(ProjectStore.ClashSection).Set("freeze", record.ToJson());

            results.Children.Add(session.Store.Save()
                ? Ui.Line(record.Describe(), 1, true)
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            if (missing > 0)
                results.Children.Add(Ui.Note(Ui.Count(missing, "element")
                    + " on the board could not be found in the model and are not in the record. "
                    + "They cannot be reported on later either, which is worth knowing now."));

            session.Record(ActivityKind.Regroup, record.Describe());
        }

        /// <summary>What has moved since somebody put their name to it.</summary>
        private static void DoNotice(Panel results, Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var held = Frozen(session);

            if (held == null)
            {
                results.Children.Add(Ui.Empty("Nothing is frozen.",
                    "Freeze the board first. A change notice is the difference between a record "
                    + "somebody agreed to and the model as it stands."));
                return;
            }

            var units = session.Profile.Units;
            var metres = session.MetresPerUnit;

            var now = new Dictionary<string, BoundingBox>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            var read = 0;

            foreach (var frozen in held.Elements)
            {
                if (!job.Step(read++, "elements checked")) return;

                var element = session.Model.Resolve(frozen.Key);
                if (element == null) continue;

                var b = element.Bounds;

                now[frozen.Key.ToString()] = new BoundingBox(
                    b.MinX * metres, b.MinY * metres, b.MinZ * metres,
                    b.MaxX * metres, b.MaxY * metres, b.MaxZ * metres);

                names[frozen.Key.ToString()] = element.DisplayName;
            }

            var notice = held.Since(now);

            results.Children.Add(Ui.Line(notice.Explain(units), 1, true));

            if (notice.IsClear) return;

            var rows = notice.Changes.Take(400).Select(c => new TableRow(c,
                names.TryGetValue(c.Was.Key.ToString(), out var name) ? name : c.Was.Key.ToString(),
                c.Describe(units),
                c.Moved > 0 ? units.Bare(c.Moved) : string.Empty,
                c.Resized > 0 ? units.Bare(c.Resized) : string.Empty)
            {
                Tone = c.IsMissing ? Tone.Bad : Tone.Warn,
            }).ToList();

            results.Children.Add(Ui.Table(rows,
                ("Element", 240), ("What changed", 200),
                ("Moved (" + units.LengthUnit + ")", 110),
                ("Resized (" + units.LengthUnit + ")", 0))
                .OnPick(item =>
                {
                    if (!(item is FrozenChange change)) return;

                    var keys = new[] { change.Was.Key };
                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);
                }));

            session.Record(ActivityKind.Report, "produced a change notice: " + notice.Explain(units));
        }

        /// <summary>
        /// The groups still worth looking at, in board order.
        ///
        /// Anything with a member that is not approved or resolved. A review that walked the
        /// closed ones too would spend most of an afternoon on work that is already done.
        /// </summary>
        /// <param name="board">The board.</param>
        internal static IReadOnlyList<ClashGroup> Reviewable(ClashPipelineResult board) =>
            board.Groups
                 .Where(g => g.Items.Count == 0 || g.Items.Any(i => FindingStatusLattice.IsOpen(i.Status)))
                 .ToList();

        /// <summary>Write the board down from outside this class, keeping the version it replaces.</summary>
        /// <param name="session">The session.</param>
        /// <param name="board">The board to record.</param>
        /// <param name="summary">What to write in the activity log.</param>
        internal static void SaveBoard(Session session, ClashPipelineResult board, string summary) =>
            Save(session, board, summary);

        /// <summary>The freeze this project holds, or null.</summary>
        private static Freeze? Frozen(Session session) =>
            Freeze.FromJson(session.Store.Section(ProjectStore.ClashSection)["freeze"]);

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
                Remember(session, group.Name, party);

                status.Children.Add(session.Store.Save()
                    ? Ui.Line("\"" + group.Name + "\" goes to " + party
                              + ". A hand assignment outranks the rules and survives the next run.", 0.8)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
            });

            // --- what to do about it ------------------------------------------------------
            // Four typed fields rather than a comment box. A comment cannot be drawn as an arrow,
            // cannot be checked against next week's model, and means three things to three readers.
            var units = session.Profile.Units;

            var verb = group.Instruction.ActionWord;
            var verbPicker = Ui.Choice(Resolution.Actions, verb, v => verb = v);

            var way = group.Instruction.DirectionWord;
            var wayPicker = Ui.Choice(Resolution.Directions, way, v => way = v);

            var howFar = Ui.Text(group.Instruction.Amount.HasValue
                ? units.Bare(group.Instruction.Amount.Value) : string.Empty, null, 90);

            var why = Ui.Text(group.Instruction.Note ?? string.Empty, null, 260);

            var stated = Ui.Line(group.Instruction.Describe(units), 0.85);

            var instruct = Ui.Button("Record what to do", () =>
            {
                status.Children.Clear();

                var action = Resolution.ActionOf(verb);

                double? amount = null;

                if (!string.IsNullOrWhiteSpace(howFar.Text))
                {
                    if (!units.TryRead(howFar.Text, out var read))
                    {
                        status.Children.Add(Ui.Problem("\"" + howFar.Text.Trim()
                            + "\" is not a distance. A bare number is read in "
                            + units.LengthUnit + "."));
                        return;
                    }

                    amount = read;
                }

                // The picked row names the element, when one is picked. Otherwise the instruction
                // is about the group, which is the honest answer when nobody said otherwise.
                var picked = table.PickedMany().OfType<ClashItem>().FirstOrDefault();

                var instruction = new Resolution(action,
                    picked?.CategoryA,
                    Resolution.AxisOf(way),
                    amount,
                    why.Text,
                    picked?.Key.A.ToString());

                group.Instruct(instruction);
                Remember(session, group.Name, instruction: instruction);

                stated.Text = instruction.Describe(units);

                status.Children.Add(session.Store.Save()
                    ? Ui.Line(instruction.IsStated
                        ? "Recorded. It prints under this group in the report, draws as an arrow in "
                          + "review, and survives the next run - no rule can restate it."
                        : "Cleared. The report will say the resolution is not stated, which is the "
                          + "truth and reads better than a blank line.", 0.8)
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

            return Ui.Card(group.Name,
                Ui.Count(group.Items.Count, "clash")
                + (group.Items.Count > DetailCap ? ", first " + DetailCap + " shown" : string.Empty)
                + (group.IsPinned ? " - made by hand" : string.Empty),
                Ui.Field("Assign to", picker),
                Ui.Across(assign, release),
                Ui.Field("What to do", verbPicker),
                Ui.Field("Which way", wayPicker),
                Ui.Field("How far (" + units.LengthUnit + ")", howFar),
                Ui.Field("Note", why),
                Ui.Across(instruct),
                stated,
                Ui.Note("Pick a row first and the instruction names that element and starts its arrow "
                        + "there; leave everything unpicked and it is about the group. A group nobody "
                        + "has instructed prints \"" + Resolution.Unstated.ToLowerInvariant()
                        + "\" rather than a blank line."),
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

                return Ui.Section("Raise an issue", "For what the clash engine cannot see", false,
                    Ui.Small("It joins the board and behaves like everything else on it."),
                    Ui.Form(
                        Ui.Field("What is wrong", _title),
                        Ui.Field("More", _note),
                        Ui.Pair(
                            Ui.Field("Who", who),
                            Ui.Field("Priority", how))),
                    Ui.Actions(Ui.Button("Raise it against the selection", () => Raise(session))),
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
                    Ui.Danger("Delete it", () => Delete(session, picked))));
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
            private static void Decide(Session session, string group, string party, string? priority) =>
                Remember(session, group, party, priority);
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

                // Headroom sits here rather than on the board. It is a test - it is run to produce
                // findings, exactly like a clash test - and it was on Triage only because its
                // findings look like clashes. What a screen is for is the act, not the output.
                //
                // _runs is inside _list because that is where the button that fills it is; the
                // matrix keeps _results inside its own card for the same reason. An answer at the
                // top of a panel, produced by a button at the bottom of it, is off screen at the
                // moment it arrives.
                return Ui.Scroll(Ui.Stack(
                    _list,
                    Ui.Rule(),
                    Matrix(),
                    HeadroomCard()));
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

                if (session.Clash.CanEdit)
                {
                    // The Monday task is "six models landed, run everything again", and the only
                    // run control used to be the one for tests that had never run at all.
                    _list.Children.Add(Ui.Runner("Run all " + Ui.Count(tests.Count, "test"), _runs,
                        job => Run(tests, job)));

                    if (stale.Count > 0)
                        _list.Children.Add(Ui.Runner("Run only the " + stale.Count + " that never have",
                            _runs, primary: false, work: job => Run(stale, job)));
                }

                _list.Children.Add(_runs);
            }

            /// <summary>The builder.</summary>
            private UIElement Matrix()
            {
                var axes = new[] { ByDiscipline, ByModel };

                var rows = Ui.Choice(axes, _rows, pick => _rows = pick);
                var columns = Ui.Choice(axes, _columns, pick => _columns = pick);
                var diagonal = Ui.Check("Include each side against itself", _selfPairs, v => _selfPairs = v);

                var build = Ui.Runner("Show me the grid", _grid, primary: false, work: job =>
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

                // The quick way stays here, in the pane, because it is genuinely quick: two lists
                // and every pairing between them, with nothing to set up first. The full matrix -
                // the one that comes from the client as a spreadsheet, with its own symbols and a
                // different tolerance per cell - opens in a window, because a square of categories
                // with a key beside it does not fit in a column four hundred and seventy pixels
                // wide, and squeezing it in there is what turned it into two dropdowns.
                var sheet = Ui.Button("Open the matrix from a spreadsheet...", () =>
                {
                    var problem = MatrixWindow.Show();
                    if (problem != null) _grid.Children.Add(Ui.Problem(problem));
                });

                return Ui.Marked(Glyphs.Clash, "Build a matrix",
                    "Two lists, every pairing between them, made in the engine in one go. Nothing has to "
                    + "be set up first: the disciplines come from what CamelWorks already worked out about "
                    + "the models that are loaded.",
                    Ui.Form(Ui.Pair(Ui.Field("Rows", rows), Ui.Field("Columns", columns))),
                    build,
                    Ui.Section("How the tests are made",
                        () => _pattern.Text + " at " + _tolerance.Text + " m", false,
                        Ui.Form(
                            Ui.Field("Name pattern", _pattern),
                            Ui.Field("Tolerance (m)", _tolerance),
                            Ui.Field("Put them in folder", _folder)),
                        diagonal),
                    _grid,
                    _results,
                    Ui.Rule(),
                    Ui.Small("Already have the matrix as a spreadsheet, with its own symbols and a "
                             + "different tolerance per cell?"),
                    Ui.Actions(sheet));
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
                    if (!job.Step(made.Count, wanted.Count, "tests made")) break;

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
                    if (!job.Step(ran, tests.Count, "tests run")) break;

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

                if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

                _rules = ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)["rules"]);

                Redraw(session);

                var save = Ui.Button("Save rules", () =>
                {
                    _status.Children.Clear();

                    session.Store.Section(ProjectStore.ClashSection).Set("rules", _rules.ToJson());

                    _status.Children.Add(session.Store.Save()
                        ? Ui.Done("Saved. The board uses these the next time it loads.")
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
                });

                // The tab's own purpose line says "previewed before it is applied", and there was
                // no preview: you saved, went to Triage, and reloaded the board to find out what
                // you had done. This is that preview, and it is the panel's answer — the four
                // figures the rules exist to move.
                var tryOut = Ui.Runner("Try these on the board", _status, job =>
                {
                    var notes = new List<string>();
                    var board = CoordinateViews.Build(session, job, notes, _rules);

                    foreach (var note in notes) _status.Children.Add(Ui.Note(note));

                    if (board == null) return;

                    var funnel = board.Funnel;

                    _status.Children.Add(Ui.Across(
                        Ui.Figure(funnel.Input.ToString("N0", CultureInfo.InvariantCulture), "from the engine"),
                        Ui.Figure(funnel.Suppressed.ToString("N0", CultureInfo.InvariantCulture), "suppressed",
                                  funnel.Suppressed > 0 ? Tone.Warn : Tone.Plain),
                        Ui.Figure(funnel.Groups.ToString("N0", CultureInfo.InvariantCulture), "issues"),
                        Ui.Figure(funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture), "unassigned",
                                  funnel.Unassigned > 0 ? Tone.Warn : Tone.Good)));

                    _status.Children.Add(Ui.Line(funnel.ToString(), 0.8));

                    if (funnel.RulesApplied.Count > 0)
                        _status.Children.Add(Ui.Line("Rules: " + string.Join(", ", funnel.RulesApplied), 0.7));

                    _status.Children.Add(Ui.Small("Nothing has been saved. This is what the board "
                                                  + "would look like if it were."));
                });

                // The sentence Ui.Numbered exists to delete. Its own doc comment: "a number on
                // each card says it without being read, and then the sentence can go."
                return Ui.Scroll(Ui.Stack(
                    Ui.Numbered(1, "Suppress and flag", "Applied to every result before anything else.", _filters),
                    Ui.Numbered(2, "Group", "Outermost rule first. Empty means the built-in stack.", _grouping),
                    Ui.Numbered(3, "Assign", "First match wins.", _assigns),
                    Ui.Actions(tryOut, save),
                    _status,
                    Ui.Small("With nothing here the board still works: it groups by model pair, then "
                             + "level, then proximity, and assigns nothing. These rules narrow that; "
                             + "they do not switch it on.")));
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

                    var drop = Ui.Icon(Glyphs.Bin, "Remove this suppress-or-flag rule", () => { _rules.Filters.Remove(filter); Redraw(session); });
                    row.Children.Add(drop);

                    Space(row);
                    _filters.Children.Add(row);
                }

                _filters.Children.Add(NewFilter(session));

                foreach (var group in _rules.Grouping.ToList())
                {
                    var row = new WrapPanel();
                    row.Children.Add(Ui.Line(group.Describe(), 1));

                    var drop = Ui.Icon(Glyphs.Bin, "Remove this grouping step", () => { _rules.Grouping.Remove(group); Redraw(session); });
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

                    var drop = Ui.Icon(Glyphs.Bin, "Remove this assignment", () => { _rules.Assigns.Remove(assign); Redraw(session); });
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

            /// <summary>The whole board, laid out for a desk.</summary>
            private const string FullReport = "Full report";

            /// <summary>One issue per page in large type, for a hand and a wall.</summary>
            private const string FieldPack = "Field pack - one issue per page";

            /// <summary>Services through structure, for the pour.</summary>
            private const string Penetration = "Penetration schedule";

            private readonly StackPanel _results = Ui.Stack();
            private readonly TextBox _path = Ui.Text(string.Empty, null, 340);
            private readonly TextBox _template = Ui.Text(Deliverable.DefaultTemplate, null, 340);

            private string _format = "PDF";
            private string _mode = FullReport;
            private string _audience = Everyone;
            private bool _changedOnly;
            private bool _pictures;
            private bool _keyPlan = true;
            private bool _perParty;

            internal UIElement Build()
            {
                var session = Host.Current;

                var parties = new List<string> { Everyone, Nobody };
                if (session != null) parties.AddRange(session.Profile.Parties);

                // The three explanations that used to sit in a stack of amber notes, one per
                // template, all visible at once. Only one of them is ever about the report being
                // written, and which one is a thing the screen already knows.
                var what = Ui.Small(Explain(_mode));

                var format = Ui.Choice(new[] { "PDF", "XLSX", "HTML" }, _format, f => _format = f, 120);
                var audience = Ui.Choice(parties, _audience, a => _audience = a, 180);

                var mode = Ui.Choice(new[] { FullReport, FieldPack, Penetration }, _mode, m =>
                {
                    _mode = m;
                    what.Text = Explain(m);
                });

                var changed = Ui.Check("Only what the last run had not seen", _changedOnly, v => _changedOnly = v);
                var pictures = Ui.Check("A picture of each group", _pictures, v => _pictures = v);
                var plan = Ui.Check("A plan of the site with every issue marked", _keyPlan, v => _keyPlan = v);
                var split = Ui.Check("One file per party, not one file", _perParty, v => _perParty = v);

                var write = Ui.Runner("Write the report", _results, Write);

                return Ui.Scroll(Ui.Stack(
                    // No subtitle. The tab's purpose line above already says "from a built-in
                    // template, nothing to configure first"; saying it twice, three pixels apart,
                    // is the panel repeating the chrome.
                    Ui.Marked(Glyphs.Report, "Write a report", null,
                        Ui.Form(
                            Ui.Field("Contents", mode),
                            what,
                            Ui.Pair(Ui.Field("For", audience), Ui.Field("Format", format))),
                        write,
                        Ui.Small("Each issue takes the next revision letter and its cover says which "
                                 + "one it supersedes. A PDF in an inbox that looks exactly like last "
                                 + "week's is the most common reason a trade builds from the wrong "
                                 + "information."),
                        Ui.Section("Where it goes, and what else is in it",
                            () => (_path.Text.Trim().Length == 0 ? "Beside the document" : "To a folder")
                                  + (_perParty ? " · per party" : string.Empty)
                                  + (_keyPlan ? " · key plan" : string.Empty)
                                  + (_pictures ? " · pictures" : string.Empty),
                            false,
                            Ui.Form(
                                Ui.PathField("Write to", _path, PathKind.Folder,
                                         hint: "blank writes it beside the document"),
                                Ui.Field("Name it", _template,
                                         "carries " + string.Join(", ", Deliverable.Tokens))),
                            changed,
                            plan,
                            split,
                            pictures,
                            Ui.Small("A picture moves your view while it runs and puts it back "
                                     + "afterwards. On a board of two hundred groups that is two "
                                     + "hundred renders, so it is off until you ask for it.")),
                        _results),

                    Ui.Rule(),

                    Exchange()));
            }

            /// <summary>
            /// The other half of a coordination cycle: the file the reviewer works in.
            ///
            /// A report is read and filed. A clash file is opened, worked through, and sent back -
            /// and it is the only clash format the rest of the industry has agreed on. Without it
            /// this tab ends at a PDF, and the answers come back as an email.
            /// </summary>
            private UIElement Exchange()
            {
                var open = Ui.Button("Send the board as a clash file...", () =>
                {
                    var problem = ClashExportWindow.Show();
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                return Ui.Marked(Glyphs.Onward, "Send it as a file somebody can work in", null,
                    Ui.Small("Clash XML, which Navisworks and every reviewing tool reads. Choose "
                             + "which tests go, at which statuses, for which party - and keep that "
                             + "choice, so next Friday is one press rather than the same twenty "
                             + "decisions. A file that comes back lands on the elements it left "
                             + "from."),
                    Ui.Actions(open));
            }

            /// <summary>What the chosen template actually produces, in the reader's terms.</summary>
            private static string Explain(string mode) => mode switch
            {
                FieldPack => "The same board set for a site cabin: one issue per page, type big "
                             + "enough to read one-handed, and a line to sign against.",

                Penetration => "Every service that goes through structure - what the model says it "
                               + "is and where. No opening size derived and no host thickness "
                               + "inferred, because those are numbers somebody cuts concrete from.",

                _ => "The whole board, laid out for a desk: every issue with its clashes, party "
                     + "and priority.",
            };

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

                var now = DateTimeOffset.UtcNow;
                var was = LastIssue(session, _audience);
                var revision = Deliverable.NextRevision(was?.Revision);

                var file = Where(session, _audience, revision, now);

                job.Say("Building the report...");

                var document = Shape(session, board, groups, _audience, regressed,
                                     Pictures(session, groups, job), revision, was, job);

                job.Say("Writing...");
                Emit(file, document, Flat(session, groups));

                Issued(session, _audience, new ReportIssue(revision, now, System.IO.Path.GetFileName(file)));
                session.Store.Save();

                _results.Children.Add(Ui.Line("Wrote revision " + revision + " to " + file, 1, true));

                if (was != null) _results.Children.Add(Ui.Line(was.Supersedes, 0.8));

                session.Record(ActivityKind.Report,
                    "wrote a " + _format + " clash report, revision " + revision, file);
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
                    if (!job.Step(written, parties.Count, "files written")) break;

                    var groups = Chosen(board, party, regressed);
                    if (groups.Count == 0) continue;

                    var now = DateTimeOffset.UtcNow;
                    var was = LastIssue(session, party);
                    var revision = Deliverable.NextRevision(was?.Revision);

                    var file = Where(session, party, revision, now);

                    job.Say("Writing " + party + "...");

                    var document = Shape(session, board, groups, party, regressed,
                                         Pictures(session, groups, job), revision, was, job);

                    Emit(file, document, Flat(session, groups));

                    // Each party has its own revision letter, because each party has its own
                    // sequence of issues and telling Mechanical they are on revision F because
                    // Structures has had five would be a lie about their own history.
                    Issued(session, party, new ReportIssue(revision, now, System.IO.Path.GetFileName(file)));

                    _results.Children.Add(Ui.Line(party + " - rev " + revision + " - "
                        + Ui.Count(groups.Count, "group") + " - " + file, 0.8));

                    written++;
                }

                session.Store.Save();

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
            private string Where(Session session, string audience, string revision, DateTimeOffset issued)
            {
                var typed = _path.Text.Trim();
                var extension = "." + _format.ToLowerInvariant();

                var stem = Deliverable.Name(_template.Text, session.Profile.ProjectName,
                                            audience == Everyone ? null : audience, revision, issued);

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

                return System.IO.Path.Combine(directory,
                    Deliverable.Safe(name + " - " + audience + " - Rev " + revision) + extension);
            }

            /// <summary>
            /// What this issue is called, and what it replaces.
            ///
            /// The most common cause of a trade building from the wrong information is a PDF in an
            /// inbox that looks exactly like last week's, so every issue takes the next revision and
            /// says on its cover which one it supersedes.
            /// </summary>
            private static ReportIssue? LastIssue(Session session, string audience) =>
                Deliverable.Read(session.Store.Section(ProjectStore.ClashSection)["issued"])
                    .TryGetValue(audience, out var issue) ? issue : null;

            /// <summary>Record what just went out, so the next one knows what it supersedes.</summary>
            private static void Issued(Session session, string audience, ReportIssue issue)
            {
                var section = session.Store.Section(ProjectStore.ClashSection);

                // Copied key by key: Dictionary has a constructor taking IDictionary and one taking
                // a capacity, and an IReadOnlyDictionary silently matches neither.
                var register = new Dictionary<string, ReportIssue>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in Deliverable.Read(section["issued"])) register[entry.Key] = entry.Value;

                register[audience] = issue;

                section.Set("issued", Deliverable.Write(register));
            }

            /// <summary>
            /// One rendered view per group, or nothing when they were not asked for.
            ///
            /// The camera is borrowed once for the whole run and put back at the end. Doing it per
            /// group would still be correct and would leave the view visibly jumping back and forth
            /// for as long as the report takes.
            /// </summary>
            /// <summary>How wide the site plan is rendered. One pixel of a 400 m site is 250 mm.</summary>
            private const int PlanWidth = 1600;

            /// <summary>How tall the site plan is rendered.</summary>
            private const int PlanHeight = 1200;

            /// <summary>
            /// One top-down plan of everything on the board, with a numbered marker per group.
            ///
            /// Rendered once per report, not once per group: it is the page a reader turns back to,
            /// and forty renders of the same plan would be forty identical pages.
            ///
            /// The camera is borrowed and returned like the group pictures, and the whole thing is
            /// skipped rather than failed when the host will not give a plan view — a 2D sheet has
            /// no camera to rotate, and a report without a key plan is still a report.
            /// </summary>
            private ImageBlock? Plan(Session session, IReadOnlyList<ClashGroup> groups, Job job,
                                     out BoundingBox framed)
            {
                framed = default;

                if (!_keyPlan || groups.Count == 0) return null;

                var site = KeyPlan.Extent(groups.SelectMany(g => g.Items).Select(i => (i.X, i.Y, i.Z)));
                if (site == null) return null;

                framed = KeyPlan.Frame(site.Value, PlanWidth, PlanHeight);

                job.Say("Rendering the site plan...");

                byte[]? png;

                using (session.View.BorrowCamera())
                {
                    if (!session.View.LookDown(framed)) return null;

                    png = session.View.Capture(PlanWidth, PlanHeight);
                }

                if (png == null || png.Length == 0) return null;

                var block = new ImageBlock(png, "Every issue on this report, seen from above");

                var number = 0;

                foreach (var group in groups)
                {
                    number++;

                    var first = group.Items.FirstOrDefault();
                    if (first == null) continue;

                    var at = KeyPlan.At(framed, first.X, first.Y);

                    block.Callouts.Add(new Callout(number, at.X, at.Y));
                }

                return block;
            }

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
                        if (!job.Step(done++, groups.Count, "views rendered")) break;

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

            /// <summary>
            /// Write it out, in whichever of the three shapes was asked for.
            ///
            /// The spreadsheet gets the same content as a flat table rather than the laid-out
            /// document: a field pack pasted into Excel one issue per sheet would be unusable, and
            /// what somebody wants from a spreadsheet is rows they can sort.
            /// </summary>
            private void Emit(string file, ReportDocument document, TableBlock flat)
            {
                switch (_format)
                {
                    case "XLSX":
                        File.WriteAllBytes(file, XlsxWriter.Write(SheetName, flat));
                        break;

                    case "HTML":
                        File.WriteAllText(file, HtmlReportWriter.Write(document));
                        break;

                    default:
                    {
                        using (var stream = File.Create(file))
                            PdfReportWriter.Write(stream, document,
                                _mode == FieldPack ? PdfReportWriter.FieldPackType : 1);

                        break;
                    }
                }
            }

            /// <summary>What the one worksheet is called.</summary>
            private string SheetName =>
                _mode == Penetration ? "Penetrations" : _mode == FieldPack ? "Field pack" : "Clashes";

            /// <summary>The document, in whichever of the three shapes was asked for.</summary>
            private ReportDocument Shape(Session session, ClashPipelineResult board,
                                         IReadOnlyList<ClashGroup> groups, string audience,
                                         ICollection<ClashItem> regressed,
                                         IReadOnlyDictionary<string, byte[]> images,
                                         string revision, ReportIssue? was, Job job)
            {
                if (_mode == FieldPack)
                    return FieldPackOf(session, groups, audience, images, revision, was);

                if (_mode == Penetration)
                    return PenetrationsOf(session, board, groups, audience, revision, was);

                var plan = Plan(session, groups, job, out var framed);

                return Document(session, board, groups, audience, regressed, images, revision, was,
                                plan, framed, PlanWidth);
            }

            /// <summary>The flat table that goes into a spreadsheet.</summary>
            private TableBlock Flat(Session session, IReadOnlyList<ClashGroup> groups)
            {
                var units = session.Profile.Units;

                if (_mode == Penetration) return Schedule(session, groups).Table(units);
                if (_mode == FieldPack) return FieldTable(groups, units);

                return Table(groups, units);
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
                                               IReadOnlyDictionary<string, byte[]> images,
                                               string? revision = null, ReportIssue? supersedes = null,
                                               ImageBlock? plan = null, BoundingBox framed = default,
                                               int planWidth = 0)
        {
            var whole = groups.Count == board.Groups.Count;

            var document = new ReportDocument(session.Profile.ProjectName + " — clash report")
            {
                Subtitle = whole ? "Produced by CamelWorks" : "For " + audience + ", produced by CamelWorks",
            };

            document.Fact("Project", session.Profile.ProjectName);
            document.Fact("Author", session.Profile.Author);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            if (revision != null) document.Fact("Revision", revision);
            if (!whole) document.Fact("For", audience);
            document.Fact("From the engine", board.Funnel.Input.ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("In this report", groups.Sum(g => g.Items.Count).ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("Groups", groups.Count.ToString("N0", CultureInfo.InvariantCulture));

            if (whole)
                document.Fact("Unassigned", board.Funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture));

            if (supersedes != null) document.Paragraph(supersedes.Supersedes);

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
            WhoOwesWhat(document, session, board, whole ? null : audience);
            WhereOnSite(document, session, groups, plan, framed, planWidth);

            document.Heading("Groups");
            document.Add(Summary(groups, session.Profile.Units));

            if (images.Count > 0) Pictures(document, groups, images, session.Profile.Units);

            document.PageBreak();
            document.Heading("Every clash");
            document.Add(Table(groups, session.Profile.Units));

            WhatItWasRunAgainst(document, session);
            WhatIsNotShown(document, session);

            return document;
        }

        /// <summary>
        /// One plan of the site with every issue marked on it, and what it can and cannot resolve.
        ///
        /// A report full of close-up renders answers "what does this look like" and never answers
        /// "where is it". One plan answers the second question for forty issues at once.
        /// </summary>
        private static void WhereOnSite(ReportDocument document, Session session,
                                        IReadOnlyList<ClashGroup> groups, ImageBlock? plan,
                                        BoundingBox framed, int planWidth)
        {
            if (plan == null) return;

            document.PageBreak();
            document.Heading("Where on the site");
            document.Add(plan);

            var number = 0;

            document.Add(KeyPlan.Legend(groups.Select(g =>
            {
                number++;

                var first = g.Items.FirstOrDefault();

                return (number, g.Name, g.AssignedTo, first?.Level);
            })));

            document.Paragraph(KeyPlan.Resolution(framed, planWidth, session.Profile.Units));
        }

        /// <summary>
        /// What this report could not show.
        ///
        /// Its own block so it cannot be forgotten. The redline line is the honest one: a
        /// coordinator who has marked up a viewpoint has every reason to expect the words to come
        /// out in the report, and finding out on site that they did not is worse than being told
        /// here.
        /// </summary>
        private static void WhatIsNotShown(ReportDocument document, Session session)
        {
            var disclosure = new DisclosureBlock("Not shown in this report");

            var marked = 0;

            try
            {
                marked = session.Viewpoints.All().Count(v => v.HasRedlines);
            }
            catch (Exception)
            {
                // A host that cannot list viewpoints simply contributes no line.
            }

            if (marked > 0)
                disclosure.Line("Redline mark-ups on " + Ui.Count(marked, "saved viewpoint")
                    + ". Navisworks exposes no supported way for a plug-in to read the text of a "
                    + "redline: the drawing objects live behind an internal state handle the public "
                    + "API never hands out, and neither the .NET nor the COM surface enumerates them. "
                    + "Anything written on those viewpoints has to be read in Navisworks.");

            if (disclosure.Lines.Count > 0) document.Add(disclosure);
        }

        /// <summary>
        /// The same board, set for a site cabin.
        ///
        /// Portrait, one issue per page, type big enough to read one-handed off a phone, and a line
        /// to sign against. Nobody had watched a foreman pinch-zoom a forty-page landscape PDF, and
        /// it is the difference between a report that gets used on site and one that gets printed
        /// and left in the cabin.
        ///
        /// No funnel, no manifest, no per-party ledger: those are a coordinator's questions, and
        /// putting them in front of somebody holding a spanner is how the page that matters gets
        /// missed.
        /// </summary>
        private static ReportDocument FieldPackOf(Session session, IReadOnlyList<ClashGroup> groups,
                                                  string audience,
                                                  IReadOnlyDictionary<string, byte[]> images,
                                                  string revision, ReportIssue? was)
        {
            var units = session.Profile.Units;

            var document = new ReportDocument(session.Profile.ProjectName + " — field pack")
            {
                Subtitle = audience,
            };

            document.Fact("Project", session.Profile.ProjectName);
            document.Fact("For", audience);
            document.Fact("Revision", revision);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            document.Fact("Issues", Ui.Count(groups.Sum(g => g.Items.Count), "clash"));

            if (was != null) document.Paragraph(was.Supersedes);

            document.Paragraph("One page per issue. Each page says where it is, what to do about it, "
                               + "and has a line to sign when it is done.");

            var number = 0;

            foreach (var group in groups)
            {
                document.PageBreak();

                number++;

                document.Heading(number.ToString(CultureInfo.InvariantCulture) + ". " + group.Name);

                if (images.TryGetValue(group.Name, out var png))
                    document.Add(new ImageBlock(png, group.AssignedTo ?? PartyLedger.Nobody));

                document.Add(new ParagraphBlock(group.Instruction.Describe(units)) { IsBold = true });

                document.Add(WhereItIs(group, units));
                document.Add(SignHere());
            }

            return document;
        }

        /// <summary>Where the issue is, in as few rows as it can be said in.</summary>
        private static TableBlock WhereItIs(ClashGroup group, Measure units)
        {
            var table = new TableBlock("Where", "What");

            var first = group.Items.FirstOrDefault();

            table.Row("Level", first?.Level ?? Measure.Nothing);
            table.Row("Models", first?.ModelPair ?? Measure.Nothing);

            table.Row("Setting out (" + units.LengthUnit + ")",
                first == null ? Measure.Nothing
                    : units.Bare(first.X) + ", " + units.Bare(first.Y) + ", " + units.Bare(first.Z));

            table.Row("Responsible", group.AssignedTo ?? PartyLedger.Nobody);
            table.Row("Clashes in this issue", Ui.Count(group.Items.Count, "clash"));

            return table;
        }

        /// <summary>
        /// A line to sign against.
        ///
        /// A table rather than a block of its own, so it renders on all three outputs without any
        /// of the three writers learning a new shape. The row is empty because somebody fills it in
        /// with a pen.
        /// </summary>
        private static TableBlock SignHere()
        {
            var table = new TableBlock("Fixed by", "Date", "Signature");

            table.Row(" ", " ", " ");

            return table;
        }

        /// <summary>The field pack as a flat table, for the spreadsheet.</summary>
        private static TableBlock FieldTable(IReadOnlyList<ClashGroup> groups, Measure units)
        {
            var axis = " (" + units.LengthUnit + ")";

            var table = new TableBlock("#", "Issue", "Level", "Models", "Responsible",
                                       "X" + axis, "Y" + axis, "Z" + axis,
                                       "What to do", "Fixed by", "Date");

            var number = 0;

            foreach (var group in groups)
            {
                var first = group.Items.FirstOrDefault();

                number++;

                table.Row(number.ToString(CultureInfo.InvariantCulture),
                          group.Name,
                          first?.Level ?? string.Empty,
                          first?.ModelPair ?? string.Empty,
                          group.AssignedTo ?? PartyLedger.Nobody,
                          first == null ? string.Empty : units.Bare(first.X),
                          first == null ? string.Empty : units.Bare(first.Y),
                          first == null ? string.Empty : units.Bare(first.Z),
                          group.Instruction.Describe(units),
                          string.Empty, string.Empty);
            }

            return table;
        }

        /// <summary>
        /// Every service that goes through structure, as a schedule.
        ///
        /// On a concrete frame the pour date is the one immovable date on the programme, and this
        /// list is what has to be right before it — today produced by reading a clash board and
        /// retyping it into a spreadsheet, which is the same list one transcription error later.
        /// </summary>
        private static ReportDocument PenetrationsOf(Session session, ClashPipelineResult board,
                                                     IReadOnlyList<ClashGroup> groups, string audience,
                                                     string revision, ReportIssue? was)
        {
            var units = session.Profile.Units;
            var schedule = Schedule(session, groups);

            var document = new ReportDocument(session.Profile.ProjectName + " — penetration schedule")
            {
                Subtitle = audience,
            };

            document.Fact("Project", session.Profile.ProjectName);
            document.Fact("For", audience);
            document.Fact("Revision", revision);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            document.Fact("Penetrations", Ui.Count(schedule.Lines.Count, "penetration"));

            if (was != null) document.Paragraph(was.Supersedes);

            document.Heading("Services through structure");
            document.Paragraph(schedule.Explain());

            if (schedule.IsEmpty)
            {
                document.Paragraph(
                    "Nothing on this board pairs a service with a piece of structure. That is a real "
                    + "answer on a steel frame or an early-stage model; on a concrete frame with MEP "
                    + "loaded it more likely means the categories in these models do not name their "
                    + "elements as ducts, pipes, walls and slabs.");

                return document;
            }

            document.Add(schedule.Table(units));

            document.Paragraph(
                "Sizes and systems are what the model states, not what CamelWorks worked out. No "
                + "opening size is derived and no host thickness is inferred: both are numbers "
                + "somebody cuts concrete from, and neither is this tool's to guess.");

            WhatItWasRunAgainst(document, session);

            return document;
        }

        /// <summary>
        /// Pick the penetrations out of the board.
        ///
        /// One line per result rather than per group, because a group of eight ducts through one
        /// wall is eight sleeves and the schedule is a list of sleeves.
        /// </summary>
        private static PenetrationSchedule Schedule(Session session, IReadOnlyList<ClashGroup> groups)
        {
            var lines = new List<PenetrationLine>();
            var considered = 0;

            foreach (var group in groups)
            {
                foreach (var item in group.Items)
                {
                    considered++;

                    var a = session.Model.Resolve(item.Key.A);
                    var b = session.Model.Resolve(item.Key.B);

                    var serviceIsA = Penetrations.ServiceIsA(
                        item.CategoryA ?? a?.Category, a?.TypeName,
                        item.CategoryB ?? b?.Category, b?.TypeName);

                    if (serviceIsA == null) continue;

                    var service = serviceIsA.Value ? a : b;
                    var host = serviceIsA.Value ? b : a;

                    var serviceName = service?.DisplayName
                                      ?? (serviceIsA.Value ? item.CategoryA : item.CategoryB);

                    var hostName = host?.DisplayName
                                   ?? (serviceIsA.Value ? item.CategoryB : item.CategoryA);

                    lines.Add(new PenetrationLine(group.Name, serviceName ?? "(unnamed)",
                                                  hostName ?? "(unnamed)")
                    {
                        Size = Size(service),
                        SystemName = service?.Property("Element", "System Name")
                                     ?? service?.Property("Item", "System")
                                     ?? service?.Property("Element", "System Classification"),
                        HostType = host?.TypeName,
                        Level = item.Level,
                        Party = group.AssignedTo,
                        X = item.X, Y = item.Y, Z = item.Z,
                        Status = item.Status,
                    });
                }
            }

            return Penetrations.Of(lines, considered);
        }

        /// <summary>
        /// The service's size, as the model states it.
        ///
        /// Read, never derived. A round duct carries a diameter, a rectangular one carries width
        /// and height, and a tool that inferred one from a bounding box would be handing somebody a
        /// number to cut concrete from that no engineer had ever seen.
        /// </summary>
        private static string? Size(IModelItem? item)
        {
            if (item == null) return null;

            var stated = item.Property("Element", "Size") ?? item.Property("Item", "Size");
            if (!string.IsNullOrWhiteSpace(stated)) return stated!.Trim();

            var diameter = item.Property("Element", "Diameter");
            if (!string.IsNullOrWhiteSpace(diameter)) return diameter!.Trim();

            var width = item.Property("Element", "Width");
            var height = item.Property("Element", "Height");

            if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                return width!.Trim() + " x " + height!.Trim();

            return null;
        }

        /// <summary>
        /// Who owes what, and whether last week's list got shorter.
        ///
        /// The board's own figures, always of the whole board even on a report for one party — the
        /// point of the block is that everybody's copy carries the same numbers, so the meeting is
        /// about the building rather than about whose report is right.
        /// </summary>
        private static void WhoOwesWhat(ReportDocument document, Session session,
                                        ClashPipelineResult board, string? audience)
        {
            var ledger = PartyLedger.Of(board.Groups, Previous(session),
                                        audience ?? LastBoardLabel, DateTimeOffset.UtcNow);

            if (ledger.Lines.Count == 0) return;

            document.Heading("Who owes what");
            document.Add(ledger.Table());
            document.Paragraph(ledger.Provenance());
        }

        /// <summary>
        /// The manifest: every model this run was run against, and every test that produced it.
        ///
        /// "Which revision of the structural model was this run against?" is the first question in
        /// the Wednesday meeting and the only question six months later, and a report that cannot
        /// answer it is a screenshot with a logo.
        /// </summary>
        private static void WhatItWasRunAgainst(ReportDocument document, Session session)
        {
            var manifest = ModelManifest.Of(session.Model.Models, session.Profile,
                                            TestRecords(session), version: Ui.Release,
                                            takenUtc: DateTimeOffset.UtcNow);

            if (manifest.IsEmpty && manifest.Tests.Count == 0) return;

            document.PageBreak();
            document.Heading("What this was run against");
            document.Paragraph(manifest.Explain());

            if (!manifest.IsEmpty) document.Add(manifest.ModelTable(session.Profile.Units));

            if (manifest.Tests.Count > 0)
            {
                document.Heading("Tests", 2);
                document.Add(manifest.TestTable());
            }

            document.Paragraph(
                "Timestamps are of the source files as this run read them, in UTC. A model with no "
                + "readable source is shown as such rather than left blank: an unreachable share and "
                + "a file that has never changed are different facts.");
        }

        /// <summary>
        /// A host last-run time as an instant.
        ///
        /// The host hands back a bare <c>DateTime</c> with no kind, which on a desktop API means
        /// local time — the same reading the Tests screen makes. Stated here once rather than
        /// guessed at in two places, so the report and the screen cannot disagree by an hour.
        /// </summary>
        internal static DateTimeOffset? RunAt(long ticks) =>
            ticks > 0 ? new DateTimeOffset(new DateTime(ticks, DateTimeKind.Local)) : (DateTimeOffset?)null;

        /// <summary>One record per clash test the host holds, or nothing on an edition with no engine.</summary>
        private static IReadOnlyList<TestRecord> TestRecords(Session session)
        {
            var clash = session.Clash;
            if (clash == null || !clash.IsAvailable) return Array.Empty<TestRecord>();

            var records = new List<TestRecord>();

            foreach (var test in clash.Tests())
                records.Add(new TestRecord(test.Name, RunAt(test.LastRunTicks), test.ResultCount));

            return records;
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

        /// <summary>
        /// A rendered view of each group that produced one, with its own heading and its instruction.
        ///
        /// The instruction goes directly under the image, in bold, because that pairing is the whole
        /// deliverable: a picture of the problem and one sentence saying what to do about it. A
        /// group nobody has instructed says so rather than leaving a gap the reader has to
        /// interpret.
        /// </summary>
        private static void Pictures(ReportDocument document, IReadOnlyList<ClashGroup> groups,
                                     IReadOnlyDictionary<string, byte[]> images, Measure units)
        {
            document.PageBreak();
            document.Heading("Where they are");

            foreach (var group in groups)
            {
                if (!images.TryGetValue(group.Name, out var png)) continue;

                document.Heading(group.Name, 2);
                document.Add(new ImageBlock(png,
                    Ui.Count(group.Items.Count, "clash") + " - " + (group.AssignedTo ?? "unassigned")));

                document.Add(new ParagraphBlock(group.Instruction.Describe(units)) { IsBold = true });
            }
        }

        private static TableBlock Summary(IReadOnlyList<ClashGroup> groups, Measure units)
        {
            var table = new TableBlock("Group", "Clashes", "Assigned to", "Priority", "What to do");

            foreach (var group in groups)
                table.Row(group.Name,
                    group.Items.Count.ToString("N0", CultureInfo.InvariantCulture),
                    group.AssignedTo ?? "unassigned",
                    group.Priority ?? string.Empty,
                    group.Instruction.Describe(units));

            return table;
        }

        /// <summary>
        /// Every clash, with its setting-out coordinates in the project's own units.
        ///
        /// The unit is in the heading rather than in four hundred cells, and it comes from the
        /// project profile rather than from whatever the host display happens to be set to — a
        /// report reissued in different units from last week's is how a trade sets out twice.
        /// </summary>
        private static TableBlock Table(IReadOnlyList<ClashGroup> groups, Measure units)
        {
            var axis = " (" + units.LengthUnit + ")";

            var table = new TableBlock("Group", "Test", "Level", "Models", "What", "Status",
                                       "X" + axis, "Y" + axis, "Z" + axis);

            foreach (var group in groups)
                foreach (var item in group.Items)
                    table.Row(group.Name,
                        item.TestName,
                        item.Level ?? string.Empty,
                        item.ModelPair,
                        (item.CategoryA ?? "?") + " v " + (item.CategoryB ?? "?"),
                        item.Status.ToString(),
                        units.Bare(item.X),
                        units.Bare(item.Y),
                        units.Bare(item.Z));

            return table;
        }

        // ===================================================================================
        // BCF
        // ===================================================================================

        /// <summary>Export the board as BCF, or read somebody else's back in.</summary>
        public static UIElement Bcf()
        {
            var results = Ui.Stack();

            // Two paths, deliberately. These were one TextBox: the box you typed an output
            // filename into was the box the importer then read from, and the worksheet leg had
            // the same defect independently. A field that means two opposite things depending on
            // which button you press is not a field.
            var outFile = Ui.Text(string.Empty, "leave it and CamelWorks names the file", null, 300);
            var inFile = Ui.Text(string.Empty, "a .bcfzip, or a worksheet that went out from here", null, 300);

            var version = "2.1";
            var picker = Ui.Choice(new[] { "2.1", "3.0" }, version, v => version = v);

            var snapshots = true;
            var snapshotBox = Ui.Toggle("A view on every topic", snapshots, v => snapshots = v);

            // The far end either has a BCF reader or it has Excel. That is a property of the file
            // being sent, not a second tool, so it is one control on the one send.
            var asSheet = false;

            var parties = new List<string> { "Everyone" };
            var session0 = Host.Current;
            if (session0 != null) parties.AddRange(session0.Profile.Parties);

            var party = parties[0];
            var forWhom = Ui.Choice(parties, party, p => party = p);

            var send = Ui.Runner("Send the board", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                // One send, two formats. The worksheet leg used to be a third card with its own
                // pair of buttons and its own pair of paths; a file format is an option of sending,
                // not a tool of its own.
                if (asSheet) { WriteSheet(results, outFile, party, job); return; }

                var board = LastBoard;

                if (board == null)
                {
                    var notes = new List<string>();
                    board = Build(session, job, notes);
                    foreach (var note in notes) results.Children.Add(Ui.Note(note));
                }

                if (board == null) return;

                var file = outFile.Text.Trim();

                if (file.Length == 0)
                {
                    file = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        session.Profile.ProjectName + ".bcfzip");

                    outFile.Text = file;
                }

                job.Say("Building topics...");
                var topics = TopicsOf(session, board, snapshots, job);

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

            var read = Ui.Runner("Read it", results, primary: false, work: job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                // The user has a file. They should not have to tell the tool which kind it is.
                if (inFile.Text.Trim().EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    ReadSheet(results, inFile, job);
                    return;
                }

                var file = inFile.Text.Trim();

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
                Ui.Marked(Glyphs.Onward, "Send it out", null,
                    Ui.Form(Ui.Pair(
                        Ui.Field("As", Ui.Segmented(new[] { "BCF", "Spreadsheet" }, "BCF",
                                                    pick => asSheet = pick == "Spreadsheet")),
                        Ui.Field("For", forWhom, "one party's rows, on the spreadsheet"))),
                    Ui.Actions(send),
                    Ui.Section("How it is written",
                        () => (asSheet ? "A spreadsheet for " + party : "BCF " + version)
                              + (asSheet || !snapshots ? string.Empty : " · a view on every topic"),
                        false,
                        Ui.Form(
                            Ui.Pair(Ui.Field("Version", picker), Ui.Field("Snapshots", snapshotBox)),
                            Ui.PathField("Write to", outFile, PathKind.Save,
                                         "BCF|*.bcf|Excel|*.xlsx|All files|*.*")))),

                Ui.Marked(Glyphs.Model, "Read the answers back",
                    "This one changes the board, so it shows you what it would do first.",
                    Ui.Form(Ui.PathField("File", inFile, PathKind.Open,
                                         "BCF or the sheet you sent|*.bcf;*.xlsx|All files|*.*")),
                    Ui.Actions(read)),

                Together(),

                results));
        }

        private static void WriteSheet(Panel results, TextBox path, string party, Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var board = LastBoard;

            if (board == null)
            {
                results.Children.Add(Ui.Note("Load the board in Triage first. The worksheet is of the "
                    + "board, and without one there is nothing to send."));
                return;
            }

            var units = session.Profile.Units;
            var everyone = string.Equals(party, "Everyone", StringComparison.Ordinal);

            var lines = new List<IssueLine>();

            foreach (var group in board.Groups)
            {
                if (!everyone && !string.Equals(group.AssignedTo, party, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Open issues only. Sending somebody a list of what they already closed is how a
                // list stops being read.
                if (group.Items.Count > 0 && group.Items.All(i => FindingStatusLattice.IsClosed(i.Status)))
                    continue;

                var first = group.Items.FirstOrDefault();

                lines.Add(new IssueLine(group.Name)
                {
                    Party = group.AssignedTo,
                    Level = first?.Level,
                    Location = first == null ? null : IssueSheet.Where(first.X, first.Y, first.Z, units),
                    Description = first == null
                        ? Ui.Count(group.Items.Count, "clash")
                        : (first.CategoryA ?? "?") + " against " + (first.CategoryB ?? "?")
                          + " - " + Ui.Count(group.Items.Count, "clash"),
                    Instruction = group.Instruction.IsStated ? group.Instruction.Describe(units) : null,
                    Status = group.Items.Count == 0
                        ? FindingStatus.Active
                        : group.Items.Min(i => i.Status),
                });
            }

            if (lines.Count == 0)
            {
                results.Children.Add(Ui.Empty("Nothing open to send.",
                    everyone
                        ? "Every group on the board is approved or resolved."
                        : "Nothing open on the board is with " + party + "."));
                return;
            }

            var file = path.Text.Trim();

            if (file.Length == 0)
            {
                file = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Deliverable.Safe(session.Profile.ProjectName + " issues - " + party) + ".xlsx");

                path.Text = file;
            }

            job.Say("Writing " + Ui.Count(lines.Count, "issue") + "...");

            try
            {
                File.WriteAllBytes(file, XlsxWriter.Write(new[] { IssueSheet.Write(party, lines) }));
            }
            catch (Exception error)
            {
                results.Children.Add(Ui.Problem("Could not write it: " + error.Message));
                return;
            }

            RememberSheet(session, lines);

            results.Children.Add(Ui.Line("Wrote " + Ui.Count(lines.Count, "issue") + " to " + file, 1, true));

            session.Record(ActivityKind.Exchange,
                "wrote an issue worksheet for " + party + " - " + Ui.Count(lines.Count, "issue"), file);
        }

        /// <summary>Read a returned worksheet and show what it would do, before doing any of it.</summary>
        private static void ReadSheet(Panel results, TextBox path, Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var board = LastBoard;

            if (board == null)
            {
                results.Children.Add(Ui.Note("Load the board in Triage and read this sheet again. "
                    + "Without a board there is nothing to reconcile against."));
                return;
            }

            var file = path.Text.Trim();

            if (file.Length == 0)
            {
                results.Children.Add(Ui.Problem("Say which file to read back."));
                return;
            }

            job.Say("Reading the sheet...");

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception error)
            {
                results.Children.Add(Ui.Problem("Could not read it: " + error.Message));
                return;
            }

            var book = XlsxReader.Read(bytes);

            if (!book.IsUsable)
            {
                results.Children.Add(Ui.Problem(book.Problem ?? "Nothing could be read from that file."));
                return;
            }

            // The record of what was sent, with the current board laid over it: the record survives
            // a re-run that renamed a group, and the board is right about what it is called today.
            var sent = SentSheets(session);
            foreach (var group in board.Groups) sent[IssueSheet.IdOf(group.Name)] = group.Name;

            var current = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);

            foreach (var group in board.Groups)
                current[group.Name] = group.Items.Count == 0
                    ? FindingStatus.Active
                    : group.Items.Min(i => i.Status);

            var report = IssueSheet.Read(book.First, sent, current);

            results.Children.Add(Ui.Line(report.Explain(), 1, true));

            if (report.Problem != null)
            {
                results.Children.Add(Ui.Problem(report.Problem));
                return;
            }

            if (report.Unmatched.Count > 0)
                results.Children.Add(Ui.Problem(
                    Ui.Count(report.Unmatched.Count, "row") + " name an issue this project never sent "
                    + "- rows " + string.Join(", ", report.Unmatched.Take(8).Select(r => r.Number))
                    + ". Somebody added them, or they came from another package. They are somebody's "
                    + "list and not this board's status to change."));

            if (report.Duplicates.Count > 0)
                results.Children.Add(Ui.Problem(
                    Ui.Count(report.Duplicates.Count, "row") + " repeat an issue an earlier row already "
                    + "answered. The first row wins."));

            if (report.Blank > 0)
                results.Children.Add(Ui.Note(Ui.Count(report.Blank, "row")
                    + " carry a response but no issue id, so there is no way to tell which issue "
                    + "they are about."));

            if (report.Matched.Count == 0) return;

            var rows = report.Matched.Select(m => new TableRow(m,
                m.Group,
                m.UnknownStatus ?? (m.Incoming == null ? "-" : FindingStatusLattice.ToName(m.Incoming.Value)),
                FindingStatusLattice.ToName(m.Current),
                m.Changes ? FindingStatusLattice.ToName(m.Merged) : "-",
                m.Note ?? string.Empty)
            {
                Tone = m.UnknownStatus != null ? Tone.Warn
                     : m.IsReopen && m.Changes ? Tone.Bad
                     : m.Changes ? Tone.Good
                     : Tone.Plain,
            }).ToList();

            results.Children.Add(Ui.Table(rows,
                ("Issue", 200), ("They say", 110), ("The board says", 110),
                ("Would become", 110), ("What they wrote", 0)));

            if (report.Refused > 0)
                results.Children.Add(Ui.Note(Ui.Count(report.Refused, "row")
                    + " asked for a status lower than the board holds and will stay where they are. "
                    + "The sheet went out before those decisions were made, and a stale \"Open\" must "
                    + "not undo an approval nobody has disputed. A deliberate re-open is different "
                    + "and is honoured."));

            if (report.Changes == 0)
            {
                results.Children.Add(Ui.Empty("Nothing to apply.",
                    "The board already agrees with everything on that sheet."));
                return;
            }

            results.Children.Add(Ui.Button("Apply " + Ui.Count(report.Changes, "change") + " to the board",
                () => ApplySheet(session, board, report, results)));

            session.Record(ActivityKind.Exchange, "read an issue worksheet", file);
        }

        /// <summary>Write a returned worksheet into the board, one group at a time.</summary>
        private static void ApplySheet(Session session, ClashPipelineResult board,
                                       IssueSheetReport report, Panel results)
        {
            var moved = 0;
            var groups = 0;

            foreach (var response in report.Matched.Where(m => m.Changes))
            {
                var group = board.Groups.FirstOrDefault(
                    g => string.Equals(g.Name, response.Group, StringComparison.Ordinal));

                if (group == null) continue;

                groups++;

                foreach (var item in group.Items)
                {
                    // Per item, and promote-only unless the sheet re-opened it. Applying the merged
                    // status flat would demote rows somebody had already taken further.
                    var next = response.IsReopen
                        ? response.Merged
                        : FindingStatusLattice.Merge(item.Status, response.Merged);

                    if (next == item.Status) continue;

                    item.Status = next;
                    moved++;
                }
            }

            Save(session, board, "applied a returned worksheet to " + Ui.Count(groups, "group"));

            results.Children.Add(session.Store.Save()
                ? Ui.Done("Applied to " + Ui.Count(groups, "group") + ", " + Ui.Count(moved, "result")
                          + " moved. Load the board again to see it.")
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            results.Children.Add(Ui.Note("The board changed, not the model. \"Undo the last board edit\" is "
                + "beside Load the board on Triage, and it puts the board back as it was before this."));

            session.Record(ActivityKind.Exchange,
                "applied a returned worksheet: " + Ui.Count(groups, "group") + " moved");
        }

        /// <summary>Which board group each issue id this project has sent out came from.</summary>
        private static Dictionary<string, string> SentSheets(Session session)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var saved = session.Store.Section(ProjectStore.ClashSection)["sheetIssues"];

            foreach (var id in saved.Keys)
            {
                var group = saved[id].AsString();
                if (!string.IsNullOrEmpty(group)) map[id] = group!;
            }

            return map;
        }

        /// <summary>
        /// Record what this worksheet sent, so a reply is readable a fortnight later.
        ///
        /// The id is derived from the group name, so it could be recomputed - but only while the
        /// board still holds that group under that name, and a reply usually arrives after a re-run
        /// has renamed something.
        /// </summary>
        private static void RememberSheet(Session session, IEnumerable<IssueLine> lines)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);
            var saved = section["sheetIssues"];

            if (saved.Kind != JsonKind.Object)
            {
                saved = JsonValue.Object();
                section.Set("sheetIssues", saved);
            }

            foreach (var line in lines) saved.Set(line.IssueId, JsonValue.String(line.Group));

            session.Store.Save();
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
        /// <summary>
        /// Six packs come back on Thursday. This is where they become one.
        ///
        /// The single-file read above answers "what did they say"; this answers "what did all of
        /// them say", which is the question a coordinator actually has and the one they were
        /// answering by opening six files and retyping. It lands on the same board through the same
        /// path, so a merged return behaves exactly like a single one from here on.
        /// </summary>
        private static UIElement Together()
        {
            const string Ask = "leave it and tell me";
            const string Latest = "take the most recent";

            var files = Ui.Stack();
            var results = Ui.Stack();
            var rows = new List<TextBox>();

            var policy = StatusConflicts.Ask;

            void AddRow(string? path)
            {
                var box = Ui.Text(path ?? string.Empty, null, 300);
                rows.Add(box);

                var row = Ui.PathField("Returned pack " + rows.Count.ToString(CultureInfo.InvariantCulture),
                                       box, PathKind.Open, "BCF|*.bcf|All files|*.*");

                files.Children.Add(row);
            }

            AddRow(null);
            AddRow(null);

            var more = Ui.Quiet("Another one", () => AddRow(null));

            var merge = Ui.Runner("Bring them together", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Problem(Host.NoModel)); return; }

                var returns = new List<MergeReturn>();
                var baseline = new List<BcfTopic>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var box in rows)
                {
                    var file = box.Text.Trim();
                    if (file.Length == 0) continue;

                    if (!File.Exists(file))
                    {
                        results.Children.Add(Ui.Problem("There is no file at " + file + "."));
                        return;
                    }

                    job.Say("Reading " + System.IO.Path.GetFileName(file) + "...");

                    using (var stream = File.OpenRead(file))
                    {
                        var read = BcfReader.Read(stream);

                        foreach (var warning in read.Warnings) results.Children.Add(Ui.Note(warning));

                        // The party is the file's own name. Asking somebody to type "Mechanical"
                        // beside a file called Mechanical.bcf is asking them to do the tool's job,
                        // and the name is what the conflict list has to say out loud anyway.
                        returns.Add(new MergeReturn(System.IO.Path.GetFileNameWithoutExtension(file), read.Topics));

                        // The pack as it went out is not always to hand, so it is reconstructed from
                        // the first sight of each topic. That is right by construction: every return
                        // starts from the same pack, so the first copy of a topic IS the baseline
                        // until somebody has changed it.
                        foreach (var topic in read.Topics)
                            if (seen.Add(topic.Guid)) baseline.Add(Copy(topic));
                    }
                }

                if (returns.Count < 2)
                {
                    results.Children.Add(Ui.Empty("Give it two or more.",
                        "One pack on its own is what the section above is for."));
                    return;
                }

                job.Say("Merging...");
                var merged = BcfMerge.Merge(baseline, returns, policy);

                results.Children.Add(Ui.Line(merged.Explain(), 1, true));

                results.Children.Add(Ui.Across(
                    Ui.Pill(Ui.Count(merged.Changed, "answer"), merged.Changed > 0 ? Tone.Good : Tone.Plain),
                    Ui.Pill(Ui.Count(merged.Comments, "comment")),
                    Ui.Pill(Ui.Count(merged.Added, "raised"), merged.Added > 0 ? Tone.Good : Tone.Plain),
                    Ui.Pill(Ui.Count(merged.Conflicts.Count, "disagreement"),
                            merged.Conflicts.Count > 0 ? Tone.Warn : Tone.Plain)));

                if (merged.Conflicts.Count > 0)
                {
                    results.Children.Add(Ui.Eyebrow("Nobody won these"));

                    var clashRows = merged.Conflicts.Take(100).Select(c => new TableRow(c,
                        c.Title,
                        c.Field,
                        c.Baseline,
                        string.Join("; ", c.Answers.Select(a => a.Party + ": " + a.Value)))
                    {
                        Tone = Tone.Warn,
                    }).ToList();

                    results.Children.Add(Ui.Table(clashRows,
                        ("Issue", 200), ("On", 90), ("Still says", 90), ("They said", 0)));

                    results.Children.Add(Ui.Note("These kept what they had. Two people who answered "
                        + "the same issue differently were sent the same pack at the same time and "
                        + "neither saw the other, so neither answer wins by being an answer. Every "
                        + "comment from both is on the topic below, which is usually enough to "
                        + "settle it."));
                }

                Remember(session, merged.Topics.Select(t => t.Guid));
                Reply(session, merged.Topics, results);

                session.Record(ActivityKind.Exchange,
                    "merged " + Ui.Count(returns.Count, "returned pack"), merged.Explain());
            });

            return Ui.Marked(Glyphs.Layers, "Bring several answers together",
                "Six packs went out on Monday and six come back on Thursday, each holding the same "
                + "issues with a handful answered. This reads all of them at once.",
                files,
                Ui.Actions(more),
                Ui.Field("When two people disagree",
                         Ui.Segmented(new[] { Ask, Latest }, Ask,
                                      pick => policy = pick == Latest ? StatusConflicts.Latest : StatusConflicts.Ask),
                         "Leaving it is right when the packs went out together, which is the usual "
                         + "case. Take the most recent when reviews were sequential and a later "
                         + "reviewer saw the earlier answer."),
                Ui.Actions(merge),
                results);
        }

        /// <summary>
        /// A topic as it was before anybody answered, for use as the baseline.
        ///
        /// Only the fields a return can change are reset; everything else is what the pack carried.
        /// </summary>
        private static BcfTopic Copy(BcfTopic from)
        {
            var copy = new BcfTopic(from.Guid, from.Title, from.CreationDate, from.CreationAuthor)
            {
                TopicType = from.TopicType,
                TopicStatus = from.TopicStatus,
                Priority = from.Priority,
                Stage = from.Stage,
                Description = from.Description,
                AssignedTo = from.AssignedTo,
                DueDate = from.DueDate,
            };

            foreach (var viewpoint in from.Viewpoints) copy.Viewpoints.Add(viewpoint);

            return copy;
        }

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
                ? Ui.Done("Applied to " + Ui.Count(groups, "group") + ", "
                          + Ui.Count(moved, "result") + " moved. Load the board again to see it.")
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            results.Children.Add(Ui.Note("The board changed, not the model. \"Undo the last board edit\" is "
                + "beside Load the board on Triage, and it puts the board back as it was before this."));

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

        /// <summary>
        /// One BCF topic per group, with its camera, its components and optionally its picture.
        ///
        /// Shared with the graph so an overnight run writes the same file the button does. A second
        /// implementation of "what a topic is" is how the automated export and the manual one come
        /// to disagree about something a client is reading.
        /// </summary>
        /// <param name="session">The document.</param>
        /// <param name="board">The board to export.</param>
        /// <param name="snapshots">Whether to render a picture per topic.</param>
        /// <param name="job">Progress, or <see cref="Job.Silent"/>.</param>
        internal static IReadOnlyList<BcfTopic> TopicsOf(Session session, ClashPipelineResult board,
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
                    if (!job.Step(topics.Count, board.Groups.Count, "topics built")) break;

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
