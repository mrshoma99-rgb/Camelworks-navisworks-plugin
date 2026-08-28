using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Measurement;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Clearance Check — the clear height over a walking surface, worked like clash results.
    ///
    /// <b>Why it exists.</b> A duct 1.9 m over a corridor intersects nothing. There is air between
    /// it and the floor, so no clash test at any tolerance will ever report it, and the first
    /// anybody hears of it is somebody walking into it on site.
    ///
    /// <b>Why it looks like the Manage tab.</b> What comes out of it is a list of places somebody
    /// has to argue about, assign and close — which is a clash list in everything but how it was
    /// found. So it gets the same bench: sort on any column, filter on any column, an ordered
    /// grouping chain, element-property columns, bulk status, assignment, comments and grouping on
    /// the picked rows, focus, isolate and marker spheres in the model, the grid to a spreadsheet,
    /// and the whole finding list out as a clash file that the Manage tab's file source opens for
    /// the same review round.
    ///
    /// <b>The two inputs are model-fed.</b> Both sides offer every search set and selection set the
    /// document holds, the current selection, or everything loaded — nothing is typed. The one
    /// guess it still offers is named as a guess, and says which words it matches.
    /// </summary>
    public static class ClearanceViews
    {
        private static readonly string[] Statuses = { "New", "Active", "Reviewed", "Approved", "Resolved" };

        /// <summary>The guess, named as one — and the only option that reads a category.</summary>
        internal const string Guessed = "Walking surfaces (a guess)";

        internal const string Selected = "Current selection";
        internal const string Loaded = "Everything loaded";
        internal const string NotTheSurfaces = "Everything except the surfaces";

        /// <summary>The words the guess matches, on the element's category or type.</summary>
        internal static readonly string[] WalkingWords =
            { "floor", "slab", "stair", "landing", "ramp", "platform", "deck" };

        private const string FromSurface = "Surface";
        private const string FromOver = "What is over it";
        private const string BothSides = "Both";

        private const string AnyColumn = "Any column";

        // The findings, the bench and what was asked for all survive a tab switch: a check over a
        // federation is a minute of traversal, and glancing at another tab must not cost it.
        private static List<Row>? _found;
        private static string _headline = string.Empty;
        private static string _surfaces = Guessed;
        private static string _targets = NotTheSurfaces;
        private static string _height = string.Empty;

        private static readonly List<PropColumn> _columns = new List<PropColumn>();
        private static readonly List<string> _chain = new List<string>();

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Check(Session session) => new Screen(session).Build();

        /// <summary>Forget the last check, so the next visit starts clean.</summary>
        internal static void Forget() => _found = null;

        /// <summary>
        /// Whether an element reads as something people walk on.
        ///
        /// A guess, and one the screen names as a guess and spells out — the alternative is
        /// refusing to run until somebody has built a set, which is the setup tax this product
        /// exists not to charge. Every other way in is a real set the model already holds.
        /// </summary>
        internal static bool IsWalkable(IModelItem item)
        {
            var category = item.Category ?? string.Empty;
            var type = item.TypeName ?? string.Empty;

            foreach (var word in WalkingWords)
                if (category.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>One element as both sides of the check see it, with what it is called.</summary>
        private sealed class Piece
        {
            internal Piece(ElementKey key, string id, string name, string? category, bool walkable,
                           HeadroomElement box)
            {
                Key = key; Id = id; Name = name; Category = category ?? string.Empty;
                Walkable = walkable; Box = box;
            }

            internal ElementKey Key { get; }
            internal string Id { get; }
            internal string Name { get; }
            internal string Category { get; }
            internal bool Walkable { get; }
            internal HeadroomElement Box { get; }
        }

        /// <summary>An element property, read off the surface, off what is over it, or both.</summary>
        private sealed class PropColumn
        {
            internal PropColumn(string tab, string name, string side, string title)
            {
                Id = Guid.NewGuid().ToString("N");
                Tab = tab; Name = name; Side = side; Title = title;
            }

            internal string Id { get; }
            internal string Tab { get; }
            internal string Name { get; }
            internal string Side { get; }
            internal string Title { get; }
        }

        /// <summary>One leaf of the grouping chain, with the rows that fell into it.</summary>
        private sealed class Bucket
        {
            internal Bucket(string name) => Name = name;

            internal string Name { get; }

            internal List<Row> Rows { get; } = new List<Row>();

            internal int Of(string status) =>
                Rows.Count(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// One place the clear height is not there — and everything a person then does to it.
        ///
        /// The judgement half (status, assignment, comments, grouping) is the manager's own, kept
        /// on the row: the host has no clash data to write it into, because no clash engine found
        /// this. It travels into the spreadsheet and into the clash file, which is where the rest
        /// of the round reads it.
        /// </summary>
        private sealed class Row
        {
            internal Row(HeadroomSpan span, Piece floor, Piece over, double minimum)
            {
                Span = span; Floor = floor; Over = over; Minimum = minimum;
            }

            internal HeadroomSpan Span { get; }
            internal Piece Floor { get; }
            internal Piece Over { get; }

            /// <summary>What was asked for when this row was found, in metres.</summary>
            internal double Minimum { get; }

            /// <summary>How this row is named, in the grid and in anything written out.</summary>
            internal string Name { get; set; } = string.Empty;

            /// <summary>The clear height, in metres.</summary>
            internal double Clear => Span.Clear;

            /// <summary>How far short of what was asked for, in metres.</summary>
            internal double Short => Math.Max(0, Minimum - Span.Clear);

            internal string Status { get; set; } = "New";
            internal string Assigned { get; set; } = string.Empty;
            internal string Group { get; set; } = string.Empty;

            internal List<ClashComment> Trail { get; } = new List<ClashComment>();

            internal Dictionary<string, string> Custom { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            internal int Comments => Trail.Count;

            internal string LastComment =>
                Trail.Count == 0 ? string.Empty : Trail[Trail.Count - 1].Body;

            /// <summary>What identifies this finding across two runs: the pair, not the numbers.</summary>
            internal string Pair => Floor.Id + " | " + Over.Id;
        }

        /// <summary>What a previous run's judgement looked like, to carry over into the next.</summary>
        private sealed class Carried
        {
            internal string Status = "New";
            internal string Assigned = string.Empty;
            internal string Group = string.Empty;
            internal List<ClashComment> Trail = new List<ClashComment>();
            internal Dictionary<string, string> Custom = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly Session _session;

            private readonly StackPanel _intro = Ui.Stack();
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _groups = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _columnList = Ui.Stack();
            private readonly StackPanel _chainList = Ui.Stack();
            private readonly StackPanel _fill = Ui.Stack();
            private readonly StackPanel _filterScope = Ui.Stack();

            private readonly TextBox _minimum;
            private readonly TextBox _filter;
            private readonly TextBox _comment;
            private readonly TextBox _group;
            private readonly ComboBox _assign;

            private readonly HashSet<string> _show =
                new HashSet<string>(Statuses, StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> _pickedGroups = new HashSet<string>(StringComparer.Ordinal);

            private string _mark = "Reviewed";
            private string _filterIn = AnyColumn;
            private bool _onlyCommented;
            private double _markerAcross = 300;

            private string _newTab = string.Empty;
            private string _newProp = string.Empty;
            private string _newSide = BothSides;
            private string _chainAdd = string.Empty;

            /// <summary>What the grid currently shows, for the writers.</summary>
            private List<Row> _shown = new List<Row>();

            internal Screen(Session session)
            {
                _session = session;

                _minimum = Ui.Text(_height.Length > 0 ? _height
                                       : session.Profile.Units.Bare(Headroom.DefaultMinimum),
                                   text => _height = text, 90);

                _filter = Ui.Text(string.Empty, null, 150);
                _filter.TextChanged += (s, e) => RedrawGroups();

                _comment = Ui.Text(string.Empty, null, 170);
                _group = Ui.Text(string.Empty, null, 170);
                _assign = Suggesting(Assignees());
            }

            private Measure Units => _session.Profile.Units;

            internal UIElement Build()
            {
                var run = Ui.Runner("Check", _log, Run);

                var stage = Ui.Stack(_intro, _log, _groups, _results);

                if (_found == null)
                {
                    _intro.Children.Add(Bench.Ready(
                        "Measures the clear height over every surface you name and lists every "
                        + "place the minimum is not there.",
                        "A duct 1.9 m over a corridor clashes with nothing — this is the check "
                        + "that finds it, and the findings work like clash results."));
                }
                else
                {
                    if (_headline.Length > 0) _log.Children.Add(Ui.Line(_headline, 1, true));
                    Redraw();
                }

                return Bench.Surface(Setup(), run, stage);
            }

            // -------------------------------------------------------------------------------
            // The options panel
            // -------------------------------------------------------------------------------

            private UIElement Setup()
            {
                var panel = Ui.Stack();

                var sets = Sets();

                var from = new List<string> { Guessed, Selected, Loaded };
                from.AddRange(sets);

                var to = new List<string> { NotTheSurfaces, Selected, Loaded };
                to.AddRange(sets);

                if (!from.Contains(_surfaces, StringComparer.OrdinalIgnoreCase)) from.Add(_surfaces);
                if (!to.Contains(_targets, StringComparer.OrdinalIgnoreCase)) to.Add(_targets);

                panel.Children.Add(Bench.Group("What to measure",
                    Ui.Field("Clear height", _minimum, "the minimum, in " + Units.LengthUnit),
                    Ui.Field("Measure up from", Ui.Choice(from, _surfaces, pick => _surfaces = pick, 210),
                             "the walking surface"),
                    Ui.Field("Measure up to", Ui.Choice(to, _targets, pick => _targets = pick, 210),
                             "what might be too low over it"),
                    Ui.Small("Both lists are this model's own search sets and selection sets. "
                             + "\"" + Guessed + "\" is the only one that guesses: it takes anything "
                             + "whose category or type contains " + string.Join(", ", WalkingWords)
                             + ". Name a set instead and nothing is guessed at all."),
                    sets.Count == 0
                        ? Ui.Note("This document holds no saved sets yet. Build one in Set Builder "
                                  + "and it appears in both lists.")
                        : null));

                var showing = Statuses.Select(status => (UIElement)Ui.Check(status,
                    _show.Contains(status), on =>
                    {
                        if (on) _show.Add(status); else _show.Remove(status);
                        RedrawGroups();
                    })).ToList();

                RedrawFilterScope();

                panel.Children.Add(Bench.Group("Show findings that are",
                    Ui.Stack(showing.ToArray()),
                    Ui.Check("Only ones with comments", _onlyCommented, on =>
                    {
                        _onlyCommented = on;
                        RedrawGroups();
                    }),
                    Ui.Field("Where", _filterScope),
                    Ui.Field("contains", _filter,
                             "matches as you type - any column, or just the one picked")));

                RedrawColumnList();
                RedrawChainList();

                panel.Children.Add(Bench.Group("Property columns",
                    Ui.Field("Tab", Pickers.Tab(_newTab, pick => _newTab = pick)),
                    Ui.Field("Property", Pickers.Property(_newProp, pick => _newProp = pick)),
                    Ui.Field("Read from", Ui.Choice(new[] { BothSides, FromSurface, FromOver },
                                                    _newSide, pick => _newSide = pick, 150)),
                    Ui.Actions(Ui.Quiet("Add", AddColumn)),
                    _columnList,
                    Ui.Runner("Read values", _fill, primary: false, work: FillColumns),
                    _fill,
                    Ui.Small("Each column reads that property off the elements themselves - the "
                             + "level, the system, the room - and the grouping below chains "
                             + "over it.")));

                panel.Children.Add(Bench.Group("Group by, in order",
                    _chainList,
                    Ui.Small("Chained: the first entry makes the bands, the next splits each "
                             + "band, and so on. Pick group rows to work just those findings.")));

                panel.Children.Add(Bench.Group("On the picked findings",
                    Ui.Field("Mark as", Ui.Choice(Statuses, _mark, pick =>
                    {
                        _mark = pick;
                        RedrawResults();
                    }, 150)),
                    Ui.Field("Assign to", _assign, "the project's parties, or type a name"),
                    Ui.Field("Comment", _comment),
                    Ui.Field("Group into", _group, "blank moves them out of any group")));

                panel.Children.Add(Bench.Group("In the model",
                    Ui.Field("Marker size", Ui.Text(
                        _markerAcross.ToString(CultureInfo.InvariantCulture), text =>
                        {
                            if (double.TryParse(text, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var mm) && mm > 0)
                                _markerAcross = mm;
                        }, 90), "millimetres across"),
                    Ui.Actions(Ui.Quiet("Show all", () =>
                    {
                        _session.View.ShowAll();
                        Said(Ui.Done("Everything is visible again."));
                    }))));

                panel.Children.Add(Bench.Group("Take it further",
                    Ui.Actions(
                        Ui.Quiet("Excel...", Sheet),
                        Ui.Quiet("Clash file...", SaveFile)),
                    Ui.Small("The clash file carries every finding with its status, assignment, "
                             + "comments and group - open it on the Manage tab's file source and "
                             + "it works the same review round as a clash export.")));

                return panel;
            }

            /// <summary>Every saved set this document holds, named once.</summary>
            private List<string> Sets()
            {
                try
                {
                    return _session.Model.Sets()
                                   .Select(set => set.DisplayName)
                                   .Where(name => !string.IsNullOrWhiteSpace(name))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            }

            // -------------------------------------------------------------------------------
            // The check
            // -------------------------------------------------------------------------------

            private void Run(Job job)
            {
                if (!Units.TryRead(_minimum.Text, out var minimum) || minimum <= 0)
                {
                    _log.Children.Add(Ui.Problem("Type a clear height, like " + Units.Length(2.1)
                        + ". A bare number is read in " + Units.LengthUnit + "."));
                    return;
                }

                var carried = Harvest();

                // Only the three whole-model answers pay for a full traversal. A named set walks
                // its own members, which on a federation is the difference between a second and
                // a minute.
                var wholeNeeded = NeedsWhole(_surfaces) || NeedsWhole(_targets);
                List<Piece>? whole = null;

                if (wholeNeeded)
                {
                    whole = new List<Piece>();
                    var read = 0;

                    foreach (var item in _session.Model.Traverse(TraversalScope.WholeDocument))
                    {
                        if (!job.Step(read++, "elements read")) return;
                        var piece = Make(item);
                        if (piece != null) whole.Add(piece);
                    }
                }

                var surfaces = Gather(_surfaces, whole, null, job);
                if (surfaces == null) return;

                var already = new HashSet<string>(surfaces.Select(p => p.Id), StringComparer.Ordinal);

                var above = Gather(_targets, whole, already, job);
                if (above == null) return;

                if (surfaces.Count == 0)
                {
                    _found = null;
                    _headline = string.Empty;
                    _log.Children.Add(Ui.Empty("No surfaces to measure from.",
                        _surfaces == Guessed
                            ? "Nothing loaded has a category or type that reads as a walking "
                              + "surface. Pick a search set or a selection set in \"Measure up "
                              + "from\" instead - the guess is only there for when you have none."
                            : _surfaces == Selected
                                ? "Nothing is selected in the host."
                                : "\"" + _surfaces + "\" holds nothing this document can resolve."));
                    return;
                }

                job.Say("Measuring...");

                var byId = new Dictionary<string, Piece>(StringComparer.Ordinal);
                foreach (var piece in surfaces) byId[piece.Id] = piece;
                foreach (var piece in above) byId[piece.Id] = piece;

                var found = Headroom.Check(surfaces.Select(p => p.Box), above.Select(p => p.Box),
                                           minimum);

                var rows = new List<Row>();

                foreach (var span in found.Spans)
                {
                    if (!byId.TryGetValue(span.Floor.Id, out var floor)) continue;
                    if (!byId.TryGetValue(span.Obstruction.Id, out var over)) continue;

                    var row = new Row(span, floor, over, minimum)
                    {
                        Name = "Clearance " + (rows.Count + 1).ToString(CultureInfo.InvariantCulture),
                    };

                    Restore(row, carried);
                    rows.Add(row);
                }

                _found = rows;
                _headline = found.ToString();

                _log.Children.Add(Ui.Line(_headline, 1, true));

                if (rows.Count == 0)
                {
                    _groups.Children.Clear();
                    _results.Children.Clear();
                    _results.Children.Add(Ui.Empty("Nothing is too low.",
                        "Every surface has at least " + Units.Length(minimum)
                        + " of clear height above it."));
                }
                else
                {
                    Redraw();
                }

                _session.Record(ActivityKind.Regroup, "checked clear height: " + found);
            }

            private static bool NeedsWhole(string choice) =>
                choice == Guessed || choice == Loaded || choice == NotTheSurfaces;

            /// <summary>
            /// One side's elements.
            ///
            /// <paramref name="exclude"/> is what "everything except the surfaces" means, stated
            /// as the ids already taken by the other side rather than as a category guess — which
            /// is what the old wording could never say plainly.
            /// </summary>
            private List<Piece>? Gather(string choice, List<Piece>? whole, HashSet<string>? exclude,
                                        Job job)
            {
                if (choice == Guessed)
                    return (whole ?? new List<Piece>()).Where(p => p.Walkable).ToList();

                if (choice == Loaded)
                    return (whole ?? new List<Piece>()).ToList();

                if (choice == NotTheSurfaces)
                    return (whole ?? new List<Piece>())
                           .Where(p => exclude == null || !exclude.Contains(p.Id))
                           .ToList();

                var scope = choice == Selected
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.Set(choice);

                var pieces = new List<Piece>();
                var read = 0;

                foreach (var item in _session.Model.Traverse(scope))
                {
                    if (!job.Step(read++, "elements read")) return null;
                    var piece = Make(item);
                    if (piece != null) pieces.Add(piece);
                }

                return pieces;
            }

            /// <summary>An element as the check sees it, or null when it has no geometry.</summary>
            private Piece? Make(IModelItem item)
            {
                if (!item.HasGeometry) return null;

                var scale = _session.MetresPerUnit;
                var bounds = item.Bounds;
                var id = item.Key.ToString();

                var box = new HeadroomElement(id, item.DisplayName)
                {
                    MinX = bounds.MinX * scale, MaxX = bounds.MaxX * scale,
                    MinY = bounds.MinY * scale, MaxY = bounds.MaxY * scale,
                    MinZ = bounds.MinZ * scale, MaxZ = bounds.MaxZ * scale,
                };

                return new Piece(item.Key, id, item.DisplayName ?? id, item.Category,
                                 IsWalkable(item), box);
            }

            /// <summary>The judgement on the last run's rows, by the pair it was about.</summary>
            private static Dictionary<string, Carried> Harvest()
            {
                var carried = new Dictionary<string, Carried>(StringComparer.Ordinal);

                if (_found == null) return carried;

                foreach (var row in _found)
                {
                    if (row.Status == "New" && row.Assigned.Length == 0 && row.Group.Length == 0
                        && row.Comments == 0 && row.Custom.Count == 0)
                        continue;

                    carried[row.Pair] = new Carried
                    {
                        Status = row.Status,
                        Assigned = row.Assigned,
                        Group = row.Group,
                        Trail = row.Trail.ToList(),
                        Custom = new Dictionary<string, string>(row.Custom, StringComparer.Ordinal),
                    };
                }

                return carried;
            }

            /// <summary>
            /// Put a previous run's judgement back on a row it is still about.
            ///
            /// Without this, re-running after a model update reopens everything anybody had closed,
            /// which is the one behaviour that makes a check nobody re-runs.
            /// </summary>
            private static void Restore(Row row, IReadOnlyDictionary<string, Carried> carried)
            {
                if (!carried.TryGetValue(row.Pair, out var was)) return;

                row.Status = was.Status;
                row.Assigned = was.Assigned;
                row.Group = was.Group;

                foreach (var comment in was.Trail) row.Trail.Add(comment);
                foreach (var pair in was.Custom) row.Custom[pair.Key] = pair.Value;
            }

            // -------------------------------------------------------------------------------
            // Drawing
            // -------------------------------------------------------------------------------

            private void Redraw() => RedrawGroups();

            /// <summary>The rows the filters allow, before any group pick narrows them.</summary>
            private List<Row> Filtered()
            {
                if (_found == null) return new List<Row>();

                var rows = _found
                    .Where(r => _show.Contains(r.Status))
                    .Where(r => !_onlyCommented || r.Comments > 0);

                var wanted = _filter.Text.Trim();

                if (wanted.Length > 0) rows = rows.Where(r => Matches(r, wanted));

                return rows.ToList();
            }

            /// <summary>Whether a row's chosen column - or any of them - contains the text.</summary>
            private bool Matches(Row row, string wanted)
            {
                bool Has(string? value) =>
                    value != null && value.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

                if (_filterIn != AnyColumn)
                {
                    foreach (var column in _columns)
                        if (string.Equals(_filterIn, column.Title, StringComparison.Ordinal))
                            return Has(row.Custom.TryGetValue(column.Id, out var v) ? v : null);

                    switch (_filterIn)
                    {
                        case "Finding": return Has(row.Name);
                        case "Surface": return Has(row.Floor.Name);
                        case "Over": return Has(row.Over.Name);
                        case "Category (surface)": return Has(row.Floor.Category);
                        case "Category (over)": return Has(row.Over.Category);
                        case "Status": return Has(row.Status);
                        case "Assigned": return Has(row.Assigned);
                        case "Group": return Has(row.Group);
                        case "Comment": return Has(row.LastComment);
                        default: return false;
                    }
                }

                if (Has(row.Name) || Has(row.Floor.Name) || Has(row.Over.Name)
                    || Has(row.Floor.Category) || Has(row.Over.Category)
                    || Has(row.Status) || Has(row.Assigned) || Has(row.Group)
                    || Has(row.LastComment))
                    return true;

                foreach (var value in row.Custom.Values)
                    if (Has(value)) return true;

                return false;
            }

            private void RedrawFilterScope()
            {
                _filterScope.Children.Clear();

                var options = new List<string>
                {
                    AnyColumn, "Finding", "Surface", "Over", "Category (surface)",
                    "Category (over)", "Status", "Assigned", "Group", "Comment",
                };

                foreach (var column in _columns) options.Add(column.Title);

                if (!options.Contains(_filterIn)) _filterIn = AnyColumn;

                _filterScope.Children.Add(Ui.Choice(options, _filterIn, pick =>
                {
                    _filterIn = pick;
                    if (_filter.Text.Trim().Length > 0) RedrawGroups();
                }, 150));
            }

            private void RedrawGroups()
            {
                _groups.Children.Clear();
                _pickedGroups.Clear();

                if (_chain.Count == 0)
                {
                    RedrawResults();
                    return;
                }

                var rows = Filtered();
                var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);

                foreach (var row in rows)
                {
                    var name = ChainKey(row);

                    if (!buckets.TryGetValue(name, out var bucket))
                    {
                        bucket = new Bucket(name);
                        buckets[name] = bucket;
                    }

                    bucket.Rows.Add(row);
                }

                var ordered = buckets.Values
                    .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _groups.Children.Add(Ui.Eyebrow("Grouped by "
                    + string.Join(" · ", _chain.Select(ChainTitle))));

                var table = Ui.Table(ordered.Select(b => new TableRow(b,
                    b.Name,
                    Skin.Num(b.Rows.Count),
                    Units.Bare(b.Rows.Min(r => r.Clear)),
                    Skin.Num(b.Of("New")),
                    Skin.Num(b.Of("Active")),
                    Skin.Num(b.Of("Reviewed")),
                    Skin.Num(b.Of("Approved")),
                    Skin.Num(b.Of("Resolved")))
                {
                    Tone = b.Of("New") > 0 ? Tone.Warn : Tone.Plain,
                }).ToList(),
                    ("Group", 280), ("Findings", 70), ("Lowest", 70), ("New", 55),
                    ("Active", 55), ("Reviewed", 70), ("Approved", 70), ("Resolved", 0));

                table.SelectionChanged += (s, e) =>
                {
                    _pickedGroups.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is Bucket bucket) _pickedGroups.Add(bucket.Name);

                    RedrawResults();
                };

                _groups.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Mark {n}", (picked, say) => Mark(Flatten(picked), say)),
                    new Ui.OnPicked("Assign {n}", (picked, say) => AssignPicked(Flatten(picked), say)),
                    new Ui.OnPicked("Comment {n}", (picked, say) => CommentPicked(Flatten(picked), say)),
                    new Ui.OnPicked("Group {n}", (picked, say) => GroupPicked(Flatten(picked), say)),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "group"))));

                RedrawResults();
            }

            private static IReadOnlyList<object> Flatten(IReadOnlyList<object> picked) =>
                picked.OfType<Bucket>().SelectMany(b => b.Rows).Cast<object>().ToList();

            private void RedrawResults()
            {
                _results.Children.Clear();

                if (_found == null) return;

                var rows = Filtered();

                if (_chain.Count > 0 && _pickedGroups.Count > 0)
                    rows = rows.Where(r => _pickedGroups.Contains(ChainKey(r))).ToList();

                _results.Children.Add(Ui.Eyebrow(
                    _chain.Count > 0 && _pickedGroups.Count > 0
                        ? "Findings in " + Ui.Count(_pickedGroups.Count, "picked group")
                        : "Every finding"));

                if (rows.Count == 0 && _found.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing is too low.",
                        "Every surface has the clear height that was asked for. The check ran; "
                        + "it found nothing to report."));
                    return;
                }

                if (rows.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing matches.",
                        "Every finding is filtered out. Untick a status on the left, or clear "
                        + "the contains filter."));
                    return;
                }

                const int most = 2500;
                var shown = rows.Count > most ? rows.Take(most).ToList() : rows;

                _shown = shown;

                var headers = new List<(string, double)>
                {
                    ("Finding", 100),
                    ("Clear (" + Units.LengthUnit + ")", 90),
                    ("Short by (" + Units.LengthUnit + ")", 95),
                    ("Surface", 160), ("Over", 160), ("Status", 70),
                };

                foreach (var column in _columns) headers.Add((column.Title, 95));

                headers.Add(("Assigned", 85));
                headers.Add(("Group", 90));
                headers.Add(("Comment", 130));
                headers.Add(("At", 0));

                TableRow Line(Row r)
                {
                    var cells = new List<string?>
                    {
                        r.Name, Units.Bare(r.Clear), Units.Bare(r.Short),
                        r.Floor.Name, r.Over.Name, r.Status,
                    };

                    foreach (var column in _columns)
                        cells.Add(r.Custom.TryGetValue(column.Id, out var value) ? value : string.Empty);

                    cells.Add(r.Assigned);
                    cells.Add(r.Group);
                    cells.Add(r.LastComment);
                    cells.Add(Units.Bare(r.Span.X) + ", " + Units.Bare(r.Span.Y));

                    return new TableRow(r, cells.ToArray())
                    {
                        Tone = string.Equals(r.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(r.Status, "Approved", StringComparison.OrdinalIgnoreCase)
                            ? Tone.Good
                            : r.Clear < r.Minimum * 0.8 ? Tone.Bad : Tone.Warn,
                    };
                }

                var table = Ui.Table(shown.Select(Line).ToList(), headers.ToArray());

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Focus {n}", Focus, primary: true),
                    new Ui.OnPicked("Isolate {n}", Isolate),
                    new Ui.OnPicked("Mark {n} " + _mark, Mark),
                    new Ui.OnPicked("Assign {n}", AssignPicked),
                    new Ui.OnPicked("Comment {n}", CommentPicked),
                    new Ui.OnPicked("Group {n}", GroupPicked),
                    new Ui.OnPicked("Sphere {n}", Spheres),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "finding"))));

                if (rows.Count > most)
                    _results.Children.Add(Ui.Note(Ui.Count(rows.Count, "finding") + " match, and "
                        + "the first " + most.ToString("N0", CultureInfo.InvariantCulture)
                        + " are on screen. Tighten the filter to see the rest."));
            }

            // -------------------------------------------------------------------------------
            // The verbs
            // -------------------------------------------------------------------------------

            private static List<Row> Rows(IReadOnlyList<object> picked) => picked.OfType<Row>().ToList();

            private List<ElementKey> Keys(IEnumerable<Row> rows)
            {
                var keys = new List<ElementKey>();

                foreach (var row in rows)
                {
                    if (!row.Floor.Key.IsEmpty) keys.Add(row.Floor.Key);
                    if (!row.Over.Key.IsEmpty) keys.Add(row.Over.Key);
                }

                return keys;
            }

            private void Focus(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = Keys(rows);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Those findings name no elements this federation "
                        + "still has."));
                    return;
                }

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done("Selected and framed " + Ui.Count(keys.Count, "element") + "."));
            }

            private void Isolate(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = Keys(rows);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Those findings name no elements this federation "
                        + "still has."));
                    return;
                }

                _session.View.Isolate(keys);
                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done(Ui.Count(keys.Count, "element") + " isolated. "
                    + "\"Show all\" on the left puts the rest back."));
            }

            private void Mark(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                foreach (var row in rows) row.Status = _mark;

                Done(say, "Marked " + Ui.Count(rows.Count, "finding") + " as " + _mark + ".");
            }

            private void AssignPicked(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var who = (_assign.Text ?? string.Empty).Trim();

                foreach (var row in rows) row.Assigned = who;

                Done(say, who.Length == 0
                    ? "Cleared the assignment on " + Ui.Count(rows.Count, "finding") + "."
                    : "Assigned " + Ui.Count(rows.Count, "finding") + " to " + who + ".");
            }

            private void CommentPicked(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var body = _comment.Text.Trim();

                if (body.Length == 0)
                {
                    say.Children.Clear();
                    say.Children.Add(Ui.Problem("Type the comment on the left first."));
                    return;
                }

                var author = Environment.UserName ?? "reviewer";
                var when = DateTime.UtcNow.Ticks;

                foreach (var row in rows) row.Trail.Add(new ClashComment(author, when, body));

                Done(say, "Commented on " + Ui.Count(rows.Count, "finding") + ".");
            }

            private void GroupPicked(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var name = _group.Text.Trim();

                foreach (var row in rows) row.Group = name;

                Done(say, name.Length == 0
                    ? "Took " + Ui.Count(rows.Count, "finding") + " out of any group."
                    : "Grouped " + Ui.Count(rows.Count, "finding") + " into \"" + name + "\".");
            }

            /// <summary>
            /// A status-coloured ball at each finding, halfway up the gap.
            ///
            /// Halfway rather than at the surface or the soffit: the point of the marker is to be
            /// visible in the space the person will not fit through.
            /// </summary>
            private void Spheres(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var items = rows.Select(r => new ClashItem(
                    ClashKey.Create(r.Floor.Key, r.Over.Key, r.Span.X, r.Span.Y,
                                    r.Floor.Box.MaxZ + r.Clear / 2), "Clearance")
                {
                    X = r.Span.X,
                    Y = r.Span.Y,
                    Z = r.Floor.Box.MaxZ + r.Clear / 2,
                    Status = Enum.TryParse<FindingStatus>(r.Status, true, out var parsed)
                        ? parsed
                        : FindingStatus.New,
                }).ToList();

                var mesh = ClashMarkers.Write(items, new MarkerRecipe
                {
                    RadiusMetres = _markerAcross / 2000.0,

                    // The coordinates above are already metres.
                    MetresPerUnit = 1.0,
                });

                if (mesh.Refusal != null)
                {
                    say.Children.Add(Ui.Problem(mesh.Refusal));
                    return;
                }

                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "camelworks-markers");
                var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
                var obj = System.IO.Path.Combine(folder, "clearance-spheres-" + stamp + ".obj");
                var mtl = System.IO.Path.ChangeExtension(obj, ".mtl");

                try
                {
                    System.IO.Directory.CreateDirectory(folder);

                    System.IO.File.WriteAllText(obj, mesh.Text.Replace(
                        "mtllib " + mesh.MaterialFile,
                        "mtllib " + System.IO.Path.GetFileName(mtl)));

                    if (mesh.Materials.Length > 0) System.IO.File.WriteAllText(mtl, mesh.Materials);
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not write the markers: " + Ui.Describe(e)));
                    return;
                }

                try
                {
                    say.Children.Add(_session.Model.Document.TryAppendFile(obj)
                        ? Ui.Done(Ui.Count(rows.Count, "sphere") + " dropped in the low gaps, "
                            + "coloured by status. They are ordinary geometry: take them out "
                            + "again from the host's file list, where the file is called "
                            + System.IO.Path.GetFileName(obj) + ".")
                        : (UIElement)Ui.Problem("The host would not append the marker file."));
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not append the markers: " + Ui.Describe(e)));
                }
            }

            /// <summary>A verb's outcome, and the grid redrawn to show what it did.</summary>
            private void Done(Panel say, string message)
            {
                say.Children.Clear();
                RedrawGroups();
                Said(Ui.Done(message));
            }

            private void Said(UIElement element)
            {
                _log.Children.Clear();
                if (_headline.Length > 0) _log.Children.Add(Ui.Line(_headline, 1, true));
                _log.Children.Add(element);
            }

            // -------------------------------------------------------------------------------
            // Property columns and the grouping chain
            // -------------------------------------------------------------------------------

            private void AddColumn()
            {
                var name = _newProp.Trim();

                if (name.Length == 0)
                {
                    Said(Ui.Problem("Pick or type the property first."));
                    return;
                }

                var title = name;

                if (_columns.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    && _newTab.Trim().Length > 0)
                    title = _newTab.Trim() + " / " + name;

                if (_newSide == FromSurface) title += " (surface)";
                else if (_newSide == FromOver) title += " (over)";

                _columns.Add(new PropColumn(_newTab.Trim(), name, _newSide, title));

                RedrawColumnList();
                RedrawChainList();
                RedrawFilterScope();
                RedrawGroups();

                Said(Ui.Done("Added \"" + title + "\". Press \"Read values\" to fill it."));
            }

            private void RedrawColumnList()
            {
                _columnList.Children.Clear();

                foreach (var column in _columns)
                {
                    var here = column;

                    _columnList.Children.Add(Ui.Across(
                        Ui.Line(here.Title, 0.85),
                        Ui.Icon(Glyphs.Close, "Take the column out", () =>
                        {
                            _columns.Remove(here);
                            _chain.Remove("col:" + here.Id);
                            RedrawColumnList();
                            RedrawChainList();
                            RedrawFilterScope();
                            RedrawGroups();
                        })));
                }
            }

            private List<(string Key, string Title)> ChainOptions()
            {
                var options = new List<(string, string)>
                {
                    ("status", "Status"),
                    ("assigned", "Assigned to"),
                    ("group", "Group"),
                    ("surface", "Surface"),
                    ("over", "What is over it"),
                    ("catsurface", "Category (surface)"),
                    ("catover", "Category (over)"),
                    ("band", "How short it is"),
                    ("commented", "Commented or not"),
                };

                foreach (var column in _columns) options.Add(("col:" + column.Id, column.Title));

                return options;
            }

            private void RedrawChainList()
            {
                _chainList.Children.Clear();

                for (var i = 0; i < _chain.Count; i++)
                {
                    var at = i;
                    var key = _chain[at];

                    _chainList.Children.Add(Ui.Across(
                        Ui.Line((at + 1).ToString(CultureInfo.InvariantCulture) + ". "
                                + ChainTitle(key), 0.85),
                        Ui.Icon(Glyphs.Up, "Group by this sooner", () =>
                        {
                            if (at == 0) return;
                            _chain.RemoveAt(at);
                            _chain.Insert(at - 1, key);
                            RedrawChainList();
                            RedrawGroups();
                        }),
                        Ui.Icon(Glyphs.Down, "Group by this later", () =>
                        {
                            if (at >= _chain.Count - 1) return;
                            _chain.RemoveAt(at);
                            _chain.Insert(at + 1, key);
                            RedrawChainList();
                            RedrawGroups();
                        }),
                        Ui.Icon(Glyphs.Close, "Take it out of the chain", () =>
                        {
                            _chain.RemoveAt(at);
                            RedrawChainList();
                            RedrawGroups();
                        })));
                }

                var options = ChainOptions().Where(o => !_chain.Contains(o.Key)).ToList();

                if (options.Count == 0) return;

                var titles = options.Select(o => o.Title).ToList();

                if (!titles.Contains(_chainAdd)) _chainAdd = titles[0];

                _chainList.Children.Add(Ui.Across(
                    Ui.Choice(titles, _chainAdd, pick => _chainAdd = pick, 160),
                    Ui.Quiet("Add", () =>
                    {
                        var chosen = options.FirstOrDefault(
                            o => string.Equals(o.Title, _chainAdd, StringComparison.Ordinal));

                        if (chosen.Key == null) return;

                        _chain.Add(chosen.Key);
                        RedrawChainList();
                        RedrawGroups();
                    })));
            }

            private string ChainTitle(string key)
            {
                foreach (var option in ChainOptions())
                    if (string.Equals(option.Key, key, StringComparison.Ordinal)) return option.Title;

                return "(a removed column)";
            }

            private string GroupValue(Row row, string key)
            {
                switch (key)
                {
                    case "status": return row.Status;
                    case "assigned": return row.Assigned.Length == 0 ? "(unassigned)" : row.Assigned;
                    case "group": return row.Group.Length == 0 ? "(no group)" : row.Group;
                    case "surface": return Blankless(row.Floor.Name, "(unnamed)");
                    case "over": return Blankless(row.Over.Name, "(unnamed)");
                    case "catsurface": return Blankless(row.Floor.Category, "(no category)");
                    case "catover": return Blankless(row.Over.Category, "(no category)");
                    case "band": return Band(row);
                    case "commented": return row.Comments > 0 ? "Commented" : "No comments";
                }

                if (key.StartsWith("col:", StringComparison.Ordinal))
                {
                    if (!row.Custom.TryGetValue(key.Substring(4), out var value))
                        return "(values not read)";

                    return value.Length == 0 ? "(none)" : value;
                }

                return "(unknown)";
            }

            private static string Blankless(string? value, string absent) =>
                string.IsNullOrWhiteSpace(value) ? absent : value!;

            /// <summary>
            /// How short it is, as a band somebody argues in.
            ///
            /// Shortfall rather than clear height, because the argument is always about how much
            /// has to be found — and grouping by the raw millimetre gives every finding its own band.
            /// </summary>
            private static string Band(Row row)
            {
                var mm = row.Short * 1000;

                return mm < 50 ? "under 50 mm short"
                     : mm < 150 ? "50 - 150 mm short"
                     : mm < 300 ? "150 - 300 mm short"
                     : "over 300 mm short";
            }

            private string ChainKey(Row row) =>
                string.Join(" · ", _chain.Select(key => GroupValue(row, key)));

            private void FillColumns(Job job)
            {
                if (_columns.Count == 0)
                {
                    _fill.Children.Add(Ui.Problem("Add a column first."));
                    return;
                }

                if (_found == null)
                {
                    _fill.Children.Add(Ui.Problem("Run the check first."));
                    return;
                }

                job.Say("Finding the elements (the first look indexes the federation)...");

                var filled = 0;
                var blank = 0;
                var done = 0;

                foreach (var row in _found)
                {
                    if (!job.Step(++done, "findings read")) return;

                    var surface = row.Floor.Key.IsEmpty ? null : _session.Model.Resolve(row.Floor.Key);
                    var over = row.Over.Key.IsEmpty ? null : _session.Model.Resolve(row.Over.Key);

                    foreach (var column in _columns)
                    {
                        var tab = column.Tab.Length == 0 ? null : column.Tab;

                        var fromA = column.Side == FromOver ? null : Value(surface, tab, column.Name);
                        var fromB = column.Side == FromSurface ? null : Value(over, tab, column.Name);

                        var value = column.Side == BothSides
                            ? (fromA != null && fromB != null && fromA != fromB
                                ? fromA + " | " + fromB
                                : fromA ?? fromB)
                            : column.Side == FromSurface ? fromA : fromB;

                        row.Custom[column.Id] = value ?? string.Empty;

                        if (value == null) blank++; else filled++;
                    }
                }

                RedrawGroups();

                _fill.Children.Add(Ui.Done(Ui.Count(filled, "value") + " read into "
                    + Ui.Count(_columns.Count, "column") + "."
                    + (blank > 0
                        ? " " + Ui.Count(blank, "cell") + " came back empty - elements that do "
                          + "not carry the property."
                        : string.Empty)));
            }

            private static string? Value(IModelItem? item, string? tab, string name)
            {
                if (item == null) return null;

                try
                {
                    if (tab != null) return item.Property(tab, name);

                    return item.Property("Element", name)
                           ?? item.Property("Item", name)
                           ?? item.Property(NavWriteTransaction.TabName, name);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // -------------------------------------------------------------------------------
            // Out of the tab
            // -------------------------------------------------------------------------------

            /// <summary>The grid to a spreadsheet, exactly as it is filtered.</summary>
            private void Sheet()
            {
                if (_shown.Count == 0)
                {
                    Said(Ui.Problem("There is nothing on the grid to write."));
                    return;
                }

                var headers = new List<string>
                {
                    "Finding", "Clear (" + Units.LengthUnit + ")",
                    "Short by (" + Units.LengthUnit + ")",
                    "Surface", "Category (surface)", "Over", "Category (over)", "Status",
                };

                foreach (var column in _columns) headers.Add(column.Title);
                if (_chain.Count > 0) headers.Add("Grouped as");

                headers.AddRange(new[]
                {
                    "Assigned", "Group", "Comments", "Latest comment", "X", "Y",
                });

                var block = new TableBlock(headers.ToArray());

                foreach (var row in _shown)
                {
                    var cells = new List<string>
                    {
                        row.Name, Units.Bare(row.Clear), Units.Bare(row.Short),
                        row.Floor.Name, row.Floor.Category, row.Over.Name, row.Over.Category,
                        row.Status,
                    };

                    foreach (var column in _columns)
                        cells.Add(row.Custom.TryGetValue(column.Id, out var value) ? value : string.Empty);

                    if (_chain.Count > 0) cells.Add(ChainKey(row));

                    cells.Add(row.Assigned);
                    cells.Add(row.Group);
                    cells.Add(row.Comments.ToString(CultureInfo.InvariantCulture));
                    cells.Add(row.LastComment);
                    cells.Add(Units.Bare(row.Span.X));
                    cells.Add(Units.Bare(row.Span.Y));

                    block.Row(cells.ToArray());
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the findings to a spreadsheet",
                    Filter = "Excel|*.xlsx",
                    FileName = "clearance.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName,
                                                 XlsxWriter.Write("Clearance", block));
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clearance grid",
                                Ui.Count(_shown.Count, "row") + " to " + dialog.FileName);

                Said(Ui.Done("Wrote " + Ui.Count(_shown.Count, "row") + " to " + dialog.FileName
                             + " - the grid exactly as it is filtered."));
            }

            /// <summary>
            /// Every shown finding as a clash file.
            ///
            /// Which is what makes this check part of the week rather than a screen somebody looks
            /// at once: the Manage tab's file source opens it, a Simulate seat marks it up, and the
            /// markup comes back like any other review round.
            /// </summary>
            private void SaveFile()
            {
                if (_shown.Count == 0)
                {
                    Said(Ui.Problem("There is nothing on the grid to write."));
                    return;
                }

                var items = new List<ClashItem>();

                foreach (var row in _shown)
                {
                    var z = row.Floor.Box.MaxZ + row.Clear / 2;

                    var item = new ClashItem(
                        ClashKey.Create(row.Floor.Key, row.Over.Key, row.Span.X, row.Span.Y, z),
                        "Clearance")
                    {
                        X = row.Span.X,
                        Y = row.Span.Y,
                        Z = z,
                        Status = Enum.TryParse<FindingStatus>(row.Status, true, out var parsed)
                            ? parsed
                            : FindingStatus.New,
                        AssignedTo = row.Assigned.Length == 0 ? null : row.Assigned,
                        CarriedGroup = row.Group.Length == 0 ? null : row.Group,
                        ModelA = row.Floor.Name,
                        ModelB = row.Over.Name,
                        CategoryA = row.Floor.Category,
                        CategoryB = row.Over.Category,

                        // The gap, not an overlap: negative, the way a clearance test states it.
                        Distance = -row.Clear,
                    };

                    foreach (var comment in row.Trail) item.Comments.Add(comment);

                    items.Add(item);
                }

                var written = ClashXml.Write(items, null, new ClashXmlOptions
                {
                    SourceFile = _session.Model.Models.Count > 0
                        ? _session.Model.Models[0].DisplayName
                        : null,
                    WhenTicks = DateTime.UtcNow.Ticks,
                });

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the findings as a clash file",
                    Filter = "Clash XML|*.xml",
                    FileName = "clearance.xml",
                    AddExtension = true,
                    DefaultExt = ".xml",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllText(dialog.FileName, written.Text);
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clearance findings as a clash file",
                                written.Explain() + " to " + dialog.FileName);

                foreach (var warning in written.Warnings) _log.Children.Add(Ui.Note(warning));

                Said(Ui.Info(written.Explain() + " Open it on Manage, under \"A file\", to mark it "
                             + "up the same way as a clash export.",
                             "Open Manage", () => Ui.Told(Shell.AppWindow.Show("clash", "manage"))));
            }

            // -------------------------------------------------------------------------------

            /// <summary>The project's parties, so an assignment is picked and not spelled.</summary>
            private List<string> Assignees()
            {
                var names = new List<string>();

                foreach (var party in _session.Profile.Parties)
                    if (!names.Contains(party, StringComparer.OrdinalIgnoreCase)) names.Add(party);

                foreach (var row in _found ?? new List<Row>())
                    if (row.Assigned.Length > 0
                        && !names.Contains(row.Assigned, StringComparer.OrdinalIgnoreCase))
                        names.Add(row.Assigned);

                return names;
            }

            /// <summary>An editable combo that offers the names it knows.</summary>
            private static ComboBox Suggesting(IReadOnlyList<string> options)
            {
                var combo = new ComboBox
                {
                    IsEditable = true,
                    Width = 170,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };

                foreach (var option in options) combo.Items.Add(option);

                return combo;
            }
        }
    }
}
