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
using CamelWorks.Core.Issues;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Measurement;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Clearance Check: the clear height over a walking surface, worked like clash results.
    ///
    /// A duct 1.9 m over a corridor intersects nothing. There is air between it and the floor, so no
    /// clash test at any tolerance reports it, and the first anybody hears of it is on site.
    ///
    /// It looks like the Manage tab because what comes out of it is a list of places to argue about,
    /// assign and close, which is a clash list in everything but how it was found. So it gets the
    /// same bench: sort on any column, filter on any column, an ordered grouping chain,
    /// element-property columns, bulk status, assignment, comments and grouping on the selected
    /// rows, focus, isolate and marker spheres in the model, the grid to a spreadsheet, and the
    /// whole finding list out as a clash file that the Manage tab's file source opens for the same
    /// review round.
    ///
    /// Both inputs are model-fed. Each side offers every search set and selection set the document
    /// holds, the current selection, or everything loaded; nothing is typed. The one guess it offers
    /// is labelled as a guess and says which words it matches.
    /// </summary>
    public static class ClearanceViews
    {
        /// <summary>The guess, named as one — and the only option that reads a category.</summary>
        internal const string Guessed = "Walking surfaces (a guess)";

        internal const string Selected = "Current selection";
        internal const string Loaded = "Everything loaded";
        internal const string NotTheSurfaces = "Everything except the surfaces";

        /// <summary>
        /// Surfaces found in the triangles rather than named by hand.
        ///
        /// The token. What the option says on screen is <see cref="DetectedSaid"/>, and the caveat
        /// is in that sentence rather than in a tooltip: the detector can tell an up-facing surface
        /// from a soffit and a large one from a small one, and it cannot tell a floor from a desk.
        /// </summary>
        internal const string Detected = "Detected walkable surfaces";

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

        // What detection was asked for, what it found, and what somebody threw out of it. Held
        // between visits like the findings are: a federation sweep is a minute of traversal.
        private static readonly WalkableOptions _detection = new WalkableOptions();
        private static List<WalkablePatch>? _patches;
        private static WalkableDiagnostics? _diagnostics;

        /// <summary>
        /// Surfaces the user dropped, by patch id.
        ///
        /// In memory for now. A patch id is derived from where the surface is, so it survives a
        /// re-run of the same build; carrying the exclusions in the run's own saved settings waits
        /// for the run record.
        /// </summary>
        private static readonly HashSet<string> _dropped = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The option's whole sentence, caveat included.
        ///
        /// The caveat is in the option rather than in a tooltip or a note under it. An option that
        /// says only "detected walkable surfaces" reads as though the software found the floors.
        /// </summary>
        /// <param name="units">The seat's units, so the area reads in them.</param>
        /// <param name="minimumAreaSquareMetres">The smallest patch detection will report.</param>
        internal static string DetectedSaid(Measure units, double minimumAreaSquareMetres) =>
            Detected + " — every up-facing surface over " + units.Area(minimumAreaSquareMetres)
            + ", which includes desk tops and duct tops.";

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Check(Session session) => new Screen(session).Build();

        static ClearanceViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget the last check, so the next visit starts clean.
        ///
        /// Called when the document changes as well as by hand: findings about the federation that
        /// was open are not findings about the one that is.
        /// </summary>
        internal static void Forget()
        {
            _found = null;
            _headline = string.Empty;

            _columns.Clear();
            _chain.Clear();

            _patches = null;
            _diagnostics = null;
            _dropped.Clear();
        }

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

            internal int Of(FindingStatus status) => Rows.Count(r => r.Status == status);
        }

        /// <summary>
        /// One place the clear height is not there — and everything a person then does to it.
        ///
        /// The judgement half (status, assignment, comments, grouping) is the manager's own,
        /// because the host has no clash data to write it into - no clash engine found this. It
        /// is kept on the finding beside the model, so it survives a re-run and closing
        /// Navisworks, and it travels into the spreadsheet and the clash file with the rest.
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

            internal FindingStatus Status { get; set; } = FindingStatus.New;
            internal string Assigned { get; set; } = string.Empty;
            internal string Group { get; set; } = string.Empty;

            internal List<ClashComment> Trail { get; } = new List<ClashComment>();

            internal Dictionary<string, string> Custom { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            internal int Comments => Trail.Count;

            internal string LastComment =>
                Trail.Count == 0 ? string.Empty : Trail[Trail.Count - 1].Body;

            /// <summary>
            /// The finding this row is a view of.
            ///
            /// Where the judgement actually lives. The row is what a table can draw; the finding
            /// is what survives closing Navisworks, and what an issue attaches to.
            /// </summary>
            internal Finding? Found { get; set; }
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
            private readonly StackPanel _strip = Ui.Stack();
            private readonly StackPanel _detectLog = Ui.Stack();
            private readonly StackPanel _detectList = Ui.Stack();

            private readonly TextBox _minimum;
            private readonly TextBox _filter;
            private readonly TextBox _comment;
            private readonly TextBox _group;
            private readonly ComboBox _assign;

            private readonly HashSet<FindingStatus> _show =
                new HashSet<FindingStatus>(FindingStatusWords.All);

            private readonly HashSet<string> _pickedGroups = new HashSet<string>(StringComparer.Ordinal);

            private FindingStatus _mark = FindingStatus.Reviewed;
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

                var stage = Ui.Stack(_strip, _intro, _log, _groups, _results);

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

                RedrawStrip();

                return Bench.Surface(null, Foot(run), stage);
            }

            /// <summary>
            /// The filters somebody changes every few minutes, above the findings.
            ///
            /// The same law as the clash bench: what is touched constantly stays on the surface,
            /// and everything decided once for the job is a window away. A column of every option
            /// beside the table it serves is a column nobody reads and a table with half a window.
            /// </summary>
            private void RedrawStrip()
            {
                _strip.Children.Clear();

                var row = new WrapPanel();

                row.Children.Add(Ui.Icon(Glyphs.Find, "Search the findings", () => _filter.Focus()));
                // Loose first: these two are kept in fields so what was typed and chosen
                // survives a redraw, and a kept control still belongs to the row thrown away by
                // the Clear above. The second draw used to throw from a window's Closed handler,
                // which is to say it closed Navisworks.
                row.Children.Add(Ui.Loose(_filter));
                row.Children.Add(new Border { Width = 8 });
                row.Children.Add(Ui.Loose(_filterScope));
                row.Children.Add(new Border { Width = 14 });

                foreach (var status in FindingStatusWords.All)
                {
                    var here = status;

                    row.Children.Add(new Border { Width = 10 });
                    row.Children.Add(Ui.Check(
                        FindingStatusWords.Of(here), _show.Contains(here), on =>
                    {
                        if (on) _show.Add(here); else _show.Remove(here);
                        RedrawGroups();
                    }));
                }

                RedrawFilterScope();

                _strip.Children.Add(row);
            }

            /// <summary>The action row: check, then the windows the rest of it lives in.</summary>
            private UIElement Foot(UIElement run)
            {
                var row = new WrapPanel();

                row.Children.Add(run);
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Icon(Glyphs.Ruler, "What to measure", () => Configure("measure")));
                row.Children.Add(Ui.Icon(Glyphs.Filter, "Filters", () => Configure("filters")));
                row.Children.Add(Ui.Icon(Glyphs.Columns, "Columns and grouping", () => Configure("columns")));
                row.Children.Add(Ui.Icon(Glyphs.Person, "Status and assignment", () => Configure("verbs")));
                row.Children.Add(Ui.Icon(Glyphs.Sphere, "Markers and the view", () => Configure("model")));
                row.Children.Add(Ui.Icon(Glyphs.Onward, "Excel and the clash file", () => Configure("out")));

                return row;
            }

            // -------------------------------------------------------------------------------
            // The options panel
            // -------------------------------------------------------------------------------

            /// <summary>
            /// What one symbol on the action row is for, in a window of its own.
            ///
            /// Titled with the section the symbol named, and holding that section only. All six
            /// used to open the same six-section window, which answered "what did I just press"
            /// with the same screen six times.
            /// </summary>
            /// <param name="section">Which section the symbol names.</param>
            private void Configure(string section)
            {
                Ui.Page page;

                switch (section)
                {
                    case "filters": page = new Ui.Page("Filters", FiltersPage); break;
                    case "columns": page = new Ui.Page("Columns and grouping", ColumnsPage); break;
                    case "verbs": page = new Ui.Page("Status and assignment", VerbsPage); break;
                    case "model": page = new Ui.Page("In the model", ModelPage); break;
                    case "out": page = new Ui.Page("Out of the tab", OutPage); break;
                    default: page = new Ui.Page("What to measure", MeasurePage); break;
                }

                Ui.Configure(page.Name, () =>
                {
                    RedrawStrip();
                    Redraw();
                }, page);
            }

            private UIElement MeasurePage()
            {
                var sets = Sets();
                var said = DetectedSaid(Units, _detection.MinPatchAreaSquareMetres);

                var from = new List<string> { said, Guessed, Selected, Loaded };
                from.AddRange(sets);

                var to = new List<string> { NotTheSurfaces, Selected, Loaded };
                to.AddRange(sets);

                var chosen = _surfaces == Detected ? said : _surfaces;

                if (!from.Contains(chosen, StringComparer.OrdinalIgnoreCase)) from.Add(chosen);
                if (!to.Contains(_targets, StringComparer.OrdinalIgnoreCase)) to.Add(_targets);

                RedrawDetected();

                return Ui.Stack(
                    Bench.Group("What to measure",
                        Ui.Field("Clear height", _minimum, "the minimum, in " + Units.LengthUnit),
                        Ui.Field("Measure up from",
                                 Ui.Choice(from, chosen, pick =>
                                 {
                                     _surfaces = pick == said ? Detected : pick;
                                     RedrawDetected();
                                 }, 420),
                                 "the walking surface"),
                        Ui.Field("Measure up to", Ui.Choice(to, _targets, pick => _targets = pick, 210),
                                 "what might be too low over it"),
                        Ui.Small("Apart from the first two, both lists are this model's own search "
                                 + "sets and selection sets. \"" + Guessed + "\" reads names: it "
                                 + "takes anything whose category or type contains "
                                 + string.Join(", ", WalkingWords)
                                 + ". Name a set instead and nothing is guessed at all."),
                        sets.Count == 0
                            ? Ui.Note("This document holds no saved sets yet. Build one in Set Builder "
                                      + "and it appears in both lists.")
                            : null),
                    Bench.Group(Detected,
                        Ui.Check("Prefer elements whose category names a floor",
                                 _detection.PreferFloorCategories, on =>
                                 {
                                     _detection.PreferFloorCategories = on;
                                     Stale();
                                 }),
                        Ui.Check("Leave stairs out — detection makes one surface per tread, and "
                                 + "the flight above then reads as low headroom over the treads "
                                 + "below it", _detection.ExcludeStairs, on =>
                        {
                            _detection.ExcludeStairs = on;
                            Stale();
                        }),
                        Ui.Runner("Find the surfaces", _detectLog, primary: false, work: FindSurfaces),
                        _detectLog,
                        _detectList,
                        Ui.Small("Detection reads triangles: it keeps every up-facing surface over "
                                 + Units.Area(_detection.MinPatchAreaSquareMetres)
                                 + ", which is a floor, and is also a desk top, a worktop, a window "
                                 + "sill and the top of a wide duct. Read the list before running "
                                 + "the check and drop what is not a floor."),
                        Ui.Small("Both ticks are off to start with: the geometric answer is the "
                                 + "default, and a stair with a soffit too low over it is still "
                                 + "worth reporting.")));
            }

            /// <summary>What was detected no longer answers what is being asked for.</summary>
            private void Stale()
            {
                _patches = null;
                _diagnostics = null;

                _detectLog.Children.Clear();

                RedrawDetected();
            }

            // -------------------------------------------------------------------------------
            // Detection
            // -------------------------------------------------------------------------------

            /// <summary>Run detection over everything loaded.</summary>
            /// <param name="job">The job, for progress and a Stop.</param>
            /// <param name="say">Where the counts and the caveats go, or null to keep them.</param>
            private IReadOnlyList<WalkablePatch> Detect(Job job, Panel? say)
            {
                var diagnostics = new WalkableDiagnostics();

                var found = WalkableSurfaces.Read(_session.Model, _session.Triangles,
                                                  TraversalScope.WholeDocument, _session.MetresPerUnit,
                                                  _detection, diagnostics,
                                                  read => job.Step(read, "elements read"));

                _patches = found.ToList();
                _diagnostics = diagnostics;

                if (say != null) Report(say);

                return found;
            }

            /// <summary>
            /// What detection found and what it could not do.
            ///
            /// Stated before the check is run, not after it. A federation that answers 412 surfaces
            /// and 8,940 m² on two storeys of offices is one where desk tops and duct tops are in
            /// the list, and the number is the only thing that says so.
            /// </summary>
            /// <param name="say">Where the lines go.</param>
            private void Report(Panel say)
            {
                if (_diagnostics == null || _patches == null) return;

                var area = 0.0;
                foreach (var patch in _patches) area += patch.AreaSquareMetres;

                say.Children.Add(Ui.Line(Ui.Count(_patches.Count, "surface") + ", " + Units.Area(area)
                    + ", " + Ui.Count(_diagnostics.ElementsRefused, "element") + " refused", 1, true));

                if (_diagnostics.UpAxis != 2)
                    say.Children.Add(Ui.Problem("Most of this model's flat area faces "
                        + (_diagnostics.UpAxis == 0 ? "X" : "Y") + ", not Z. The clear height is "
                        + "measured up the Z axis, so what comes back will be wrong."));

                foreach (var note in _diagnostics.Notes) say.Children.Add(Ui.Small(note));
            }

            /// <summary>Detect from the set-up window, and redraw the list underneath it.</summary>
            private void FindSurfaces(Job job)
            {
                Detect(job, null);

                RedrawDetected();
            }

            /// <summary>What detection found, less what was dropped from it.</summary>
            private static List<WalkablePatch> Kept() =>
                (_patches ?? new List<WalkablePatch>())
                    .Where(patch => !_dropped.Contains(patch.Id))
                    .ToList();

            /// <summary>How many rows of the list are drawn at once.</summary>
            private const int Shown = 500;

            /// <summary>
            /// The detected surfaces, largest first, with the ones somebody dropped marked.
            ///
            /// Largest first because the list is read to find what is obviously not a floor, and a
            /// 40 m² slab and a 0.4 m² sill are told apart by their size before anything else.
            /// </summary>
            private void RedrawDetected()
            {
                _detectList.Children.Clear();

                if (_patches == null)
                {
                    _detectList.Children.Add(Ui.Small("Nothing detected yet."));
                    return;
                }

                Report(_detectList);

                if (_patches.Count == 0)
                {
                    _detectList.Children.Add(Ui.Empty("No surfaces were found.",
                        "Nothing loaded has an up-facing surface over "
                        + Units.Area(_detection.MinPatchAreaSquareMetres)
                        + ". If this seat will not give triangles, use a set or the guess instead."));
                    return;
                }

                var ordered = _patches.OrderByDescending(patch => patch.AreaSquareMetres)
                                      .Take(Shown)
                                      .ToList();

                var rows = ordered.Select(patch => new TableRow(patch,
                    patch.Level ?? "—",
                    Units.Area(patch.AreaSquareMetres),
                    Units.Length(patch.TopMetres),
                    _dropped.Contains(patch.Id) ? "dropped" : string.Empty)
                {
                    Tone = _dropped.Contains(patch.Id) ? Tone.Warn : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows, ("Level", 0), ("Area", 90), ("Top", 90), ("In the check", 90));

                _detectList.Children.Add(table);

                _detectList.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Drop these {n}", (picked, said) => Drop(picked, said, dropping: true)),
                    new Ui.OnPicked("Put these {n} back", (picked, said) => Drop(picked, said, dropping: false))));

                _detectList.Children.Add(Ui.Small(
                    Ui.Count(Kept().Count, "surface") + " in the check"
                    + (_dropped.Count == 0 ? string.Empty : ", " + _dropped.Count + " dropped")
                    + (_patches.Count > Shown
                        ? ". The largest " + Shown + " are listed."
                        : string.Empty)));
            }

            /// <summary>Drop the picked surfaces from the check, or put them back.</summary>
            /// <param name="picked">The rows picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            /// <param name="dropping">Whether they are dropped.</param>
            private void Drop(IReadOnlyList<object> picked, Panel said, bool dropping)
            {
                var touched = 0;

                foreach (var one in picked)
                {
                    if (!(one is WalkablePatch patch)) continue;

                    if (dropping ? _dropped.Add(patch.Id) : _dropped.Remove(patch.Id)) touched++;
                }

                RedrawDetected();

                said.Children.Add(Ui.Done(Ui.Count(touched, "surface")
                    + (dropping ? " dropped from the check." : " back in the check.")));
            }

            private UIElement FiltersPage() => Bench.Group("Narrow the findings",
                Ui.Check("Only ones with comments", _onlyCommented, on =>
                {
                    _onlyCommented = on;
                    RedrawGroups();
                }),
                Ui.Small("The status filters and the search are on the strip above the findings "
                         + "because they are changed frequently."));

            private UIElement ColumnsPage()
            {
                RedrawColumnList();
                RedrawChainList();

                return Ui.Stack(
                    Bench.Group("Property columns",
                        Ui.Field("Tab", Pickers.Tab(_newTab, pick => _newTab = pick)),
                        Ui.Field("Property", Pickers.Property(_newProp, pick => _newProp = pick)),
                        Ui.Field("Read from", Ui.Choice(new[] { BothSides, FromSurface, FromOver },
                                                        _newSide, pick => _newSide = pick, 150)),
                        Ui.Actions(Ui.Icon(Glyphs.Add, "Add the column", AddColumn)),
                        _columnList,
                        Ui.Runner("Read values", _fill, primary: false, work: FillColumns),
                        _fill,
                        Ui.Small("Each column reads that property off the elements themselves - the "
                                 + "level, the system, the room - and the grouping below chains "
                                 + "over it.")),
                    Bench.Group("Group by, in order",
                        _chainList,
                        Ui.Small("Chained: the first entry makes the bands, the next splits each "
                                 + "band, and so on. Pick group rows to work just those findings.")));
            }

            private UIElement VerbsPage() => Bench.Group("What the symbols under the findings write",
                Ui.Field("Mark as", Ui.Choice(
                    FindingStatusWords.Words, FindingStatusWords.Of(_mark), pick =>
                {
                    _mark = FindingStatusWords.Read(pick);
                    RedrawResults();
                }, 150)),
                Ui.Field("Assign to", _assign, "the project's parties, or type a name"),
                Ui.Field("Comment", _comment),
                Ui.Field("Group into", _group, "blank moves them out of any group"));

            private UIElement ModelPage() => Bench.Group("Markers and the view",
                Ui.Field("Marker size", Ui.Text(
                    _markerAcross.ToString(CultureInfo.InvariantCulture), text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var mm) && mm > 0)
                            _markerAcross = mm;
                    }, 90), "millimetres across"),
                Ui.Actions(
                    Ui.Quiet("Hide or show the markers", Unmark),
                    Ui.Quiet("Show all", () =>
                    {
                        _session.View.ShowAll();
                        Said(Ui.Done("Everything is visible again."));
                    })),
                Ui.Small("A marker is a status-coloured ball drawn over the model, halfway up the "
                         + "gap that is too low. It is not geometry: nothing is added to the "
                         + "document, and hiding them takes them straight back down - pressing "
                         + "it again puts the same ones back."));

            private UIElement OutPage() => Bench.Group("Out of the tab",
                Ui.Actions(
                    Ui.Quiet("Excel...", Sheet),
                    Ui.Quiet("Clash file...", SaveFile)),
                Ui.Small("The clash file carries every finding with its status, assignment, "
                         + "comments and group - open it on the Manage tab's file source and "
                         + "it works the same review round as a clash export."));

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

                var detecting = _surfaces == Detected;

                // Detection does its own walk, so pressing Check without opening the set-up window
                // first still measures something rather than refusing.
                if (detecting && _patches == null) Detect(job, _log);

                var patches = detecting ? Kept() : new List<WalkablePatch>();

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

                var surfaces = detecting
                    ? patches.Select(Standing).ToList()
                    : Gather(_surfaces, whole, null, job);

                if (surfaces == null) return;

                // What "everything except the surfaces" excludes. A detected surface is part of an
                // element, so it is that element that comes off the other side.
                var already = detecting
                    ? new HashSet<string>(patches.Select(patch => patch.Source.ToString()),
                                          StringComparer.Ordinal)
                    : new HashSet<string>(surfaces.Select(p => p.Id), StringComparer.Ordinal);

                var above = Gather(_targets, whole, already, job);
                if (above == null) return;

                if (surfaces.Count == 0)
                {
                    _found = null;
                    _headline = string.Empty;
                    _log.Children.Add(Ui.Empty("No surfaces to measure from.",
                        detecting
                            ? "Detection found no up-facing surface over "
                              + Units.Area(_detection.MinPatchAreaSquareMetres)
                              + ", or every one it found was dropped."
                            : _surfaces == Guessed
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

                // Over a detected surface the volume swept is the patch's own outline extruded,
                // not its bounding rectangle: the rectangle over an L-shaped plate or a ring
                // corridor covers ground that is not floor, and everything in it would be a
                // finding.
                var found = detecting
                    ? Headroom.Check(patches, above.Select(p => p.Box), minimum)
                    : Headroom.Check(surfaces.Select(p => p.Box), above.Select(p => p.Box), minimum);

                var rows = new List<Row>();

                foreach (var span in found.Spans)
                {
                    if (!byId.TryGetValue(span.Floor.Id, out var floor)) continue;
                    if (!byId.TryGetValue(span.Obstruction.Id, out var over)) continue;

                    var row = new Row(span, floor, over, minimum)
                    {
                        Name = "Clearance " + (rows.Count + 1).ToString(CultureInfo.InvariantCulture),
                    };

                    rows.Add(row);
                }

                Keep(rows, minimum, job);

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

                // These findings carry focus, isolate, marker spheres and snapshots, every one of
                // which resolves element keys. Paid here, with a progress bar and a Stop, rather
                // than inside whichever button is pressed first.
                Ui.Ready(job, Keys(rows));

            }

            private static bool NeedsWhole(string choice) =>
                choice == Guessed || choice == Loaded || choice == NotTheSurfaces;

            /// <summary>
            /// A detected surface as the rest of the tab sees an element.
            ///
            /// The box is the patch's footprint with its top at the patch's own highest point, and
            /// the key is the element the surface came off, so focus, isolate, markers and the
            /// findings register all still name something in the model.
            /// </summary>
            private Piece Standing(WalkablePatch patch)
            {
                var box = new HeadroomElement(patch.Id, Surface(patch))
                {
                    MinX = patch.Footprint.MinX, MaxX = patch.Footprint.MaxX,
                    MinY = patch.Footprint.MinY, MaxY = patch.Footprint.MaxY,
                    MinZ = patch.Footprint.MinZ, MaxZ = patch.TopMetres,
                };

                return new Piece(patch.Source, patch.Id, Surface(patch), patch.Level, true, box);
            }

            /// <summary>How a detected surface reads on the board.</summary>
            private string Surface(WalkablePatch patch) =>
                (patch.Level == null ? "Surface" : patch.Level + " surface") + " "
                + Units.Area(patch.AreaSquareMetres);

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

            /// <summary>
            /// Put what this run measured into the findings register, and lay back what is known.
            ///
            /// The judgement previously lived on the row and was carried between runs in memory:
            /// close a breach, close Navisworks, and it was open again the next day. It is now
            /// stored beside the model.
            ///
            /// Every clear-height finding is in scope, because the check always measures the whole
            /// of what it was pointed at: one this run did not report is no longer too low, and it
            /// is marked absent rather than deleted so the record of the fix survives.
            /// </summary>
            private void Keep(IReadOnlyList<Row> rows, double minimum, Job job)
            {
                var found = new List<Finding>();

                foreach (var row in rows)
                {
                    var finding = ClearanceFindings.From(row.Span, row.Floor.Key, row.Over.Key,
                                                         minimum, row.Name);

                    found.Add(finding);
                    row.Found = finding;
                }

                job.Say("Keeping what was found...");

                var register = _session.Findings.Findings;

                register.Observe(FindingSource.Headroom, found, DateTime.UtcNow.Ticks,
                                 origin: _session.Store.Path);

                // Observe merges a repeat into the record already held, so the row must be pointed
                // at what the register kept rather than at the copy this run made of it.
                foreach (var row in rows)
                {
                    row.Found = register.Find(row.Found?.Id) ?? row.Found;
                    Lay(row);
                }

                if (!_session.Findings.Save())
                    _log.Children.Add(Ui.Problem(
                        "These findings are on the board but could not be kept: "
                        + (_session.Findings.LastSaveProblem ?? "there was nowhere to write.")));
            }

            /// <summary>
            /// Lay the finding over the row, so the board reads the way somebody left it.
            ///
            /// Without this, re-running after a model update reopens everything anybody had
            /// closed, which is the one behaviour that makes a check nobody re-runs.
            /// </summary>
            private static void Lay(Row row)
            {
                var finding = row.Found;

                if (finding == null) return;

                row.Status = finding.Status;
                row.Assigned = finding.AssignedTo ?? string.Empty;
                row.Group = finding.Group ?? string.Empty;

                row.Trail.Clear();

                foreach (var comment in finding.Comments)
                    row.Trail.Add(new ClashComment(comment.Author, comment.WhenTicks, comment.Body));

                if (_custom.TryGetValue(finding.Id, out var values))
                    foreach (var pair in values) row.Custom[pair.Key] = pair.Value;
            }

            /// <summary>
            /// The custom column values from the last run, by finding.
            ///
            /// Not on the finding: a column is somebody's bench, not a fact about the building,
            /// and a property read costs a host round trip each. Carried in memory only, which is
            /// all a bench needs.
            /// </summary>
            private static readonly Dictionary<string, Dictionary<string, string>> _custom =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

            /// <summary>Write the findings out. Nowhere to write is answered, never thrown.</summary>
            private void KeepFindings(Panel? say = null)
            {
                if (_session.Findings.Save()) return;

                say?.Children.Add(Ui.Problem("That is on the board but could not be kept: "
                    + (_session.Findings.LastSaveProblem ?? "there was nowhere to write.")));
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
                        case "Status": return Has(FindingStatusWords.Of(row.Status));
                        case "Assigned": return Has(row.Assigned);
                        case "Group": return Has(row.Group);
                        case "Comment": return Has(row.LastComment);
                        default: return false;
                    }
                }

                if (Has(row.Name) || Has(row.Floor.Name) || Has(row.Over.Name)
                    || Has(row.Floor.Category) || Has(row.Over.Category)
                    || Has(FindingStatusWords.Of(row.Status)) || Has(row.Assigned) || Has(row.Group)
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
                    Skin.Num(b.Of(FindingStatus.New)),
                    Skin.Num(b.Of(FindingStatus.Active)),
                    Skin.Num(b.Of(FindingStatus.Reviewed)),
                    Skin.Num(b.Of(FindingStatus.Approved)),
                    Skin.Num(b.Of(FindingStatus.Resolved)))
                {
                    Tone = b.Of(FindingStatus.New) > 0 ? Tone.Warn : Tone.Plain,
                }).ToList(),
                    ("Group", 280), ("Findings", 70), ("Lowest", 70),
                    (FindingStatusWords.Of(FindingStatus.New), 55),
                    (FindingStatusWords.Of(FindingStatus.Active), 55),
                    (FindingStatusWords.Of(FindingStatus.Reviewed), 70),
                    (FindingStatusWords.Of(FindingStatus.Approved), 70),
                    (FindingStatusWords.Of(FindingStatus.Resolved), 0));

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
                        r.Floor.Name, r.Over.Name, FindingStatusWords.Of(r.Status),
                    };

                    foreach (var column in _columns)
                        cells.Add(r.Custom.TryGetValue(column.Id, out var value) ? value : string.Empty);

                    cells.Add(r.Assigned);
                    cells.Add(r.Group);
                    cells.Add(r.LastComment);
                    cells.Add(Units.Bare(r.Span.X) + ", " + Units.Bare(r.Span.Y));

                    return new TableRow(r, cells.ToArray())
                    {
                        Tone = FindingStatusLattice.IsClosed(r.Status)
                            ? Tone.Good
                            : r.Clear < r.Minimum * 0.8 ? Tone.Bad : Tone.Warn,
                    };
                }

                var table = Ui.Table(shown.Select(Line).ToList(), headers.ToArray())
                    .Menu(new Ui.OnMenu("Create an issue from {n}", picked => RaiseIssue(picked, false)),
                          new Ui.OnMenu("An issue for each of {n}", picked => RaiseIssue(picked, true)),
                          new Ui.OnMenu("Attach {n} to an issue...", picked => AttachToIssue(picked)));

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Focus {n}", Focus, primary: true),
                    new Ui.OnPicked("Isolate {n}", Isolate),
                    new Ui.OnPicked("Mark {n} " + FindingStatusWords.Of(_mark), Mark),
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

                // Selected once, then framed on what was selected. ZoomTo takes keys and turns
                // every one of them back into the element it names - a second resolution of the
                // same handful, and on a key that is not an instance GUID that means indexing the
                // federation inside the press.
                _session.Model.Select(keys);
                _session.View.ZoomToSelection(1.0);
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

                // One resolution, then two verbs on the selection it made. This resolved the same
                // keys three times over - once to isolate, once to select, once to frame.
                _session.Model.Select(keys);
                _session.View.IsolateSelection();
                _session.View.ZoomToSelection(1.0);
            }

            private void Mark(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                foreach (var row in rows)
                {
                    row.Status = _mark;

                    if (row.Found != null) row.Found.Status = _mark;
                }

                KeepFindings(say);
                Done(say, "Marked " + Ui.Count(rows.Count, "finding") + " as "
                          + FindingStatusWords.Of(_mark) + ".");
            }

            private void AssignPicked(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var who = (_assign.Text ?? string.Empty).Trim();

                foreach (var row in rows)
                {
                    row.Assigned = who;

                    if (row.Found != null) row.Found.AssignedTo = who.Length == 0 ? null : who;
                }

                KeepFindings(say);
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

                foreach (var row in rows)
                {
                    row.Trail.Add(new ClashComment(author, when, body));
                    row.Found?.Comments.Add(new FindingComment(author, when, body));
                }

                KeepFindings(say);
                Done(say, "Commented on " + Ui.Count(rows.Count, "finding") + ".");
            }

            private void GroupPicked(IReadOnlyList<object> picked, Panel say)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var name = _group.Text.Trim();

                foreach (var row in rows)
                {
                    row.Group = name;

                    if (row.Found != null) row.Found.Group = name.Length == 0 ? null : name;
                }

                KeepFindings(say);
                Done(say, name.Length == 0
                    ? "Took " + Ui.Count(rows.Count, "finding") + " out of any group."
                    : "Grouped " + Ui.Count(rows.Count, "finding") + " into \"" + name + "\".");
            }

            /// <summary>
            /// A status-coloured ball at each finding, halfway up the gap.
            ///
            /// Halfway rather than at the surface or the soffit, so the marker is visible in the
            /// space that is too low to pass through.
            ///
            /// Temporary graphics rather than geometry. They are drawn over the model each frame
            /// from a set handed to the view, so they appear and clear at once: nothing is added to
            /// the document, nothing joins the selection tree, and nothing needs unloading.
            /// </summary>
            /// <param name="picked">The picked rows.</param>
            /// <param name="say">Where to report what happened.</param>
            private void Spheres(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                if (!_session.View.CanMark)
                {
                    say.Children.Add(Ui.Problem("This host has no view to draw markers into."));
                    return;
                }

                // The clearance engine works in metres and the host's graphics calls take the
                // document's own units, so every coordinate and the radius cross over here.
                var metres = _session.MetresPerUnit;
                var perUnit = metres > 0 ? metres : 1.0;
                var radius = _markerAcross / 2000.0 / perUnit;

                if (radius <= 0)
                {
                    say.Children.Add(Ui.Problem("A marker radius of zero produces nothing visible. "
                        + "Set a size on the Model page."));
                    return;
                }

                var marks = rows.Select(r => new SceneMarker(
                        r.Span.X / perUnit,
                        r.Span.Y / perUnit,
                        (r.Floor.Box.MaxZ + r.Clear / 2) / perUnit,
                        radius,
                        ClashMarkers.Colour(r.Status)))
                    .ToList();

                _session.View.ShowMarkers(marks);

                say.Children.Add(Ui.Done(Ui.Count(marks.Count, "sphere") + " hanging in the low "
                    + (marks.Count == 1 ? "gap" : "gaps") + ", coloured by status. They are drawn "
                    + "over the model, not added to it - \"Clear the markers\" takes them down."));
            }

            /// <summary>Hide the markers, or put back the ones that were hidden.</summary>
            private void Unmark()
            {
                if (_session.View.HideMarkers() == 0 && _session.View.CanUnhideMarkers)
                    _session.View.UnhideMarkers();
            }

            /// <summary>
            /// Put the picked findings onto an issue that already exists.
            ///
            /// The other half of raising one. A corridor that is too low for its whole length is
            /// one conversation, and it is often the same conversation as the clashes in it - so
            /// the findings join the issue that is already about it rather than starting a
            /// fifteenth.
            /// </summary>
            /// <param name="picked">The picked findings.</param>
            private void AttachToIssue(IReadOnlyList<object> picked)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var findings = rows.Select(r => r.Found).Where(f => f != null).Select(f => f!)
                                   .ToList();

                if (findings.Count == 0)
                {
                    Said(Ui.Problem("This run kept no findings, so there is nothing to attach. "
                        + "Run the check again and try once more."));

                    return;
                }

                var register = IssueViews.Read(_session);

                var open = register.Issues
                    .OrderBy(i => !i.IsOpen)
                    .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (open.Count == 0)
                {
                    Said(Ui.Problem("There are no issues yet. \"Create an issue from N\" makes "
                        + "one from what is picked."));

                    return;
                }

                _log.Children.Clear();
                _log.Children.Add(Ui.Eyebrow("Attach " + Ui.Count(findings.Count, "finding")
                                             + " to an issue"));

                var table = Ui.Table(
                    open.Select(i => new TableRow(i, i.Id, i.Title,
                                                  FindingStatusWords.Of(i.Status),
                                                  i.AssignedTo ?? string.Empty,
                                                  Ui.Count(i.Findings.Count, "finding"))),
                    ("Issue", 70), ("Title", 240), ("Status", 90), ("Assigned", 110),
                    ("Already on it", 0));

                _log.Children.Add(table);

                _log.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Attach to {n}", (chosen, told) =>
                    {
                        told.Children.Clear();

                        var issues = chosen.OfType<Issue>().ToList();

                        if (issues.Count == 0) return;

                        var added = 0;
                        var now = DateTime.UtcNow.Ticks;

                        foreach (var issue in issues)
                        {
                            var joined = IssueFindings.Attach(issue, findings);

                            if (joined == 0) continue;

                            added += joined;
                            issue.ModifiedTicks = now;
                        }

                        var problem = IssueViews.Write(_session, register);

                        if (problem != null)
                        {
                            told.Children.Add(Ui.Problem(problem));
                            return;
                        }

                        told.Children.Add(added == 0
                            ? Ui.Done("Every one of them was already on "
                                      + (issues.Count == 1 ? "that issue." : "those issues."))
                            : Ui.Done(Ui.Count(added, "finding") + " attached to "
                                      + (issues.Count == 1
                                          ? issues[0].Id + "."
                                          : Ui.Count(issues.Count, "issue") + ".")));
                    })));
            }

            /// <summary>
            /// Make issues out of picked findings.
            ///
            /// The clearance check finds places, not conversations. What gets chased is "the
            /// corridor on level three is 1.85 where it has to be 2.1", which is one issue over a
            /// run of findings - so the default is one issue across everything picked, and one
            /// each is there for when they really are separate.
            /// </summary>
            /// <param name="picked">The picked findings.</param>
            /// <param name="each">One issue per finding, rather than one over all of them.</param>
            private void RaiseIssue(IReadOnlyList<object> picked, bool each)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                Issue? Make(string id, IReadOnlyList<Row> from)
                {
                    if (from.Count == 0) return null;

                    var first = from[0];
                    var worst = from.OrderBy(r => r.Clear).First();

                    var title = from.Count == 1
                        ? "Clear height " + Units.Length(first.Clear) + " under " + first.Over.Name
                        : Ui.Count(from.Count, "place") + " under " + Units.Length(first.Minimum)
                          + ", worst " + Units.Length(worst.Clear);

                    var issue = new Issue(id, title, "Clearance")
                    {
                        Type = "Clearance",
                        Description = "Asked for " + Units.Length(first.Minimum) + "."
                            + Environment.NewLine
                            + string.Join(Environment.NewLine, from.Take(20).Select(r =>
                                Units.Length(r.Clear) + " under " + r.Over.Name
                                + " over " + r.Floor.Name)),
                        HasPoint = true,

                        // The findings are already metres, and the point is the middle of the gap
                        // - which is the space a person does not fit through.
                        X = from.Average(r => r.Span.X),
                        Y = from.Average(r => r.Span.Y),
                        Z = from.Average(r => r.Floor.Box.MaxZ + r.Clear / 2),
                    };

                    issue.Facts[Issue.SourceFact] = "Clearance Check";

                    IssueFindings.Attach(issue, from.Select(r => r.Found)
                                                     .Where(f => f != null).Select(f => f!));

                    foreach (var row in from)
                    {
                        if (!row.Floor.Key.IsEmpty && !issue.Elements.Contains(row.Floor.Key))
                            issue.Elements.Add(row.Floor.Key);

                        if (!row.Over.Key.IsEmpty && !issue.Elements.Contains(row.Over.Key))
                            issue.Elements.Add(row.Over.Key);
                    }

                    return issue;
                }

                var at = 0;

                var (made, problem) = IssueViews.Raise(_session,
                    id => each ? Make(id, new[] { rows[at++] }) : Make(id, rows),
                    each ? rows.Count : 1);

                if (problem != null)
                {
                    Said(Ui.Problem(problem));
                    return;
                }

                Said(Ui.Info(Ui.Count(made, "issue") + " raised from "
                    + Ui.Count(rows.Count, "finding") + ".",
                    "Open the register", () => IssueViews.Open(null)));
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
                    case "status": return FindingStatusWords.Of(row.Status);
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

                        // Kept beside the run so a re-check does not pay for the same property
                        // reads again. In memory only: a column is somebody's bench, not a fact
                        // about the building.
                        if (row.Found != null)
                        {
                            if (!_custom.TryGetValue(row.Found.Id, out var kept))
                                _custom[row.Found.Id] = kept =
                                    new Dictionary<string, string>(StringComparer.Ordinal);

                            kept[column.Id] = value ?? string.Empty;
                        }

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
                        FindingStatusWords.Of(row.Status),
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

                if (!Ui.Asked(dialog)) return;

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

                var going = new List<Finding>();

                foreach (var row in _shown)
                    if (row.Found != null) going.Add(row.Found);

                if (going.Count == 0)
                {
                    Said(Ui.Problem("No row on the grid carries a finding, so there is nothing "
                                    + "a reviewer could answer."));
                    return;
                }

                // The GlobalIds and the routes to the elements, so the receiving tool can select
                // them and a reviewing seat can isolate them without keying the federation.
                var ids = new ClashIdentity.Ids(null, going, null);

                Ui.Busy(() => ids = new ClashIdentity.Ids(_session, going, null));

                var how = FindingBcfOptions.For(FindingSource.Headroom);

                how.SourceFile = _session.Model.Models.Count > 0
                    ? _session.Model.Models[0].DisplayName
                    : null;
                how.ProjectName = _session.Profile.ProjectName;
                how.Author = Environment.UserName ?? "CamelWorks";
                how.WhenTicks = DateTime.UtcNow.Ticks;
                how.IfcGuidOf = ids.Of;
                how.LocatorOf = ids.At;

                var written = FindingBcf.Write(going, null, how);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the findings as a clash file",
                    Filter = ClashFile.Filter,
                    FileName = "clearance" + ClashFile.Extension,
                    AddExtension = true,
                    DefaultExt = ClashFile.Extension,
                };

                if (!Ui.Asked(dialog)) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName, written.Bytes);
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clearance findings as a clash file",
                                written.Explain() + " to " + dialog.FileName);

                foreach (var warning in written.Warnings) _log.Children.Add(Ui.Note(warning));

                var about = ClashIdentity.Explain(going.Count, ids.Stamp);

                if (about != null) _log.Children.Add(ids.Derived > 0 ? Ui.Note(about) : Ui.Line(about, 0.8));

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
