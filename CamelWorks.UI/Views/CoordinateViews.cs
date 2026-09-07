using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
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
        /// The findings the last read reported present again after somebody had closed them.
        ///
        /// Finding ids rather than items. A regression is recorded by the register at the moment
        /// the read merged a closed finding that this run still reports, and the status lattice
        /// never demotes it, so there is nothing on the item that says a result came back.
        /// </summary>
        internal static HashSet<string> Regressed()
        {
            var regressed = new HashSet<string>(StringComparer.Ordinal);
            var delta = LastDelta;

            if (delta != null) foreach (var id in delta.Regressed) regressed.Add(id);

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
        internal static bool Changed(ClashGroup group, ICollection<string> regressed) =>
            group.Items.Any(i => i.Status == FindingStatus.New
                                 || (i.FindingId != null && regressed.Contains(i.FindingId)));

        /// <summary>
        /// What changed between the last read and the run before it.
        ///
        /// Held beside the board rather than recomputed, because the journal the comparison is
        /// against is rewritten by the read that produced it — ask again afterwards and every
        /// answer is "unchanged".
        /// </summary>
        internal static CheckDelta? LastDelta { get; private set; }

        // ===================================================================================
        // Building the board
        // ===================================================================================

        /// <summary>
        /// The board is not loaded — and the press that loads it, here.
        ///
        /// The board used to be a tab, so every screen that needs one sent people to it. It is not
        /// a tab any more: what a coordinator works is the results grid and the issue register, and
        /// the board is the derived thing underneath the report, the BCF and the clash file. So the
        /// screens that need one build it where they stand, and nobody navigates to fetch a
        /// prerequisite.
        /// </summary>
        /// <param name="why">What this tab cannot do until the board exists.</param>
        internal static UIElement LoadHere(string why)
        {
            var said = Ui.Stack();

            var runner = Ui.Runner("Load the board", said, job =>
            {
                var session = Host.Current;

                if (session == null) { said.Children.Add(Ui.Line(Host.NoModel)); return; }

                var notes = new List<string>();
                var board = Build(session, job, notes);

                if (board == null)
                {
                    said.Children.Add(Ui.Problem("The board could not be built: "
                        + (session.ClashProblem ?? "nothing was found to build it from.")));
                    return;
                }

                foreach (var note in notes) said.Children.Add(Ui.Note(note));

                said.Children.Add(Ui.Done("The board holds " + Ui.Count(board.Groups.Count, "issue")
                    + ". Press this tab's own button now."));
            }, primary: false);

            return Ui.Stack(Ui.Line(why, 0.85), runner, said);
        }

        /// <summary>
        /// Read the clash check and run the rules over what it recorded.
        ///
        /// One read, not two. The board used to read the host itself and the register alongside
        /// it, so the items the rules ran over and the findings that held the decisions were two
        /// descriptions of one document that disagreed the moment anything was decided between
        /// them. The check reads the host into findings, the register takes them, and the items
        /// are projected back out of the register — which is what makes a status, an assignee or a
        /// hand-made group survive to the next run.
        ///
        /// Every coordinate is in metres, because every threshold above this line — a grouping
        /// distance, a tolerance, a proximity band — is stated in metres, and a millimetre model
        /// would otherwise group everything within five millimetres and report one group per
        /// clash.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="job">The run this is part of, for progress and for stopping.</param>
        /// <param name="notes">Anything the reader has to know, added as it is found.</param>
        /// <param name="trying">
        /// A rule set to use instead of the saved one, so the Rules tab can show what its edits
        /// would do before anybody commits them. Null reads the project file, which is what every
        /// other caller wants.
        /// </param>
        /// <param name="preview">
        /// Whether this is the Rules tab trying a rule set out. A preview writes no journal,
        /// records no board and does not become what the report and the BCF are taken from: a
        /// journal written by a preview is what the next real read would compare against, so every
        /// run after it would report nothing changed.
        /// </param>
        internal static ClashPipelineResult? Build(Session session, Job job, ICollection<string> notes,
                                                   ClashRuleSet? trying = null, bool preview = false)
        {
            if (!session.Clash.IsAvailable) return Hydrate(session, job, notes, trying, preview);

            var check = new ClashCheck(session.Clash, session.MetresPerUnit, session.ClashProblem);
            var register = session.Findings.Findings;

            job.Say("Reading the clash engine...");

            var read = check.Read(null, seen => job.Step(seen, "results read"));

            foreach (var note in read.Notes) notes.Add(note);

            // Read before the journals are written. A write replaces a run's journal, so what is
            // current now is what the last run stood at, and it is what this run is compared
            // against.
            var before = RunJournal.Together(
                check.Key, read.Runs.Select(run => RunJournal.Current(session.Store, check.Key, run.Id)));

            var now = DateTime.UtcNow.Ticks;

            var intake = register.Observe(FindingSource.Clash, read.Findings, now,
                                          origin: session.Store.Path, scope: read.Scope);

            var delta = CheckDelta.Between(before, intake, register, read.Scope, check.Aliases);

            // The held findings, not the ones the read produced: the register merges a fresh
            // finding into the one it holds, and the held object is what carries the decisions and
            // what the board draws.
            //
            // In the order the host reported them, not the order the register enumerates. The
            // register is a dictionary and its enumeration order changes on any remove, so a board
            // ordered by it would reorder its rows and its groups with no edit to the model or the
            // rules.
            var held = Held(register, read);

            // Not on a preview: a journal written here is what the next real read compares
            // against, so a preview would leave every following run reporting nothing changed.
            if (!preview)
                foreach (var run in read.Runs)
                    RunJournal.Of(check.Key, run.Id, now, held).Write(session.Store);

            job.Say("Describing " + Ui.Count(held.Count, "result") + "...");

            var items = Project(session, job, read.Runs).Items(held);

            if (items.Count == 0)
            {
                notes.Add("The clash engine has tests but no results. Run them in Clash Detective first.");
                return null;
            }

            // Cross-test duplicates go first, before anything counts. Two tests that both report
            // one pipe through one wall are one problem; leaving both on the board means the same
            // conflict is assigned twice, reported twice, and closed once.
            job.Say("Collapsing duplicates...");

            var collapse = DuplicateCollapse.Across(items, session.Profile.DuplicateBand);
            var survivors = collapse.Kept;

            job.Say("Applying rules...");

            var rules = trying
                        ?? ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Rules]);

            var options = rules.ToOptions(session.Profile.ClashProximity);
            options.CollapsedDuplicates = collapse.Count;

            // Read from the file rather than from the last run. A hand assignment is a decision
            // about a group, not about a run — reading it off the previous run would mean losing
            // every assignment the first time somebody opens the board on a machine that has not
            // run it before.
            options.Decisions = Decisions(session);
            options.PinnedGroups = Pinned(session);

            // A raised issue is not something the clash engine reported, so it is added after the
            // comparison rather than before it: compared against the engine's results it would be
            // missing from every run and called resolved every week.
            var everything = new List<ClashItem>(survivors);
            var issues = Issues(session);
            var raised = IssueItems.Open(issues);
            everything.AddRange(raised);

            var board = ClashPipeline.Run(everything, options);

            Instruct(board, issues);

            if (raised.Count > 0)
                notes.Add(Ui.Count(raised.Count, "issue") + " raised by hand "
                          + (raised.Count == 1 ? "is" : "are") + " on this board alongside the engine's "
                          + "results. They group, assign, report and export like anything else; the "
                          + "test column says where they came from.");

            notes.Add(delta.Explain());

            if (collapse.Count > 0)
                notes.Add(Ui.Count(collapse.Count, "result")
                          + " were the same conflict found by more than one test, and were folded "
                          + "into the one the funnel keeps. They are counted, not hidden.");
            if (rules.IsDefault) notes.Add("Nothing is configured, so the built-in rules ran: " + board.Funnel);

            // A preview is not a run: it is neither recorded nor held as the board the report, the
            // BCF and the clash file are taken from.
            if (preview) return board;

            LastBoard = board;
            LastBoardLabel = session.Profile.ProjectName;
            LastDelta = delta;

            Save(session, board, offered: held);
            return board;
        }

        /// <summary>
        /// The findings a read reported, as the register holds them, in the order the host read
        /// them.
        ///
        /// One entry per finding: two results of one test that quantise into one finding are one
        /// row, and taking the read's order without that would draw the row twice.
        /// </summary>
        /// <param name="register">The register the read was observed into.</param>
        /// <param name="read">What the check read.</param>
        private static IReadOnlyList<Finding> Held(FindingRegister register, CheckRead read)
        {
            var held = new List<Finding>(read.Findings.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fresh in read.Findings)
            {
                if (fresh == null || !seen.Add(fresh.Id)) continue;

                var one = register.Find(fresh.Id);

                if (one != null && one.IsPresent && read.Scope(one)) held.Add(one);
            }

            return held;
        }

        /// <summary>
        /// Rebuild the board out of the record when there is no clash engine to read.
        ///
        /// This is the promise that makes CamelWorks worth installing on a Simulate seat, which is
        /// most of the seats on most jobs. Simulate has no Clash Detective, so tests cannot be read
        /// and cannot be run — but triaging a board, assigning it, reporting on it and issuing BCF
        /// are not clash detection, and the findings a Manage seat recorded are enough to rebuild
        /// what it last ran.
        ///
        /// The register and not the clash snapshot. The snapshot keeps a status and a quantised
        /// cell per result; the register keeps the elements, the exact point, the status, the
        /// assignee, the comments, the group, the priority, the stage and the topic guid. So a
        /// hydrated board is now positioned exactly rather than to within half a cell, and it
        /// carries what the snapshot never held.
        ///
        /// The result is labelled as a record rather than a reading, every time, because the one
        /// way this feature could do harm is by looking like a live board. It is not saved back
        /// either: this seat has read no host, and writing its board over the record would take the
        /// host's own facts off every finding on it.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="job">The run this is part of, for progress and for stopping.</param>
        /// <param name="notes">Anything the reader has to know, added as it is found.</param>
        /// <param name="trying">A rule set to use instead of the saved one, or null for the saved one.</param>
        /// <param name="preview">
        /// Whether this is the Rules tab trying a rule set out. A hydrated board records nothing
        /// either way; a preview additionally does not become the board the report and the BCF are
        /// taken from.
        /// </param>
        private static ClashPipelineResult? Hydrate(Session session, Job job, ICollection<string> notes,
                                                    ClashRuleSet? trying = null, bool preview = false)
        {
            job.Say("Reading what this project has recorded...");

            // Ordered explicitly: the register is a dictionary and its enumeration order changes
            // on any remove, so a board ordered by it would reorder its rows and its groups
            // between two loads of the same record.
            var held = session.Findings.Findings.From(FindingSource.Clash)
                              .Where(finding => finding.IsPresent)
                              .OrderBy(finding => finding.Id, StringComparer.Ordinal).ToList();

            job.Say("Describing " + Ui.Count(held.Count, "result") + "...");

            // No folder lookup: naming a test's folder needs the host's test list, and this seat
            // has no clash engine to ask.
            var items = Project(session, job, Array.Empty<CheckRun>()).Items(held);

            // What the record holds and this seat could not rebuild: a clash finding needs two
            // resolved elements and a point to be an item. Counted and said, because a board that
            // silently drew fewer results than the project holds is the one failure a rebuilt
            // board must not have.
            var unusable = held.Count - items.Count;

            if (items.Count == 0)
            {
                notes.Add(session.ClashProblem ?? "There is no clash engine in this Navisworks edition.");
                notes.Add("There is no recorded run in this project either, so there is nothing to open. "
                          + "A board comes from a Navisworks Manage seat running the tests once; after "
                          + "that this seat can triage, assign, report and issue it.");
                return null;
            }

            // Cross-test duplicates go first, before anything counts, exactly as on a read. Two
            // tests that both reported one pipe through one wall are one problem, and a rebuilt
            // board that drew both would assign and report the same conflict twice.
            job.Say("Collapsing duplicates...");

            var collapse = DuplicateCollapse.Across(items, session.Profile.DuplicateBand);

            job.Say("Applying rules...");

            var rules = trying
                        ?? ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Rules]);
            var options = rules.ToOptions(session.Profile.ClashProximity);
            options.CollapsedDuplicates = collapse.Count;
            options.Decisions = Decisions(session);
            options.PinnedGroups = Pinned(session);

            var raised = Issues(session);

            var everything = new List<ClashItem>(collapse.Kept);
            everything.AddRange(IssueItems.Open(raised));

            var board = ClashPipeline.Run(everything, options);

            Instruct(board, raised);

            notes.Add(Ui.Count(items.Count, "result") + " from what this project recorded of the last run.");
            notes.Add("This board was rebuilt from the record, because this Navisworks edition has "
                      + "no clash engine. Everything on it can be triaged, assigned, reported and "
                      + "issued as BCF. Two things it cannot do: run a test, and notice a conflict "
                      + "that has appeared since the run it came from.");

            if (collapse.Count > 0)
                notes.Add(Ui.Count(collapse.Count, "result")
                          + " were the same conflict found by more than one test, and were folded "
                          + "into the one the funnel keeps. They are counted, not hidden.");

            if (unusable > 0)
                notes.Add(Ui.Count(unusable, "recorded result")
                          + " could not be rebuilt and " + (unusable == 1 ? "is" : "are")
                          + " not on this board: a clash needs two resolved elements and a point, "
                          + "and the record does not hold both. "
                          + (unusable == 1 ? "It is" : "They are")
                          + " still in the project's findings.");

            if (preview) return board;

            LastBoard = board;
            LastBoardLabel = session.Profile.ProjectName;

            // No comparison and no save. There was no read to compare against, and this seat holds
            // nothing the record does not already have.
            LastDelta = null;

            return board;
        }

        /// <summary>
        /// How a held finding becomes the item the rules run over.
        ///
        /// The three lookups an item needs and a finding does not carry: the level a height falls
        /// on, the discipline a model is, and what a model is called. None of them resolves an
        /// element key — the model scope is in the key itself and the level is derived from the
        /// height — so a board of eighteen thousand results costs no walk of the federation.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="job">The run this is part of, for the levels pass.</param>
        /// <param name="runs">The runs that were read, for the folder each test sits in.</param>
        private static ClashProjection Project(Session session, Job job, IReadOnlyList<CheckRun> runs)
        {
            var scale = session.MetresPerUnit;
            var levels = Levels(session, job, scale);

            // Model display names once, not once per result: eighteen thousand results times a
            // linear scan over the model list is a pause somebody can feel.
            var modelNames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var model in session.Model.Models)
                if (!modelNames.ContainsKey(model.Scope)) modelNames[model.Scope] = model.DisplayName;

            var folders = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var run in runs) folders[run.Id] = run.Folder;

            return new ClashProjection(scale)
            {
                ModelName = key => modelNames.TryGetValue(key.ModelScope, out var name) ? name : null,
                Discipline = key => session.Profile.DisciplineOf(key.ModelScope),
                Level = height => levels.LevelAt(height),
                Folder = id => id != null && folders.TryGetValue(id, out var folder) ? folder : null,
            };
        }


        private static LevelSet? _levels;
        private static string? _levelsShape;

        private static LevelSet Levels(Session session, Job job, double scale)
        {
            // Deriving levels reads the elevation of every element in the federation — the most
            // expensive read the board makes, and one whose answer only changes when the
            // federation does. Cached against its shape, so the second board of the day costs
            // nothing and a reload after an append derives afresh.
            var shape = string.Join("|", session.Model.Models.Select(m => m.Scope))
                        + "@" + scale.ToString("R", CultureInfo.InvariantCulture);

            if (_levels != null && string.Equals(_levelsShape, shape, StringComparison.Ordinal))
                return _levels;

            job.Say("Working out levels...");

            var elevations = new List<double>();

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!item.HasGeometry) continue;
                elevations.Add(item.Bounds.MinZ * scale);

                if (!job.Step(elevations.Count, "elements measured"))
                    return LevelSet.Empty;
            }

            var derived = LevelSet.Derive(elevations);

            _levels = derived;
            _levelsShape = shape;

            return derived;
        }

        /// <summary>
        /// The board as it stood before the last run, for the per-party comparison.
        ///
        /// The per-party counts and the time it was taken ride inside the snapshot value, so moving
        /// a snapshot into the undo slot moves them with it.
        /// </summary>
        private static ClashSnapshot? Previous(Session session)
        {
            var section = session.Store.Section(ProjectStore.ClashSection);

            var read = ClashSnapshotJson.FromJson(ClashSnapshotJson.Migrated(
                section[ClashKeys.Previous], section[ClashKeys.Retired.PreviousParties],
                section[ClashKeys.Retired.PreviousTaken]));

            return read != null && read.OpenByParty.Count > 0 ? read : null;
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
            var saved = session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Pinned];

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
            var saved = section[ClashKeys.Pinned];

            if (saved.Kind != JsonKind.Object)
            {
                saved = JsonValue.Object();
                section.Set(ClashKeys.Pinned, saved);
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
            var saved = session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Pinned];
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
        private static void Instruct(ClashPipelineResult board, IssueRegister issues)
        {
            foreach (var issue in issues.Issues)
            {
                var said = IssueItems.Instruction(issue);

                if (!said.IsStated) continue;

                var group = board.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, issue.Title, StringComparison.Ordinal));

                if (group != null && !group.Instruction.IsStated) group.Instruct(said);
            }
        }

        /// <summary>Every issue on this project, from the project file.</summary>
        internal static IssueRegister Issues(Session session) =>
            IssueRegister.FromJson(session.Store.Section(ProjectStore.IssuesSection)["register"]);

        /// <summary>The decisions made by hand, which outrank the rules and survive a re-run.</summary>
        private static IReadOnlyDictionary<string, GroupDecision> Decisions(Session session)
        {
            var decisions = new Dictionary<string, GroupDecision>(StringComparer.Ordinal);
            var saved = session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Decisions];

            foreach (var name in saved.Keys)
                decisions[name] = new GroupDecision(saved[name]["party"].AsString(),
                                                   saved[name]["priority"].AsString(),
                                                   Resolution.FromJson(saved[name]["instruction"]));

            return decisions;
        }

        /// <summary>The people on this job, or an empty roster when nobody has named any.</summary>
        internal static TeamRoster Roster(Session session) =>
            TeamRoster.FromJson(session.Store.Section(ProjectStore.ProfileSection)["roster"]);

        /// <summary>Who is on which group.</summary>
        internal static AssignmentBook Assignments(Session session) =>
            AssignmentBook.FromJson(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Assignments]);

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
            var decisions = section[ClashKeys.Decisions];

            if (decisions.Kind != JsonKind.Object)
            {
                decisions = JsonValue.Object();
                section.Set(ClashKeys.Decisions, decisions);
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
        /// How many times the board has been written down this session.
        ///
        /// Not a version and not interesting in itself. It exists so a screen holding figures from
        /// a load can tell that somebody has changed the board since — assigned a group, marked
        /// results, applied a returned worksheet — and say so, rather than showing last load's
        /// numbers over this load's rows.
        /// </summary>
        internal static int BoardEdits { get; private set; }

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
        /// <param name="offered">
        /// The findings the board was built from, for the board facts written back onto them. A
        /// caller with a board and no read passes nothing, and the findings the board never drew
        /// are then left as they were.
        /// </param>
        private static void Save(Session session, ClashPipelineResult board, string? summary = null,
                                 IReadOnlyList<Finding>? offered = null)
        {
            BoardEdits++;

            var section = session.Store.Section(ProjectStore.ClashSection);
            var previous = section[ClashKeys.Snapshot];

            if (ClashSnapshotJson.IsSnapshot(previous))
            {
                // A snapshot written before ClashSnapshotJson existed is a bare array with its
                // counts in two sibling keys. Folded in as it moves, so the per-party comparison
                // does not disappear on the first save after an upgrade.
                var carried = ClashSnapshotJson.Migrated(previous,
                                                        section[ClashKeys.Retired.Parties],
                                                        section[ClashKeys.Retired.Taken]);

                if (carried != null) section.Set(ClashKeys.Previous, carried);
            }

            // The four keys the two shapes were spread across. Removed once the value that
            // replaced them is written, so a stale count cannot be read back later.
            section.Remove(ClashKeys.Retired.Parties);
            section.Remove(ClashKeys.Retired.Taken);
            section.Remove(ClashKeys.Retired.PreviousParties);
            section.Remove(ClashKeys.Retired.PreviousTaken);

            var snapshot = ClashSnapshot.Of(board, takenUtc: DateTimeOffset.UtcNow);

            // Raised issues are kept in their own list and left out of the snapshot. The snapshot
            // is what the per-party comparison reads, and a record the engine can never produce
            // would read as resolved on every single run.
            section.Set(ClashKeys.Snapshot, ClashSnapshotJson.ToJson(snapshot,
                r => !string.Equals(r.TestName, IssueItems.RaisedByHand, StringComparison.Ordinal)));

            // What the board did with each result, back onto the finding behind it: the group and
            // whether it was made by hand, and the rule that took a result off the board. Without
            // them a seat with no clash engine rebuilds a board with no hand grouping, and every
            // suppressed result arrives as new the moment somebody turns the rule off. The
            // findings the read offered are passed too, so a result folded into a cross-test
            // duplicate loses the facts it was recorded with when it was the survivor.
            ClashFindings.Recorded(board, session.Findings.Findings, offered);

            var kept = session.Findings.Save();

            session.Record(ActivityKind.Regroup, summary ?? "ran the rules: " + board.Funnel,
                           kept ? null
                                : "The board's own facts were not recorded: "
                                  + (session.Findings.LastSaveProblem ?? "there was nowhere to write."));
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
            var previous = section[ClashKeys.Previous];

            if (!ClashSnapshotJson.IsSnapshot(previous)) return false;

            var current = section[ClashKeys.Snapshot];

            section.Set(ClashKeys.Snapshot, previous);
            if (ClashSnapshotJson.IsSnapshot(current)) section.Set(ClashKeys.Previous, current);
            else section.Remove(ClashKeys.Previous);

            return session.Store.Save();
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

        /// <summary>
        /// Select both sides of every picked clash.
        ///
        /// Both sides, because a clash is a pair and looking at one half of it tells you nothing
        /// about why it is a clash.
        /// </summary>
        private static void Reveal(Session session, IReadOnlyList<object> picked, Panel status)
        {
            status.Children.Clear();

            var keys = new List<ElementKey>();

            foreach (var clash in picked.OfType<ClashItem>())
            {
                keys.Add(clash.Key.A);
                keys.Add(clash.Key.B);
            }

            if (keys.Count == 0)
            {
                status.Children.Add(Ui.Line("Nothing picked.", 0.8));
                return;
            }

            // Framed on the selection just made, not on the keys again. ZoomTo resolves every
            // key back to its element, which for a key that is not an instance GUID indexes the
            // whole federation - a second time, inside the same press.
            session.Model.Select(keys);
            session.View.ZoomToSelection(2.0);

            status.Children.Add(Ui.Done("Selected both sides of "
                                        + Ui.Count(picked.Count, "clash") + "."));
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
                    return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

                _rules = ClashRuleSet.FromJson(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Rules]);

                Redraw(session);

                var save = Ui.Button("Save rules", () =>
                {
                    _status.Children.Clear();

                    session.Store.Section(ProjectStore.ClashSection).Set(ClashKeys.Rules, _rules.ToJson());

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
                    // A preview and not a run: it writes no journal, records no board and does
                    // not become what the report and the BCF are taken from. Without that, trying
                    // a rule out left the next real read comparing against the preview and
                    // reporting nothing changed.
                    var board = CoordinateViews.Build(session, job, notes, _rules, preview: true);

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

                // The pipeline is the working material, so it is the stage: three stretches under
                // small-caps labels, in the order the rules run. The one action is the preview -
                // the save is the quiet act beside the answer it makes true.
                return Bench.Surface(
                    null,
                    tryOut,
                    Ui.Stack(
                        Ui.Eyebrow("1 · Suppress and flag"),
                        Ui.Small("Applied to every result before anything else."),
                        _filters,
                        Ui.Eyebrow("2 · Group"),
                        Ui.Small("Outermost rule first. Empty means the built-in stack."),
                        _grouping,
                        Ui.Eyebrow("3 · Assign"),
                        Ui.Small("First match wins."),
                        _assigns,
                        Ui.Actions(save),
                        _status,
                        Ui.Small("With nothing here the board still works: it groups by model pair, "
                                 + "then level, then proximity, and assigns nothing. These rules "
                                 + "narrow that; they do not switch it on.")));
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

                var add = Ui.Icon(Glyphs.Add, "Add this rule", () =>
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

                var add = Ui.Icon(Glyphs.Add, "Add this rule", () =>
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

                var add = Ui.Icon(Glyphs.Add, "Add this rule", () =>
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
        /// <summary>
        /// Where the board stands, said at the top of every Issue Link tab.
        ///
        /// Everything in Issue Link issues the board, and the board lives in Clash Manager. A tab
        /// that only says so at the press — "the board has not been built yet" — has already let
        /// somebody set up a report of nothing. So each tab opens on the fact: loaded and how many
        /// issues, or not loaded with Clash Manager's Board one press away, loading on arrival.
        /// </summary>
        /// <param name="whenMissing">What pressing on without a board means, on this tab.</param>
        private static UIElement BoardState(string whenMissing)
        {
            var board = LastBoard;

            if (board == null)
                return LoadHere("The board is not loaded. " + whenMissing);

            return Ui.Line("On the board: " + Ui.Count(board.Groups.Count, "issue") + ", "
                + Ui.Count(board.Groups.Sum(g => g.Items.Count), "clash")
                + (string.IsNullOrEmpty(LastBoardLabel) ? string.Empty : " — " + LastBoardLabel)
                + ". This tab sends that board.", 0.75);
        }

        /// <summary>
        /// What leaves after a report or a file has left: the other format, and the way back.
        ///
        /// The Thursday issue is rarely one artefact — the PDF goes to the meeting, the BCF or the
        /// clash file goes to whoever answers in a tool — and the reply lands the following week.
        /// Saying so at the moment of a successful send is what keeps the round trip one workflow
        /// instead of three windows somebody has to remember are related.
        /// </summary>
        /// <param name="into">Where the send just reported.</param>
        /// <param name="also">The other formats worth sending, as tool ids of this app's tabs.</param>
        private static void NextSteps(Panel into, params string[] also)
        {
            var buttons = new List<UIElement?>();

            foreach (var tool in also)
            {
                var here = tool;

                buttons.Add(Ui.Quiet(
                    here == "bcf" ? "Send it as BCF too"
                    : here == "clashxml" ? "Send it as a clash file too"
                    : "Write the report to go with it",
                    () => Ui.Told(Shell.AppWindow.Show("issues", here))));
            }

            into.Children.Add(Ui.Actions(buttons.FirstOrDefault(), buttons.Skip(1).ToArray()));

            into.Children.Add(Ui.Small("When an answered file comes back, the BCF tab reads it "
                + "against this board — and several reviewers' returns come together on Merge."));
        }

        public static UIElement Report() => new ReportScreen().Build();

        /// <summary>The penetration schedule, as its own tool rather than a template choice.</summary>
        internal static UIElement PenetrationsReport() =>
            new ReportScreen("Penetration schedule").Build();

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

            /// <summary>The marker shapes, in the words somebody would use for them.</summary>
            private const string Ball = "a ball";

            private const string Box = "a cube";

            private const string Spike = "a diamond";

            /// <summary>The units choice that means "whatever the document says".</summary>
            private const string FromDocument = "whatever the document says";

            /// <summary>
            /// The units a marker can be converted from.
            ///
            /// Written out rather than borrowed from the formatter's list, which offers "Feet and
            /// inches" - a way of printing a length, not a unit a number can be divided by.
            /// </summary>
            private static readonly string[] Units =
            {
                FromDocument, "Millimetres", "Centimetres", "Metres", "Inches", "Feet",
            };

            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _markers = Ui.Stack();

            /// <summary>What this tab was last told, so it does not ask again.</summary>
            private readonly ToolMemory _kept =
                ToolMemory.Of(Host.Current?.Store, "coordinate.report");

            private readonly TextBox _path;
            private readonly TextBox _template;

            private string _units;

            /// <summary>
            /// Which template this screen is fixed to, or null when the user picks.
            ///
            /// The penetration schedule is its own ribbon button: a structural engineer asking for
            /// the list before a pour is not writing a clash report, and a window that answers
            /// "which of three reports?" first is asking a question they do not have.
            /// </summary>
            private readonly string? _locked;

            internal ReportScreen(string? locked = null)
            {
                _locked = locked;
                if (locked != null) _mode = locked;

                _path = Ui.Text(_kept.Text("folder"), text => _kept.Set("folder", text), 340);

                _template = Ui.Text(_kept.Text("name", Deliverable.DefaultTemplate),
                                    text => _kept.Set("name", text), 340);

                _units = _kept.Text("units", FromDocument) ?? FromDocument;

                _format = _kept.Text("format", "PDF") ?? "PDF";
                _keyPlan = _kept.Flag("keyPlan", true);
                _pictures = _kept.Flag("pictures");
                _perParty = _kept.Flag("perParty");
                _changedOnly = _kept.Flag("changedOnly");
            }

            private string _format;
            private string _mode = FullReport;
            private string _audience = Everyone;
            private bool _changedOnly;
            private bool _pictures;
            private bool _keyPlan;
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

                var format = Ui.Choice(new[] { "PDF", "XLSX", "HTML" }, _format,
                                       f => { _format = f; _kept.Set("format", f); }, 120);
                var audience = Ui.Choice(parties, _audience, a => _audience = a, 180);

                var mode = Ui.Choice(new[] { FullReport, FieldPack, Penetration }, _mode, m =>
                {
                    _mode = m;
                    what.Text = Explain(m);
                });

                // Locked: the template is the tool, so the picker is not a choice this window has.
                var contents = _locked == null ? Ui.Field("Contents", mode) : null;

                var changed = Ui.Check("Only what the last run had not seen", _changedOnly,
                                       v => { _changedOnly = v; _kept.Set("changedOnly", v); });

                var pictures = Ui.Check("A picture of each group", _pictures,
                                        v => { _pictures = v; _kept.Set("pictures", v); });

                var plan = Ui.Check("A plan of the site with every issue marked", _keyPlan,
                                    v => { _keyPlan = v; _kept.Set("keyPlan", v); });

                var split = Ui.Check("One file per party, not one file", _perParty,
                                     v => { _perParty = v; _kept.Set("perParty", v); });

                var write = Ui.Runner("Write the report", _results, Write);

                _results.Children.Add(BoardState("Writing will build it fresh from the tests "
                    + "and the saved rules."));

                _results.Children.Add(Bench.Ready(
                    "Writes the board out for whoever it is for, from a built-in template.",
                    "Each issue takes the next revision letter, and its cover says which one it "
                    + "supersedes."));

                // The rail keeps what is chosen per report; where it lands, what the markers
                // look like and the way over to a file are decided once for the job and live in
                // the window behind the symbols beside the press.
                var foot = new WrapPanel();

                foot.Children.Add(write);
                foot.Children.Add(new Border { Width = 14 });
                foot.Children.Add(Ui.Icon(Glyphs.Folder, "Where it goes and what it is called",
                                          () => Configure("where")));
                foot.Children.Add(Ui.Icon(Glyphs.Sphere, "Markers in the model",
                                          () => Configure("markers")));
                foot.Children.Add(Ui.Icon(Glyphs.Onward, "Send it as an exchange file",
                                          () => Configure("file")));

                return Bench.Surface(
                    Ui.Stack(
                        Bench.Group("",
                            contents,
                            what,
                            Ui.Field("For", audience),
                            Ui.Field("Format", format)),
                        Bench.Group("What else is in it",
                            changed,
                            plan,
                            split,
                            pictures,
                            Ui.Small("A picture moves your view while it runs and puts it back "
                                     + "afterwards. On a board of two hundred groups that is two "
                                     + "hundred renders, so it is off until you ask for it."))),
                    foot,
                    Ui.Stack(
                        _results,
                        _markers));
            }

            /// <summary>
            /// What one symbol beside the press is for, in a window of its own.
            ///
            /// Titled with the section the symbol named. All three used to open the same
            /// three-section window called "Report", so nothing about the window said which of
            /// them had been pressed.
            /// </summary>
            /// <param name="section">Which section the symbol names.</param>
            private void Configure(string section)
            {
                Ui.Page page;

                switch (section)
                {
                    case "markers":
                        page = new Ui.Page("Markers", Markers);
                        break;

                    case "file":
                        page = new Ui.Page("Exchange file", Exchange);
                        break;

                    default:
                        page = new Ui.Page("Where it goes", () => Bench.Group("Where it goes",
                            Ui.PathField("Write to", _path, PathKind.Folder,
                                         hint: "blank writes it beside the document"),
                            Ui.Field("Name it", _template,
                                     "carries " + string.Join(", ", Deliverable.Tokens))));
                        break;
                }

                Ui.Configure(page.Name, null, page);
            }

            /// <summary>
            /// The clashes, in the model.
            ///
            /// A clash is a row on a board and a camera position, and both need somebody driving
            /// the mouse. A walkthrough, a site meeting or a subcontractor's own copy of the model
            /// needs the clash where the clash is: a ball hanging in the air where the duct meets
            /// the beam, visible from across the room and still there tomorrow.
            /// </summary>
            private UIElement Markers()
            {
                var recipe = new MarkerRecipe();

                var shapes = new[] { Ball, Box, Spike };
                var was = _kept.Text("markerShape", Ball) ?? Ball;

                recipe.Shape = was == Box ? MarkerShape.Cube
                             : was == Spike ? MarkerShape.Diamond
                             : MarkerShape.Sphere;

                var shape = Ui.Choice(shapes, shapes.Contains(was) ? was : Ball, pick =>
                {
                    recipe.Shape = pick == Box ? MarkerShape.Cube
                                 : pick == Spike ? MarkerShape.Diamond
                                 : MarkerShape.Sphere;

                    _kept.Set("markerShape", pick);
                }, 140);

                var across = _kept.Number("markerSize", 300);

                recipe.RadiusMetres = across / 2000.0;

                var size = Ui.Text(across.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                         out var mm) || mm <= 0) return;

                    recipe.RadiusMetres = mm / 2000;
                    _kept.Set("markerSize", (int)Math.Round(mm));
                }, 90);

                var units = Ui.Choice(Units, _units, pick => { _units = pick; _kept.Set("units", pick); }, 160);

                var write = Ui.Runner("Make them", _markers, primary: false,
                                      work: job => Mark(job, recipe));

                // One marker per clash, grouped by test, as ordinary geometry the host can select,
                // hide and remove. A setup group rather than its own card: the fields are inputs
                // and the secondary runner sits at the group's foot, with its answer on the stage.
                return Bench.Group("Put the clashes in the model",
                    Ui.Field("Shape", shape),
                    Ui.Field("Across", size, "millimetres, on screen"),
                    Ui.Field("The model is in", units,
                             "only needed if the document does not say"),
                    write);
            }

            private void Mark(Job job, MarkerRecipe recipe)
            {
                _markers.Children.Clear();

                var session = Host.Current;
                if (session == null) { _markers.Children.Add(Ui.Line(Host.NoModel)); return; }

                var board = LastBoard;

                if (board == null)
                {
                    _markers.Children.Add(LoadHere("The board has not been built yet, and this "
                        + "marks what the board holds."));
                    return;
                }

                // The conversion the whole thing turns on. The host reports clash points in the
                // document's units and its OBJ reader takes every vertex as metres.
                var word = _units == FromDocument ? session.Document.Units.ToString() : _units;
                var metres = Measure.MetresPer(word);

                if (metres == null)
                {
                    _markers.Children.Add(Ui.Problem("This document is in \"" + word + "\", which is "
                        + "not a unit CamelWorks converts. Say which unit it is in the box above, or "
                        + "the markers would land in the wrong place and look like an empty model."));
                    return;
                }

                // The board's coordinates were already scaled to metres when it was built, by the
                // session's own factor. What remains is only the difference between that factor
                // and the truth: on a document that declares its units the two agree and this is
                // 1, and on one that declares nothing the session factor was 1 and the picker
                // supplies the whole conversion. Multiplying by the full factor here a second
                // time put every marker a thousandfold off on a millimetre model.
                recipe.MetresPerUnit = session.MetresPerUnit > 0
                    ? metres.Value / session.MetresPerUnit
                    : metres.Value;

                job.Say("Building the geometry...");

                var items = board.Groups.SelectMany(g => g.Items).ToList();
                var mesh = ClashMarkers.Write(items, recipe);

                if (mesh.Refusal != null) { _markers.Children.Add(Ui.Problem(mesh.Refusal)); return; }

                foreach (var warning in mesh.Warnings) _markers.Children.Add(Ui.Note(warning));

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the clash markers",
                    Filter = "Wavefront OBJ|*.obj",
                    FileName = "clash-markers.obj",
                    AddExtension = true,
                    DefaultExt = ".obj",
                };

                if (!Ui.Asked(dialog)) return;

                var obj = dialog.FileName;
                var mtl = System.IO.Path.ChangeExtension(obj, ".mtl");

                try
                {
                    // The .obj names the .mtl beside it, so the two travel together or the colours
                    // are lost without anything saying why.
                    var beside = System.IO.Path.GetFileName(mtl);

                    System.IO.File.WriteAllText(obj,
                        mesh.Text.Replace("mtllib " + mesh.MaterialFile, "mtllib " + beside));

                    if (mesh.Materials.Length > 0) System.IO.File.WriteAllText(mtl, mesh.Materials);
                }
                catch (Exception e)
                {
                    _markers.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _markers.Children.Add(Ui.Done("Wrote " + mesh.Explain() + " to " + obj));

                _markers.Children.Add(Ui.Actions(Ui.Primary("Bring them in", () =>
                {
                    try
                    {
                        _markers.Children.Add(session.Model.Document.TryAppendFile(obj)
                            ? Ui.Done("Appended. They are in the Selection Tree under their tests, "
                                      + "and in the host's file list if you want them out again.")
                            : Ui.Problem("The host would not append that file."));
                    }
                    catch (Exception e)
                    {
                        _markers.Children.Add(Ui.Problem("Could not append it: " + Ui.Describe(e)));
                    }
                })));

                // Kept at the press rather than at every keystroke: this is the moment somebody
                // committed to the settings, and the next run opens on them.
                _kept.Keep();

                session.Record(ActivityKind.Export, "made clash markers: " + mesh.Explain(), obj);
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
                var open = Ui.Quiet("Clash file...", () =>
                {
                    var problem = ToolViews.Open("clashxml");
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                // The clash file has its own tool; this is the way over to it, at the end of setup.
                return Bench.Group("Exchange file",
                    open,
                    Ui.Small("BCF, which Solibri, BIMcollab, Revizto and every other reviewing "
                             + "tool opens. A file that comes back lands on the elements it left "
                             + "from."));
            }

            /// <summary>What the chosen template actually produces, in the reader's terms.</summary>
            private static string Explain(string mode) => mode switch
            {
                FieldPack => "The same board set for a site cabin: one issue per page, type big "
                             + "enough to read one-handed, and a line to sign against.",

                Penetration => "Every service that goes through structure - what the model says it "
                               + "is and where. No opening size derived and no host thickness "
                               + "inferred: those dimensions are used to cut concrete and must not be "
                               + "estimated.",

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
                        "No group on the board matches this filter. Check the selected audience if "
                        + "results were expected."));
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

                // Not after a penetration schedule: that goes to the structural engineer as the
                // document it is, and nobody answers a pour list through a BCF tool.
                if (_mode != Penetration) NextSteps(_results, "bcf", "clashxml");

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
                                   ICollection<string> regressed, Job job)
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
                    _results.Children.Add(Ui.Empty("No recipients.",
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

                if (written > 0 && _mode != Penetration) NextSteps(_results, "bcf", "clashxml");

                session.Record(ActivityKind.Report,
                    "wrote " + Ui.Count(written, "clash report") + ", one per party");
            }

            /// <summary>Which groups belong in a report for this audience.</summary>
            private IReadOnlyList<ClashGroup> Chosen(ClashPipelineResult board, string audience,
                                                     ICollection<string> regressed)
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
                Deliverable.Read(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Issued])
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

                section.Set(ClashKeys.Issued, Deliverable.Write(register));
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
                                         ICollection<string> regressed,
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
                                               ICollection<string> regressed,
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
                + "opening size is derived and no host thickness is inferred: both are used to "
                + "cut concrete and are not estimated here.");

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
                                  ICollection<string> regressed)
        {
            var delta = LastDelta;
            if (delta == null) return;

            var fresh = groups.Sum(g => g.Items.Count(i => i.Status == FindingStatus.New));

            var back = groups.Sum(g => g.Items.Count(
                i => i.FindingId != null && regressed.Contains(i.FindingId)));

            document.Heading("Since the last run");

            if (delta.NoPreviousRun)
            {
                document.Paragraph("No previous run recorded, so there is nothing to compare this "
                    + "board against. The next run will report what changed.");
                return;
            }

            document.Paragraph(fresh.ToString("N0", CultureInfo.InvariantCulture) + " new, "
                + back.ToString("N0", CultureInfo.InvariantCulture) + " back again, "
                + delta.Gone.Count.ToString("N0", CultureInfo.InvariantCulture)
                + " gone from the model since the previous run.");

            if (back > 0)
                document.Paragraph(
                    "Something recorded as resolved is in this list again. Either a fix was undone or a "
                    + "model went backwards, and it is the first thing to look at — native clash detection "
                    + "cannot tell you this at all, because the result simply reappears looking new.");

            if (delta.MatchedOnAlias > 0)
                document.Paragraph(delta.MatchedOnAlias.ToString("N0", CultureInfo.InvariantCulture)
                    + " of these matched the previous run on a neighbouring position rather than "
                    + "exactly, which means the model has moved under them. Their history is "
                    + "carried on that tolerance rather than on an exact match.");
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
                results.Children.Add(LoadHere("Without a board there is nothing to reconcile "
                    + "these answers against. Load it, then read the file again."));
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
                    + "nothing on the board to reconcile. Treat them as an external list."));
                return;
            }

            results.Children.Add(Ui.Line("What this reply says about the board", 1, true));
            results.Children.Add(Ui.Line(report.Explain(), 0.8));

            var rows = report.Matched.Select(m => new TableRow(m,
                m.GroupName,
                m.Topic.TopicStatus,
                FindingStatusWords.Of(m.Current),
                m.Changes ? FindingStatusWords.Of(m.Merged) : "-",
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

            var incoming = Ui.Table(rows,
                ("Group", 200), ("The file says", 120), ("The board says", 110),
                ("Would become", 110), ("", 0));

            results.Children.Add(Ui.Picking(incoming,
                new Ui.OnPicked("Copy {n}",
                    (picked, say) => Ui.Copy(Ui.Lines(incoming, picked), say, "group"))));

            if (report.Refused > 0)
                results.Children.Add(Ui.Note(Ui.Count(report.Refused, "topic")
                    + " asked for a status lower than the board holds and will stay where they are. "
                    + "The file was exported before those decisions were made, and a stale \"Open\" "
                    + "must not undo an undisputed approval. A deliberate re-open is applied."));

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
                + "under the figures on the Clash Board, and it puts the board back as it was before this."));

            session.Record(ActivityKind.Exchange,
                "applied a BCF reply: " + Ui.Count(groups, "group") + " moved");
        }

        private static HashSet<string> Known(Session session)
        {
            var saved = session.Store.Section(ProjectStore.ClashSection)[ClashKeys.BcfTopics];

            return new HashSet<string>(
                saved.Items.Select(i => i.AsString() ?? string.Empty).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static void Remember(Session session, IEnumerable<string> guids)
        {
            var known = Known(session);
            foreach (var guid in guids) known.Add(guid);

            session.Store.Section(ProjectStore.ClashSection)
                   .Set(ClashKeys.BcfTopics, JsonValue.Array(known.OrderBy(g => g, StringComparer.Ordinal)
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
            var saved = session.Store.Section(ProjectStore.ClashSection)[ClashKeys.BcfGroups];

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
            var saved = section[ClashKeys.BcfGroups];

            if (saved.Kind != JsonKind.Object)
            {
                saved = JsonValue.Object();
                section.Set(ClashKeys.BcfGroups, saved);
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
