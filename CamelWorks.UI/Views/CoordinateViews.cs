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

            var board = ClashPipeline.Run(survivors, options);

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

            var board = ClashPipeline.Run(hydrated.Items, options);

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

        private static void Save(Session session, ClashPipelineResult board)
        {
            var snapshot = ClashSnapshot.Of(board);

            var array = JsonValue.Array(snapshot.Records.Select(r =>
            {
                var json = JsonValue.Object()
                    .Set("key", JsonValue.String(r.Key.ToString()))
                    .Set("status", JsonValue.String(r.Status.ToString()));

                if (r.GroupName != null) json.Set("group", JsonValue.String(r.GroupName));
                if (r.GroupWasPinned) json.Set("pinned", JsonValue.String("yes"));
                if (r.TestName != null) json.Set("test", JsonValue.String(r.TestName));
                return json;
            }));

            session.Store.Section(ProjectStore.ClashSection).Set("snapshot", array);
            session.Record(ActivityKind.Regroup, "ran the rules: " + board.Funnel);
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
            private readonly HashSet<ClashItem> _regressed = new HashSet<ClashItem>();

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

                    _regressed.Clear();
                    if (_delta != null) foreach (var item in _delta.Regressed) _regressed.Add(item);

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
                Changed => group.Items.Any(i => i.Status == FindingStatus.New || _regressed.Contains(i)),
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

        private static UIElement Detail(Session session, ClashGroup group)
        {
            var status = Ui.Stack();

            var rows = group.Items.Take(300).Select(item => new TableRow(item,
                item.TestName,
                item.Level ?? "-",
                item.ModelPair,
                (item.CategoryA ?? "?") + " v " + (item.CategoryB ?? "?"),
                item.Status.ToString())).ToList();

            var table = Ui.Table(rows,
                ("Test", 130), ("Level", 90), ("Models", 170), ("What", 170), ("Status", 90))
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

            return Ui.Card(group.Name,
                Ui.Count(group.Items.Count, "clash") + (group.Items.Count > 300 ? ", first 300 shown" : string.Empty),
                Ui.Field("Assign to", picker),
                Ui.Across(assign),
                status,
                table);
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
        public static UIElement Report()
        {
            var results = Ui.Stack();
            var path = Ui.Text(string.Empty, null, 340);
            var format = "PDF";
            var picker = Ui.Choice(new[] { "PDF", "XLSX", "HTML" }, format, f => format = f);

            var write = Ui.Runner("Write the report", results, job =>
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
                        session.Profile.ProjectName + " clash report." + format.ToLowerInvariant());

                    path.Text = file;
                }

                job.Say("Building the report...");
                var document = Document(session, board);

                job.Say("Writing...");

                switch (format)
                {
                    case "XLSX":
                        File.WriteAllBytes(file, XlsxWriter.Write("Clashes", Table(board)));
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

                results.Children.Add(Ui.Line("Wrote " + file, 1, true));
                session.Record(ActivityKind.Report, "wrote a " + format + " clash report", file);
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Report"),
                Ui.Sub("Of the board as it stands. Nothing to lay out first — the template is built in, and "
                       + "the numbers on the cover are the funnel's, so the report and the screen agree."),
                Ui.Field("Format", picker),
                Ui.Field("Write to", path),
                write,
                results));
        }

        private static ReportDocument Document(Session session, ClashPipelineResult board)
        {
            var document = new ReportDocument(session.Profile.ProjectName + " — clash report")
            {
                Subtitle = "Produced by CamelWorks",
            };

            document.Fact("Project", session.Profile.ProjectName);
            document.Fact("Author", session.Profile.Author);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            document.Fact("From the engine", board.Funnel.Input.ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("On the board", board.Groups.Sum(g => g.Items.Count).ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("Groups", board.Groups.Count.ToString("N0", CultureInfo.InvariantCulture));
            document.Fact("Unassigned", board.Funnel.Unassigned.ToString("N0", CultureInfo.InvariantCulture));

            document.Heading("How this list was produced");
            document.Paragraph(board.Funnel.ToString());

            document.Paragraph(
                "Every result the engine produced is accounted for above: what was removed, by which rule, "
                + "and what remains. A report that quietly showed a smaller number than the engine found "
                + "would be worse than no report.");

            document.Heading("Groups");
            document.Add(Summary(board));

            document.PageBreak();
            document.Heading("Every clash");
            document.Add(Table(board));

            return document;
        }

        private static TableBlock Summary(ClashPipelineResult board)
        {
            var table = new TableBlock("Group", "Clashes", "Assigned to", "Priority");

            foreach (var group in board.Groups)
                table.Row(group.Name,
                    group.Items.Count.ToString("N0", CultureInfo.InvariantCulture),
                    group.AssignedTo ?? "unassigned",
                    group.Priority ?? string.Empty);

            return table;
        }

        private static TableBlock Table(ClashPipelineResult board)
        {
            var table = new TableBlock("Group", "Test", "Level", "Models", "What", "Status", "X", "Y", "Z");

            foreach (var group in board.Groups)
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

            session.View.FrameBox(Box(points));
            viewpoint.Snapshot = session.View.Capture(SnapshotWidth, SnapshotHeight);

            return viewpoint;
        }

        /// <summary>The box a group occupies, in metres, never degenerate.</summary>
        private static BoundingBox Box(IReadOnlyList<(double X, double Y, double Z)> points)
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
