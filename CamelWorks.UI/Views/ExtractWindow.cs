using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Publish;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A piece of the federation, chosen by a rule rather than by clicking.
    ///
    /// Behind every deliverable that is not the whole model: this level, this zone, this area, what
    /// that viewpoint shows, that supplier's model. Done by hand it is a selection made by eye that
    /// nobody can reproduce, which is why the same person makes it again every month and it is
    /// subtly different every time.
    ///
    /// The rule is saved, not the answer. "Level 03 mechanical" re-runs against whatever is loaded
    /// today and takes whatever is on Level 03 today.
    ///
    /// A window because choosing is a form with six shapes to it and a running count underneath,
    /// and because what comes out is four different files.
    /// </summary>
    public static class ExtractWindow
    {
        /// <summary>Where saved extracts live in the project file.</summary>
        public const string Key = Extract.StoreKey;

        /// <summary>How many elements will be read into one extract before it stops and says so.</summary>
        public const int ReadCeiling = 60000;

        /// <summary>How many elements' geometry will be read for a drawing.</summary>
        public const int DrawCeiling = 20000;

        private const string BySelection = "what is selected";
        private const string ByViewpoint = "what a viewpoint shows";
        private const string ByLevel = "everything on a level";
        private const string ByValue = "everything with a property value";
        private const string ByBox = "everything in a box";
        private const string ByModel = "everything from one model";

        private const string Touching = "touching it";
        private const string Wholly = "wholly inside it";
        private const string Centred = "centred in it";


        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("extract"), () => new Cutter(session).View, 1140, 780);
        }

        private sealed class Cutter
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            private readonly List<ExtractRecipe> _saved = new List<ExtractRecipe>();
            private readonly List<ReportElement> _scope = new List<ReportElement>();

            private readonly StackPanel _read = Ui.Stack();
            private readonly StackPanel _how = Ui.Stack();
            private readonly StackPanel _verdict = Ui.Stack();
            private readonly StackPanel _out = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            private readonly TextBox _folder;
            private readonly TextBox _name;

            private LevelSet _levels = LevelSet.Empty;
            private IReadOnlyList<ElementKey>? _shown;
            private string? _showing;
            private ExtractRecipe _recipe = new ExtractRecipe();
            private ComboBox? _picker;
            private int _cut;

            internal Cutter(Session session)
            {
                _session = session;
                _kept = ToolMemory.Of(session.Store, "publish.extract");

                _folder = Ui.Text(_kept.Text("folder"), "where the files go",
                                  text => _kept.Set("folder", text), 260);

                _name = Ui.Text(_kept.Text("name", "Extract"), null, 220);

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Three decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns, so the buttons that write files sat beside a rule
            /// nothing had run yet, pointed at a model nothing had read. The dependency is real:
            /// the rule needs elements to choose from, and the writers need a rule that chose
            /// something.
            ///
            /// The rule and what it takes share a step rather than getting one each. They are one
            /// decision looked at from two sides - you change the property and read the count, and
            /// putting a Next between them would mean pressing it forty times.
            ///
            /// Each step's surface is built once and handed back on every visit, so going back to
            /// change the rule does not re-read the model.
            /// </summary>
            private UIElement Build()
            {
                var model = Ui.Stack(
                    Ui.Runner("Read it", _read, Gather),
                    Ui.Small("Read once, then try as many rules as you like against it."),
                    _read);

                var piece = Ui.Stack(
                    Ui.Form(Ui.Field("Take", Ui.Choice(Axes, Word(_recipe.Axis), Chose, 250))),
                    _how,
                    Ui.Rule(),
                    Ui.Eyebrow("What that is"),
                    _verdict,
                    Ui.Rule(),
                    Ui.Eyebrow("This extract"),
                    Saved());

                var take = Ui.Stack(
                    Ui.PathField("Folder", _folder, PathKind.Folder),
                    _out,
                    _note,
                    Ui.Rule(),
                    Ui.Section("Why there is no NWD button", "what Navisworks lets a plug-in do", false,
                        Ui.Small("The Navisworks .NET API has no call that writes part of a document "
                                 + "to NWD. A plug-in can save the document it has, whole, and "
                                 + "nothing narrower - so a button here saying \"NWD\" would either "
                                 + "write the entire federation under a name that promised a slice "
                                 + "of it, or quietly do nothing."),
                        Ui.Small("What is here instead is the hard part and the reusable part: "
                                 + "working out precisely what belongs in the piece, saying so, and "
                                 + "writing it to the formats CamelWorks does write. Press \"Select "
                                 + "it in the model\" and Navisworks' own File ▸ Export takes it "
                                 + "from there, on exactly the elements this rule chose.")));

                Draw();

                var steps = new[]
                {
                    new Ui.Step("The model", () => model,
                                () => _scope.Count == 0 ? "Read the model first." : null),
                    new Ui.Step("The piece", () => piece,
                                () => _out.Children.Count == 0
                                      ? "The rule has not taken anything out yet." : null),
                    new Ui.Step("Take it out", () => take),
                };

                return Ui.Scroll(Ui.Steps(steps, null, null));
            }

            private UIElement Saved()
            {
                var names = _saved.Select(e => e.Name).ToList();

                if (names.Count == 0) names.Add(_recipe.Name);

                _picker = Ui.Choice(names, _recipe.Name, Switch, 220);

                return Ui.Stack(
                    Ui.Small("A saved extract re-runs against whatever is loaded next revision and "
                             + "takes whatever matches then. Nothing has to be saved for it to work "
                             + "now."),
                    Ui.Form(
                        Ui.Field("Saved", _picker),
                        Ui.Field("Name", _name, "what the files are named after")),
                    Ui.Actions(
                        Ui.Quiet("Save this one", Keep),
                        Ui.Quiet("Forget it", Forget)));
            }

            // -----------------------------------------------------------------------------------
            // Reading
            // -----------------------------------------------------------------------------------

            private void Gather(Job job)
            {
                _scope.Clear();
                _cut = 0;

                var elevations = new List<double>();
                var seen = 0;

                foreach (var item in _session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    seen++;

                    if (!job.Step(_scope.Count, "elements read")) return;

                    if (_scope.Count >= ReadCeiling) { _cut++; continue; }

                    var element = ReportElement.Of(item);

                    if (element == null) continue;

                    _scope.Add(element);

                    if (element.HasGeometry) elevations.Add(element.Bounds.MinZ);
                }

                if (_scope.Count == 0)
                {
                    _read.Children.Add(Ui.Empty("Nothing is loaded.", "Append a model first."));
                    Draw();
                    return;
                }

                job.Say("Working out the levels...");

                _levels = Bands(elevations);

                _read.Children.Add(Ui.Line(
                    Ui.Count(_scope.Count, "element") + ", "
                    + Ui.Count(_levels.Bands.Count, "level") + ", "
                    + Ui.Count(_scope.Select(e => e.Model ?? string.Empty)
                                     .Distinct(StringComparer.OrdinalIgnoreCase).Count(), "model"),
                    1, true));

                if (_levels.Bands.Count > 0) _read.Children.Add(Ui.Small(_levels.Provenance()));

                if (_cut > 0)
                    _read.Children.Add(Ui.Note(
                        "That model holds " + Ui.Count(seen, "element") + ". An extract reads the "
                        + "first " + ReadCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + "; the other " + _cut.ToString("N0", CultureInfo.InvariantCulture)
                        + " were not read."));

                Draw();
            }

            /// <summary>
            /// The levels, from what the models declare where they declare one and from where things
            /// actually sit where they do not.
            /// </summary>
            /// <param name="elevations">The base of everything with geometry.</param>
            private LevelSet Bands(IReadOnlyList<double> elevations)
            {
                var declared = new List<KeyValuePair<string, double>>();

                foreach (var element in _scope)
                {
                    if (!element.HasGeometry) continue;

                    var named = element.Value(new PropertyRef(string.Empty, "Level"))
                                ?? element.Value(new PropertyRef(string.Empty, "Reference Level"));

                    if (string.IsNullOrWhiteSpace(named)) continue;

                    declared.Add(new KeyValuePair<string, double>(named!.Trim(), element.Bounds.MinZ));
                }

                if (declared.Count > 0) return LevelSet.FromModel(declared);

                return elevations.Count > 0 ? LevelSet.Derive(elevations) : LevelSet.Empty;
            }

            // -----------------------------------------------------------------------------------
            // Choosing
            // -----------------------------------------------------------------------------------

            // Extract.Set is not offered here. Navisworks gives a plug-in no way to list its own
            // selection sets by name, so a picker of them would be a picker of nothing - and the
            // useful half of that idea runs the other way: turn what the rule chose INTO a set,
            // which is the button under the verdict.
            private static IReadOnlyList<string> Axes { get; } = new[]
            {
                BySelection, ByViewpoint, ByLevel, ByValue, ByBox, ByModel,
            };

            private void Chose(string word)
            {
                _recipe.Axis = Axis(word);
                Draw();
            }

            private void DrawHow()
            {
                _how.Children.Clear();

                switch (_recipe.Axis)
                {
                    case ExtractAxis.Selection:
                        _how.Children.Add(Ui.Small("Whatever is selected in the model when you press "
                            + "a button below. Saving this rule saves \"the selection\", not the "
                            + "elements, so it means something different next month - which is "
                            + "usually not what a saved extract is for."));
                        break;

                    case ExtractAxis.Viewpoint:
                        Named("Viewpoint", Viewpoints());
                        Look();
                        break;

                    case ExtractAxis.Level:
                        Named("Level", _levels.Bands.Select(b => b.Name).ToList());
                        break;

                    case ExtractAxis.Model:
                        Named("Model", _scope.Select(e => e.Model)
                                             .Where(m => m != null)
                                             .Select(m => m!)
                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                             .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                                             .ToList());
                        break;

                    case ExtractAxis.Where:
                        DrawWhere();
                        break;

                    case ExtractAxis.Box:
                        DrawBox();
                        break;
                }
            }

            private void Named(string label, IReadOnlyList<string> options)
            {
                if (options.Count == 0)
                {
                    _how.Children.Add(Ui.Note("There are no " + label.ToLowerInvariant()
                        + "s to choose from. Read the model first, or pick another way of choosing."));
                    return;
                }

                var chosen = options.Contains(_recipe.Named ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ? _recipe.Named
                    : options[0];

                _recipe.Named = chosen;

                _how.Children.Add(Ui.Form(Ui.Field(label,
                    Ui.Choice(options, chosen, pick => { _recipe.Named = pick; DrawVerdict(); }, 260))));
            }

            /// <summary>
            /// The button that answers "what does that viewpoint show".
            ///
            /// A viewpoint does not carry a list of elements - it carries a camera and a set of
            /// overrides, and what it shows is whatever is left visible once it has been applied.
            /// So the only honest answer is to apply it and look, and that moves the camera, which
            /// is a thing to do when asked rather than every time a dropdown changes.
            /// </summary>
            private void Look()
            {
                if (_showing != null && string.Equals(_showing, _recipe.Named, StringComparison.OrdinalIgnoreCase))
                {
                    _how.Children.Add(Ui.Line(
                        Ui.Count(_shown?.Count ?? 0, "element") + " visible in it", 0.75));
                }

                _how.Children.Add(Ui.Actions(Ui.Quiet("Go to it and see what it shows", Looking)));

                _how.Children.Add(Ui.Small("Applying a viewpoint moves the camera and restores "
                    + "whatever it was hiding, which is the only way to find out what it shows - a "
                    + "saved view holds a camera and a set of overrides, not a list of elements."));
            }

            private void Looking()
            {
                _note.Children.Clear();

                var wanted = _session.Viewpoints.All()
                    .Where(v => string.Equals(v.Name, _recipe.Named, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.Id)
                    .FirstOrDefault();

                if (wanted == null)
                {
                    _note.Children.Add(Ui.Note("There is no viewpoint called \"" + _recipe.Named
                        + "\" any more."));
                    return;
                }

                try
                {
                    _session.Viewpoints.Apply(wanted);
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not go to it: " + Ui.Describe(e)));
                    return;
                }

                _shown = _session.Model.Traverse(TraversalScope.WholeDocument)
                                 .Where(i => i.HasGeometry && !i.IsHidden)
                                 .Select(i => i.Key)
                                 .ToList();

                _showing = _recipe.Named;

                Draw();
            }

            private void DrawWhere()
            {
                var fields = DataReport.Fields(_scope)
                    .Where(f => f.Distinct > 1 && f.Distinct <= 500)
                    .Select(f => f.Category.Length == 0 ? f.Name : f.Category + " / " + f.Name)
                    .ToList();

                if (fields.Count == 0)
                {
                    _how.Children.Add(Ui.Note("Nothing the model carries has few enough different "
                        + "values to cut by. Read the model first, or pick another way of choosing."));
                    return;
                }

                var shown = _recipe.Property.Name.Length == 0
                    ? fields[0]
                    : (_recipe.Property.Category.Length == 0
                        ? _recipe.Property.Name
                        : _recipe.Property.Category + " / " + _recipe.Property.Name);

                if (!fields.Contains(shown, StringComparer.OrdinalIgnoreCase)) shown = fields[0];

                _recipe.Property = Split(shown);

                var values = Ui.Stack();

                _how.Children.Add(Ui.Form(Ui.Field("Property",
                    Ui.Choice(fields, shown, pick =>
                    {
                        _recipe.Property = Split(pick);
                        _recipe.Value = null;
                        DrawHow();
                        DrawVerdict();
                    }, 260))));

                _how.Children.Add(values);

                var choices = Extract.Values(_scope, _recipe.Property).ToList();

                if (choices.Count == 0)
                {
                    values.Children.Add(Ui.Note("Nothing in scope carries a value there."));
                    return;
                }

                var picked = choices.Contains(_recipe.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ? _recipe.Value
                    : choices[0];

                _recipe.Value = picked;

                values.Children.Add(Ui.Form(Ui.Field("Value",
                    Ui.Choice(choices, picked, pick => { _recipe.Value = pick; DrawVerdict(); }, 260),
                    Ui.Count(choices.Count, "value") + " in the model")));
            }

            private void DrawBox()
            {
                var box = _recipe.Box;

                _how.Children.Add(Ui.Line(
                    box == null ? "No box yet." : "A box of " + box + ".", 1, box != null));

                _how.Children.Add(Ui.Form(Ui.Field("An element counts as in when it is",
                    Ui.Choice(new[] { Touching, Wholly, Centred }, Reach(_recipe.Reach), pick =>
                    {
                        _recipe.Reach = Reaching(pick);
                        DrawVerdict();
                    }, 200))));

                _how.Children.Add(Ui.Actions(
                    Ui.Button("Take it from what is selected", () => Box(false)),
                    Ui.Quiet("Take it from everything loaded", () => Box(true))));

                if (box != null)
                    _how.Children.Add(Ui.Actions(
                        Ui.Quiet("Show me the box", () => _session.View.SetSectionBox(
                            new BoundingBox(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ), 0)),
                        Ui.Quiet("Put the section box away",
                                 () => _session.View.SetSectionBoxEnabled(false))));
            }

            private void Box(bool everything)
            {
                var from = everything
                    ? _scope
                    : (IReadOnlyList<ReportElement>)Chosen()
                        .Select(k => _scope.FirstOrDefault(e => e.Key == k))
                        .Where(e => e != null)
                        .Select(e => e!)
                        .ToList();

                _note.Children.Clear();

                var box = ExtractBox.Around(from);

                if (box == null)
                {
                    _note.Children.Add(Ui.Note(everything
                        ? "Nothing loaded has geometry, so there is no box to take."
                        : "Nothing with geometry is selected. Select something in the model first."));
                    return;
                }

                _recipe.Box = box;

                Draw();
            }

            // -----------------------------------------------------------------------------------
            // Drawing
            // -----------------------------------------------------------------------------------

            private void Draw()
            {
                DrawHow();
                DrawVerdict();
            }

            private void DrawVerdict()
            {
                _verdict.Children.Clear();
                _out.Children.Clear();

                if (_scope.Count == 0)
                {
                    _verdict.Children.Add(Ui.Small("Read the model and what the rule takes appears "
                                                   + "here as you change it."));
                    return;
                }

                var plan = Extract.Plan(_scope, _recipe, Reachable(), _levels);

                if (plan.Refusal != null)
                {
                    _verdict.Children.Add(Ui.Note(plan.Refusal));
                    return;
                }

                _verdict.Children.Add(Ui.Line(plan.Explain(), 1, true));

                _verdict.Children.Add(Ui.Across(
                    Ui.Figure(plan.Count.ToString("N0", CultureInfo.InvariantCulture), "elements",
                              plan.Count > 0 ? Tone.Good : Tone.Warn),
                    Ui.Figure(plan.Models.Count.ToString("N0", CultureInfo.InvariantCulture), "models"),
                    Ui.Figure(plan.WithoutGeometry.ToString("N0", CultureInfo.InvariantCulture),
                              "nodes with no body")));

                foreach (var note in plan.Notes) _verdict.Children.Add(Ui.Note(note));

                if (plan.Models.Count > 0)
                    _verdict.Children.Add(Ui.Table(
                        plan.Models.Select(m => new TableRow(m, m.Model,
                            m.Count.ToString("N0", CultureInfo.InvariantCulture))).ToList(),
                        ("Model", 220), ("Elements", 0)));

                if (!plan.IsUsable) return;

                _verdict.Children.Add(Ui.Actions(
                    Ui.Quiet("Select it in the model", () =>
                    {
                        _session.Model.Select(plan.Keys);
                        _session.View.ZoomTo(plan.Keys, 2.0);
                    }),
                    Ui.Quiet("Leave only it on screen", () => Only(plan)),
                    Ui.Quiet("Make it a set", () => AsSet(plan))));

                DrawOut(plan);
            }

            private void DrawOut(ExtractPlan plan)
            {
                _out.Children.Add(Ui.Small("Named after the extract, in the folder above. "
                    + Ui.Count(plan.Count, "element") + " to write."));

                _out.Children.Add(Ui.Across(
                    Ui.Runner("Drawing (.dxf)", _note, job => Drawing(plan, job)),
                    Ui.Runner("Three-D (.glb)", _note, job => Solid(plan, job), primary: false),
                    Ui.Runner("Its data (.xlsx)", _note, job => Data(plan, job, true), primary: false),
                    Ui.Runner("Its data (.csv)", _note, job => Data(plan, job, false), primary: false)));
            }

            /// <summary>
            /// The rule's answer as a selection set in the model.
            ///
            /// The way to an NWD of this piece: a set Navisworks itself understands, which its own
            /// export, its own search and everybody else's plug-in can then work from.
            /// </summary>
            /// <param name="plan">What the rule chose.</param>
            private void AsSet(ExtractPlan plan)
            {
                _note.Children.Clear();

                try
                {
                    var made = _session.Search.PublishKeys(_recipe.Name, plan.Keys);

                    _note.Children.Add(Ui.Done("Made the set \"" + _recipe.Name + "\" with "
                        + Ui.Count(made, "element") + " in it. It is in the Sets window, and "
                        + "Navisworks' own File ▸ Export works from there."));

                    Told(ActivityKind.Setup, "made a set of " + _recipe.Name,
                         Ui.Count(made, "element"));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not make the set: " + Ui.Describe(e)));
                }
            }

            private void Only(ExtractPlan plan)
            {
                var wanted = new HashSet<ElementKey>(plan.Keys);
                var hide = new List<ElementKey>();

                foreach (var element in _scope)
                    if (element.HasGeometry && !wanted.Contains(element.Key)) hide.Add(element.Key);

                _session.Model.Select(plan.Keys);
                _session.View.SetVisible(hide, false);
                _session.View.SetVisible(plan.Keys, true);
                _session.View.ZoomTo(plan.Keys, 2.0);
            }

            // -----------------------------------------------------------------------------------
            // Writing
            // -----------------------------------------------------------------------------------

            private void Drawing(ExtractPlan plan, Job job)
            {
                var file = Where(".dxf");
                if (file == null) return;

                var geometry = new NavGeometry(_session.Model);
                var solids = new List<DxfSolid>();
                var silent = 0;
                var read = 0;

                job.Say("Reading geometry...");

                foreach (var key in plan.Keys)
                {
                    if (!job.Step(read, plan.Keys.Count, "elements read")) return;

                    if (read >= DrawCeiling) break;

                    read++;

                    var item = _session.Model.Resolve(key);

                    if (item == null || !item.HasGeometry) continue;

                    var mesh = geometry.Read(item, item.DisplayName);

                    if (mesh == null) { silent++; continue; }

                    var solid = DxfSolid.From(mesh);

                    if (solid == null) continue;

                    // The layer is the thing that makes it a drawing rather than a mesh: nobody can
                    // freeze the ductwork if it is all on layer 0.
                    solid.Layer = item.Category ?? item.Model.DisplayName;

                    foreach (var reading in item.Properties().Take(20))
                        solid.Data.Add(new KeyValuePair<string, string>(
                            reading.Name, reading.Value ?? string.Empty));

                    solids.Add(solid);
                }

                job.Say("Writing the drawing...");

                var written = DxfWriter.Write(solids, _session.Profile.ProjectName);

                if (!Put(file, () => File.WriteAllText(file, written.Text, new UTF8Encoding(false)))) return;

                _note.Children.Add(Ui.Line(written.Explain(), 1, true));
                _note.Children.Add(Ui.Done("Written to " + file + "."));

                foreach (var note in written.Notes) _note.Children.Add(Ui.Note(note));

                if (silent > 0)
                    _note.Children.Add(Ui.Note(Ui.Count(silent, "element")
                        + " has geometry the drawing layer would not give triangles for."));

                if (read >= DrawCeiling && plan.Count > DrawCeiling)
                    _note.Children.Add(Ui.Note("A drawing takes the first "
                        + DrawCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + " elements. Cut the extract smaller for the rest."));

                Told(ActivityKind.Export, "drew " + _recipe.Name, written.Explain());
            }

            private void Solid(ExtractPlan plan, Job job)
            {
                var file = Where(".glb");
                if (file == null) return;

                var geometry = new NavGeometry(_session.Model);
                var meshes = new List<GltfMesh>();
                var read = 0;

                job.Say("Reading geometry...");

                foreach (var key in plan.Keys)
                {
                    if (!job.Step(read, plan.Keys.Count, "elements read")) return;

                    if (read >= DrawCeiling) break;

                    read++;

                    var item = _session.Model.Resolve(key);

                    if (item == null || !item.HasGeometry) continue;

                    var mesh = geometry.Read(item, item.DisplayName);

                    if (mesh != null) meshes.Add(mesh);
                }

                job.Say("Writing the file...");

                var written = GltfWriter.Write(meshes, _recipe.Name);

                if (!Put(file, () => File.WriteAllBytes(file, written.Bytes))) return;

                _note.Children.Add(Ui.Line(written.Explain(), 1, true));
                _note.Children.Add(Ui.Done("Written to " + file + "."));

                Told(ActivityKind.Export, "exported " + _recipe.Name, written.Explain());
            }

            private void Data(ExtractPlan plan, Job job, bool book)
            {
                var file = Where(book ? ".xlsx" : ".csv");
                if (file == null) return;

                job.Say("Building the sheet...");

                var wanted = new HashSet<ElementKey>(plan.Keys);
                var inside = _scope.Where(e => wanted.Contains(e.Key)).ToList();

                var template = DataReport.Propose(inside);
                template.Name = _recipe.Name;

                var table = DataReport.Build(inside, template, _session.Profile.Units);

                if (table.Refusal != null)
                {
                    _note.Children.Add(Ui.Problem(table.Refusal));
                    return;
                }

                if (!Put(file, () =>
                {
                    if (book) File.WriteAllBytes(file, XlsxWriter.Write(DataReport.Workbook(table, template)));
                    else File.WriteAllText(file, DataReport.Csv(table), new UTF8Encoding(true));
                })) return;

                _note.Children.Add(Ui.Line(table.Explain(), 1, true));
                _note.Children.Add(Ui.Done("Written to " + file + "."));

                Told(ActivityKind.Export, "exported the data for " + _recipe.Name, table.Explain());
            }

            /// <summary>Where a file goes, or null having already said why it cannot go anywhere.</summary>
            /// <param name="extension">The extension, with its dot.</param>
            private string? Where(string extension)
            {
                _note.Children.Clear();

                var folder = _folder.Text.Trim();

                if (folder.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which folder the files go in first."));
                    return null;
                }

                _kept.Set("folder", folder);
                _kept.Keep();

                return Path.Combine(folder, Extract.FileName(_recipe, extension));
            }

            private bool Put(string file, Action write)
            {
                try
                {
                    write();
                    return true;
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return false;
                }
            }

            private void Told(string kind, string what, string detail) =>
                _session.Record(kind, what, detail + " — " + _recipe.Describe());

            // -----------------------------------------------------------------------------------
            // What the host knows
            // -----------------------------------------------------------------------------------

            private IReadOnlyCollection<ElementKey>? Reachable()
            {
                switch (_recipe.Axis)
                {
                    case ExtractAxis.Selection:
                        return Chosen();

                    case ExtractAxis.Viewpoint:
                        return _showing != null
                            && string.Equals(_showing, _recipe.Named, StringComparison.OrdinalIgnoreCase)
                            ? _shown
                            : null;

                    default:
                        return null;
                }
            }

            private IReadOnlyList<ElementKey> Chosen() => _session.Model.SelectedKeys();

            private IReadOnlyList<string> Viewpoints() =>
                _session.Viewpoints.All()
                        .Select(v => v.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

            // -----------------------------------------------------------------------------------
            // Saving
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                _saved.Clear();
                _saved.AddRange(Extract.Saved(_session.Store.Section(ProjectStore.DataSection)[Key]));

                var last = _kept.Text("extract");

                var wanted = last == null
                    ? _saved.FirstOrDefault()
                    : _saved.FirstOrDefault(e => string.Equals(e.Name, last, StringComparison.OrdinalIgnoreCase));

                if (wanted != null) _recipe = wanted.Copy();
            }

            private void Switch(string name)
            {
                var wanted = _saved.FirstOrDefault(
                    e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _recipe = wanted.Copy();
                _name.Text = _recipe.Name;

                _kept.Set("extract", _recipe.Name);
                _kept.Keep();

                _note.Children.Clear();

                Draw();
            }

            private void Keep()
            {
                _note.Children.Clear();

                var name = _name.Text.Trim();

                _recipe.Name = name.Length == 0 ? "Extract" : name;

                var written = Extract.Keep(_saved, _recipe);

                _session.Store.Section(ProjectStore.DataSection).Set(Key, written);

                _saved.Clear();
                _saved.AddRange(Extract.Saved(written));

                _kept.Set("extract", _recipe.Name);
                _kept.Keep();

                _session.Record(ActivityKind.Setup, "Saved the extract \"" + _recipe.Name + "\"",
                                _recipe.Describe());

                _note.Children.Add(Ui.Done("Saved \"" + _recipe.Name + "\" — " + _recipe.Describe()
                    + ". It takes whatever matches next revision."));

                Repick();
            }

            private void Forget()
            {
                _note.Children.Clear();

                var name = _recipe.Name;

                var left = _saved
                    .Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (left.Count == _saved.Count)
                {
                    _note.Children.Add(Ui.Note("\"" + name + "\" was never saved, so there is "
                                               + "nothing to forget."));
                    return;
                }

                _session.Store.Section(ProjectStore.DataSection)
                        .Set(Key, JsonValue.Array(left.Select(e => e.ToJson())));

                _saved.Clear();
                _saved.AddRange(left);

                _note.Children.Add(Ui.Done("Forgot \"" + name + "\". It is still open here."));

                Repick();
            }

            private void Repick()
            {
                var picker = _picker;
                if (picker == null) return;

                picker.Items.Clear();

                foreach (var recipe in _saved) picker.Items.Add(recipe.Name);

                if (_saved.Count == 0) picker.Items.Add(_recipe.Name);

                picker.SelectedItem = _recipe.Name;
            }

            // -----------------------------------------------------------------------------------
            // Words
            // -----------------------------------------------------------------------------------

            private static string Word(ExtractAxis axis) => axis switch
            {
                ExtractAxis.Viewpoint => ByViewpoint,
                ExtractAxis.Level => ByLevel,
                ExtractAxis.Where => ByValue,
                ExtractAxis.Box => ByBox,
                ExtractAxis.Model => ByModel,
                _ => BySelection,
            };

            private static ExtractAxis Axis(string word) => word switch
            {
                ByViewpoint => ExtractAxis.Viewpoint,
                ByLevel => ExtractAxis.Level,
                ByValue => ExtractAxis.Where,
                ByBox => ExtractAxis.Box,
                ByModel => ExtractAxis.Model,
                _ => ExtractAxis.Selection,
            };

            private static string Reach(BoxReach reach) => reach switch
            {
                BoxReach.Inside => Wholly,
                BoxReach.Centre => Centred,
                _ => Touching,
            };

            private static BoxReach Reaching(string word) => word switch
            {
                Wholly => BoxReach.Inside,
                Centred => BoxReach.Centre,
                _ => BoxReach.Touching,
            };

            private static PropertyRef Split(string? text)
            {
                var whole = (text ?? string.Empty).Trim();
                var slash = whole.IndexOf('/');

                return slash < 0
                    ? new PropertyRef(string.Empty, whole)
                    : new PropertyRef(whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
            }
        }
    }
}
