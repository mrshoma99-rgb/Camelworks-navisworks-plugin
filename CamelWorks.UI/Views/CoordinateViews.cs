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
using CamelWorks.Core.Search;
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
            var levels = Levels(session);

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


        /// <summary>
        /// The levels every screen bands by: the storeys the project declares, or one default
        /// level that everything is on.
        ///
        /// Nothing is derived here any more. Deriving read the bounds of every element in the
        /// federation, which is the most expensive read the board ever made, and it ran on the
        /// press somebody was waiting on. It is a button on Settings — Levels now, where it
        /// writes the storeys it works out into the plan and every board afterwards reads them
        /// like any other declared level.
        /// </summary>
        /// <param name="session">The open document.</param>
        private static LevelSet Levels(Session session)
        {
            var declared = LevelPlan
                .FromJson(session.Store.Section(ProjectStore.ProfileSection)[LevelPlan.Key]);

            return declared.IsEmpty ? LevelSet.Flat : declared.Levels();
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

            var read = ClashSnapshotJson.FromJson(section[ClashKeys.Previous]);

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

            // The board being replaced becomes the one Undo puts back.
            if (ClashSnapshotJson.IsSnapshot(previous)) section.Set(ClashKeys.Previous, previous);

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

        /// <summary>
        /// The cards that shape the board, on the live seat.
        ///
        /// The cards are project-file state and the project file sits beside the open model, so
        /// there is nowhere to write them until a model is open. The screen itself takes a seat
        /// rather than a session, so it can be drawn without one.
        /// </summary>
        public static UIElement Rules()
        {
            var seat = Host.Current;

            if (seat == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty(
                    "No model is open.",
                    "Cards are kept in the project file beside the model, so there is nowhere to "
                    + "write them yet. Open the model these cards belong to.")));

            return Rules(seat);
        }

        /// <summary>The cards screen on a given seat.</summary>
        /// <param name="seat">The seat. Its project file is where the cards are read and written.</param>
        public static UIElement Rules(ISeat seat) => new RulesScreen(seat).Build();

        /// <summary>
        /// One ordered list of cards. Each card finds results and does one thing to them.
        ///
        /// The screen used to draw the pipeline: three fixed stages, each with its own list and
        /// its own editor. That put the machinery on screen rather than the work, and three of the
        /// six things a coordinator does to a set of results — set a status, delete, group by name
        /// — had nowhere to be written at all.
        ///
        /// The cards run top to bottom on a press. Nothing runs on save, and nothing runs on
        /// opening the tab.
        ///
        /// The engine behind this is clash-shaped — <see cref="ClashCard"/> and
        /// <see cref="ClashPipeline"/> work on <see cref="ClashItem"/> — so the screen states that
        /// scope rather than implying it covers the clearance and fall-hazard results the board
        /// also carries.
        /// </summary>
        private sealed class RulesScreen
        {
            /// <summary>How wide a paragraph is allowed to be, in device pixels — about 80 characters.</summary>
            private const double TextWidth = 620;

            /// <summary>What a dragged rule is carried as.</summary>
            private const string CarryFormat = "CamelWorks.ClashRule";

            /// <summary>The actions, in the order the picker offers them.</summary>
            private static readonly ClashActionKind[] Acts =
            {
                ClashActionKind.Suppress,
                ClashActionKind.Flag,
                ClashActionKind.Group,
                ClashActionKind.Assign,
                ClashActionKind.Status,
                ClashActionKind.Remove,
            };

            private readonly ISeat _seat;

            /// <summary>The live session, or null on a seat with no document. Review and Run need it.</summary>
            private readonly Session? _live;

            private ClashRuleSet _rules = new ClashRuleSet();

            /// <summary>What the last review or run reported, by rule, for the list's counts.</summary>
            private readonly Dictionary<ClashCard, ClashCardOutcome> _said =
                new Dictionary<ClashCard, ClashCardOutcome>();

            /// <summary>The rule the set-up area is editing, or null when the list is empty.</summary>
            private ClashCard? _open;

            /// <summary>The rule being dragged, or null.</summary>
            private ClashCard? _carrying;

            /// <summary>Where the press that might become a drag started.</summary>
            private Point _pressAt;

            /// <summary>
            /// Whether the press that is still down landed on a row rather than on one of its
            /// symbols.
            ///
            /// A symbol handles its own press, so the row never sees it and never records where it
            /// was. Without this the next mouse move would measure against wherever the last row
            /// press happened to be, and dragging off the Delete symbol would pick the rule up.
            /// </summary>
            private bool _pressed;

            /// <summary>
            /// The rules, all drawn to one width.
            ///
            /// The width is the list's, not each row's. Sized to their own text the cards came out
            /// ragged down the left column, and a list whose edge moves with every name is one
            /// nobody can see the order of.
            /// </summary>
            private readonly StackPanel _list = Ui.Stack();

            private readonly StackPanel _pane = Ui.Stack();
            private readonly StackPanel _preview = Ui.Stack();
            private readonly StackPanel _saved = Ui.Stack();
            private readonly StackPanel _marks = Ui.Stack();

            internal RulesScreen(ISeat seat)
            {
                _seat = seat ?? throw new ArgumentNullException(nameof(seat));
                _live = seat as Session;

                _list.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            internal UIElement Build()
            {
                _rules = ClashRuleSet.FromJson(_seat.Store.Section(ProjectStore.ClashSection)[ClashKeys.Rules]);
                _open = _rules.Cards.FirstOrDefault();

                Redraw();

                var save = Ui.Button("Save rules", Save);
                var help = Ui.Icon(Glyphs.Info, "How rules are run", Guide);

                // Review walks the list and writes nothing; Run all rules writes. Saving is
                // neither, and runs nothing.
                var review = _live == null
                    ? (UIElement)Stopped("Review", primary: false)
                    : Ui.Runner("Review", _preview, job => Preview(_live, job), primary: false);

                var run = _live == null
                    ? (UIElement)Stopped("Run all rules", primary: true)
                    : Ui.Runner("Run all rules", _preview, job => Play(_live, job, null));

                // Half the window to the rule being edited. The list says what the rules are and
                // what order they run in; the panel is where a rule is actually written, and on a
                // three-hundred pixel panel a criteria row has nowhere to grow.
                return Bench.Surface(_pane, Ui.Foot(run, Ui.Actions(save, review), help), Stage(),
                                     setupShare: 0.5);
            }

            /// <summary>
            /// A footer act on a seat with no document.
            ///
            /// Not <see cref="Ui.Blocked"/>: that returns a whole footer of its own, and a footer
            /// inside a footer draws its reason in a slot with no width, a character to a line.
            /// A plain disabled button is what <see cref="Ui.Foot"/> wants — it reads the reason
            /// off the primary's tooltip and states it once, where the anatomy puts it.
            /// </summary>
            /// <param name="caption">What the button would say.</param>
            /// <param name="primary">Whether it is the screen's main press.</param>
            private static System.Windows.Controls.Button Stopped(string caption, bool primary)
            {
                var go = new System.Windows.Controls.Button
                {
                    Content = caption,
                    IsEnabled = false,
                    ToolTip = "Rules read and write the clash results, so this needs an open model.",
                };

                go.Wearing(primary ? "PrimaryBtn" : "Btn");

                return go;
            }

            /// <summary>The ordered list of rules, and what they last found.</summary>
            private UIElement Stage() =>
                Ui.Stack(
                    Bench.Scope(Scope()),
                    _live == null
                        ? Say("No model is open. Rules can be written and saved from here; "
                              + "reviewing and running them reads the clash results, which needs "
                              + "the model open.")
                        : null,
                    _saved,
                    _marks,
                    _preview,
                    Head("Rules"),
                    Say("They run top to bottom. A rule that takes results off the board or "
                        + "deletes them hides those results from the rules under it, so the order "
                        + "is part of what they do. Drag a rule to move it; press one to open it."),
                    _list,
                    Ui.Actions(Ui.Button("New rule", Fresh)));

            // ---- The list -----------------------------------------------------------------

            /// <summary>Draw the list and the set-up area from the rule set.</summary>
            private void Redraw()
            {
                _list.Children.Clear();
                _marks.Children.Clear();

                if (_rules.Cards.Count == 0)
                {
                    _list.Children.Add(Nothing());
                }
                else
                {
                    for (var at = 0; at < _rules.Cards.Count; at++)
                        _list.Children.Add(Row(_rules.Cards[at], at));
                }

                // What past runs wrote stands until somebody takes it off, so the screen says how
                // much is standing and offers the one way out. Suppressing used to be re-derived
                // on every board build, where switching a rule off was the way back.
                var marked = _seat.Findings.Findings.Present.Count(ClashCardRun.Marked);

                if (marked > 0)
                {
                    var line = Ui.Across(
                        Ui.Line(Ui.Count(marked, "result") + " carry a mark from a past run.", 0.75),
                        Ui.Link("Clear the marks", Unmark));

                    line.Margin = new Thickness(0, 4, 0, 0);
                    _marks.Children.Add(line);
                }

                Opened();
            }

            /// <summary>The list with nothing in it, drawn where a rule would be.</summary>
            private UIElement Nothing()
            {
                // No way out on the card: New rule sits directly under the list, where it also is
                // once there are rules, and two of the same button a centimetre apart reads as two
                // different things.
                var card = Ui.Empty("No rules.",
                                    "Every result the tests report reaches the board as it is.");

                // Stretched, not sized: the stage is now half the window and a fixed 520 would
                // overflow it on a narrow one.
                if (card is FrameworkElement frame)
                {
                    frame.HorizontalAlignment = HorizontalAlignment.Stretch;
                    frame.MaxWidth = double.PositiveInfinity;
                }

                return card;
            }

            /// <summary>
            /// One rule in the list: its place, its name, what it finds and does, and its tick.
            ///
            /// A card rather than a table row. The list is the thing being arranged, and a row
            /// that can be picked up wants an edge somebody can see and take hold of.
            /// </summary>
            /// <param name="card">The rule.</param>
            /// <param name="at">Its place, counted from nought.</param>
            private UIElement Row(ClashCard card, int at)
            {
                var line = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    AllowDrop = true,
                };

                var chosen = ReferenceEquals(card, _open);

                line.Tint(Border.BackgroundProperty, chosen ? "AccentWash" : "Card");
                line.Tint(Border.BorderBrushProperty, chosen ? "Accent" : "Bd");

                var title = Ui.Line(Place(at) + ".  " + card.Name, 1, bold: chosen);

                var under = Ui.Small(Searched(card.When) + "   →   " + card.Does
                                     + (card.IsEnabled ? string.Empty : "   (off)")
                                     + Found(card));

                under.TextWrapping = TextWrapping.Wrap;

                var tick = Ui.Check(string.Empty, card.IsEnabled, on =>
                {
                    card.IsEnabled = on;
                    Touched();
                    Redraw();
                });

                tick.VerticalAlignment = VerticalAlignment.Center;
                tick.Margin = new Thickness(0, 0, 10, 0);
                tick.ToolTip = "Run this rule when the list runs";

                var body = new DockPanel();

                DockPanel.SetDock(tick, Dock.Left);
                body.Children.Add(tick);

                var verbs = Verbs(card);

                DockPanel.SetDock(verbs, Dock.Right);
                body.Children.Add(verbs);

                body.Children.Add(Ui.Loose(Ui.Stack(title, under)));

                line.Child = body;

                line.MouseLeftButtonDown += (s, e) =>
                {
                    _pressAt = e.GetPosition(line);
                    _pressed = true;
                };

                line.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    var opening = _pressed && _carrying == null;

                    _pressed = false;

                    if (!opening) return;

                    _open = card;
                    Redraw();
                });

                Dragging(line, card);

                return line;
            }

            /// <summary>
            /// What a rule can be told to do from its own row.
            ///
            /// Run, copy and delete, on the row rather than only in the panel beside it. Opening a
            /// rule to delete it is two presses and a read to check it is the one meant; the row
            /// already says which rule it is.
            /// </summary>
            /// <param name="card">The rule.</param>
            private UIElement Verbs(ClashCard card)
            {
                var verbs = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                };

                if (_live == null)
                {
                    var stopped = Ui.Icon(Glyphs.Play, "Running a rule reads and writes the clash "
                                                       + "results, so it needs an open model",
                                          () => { });

                    stopped.IsEnabled = false;
                    verbs.Children.Add(stopped);
                }
                else
                {
                    verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run this rule on its own",
                        () => Ui.Run(_preview, job => Play(_live, job, card))));
                }

                verbs.Children.Add(Ui.Icon(Glyphs.Copy, "Make another rule like this one",
                    () => Clone(card)));

                verbs.Children.Add(Ui.Icon(Glyphs.Bin, "Delete this rule", () => Forget(card)));

                return verbs;
            }

            /// <summary>Copy a rule in under the one it came from, and open the copy.</summary>
            /// <param name="card">The rule to copy.</param>
            private void Clone(ClashCard card)
            {
                var copy = card.Copy(Untaken(card.Name + " copy"));
                var at = _rules.Cards.IndexOf(card);

                _rules.Cards.Insert(at < 0 ? _rules.Cards.Count : at + 1, copy);

                _open = copy;

                Touched();
                Redraw();
            }

            /// <summary>
            /// Pick a rule up and drop it somewhere else in the list.
            ///
            /// The order is what the list means, so moving a rule is the edit this screen is most
            /// often used for. A threshold stops a press that wobbles a pixel from becoming a
            /// drag, since the same press also opens the rule.
            /// </summary>
            /// <param name="line">The row.</param>
            /// <param name="card">The rule it draws.</param>
            private void Dragging(Border line, ClashCard card)
            {
                line.MouseMove += (s, e) => Ui.Safely(() =>
                {
                    if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    if (_carrying != null || !_pressed) return;

                    var at = e.GetPosition(line);

                    if (Math.Abs(at.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance
                        && Math.Abs(at.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance)
                        return;

                    _carrying = card;

                    try
                    {
                        DragDrop.DoDragDrop(line, new DataObject(CarryFormat, card.Name),
                                            DragDropEffects.Move);
                    }
                    finally
                    {
                        _carrying = null;
                        _pressed = false;
                    }
                });

                line.DragOver += (s, e) => Ui.Safely(() =>
                {
                    var take = _carrying != null && !ReferenceEquals(_carrying, card);

                    e.Effects = take ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;

                    if (take) line.Tint(Border.BorderBrushProperty, "Accent");
                });

                line.DragLeave += (s, e) => Ui.Safely(() =>
                    line.Tint(Border.BorderBrushProperty,
                              ReferenceEquals(card, _open) ? "Accent" : "Bd"));

                line.Drop += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;

                    var carried = _carrying;

                    if (carried == null || ReferenceEquals(carried, card)) return;

                    var from = _rules.Cards.IndexOf(carried);
                    var onto = _rules.Cards.IndexOf(card);

                    if (from < 0 || onto < 0) return;

                    _rules.Cards.RemoveAt(from);
                    _rules.Cards.Insert(onto, carried);

                    _open = carried;

                    Touched();
                    Redraw();
                });
            }

            /// <summary>A rule's place in the list, counted from one.</summary>
            /// <param name="at">Its index.</param>
            private static string Place(int at) => (at + 1).ToString(CultureInfo.InvariantCulture);

            /// <summary>What the last review or run said about a rule, or nothing at all.</summary>
            /// <param name="card">The rule.</param>
            private string Found(ClashCard card)
            {
                if (!_said.TryGetValue(card, out var outcome)) return string.Empty;

                if (outcome.Problem != null) return "   — " + outcome.Problem;

                return "   — " + Tallied(outcome.Matched) + " found, "
                       + Tallied(outcome.Changed) + " changed";
            }

            // ---- The set-up area ----------------------------------------------------------

            /// <summary>
            /// The open rule: its name, what it finds, and what it does to what it finds.
            ///
            /// Built fresh whenever the open rule changes, and edited in place: every control
            /// writes straight to the rule the list is holding, so there is no Apply and nothing
            /// to lose by opening another one. The project file is a separate press.
            /// </summary>
            private void Opened()
            {
                _pane.Children.Clear();

                if (_open == null)
                {
                    _pane.Children.Add(Bench.Group("Rule",
                        Ui.Small("Press a rule to open it, or add one.")));

                    return;
                }

                var card = _open;

                var name = Ui.Text(card.Name, typed => { card.Name = typed; Touched(); }, 240);

                name.LostFocus += (s, e) => Ui.Safely(Redraw);

                // The search writes into the rule as it is edited. Redrawing the whole screen on
                // every keystroke would take the caret with it, so a change marks the project
                // unsaved and the list is redrawn only when the shape of the search changes.
                var when = new SearchEditor(card.When, _seat.Checks.Find(FindingSource.Clash),
                                            Touched, Offered);

                var act = Ui.Choice(Acts.Select(Verb), Verb(card.Action), picked =>
                {
                    card.Action = Read(picked);

                    Touched();
                    Redraw();
                }, 240);

                var value = Value(card);

                var said = Ui.Stack();

                if (card.Action == ClashActionKind.Remove)
                    said.Children.Add(Ui.Note(
                        "Remove deletes the results it finds from the Navisworks document. They "
                        + "go from the native clash window too, and running the clash test again "
                        + "is the only way to get them back."));

                if (card.When.IsEverything
                    && (card.Action == ClashActionKind.Suppress
                        || card.Action == ClashActionKind.Remove))
                    said.Children.Add(Ui.Note("This search has no conditions, so it finds every "
                        + "result and the rule would take the whole board off. Narrow it first."));

                if (card.Lost > 0)
                    said.Children.Add(Ui.Problem(
                        Ui.Count(card.Lost, "condition") + " in this rule could not be read, so it "
                        + "was turned off. It was saved by a newer build; running it as it stands "
                        + "would act on more results than it was written for."));

                var one = _live == null
                    ? (UIElement)Stopped("Run this rule", primary: false)
                    : Ui.Runner("Run this rule", _preview, job => Play(_live, job, card),
                                primary: false);

                _pane.Children.Add(Bench.Group("Criteria",
                    Ui.Field("Rule name", name),
                    when.View));

                _pane.Children.Add(Ui.Small(
                    "Conditions read the result's own fields, whatever the check that produced it "
                    + "measured, and the properties of the elements it is about."));

                _pane.Children.Add(Bench.Group("Action",
                    Ui.Field("Do this", act),
                    value,
                    said,
                    Ui.Actions(one, Ui.Danger("Delete rule", () => Forget(card)))));
            }

            /// <summary>The one value field this action takes, or an empty panel where it takes none.</summary>
            /// <param name="card">The open rule.</param>
            private UIElement Value(ClashCard card)
            {
                switch (card.Action)
                {
                    case ClashActionKind.Group:
                        return Ui.Field("Group name",
                            Ui.Text(card.Value, typed => { card.Value = typed; Touched(); }, 240));

                    case ClashActionKind.Assign:
                        return Ui.Field("Assign to", Picker(card, Parties()));

                    case ClashActionKind.Status:
                        return Ui.Field("Status", Picker(card, FindingStatusWords.Words));

                    case ClashActionKind.Priority:
                        return Ui.Field("Priority", Picker(card, Ranks()));

                    case ClashActionKind.Label:
                        return Ui.Field("Label",
                            Ui.Text(card.Value, typed => { card.Value = typed; Touched(); }, 240));

                    case ClashActionKind.Issue:
                        return Ui.Field("Issue",
                            Ui.Text(card.Value, typed => { card.Value = typed; Touched(); }, 240),
                            "An issue of this name is raised if the project has none, and the "
                            + "results are attached to it. Attaching writes the project file at "
                            + "once, because the issue register is another tab's state.");

                    case ClashActionKind.Comment:
                        var said = Ui.Text(card.Value, typed => { card.Value = typed; Touched(); }, 300);

                        said.AcceptsReturn = true;
                        said.TextWrapping = TextWrapping.Wrap;
                        said.MinLines = 2;
                        said.MaxLines = 5;

                        return Ui.Field("Remark", said,
                            "Written once. A rule that has already left this remark on a result "
                            + "does not leave it again.");

                    default:
                        return Ui.Stack();
                }
            }

            /// <summary>A dropdown that writes the open rule's one value.</summary>
            /// <param name="card">The open rule.</param>
            /// <param name="options">What it offers.</param>
            private ComboBox Picker(ClashCard card, IReadOnlyList<string> options)
            {
                var held = options.FirstOrDefault(one =>
                    string.Equals(one, card.Value, StringComparison.Ordinal)) ?? options[0];

                // Written straight away rather than on the first change: a rule created with no
                // value would otherwise show the first option and hold none, and refuse to run
                // saying it has no party set.
                card.Value = held;

                return Ui.Choice(options, held, picked =>
                {
                    card.Value = picked;

                    Touched();
                    Redraw();
                }, 240);
            }

            /// <summary>The project's priority words, never empty.</summary>
            private IReadOnlyList<string> Ranks()
            {
                var held = _seat.Profile.Priorities.ToList();

                if (held.Count == 0) held.AddRange(Core.Project.Priorities.All);

                return held;
            }

            /// <summary>
            /// What a search condition offers for a field instead of a box to type in.
            ///
            /// From the project and from the results already on the board, never from a fixed
            /// list written here: the statuses are the lattice's, the priorities and parties are
            /// the project's, and the tests are the ones the checks on this seat actually hold.
            /// A field with nothing to offer gets a box, which is every free-text field.
            /// </summary>
            /// <param name="field">The field id.</param>
            private IReadOnlyList<SearchEditor.Choice> Offered(string field)
            {
                switch (field)
                {
                    case ResultFields.Status:
                        return Offering(FindingStatusWords.Words);

                    case ResultFields.Priority:
                        return Offering(Ranks());

                    case ResultFields.Assignee:
                        return Offering(Parties());

                    case ResultFields.Orientation:
                        return Offering(new[] { ElementGeometry.Vertical,
                                                ElementGeometry.Horizontal });

                    case ResultFields.Source:
                        return _seat.Checks.All
                            .Select(one => new SearchEditor.Choice(one.Key, one.Name))
                            .ToList();

                    case ResultFields.Run:
                        return Tests();

                    case ResultFields.Model:
                        return Offering(Told(_seat.Models()));

                    default:
                        // The rest are read off the results the board already holds, so a group,
                        // a stage or a label that exists is offered and one that does not is not
                        // invented.
                        return Offering(Seen(field));
                }
            }

            /// <summary>Values offered under their own names.</summary>
            /// <param name="values">The values.</param>
            private static IReadOnlyList<SearchEditor.Choice> Offering(IEnumerable<string> values) =>
                values.Select(one => new SearchEditor.Choice(one)).ToList();

            /// <summary>
            /// Every test on this seat: the id a result carries, under the name people know it by.
            ///
            /// A clash result names its test by the host's own id, so a picker offering the ids
            /// themselves would be a column of GUIDs.
            /// </summary>
            private IReadOnlyList<SearchEditor.Choice> Tests()
            {
                var found = new List<SearchEditor.Choice>();

                foreach (var check in _seat.Checks.All)
                {
                    try
                    {
                        foreach (var run in check.Runs())
                            found.Add(new SearchEditor.Choice(run.Id, run.Name));
                    }
                    catch (Exception)
                    {
                        // A check that will not list its tests offers none, like one with none.
                    }
                }

                return found.OrderBy(one => one.Said, StringComparer.OrdinalIgnoreCase).ToList();
            }

            /// <summary>The results the register holds, without reading the document.</summary>
            private IReadOnlyList<Finding> Held() =>
                _seat.Findings.Findings.Present.ToList();

            /// <summary>What the results on the board actually say under a field.</summary>
            /// <param name="field">The field id.</param>
            private IReadOnlyList<string> Seen(string field)
            {
                switch (field)
                {
                    case ResultFields.Group:
                        return Told(Held().Select(one => one.Group));

                    case ResultFields.Stage:
                        return Told(Held().Select(one => one.Stage));

                    case ResultFields.Label:
                        return Told(Held().SelectMany(one => one.Labels));

                    default:
                        return Array.Empty<string>();
                }
            }

            /// <summary>A list of values, without the empties, the repeats or the disorder.</summary>
            /// <param name="values">What was found.</param>
            private static IReadOnlyList<string> Told(IEnumerable<string?> values) =>
                values.Where(one => !string.IsNullOrWhiteSpace(one))
                      .Select(one => one!)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)
                      .ToList();

            /// <summary>The project's parties, never empty.</summary>
            private IReadOnlyList<string> Parties()
            {
                var parties = _seat.Profile.Parties.ToList();

                if (parties.Count == 0) parties.Add("Unassigned");

                return parties;
            }

            /// <summary>What one action is called on screen.</summary>
            /// <param name="action">The action.</param>
            private static string Verb(ClashActionKind action) =>
                action switch
                {
                    ClashActionKind.Suppress => "Suppress",
                    ClashActionKind.Flag => "Flag",
                    ClashActionKind.Group => "Group",
                    ClashActionKind.Assign => "Assign",
                    ClashActionKind.Status => "Change status",
                    ClashActionKind.Remove => "Remove",
                    _ => action.ToString(),
                };

            /// <summary>The action behind one of those words.</summary>
            /// <param name="said">What the picker holds.</param>
            private static ClashActionKind Read(string? said)
            {
                foreach (var one in Acts)
                    if (Verb(one) == said)
                        return one;

                return ClashActionKind.Suppress;
            }

            // ---- Editing the list ---------------------------------------------------------

            /// <summary>Add a rule and open it.</summary>
            private void Fresh()
            {
                var card = new ClashCard(Untaken(), new ResultQuery(), ClashActionKind.Suppress);

                _rules.Cards.Add(card);
                _open = card;

                Touched();
                Redraw();
            }

            /// <summary>A name no rule in the list is using.</summary>
            /// <param name="wanted">What to call it, or null for the next numbered name.</param>
            private string Untaken(string? wanted = null)
            {
                var stem = string.IsNullOrWhiteSpace(wanted)
                    ? "Rule " + (_rules.Cards.Count + 1).ToString(CultureInfo.InvariantCulture)
                    : wanted!.Trim();

                if (!Taken(stem)) return stem;

                for (var n = 2; ; n++)
                {
                    var tried = stem + " " + n.ToString(CultureInfo.InvariantCulture);

                    if (!Taken(tried)) return tried;
                }
            }

            /// <summary>Whether a rule in the list is already called this.</summary>
            /// <param name="name">The name.</param>
            private bool Taken(string name) =>
                _rules.Cards.Any(one => string.Equals(one.Name, name, StringComparison.Ordinal));

            /// <summary>Take one rule out of the list.</summary>
            /// <param name="card">The rule.</param>
            private void Forget(ClashCard card) =>
                Parts.Sure(_list, "Delete the rule “" + card.Name + "”? What past runs of "
                                  + "it wrote to the results is not undone.",
                    "Delete rule",
                    () =>
                    {
                        var at = _rules.Cards.IndexOf(card);

                        _rules.Cards.Remove(card);
                        _said.Remove(card);

                        _open = _rules.Cards.Count == 0
                            ? null
                            : _rules.Cards[Math.Min(at, _rules.Cards.Count - 1)];

                        Touched();
                        Redraw();
                    });

            /// <summary>
            /// Take every suppress and flag mark off, so the board carries what the tests report.
            ///
            /// Confirmed, because it undoes every run of every rule at once and the counts on the
            /// list are the only record of what those runs did.
            /// </summary>
            private void Unmark() =>
                Parts.Sure(_marks,
                    "This takes the suppress and flag marks off every result in the project, "
                    + "whichever rule wrote them. The results themselves are not touched.",
                    "Clear the marks",
                    () =>
                    {
                        var cleared = ClashCardRun.Clear(_seat.Findings.Findings.Present.ToList());

                        _preview.Children.Clear();

                        if (!_seat.Findings.Save())
                        {
                            _preview.Children.Add(Ui.Problem(
                                "The marks were cleared, but the findings file could not be written."));
                            return;
                        }

                        _said.Clear();
                        Redraw();

                        _preview.Children.Add(Ui.Done(
                            Ui.Count(cleared, "result") + " no longer carry a mark."));
                    });

            /// <summary>Say that what is on screen is no longer what the project file holds.</summary>
            private void Touched()
            {
                _saved.Children.Clear();
                _saved.Children.Add(Say("Edited. Nothing is written to the project file until "
                                        + "Save rules is pressed."));
            }

            /// <summary>Write the rules to the project file.</summary>
            private void Save()
            {
                _saved.Children.Clear();

                _seat.Store.Section(ProjectStore.ClashSection).Set(ClashKeys.Rules, _rules.ToJson());

                if (!_seat.Store.Save())
                {
                    _saved.Children.Add(Ui.Problem(_seat.Store.LastSaveProblem
                                                   ?? "Could not write the project file."));
                    return;
                }

                _seat.Record(ActivityKind.Setup,
                             "Saved " + Ui.Count(_rules.Cards.Count, "clash rule") + ".");

                _saved.Children.Add(Ui.Done("Saved. Saving does not run anything: press Run all "
                                            + "rules, or open a rule and run it on its own."));
            }

            // ---- Running ------------------------------------------------------------------

            /// <summary>
            /// Run the list, or one rule of it, and report what each did.
            ///
            /// The results are read with no rule applied, so what they find is what the tests
            /// reported rather than what a previous run of the same list left behind.
            /// </summary>
            /// <param name="session">The live session.</param>
            /// <param name="job">The job, for progress and Stop.</param>
            /// <param name="only">The one rule to run, or null for the whole list.</param>
            private void Play(Session session, Job job, ClashCard? only)
            {
                var live = only != null
                    ? new List<ClashCard> { only }
                    : _rules.Cards.Where(one => one.IsEnabled).ToList();

                if (live.Count == 0)
                {
                    _preview.Children.Add(Ui.Note("No rules are turned on, so there is nothing to run."));
                    return;
                }

                // A rule that deletes is the one act on this screen that running the test again
                // will not undo, so it is confirmed before anything is read.
                var final = live.Count(one => ClashCard.IsFinal(one.Action));

                if (final > 0)
                {
                    var went = false;

                    Parts.Sure(_preview,
                        Ui.Count(final, "rule") + " about to run deletes the results it finds from "
                        + "the Navisworks document. They go from the native clash window too, and "
                        + "running the clash test again is the only way to get them back.",
                        only == null ? "Run all rules" : "Run this rule", () => went = true);

                    if (!went)
                    {
                        _preview.Children.Add(Ui.Note("Nothing was run."));
                        return;
                    }
                }

                var results = Reading(session, job);
                if (results == null) return;

                var world = new SeatWorld(session);
                var writer = new CardWriter(session, world);

                job.Say("Running " + Ui.Count(live.Count, "rule") + "...");

                var outcomes = ClashCardRun.Apply(live, results, world, writer);

                if (!session.Findings.Save())
                    _preview.Children.Add(Ui.Problem(
                        "The rules ran, but the findings file could not be written."));

                Report(outcomes, ran: true);
            }

            /// <summary>Say what the rules would do, changing nothing.</summary>
            /// <param name="session">The live session.</param>
            /// <param name="job">The job, for progress and Stop.</param>
            private void Preview(Session session, Job job)
            {
                var results = Reading(session, job);
                if (results == null) return;

                Report(ClashCardRun.Plan(_rules.Cards, results, new SeatWorld(session)), ran: false);
            }

            /// <summary>
            /// The results the rules run over: the project's findings, read fresh.
            ///
            /// The register rather than the clash board. A rule's search is written on a finding —
            /// its status, its check's own columns, its elements — so the results of every check
            /// are searchable by the same rule, and a rule that names no check runs over all of
            /// them.
            ///
            /// The board is still built first, on a seat with a clash engine, because that is what
            /// reads the engine and brings its results into the register. Built with an empty rule
            /// set, so what the rules are handed is what the tests reported.
            /// </summary>
            /// <param name="session">The live session.</param>
            /// <param name="job">The job.</param>
            private IReadOnlyList<Finding>? Reading(Session session, Job job)
            {
                var notes = new List<string>();

                CoordinateViews.Build(session, job, notes, new ClashRuleSet(), preview: true);

                foreach (var note in notes) _preview.Children.Add(Ui.Note(note));

                var held = session.Findings.Findings.Present.ToList();

                if (held.Count == 0)
                {
                    _preview.Children.Add(Ui.Note(
                        "The project holds no results. Run a check on the Create tab first."));

                    return null;
                }

                return held;
            }

            /// <summary>
            /// What the rules found, written onto the list itself.
            ///
            /// The counts go on the rules rather than into a table beside them: the rule is where
            /// the reader is looking, and a rule that found nothing is only meaningful next to
            /// the criteria that found nothing.
            /// </summary>
            /// <param name="outcomes">What the walk reported.</param>
            /// <param name="ran">True when the writes happened, false for a review.</param>
            private void Report(IReadOnlyList<ClashCardOutcome> outcomes, bool ran)
            {
                foreach (var one in outcomes) _said[one.Card] = one;

                Redraw();

                if (outcomes.Count == 0)
                {
                    _preview.Children.Add(Ui.Note("No rules are turned on, so nothing was tried."));
                    return;
                }

                foreach (var one in outcomes.Where(one => one.Problem != null))
                    _preview.Children.Add(Ui.Problem(one.Card.Name + ": " + one.Problem));

                var changed = outcomes.Sum(one => one.Changed);

                _preview.Children.Add(Say(ran
                    ? Ui.Count(changed, "result") + " changed. The Manage tab shows the register "
                      + "these rules wrote to."
                    : "Nothing was written. " + Ui.Count(changed, "result") + " would change. "
                      + "Press Run all rules to write it."));
            }

            // ---- Wording ------------------------------------------------------------------

            /// <summary>
            /// What the rules act on, and what the project holds that they cannot reach.
            ///
            /// The engine is typed on <see cref="ClashItem"/> — two sides, a model pair and an
            /// overlap — so no rule here can find a clearance shortfall or a fall-hazard opening.
            /// Those are counted rather than left to be discovered.
            /// </summary>
            private string Scope()
            {
                var covers = "Rules run over clash results only — every result from every clash "
                             + "test in the project.";

                var beside = new[] { FindingSource.Headroom, FindingSource.FallHazard };

                var others = _seat.Findings.Findings.Present
                    .Where(f => beside.Contains(f.Source, StringComparer.OrdinalIgnoreCase))
                    .GroupBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (others.Count == 0) return covers;

                return covers + " Not in it: " + string.Join(", ", others.Select(one =>
                    Ui.Count(one.Count(), Named(one.Key) + " result"))) + ".";
            }

            /// <summary>What a check's findings are called, in that check's own words.</summary>
            /// <param name="key">The finding's source.</param>
            private string Named(string key) =>
                (_seat.Checks.Find(key)?.Name ?? key).ToLowerInvariant();

            /// <summary>A section heading: sentence case, one size, one weight.</summary>
            /// <param name="text">The heading.</param>
            private static TextBlock Head(string text)
            {
                var head = Ui.Heading(text);
                head.Margin = new Thickness(0, 20, 0, 2);
                return head;
            }

            /// <summary>A line of orientation, capped at a readable measure.</summary>
            /// <param name="text">The line.</param>
            private static TextBlock Say(string text)
            {
                var line = Ui.Line(text, 0.75);
                line.MaxWidth = TextWidth;
                line.HorizontalAlignment = HorizontalAlignment.Left;
                return line;
            }

            /// <summary>Whether a length reads better in millimetres: non-zero and under a metre.</summary>
            /// <param name="metres">The length.</param>
            private static bool Fine(double metres) => metres != 0 && Math.Abs(metres) < 1;

            /// <summary>A length with its unit, in the scale asked for.</summary>
            /// <param name="metres">The length.</param>
            /// <param name="fine">True for millimetres, false for metres.</param>
            private static string Length(double metres, bool fine) =>
                fine
                    ? (metres * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm"
                    : metres.ToString("0.###", CultureInfo.InvariantCulture) + " m";

            /// <summary>
            /// The two ends of a distance range, in one scale.
            ///
            /// Both ends take the finer scale if either needs it, so a range never reads
            /// "between 0 m and 5 mm".
            /// </summary>
            /// <param name="from">The near end.</param>
            /// <param name="to">The far end.</param>
            private static string Span(double from, double to)
            {
                var fine = Fine(from) || Fine(to);
                return Length(from, fine) + " and " + Length(to, fine);
            }

            /// <summary>
            /// A rule's search in one line, for the list.
            ///
            /// A count rather than the search written out: a stacked search does not fit on a card
            /// and a half-written one would read as nonsense. The panel beside the list holds the
            /// search itself.
            /// </summary>
            /// <param name="query">The search.</param>
            private static string Searched(ResultQuery query) =>
                query.IsEverything
                    ? "Every result"
                    : Ui.Count(query.Clauses, "condition");

            /// <summary>
            /// What a rule finds, with the unit on every measured value.
            ///
            /// <see cref="PredicateSpec.Describe"/> writes bare numbers, because Core has no
            /// opinion about how a screen shows a length. The three measured kinds are formatted
            /// here and everything else falls through to the Core wording.
            /// </summary>
            /// <param name="spec">The criteria.</param>
            private static string Criteria(PredicateSpec spec)
            {
                var kind = spec.Descriptor;
                if (kind == null) return spec.Describe();

                return kind.Id switch
                {
                    "maxVolume" => kind.Title + " "
                                   + spec.A.ToString("0.####", CultureInfo.InvariantCulture) + " m³",
                    "distance" => kind.Title + " " + Span(spec.A, spec.B),
                    "angle" => kind.Title + " " + Degrees(spec.A) + " and " + Degrees(spec.B),
                    _ => spec.Describe(),
                };
            }

            /// <summary>An angle with its unit.</summary>
            /// <param name="degrees">The angle.</param>
            private static string Degrees(double degrees) =>
                degrees.ToString("0.###", CultureInfo.InvariantCulture) + "°";

            /// <summary>A count as it reads on a rule.</summary>
            /// <param name="count">The count.</param>
            private static string Tallied(int count) => count.ToString("N0", CultureInfo.InvariantCulture);

            /// <summary>
            /// The reference matter, behind one symbol rather than standing under the list.
            ///
            /// What each action does, what order means, and the long form of the scope the surface
            /// states in one line.
            /// </summary>
            private void Guide()
            {
                Ui.Configure("How rules are run", null,
                    new Ui.Page("The order", () => Ui.Stack(
                        Part("Top to bottom",
                             "A rule finds results with its criteria and does one thing to them. "
                             + "The first rule sees every clash result in the project; each rule "
                             + "after it sees what the rules above left. A rule that suppresses or "
                             + "removes takes its results off the list the rules under it see, so "
                             + "dragging a rule up or down changes what the ones below it find."),
                        Part("On a press",
                             "Nothing runs on save, and nothing runs when the tab opens. Review "
                             + "walks the list and writes nothing; Run all rules walks it and "
                             + "writes; a rule opened on the right can be run on its own. All "
                             + "three read the results with no rule applied, so running the same "
                             + "list twice reports what it found and nothing changed the second "
                             + "time."))),
                    new Ui.Page("The actions", () => Ui.Stack(
                        Part("Suppress and flag",
                             "Both are marks on the finding and both name the rule that set them. "
                             + "A suppressed result is off the board and hidden from the rules "
                             + "below; a flagged one stays where it is."),
                        Part("Group, assign and change status",
                             "Written to the project's finding register and echoed into the "
                             + "Navisworks document, so the native clash window shows the same "
                             + "group, the same assignee and the same status. A document that "
                             + "will not take the write says so on the rule."),
                        Part("Remove",
                             "Deletes the results from the Navisworks document. They are gone from "
                             + "the native clash window as well, and running the clash test again "
                             + "is the only way to get them back. The finding stays in the "
                             + "register marked absent, so an issue that names one can still say "
                             + "what it was about."))),
                    new Ui.Page("What they cover", () => Ui.Stack(
                        Part("Clash results only",
                             "A rule's criteria work on a clash result: two items, the models they "
                             + "came from, and the overlap between them. A clearance shortfall and "
                             + "a fall-hazard opening are not that shape, so no rule on this screen "
                             + "can find one."),
                        Part("Which tests",
                             "Every clash test in the project. There is no rule that narrows the "
                             + "list to one test; add criteria on the test name to keep a rule to "
                             + "one."),
                        Part("Undoing a run",
                             "A suppress or flag mark stands until it is taken off, because it is "
                             + "written by a run rather than derived by the board on every load. "
                             + "Clear the marks, above the list, takes every one of them "
                             + "off. A group, an assignee or a status is changed by running a rule "
                             + "that sets a different one, or by hand on the Manage tab."))));
            }

            /// <summary>One passage of the help window: a sentence-case heading and its paragraph.</summary>
            /// <param name="title">What it is about.</param>
            /// <param name="text">The passage.</param>
            private static UIElement Part(string title, string text)
            {
                var head = Ui.Heading(title);
                head.Margin = new Thickness(0, 0, 0, 4);

                var body = Ui.Small(text);
                body.MaxWidth = TextWidth;
                body.Margin = new Thickness(0, 0, 0, 16);

                return Ui.Stack(head, body);
            }
        }

        /// <summary>
        /// The band stack: how the board divides the results it is given.
        ///
        /// Not a rule and not on the Filter tab. A band changes nothing about a result — it
        /// decides which heading the result is drawn under — so it belongs with the project's
        /// other board settings rather than in a list of things that act on results.
        /// </summary>
        /// <param name="stack">The stack, edited in place.</param>
        /// <param name="proximityMetres">The project's grouping distance, for the empty state.</param>
        /// <param name="onChanged">Called after every edit, so the host can mark itself unsaved.</param>
        internal static UIElement Bands(IList<GroupingSpec> stack, double proximityMetres,
                                        Action onChanged) =>
            new BandStack(stack, proximityMetres, onChanged).View;

        /// <summary>The band stack as a table with an editor under it.</summary>
        private sealed class BandStack
        {
            private readonly IList<GroupingSpec> _stack;
            private readonly double _proximity;
            private readonly Action _onChanged;
            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _said = Ui.Stack();
            private readonly SpecEditor _by;

            /// <summary>Build one over a stack.</summary>
            /// <param name="stack">The stack, edited in place.</param>
            /// <param name="proximityMetres">The project's grouping distance.</param>
            /// <param name="onChanged">Called after every edit.</param>
            internal BandStack(IList<GroupingSpec> stack, double proximityMetres, Action onChanged)
            {
                _stack = stack ?? throw new ArgumentNullException(nameof(stack));
                _proximity = proximityMetres;
                _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

                Grid.SetIsSharedSizeScope(_list, true);

                _by = new SpecEditor("Band by", GroupingSpec.Kinds, null);
                _by.View.Margin = new Thickness(0, 6, 0, 6);

                View = Ui.Stack(_list, _by.View, Ui.Actions(Ui.Button("Add band", Add)), _said);

                Redraw();
            }

            /// <summary>The table, the editor and its action.</summary>
            internal StackPanel View { get; }

            /// <summary>Draw the stack.</summary>
            private void Redraw()
            {
                _list.Children.Clear();

                if (_stack.Count == 0)
                {
                    _list.Children.Add(Ui.Small(
                        "No bands. The board divides by model pair, then level, then results "
                        + "within " + Metres(_proximity) + " of each other."));

                    return;
                }

                var rows = new List<TableRow>();

                for (var at = 0; at < _stack.Count; at++)
                {
                    var step = _stack[at];

                    rows.Add(new TableRow(step,
                        (at + 1).ToString(CultureInfo.InvariantCulture),
                        step.Descriptor?.Title ?? step.Kind,
                        at == 0 ? "The whole board" : "Each band from " + Above(at),
                        Detail(step)));
                }

                var table = Ui.Table(rows,
                    ("#", 28), ("Band by", 180), ("Divides", 175), ("Detail", 0));

                _list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Earlier", (picked, said) => Shift(picked, -1)),
                    new Ui.OnPicked("Later", (picked, said) => Shift(picked, 1)),
                    new Ui.OnPicked("Remove {n}", (picked, said) => Forget(picked), danger: true)));
            }

            /// <summary>Add the composed band to the stack.</summary>
            private void Add()
            {
                _said.Children.Clear();

                var kind = _by.Kind();
                if (kind == null) return;

                if (kind.Input == SpecInput.Text && _by.Name() == null)
                {
                    _said.Children.Add(Ui.Problem("This band needs a " + _by.Wants() + "."));
                    return;
                }

                _stack.Add(new GroupingSpec(kind.Id) { A = _by.Number(), Name = _by.Name() });

                Redraw();
                _onChanged();
            }

            /// <summary>
            /// Move the picked bands one place.
            ///
            /// A band at the end cannot move, and a wall stops the ones behind it stepping over
            /// it: without that, picking the first two and pressing Earlier would swap them
            /// rather than leave the pair where it is.
            /// </summary>
            /// <param name="picked">The rows the bar was pressed with.</param>
            /// <param name="by">Minus one for earlier, plus one for later.</param>
            private void Shift(IReadOnlyList<object> picked, int by)
            {
                var order = picked.OfType<GroupingSpec>()
                    .Select(one => _stack.IndexOf(one))
                    .Where(at => at >= 0)
                    .OrderBy(at => by < 0 ? at : -at)
                    .ToList();

                if (order.Count == 0) return;

                var wall = by < 0 ? 0 : _stack.Count - 1;

                foreach (var at in order)
                {
                    if (by < 0 ? at <= wall : at >= wall)
                    {
                        wall += by < 0 ? 1 : -1;
                        continue;
                    }

                    var moved = _stack[at];
                    _stack.RemoveAt(at);
                    _stack.Insert(at + by, moved);
                }

                Redraw();
                _onChanged();
            }

            /// <summary>Take the picked bands out of the stack.</summary>
            /// <param name="picked">The rows the bar was pressed with.</param>
            private void Forget(IReadOnlyList<object> picked)
            {
                foreach (var one in picked.OfType<GroupingSpec>()) _stack.Remove(one);

                Redraw();
                _onChanged();
            }

            /// <summary>The bands above this one, as a row refers to them.</summary>
            /// <param name="at">The index of the row doing the referring, which is never nought.</param>
            private static string Above(int at) =>
                at == 1 ? "band 1" : "bands 1\u2013" + at.ToString(CultureInfo.InvariantCulture);

            /// <summary>What a band divides by, without repeating its title.</summary>
            /// <param name="step">The band.</param>
            private static string Detail(GroupingSpec step)
            {
                var kind = step.Descriptor;
                if (kind == null) return step.Describe();

                return kind.Input switch
                {
                    SpecInput.Number => Metres(step.A),
                    SpecInput.Text => "'" + (step.Name ?? string.Empty) + "'",
                    _ => string.Empty,
                };
            }

            /// <summary>A distance, with the default the board uses when it is unset.</summary>
            /// <param name="metres">The distance, or zero for the default.</param>
            private static string Metres(double metres)
            {
                var said = metres > 0 ? metres : 5.0;

                return said.ToString("0.###", CultureInfo.InvariantCulture) + " m";
            }
        }

        /// <summary>
        /// What a rule's search reads besides the result: the elements, the check's columns, and
        /// whether an issue has been raised for it.
        ///
        /// The elements come from the open model, so a search about a property answers on a live
        /// seat and finds nothing on one reading a file — which is the honest answer, because the
        /// model those elements were in is not there to ask.
        /// </summary>
        private sealed class SeatWorld : IResultWorld
        {
            private readonly Session _session;
            private readonly Dictionary<string, IReadOnlyList<ResultColumn>> _columns =
                new Dictionary<string, IReadOnlyList<ResultColumn>>(StringComparer.OrdinalIgnoreCase);

            private Dictionary<string, string>? _raised;

            /// <summary>Read from one seat.</summary>
            /// <param name="session">The live session.</param>
            internal SeatWorld(Session session) =>
                _session = session ?? throw new ArgumentNullException(nameof(session));

            /// <inheritdoc />
            public IModelItem? Element(ElementKey key) => _session.Model.Resolve(key);

            /// <inheritdoc />
            public double MetresPerUnit => _session.MetresPerUnit;

            /// <summary>
            /// The columns of the check that produced a result, held per check.
            ///
            /// A search runs over every finding in the project and asks this for each one. Looking
            /// the check up in the catalogue every time would be a walk per result.
            /// </summary>
            /// <param name="finding">The result.</param>
            public IReadOnlyList<ResultColumn> Columns(Finding finding)
            {
                if (_columns.TryGetValue(finding.Source, out var held)) return held;

                var found = _session.Checks.Find(finding.Source)?.Columns
                            ?? (IReadOnlyList<ResultColumn>)Array.Empty<ResultColumn>();

                _columns[finding.Source] = found;

                return found;
            }

            /// <inheritdoc />
            public string? IssueOf(Finding finding) => Raised(finding);

            /// <summary>Read the issue register again, because something has written to it.</summary>
            internal void Forget() => _raised = null;

            /// <summary>
            /// The title of the issue a result is attached to, or null.
            ///
            /// The register is read once and turned inside out: an issue names its findings, and
            /// the question asked here is the other way round.
            /// </summary>
            /// <param name="finding">The result.</param>
            private string? Raised(Finding finding)
            {
                if (_raised == null)
                {
                    _raised = new Dictionary<string, string>(StringComparer.Ordinal);

                    foreach (var issue in Issues(_session).Issues)
                        foreach (var id in issue.Findings)
                            if (id != null && !_raised.ContainsKey(id))
                                _raised[id] = issue.Title;
                }

                return _raised.TryGetValue(finding.Id, out var said) ? said : null;
            }
        }

        /// <summary>
        /// Where a card's writes go on a live seat.
        ///
        /// The first three go through the clash check's host echo, which is the same path the
        /// Manage tab's own verbs take, so a card and a hand edit reach the document the same way.
        /// Deleting has no echo — it is not an edit to a result but the end of one — so it goes to
        /// the clash source directly, a test at a time.
        /// </summary>
        private sealed class CardWriter : IClashCardWriter
        {
            private readonly Session _session;
            private readonly SeatWorld _world;
            private readonly ICheckHostEcho? _echo;

            /// <summary>Write to one seat's document.</summary>
            /// <param name="session">The live session.</param>
            /// <param name="world">
            /// The same world the search is reading, so a write that changes what a condition
            /// would answer is seen by the results after it.
            /// </param>
            internal CardWriter(Session session, SeatWorld world)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
                _world = world ?? throw new ArgumentNullException(nameof(world));

                _echo = new ClashCheck(session.Clash, session.MetresPerUnit, session.ClashProblem)
                    .HostEcho;
            }

            /// <inheritdoc />
            public string? LastProblem { get; private set; }

            /// <inheritdoc />
            public bool Status(IReadOnlyList<Finding> findings, FindingStatus status) =>
                Echoed(echo => echo.SetStatus(findings, status));

            /// <inheritdoc />
            public bool Assign(IReadOnlyList<Finding> findings, string party) =>
                Echoed(echo => echo.Assign(findings, party));

            /// <inheritdoc />
            public bool Group(IReadOnlyList<Finding> findings, string name) =>
                Echoed(echo => echo.SetGroup(findings, name));

            /// <inheritdoc />
            public bool Comment(IReadOnlyList<Finding> findings, string body) =>
                Echoed(echo => echo.Comment(findings, body, _session.Owner));

            /// <summary>
            /// Attach results to an issue, raising it under that name if it is not there yet.
            ///
            /// Written straight to the project file rather than held for the tab's own save: the
            /// issue register is another screen's state, and a run that changed it in memory only
            /// would show the Issue Manager something the file does not hold.
            /// </summary>
            /// <param name="findings">The results.</param>
            /// <param name="title">What the issue is called.</param>
            public bool Raise(IReadOnlyList<Finding> findings, string title)
            {
                LastProblem = null;

                var register = Issues(_session);

                var issue = register.Issues.FirstOrDefault(one =>
                    string.Equals(one.Title, title, StringComparison.OrdinalIgnoreCase));

                if (issue == null)
                {
                    issue = new Issue(Guid.NewGuid().ToString("N"), title);
                    register.Put(issue);
                }

                foreach (var one in findings)
                    if (!issue.Findings.Contains(one.Id, StringComparer.Ordinal))
                        issue.Findings.Add(one.Id);

                _session.Store.Section(ProjectStore.IssuesSection).Set("register", register.ToJson());

                // Through the one door: an issue names finding ids and the findings are in the
                // sidecar beside the project file, so the two are written together or not at all.
                if (!_session.SaveWork(out var problem))
                {
                    LastProblem = problem ?? "The project file could not be written.";
                    return false;
                }

                // The world has to see the attachment this call made, or the next rule about
                // results not yet on an issue would find the ones just attached.
                _world.Forget();

                return true;
            }

            /// <inheritdoc />
            public bool Remove(IReadOnlyList<Finding> findings)
            {
                LastProblem = null;

                if (!_session.Clash.IsAvailable)
                {
                    LastProblem = _session.ClashProblem ?? "No clash engine is available.";
                    return false;
                }

                var all = true;

                foreach (var test in findings.GroupBy(ClashCheck.TestOf, StringComparer.OrdinalIgnoreCase))
                {
                    if (test.Key == null) continue;

                    var ids = test.Select(ClashCheck.ResultOf)
                                  .Where(id => id != null)
                                  .Select(id => id!)
                                  .ToList();

                    if (ids.Count == 0) continue;

                    all &= _session.Clash.DeleteResults(test.Key, ids);
                }

                if (!all) LastProblem = _session.Clash.LastProblem;

                return all;
            }

            /// <summary>One echo, with the reason kept when it is refused.</summary>
            /// <param name="write">The write.</param>
            private bool Echoed(Func<ICheckHostEcho, bool> write)
            {
                LastProblem = null;

                if (_echo == null)
                {
                    LastProblem = "This seat has no clash engine to write to.";
                    return false;
                }

                if (!_echo.CanEdit)
                {
                    LastProblem = _echo.LastProblem ?? "The document will not take edits.";
                    return false;
                }

                if (write(_echo)) return true;

                LastProblem = _echo.LastProblem ?? "The document would not take it.";
                return false;
            }
        }

        /// <summary>
        /// One editor that can draw every rule kind, labelled from the kind it is set to.
        ///
        /// Without this each of the thirteen predicates and ten grouping rules would need its own
        /// row of controls, and adding a rule to Core would mean remembering to add one here.
        /// </summary>
        private sealed class SpecEditor
        {
            private readonly IReadOnlyList<SpecKind> _kinds;
            private readonly ComboBox _kind;
            private readonly TextBox _first = Ui.Text(string.Empty, null, 90);
            private readonly TextBox _second = Ui.Text(string.Empty, null, 90);

            private readonly TextBlock _firstLabel =
                new TextBlock { Margin = new Thickness(0, 0, 0, 4) }.Wearing("Lbl");

            private readonly TextBlock _secondLabel =
                new TextBlock { Margin = new Thickness(0, 0, 0, 4) }.Wearing("Lbl");

            private readonly StackPanel _firstBox;
            private readonly StackPanel _secondBox;
            private readonly Action? _onChanged;

            /// <summary>Build an editor over one set of kinds.</summary>
            /// <param name="label">What the kind dropdown is called.</param>
            /// <param name="kinds">The kinds it offers.</param>
            /// <param name="onChanged">Called when the kind changes.</param>
            internal SpecEditor(string label, IReadOnlyList<SpecKind> kinds, Action? onChanged)
            {
                _kinds = kinds;
                _onChanged = onChanged;

                _kind = Ui.Choice(kinds.Select(k => k.Title), kinds[0].Title, _ =>
                {
                    Relabel();
                    Fired();
                }, 240);

                _first.TextChanged += (s, e) => Ui.Safely(Fired);
                _second.TextChanged += (s, e) => Ui.Safely(Fired);

                _first.MaxWidth = 130;
                _second.MaxWidth = 130;

                _firstBox = Ui.Stack(_firstLabel, _first);
                _secondBox = Ui.Stack(_secondLabel, _second);

                View = Ui.Stack(Ui.Field(label, _kind), Ui.Across(_firstBox, _secondBox));

                Relabel();
            }

            /// <summary>The dropdown and its operand fields.</summary>
            internal StackPanel View { get; }

            /// <summary>
            /// Called when anything in the editor changes, on top of the one given at build time.
            ///
            /// Settable because the editor outlives the thing it is editing on a master-detail
            /// screen: the same editor is pointed at one rule, then another, and what to do with
            /// a change is different each time.
            /// </summary>
            internal Action? Changed { get; set; }

            /// <summary>Whether a change is the caller's own, so loading one in does not echo back.</summary>
            private bool _loading;

            /// <summary>Tell whoever is listening, unless this is a load.</summary>
            private void Fired()
            {
                if (_loading) return;

                _onChanged?.Invoke();
                Changed?.Invoke();
            }

            /// <summary>
            /// Point the editor at an existing rule.
            ///
            /// Silent: the controls are set from the rule, and setting a control raises the same
            /// event a person typing raises, which would report the load as an edit.
            /// </summary>
            /// <param name="spec">What to show.</param>
            internal void Show(PredicateSpec? spec)
            {
                _loading = true;

                try
                {
                    var kind = spec == null
                        ? null
                        : _kinds.FirstOrDefault(k => string.Equals(k.Id, spec.Kind, StringComparison.Ordinal));

                    _kind.SelectedItem = (kind ?? _kinds[0]).Title;

                    Relabel();

                    if (spec == null || kind == null)
                    {
                        _first.Text = string.Empty;
                        _second.Text = string.Empty;
                        return;
                    }

                    switch (kind.Input)
                    {
                        case SpecInput.Number:
                            _first.Text = spec.A.ToString("0.####", CultureInfo.InvariantCulture);
                            _second.Text = string.Empty;
                            break;

                        case SpecInput.Range:
                            _first.Text = spec.A.ToString("0.####", CultureInfo.InvariantCulture);
                            _second.Text = spec.B.ToString("0.####", CultureInfo.InvariantCulture);
                            break;

                        case SpecInput.Text:
                            _first.Text = spec.Name ?? string.Empty;
                            _second.Text = string.Empty;
                            break;

                        case SpecInput.NameAndValue:
                            _first.Text = spec.Name ?? string.Empty;
                            _second.Text = spec.Value ?? string.Empty;
                            break;

                        default:
                            _first.Text = string.Empty;
                            _second.Text = string.Empty;
                            break;
                    }
                }
                finally
                {
                    _loading = false;
                }
            }

            /// <summary>The kind now picked, or null when the dropdown holds something unknown.</summary>
            /// <summary>Put the caret in it, from an empty list's way in.</summary>
            internal void Focus() => _kind.Focus();

            internal SpecKind? Kind()
            {
                var title = _kind.SelectedItem as string;
                return _kinds.FirstOrDefault(k => string.Equals(k.Title, title, StringComparison.Ordinal));
            }

            /// <summary>What the first field is asking for, for a validation line.</summary>
            internal string Wants()
            {
                var kind = Kind();
                return kind == null ? "value" : FirstLabel(kind).ToLowerInvariant();
            }

            /// <summary>The first field as a number, or zero.</summary>
            internal double Number() =>
                double.TryParse(_first.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

            /// <summary>The first field as text, or null when it is empty.</summary>
            internal string? Name() => _first.Text.Trim().Length == 0 ? null : _first.Text.Trim();

            /// <summary>The rule the fields describe, or null when no kind is picked.</summary>
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

            /// <summary>
            /// What the operand boxes are called, per kind, with the unit in the label.
            ///
            /// Held here rather than on <see cref="SpecKind"/> so the rule engine keeps one hint
            /// string and this screen carries its own wording. A kind this does not name falls
            /// back to that hint.
            /// </summary>
            /// <param name="kind">The kind.</param>
            private static string FirstLabel(SpecKind kind) => kind.Id switch
            {
                "maxVolume" => "Volume (m³)",
                "distance" => "From (m)",
                "angle" => "From (°)",
                "eitherInSet" => "Set name",
                "bothInSet" => "Set name",
                "propertyEquals" => "Property",
                "propertyContains" => "Property",
                "category" => "Category",
                "proximity" => "Distance (m)",
                "property" => "Property",
                _ => Capital(kind.Hint),
            };

            /// <summary>What the second box is called, for the kinds that have one.</summary>
            /// <param name="kind">The kind.</param>
            private static string SecondLabel(SpecKind kind) => kind.Id switch
            {
                "distance" => "To (m)",
                "angle" => "To (°)",
                "propertyEquals" => "Equals",
                "propertyContains" => "Contains",
                _ => "Value",
            };

            /// <summary>A hint written as a label.</summary>
            /// <param name="text">The hint.</param>
            private static string Capital(string text) =>
                text.Length == 0 ? "Value" : char.ToUpperInvariant(text[0]) + text.Substring(1);

            /// <summary>Label the operand boxes for the kind now picked, and hide the ones it has no use for.</summary>
            private void Relabel()
            {
                var kind = Kind();
                if (kind == null) return;

                _firstLabel.Text = FirstLabel(kind);
                _secondLabel.Text = SecondLabel(kind);

                _firstBox.Visibility = kind.Input == SpecInput.None || kind.Input == SpecInput.Nested
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                _secondBox.Visibility = kind.Input == SpecInput.Range || kind.Input == SpecInput.NameAndValue
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        // ===================================================================================
        // Report
        // ===================================================================================

        /// <summary>
        /// What the board holds, over the report about to be written.
        ///
        /// The board is built on Manage and every report is taken from it, so this tab opens on
        /// whether there is one rather than saying so at the press.
        /// </summary>
        /// <param name="whenMissing">What pressing on without a board means, on this tab.</param>
        private static UIElement BoardState(string whenMissing)
        {
            var board = LastBoard;

            if (board == null)
                return LoadHere("No board is loaded. " + whenMissing);

            return Bench.Scope("On the board: " + Ui.Count(board.Groups.Count, "issue") + ", "
                + Ui.Count(board.Groups.Sum(g => g.Items.Count), "clash")
                + (string.IsNullOrEmpty(LastBoardLabel) ? string.Empty : " — " + LastBoardLabel));
        }

        /// <summary>
        /// Where the same board goes next, offered once a report has been written.
        ///
        /// A report is read and filed; the answers come back as BCF, which is the Exchange tab.
        /// The two buttons this used to draw named an app id and two tool ids that do not exist,
        /// so both only ever reported that there is no such app.
        /// </summary>
        /// <param name="into">Where the write just reported.</param>
        private static void NextSteps(Panel into)
        {
            into.Children.Add(Ui.Actions(Ui.Quiet("Open the Exchange tab",
                () => Ui.Told(Shell.AppWindow.Show("clash", "exchange")))));

            into.Children.Add(Ui.Small("Exchange sends the same board as BCF and reads the "
                + "answers back onto it."));
        }

        /// <summary>
        /// The report, on the live seat.
        ///
        /// A report is written from the board, which is built from the open model, so there is
        /// nothing to write without one. The screen itself takes a seat rather than a session, so
        /// it can be drawn without one.
        /// </summary>
        public static UIElement Report()
        {
            var seat = Host.Current;

            if (seat == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty(
                    "No model is open.",
                    "A report is written from the clash board, and the board is built from the "
                    + "results in the open model. Open the model this report is about.")));

            return Report(seat);
        }

        /// <summary>The report screen on a given seat.</summary>
        /// <param name="seat">The seat. Its project file holds the settings and the revisions.</param>
        public static UIElement Report(ISeat seat) => new ReportScreen(seat).Build();

        /// <summary>The penetration schedule, as its own tool rather than a document choice.</summary>
        internal static UIElement PenetrationsReport()
        {
            var seat = Host.Current;

            if (seat == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty(
                    "No model is open.",
                    "The schedule lists what the model says passes through structure, so it "
                    + "needs that model open.")));

            return new ReportScreen(seat, "Penetration schedule").Build();
        }

        /// <summary>
        /// The report: which document, who it is for, and what goes in it.
        ///
        /// Three documents come off one board — the full report, the field pack and the
        /// penetration schedule — and each can be written for one party or for everybody, with a
        /// revision letter kept per party.
        ///
        /// The board is clash-shaped, so these documents carry the clash results and the issues
        /// raised by hand on the board. The screen states that rather than implying it covers the
        /// clearance and fall-hazard findings the same project holds.
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
            private const string FieldPack = "Field pack";

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

            /// <summary>The preview of the document about to be written, redrawn on every setting change.</summary>
            private readonly StackPanel _shape = Ui.Stack();

            /// <summary>The seat. Everything the screen reads without a document comes from here.</summary>
            private readonly ISeat _seat;

            /// <summary>The live session, or null on a seat with no document. Writing needs one.</summary>
            private readonly Session? _live;

            /// <summary>What this tab was last told, so it does not ask again.</summary>
            private readonly ToolMemory _kept;

            private readonly TextBox _path;
            private readonly TextBox _template;

            /// <summary>The line under the document picker, retuned when the pick changes.</summary>
            private readonly TextBlock _what = Ui.Small(string.Empty);

            /// <summary>The three controls an empty result blames, so it can offer the way back.</summary>
            private ComboBox? _for;

            private CheckBox? _changed;

            private CheckBox? _split;

            private string _units;

            /// <summary>
            /// Which document this screen is fixed to, or null when the user picks.
            ///
            /// The penetration schedule is its own ribbon button: a structural engineer asking for
            /// the list before a pour is not writing a clash report, and a window that answers
            /// "which of three documents?" first is asking a question they do not have.
            /// </summary>
            private readonly string? _locked;

            /// <summary>Open the screen on a seat.</summary>
            /// <param name="seat">The seat. Its project file holds the settings and the revisions.</param>
            /// <param name="locked">The one document this screen writes, or null when the user picks.</param>
            internal ReportScreen(ISeat seat, string? locked = null)
            {
                _seat = seat ?? throw new ArgumentNullException(nameof(seat));
                _live = seat as Session;

                _locked = locked;
                if (locked != null) _mode = locked;

                _kept = ToolMemory.Of(seat.Store, "coordinate.report");

                _path = Ui.Text(_kept.Text("folder"), text => { _kept.Set("folder", text); Restate(); }, 200);

                _template = Ui.Text(_kept.Text("name", Deliverable.DefaultTemplate),
                                    text => { _kept.Set("name", text); Restate(); }, 200);

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

            /// <summary>How wide a paragraph is allowed to be, in device pixels — about 80 characters.</summary>
            private const double TextWidth = 620;

            /// <summary>How wide the options panel is. Its one text field is capped by it.</summary>
            private const double PanelWidth = 360;

            internal UIElement Build()
            {
                var parties = new List<string> { Everyone, Nobody };
                parties.AddRange(_seat.Profile.Parties);

                _for = Ui.Choice(parties, _audience, a => { _audience = a; Restate(); }, 150);

                var format = Ui.Segmented(new[] { "PDF", "XLSX", "HTML" }, _format,
                                          f => { _format = f; _kept.Set("format", f); Restate(); });

                _changed = Ui.Check("Only issues that are new or back after being resolved",
                                    _changedOnly,
                                    v => { _changedOnly = v; _kept.Set("changedOnly", v); Restate(); });

                var plan = Ui.Check("A site plan with every issue numbered on it", _keyPlan,
                                    v => { _keyPlan = v; _kept.Set("keyPlan", v); Restate(); });

                _split = Ui.Check("A separate file for each party", _perParty,
                                  v => { _perParty = v; _kept.Set("perParty", v); Restate(); });

                var pictures = Ui.Check("A rendered view of each issue", _pictures,
                                        v => { _pictures = v; _kept.Set("pictures", v); Restate(); });

                var cost = Ui.Small("A rendered view moves the model view while it runs, once "
                                    + "per issue.");

                // Only the full report has a site plan, and the schedule has no pictures at all.
                // Shown against the document rather than left on screen doing nothing.
                void Retune()
                {
                    _what.Text = Explain(_mode);

                    plan.Visibility = _mode == FullReport ? Visibility.Visible : Visibility.Collapsed;

                    var images = _mode != Penetration;

                    pictures.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
                    cost.Visibility = images ? Visibility.Visible : Visibility.Collapsed;

                    Restate();
                }

                var document = Ui.Choice(new[] { FullReport, FieldPack, Penetration }, _mode, m =>
                {
                    _mode = m;
                    Retune();
                }, 200);

                Retune();

                // Locked: the document is the tool, so the picker is not a choice this window has.
                var setup = Ui.Stack(
                    Bench.Group(string.Empty,
                        _locked == null ? Ui.Field("Document", document) : null,
                        _what,
                        Ui.Across(Ui.Field("Recipient", _for), Ui.Field("File format", format))),
                    Bench.Group("What else is in it", _changed, plan, _split, pictures, cost),
                    Bench.Group("Where it goes",
                        Ui.PathField("Folder", _path, PathKind.Folder,
                                     hint: "blank writes it to your Documents folder"),
                        Ui.Field("File name", _template,
                                 "tokens: " + string.Join(", ", Deliverable.Tokens))));

                var help = Ui.Icon(Glyphs.Info, "What goes in each document", Help);
                var markers = Ui.Icon(Glyphs.Sphere, "Markers in the model", Markers);

                var write = _live == null
                    ? Ui.Blocked("Write the report",
                                 "A report is written from the board, which needs an open model.")
                    : Ui.Runner("Write the report", _results, Write);

                // The scope, the coverage and the way to load a board are kept out of _results:
                // a runner clears its output panel on every press, so anything put there is gone
                // the first time the button is used and never comes back.
                var stage = Ui.Stack(
                    _live == null
                        ? Ui.Empty("No model is open.",
                                   "The report is written from the clash board, which is built "
                                   + "from the results in the open model.")
                        : BoardState("Writing builds it fresh from the tests and the saved rules."),
                    Say(Covers()),
                    _shape,
                    _results);

                Restate();

                return Bench.Surface(setup, Ui.Across(help, markers, write), stage, PanelWidth);
            }

            /// <summary>
            /// What the press will produce, read off the board and the settings on the right.
            ///
            /// Every control on this screen is in the panel, so without this the stage carried a
            /// scope line and a sentence and nothing else. It holds the document instead: how much
            /// of the board this recipient gets, what goes into the file, which revision it
            /// becomes, where it lands, and what has been issued before. Redrawn whenever a
            /// setting changes, so a setting has somewhere to show its effect before the press.
            /// </summary>
            private void Restate()
            {
                _shape.Children.Clear();

                var session = _live;
                var board = LastBoard;

                if (session == null || board == null) return;

                var regressed = Regressed();

                // One file per party still reports on the whole board, split at the end, so the
                // preview counts the board rather than one recipient's share of it.
                var audience = _perParty ? Everyone : _audience;
                var groups = Chosen(board, audience, regressed);

                _shape.Children.Add(Title("In this issue"));

                if (groups.Count == 0)
                {
                    _shape.Children.Add(Nothing());
                    return;
                }

                var parties = groups
                    .Select(g => g.AssignedTo)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var unowned = groups.Count(g => g.AssignedTo == null);

                _shape.Children.Add(Ui.Across(
                    Ui.Figure(Tallied(groups.Count), "issues"),
                    Ui.Figure(Tallied(groups.Sum(g => g.Items.Count)), "clashes"),
                    Ui.Figure(Tallied(parties), "parties"),
                    Ui.Figure(Tallied(unowned), "issues unassigned",
                              unowned > 0 ? Tone.Warn : Tone.Good)));

                foreach (var section in Sections()) _shape.Children.Add(Say("— " + section));

                if (_perParty)
                {
                    _shape.Children.Add(Say(Ui.Count(parties + (unowned > 0 ? 1 : 0), "file")
                        + ", one for each party on the board"
                        + (unowned > 0 ? " and one for the unassigned issues" : string.Empty)
                        + ". Each takes the next letter in that party's own sequence."));
                }
                else
                {
                    var was = LastIssue(session, audience);
                    var revision = Deliverable.NextRevision(was?.Revision);
                    var now = DateTimeOffset.UtcNow;

                    _shape.Children.Add(Say("This issue is revision " + revision + ". "
                        + (was == null
                            ? "Nothing has been issued to this recipient before."
                            : was.Supersedes)));

                    _shape.Children.Add(Say(Lands(session, audience, revision, now)));
                }

                History(session);
            }

            /// <summary>
            /// Where the file will be written, without the side effect of working it out for real.
            ///
            /// <see cref="Where"/> fills the folder box in when it is blank, which from here would
            /// re-enter this method through that box's change handler.
            /// </summary>
            /// <param name="session">The live session, for the project name.</param>
            /// <param name="audience">Who it is for.</param>
            /// <param name="revision">The revision it will take.</param>
            /// <param name="issued">When it goes out.</param>
            private string Lands(Session session, string audience, string revision,
                                 DateTimeOffset issued)
            {
                var typed = _path.Text.Trim();

                if (typed.Length > 0) return "Writes to " + typed + ".";

                var stem = Deliverable.Name(_template.Text, session.Profile.ProjectName,
                                            audience == Everyone ? null : audience, revision, issued);

                return "Writes " + stem + "." + _format.ToLowerInvariant()
                       + " to your Documents folder.";
            }

            /// <summary>What the chosen document holds, in the order it holds it.</summary>
            private IEnumerable<string> Sections()
            {
                if (_mode == Penetration)
                {
                    yield return "A cover naming the project, the recipient and the revision.";
                    yield return "Every service the model shows passing through structure, with "
                                 + "what it is and where.";
                    yield break;
                }

                yield return "A cover naming the project, the recipient and the revision.";

                if (_mode == FullReport)
                {
                    yield return "The funnel the board was narrowed by, and what changed since "
                                 + "the last run.";

                    if (_keyPlan) yield return "A site plan with every issue numbered on it.";

                    yield return "Every issue with its clashes, party and priority.";
                }
                else
                {
                    yield return "One page per issue, in large type, with a line to sign against.";
                }

                if (_pictures) yield return "A rendered view of each issue.";
            }

            /// <summary>
            /// What has gone out before, per recipient.
            ///
            /// The register is what the next revision letter is derived from, so it belongs where
            /// the letter is stated rather than only on the cover of the file.
            /// </summary>
            /// <param name="session">The live session, whose project file holds the register.</param>
            private void History(Session session)
            {
                var register = Deliverable.Read(
                    session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Issued]);

                if (register.Count == 0) return;

                _shape.Children.Add(Title("Issued so far"));

                foreach (var entry in register.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                    _shape.Children.Add(Say(entry.Key + " — revision " + entry.Value.Revision
                        + ", issued "
                        + entry.Value.IssuedUtc.UtcDateTime.ToString("d MMMM yyyy",
                                                                     CultureInfo.InvariantCulture)
                        + (string.IsNullOrWhiteSpace(entry.Value.File)
                            ? "."
                            : " as " + entry.Value.File + ".")));
            }

            /// <summary>A count as it reads beside a figure.</summary>
            /// <param name="count">The count.</param>
            private static string Tallied(int count) =>
                count.ToString("N0", CultureInfo.InvariantCulture);

            /// <summary>A section heading on the stage: sentence case, one size, one weight.</summary>
            /// <param name="text">The heading.</param>
            private static TextBlock Title(string text)
            {
                var head = Ui.Heading(text);
                head.Margin = new Thickness(0, 18, 0, 6);
                return head;
            }

            /// <summary>A line of orientation, capped at a readable measure.</summary>
            /// <param name="text">The line.</param>
            private static TextBlock Say(string text)
            {
                var line = Ui.Line(text, 0.75);
                line.MaxWidth = TextWidth;
                line.HorizontalAlignment = HorizontalAlignment.Left;
                return line;
            }

            /// <summary>
            /// What these documents cover, and what the project holds that they do not.
            ///
            /// The board is the clash results and the issues raised by hand on it. The clearance
            /// and fall-hazard checks write into the same register and none of their findings
            /// reach a report, so the count is stated here rather than left to be discovered by
            /// whoever reads the PDF.
            /// </summary>
            private string Covers()
            {
                var covers = "Covers the clash board: clash results, and issues raised by hand.";

                // The two kinds the Manage board carries beside clash. Health, IDS and compare
                // findings are also in this register and belong to other apps, so naming them
                // here would read as a list of everything this report is not.
                var beside = new[] { FindingSource.Headroom, FindingSource.FallHazard };

                var others = _seat.Findings.Findings.Present
                    .Where(f => beside.Contains(f.Source, StringComparer.OrdinalIgnoreCase))
                    .GroupBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (others.Count == 0) return covers;

                return covers + " Not in it: " + string.Join(", ", others.Select(one =>
                    Ui.Count(one.Count(), Kind(one.Key) + " result"))) + ".";
            }

            /// <summary>What a check's findings are called, in that check's own words.</summary>
            /// <param name="key">The finding's source.</param>
            private string Kind(string key) =>
                (_seat.Checks.Find(key)?.Name ?? key).ToLowerInvariant();

            /// <summary>
            /// The clashes as geometry: one marker per clash, written as an OBJ the host appends.
            ///
            /// Behind its own symbol rather than on the surface. It produces a model file rather
            /// than a document, and it is set up once for a job rather than once per report.
            /// </summary>
            private void Markers() =>
                Ui.Configure("Markers in the model", null,
                             new Ui.Page("Markers in the model", MarkerSetup));

            /// <summary>The marker settings and the run, inside the window the symbol opened.</summary>
            private UIElement MarkerSetup()
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

                // The run reports into this window. Its answers used to be written to a panel on
                // the tab behind it, so nothing appeared until the window was closed.
                var said = Ui.Stack();

                return Ui.Stack(
                    Say("One marker per clash on the board, grouped by test, written as an OBJ "
                        + "file that can be appended to the model."),
                    Ui.Field("Shape", shape),
                    Ui.Field("Marker diameter (mm)", size),
                    Ui.Field("Model units", units),
                    Ui.Runner("Make the markers", said, job => Mark(job, recipe, said),
                              primary: false),
                    said);
            }

            /// <summary>Write the markers, reporting into the window the settings are in.</summary>
            /// <param name="job">The run this is part of.</param>
            /// <param name="recipe">The shape and size the settings above describe.</param>
            /// <param name="said">Where this run reports, inside the markers window.</param>
            private void Mark(Job job, MarkerRecipe recipe, Panel said)
            {
                said.Children.Clear();

                var session = _live;
                if (session == null) { said.Children.Add(Ui.Line(Host.NoModel)); return; }

                var board = LastBoard;

                if (board == null)
                {
                    said.Children.Add(LoadHere("No board is loaded, and this marks what the "
                        + "board holds."));
                    return;
                }

                // The conversion the whole thing turns on. The host reports clash points in the
                // document's units and its OBJ reader takes every vertex as metres.
                var word = _units == FromDocument ? session.Document.Units.ToString() : _units;
                var metres = Measure.MetresPer(word);

                if (metres == null)
                {
                    said.Children.Add(Ui.Problem("This document is in \"" + word + "\", which is "
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

                if (mesh.Refusal != null) { said.Children.Add(Ui.Problem(mesh.Refusal)); return; }

                foreach (var warning in mesh.Warnings) said.Children.Add(Ui.Note(warning));

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
                    said.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                said.Children.Add(Ui.Done("Wrote " + mesh.Explain() + " to " + obj));

                said.Children.Add(Ui.Actions(Ui.Primary("Bring them in", () =>
                {
                    try
                    {
                        said.Children.Add(session.Model.Document.TryAppendFile(obj)
                            ? Ui.Done("Appended. They are in the Selection Tree under their tests, "
                                      + "and in the host's file list if you want them out again.")
                            : Ui.Problem("The host would not append that file."));
                    }
                    catch (Exception e)
                    {
                        said.Children.Add(Ui.Problem("Could not append it: " + Ui.Describe(e)));
                    }
                })));

                // Kept at the press rather than at every keystroke: this is the moment somebody
                // committed to the settings, and the next run opens on them.
                _kept.Keep();

                session.Record(ActivityKind.Export, "made clash markers: " + mesh.Explain(), obj);
            }

            /// <summary>One line saying what the chosen document is, at the point it is chosen.</summary>
            /// <param name="mode">Which document.</param>
            private static string Explain(string mode) => mode switch
            {
                FieldPack => "One issue per page, in type big enough to read one-handed, with a "
                             + "line to sign against.",

                Penetration => "Every service that passes through structure, as the model states "
                               + "it — no opening size derived.",

                _ => "Every issue on the board with its clashes, party and priority.",
            };

            /// <summary>
            /// The reference matter, behind one symbol rather than standing on the surface.
            ///
            /// Three documents, a revision rule and a coverage limit: each is read once and then
            /// known, so none of them is worth a paragraph beside a control.
            /// </summary>
            private void Help() =>
                Ui.Configure("What goes in each document", null,
                    new Ui.Page("The three documents", () => Ui.Stack(
                        Part("Full report",
                             "The whole board laid out for a desk: the funnel the board was "
                             + "narrowed by, what changed since the last run, then every issue "
                             + "with its clashes, party and priority."),
                        Part("Field pack",
                             "The same board set for a site cabin: one issue per page, large "
                             + "type, and a line to sign against."),
                        Part("Penetration schedule",
                             "Every service the model shows passing through structure, with what "
                             + "it is and where. No opening size is derived and no host thickness "
                             + "is inferred: those dimensions are used to cut concrete and are "
                             + "not estimated here."))),
                    new Ui.Page("Revisions and coverage", () => Ui.Stack(
                        Part("Revisions",
                             "Each issue takes the next revision letter and its cover names the "
                             + "one it supersedes. Each party has its own sequence, so writing "
                             + "one file per party moves only that party's letter on."),
                        Part("What is covered",
                             "The clash board: the clash results the engine reported and the "
                             + "issues raised by hand on the board. Clearance and fall-hazard "
                             + "findings are held in the same register and are not written into "
                             + "any of these documents."),
                        Part("Where it goes",
                             "The folder and the file name are kept per project. A blank folder "
                             + "writes to your Documents folder, and the name is filled in from "
                             + "the tokens listed under it."))));

            /// <summary>One passage of the help window: a sentence-case heading and its paragraph.</summary>
            /// <param name="title">What it is about.</param>
            /// <param name="text">The passage.</param>
            private static UIElement Part(string title, string text)
            {
                var head = Ui.Heading(title);
                head.Margin = new Thickness(0, 0, 0, 4);

                var body = Ui.Small(text);
                body.MaxWidth = TextWidth;
                body.Margin = new Thickness(0, 0, 0, 16);

                return Ui.Stack(head, body);
            }

            private void Write(Job job)
            {
                var session = _live;
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
                    _results.Children.Add(Nothing());
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
                if (_mode != Penetration) NextSteps(_results);

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
                    _results.Children.Add(Ui.Empty(
                        "No recipients.",
                        "No issue on the board is assigned to a party, and there are no "
                        + "unassigned ones either.",
                        "Write one file instead", () => { if (_split != null) _split.IsChecked = false; }));
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

                if (written > 0 && _mode != Penetration) NextSteps(_results);

                session.Record(ActivityKind.Report,
                    "wrote " + Ui.Count(written, "clash report") + ", one per party");
            }

            /// <summary>
            /// Nothing matched, said against the setting that caused it and carrying the control
            /// that undoes it.
            /// </summary>
            private UIElement Nothing()
            {
                if (_changedOnly)
                    return Ui.Empty(
                        "Nothing new to report.",
                        "Every issue this filter would report was on the last run as well.",
                        "Report the whole board",
                        () => { if (_changed != null) _changed.IsChecked = false; });

                if (_audience != Everyone)
                    return Ui.Empty(
                        "Nothing to report for " + _audience + ".",
                        "No issue on the board is theirs.",
                        "Report everyone",
                        () => { if (_for != null) _for.SelectedItem = Everyone; });

                return Ui.Empty("Nothing to report.", "The board holds no issues.");
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
