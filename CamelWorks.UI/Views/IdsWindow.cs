using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The requirements a project sets for itself, written down as a file everybody's tool reads.
    ///
    /// We could answer a client's IDS and could not write one, which made this half a tool. What a
    /// job actually argues about all week - which property has to be filled in, on what, before an
    /// issue goes out - lived in an email and got enforced by eye. An IDS is where that belongs,
    /// and once it is one, the same file checks the model, travels to the architect and comes back
    /// with the next revision.
    ///
    /// A window because a specification is a form with two lists in it, and because writing five of
    /// them is a sitting rather than a click.
    /// </summary>
    public static class IdsWindow
    {
        /// <summary>Where the draft lives in the project file, as IDS itself.</summary>
        public const string Key = "idsDraft";

        /// <summary>
        /// This project's own rules, both halves, as one document to check against.
        ///
        /// <b>Why the check cannot just open a file.</b> A rule scoped to one of this model's own
        /// sets has no place in an IDS file, so a rule set written here is only ever partly a
        /// file. A check that read files and nothing else could never run the very rules this
        /// window exists to write - the feature would be decorative. So the run reads the project,
        /// where both halves are, and the file path stays for a client's IDS.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="problem">Why there is no rule set, or null.</param>
        public static IdsDocument? Draft(Session session, out string? problem)
        {
            problem = null;

            if (session == null) { problem = "There is no open document."; return null; }

            var section = session.Store.Section(ProjectStore.ProfileSection);
            var document = new IdsDocument("This project's rules");

            var stored = section[Key].AsString();

            if (!string.IsNullOrWhiteSpace(stored))
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stored!));

                var read = IdsReader.Read(stream);

                if (read.Document != null) document = read.Document;
            }

            foreach (var specification in IdsJson.ReadAll(section[ScopedKey]))
                document.Specifications.Add(specification);

            if (document.Specifications.Count == 0)
            {
                problem = "This project has no rules of its own yet. Write them in Model Audit's "
                          + "IDS tab, or point this at a rule file somebody sent.";

                return null;
            }

            return document;
        }

        /// <summary>
        /// Where the rules an IDS file cannot hold live, beside the draft.
        ///
        /// <b>Two keys, and no rule in both.</b> The draft is kept as IDS because the format is
        /// the point - what the project holds is already a file another tool can read - and a rule
        /// scoped to one of this model's own sets has no place in that format. Writing only the
        /// portable half would lose the rest the next time somebody opened the window, which is
        /// silent data loss and the one outcome worth any amount of code to avoid. So the scoped
        /// rules are kept here, in this window's own shape, and the two halves are read back
        /// together.
        /// </summary>
        public const string ScopedKey = "idsScopedRules";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Editor(session).View;

        // ---------------------------------------------------------------------------------------
        // What one facet row holds while it is being edited
        // ---------------------------------------------------------------------------------------

        private const string Entity = "IFC class";
        private const string Property = "Property";
        private const string Attribute = "Attribute";
        private const string Classification = "Classification";
        private const string Material = "Material";
        private const string PartOf = "Part of";

        /// <summary>Saying who a rule is about in this model's terms rather than IFC's.</summary>
        private const string Scoped = "Set in this model";

        /// <summary>What the set box offers for "whatever is picked when this runs".</summary>
        private const string TheSelection = "What is selected when it runs";

        private const string Present = "just has to be there";
        private const string Exactly = "is exactly";
        private const string OneOf = "is one of";
        private const string Matches = "matches a pattern";
        private const string Between = "is between";

        private const string Required = "required";
        private const string Optional = "optional";
        private const string Prohibited = "must not be there";

        /// <summary>One row of the two lists, as the boxes hold it before it becomes a facet.</summary>
        private sealed class Row
        {
            internal string Kind { get; set; } = Property;

            /// <summary>The first name box: property set, attribute name, system, IFC class.</summary>
            internal string First { get; set; } = string.Empty;

            /// <summary>The second name box: the property's own name. Unused by the rest.</summary>
            internal string Second { get; set; } = string.Empty;

            internal string Test { get; set; } = Present;

            internal string Value { get; set; } = string.Empty;

            internal string Upper { get; set; } = string.Empty;

            internal string Cardinality { get; set; } = Required;

            internal string DataType { get; set; } = string.Empty;

            internal string Relation { get; set; } = string.Empty;

            internal string Instructions { get; set; } = string.Empty;

            /// <summary>Whether this kind of facet has a second name box.</summary>
            internal bool HasSecond => Kind == Property;

            /// <summary>Whether this row says who the rule is about in the model's own terms.</summary>
            internal bool IsScope => Kind == Scoped;

            /// <summary>
            /// Whether this kind of facet asks a separate question about a value.
            ///
            /// A material does not: the box is the material, so a value picker beside it would be
            /// asking the same thing twice.
            /// </summary>
            internal bool HasValue => Kind != PartOf && Kind != Material && Kind != Scoped;

            /// <summary>What the first box is called, which is different for every kind.</summary>
            internal string FirstLabel => Kind switch
            {
                Scoped => "Set",
                Entity => "IFC class",
                Property => "Property tab",
                Attribute => "Attribute",
                Classification => "System",
                Material => "Material",
                _ => "IFC class it is part of",
            };

            /// <summary>Turn it into a facet, or say what is missing.</summary>
            internal IdsFacet? Build(bool requirement, out string? problem)
            {
                problem = null;

                var first = First.Trim();
                var second = Second.Trim();

                if (Kind == Scoped)
                {
                    if (string.Equals(first, TheSelection, StringComparison.Ordinal))
                    {
                        problem = null;
                        return new IdsScopeFacet(IdsScope.Selected);
                    }

                    var scope = IdsScope.Of(first);

                    if (scope == null)
                    {
                        problem = "A set row does not say which set. Pick one, or pick \""
                                  + TheSelection + "\".";

                        return null;
                    }

                    return new IdsScopeFacet(scope);
                }

                if (first.Length == 0 && Kind != Material)
                {
                    problem = "A " + Kind.ToLowerInvariant() + " row has no " +
                              FirstLabel.ToLowerInvariant() + " in it.";
                    return null;
                }

                if (Kind == Property && second.Length == 0)
                {
                    problem = "\"" + first + "\" says which tab but not which property.";
                    return null;
                }

                var value = Read(out var wrong);

                if (wrong != null) { problem = wrong; return null; }

                IdsFacet facet;

                switch (Kind)
                {
                    case Entity:
                        facet = new IdsEntityFacet(IdsValue.Simple(first));
                        break;

                    case Attribute:
                        facet = new IdsAttributeFacet(IdsValue.Simple(first), value);
                        break;

                    case Classification:
                        facet = new IdsClassificationFacet(IdsValue.Simple(first), value);
                        break;

                    case Material:
                        facet = new IdsMaterialFacet(value ?? (first.Length == 0
                            ? null
                            : IdsValue.Simple(first)));
                        break;

                    case PartOf:
                        facet = new IdsPartOfFacet(IdsValue.Simple(first),
                                                   Relation.Trim().Length == 0 ? null : Relation.Trim());
                        break;

                    default:
                        facet = new IdsPropertyFacet(IdsValue.Simple(first), IdsValue.Simple(second),
                                                     value,
                                                     DataType.Trim().Length == 0 ? null : DataType.Trim());
                        break;
                }

                if (requirement) facet.Cardinality = Card(Cardinality);

                if (Instructions.Trim().Length > 0) facet.Instructions = Instructions.Trim();

                return facet;
            }

            /// <summary>The value the test asks for, or null when the test is "just be there".</summary>
            private IdsValue? Read(out string? problem)
            {
                problem = null;

                var text = Value.Trim();

                switch (Test)
                {
                    case Present:
                        return null;

                    case Exactly:
                        if (text.Length == 0) { problem = Empty("a value"); return null; }
                        return IdsValue.Simple(text);

                    case OneOf:
                        var values = text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(v => v.Trim())
                                         .Where(v => v.Length > 0)
                                         .ToList();

                        if (values.Count == 0) { problem = Empty("some values"); return null; }
                        return IdsValue.Restriction(enumeration: values);

                    case Matches:
                        if (text.Length == 0) { problem = Empty("a pattern"); return null; }

                        try
                        {
                            return IdsValue.Restriction(pattern: text);
                        }
                        catch (ArgumentException e)
                        {
                            problem = "\"" + text + "\" is not a pattern: " + e.Message;
                            return null;
                        }

                    default:
                        var lower = Parse(text);
                        var upper = Parse(Upper);

                        if (lower == null && upper == null)
                        {
                            problem = Empty("a number at one end at least");
                            return null;
                        }

                        if (lower != null && upper != null && lower > upper)
                        {
                            problem = "The lower bound is above the upper one, so nothing could "
                                      + "satisfy it.";
                            return null;
                        }

                        return IdsValue.Restriction(minInclusive: lower, maxInclusive: upper);
                }
            }

            private string Empty(string what) =>
                "\"" + (First.Trim().Length == 0 ? Kind : First.Trim())
                + "\" is set to \"" + Test + "\" and needs " + what + ".";

            private static double? Parse(string text) =>
                double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                out var value)
                    ? value
                    : (double?)null;

            private static IdsCardinality Card(string word) =>
                word == Prohibited ? IdsCardinality.Prohibited
                : word == Optional ? IdsCardinality.Optional
                : IdsCardinality.Required;
        }

        /// <summary>One specification, as the boxes hold it.</summary>
        private sealed class Spec
        {
            internal string Name { get; set; } = "New specification";

            internal string Identifier { get; set; } = string.Empty;

            internal string Description { get; set; } = string.Empty;

            internal string Instructions { get; set; } = string.Empty;

            internal int MinOccurs { get; set; }

            internal int? MaxOccurs { get; set; }

            internal ISet<string> Versions { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IFC4" };

            internal IList<Row> Applies { get; } = new List<Row>();

            internal IList<Row> Wants { get; } = new List<Row>();

            /// <summary>
            /// Turn this rule into a specification, or return null and say what stopped it.
            /// </summary>
            /// <param name="refused">Where the reasons go, one per row that would not build.</param>
            internal IdsSpecification? Build(IList<string> refused)
            {
                var built = new IdsSpecification(string.IsNullOrWhiteSpace(Name) ? "Untitled" : Name)
                {
                    Identifier = Identifier.Trim().Length == 0 ? null : Identifier.Trim(),
                    Description = Description.Trim().Length == 0 ? null : Description.Trim(),
                    Instructions = Instructions.Trim().Length == 0 ? null : Instructions.Trim(),
                    MinOccurs = MinOccurs,
                    MaxOccurs = MaxOccurs,
                };

                foreach (var version in Versions) built.IfcVersions.Add(version);

                foreach (var row in Applies)
                {
                    var facet = row.Build(requirement: false, out var why);

                    if (facet == null) { refused.Add(why ?? "a row would not build"); return null; }

                    built.Applicability.Add(facet);
                }

                foreach (var row in Wants)
                {
                    var facet = row.Build(requirement: true, out var why);

                    if (facet == null) { refused.Add(why ?? "a row would not build"); return null; }

                    built.Requirements.Add(facet);
                }

                return built.Applicability.Count == 0 ? null : built;
            }

            /// <inheritdoc />
            public override string ToString() => Name;
        }

        private sealed class Editor
        {
            private readonly Session _session;

            private readonly List<Spec> _specs = new List<Spec>();

            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _detail = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _preview = Ui.Stack();

            private readonly TextBox _title = Ui.Text("Information requirements", null, 240);
            private readonly TextBox _version = Ui.Text("1.0", null, 100);
            private readonly TextBox _author = Ui.Text(string.Empty, "somebody@example.com", null, 200);
            private readonly TextBox _about = Ui.Text(string.Empty, "what it is for", null, 240);

            private Spec? _chosen;

            /// <summary>What the two save buttons cost, said before either is pressed.</summary>
            private readonly StackPanel _saveSays = Ui.Stack();

            internal Editor(Session session)
            {
                _session = session;

                Load();

                _chosen = _specs.FirstOrDefault();
                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Starting from something
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// The draft this project already has, or the specification everybody writes first.
            ///
            /// Kept as IDS rather than as some private shape of our own: the format is the point,
            /// and a draft in it is a draft another tool can already read.
            /// </summary>
            private void Load()
            {
                var section = _session.Store.Section(ProjectStore.ProfileSection);
                var stored = section[Key].AsString();
                var found = false;

                if (!string.IsNullOrWhiteSpace(stored))
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(stored!)))
                    {
                        var read = IdsReader.Read(stream);

                        if (read.Document != null && read.Document.Specifications.Count > 0)
                        {
                            Adopt(read.Document);
                            found = true;
                        }
                    }
                }

                // The other half: the rules no IDS file can hold. Read after the portable ones so
                // they keep the order somebody put them in, and never instead of them.
                var scoped = IdsJson.ReadAll(section[ScopedKey]);

                if (scoped.Count > 0)
                {
                    var extra = new IdsDocument(_title.Text);

                    foreach (var specification in scoped) extra.Specifications.Add(specification);

                    Adopt(extra, keepTitle: true);
                    found = true;
                }

                if (found) return;

                var starter = new Spec { Name = "Walls carry a fire rating" };

                starter.Applies.Add(new Row { Kind = Entity, First = "IFCWALL", Test = Present });

                starter.Wants.Add(new Row
                {
                    Kind = Property,
                    First = "Pset_WallCommon",
                    Second = "FireRating",
                    Test = OneOf,
                    Value = "30, 60, 120",
                    DataType = "IFCLABEL",
                });

                _specs.Add(starter);
            }

            /// <summary>Turn a document that was read back into rows somebody can edit.</summary>
            /// <param name="document">What to turn into rows.</param>
            /// <param name="keepTitle">
            /// True when these rules are the second half of a draft, so the title and author the
            /// first half carried are the document's and must not be overwritten by a container.
            /// </param>
            private void Adopt(IdsDocument document, bool keepTitle = false)
            {
                if (!keepTitle)
                {
                    _title.Text = document.Title;
                    _version.Text = document.Version ?? string.Empty;
                    _author.Text = document.Author ?? string.Empty;
                    _about.Text = document.Description ?? string.Empty;
                }

                foreach (var spec in document.Specifications)
                {
                    var here = new Spec
                    {
                        Name = spec.Name,
                        Identifier = spec.Identifier ?? string.Empty,
                        Description = spec.Description ?? string.Empty,
                        Instructions = spec.Instructions ?? string.Empty,
                        MinOccurs = spec.MinOccurs,
                        MaxOccurs = spec.MaxOccurs,
                    };

                    here.Versions.Clear();

                    foreach (var version in spec.IfcVersions) here.Versions.Add(version);

                    if (here.Versions.Count == 0) here.Versions.Add(IdsWriter.DefaultIfcVersion);

                    foreach (var facet in spec.Applicability) here.Applies.Add(From(facet));
                    foreach (var facet in spec.Requirements) here.Wants.Add(From(facet));

                    _specs.Add(here);
                }
            }

            /// <summary>
            /// A facet as boxes.
            ///
            /// Lossy on purpose where the boxes cannot hold what the file did - a pattern on a
            /// property set, a length restriction - and the loss is visible rather than silent:
            /// the row comes back as "just has to be there" with the old text beside it, so
            /// somebody can see what it was and decide.
            /// </summary>
            private static Row From(IdsFacet facet)
            {
                var row = new Row
                {
                    Cardinality = facet.Cardinality == IdsCardinality.Prohibited ? Prohibited
                                : facet.Cardinality == IdsCardinality.Optional ? Optional
                                : Required,
                    Instructions = facet.Instructions ?? string.Empty,
                };

                switch (facet)
                {
                    case IdsScopeFacet scoped:
                        row.Kind = Scoped;
                        row.First = scoped.Scope.Kind == IdsScopeKind.Selection
                            ? TheSelection
                            : scoped.Scope.Name;
                        break;

                    case IdsEntityFacet entity:
                        row.Kind = Entity;
                        row.First = entity.Name.Literal ?? entity.Name.Description;
                        break;

                    case IdsAttributeFacet attribute:
                        row.Kind = Attribute;
                        row.First = attribute.Name.Literal ?? attribute.Name.Description;
                        Fill(row, attribute.Value);
                        break;

                    case IdsPropertyFacet property:
                        row.Kind = Property;
                        row.First = property.PropertySet.Literal ?? property.PropertySet.Description;
                        row.Second = property.BaseName.Literal ?? property.BaseName.Description;
                        row.DataType = property.DataType ?? string.Empty;
                        Fill(row, property.Value);
                        break;

                    case IdsClassificationFacet classification:
                        row.Kind = Classification;
                        row.First = classification.System.Literal ?? classification.System.Description;
                        Fill(row, classification.Value);
                        break;

                    case IdsMaterialFacet material:
                        row.Kind = Material;
                        row.First = material.Value?.Literal ?? string.Empty;
                        break;

                    case IdsPartOfFacet part:
                        row.Kind = PartOf;
                        row.First = part.EntityName.Literal ?? part.EntityName.Description;
                        row.Relation = part.Relation ?? string.Empty;
                        break;
                }

                return row;
            }

            private static void Fill(Row row, IdsValue? value)
            {
                if (value == null) { row.Test = Present; return; }

                if (value.Literal != null)
                {
                    row.Test = Exactly;
                    row.Value = value.Literal;
                    return;
                }

                if (value.Enumeration != null && value.Enumeration.Count > 0)
                {
                    row.Test = OneOf;
                    row.Value = string.Join(", ", value.Enumeration);
                    return;
                }

                if (value.Pattern != null)
                {
                    row.Test = Matches;
                    row.Value = value.Pattern;
                    return;
                }

                if (value.MinInclusive != null || value.MaxInclusive != null)
                {
                    row.Test = Between;
                    row.Value = Number(value.MinInclusive);
                    row.Upper = Number(value.MaxInclusive);
                    return;
                }

                // A length or an exclusive bound: real, and not something these boxes hold. Said
                // rather than dropped quietly.
                row.Test = Present;
                row.Instructions = (row.Instructions.Length > 0 ? row.Instructions + " " : string.Empty)
                                 + "(was: " + value.Description + ")";
            }

            private static string Number(double? value) =>
                value == null ? string.Empty : value.Value.ToString("0.##########", CultureInfo.InvariantCulture);

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            private UIElement Build()
            {
                var columns = new Grid();
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // The specifications lead, and nothing else competes with them. What the document
                // is called and who wrote it is set once and read never, so it is behind a symbol
                // rather than laid out under the list somebody is actually working in.
                var left = Ui.Scroll(Ui.Stack(
                    Ui.Eyebrow("Specifications"),
                    _list,
                    Ui.Rule(),
                    Ui.Actions(
                        Ui.Primary("Save IDS...", () => Save(everything: false)),
                        Ui.Quiet("Save BIMCamel rules...", () => Save(everything: true)),
                        Ui.Icon(Glyphs.Save, "Keep the draft here", Keep),
                        Ui.Icon(Glyphs.Tune, "What this document is called", Details)),
                    _saveSays,
                    Ui.Small("The draft is kept as IDS itself, so what the project holds is already "
                             + "a file another tool can read."),
                    _output));

                Grid.SetColumn(left, 0);
                columns.Children.Add(left);

                var right = Ui.Scroll(Ui.Stack(_detail, Ui.Rule(), Ui.Eyebrow("What it comes to"),
                                               _preview));

                Grid.SetColumn(right, 1);
                columns.Children.Add(right);

                DrawList();
                DrawDetail();

                return columns;
            }

            /// <summary>What the document is called, in a window of its own.</summary>
            private void Details() =>
                Ui.Configure("This document", null,
                    new Ui.Page("What it is called", () => Ui.Stack(
                        Ui.Field("Called", _title),
                        Ui.Pair(Ui.Field("Version", _version),
                                Ui.Field("Author", _author, "an email address, or left out")),
                        Ui.Field("About", _about),
                        Ui.Small("These go into the IDS file's header, where the tool that reads it "
                                 + "says who asked for what."))));

            private void DrawList()
            {
                _list.Children.Clear();

                foreach (var spec in _specs)
                {
                    var here = spec;
                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                    var open = Ui.Button(here.Name.Length == 0 ? "(unnamed)" : here.Name,
                                         () => { _chosen = here; DrawDetail(); DrawList(); });

                    if (ReferenceEquals(here, _chosen)) open.Wearing("PrimaryBtn");

                    row.Children.Add(open);
                    row.Children.Add(Ui.Pill(Ui.Count(here.Wants.Count, "requirement"), Tone.Plain));

                    row.Children.Add(Ui.Icon(Glyphs.Close, "Remove this specification", () =>
                    {
                        _specs.Remove(here);
                        if (ReferenceEquals(here, _chosen)) _chosen = _specs.FirstOrDefault();
                        DrawList();
                        DrawDetail();
                    }));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 6, 0);

                    _list.Children.Add(row);
                }

                _list.Children.Add(Ui.Actions(Ui.Icon(Glyphs.Add, "Add a specification", () =>
                {
                    var fresh = new Spec();
                    fresh.Applies.Add(new Row { Kind = Entity, Test = Present });
                    _specs.Add(fresh);
                    _chosen = fresh;
                    DrawList();
                    DrawDetail();
                })));

                if (_specs.Count == 0)
                    _list.Children.Add(Ui.Empty("Nothing in it yet.",
                        "A specification is two lists: which elements it is about, and what they "
                        + "must have."));
            }

            private void DrawDetail()
            {
                _detail.Children.Clear();
                _preview.Children.Clear();

                var spec = _chosen;

                if (spec == null)
                {
                    _detail.Children.Add(Ui.Empty("No specification chosen.",
                        "Pick one on the left, or add one."));
                    return;
                }

                var name = Ui.Text(spec.Name, text => { spec.Name = text; }, 260);
                var identifier = Ui.Text(spec.Identifier, text => spec.Identifier = text, 140);
                var about = Ui.Text(spec.Description, text => spec.Description = text, 260);
                var told = Ui.Text(spec.Instructions, "what to do about a failure",
                                   text => spec.Instructions = text, 260);

                var versions = new WrapPanel();

                foreach (var version in IdsWriter.IfcVersions)
                {
                    var here = version;

                    var box = Ui.Check(here, spec.Versions.Contains(here), on =>
                    {
                        if (on) spec.Versions.Add(here); else spec.Versions.Remove(here);
                    });

                    box.Margin = new Thickness(0, 0, 12, 0);
                    versions.Children.Add(box);
                }

                _detail.Children.Add(Ui.Eyebrow("The specification"));

                _detail.Children.Add(Ui.Form(
                    Ui.Field("Called", name),
                    Ui.Pair(Ui.Field("Reference", identifier), Ui.Field("IFC versions", versions)),
                    Ui.Field("About", about),
                    Ui.Field("Instruction", told,
                             "It travels with every failure, into the workbook and into the "
                             + "authoring tool. It is the difference between a list and a job."),
                    Ui.Pair(
                        Ui.Field("At least this many match", Count(spec.MinOccurs,
                                                                  v => spec.MinOccurs = v)),
                        Ui.Field("At most", Count(spec.MaxOccurs ?? 0,
                                                  v => spec.MaxOccurs = v <= 0 ? (int?)null : v)))));

                _detail.Children.Add(Ui.Small(
                    "\"At least one\" is how a specification stops passing for the wrong reason: "
                    + "with it, a model holding no walls fails \"every wall carries a fire rating\" "
                    + "rather than sailing through it. Zero means no expectation either way."));

                _detail.Children.Add(Ui.Rule());
                _detail.Children.Add(Ui.Eyebrow("It applies to"));
                _detail.Children.Add(Ui.Small("Every row has to match for an element to be in scope. "
                                              + "One IFC class, and as many other filters as you like."));
                _detail.Children.Add(Rows(spec.Applies, requirement: false));

                _detail.Children.Add(Ui.Rule());
                _detail.Children.Add(Ui.Eyebrow("Those elements must have"));
                _detail.Children.Add(Rows(spec.Wants, requirement: true));

                Look();
            }

            private TextBox Count(int value, Action<int> set) =>
                Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                     out var parsed) && parsed >= 0) set(parsed);
                }, 70);

            private UIElement Rows(IList<Row> rows, bool requirement)
            {
                var host = Ui.Stack();

                void Redraw()
                {
                    host.Children.Clear();

                    foreach (var row in rows) host.Children.Add(Card(row, rows, requirement, Redraw));

                    host.Children.Add(Ui.Actions(Ui.Quiet("Add a row", () =>
                    {
                        rows.Add(new Row { Kind = requirement ? Property : Entity });
                        Redraw();
                        Look();
                    })));
                }

                Redraw();

                return host;
            }

            /// <summary>
            /// One facet, with the fields that kind of facet actually has.
            ///
            /// Redrawn when the kind changes rather than greying boxes out: a field that does not
            /// apply is not a disabled field, it is a field that is not part of this question.
            /// </summary>
            private UIElement Card(Row row, IList<Row> owner, bool requirement, Action redraw)
            {
                // A scope is offered on applicability only. Written as a requirement it would
                // assert "this element must be in that set", which grades a model on how somebody
                // filed it rather than on the building - and the sets are usually made by the
                // person being checked.
                var kind = Ui.Choice(
                    requirement
                        ? new[] { Property, Attribute, Classification, Material, PartOf }
                        : new[] { Entity, Scoped, Property, Attribute, Classification, Material, PartOf },
                    row.Kind,
                    pick => { row.Kind = pick; redraw(); Look(); }, 160);

                var body = new List<UIElement?>();

                // Property rows get pickers fed from the model: the set and name being demanded
                // usually exist on the compliant elements already, and typing "Pset_WallCommon"
                // from memory is how a specification fails a model that actually complies. Both
                // stay editable — a name the federation does not carry yet is still a legitimate
                // requirement to write down.
                FrameworkElement first =
                    row.Kind == Scoped
                        ? Ui.Choice(SetChoices(), row.First,
                                    pick => { row.First = pick; redraw(); Look(); }, 240)
                    : row.Kind == Property
                        ? Pickers.Tab(row.First, text => { row.First = text; Look(); }, 180)
                        : (FrameworkElement)Ui.Text(row.First, text => { row.First = text; Look(); }, 180);

                body.Add(row.HasSecond
                    ? Ui.Pair(Ui.Field(row.FirstLabel, first),
                              Ui.Field("Property", Pickers.Property(row.Second,
                                                       text => { row.Second = text; Look(); }, 180)))
                    : Ui.Field(row.FirstLabel, first));

                if (row.IsScope)
                {
                    var counted = Ui.Stack();

                    body.Add(Ui.Actions(Ui.Quiet("How many is that?", () => Count(row, counted))));
                    body.Add(counted);

                    body.Add(Ui.Small(
                        string.Equals(row.First, TheSelection, StringComparison.Ordinal)
                            ? "This means whatever is selected at the moment the check runs, so it "
                              + "cannot mean the same thing twice. Good for a check you run now; "
                              + "name a set for one the project keeps."
                            : "Who this rule is about, in this model's own terms. It works on a "
                              + "federation with no IFC in it - and it cannot go in a .ids file, "
                              + "so the client's copy will not carry this rule."));
                }
                else if (row.Kind == PartOf)
                {
                    body.Add(Ui.Field("Through", Ui.Choice(
                        new[] { string.Empty }.Concat(IdsWriter.Relations).ToList(),
                        row.Relation, pick => { row.Relation = pick; Look(); }, 240),
                        "Left blank, any relationship counts."));
                }
                else if (row.Kind != Entity && row.HasValue)
                {
                    var test = Ui.Choice(new[] { Present, Exactly, OneOf, Matches, Between },
                                         row.Test,
                                         pick => { row.Test = pick; redraw(); Look(); }, 180);

                    body.Add(Ui.Field("Its value", test));

                    if (row.Test == Between)
                    {
                        body.Add(Ui.Pair(
                            Ui.Field("From", Ui.Text(row.Value, text => { row.Value = text; Look(); }, 90)),
                            Ui.Field("To", Ui.Text(row.Upper, text => { row.Upper = text; Look(); }, 90))));
                    }
                    else if (row.Test != Present)
                    {
                        body.Add(Ui.Field(row.Test == OneOf ? "Any of these" : "Value",
                            Ui.Text(row.Value, text => { row.Value = text; Look(); }, 240),
                            row.Test == OneOf ? "separated by commas"
                            : row.Test == Matches ? "a regular expression, matched against the whole value"
                            : null));
                    }
                }

                if (requirement && row.Kind == Property)
                    body.Add(Ui.Field("IFC type", Ui.Text(row.DataType, "IFCLABEL",
                                                          text => { row.DataType = text; Look(); }, 140),
                                      "all capitals, and only a hint about units"));

                if (requirement && row.Kind != Entity)
                    body.Add(Ui.Field("It is", Ui.Choice(
                        row.Kind == PartOf ? new[] { Required, Prohibited }
                                           : new[] { Required, Optional, Prohibited },
                        row.Cardinality, pick => { row.Cardinality = pick; Look(); }, 180)));

                if (requirement)
                    body.Add(Ui.Field("Instruction", Ui.Text(row.Instructions, "optional",
                                                             text => { row.Instructions = text; Look(); }, 240)));

                // A bin, not three words. This sits at the foot of every facet row on a
                // specification that can carry a dozen of them, and "Remove this row" twelve times
                // over is more prose about the chrome than there is about the rule being written.
                body.Add(Ui.Actions(Ui.Icon(Glyphs.Bin, "Remove this row", () =>
                {
                    owner.Remove(row);
                    redraw();
                    Look();
                })));

                // Every row open, none folded: a rule somebody is checking a federation against
                // is not something to hide behind its own summary.
                var open = Ui.Stack(
                    Ui.Eyebrow(row.Kind),
                    Ui.Field("This row is a", kind),
                    Ui.Form(body.ToArray()),
                    Ui.Rule());

                return open;
            }

            // -----------------------------------------------------------------------------------
            // What it comes to
            // -----------------------------------------------------------------------------------

            /// <summary>Build the document, or say what is stopping it.</summary>
            private IdsDocument? Compose(ICollection<string> problems)
            {
                var document = new IdsDocument(_title.Text.Trim().Length == 0
                    ? "Information requirements"
                    : _title.Text.Trim());

                document.Version = Or(_version.Text);
                document.Author = Or(_author.Text);
                document.Description = Or(_about.Text);

                foreach (var spec in _specs)
                {
                    var named = spec.Name.Trim();

                    if (named.Length == 0)
                    {
                        problems.Add("A specification has no name, and every one needs one.");
                        continue;
                    }

                    var built = new IdsSpecification(named)
                    {
                        Identifier = Or(spec.Identifier),
                        Description = Or(spec.Description),
                        Instructions = Or(spec.Instructions),
                        MinOccurs = spec.MinOccurs,
                        MaxOccurs = spec.MaxOccurs,
                    };

                    foreach (var version in IdsWriter.IfcVersions.Where(v => spec.Versions.Contains(v)))
                        built.IfcVersions.Add(version);

                    foreach (var row in spec.Applies)
                    {
                        var facet = row.Build(requirement: false, out var problem);

                        if (facet == null) problems.Add("\"" + named + "\": " + problem);
                        else built.Applicability.Add(facet);
                    }

                    foreach (var row in spec.Wants)
                    {
                        var facet = row.Build(requirement: true, out var problem);

                        if (facet == null) problems.Add("\"" + named + "\": " + problem);
                        else built.Requirements.Add(facet);
                    }

                    document.Specifications.Add(built);
                }

                return document;
            }

            private static string? Or(string? text)
            {
                var value = (text ?? string.Empty).Trim();
                return value.Length == 0 ? null : value;
            }

            /// <summary>
            /// The verdict, kept live under the form.
            ///
            /// The writer refuses a file nobody could load, and finding that out at the save dialog
            /// is finding it out after the sitting. Here it is the line under the boxes.
            /// </summary>
            /// <summary>
            /// The sets a rule can be pointed at, with the current selection first.
            ///
            /// Names only, and no counts: a count costs a traversal each and this list is built
            /// every time a row is redrawn. "How many is that?" on the row answers when asked.
            /// </summary>
            private IReadOnlyList<string> SetChoices()
            {
                var choices = new List<string> { TheSelection };

                try
                {
                    choices.AddRange(_session.Model.Sets()
                        .Select(set => set.DisplayName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                }
                catch (Exception)
                {
                    // No document, or a host that will not list them. The box still takes a name,
                    // because a rule about a set somebody has not made yet is worth writing down.
                }

                return choices;
            }

            /// <summary>
            /// Say how many elements a scope row actually catches, when asked.
            ///
            /// The question somebody has before they trust a rule, and the one a rule set cannot
            /// answer on its own: a set named correctly and holding nothing looks exactly like a
            /// rule that works until the day it reports nothing.
            /// </summary>
            private void Count(Row row, Panel say)
            {
                say.Children.Clear();

                var picked = string.Equals(row.First, TheSelection, StringComparison.Ordinal);

                if (!picked && string.IsNullOrWhiteSpace(row.First))
                {
                    say.Children.Add(Ui.Problem("Pick a set first."));
                    return;
                }

                var scope = picked ? TraversalScope.CurrentSelection : TraversalScope.Set(row.First);

                int held;

                try
                {
                    held = _session.Model.Traverse(scope).Count();
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                say.Children.Add(held == 0
                    ? (UIElement)Ui.Problem((picked ? "Nothing is selected" : "That set holds nothing")
                        + " right now, so this rule would apply to nothing and the run would say so "
                        + "rather than pass.")
                    : Ui.Done(Ui.Count(held, "element") + " right now."));
            }

            /// <summary>
            /// What each of the two save buttons would cost, said before either is pressed.
            ///
            /// The choice is only a real choice if somebody can see it. A line that appears after
            /// the file is written tells them what they should have done.
            /// </summary>
            private void SaySave()
            {
                _saveSays.Children.Clear();

                var scoped = _specs.Count(Scopes);

                if (scoped == 0)
                {
                    _saveSays.Children.Add(Ui.Small(
                        "Every rule here is an ordinary IDS rule, so both buttons carry all of "
                        + "them. Save IDS is the one to send."));

                    return;
                }

                _saveSays.Children.Add(scoped == _specs.Count
                    ? Ui.Note("Every rule here is about a set in this model, and IDS has no way to "
                        + "say that - so \"Save IDS\" has nothing to write. \"Save BIMCamel "
                        + "rules\" carries them, and CamelWorks reads it.")
                    : Ui.Note("\"Save IDS\" carries "
                        + Ui.Count(_specs.Count - scoped, "rule") + " and leaves "
                        + Ui.Count(scoped, "rule") + " behind, because they are about sets in this "
                        + "model and IDS has no way to say that. \"Save BIMCamel rules\" carries "
                        + "all " + _specs.Count + ", and only CamelWorks reads it."));
            }

            private void Look()
            {
                SaySave();
                _preview.Children.Clear();

                var problems = new List<string>();
                var document = Compose(problems);

                foreach (var problem in problems.Take(6)) _preview.Children.Add(Ui.Problem(problem));

                if (document == null) return;

                var split = IdsScopeSpecification.Split(document);
                var written = IdsWriter.Write(document, _session.NowTicks);

                if (written.Problem is string refused)
                {
                    // A rule set that is all scoped is not broken - it runs here perfectly well,
                    // and only the file cannot hold it. Showing the writer's refusal as a fault
                    // would tell somebody their rules are wrong when they are not.
                    _preview.Children.Add(split.Portable.Count == 0 && split.Staying.Count > 0
                        ? (UIElement)Ui.Note(
                            Ui.Count(split.Staying.Count, "rule") + ", every one of them about a "
                            + "set in this model. That runs here, and there is no .ids file in it: "
                            + "IDS says who a rule is about with IFC facets and nothing else. Give "
                            + "one rule an IFC class if the client needs a copy.")
                        : Ui.Problem(refused));

                    return;
                }

                _preview.Children.Add(problems.Count == 0
                    ? Ui.Done(written.Explain())
                    : Ui.Note(written.Explain() + " The rows above are not in it."));

                foreach (var warning in written.Warnings.Take(6))
                    _preview.Children.Add(Ui.Note(warning));
            }

            /// <summary>The file, or the reason there is not one.</summary>
            private IdsWriteResult? Ready()
            {
                _output.Children.Clear();

                var problems = new List<string>();
                var document = Compose(problems);

                if (problems.Count > 0)
                {
                    foreach (var problem in problems.Take(6)) _output.Children.Add(Ui.Problem(problem));

                    _output.Children.Add(Ui.Note("Nothing has been written. Fix those rows, or "
                        + "remove them, and press it again."));
                    return null;
                }

                var written = IdsWriter.Write(document, _session.NowTicks);

                if (written.Problem == null)
                {
                    // Said before the file is written, not after: somebody about to send this to
                    // a client has to know which of their requirements will not be in it.
                    foreach (var behind in written.Stayed.Take(6))
                        _output.Children.Add(Ui.Note("Not in the file: \""
                            + behind.Specification.Name + "\" - " + behind.Why + "."));

                    if (written.Stayed.Count > 6)
                        _output.Children.Add(Ui.Note("And "
                            + Ui.Count(written.Stayed.Count - 6, "other rule") + " like them."));

                    return written;
                }

                _output.Children.Add(Ui.Problem(
                    written.Problem ?? "The rules could not be written."));

                return null;
            }

            /// <summary>
            /// Write the rules out, in one of the two kinds there are.
            ///
            /// <b>Why it is a choice and not a rule.</b> An IDS is worth writing because everybody
            /// reads one, and a rule that says who it is about in this model's own terms cannot be
            /// in one. So a project with both kinds of rule can send a file everyone opens without
            /// those rules, or a file with all of them that only CamelWorks opens. Which is right
            /// depends entirely on who it is going to, and that is not something this code knows.
            /// </summary>
            /// <param name="everything">
            /// True for the BIMCamel rule set, which carries the scoped rules too.
            /// </param>
            private void Save(bool everything)
            {
                if (everything) { SaveOurs(); return; }

                var written = Ready();                if (written == null) return;

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the IDS",
                    Filter = "IDS|*.ids|All files|*.*",
                    FileName = _title.Text.Trim() + ".ids",
                    AddExtension = true,
                    DefaultExt = ".ids",
                };

                if (!Ui.Asked(dialog)) return;

                try
                {
                    File.WriteAllText(dialog.FileName, written.Text, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _output.Children.Add(Ui.Done("Wrote " + written.Explain() + " to " + dialog.FileName));

                // The file's whole point is being run: the check lives on the Health tab, where
                // "IDS file" is a box waiting for the path that was just written.
                _output.Children.Add(Ui.Actions(Ui.Quiet("Check it",
                    () => Ui.Told(AppWindow.Show("audit", "health")))));

                foreach (var warning in written.Warnings.Take(6)) _output.Children.Add(Ui.Note(warning));

                _session.Record(ActivityKind.Exchange,
                    "wrote an IDS: " + written.Explain(), dialog.FileName);
            }

            /// <summary>
            /// Write everything, in the kind only CamelWorks reads.
            ///
            /// A zip with a real IDS inside it: somebody with none of this can rename it, unzip
            /// it, and open the portable half. The degradation path is a file rather than a shrug.
            /// </summary>
            private void SaveOurs()
            {
                _output.Children.Clear();

                var problems = new List<string>();
                var document = Compose(problems);

                if (problems.Count > 0 || document == null)
                {
                    foreach (var problem in problems.Take(6)) _output.Children.Add(Ui.Problem(problem));

                    _output.Children.Add(Ui.Note("Nothing has been written. Fix those rows, or "
                        + "remove them, and press it again."));

                    return;
                }

                var packed = IdsFile.Write(document, _session.NowTicks);

                if (!packed.IsUsable)
                {
                    _output.Children.Add(Ui.Problem(packed.Problem ?? "Nothing could be written."));
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the BIMCamel rules",
                    Filter = "BIMCamel rules|*" + IdsFile.Extension + "|All files|*.*",
                    FileName = _title.Text.Trim() + IdsFile.Extension,
                    AddExtension = true,
                    DefaultExt = IdsFile.Extension,
                };

                if (!Ui.Asked(dialog)) return;

                try
                {
                    File.WriteAllBytes(dialog.FileName, packed.Bytes!);
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _output.Children.Add(Ui.Done("Wrote " + packed.Explain() + " to " + dialog.FileName));

                if (packed.Scoped > 0)
                    _output.Children.Add(Ui.Note(
                        "The " + Ui.Count(packed.Scoped, "scoped rule") + " in it name sets by "
                        + "name, so on somebody else's model they apply to whatever their sets of "
                        + "those names hold - and a run says plainly when a set is not there. "
                        + "That is what makes one rule set runnable against several models."));

                _output.Children.Add(Ui.Actions(Ui.Quiet("Check it",
                    () => Ui.Told(AppWindow.Show("audit", "health")))));

                foreach (var warning in packed.Warnings.Take(6)) _output.Children.Add(Ui.Note(warning));

                _session.Record(ActivityKind.Exchange,
                    "wrote a BIMCamel rule set: " + packed.Explain(), dialog.FileName);
            }

            private void Keep()
            {
                // Every rule that cannot go in a file, in this window's own shape. Composed from
                // the rows rather than from the written document, because the written document is
                // exactly the half that leaves these out.
                var scoped = _specs.Where(Scopes).ToList();

                var problems = new List<string>();
                var document = Compose(problems);

                _output.Children.Clear();

                if (problems.Count > 0)
                {
                    foreach (var problem in problems.Take(6)) _output.Children.Add(Ui.Problem(problem));

                    _output.Children.Add(Ui.Note("Nothing has been kept. Fix those rows, or "
                        + "remove them, and press it again."));

                    return;
                }

                var written = document == null ? null : IdsWriter.Write(document, _session.NowTicks);

                // An all-scoped rule set has no portable half at all, and that is not a failure
                // here: the project keeps the rules and the run uses them. Only the file cannot.
                if (written?.Problem is string stopped && scoped.Count == 0)
                {
                    _output.Children.Add(Ui.Problem(stopped));
                    return;
                }

                var section = _session.Store.Section(ProjectStore.ProfileSection);

                section.Set(Key, written != null && written.Problem == null
                    ? written.Text
                    : string.Empty);

                section.Set(ScopedKey, IdsJson.WriteAll(
                    scoped.Select(spec => spec.Build(new List<string>()))
                          .Where(built => built != null)
                          .Select(built => built!)));

                if (!_session.Store.Save())
                {
                    _output.Children.Add(Ui.Problem(
                        _session.Store.LastSaveProblem ?? "Could not write the project file."));

                    return;
                }

                _output.Children.Add(Ui.Done("Kept in the project. It opens here next time"
                    + (written != null && written.Problem == null
                        ? ", and the part an IDS file can hold is already a file another tool can read."
                        : ".")
                    + (scoped.Count > 0
                        ? " " + Ui.Count(scoped.Count, "rule") + " about a set in this model "
                          + (scoped.Count == 1 ? "is" : "are") + " kept beside it, where no IDS "
                          + "file could carry " + (scoped.Count == 1 ? "it" : "them") + "."
                        : string.Empty)));
            }

            /// <summary>Whether a rule says who it is about in this model's own terms.</summary>
            private static bool Scopes(Spec spec) => spec.Applies.Any(row => row.IsScope);
        }
    }
}
