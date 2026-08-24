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

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The three places the model meets something that is not the model: a crane, a total station,
    /// and a folder of paperwork.
    ///
    /// Everything else in CamelWorks ends at a file somebody opens on a laptop. These end on site.
    /// Where the weight of a module sits decides where the hook goes; the setting-out file is what
    /// the engineer walks the floor with; and the O&amp;M folder is the thing a commissioning engineer
    /// asks for and nobody can find.
    ///
    /// One window because they share the read: all three work off the current selection, and
    /// reading a federation's geometry three times to answer three questions about the same forty
    /// elements is how a tool earns a reputation for being slow.
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
        private const string ByVolume = "the volume of the shapes";

        private static Window? _open;

        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            if (_open != null)
            {
                _open.Activate();
                return null;
            }

            var window = new Window
            {
                Title = "CamelWorks - the model on site",
                Width = 1160,
                Height = 800,
                MinWidth = 900,
                MinHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = new Site(session).View,
            };

            window.Closed += (_, __) => _open = null;

            _open = window;
            window.Show();

            return null;
        }

        private sealed class Site
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            private readonly List<ReportElement> _scope = new List<ReportElement>();
            private readonly List<AttachmentRule> _rules = new List<AttachmentRule>();

            private readonly StackPanel _read = Ui.Stack();
            private readonly StackPanel _weight = Ui.Stack();
            private readonly StackPanel _points = Ui.Stack();
            private readonly StackPanel _paper = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            private readonly TextBox _folder;
            private readonly TextBox _root;

            private SetOutRecipe _recipe = new SetOutRecipe();
            private MassResult? _mass;
            private AttachmentReport? _found;
            private string _basis;
            private PropertyRef _massProperty;
            private int _cut;

            internal Site(Session session)
            {
                _session = session;
                _kept = ToolMemory.Of(session.Store, "field.site");

                _basis = _kept.Text("basis", ByWeight) ?? ByWeight;

                _folder = Ui.Text(_kept.Text("folder"), "where the files go",
                                  text => _kept.Set("folder", text), 260);

                _root = Ui.Text(_kept.Text("root"), "a folder relative paths are taken from",
                                text => _kept.Set("root", text), 260);

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            private UIElement Build()
            {
                var columns = new Grid();
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var left = Ui.Scroll(Ui.Stack(
                    Ui.Eyebrow("What is on site"),
                    Ui.Small("All three answers come off what is selected in the model. Select the "
                             + "module, the lift, the floor - and read it once."),
                    Ui.Runner("Read the selection", _read, Gather),
                    _read,
                    Ui.Rule(),
                    Ui.Eyebrow("Where the files go"),
                    Ui.PathField("Folder", _folder, PathKind.Folder),
                    _note));

                Grid.SetColumn(left, 0);
                columns.Children.Add(left);

                var right = Ui.Scroll(Ui.Stack(
                    Ui.Marked(Glyphs.Layers, "Weight and balance",
                        "Where the hook goes. Weighted by what the model says things weigh, or by "
                        + "the volume of the shapes when it says nothing - and it tells you which.",
                        _weight),
                    Ui.Marked(Glyphs.Camera, "Setting out",
                        "The file the engineer loads into a total station. Site coordinates, "
                        + "northing before easting, with what they are referenced to written into "
                        + "the file itself.",
                        _points),
                    Ui.Marked(Glyphs.Save, "The paperwork",
                        "The folder of data sheets, the folder of site photos, the address into a "
                        + "document system - found from whatever property the model already uses "
                        + "to say where they are.",
                        _paper)));

                Grid.SetColumn(right, 1);
                columns.Children.Add(right);

                Draw();

                return columns;
            }

            // -----------------------------------------------------------------------------------
            // Reading
            // -----------------------------------------------------------------------------------

            private void Gather(Job job)
            {
                _scope.Clear();
                _cut = 0;
                _mass = null;
                _found = null;

                var seen = 0;

                foreach (var item in _session.Model.Traverse(TraversalScope.CurrentSelection))
                {
                    seen++;

                    if (!job.Step(_scope.Count, "elements read")) return;

                    if (_scope.Count >= ReadCeiling) { _cut++; continue; }

                    var element = ReportElement.Of(item);

                    if (element != null) _scope.Add(element);
                }

                if (_scope.Count == 0)
                {
                    _read.Children.Add(Ui.Empty("Nothing is selected.",
                        "Select what is going on site - a module, a lift, a run of pipework - and "
                        + "read it again."));

                    Draw();
                    return;
                }

                _read.Children.Add(Ui.Line(
                    Ui.Count(_scope.Count, "element") + ", "
                    + Ui.Count(_scope.Count(e => e.HasGeometry), "with a body"), 1, true));

                if (_cut > 0)
                    _read.Children.Add(Ui.Note("That selection holds " + Ui.Count(seen, "element")
                        + ". The first " + ReadCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + " were read."));

                Draw();
            }

            private void Draw()
            {
                DrawWeight();
                DrawPoints();
                DrawPaper();
            }

            // -----------------------------------------------------------------------------------
            // Weight and balance
            // -----------------------------------------------------------------------------------

            private void DrawWeight()
            {
                _weight.Children.Clear();

                if (_scope.Count == 0)
                {
                    _weight.Children.Add(Ui.Small("Read a selection first."));
                    return;
                }

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

                _weight.Children.Add(Ui.Form(
                    Ui.Field("Weight it by",
                        Ui.Choice(new[] { ByWeight, ByVolume }, _basis, pick =>
                        {
                            _basis = pick;
                            _kept.Set("basis", pick);
                        }, 240)),
                    Ui.Field("The mass is in", picker,
                             fields.Count == 0
                                 ? "nothing in this selection looks like a mass"
                                 : "a property holding kg, t or lb - or a bare number, taken as kg")));

                _weight.Children.Add(Ui.Runner("Work out where it sits", _weight, Weigh));
            }

            private void Weigh(Job job)
            {
                var geometry = new NavGeometry(_session.Model);
                var parts = new List<MassPart>();
                var read = 0;
                var silent = 0;

                job.Say("Reading geometry...");

                foreach (var element in _scope)
                {
                    if (!job.Step(read++, _scope.Count, "elements measured")) return;

                    var item = _session.Model.Resolve(element.Key);

                    if (item == null) continue;

                    if (!item.HasGeometry)
                    {
                        parts.Add(new MassPart(element.Key, element.Name, null, null));
                        continue;
                    }

                    var mesh = geometry.Read(item, element.Name);

                    if (mesh == null)
                    {
                        silent++;
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
                    Ui.Button("Copy the position", () => Copy(
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
            // Setting out
            // -----------------------------------------------------------------------------------

            private void DrawPoints()
            {
                _points.Children.Clear();

                if (_scope.Count == 0)
                {
                    _points.Children.Add(Ui.Small("Read a selection first."));
                    return;
                }

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

                _points.Children.Add(Ui.Form(
                    Ui.Field("Point numbers start with",
                        Ui.Text(_recipe.Prefix, text => _recipe.Prefix =
                            string.IsNullOrWhiteSpace(text) ? "SO" : text.Trim(), 100)),
                    Ui.Field("Take", kinds)));

                _points.Children.Add(Ui.Section("Where the site grid is",
                    _recipe.IsUnshifted ? "not set — these will be model coordinates"
                                        : "offset and angle set",
                    _recipe.IsUnshifted,
                    Ui.Small("The survey offset every job has and nobody writes down. Wrong, and "
                             + "every point is wrong by the same amount - the one kind of error a "
                             + "setting-out file can carry that nobody notices until the steel "
                             + "does not fit."),
                    Ui.Form(
                        Ui.Field("Model origin is at northing",
                            Number(_recipe.NorthAtOrigin, v => _recipe.NorthAtOrigin = v)),
                        Ui.Field("...and easting",
                            Number(_recipe.EastAtOrigin, v => _recipe.EastAtOrigin = v)),
                        Ui.Field("...and level",
                            Number(_recipe.LevelAtOrigin, v => _recipe.LevelAtOrigin = v)),
                        Ui.Field("Project angle, clockwise",
                            Number(_recipe.RotationDegrees, v => _recipe.RotationDegrees = v),
                            "degrees from grid north, the way it is written on the drawings"))));

                _points.Children.Add(Ui.Runner("Take the points", _points, Points));
            }

            private void Points(Job job)
            {
                var elements = _scope.Select(e => new SetOutElement(
                    e.Key, e.Name, e.Bounds, e.HasGeometry, Mark(e))).ToList();

                var result = SettingOut.Of(elements, _recipe);

                if (result.Refusal != null)
                {
                    _points.Children.Add(Ui.Problem(result.Refusal));
                    return;
                }

                _points.Children.Add(Ui.Line(result.Explain(), 1, true));

                foreach (var note in result.Notes) _points.Children.Add(Ui.Note(note));

                _points.Children.Add(Ui.Table(
                    result.Points.Take(Shown).Select(p => new TableRow(p,
                        p.Number,
                        p.Northing.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Easting.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Level.ToString("0.###", CultureInfo.InvariantCulture),
                        p.Description)).ToList(),
                    ("Point", 80), ("Northing", 100), ("Easting", 100), ("Level", 90), ("What", 0)));

                _points.Children.Add(Ui.Tally(Math.Min(Shown, result.Points.Count),
                                              result.Points.Count, "point"));

                _points.Children.Add(Ui.Actions(
                    Ui.Button("Write the file", () => WritePoints(result)),
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
            // The paperwork
            // -----------------------------------------------------------------------------------

            private void DrawPaper()
            {
                _paper.Children.Clear();

                if (_scope.Count == 0)
                {
                    _paper.Children.Add(Ui.Small("Read a selection first."));
                    return;
                }

                foreach (var kind in new[] { AttachmentKind.Document, AttachmentKind.Photo })
                {
                    var here = kind;
                    var options = Attachments.Likely(_scope, here).Select(Show).ToList();

                    if (options.Count == 0)
                    {
                        _paper.Children.Add(Ui.Small("Nothing in this selection looks like it says "
                            + "where the " + (here == AttachmentKind.Photo ? "photos" : "documents")
                            + " are."));

                        continue;
                    }

                    var rule = _rules.FirstOrDefault(r => r.Kind == here);

                    var shown = rule == null ? options[0] : Show(rule.Property);

                    if (!options.Contains(shown, StringComparer.OrdinalIgnoreCase)) shown = options[0];

                    Remember(here, Split(shown));

                    _paper.Children.Add(Ui.Form(Ui.Field(
                        here == AttachmentKind.Photo ? "Photos are named by" : "Documents are named by",
                        Ui.Choice(options, shown, pick => Remember(here, Split(pick)), 260))));
                }

                _paper.Children.Add(Ui.Form(Ui.Field("Relative paths come from", _root,
                    "so a job that moves between machines does not lose every link")));

                _paper.Children.Add(Ui.Runner("Find what is attached", _paper, Paper));
            }

            private void Paper(Job job)
            {
                var root = _root.Text.Trim();

                foreach (var rule in _rules) rule.Root = root.Length == 0 ? null : root;

                job.Say("Looking...");

                var report = Attachments.Find(_scope, _rules, PhysicalFileSystem.Instance);

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
                    _paper.Children.Add(Ui.Section("Folders that are there and empty",
                        Ui.Count(report.EmptyFolders.Count, "folder"), false,
                        Ui.Table(report.EmptyFolders.Take(Shown)
                                     .Select(f => new TableRow(f, f)).ToList(),
                                 ("Folder", 0))));
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

                var names = _scope.ToDictionary(e => e.Key, e => e.Name);

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
                DataReport.Fields(_scope)
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
