using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The four places the model meets something that is not the model: a crane, a tape measure, a
    /// total station, and a folder of paperwork.
    ///
    /// Everything else in CamelWorks ends at a file somebody opens on a laptop. These end on site.
    /// Where the weight of a module sits decides where the hook goes; the dimension schedule
    /// answers the question somebody would otherwise measure on screen and retype into an email;
    /// the setting-out file is what the engineer walks the floor with; and the O&amp;M folder is the
    /// thing a commissioning engineer asks for and nobody can find.
    ///
    /// One window because they share the read: all four work off the current selection, and two of
    /// them want the same triangles. Reading a federation's geometry four times to answer four
    /// questions about the same forty elements is how a tool earns a reputation for being slow, so
    /// it is read once and kept.
    /// </summary>
    public static class SiteWindow
    {
        /// <summary>Where saved setting-out runs live in the project file.</summary>
        public const string SetOutKey = SettingOut.StoreKey;

        /// <summary>Where saved attachment rules live in the project file.</summary>
        public const string AttachedKey = Attachments.StoreKey;


        /// <summary>How many elements will be read for one answer.</summary>
        public const int ReadCeiling = 20000;

        /// <summary>How many rows any of the tables shows.</summary>
        public const int Shown = 60;

        private const string ByWeight = "the mass the model carries";
        private const string FromMark = "the mark, or the name where there is none";
        private const string FromKey = "the identity CamelWorks knows it by";

        private static readonly string[] Losses =
        {
            "about a seventh", "about a sixth", "about a quarter", "about a third",
        };
        private const string ByVolume = "the volume of the shapes";

        /// <summary>The surface for one of the five field tools, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        /// <param name="toolId">Which of the five.</param>
        public static System.Windows.UIElement Surface(Session session, string toolId) =>
            new Site(session, toolId).View;

        private sealed class Site
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            // Shared by the five windows, because the read is the expensive part and all five
            // answer questions about the same selection. Whoever reads first pays; the next window
            // opens on what is already there and can read again if the selection has moved.
            private static readonly List<ReportElement> Chosen = new List<ReportElement>();

            // One entry per element in Chosen, in the same order, null where there is no body or it
            // could not be read. Shared for the same reason: the weight and the dimensions both
            // work off the triangles.
            private static readonly List<Core.Report.GltfMesh?> Shapes =
                new List<Core.Report.GltfMesh?>();

            private static Session? _readFor;

            private readonly List<AttachmentRule> _rules = new List<AttachmentRule>();

            private readonly StackPanel _read = Ui.Stack();
            private readonly StackPanel _weight = Ui.Stack();
            private readonly StackPanel _sizes = Ui.Stack();
            private readonly StackPanel _points = Ui.Stack();
            private readonly StackPanel _paper = Ui.Stack();
            private readonly StackPanel _tags = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            // The options a read can change - which properties look like a mass, a mark, a file
            // path - so they are rebuilt after each read rather than built once and left stale.
            private readonly StackPanel _options = Ui.Stack();

            // The stage before the first run: what pressing the action will do, off the live read.
            private readonly StackPanel _idle = Ui.Stack();

            private readonly TextBox _folder;
            private readonly TextBox _root;
            private readonly TextBox _prefix;

            private readonly string _tool;

            private SetOutRecipe _recipe = new SetOutRecipe();
            private DimensionRecipe _shape = new DimensionRecipe();
            private LabelRecipe _tag = new LabelRecipe();
            private DimensionSchedule? _schedule;
            private MassResult? _mass;
            private AttachmentReport? _found;
            private LabelRun? _printed;
            private string _basis;
            private PropertyRef _massProperty;
            private int _cut;

            internal Site(Session session, string tool)
            {
                _session = session;
                _tool = tool;

                // The read is shared across the five windows; a different model is a different
                // read, so it is dropped rather than shown against the wrong selection.
                if (!ReferenceEquals(_readFor, session))
                {
                    Chosen.Clear();
                    Shapes.Clear();
                    _readFor = null;
                }

                _kept = ToolMemory.Of(session.Store, "field.site");

                _basis = _kept.Text("basis", ByWeight) ?? ByWeight;

                _folder = Ui.Text(_kept.Text("folder"), "where the files go",
                                  text => _kept.Set("folder", text), 260);

                _root = Ui.Text(_kept.Text("root"), "a folder relative paths are taken from",
                                text => _kept.Set("root", text), 260);

                _prefix = Ui.Text(_kept.Text("prefix"), "an address every code goes behind, or nothing",
                                  text => { _tag.Prefix = text.Trim(); _kept.Set("prefix", text); }, 260);

                Load();

                View = Build();

                // A window opened after another has already read says so, or it looks unread while
                // showing an answer.
                if (Chosen.Count > 0)
                    _read.Children.Add(Ui.Line(Ui.Count(Chosen.Count, "element")
                        + " already read, from the selection at the time.", 0.75));
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// One tool's surface: the shared read and this tool's options in the setup column,
            /// its one action at the foot, and its answer on the stage.
            ///
            /// <b>Five tools, five surfaces, one read.</b> This was one window with four sections
            /// side by side and a fifth that was built and never placed on it - so the ribbon's
            /// Labels button opened a window with no labels on it. Each of the five has its own
            /// surface now, and the expensive part is what they share: the selection and its
            /// geometry are read once and every surface opened afterwards is looking at it.
            /// </summary>
            private UIElement Build()
            {
                Draw();

                return Bench.Surface(Setup(), Foot(), Ui.Stack(_idle, Results(), _note));
            }

            /// <summary>The setup column: the shared read first, then this tool's own options.</summary>
            private UIElement Setup()
            {
                var read = Bench.Group("Selection",
                    Ui.Runner("Read the selection", _read, Gather, primary: false),
                    _read);

                var files = Bench.Group("Files",
                    Ui.PathField("Folder", _folder, PathKind.Folder));

                switch (_tool)
                {
                    case "dimensions":
                        return Ui.Stack(read,
                            Bench.Group("What to measure",
                                Ui.Check("How big each element is", _shape.Sizes,
                                    on => { _shape.Sizes = on; _kept.Set("dim.sizes", on); }),
                                Ui.Check("Corner to corner as well", _shape.Diagonals,
                                    on => { _shape.Diagonals = on; _kept.Set("dim.diagonals", on); }),
                                Ui.Check("How far each sits above its level", _shape.Heights,
                                    on => { _shape.Heights = on; _kept.Set("dim.heights", on); }),
                                Ui.Check("Each to the nearest other one", _shape.Neighbours,
                                    on => { _shape.Neighbours = on; _kept.Set("dim.neighbours", on); }),
                                Ui.Check("The step from each to the next along a run", _shape.Spacing,
                                    on => { _shape.Spacing = on; _kept.Set("dim.spacing", on); })),
                            files);

                    case "setout":
                        return Ui.Stack(read,
                            Bench.Group("Points",
                                Ui.Field("Point numbers start with",
                                    Ui.Text(_recipe.Prefix, text => _recipe.Prefix =
                                        string.IsNullOrWhiteSpace(text) ? "SO" : text.Trim(), 100)),
                                Ui.Field("Take", PointKinds())),
                            Bench.Group("Where the site grid is",
                                Ui.Field("Model origin is at northing",
                                    Number(_recipe.NorthAtOrigin, v => _recipe.NorthAtOrigin = v),
                                    "wrong, and every point is out by the same amount"),
                                Ui.Field("...and easting",
                                    Number(_recipe.EastAtOrigin, v => _recipe.EastAtOrigin = v)),
                                Ui.Field("...and level",
                                    Number(_recipe.LevelAtOrigin, v => _recipe.LevelAtOrigin = v)),
                                Ui.Field("Project angle, clockwise",
                                    Number(_recipe.RotationDegrees, v => _recipe.RotationDegrees = v),
                                    "degrees from grid north, the way it is written on the drawings")),
                            files);

                    case "paperwork":
                        return Ui.Stack(read,
                            Bench.Group("Where the paperwork is named",
                                _options,
                                Ui.Field("Relative paths come from", _root,
                                    "so a job that moves between machines does not lose every link")),
                            files);

                    case "labels":
                        return Ui.Stack(read,
                            Bench.Group("What goes on them", _options),
                            files);

                    default:
                        // No files group: the centre of gravity is copied or boxed, never written
                        // to a folder, and a path field that feeds nothing is clutter.
                        return Ui.Stack(read,
                            Bench.Group("How it is weighed", _options));
                }
            }

            /// <summary>This tool's one action, whose answer lands on the stage.</summary>
            private UIElement Foot()
            {
                switch (_tool)
                {
                    case "dimensions": return Ui.Runner("Measure it", _sizes, Measure);
                    case "setout": return Ui.Runner("Take the points", _points, Points);
                    case "paperwork": return Ui.Runner("Find what is attached", _paper, Paper);
                    case "labels": return Ui.Runner("Make the labels", _tags, Tag);
                    default: return Ui.Runner("Work out where it sits", _weight, Weigh);
                }
            }

            /// <summary>The stage panel this tool's answer lands in.</summary>
            private StackPanel Results()
            {
                switch (_tool)
                {
                    case "dimensions": return _sizes;
                    case "setout": return _points;
                    case "paperwork": return _paper;
                    case "labels": return _tags;
                    default: return _weight;
                }
            }

            /// <summary>The point kinds to take, each its own tick.</summary>
            private UIElement PointKinds()
            {
                var kinds = Ui.Stack();

                foreach (var kind in new[]
                         {
                             PointKind.Base, PointKind.Top, PointKind.Centre,
                             PointKind.Start, PointKind.End,
                         })
                {
                    var here = kind;

                    kinds.Children.Add(Ui.Check(SettingOut.Word(here), _recipe.Kinds.Contains(here),
                        on =>
                        {
                            if (on && !_recipe.Kinds.Contains(here)) _recipe.Kinds.Add(here);
                            if (!on) _recipe.Kinds.Remove(here);
                        }));
                }

                return kinds;
            }

            // -----------------------------------------------------------------------------------
            // Reading
            // -----------------------------------------------------------------------------------

            private void Gather(Job job)
            {
                Chosen.Clear();
                Shapes.Clear();
                _cut = 0;
                _mass = null;
                _schedule = null;
                _found = null;
                _printed = null;

                var seen = 0;

                foreach (var item in _session.Model.Traverse(TraversalScope.CurrentSelection))
                {
                    seen++;

                    if (!job.Step(Chosen.Count, "elements read")) return;

                    if (Chosen.Count >= ReadCeiling) { _cut++; continue; }

                    var element = ReportElement.Of(item);

                    if (element != null) Chosen.Add(element);
                }

                if (Chosen.Count == 0)
                {
                    _read.Children.Add(Ui.Empty("Nothing is selected.",
                        "Select what is going on site - a module, a lift, a run of pipework - and "
                        + "read it again."));

                    Draw();
                    return;
                }

                _read.Children.Add(Ui.Line(
                    Ui.Count(Chosen.Count, "element") + ", "
                    + Ui.Count(Chosen.Count(e => e.HasGeometry), "with a body"), 1, true));

                if (_cut > 0)
                    _read.Children.Add(Ui.Note("That selection holds " + Ui.Count(seen, "element")
                        + ". The first " + ReadCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + " were read."));

                _readFor = _session;

                Draw();
            }

            /// <summary>
            /// Bring the surface up to date with the read: clear an answer taken off the old
            /// selection, restate what the action will do, and rebuild the options a read changes.
            /// </summary>
            private void Draw()
            {
                Results().Children.Clear();
                Idle();

                switch (_tool)
                {
                    case "paperwork": PaperOptions(); break;
                    case "labels": TagOptions(); break;
                    case "weight": WeightOptions(); break;
                }
            }

            /// <summary>What pressing the action will do, off what is actually read.</summary>
            private void Idle()
            {
                _idle.Children.Clear();

                var none = Chosen.Count == 0;
                var count = Ui.Count(Chosen.Count, "element");

                switch (_tool)
                {
                    case "dimensions":
                        _idle.Children.Add(none
                            ? Bench.Ready("Nothing is read yet.",
                                "Read the selection, and the schedule is measured off it here.")
                            : Bench.Ready("Measures " + count + " onto one schedule.",
                                "Sizes, heights and spacings land here, ready to write as a file."));
                        break;

                    case "setout":
                        _idle.Children.Add(none
                            ? Bench.Ready("Nothing is read yet.",
                                "Read the selection, and its setting-out points are taken here.")
                            : Bench.Ready("Takes setting-out points off " + count + ".",
                                "Numbered points in site coordinates land here, ready to write as "
                                + "the file a total station loads."));
                        break;

                    case "paperwork":
                        _idle.Children.Add(none
                            ? Bench.Ready("Nothing is read yet.",
                                "Read the selection, and what it points at is gathered here.")
                            : Bench.Ready("Looks along " + count + " for the documents and photos "
                                + "they point at.",
                                "What is found lands here; a row opens the file it names."));
                        break;

                    case "labels":
                        _idle.Children.Add(none
                            ? Bench.Ready("Nothing is read yet.",
                                "Read the selection, and a label is made for each element here.")
                            : Bench.Ready("Makes a label for each of " + count + ".",
                                "The sheet previews here, ready to write and print."));
                        break;

                    default:
                        _idle.Children.Add(none
                            ? Bench.Ready("Nothing is read yet.",
                                "Read the selection, and where its weight sits is worked out here.")
                            : Bench.Ready("Works out where the weight of " + count + " sits.",
                                "The centre of gravity, the mass and the enclosed volume land here."));
                        break;
                }
            }

            /// <summary>
            /// True when the foot action was pressed before anything was read, said in the panel
            /// the answer would have landed in. Clears the idle statement either way, because from
            /// this press on the panel holds the outcome.
            /// </summary>
            private bool Unread(Panel into)
            {
                _idle.Children.Clear();

                if (Chosen.Count > 0) return false;

                into.Children.Add(Ui.Empty("Nothing is read yet.",
                    "Read the selection in the setup column first."));

                return true;
            }

            // -----------------------------------------------------------------------------------
            // Weight and balance
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// The two choices the answer turns on. Rebuilt after each read, because which
            /// properties look like a mass is a fact about the selection, not the tool.
            /// </summary>
            private void WeightOptions()
            {
                _options.Children.Clear();

                var fields = MassFields();

                var shown = _massProperty.Name.Length == 0
                    ? fields.FirstOrDefault()
                    : Show(_massProperty);

                if (shown != null && fields.Count > 0) _massProperty = Split(shown);

                var picker = Ui.Choice(fields, shown, pick =>
                {
                    _massProperty = Split(pick);
                    _kept.Set("mass", pick);
                }, 240);

                picker.IsEnabled = fields.Count > 0;

                _options.Children.Add(Ui.Field("Weight it by",
                    Ui.Choice(new[] { ByWeight, ByVolume }, _basis, pick =>
                    {
                        _basis = pick;
                        _kept.Set("basis", pick);
                    }, 240)));

                _options.Children.Add(Ui.Field("The mass is in", picker,
                    Chosen.Count == 0
                        ? "read the selection and the candidates appear"
                        : fields.Count == 0
                            ? "nothing in this selection looks like a mass"
                            : "a property holding kg, t or lb - or a bare number, taken as kg"));
            }

            /// <summary>
            /// Read every element's triangles once, into <c>Shapes</c>, in the order they were chosen.
            ///
            /// Both the weight and the dimensions want the same triangles, and a federation is
            /// slow enough to read that doing it twice is felt. So it is done once and the entry
            /// stays null where the element has no body or the reader gave nothing back - the two
            /// callers each say what a null means for their own answer, because a hanger with no
            /// body is a different problem for a centre of gravity than it is for a length.
            /// </summary>
            private bool ReadGeometry(Job job)
            {
                if (Shapes.Count == Chosen.Count) return true;

                var geometry = new NavGeometry(_session.Model);
                var read = 0;

                Shapes.Clear();

                job.Say("Reading geometry...");

                foreach (var element in Chosen)
                {
                    if (!job.Step(read++, Chosen.Count, "elements read"))
                    {
                        // Stopped part way through leaves a cache shorter than the scope, which
                        // would silently mis-pair every element with the next one's triangles.
                        Shapes.Clear();
                        return false;
                    }

                    var item = _session.Model.Resolve(element.Key);

                    Shapes.Add(item == null || !item.HasGeometry
                                  ? null
                                  : geometry.Read(item, element.Name));
                }

                return true;
            }

            private void Weigh(Job job)
            {
                if (Unread(_weight)) return;

                if (!ReadGeometry(job)) return;

                var parts = new List<MassPart>();
                var silent = 0;

                for (var at = 0; at < Chosen.Count; at++)
                {
                    var element = Chosen[at];
                    var mesh = Shapes[at];

                    if (mesh == null)
                    {
                        if (_session.Model.Resolve(element.Key)?.HasGeometry == true) silent++;

                        parts.Add(new MassPart(element.Key, element.Name, null, null));
                        continue;
                    }

                    double? mass = null;

                    if (_massProperty.Name.Length > 0
                        && MassProperties.TryMass(element.Value(_massProperty), out var kilograms))
                        mass = kilograms;

                    parts.Add(new MassPart(element.Key, element.Name, mesh.Positions, mesh.Indices, mass));
                }

                job.Say("Working it out...");

                var result = MassProperties.Of(parts, _basis == ByWeight);

                _mass = result;

                if (result.Refusal != null)
                {
                    _weight.Children.Add(Ui.Problem(result.Refusal));
                    return;
                }

                _weight.Children.Add(Ui.Line(result.Explain(_session.Profile.Units), 1, true));

                _weight.Children.Add(Ui.Across(
                    Ui.Figure(result.IsTrueCentreOfGravity ? "yes" : "no", "a real centre of gravity",
                              result.IsTrueCentreOfGravity ? Tone.Good : Tone.Warn),
                    Ui.Figure(result.Weight > 0
                                  ? result.Weight.ToString("N0", CultureInfo.InvariantCulture) + " kg"
                                  : "—",
                              "total mass"),
                    Ui.Figure(result.Volume.ToString("0.##", CultureInfo.InvariantCulture) + " m³",
                              "enclosed volume")));

                foreach (var note in result.Notes) _weight.Children.Add(Ui.Note(note));

                _weight.Children.Add(Ui.Actions(
                    Ui.Quiet("Copy the position", () => Copy(
                        result.X.ToString("0.###", CultureInfo.InvariantCulture) + ", "
                        + result.Y.ToString("0.###", CultureInfo.InvariantCulture) + ", "
                        + result.Z.ToString("0.###", CultureInfo.InvariantCulture))),
                    Ui.Quiet("Put a section box round it", () => Box(result))));

                if (silent > 0)
                    _weight.Children.Add(Ui.Note(Ui.Count(silent, "element")
                        + " has a body the drawing layer would not give triangles for, so "
                        + (silent == 1 ? "it is" : "they are") + " not in this."));
            }

            /// <summary>A metre cube around the answer, so it can be seen rather than read.</summary>
            /// <param name="result">Where the weight sits.</param>
            private void Box(MassResult result)
            {
                _note.Children.Clear();

                try
                {
                    _session.View.SetSectionBox(
                        new BoundingBox(result.X - 0.5, result.Y - 0.5, result.Z - 0.5,
                                        result.X + 0.5, result.Y + 0.5, result.Z + 0.5), 0);

                    _note.Children.Add(Ui.Done("A one metre box sits on it. Project ▸ turn the "
                        + "section box off again when you are done."));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not place it: " + Ui.Describe(e)));
                }
            }

            // -----------------------------------------------------------------------------------
            // Dimensions
            // -----------------------------------------------------------------------------------

            private void Measure(Job job)
            {
                if (Unread(_sizes)) return;

                if (!ReadGeometry(job)) return;

                job.Say("Measuring...");

                var elements = new List<FieldElement>(Chosen.Count);

                for (var at = 0; at < Chosen.Count; at++)
                {
                    var element = Chosen[at];

                    elements.Add(new FieldElement(element.Key, element.Name, element.Bounds,
                                                  element.HasGeometry, Mark(element),
                                                  Shapes[at]?.Positions));
                }

                var schedule = Dimensions.Schedule(elements, _shape, Levels());

                _schedule = schedule;

                if (schedule.Refusal != null)
                {
                    _sizes.Children.Add(Ui.Problem(schedule.Refusal));
                    return;
                }

                _sizes.Children.Add(Ui.Line(schedule.Explain(), 1, true));

                foreach (var note in schedule.Notes) _sizes.Children.Add(Ui.Note(note));

                var units = _session.Profile.Units;

                var sizes = Ui.Table(
                    schedule.Rows.Take(Shown).Select(r => new TableRow(r,
                        r.Reference,
                        Dimensions.Word(r.Kind),
                        r.Subject,
                        r.Against ?? "—",
                        r.Say(units),
                        r.Oriented ? "geometry" : "box")).ToList(),
                    ("Ref", 70), ("Measurement", 110), ("Element", 0), ("Against", 150),
                    (units.LengthUnit, 100), ("From", 90));

                // The rows worth sending are the ones that failed, and they are scattered down the
                // schedule rather than gathered at the top.
                _sizes.Children.Add(Ui.Picking(sizes,
                    new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(sizes, picked), say),
                                    primary: true)));

                _sizes.Children.Add(Ui.Tally(Math.Min(Shown, schedule.Rows.Count),
                                             schedule.Rows.Count, "dimension"));

                _sizes.Children.Add(Ui.Actions(
                    Ui.Primary("Write the schedule", () => WriteSchedule(schedule))));
            }

            /// <summary>
            /// The levels to measure heights from: the grid's where the model has one, and
            /// otherwise the ones the selection itself implies.
            ///
            /// Nobody sets levels up before asking how high something is, so where the model does
            /// not declare them they are worked out from where things actually sit. That answer is
            /// worth less than an authored one, which is why the schedule names the level each
            /// height was taken from rather than only the number.
            /// </summary>
            private LevelSet Levels()
            {
                var grid = new NavGrids(_session.Model).Derive();

                if (grid.Bands.Count > 0) return grid;

                var floors = Chosen.Where(e => e.HasGeometry).Select(e => e.Bounds.MinZ).ToList();

                return floors.Count == 0 ? LevelSet.Empty : LevelSet.Derive(floors);
            }

            private void WriteSchedule(DimensionSchedule schedule)
            {
                _note.Children.Clear();

                var folder = _folder.Text.Trim();

                if (folder.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which folder the files go in first."));
                    return;
                }

                var stem = Path.Combine(folder, Safe(schedule.Recipe.Name));
                var document = Dimensions.Report(schedule, _session.Profile.Units);

                try
                {
                    File.WriteAllText(stem + ".csv", Dimensions.Csv(schedule),
                                      new UTF8Encoding(true));

                    File.WriteAllText(stem + ".html", Core.Report.HtmlReportWriter.Write(document),
                                      new UTF8Encoding(true));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _kept.Set("folder", folder);
                _kept.Keep();

                _session.Record(ActivityKind.Export, "measured " + schedule.Recipe.Name,
                                Ui.Count(schedule.Rows.Count, "dimension") + " to " + stem + ".csv");

                _note.Children.Add(Ui.Done("Wrote " + stem + ".csv and " + stem + ".html — "
                    + Ui.Count(schedule.Rows.Count, "dimension") + "."));

                _note.Children.Add(Ui.Note("The file is in metres whatever the screen reads in, "
                    + "because a number going into a calculation has to carry one unit and say "
                    + "which. The report reads in " + _session.Profile.Units.LengthUnit + "."));
            }

            // -----------------------------------------------------------------------------------
            // Setting out
            // -----------------------------------------------------------------------------------

            private void Points(Job job)
            {
                if (Unread(_points)) return;

                var elements = Chosen.Select(e => new FieldElement(
                    e.Key, e.Name, e.Bounds, e.HasGeometry, Mark(e))).ToList();

                var result = SettingOut.Of(elements, _recipe);

                if (result.Refusal != null)
                {
                    _points.Children.Add(Ui.Problem(result.Refusal));
                    return;
                }

                _points.Children.Add(Ui.Line(result.Explain(), 1, true));

                foreach (var note in result.Notes) _points.Children.Add(Ui.Note(note));

                // A surveyor going out to set six points does not want the file of two hundred.
                var points = Ui.Table(
                    result.Points.Take(Shown).Select(p => new TableRow(p,
                        p.Number,
                        p.Northing.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Easting.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Level.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Description)).ToList(),
                    ("Point", 80), ("Northing", 100), ("Easting", 100), ("Level", 90), ("What", 0));

                _points.Children.Add(Ui.Picking(points,
                    new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(points, picked), say, "point"),
                                    primary: true)));

                _points.Children.Add(Ui.Tally(Math.Min(Shown, result.Points.Count),
                                              result.Points.Count, "point"));

                _points.Children.Add(Ui.Actions(
                    Ui.Primary("Write the file", () => WritePoints(result)),
                    Ui.Quiet("Save this setting out", () => KeepPoints())));
            }

            private void WritePoints(SetOutResult result)
            {
                _note.Children.Clear();

                var folder = _folder.Text.Trim();

                if (folder.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which folder the files go in first."));
                    return;
                }

                var file = Path.Combine(folder, Safe(_recipe.Name) + ".csv");

                try
                {
                    File.WriteAllText(file,
                        SettingOut.Csv(result, _session.Profile.ProjectName),
                        new UTF8Encoding(true));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _kept.Set("folder", folder);
                _kept.Keep();

                _session.Record(ActivityKind.Export, "set out " + _recipe.Name,
                                Ui.Count(result.Points.Count, "point") + " to " + file);

                _note.Children.Add(Ui.Done("Wrote " + file + " — "
                    + Ui.Count(result.Points.Count, "point") + "."));

                if (_recipe.IsUnshifted)
                    _note.Children.Add(Ui.Note("Those are model coordinates. The file says so in "
                        + "its own header, but somebody has to read it."));
            }

            private void KeepPoints()
            {
                _note.Children.Clear();

                var written = SettingOut.Keep(
                    SettingOut.Saved(_session.Store.Section(ProjectStore.DataSection)[SetOutKey]),
                    _recipe);

                _session.Store.Section(ProjectStore.DataSection).Set(SetOutKey, written);

                _session.Record(ActivityKind.Setup, "Saved the setting out \"" + _recipe.Name + "\"",
                                _recipe.ToString());

                _note.Children.Add(Ui.Done("Saved. The offset and the angle come back next time."));
            }

            // -----------------------------------------------------------------------------------
            // Labels for the work
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// What the labels say. Rebuilt after each read, because the properties worth printing
            /// - nearly filled in, not constant, short enough for a sticker - come off the
            /// selection itself.
            /// </summary>
            private void TagOptions()
            {
                _options.Children.Clear();

                var marks = MarkFields();

                var options = new List<string> { FromMark };

                options.AddRange(marks);
                options.Add(FromKey);

                var shown = _tag.Source == LabelSource.Key
                    ? FromKey
                    : _tag.Source == LabelSource.Property && _tag.PropertyName.Length > 0
                        ? Show(new PropertyRef(_tag.PropertyCategory, _tag.PropertyName))
                        : FromMark;

                if (!options.Contains(shown, StringComparer.OrdinalIgnoreCase)) shown = FromMark;

                _options.Children.Add(
                    Ui.Field("What the code says", Ui.Choice(options, shown, Chose, 260),
                             "the mark is what a drawing calls it, and needs nothing setting up"));

                _options.Children.Add(
                    Ui.Field("Behind it", _prefix,
                             "leave it empty and the code says exactly what is printed under it"));

                _options.Children.Add(
                    Ui.Field("How much can be lost",
                        Ui.Choice(Losses, Loss(_tag.Level), pick =>
                        {
                            _tag.Level = Level(pick);
                            _kept.Set("labelLevel", pick);
                        }, 200),
                        "a label on site gets scuffed; the more it can lose the bigger the code"));
            }

            private void Chose(string pick)
            {
                _kept.Set("labelFrom", pick);

                if (string.Equals(pick, FromMark, StringComparison.Ordinal))
                {
                    _tag.Source = LabelSource.Mark;
                    _tag.PropertyName = string.Empty;
                    _tag.PropertyCategory = string.Empty;
                    return;
                }

                if (string.Equals(pick, FromKey, StringComparison.Ordinal))
                {
                    _tag.Source = LabelSource.Key;
                    _tag.PropertyName = string.Empty;
                    _tag.PropertyCategory = string.Empty;
                    return;
                }

                var field = Split(pick);

                _tag.Source = LabelSource.Property;
                _tag.PropertyName = field.Name;
                _tag.PropertyCategory = field.Category;
            }

            private void Tag(Job job)
            {
                if (Unread(_tags)) return;

                job.Say("Making the codes...");

                // Kept beside the element it came off rather than looked up by key, because two
                // models in a federation can hand back the same key for different things and a
                // label that names the wrong pipe is worse than no label.
                var against = new Dictionary<FieldElement, ReportElement>();
                var elements = new List<FieldElement>(Chosen.Count);

                foreach (var element in Chosen)
                {
                    var made = new FieldElement(element.Key, element.Name, element.Bounds,
                                                element.HasGeometry, Mark(element));

                    against[made] = element;
                    elements.Add(made);
                }

                var field = new PropertyRef(_tag.PropertyCategory, _tag.PropertyName);

                var run = LabelSheet.Make(elements, _tag,
                    made => against.TryGetValue(made, out var from) ? from.Value(field) : null);

                _printed = run;

                if (run.Refusal != null)
                {
                    _tags.Children.Add(Ui.Problem(run.Refusal));
                    return;
                }

                _tags.Children.Add(Ui.Line(run.Explain(), 1, true));

                foreach (var note in run.Notes) _tags.Children.Add(Ui.Note(note));

                var tags = Ui.Table(
                    run.Labels.Take(Shown).Select(l => new TableRow(l,
                        l.Heading,
                        l.Encoded,
                        l.Symbol == null
                            ? "—"
                            : l.Symbol.Size.ToString(CultureInfo.InvariantCulture) + " modules")).ToList(),
                    ("On the label", 200), ("In the code", 0), ("Size", 110));

                _tags.Children.Add(Ui.Picking(tags,
                    new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(tags, picked), say, "label"),
                                    primary: true)));

                _tags.Children.Add(Ui.Tally(Math.Min(Shown, run.Labels.Count), run.Labels.Count, "label"));

                _tags.Children.Add(Ui.Actions(
                    Ui.Primary("Write the sheet", () => WriteTags(run))));
            }

            private void WriteTags(LabelRun run)
            {
                _note.Children.Clear();

                var folder = _folder.Text.Trim();

                if (folder.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which folder the files go in first."));
                    return;
                }

                var stem = Path.Combine(folder, Safe(run.Recipe.Name));

                try
                {
                    File.WriteAllText(stem + ".html",
                                      LabelSheet.Html(run, _session.Profile.ProjectName),
                                      new UTF8Encoding(true));

                    File.WriteAllText(stem + ".csv", LabelSheet.Csv(run), new UTF8Encoding(true));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _kept.Set("folder", folder);
                _kept.Keep();

                _session.Record(ActivityKind.Export, "labelled " + run.Recipe.Name,
                                Ui.Count(run.Labels.Count, "label") + " to " + stem + ".html");

                _note.Children.Add(Ui.Done("Wrote " + stem + ".html and " + stem + ".csv — "
                    + Ui.Count(run.Labels.Count, "label") + "."));

                _note.Children.Add(Ui.Note("Open the page and print it. The dashed outlines are "
                    + "there on screen to show where each label ends and do not print. The .csv is "
                    + "the index of what was printed, so whatever reads the codes has something to "
                    + "look them up in."));
            }

            /// <summary>Properties that read like something written on a drawing.</summary>
            private IReadOnlyList<string> MarkFields() =>
                DataReport.Fields(Chosen)
                    // Nearly filled in, not the same on everything, and short enough to print on a
                    // sticker. A property that reads the same on every element identifies nothing,
                    // and a paragraph of description will not fit on a label whatever it says.
                    .Where(f => f.Filled >= Math.Max(1, Chosen.Count / 2)
                                && !f.IsConstant
                                && (f.Example == null || f.Example.Length <= 40))
                    .Select(f => Show(f.Field))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(40)
                    .ToList();

            private static string Loss(QrLevel level) => level switch
            {
                QrLevel.Low => "about a seventh",
                QrLevel.Medium => "about a sixth",
                QrLevel.High => "about a quarter",
                _ => "about a third",
            };

            private static QrLevel Level(string pick) => pick switch
            {
                "about a seventh" => QrLevel.Low,
                "about a sixth" => QrLevel.Medium,
                "about a quarter" => QrLevel.High,
                _ => QrLevel.Highest,
            };

            // -----------------------------------------------------------------------------------
            // The paperwork
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Which properties name the files. Rebuilt after each read, because what looks like a
            /// document path is a fact about the selection, not the tool.
            /// </summary>
            private void PaperOptions()
            {
                _options.Children.Clear();

                if (Chosen.Count == 0)
                {
                    _options.Children.Add(Ui.Small("Read the selection and the naming "
                        + "properties appear."));
                    return;
                }

                foreach (var kind in new[] { AttachmentKind.Document, AttachmentKind.Photo })
                {
                    var here = kind;
                    var options = Attachments.Likely(Chosen, here).Select(Show).ToList();

                    if (options.Count == 0)
                    {
                        _options.Children.Add(Ui.Small("Nothing in this selection looks like it says "
                            + "where the " + (here == AttachmentKind.Photo ? "photos" : "documents")
                            + " are."));

                        continue;
                    }

                    var rule = _rules.FirstOrDefault(r => r.Kind == here);

                    var shown = rule == null ? options[0] : Show(rule.Property);

                    if (!options.Contains(shown, StringComparer.OrdinalIgnoreCase)) shown = options[0];

                    Remember(here, Split(shown));

                    _options.Children.Add(Ui.Field(
                        here == AttachmentKind.Photo ? "Photos are named by" : "Documents are named by",
                        Ui.Choice(options, shown, pick => Remember(here, Split(pick)), 260)));
                }
            }

            private void Paper(Job job)
            {
                if (Unread(_paper)) return;

                var root = _root.Text.Trim();

                foreach (var rule in _rules) rule.Root = root.Length == 0 ? null : root;

                job.Say("Looking...");

                var report = Attachments.Find(Chosen, _rules, PhysicalFileSystem.Instance);

                _found = report;

                if (report.Refusal != null)
                {
                    _paper.Children.Add(Ui.Problem(report.Refusal));
                    return;
                }

                _paper.Children.Add(Ui.Line(report.Explain(), 1, true));

                foreach (var note in report.Notes) _paper.Children.Add(Ui.Note(note));

                if (report.Found.Count == 0) return;

                var table = Ui.Table(
                    report.Found.Take(Shown).Select(a => new TableRow(a,
                        a.Name, a.Kind.ToString(), a.Where)).ToList(),
                    ("File", 220), ("Kind", 90), ("Where", 0));

                table.OnPick(picked =>
                {
                    if (picked is Attachment attachment) Open(attachment.Where);
                });

                _paper.Children.Add(table);
                _paper.Children.Add(Ui.Small("Click a row to open it."));
                _paper.Children.Add(Ui.Tally(Math.Min(Shown, report.Found.Count),
                                             report.Found.Count, "thing"));

                _paper.Children.Add(Ui.Actions(
                    Ui.Quiet("Remember these rules", KeepRules),
                    Ui.Quiet("Write the list", () => WritePaper(report))));

                if (report.EmptyFolders.Count > 0)
                {
                    var empty = Ui.Table(report.EmptyFolders.Take(Shown)
                                             .Select(f => new TableRow(f, f)).ToList(),
                                         ("Folder", 0));

                    var heading = Ui.Eyebrow("Folders that are there and empty");
                    heading.Margin = new Thickness(0, 14, 0, 6);

                    _paper.Children.Add(heading);
                    _paper.Children.Add(Ui.Picking(empty,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(empty, picked), say, "folder"))));
                    _paper.Children.Add(Ui.Tally(Math.Min(Shown, report.EmptyFolders.Count),
                                                 report.EmptyFolders.Count, "folder"));
                }
            }

            private void WritePaper(AttachmentReport report)
            {
                _note.Children.Clear();

                var folder = _folder.Text.Trim();

                if (folder.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which folder the files go in first."));
                    return;
                }

                var sheet = new Core.Report.XlsxSheet("Attached");

                sheet.Row("Element", "Kind", "File", "Where");

                var names = Chosen.ToDictionary(e => e.Key, e => e.Name);

                foreach (var attachment in report.Found)
                    sheet.Row(names.TryGetValue(attachment.Key, out var name) ? name : "(gone)",
                              attachment.Kind.ToString(), attachment.Name, attachment.Where);

                var file = Path.Combine(folder, "Attached.xlsx");

                try
                {
                    File.WriteAllBytes(file, Core.Report.XlsxWriter.Write(new[] { sheet }));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "listed what is attached",
                                Ui.Count(report.Found.Count, "thing") + " to " + file);

                _note.Children.Add(Ui.Done("Wrote " + file + "."));
            }

            private void KeepRules()
            {
                _note.Children.Clear();

                _session.Store.Section(ProjectStore.DataSection)
                        .Set(AttachedKey, Attachments.Keep(_rules));

                _session.Record(ActivityKind.Setup, "Saved where the paperwork lives",
                                string.Join(", ", _rules.Select(r => r.ToString())));

                _note.Children.Add(Ui.Done("Saved. These come back next time this project opens."));
            }

            // -----------------------------------------------------------------------------------
            // Odds and ends
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                var section = _session.Store.Section(ProjectStore.DataSection);

                var saved = SettingOut.Saved(section[SetOutKey]);

                if (saved.Count > 0) _recipe = saved[saved.Count - 1].Copy();

                _rules.Clear();
                _rules.AddRange(Attachments.Saved(section[AttachedKey]));

                var mass = _kept.Text("mass");

                if (mass != null) _massProperty = Split(mass);

                var from = _kept.Text("labelFrom");

                if (from != null) Chose(from);

                var loss = _kept.Text("labelLevel");

                if (loss != null) _tag.Level = Level(loss);

                var behind = _kept.Text("prefix");

                if (behind != null) _tag.Prefix = behind.Trim();

                _shape.Sizes = _kept.Flag("dim.sizes", _shape.Sizes);
                _shape.Diagonals = _kept.Flag("dim.diagonals", _shape.Diagonals);
                _shape.Heights = _kept.Flag("dim.heights", _shape.Heights);
                _shape.Neighbours = _kept.Flag("dim.neighbours", _shape.Neighbours);
                _shape.Spacing = _kept.Flag("dim.spacing", _shape.Spacing);

                // Every box unticked is a schedule with no rows and no way for the screen to say
                // why, so what comes back never leaves the tool asking for nothing.
                if (_shape.IsEmpty) { _shape.Sizes = true; _shape.Heights = true; }

                var root = _rules.Select(r => r.Root).FirstOrDefault(r => r != null);

                if (root != null) _root.Text = root;
            }

            private void Remember(AttachmentKind kind, PropertyRef property)
            {
                var found = _rules.FirstOrDefault(r => r.Kind == kind);

                if (found != null) _rules.Remove(found);

                _rules.Add(new AttachmentRule(kind, property)
                {
                    Root = found?.Root,
                });
            }

            /// <summary>The properties in scope that look like they hold a mass.</summary>
            private IReadOnlyList<string> MassFields() =>
                DataReport.Fields(Chosen)
                    // Called what a mass is usually called, or holding something that reads as one.
                    // The second is what finds it on a model whose exporter named it "Unit Wt".
                    .Where(f => f.Filled > 0
                                && (MassProperties.MassNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase)
                                    || (f.Example != null && MassProperties.TryMass(f.Example, out _))))
                    .Select(f => Show(f.Field))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            /// <summary>What the model calls this element on the drawings, where it says.</summary>
            private static string? Mark(ReportElement element)
            {
                foreach (var name in new[] { "Mark", "Type Mark", "Tag", "Asset Tag", "Reference" })
                {
                    var found = element.Value(new PropertyRef(string.Empty, name));

                    if (!string.IsNullOrWhiteSpace(found)) return found;
                }

                return null;
            }

            private void Open(string where)
            {
                _note.Children.Clear();

                try
                {
                    Process.Start(new ProcessStartInfo(where) { UseShellExecute = true });
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not open it: " + Ui.Describe(e)));
                }
            }

            private void Copy(string text)
            {
                _note.Children.Clear();

                try
                {
                    Clipboard.SetText(text);
                    _note.Children.Add(Ui.Done("Copied " + text + "."));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not copy it: " + Ui.Describe(e)));
                }
            }

            private static TextBox Number(double value, Action<double> onChanged) =>
                Ui.Text(value.ToString("0.####", CultureInfo.InvariantCulture), text =>
                {
                    if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture,
                                        out var read))
                        onChanged(read);
                }, 140);

            private static string Show(PropertyRef property) =>
                property.Category.Length == 0
                    ? property.Name
                    : property.Category + " / " + property.Name;

            private static PropertyRef Split(string? text)
            {
                var whole = (text ?? string.Empty).Trim();
                var slash = whole.IndexOf('/');

                return slash < 0
                    ? new PropertyRef(string.Empty, whole)
                    : new PropertyRef(whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
            }

            private static string Safe(string? name)
            {
                var text = (name ?? string.Empty).Trim();
                var built = new StringBuilder(text.Length);

                foreach (var letter in text)
                    built.Append("\\/:*?\"<>|".IndexOf(letter) >= 0 || letter < ' ' ? '-' : letter);

                var cleaned = built.ToString().Trim('-', ' ', '.');

                return cleaned.Length == 0 ? "Setting out" : cleaned;
            }
        }
    }
}
