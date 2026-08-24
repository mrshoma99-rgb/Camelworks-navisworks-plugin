using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Data
{
    /// <summary>Where one column of a report reads from.</summary>
    public enum ReportField
    {
        /// <summary>A named property, on whatever tab the model puts it.</summary>
        Property = 0,

        /// <summary>The name the selection tree shows.</summary>
        Name,

        /// <summary>The element's category, where the source gave one.</summary>
        Category,

        /// <summary>The type or family name, where the source gave one.</summary>
        Type,

        /// <summary>Which model in the federation it came from.</summary>
        Model,

        /// <summary>The whole selection-tree path, joined.</summary>
        Path,

        /// <summary>One level of the selection-tree path, counted from the root.</summary>
        PathLevel,

        /// <summary>The element's identity, as CamelWorks writes it.</summary>
        Identity,
    }

    /// <summary>What a column does when several elements fold into one row.</summary>
    public enum ReportRoll
    {
        /// <summary>The one value they share, or a note that they do not share one.</summary>
        Same = 0,

        /// <summary>How many elements are in the row.</summary>
        Count,

        /// <summary>How many different values they carry between them.</summary>
        Distinct,

        /// <summary>Every different value, listed.</summary>
        List,

        /// <summary>Added up.</summary>
        Sum,

        /// <summary>The smallest.</summary>
        Least,

        /// <summary>The largest.</summary>
        Most,

        /// <summary>The average.</summary>
        Mean,
    }

    /// <summary>
    /// One element, flattened to what a tool can read from it without holding the model.
    ///
    /// A model item is only valid until the document changes underneath it, and a report a person
    /// edits, sorts and exports outlives that by minutes — so the reading happens once, at the top,
    /// and everything after works on the copy.
    ///
    /// Named for the report builder because that is what first needed it, and shared since: the
    /// data link joins on it and the extract selects on it. One flattening, read once per tool run,
    /// rather than three near-identical ones each with their own idea of what a blank means.
    /// </summary>
    public sealed class ReportElement
    {
        private readonly Dictionary<PropertyRef, string?> _exact;
        private readonly Dictionary<string, string?> _byName;

        /// <summary>Flatten one element.</summary>
        /// <param name="key">Its identity.</param>
        /// <param name="name">What the selection tree calls it.</param>
        /// <param name="category">Its category, where the source gave one.</param>
        /// <param name="type">Its type or family, where the source gave one.</param>
        /// <param name="model">Which model it came from.</param>
        /// <param name="path">The selection-tree path, root first.</param>
        /// <param name="properties">Everything it carries.</param>
        /// <param name="bounds">Its world-space box, where it has geometry.</param>
        /// <param name="hasGeometry">Whether it carries geometry rather than being a grouping node.</param>
        public ReportElement(ElementKey key, string? name, string? category = null, string? type = null,
                             string? model = null, IReadOnlyList<string>? path = null,
                             IEnumerable<PropertyValue>? properties = null,
                             BoundingBox bounds = default, bool hasGeometry = false)
        {
            Key = key;
            Bounds = bounds;
            HasGeometry = hasGeometry;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name!.Trim();
            Category = Tidy(category);
            Type = Tidy(type);
            Model = Tidy(model);
            Path = path == null
                ? Array.Empty<string>()
                : path.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

            _exact = new Dictionary<PropertyRef, string?>();
            _byName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (properties == null) return;

            foreach (var reading in properties)
            {
                if (string.IsNullOrWhiteSpace(reading.Name)) continue;

                var field = new PropertyRef(reading.Category ?? string.Empty, reading.Name);

                if (!_exact.ContainsKey(field)) _exact.Add(field, reading.Value);
                if (!_byName.ContainsKey(reading.Name)) _byName.Add(reading.Name, reading.Value);
            }
        }

        /// <summary>Its identity.</summary>
        public ElementKey Key { get; }

        /// <summary>What the selection tree calls it.</summary>
        public string Name { get; }

        /// <summary>Its category, or null.</summary>
        public string? Category { get; }

        /// <summary>Its type or family, or null.</summary>
        public string? Type { get; }

        /// <summary>Which model it came from, or null.</summary>
        public string? Model { get; }

        /// <summary>The selection-tree path, root first.</summary>
        public IReadOnlyList<string> Path { get; }

        /// <summary>Its world-space box. Meaningless when it carries no geometry.</summary>
        public BoundingBox Bounds { get; }

        /// <summary>Whether it carries geometry, as opposed to being a grouping node.</summary>
        public bool HasGeometry { get; }

        /// <summary>Every property it carries, as a report sees them.</summary>
        public IReadOnlyCollection<PropertyRef> Fields => _exact.Keys;

        /// <summary>
        /// Read one property.
        ///
        /// A reference with no category matches on name alone, which is what a person means when
        /// they pick "Mark" off a list without caring which tab of a federated model it sits on.
        /// </summary>
        /// <param name="field">What to read.</param>
        public string? Value(PropertyRef field)
        {
            if (field.Category.Length == 0)
                return _byName.TryGetValue(field.Name, out var loose) ? loose : null;

            return _exact.TryGetValue(field, out var exact) ? exact : null;
        }

        /// <summary>True when the element carries that property at all, blank value included.</summary>
        /// <param name="field">What to look for.</param>
        public bool Carries(PropertyRef field) =>
            field.Category.Length == 0 ? _byName.ContainsKey(field.Name) : _exact.ContainsKey(field);

        /// <summary>Flatten a model item.</summary>
        /// <param name="item">The item, as the host resolved it.</param>
        public static ReportElement? Of(IModelItem? item)
        {
            if (item == null) return null;

            return new ReportElement(item.Key, item.DisplayName, item.Category, item.TypeName,
                                     item.Model?.DisplayName, item.TreePath, item.Properties(),
                                     item.Bounds, item.HasGeometry);
        }

        /// <inheritdoc />
        public override string ToString() => Name;

        private static string? Tidy(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }

    /// <summary>One column of a report.</summary>
    public sealed class ReportColumn
    {
        /// <summary>Make a column.</summary>
        /// <param name="heading">What to call it. Empty takes the field's own name.</param>
        /// <param name="field">Where it reads from.</param>
        /// <param name="source">Which property, when the field is a property.</param>
        /// <param name="level">Which level of the path, when the field is a path level.</param>
        public ReportColumn(string? heading, ReportField field = ReportField.Property,
                            PropertyRef source = default, int level = 1)
        {
            Field = field;
            Source = source;
            Level = level < 1 ? 1 : level;
            Heading = string.IsNullOrWhiteSpace(heading) ? Natural() : heading!.Trim();
        }

        /// <summary>What the sheet calls it.</summary>
        public string Heading { get; set; }

        /// <summary>Where it reads from.</summary>
        public ReportField Field { get; }

        /// <summary>Which property, when the field is a property.</summary>
        public PropertyRef Source { get; }

        /// <summary>Which level of the path, when the field is a path level.</summary>
        public int Level { get; }

        /// <summary>
        /// Whether a person may fill this column in and send the sheet back.
        ///
        /// Only a property column can be: there is nowhere to put an edited category or an edited
        /// tree path, and a sheet that invites an edit it cannot honour is worse than one that
        /// does not invite it.
        /// </summary>
        public bool Editable
        {
            get => _editable && Field == ReportField.Property;
            set => _editable = value;
        }

        /// <summary>What it does when several elements fold into one row.</summary>
        public ReportRoll Roll { get; set; } = ReportRoll.Same;

        private bool _editable;

        /// <summary>Read this column off an element.</summary>
        /// <param name="element">The element.</param>
        public string? Read(ReportElement? element)
        {
            if (element == null) return null;

            switch (Field)
            {
                case ReportField.Name: return element.Name;
                case ReportField.Category: return element.Category;
                case ReportField.Type: return element.Type;
                case ReportField.Model: return element.Model;
                case ReportField.Identity: return element.Key.ToString();
                case ReportField.Path:
                    return element.Path.Count == 0 ? null : string.Join(" > ", element.Path);
                case ReportField.PathLevel:
                    return Level <= element.Path.Count ? element.Path[Level - 1] : null;
                default:
                    return element.Value(Source);
            }
        }

        /// <summary>A column reading a property.</summary>
        /// <param name="source">Which property.</param>
        /// <param name="heading">What to call it, or null for the property's own name.</param>
        public static ReportColumn Of(PropertyRef source, string? heading = null) =>
            new ReportColumn(heading, ReportField.Property, source);

        /// <summary>A column reading a property.</summary>
        /// <param name="category">The property tab, or empty for any tab.</param>
        /// <param name="property">The property name.</param>
        /// <param name="heading">What to call it, or null for the property's own name.</param>
        public static ReportColumn Of(string category, string property, string? heading = null) =>
            new ReportColumn(heading, ReportField.Property, new PropertyRef(category, property));

        /// <summary>A column reading something every element has.</summary>
        /// <param name="field">Which one.</param>
        /// <param name="heading">What to call it, or null for its own name.</param>
        /// <param name="level">Which level, when the field is a path level.</param>
        public static ReportColumn Built(ReportField field, string? heading = null, int level = 1) =>
            new ReportColumn(heading, field, default, level);

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("heading", Heading)
                .Set("field", Field.ToString());

            if (Field == ReportField.Property)
            {
                json.Set("property", Source.Name);
                if (Source.Category.Length > 0) json.Set("category", Source.Category);
                if (Editable) json.Set("editable", true);
            }

            if (Field == ReportField.PathLevel) json.Set("level", Level);
            if (Roll != ReportRoll.Same) json.Set("roll", Roll.ToString());

            return json;
        }

        /// <summary>Read one back, or null when there is nothing readable there.</summary>
        /// <param name="json">What was written.</param>
        public static ReportColumn? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            if (!Enum.TryParse<ReportField>(json["field"].AsString() ?? "Property", out var field))
                field = ReportField.Property;

            var property = (json["property"].AsString() ?? string.Empty).Trim();

            if (field == ReportField.Property && property.Length == 0) return null;

            var source = new PropertyRef((json["category"].AsString() ?? string.Empty).Trim(), property);
            var level = (int)json["level"].AsLong();

            var column = new ReportColumn(json["heading"].AsString(), field, source, level < 1 ? 1 : level)
            {
                Editable = json["editable"].AsBool(),
            };

            if (Enum.TryParse<ReportRoll>(json["roll"].AsString() ?? "Same", out var roll))
                column.Roll = roll;

            return column;
        }

        /// <inheritdoc />
        public override string ToString() => Heading;

        private string Natural() => Field switch
        {
            ReportField.Name => "Name",
            ReportField.Category => "Category",
            ReportField.Type => "Type",
            ReportField.Model => "Model",
            ReportField.Path => "Selection tree",
            ReportField.PathLevel => "Level " + Level.ToString(CultureInfo.InvariantCulture),
            ReportField.Identity => SheetImport.KeyHeading,
            _ => Source.Name.Length == 0 ? "(no property)" : Source.Name,
        };
    }

    /// <summary>What a report is made of, saved so it can be run again next revision.</summary>
    public sealed class ReportTemplate
    {
        /// <summary>How many rows a report will produce before it stops and says so.</summary>
        public const int DefaultCeiling = 20000;

        /// <summary>How many columns a template will hold.</summary>
        public const int MaxColumns = 40;

        /// <summary>Start a template.</summary>
        /// <param name="name">What to call it.</param>
        public ReportTemplate(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Model data" : name!.Trim();
        }

        /// <summary>What to call it.</summary>
        public string Name { get; set; }

        /// <summary>The columns, left to right.</summary>
        public IList<ReportColumn> Columns { get; } = new List<ReportColumn>();

        /// <summary>The heading of the column to fold rows on, or null for one row per element.</summary>
        public string? GroupBy { get; set; }

        /// <summary>The heading of the column to sort on, or null for model order.</summary>
        public string? SortBy { get; set; }

        /// <summary>Whether the sort runs the other way.</summary>
        public bool Descending { get; set; }

        /// <summary>
        /// Whether to put the identity column on the front, so the sheet can come back.
        ///
        /// Meaningless on a grouped report — a row that stands for forty elements has no single
        /// identity to write — so a grouped report drops it and says why.
        /// </summary>
        public bool WithIdentity { get; set; } = true;

        /// <summary>Whether an element that carries nothing there says so, rather than sitting empty.</summary>
        public bool BlanksShown { get; set; } = true;

        /// <summary>How many rows before it stops.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>Add a column, unless the template is already full.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The column, or null when there was no room.</returns>
        public ReportColumn? Add(ReportColumn? column)
        {
            if (column == null || Columns.Count >= MaxColumns) return null;

            Columns.Add(column);
            return column;
        }

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public ReportTemplate Copy()
        {
            var copy = new ReportTemplate(Name)
            {
                GroupBy = GroupBy,
                SortBy = SortBy,
                Descending = Descending,
                WithIdentity = WithIdentity,
                BlanksShown = BlanksShown,
                Ceiling = Ceiling,
            };

            foreach (var column in Columns)
            {
                var clone = ReportColumn.FromJson(column.ToJson());
                if (clone != null) copy.Columns.Add(clone);
            }

            return copy;
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("name", Name)
                .Set("columns", JsonValue.Array(Columns.Select(c => c.ToJson())))
                .Set("identity", WithIdentity)
                .Set("blanks", BlanksShown)
                .Set("ceiling", Ceiling);

            if (GroupBy != null) json.Set("groupBy", GroupBy);
            if (SortBy != null) json.Set("sortBy", SortBy);
            if (Descending) json.Set("descending", true);

            return json;
        }

        /// <summary>Read one back. An unreadable one comes back empty rather than throwing.</summary>
        /// <param name="json">What was written.</param>
        public static ReportTemplate FromJson(JsonValue? json)
        {
            var template = new ReportTemplate(json?["name"].AsString());

            if (json == null) return template;

            foreach (var item in json["columns"].Items)
            {
                var column = ReportColumn.FromJson(item);
                if (column != null) template.Add(column);
            }

            template.GroupBy = json["groupBy"].AsString();
            template.SortBy = json["sortBy"].AsString();
            template.Descending = json["descending"].AsBool();
            template.WithIdentity = json["identity"].AsBool(true);
            template.BlanksShown = json["blanks"].AsBool(true);

            var ceiling = (int)json["ceiling"].AsLong();
            template.Ceiling = ceiling > 0 ? ceiling : DefaultCeiling;

            return template;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + Columns.Count.ToString("N0", CultureInfo.InvariantCulture)
            + (Columns.Count == 1 ? " column" : " columns");
    }

    /// <summary>One row of a built report.</summary>
    public sealed class ReportRow
    {
        internal ReportRow(string identity, IReadOnlyList<string?> cells, int members)
        {
            Identity = identity;
            Cells = cells;
            Members = members;
        }

        /// <summary>
        /// The element's identity, or on a grouped report the value the row was folded on.
        /// </summary>
        public string Identity { get; }

        /// <summary>The cells, in heading order.</summary>
        public IReadOnlyList<string?> Cells { get; }

        /// <summary>How many elements this row stands for.</summary>
        public int Members { get; }

        /// <summary>Read one cell.</summary>
        /// <param name="column">Its index.</param>
        public string? Cell(int column) =>
            column >= 0 && column < Cells.Count ? Cells[column] : null;

        /// <inheritdoc />
        public override string ToString() => string.Join(" | ", Cells.Select(c => c ?? string.Empty));
    }

    /// <summary>A report, built.</summary>
    public sealed class ReportTable
    {
        internal ReportTable(string name, IReadOnlyList<string> headings, IReadOnlyList<ReportRow> rows,
                             int elements, int dropped, bool grouped, bool withIdentity,
                             IReadOnlyList<string> notes, string? refusal)
        {
            Name = name;
            Headings = headings;
            Rows = rows;
            Elements = elements;
            Dropped = dropped;
            Grouped = grouped;
            WithIdentity = withIdentity;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What the template was called.</summary>
        public string Name { get; }

        /// <summary>The headings, left to right.</summary>
        public IReadOnlyList<string> Headings { get; }

        /// <summary>The rows.</summary>
        public IReadOnlyList<ReportRow> Rows { get; }

        /// <summary>How many elements went in.</summary>
        public int Elements { get; }

        /// <summary>How many rows the ceiling cut off.</summary>
        public int Dropped { get; }

        /// <summary>Whether rows were folded together.</summary>
        public bool Grouped { get; }

        /// <summary>Whether the identity column is on the front, so the sheet can come back.</summary>
        public bool WithIdentity { get; }

        /// <summary>Anything worth saying about how it was built.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is no report, when there is none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is nothing in it.</summary>
        public bool IsEmpty => Rows.Count == 0;

        /// <summary>True when it can be written out.</summary>
        public bool IsUsable => Refusal == null && Rows.Count > 0;

        /// <summary>Where a heading sits, or -1.</summary>
        /// <param name="heading">The heading.</param>
        public int ColumnOf(string? heading)
        {
            if (heading == null) return -1;

            for (var i = 0; i < Headings.Count; i++)
                if (string.Equals(Headings[i], heading, StringComparison.OrdinalIgnoreCase))
                    return i;

            return -1;
        }

        /// <summary>What happened, in a sentence a person can act on.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            if (Rows.Count == 0)
                return "Nothing came out of that. There were no elements in scope to report on.";

            var text = Rows.Count.ToString("N0", CultureInfo.InvariantCulture)
                + (Rows.Count == 1 ? " row" : " rows")
                + (Grouped
                    ? " over " + Elements.ToString("N0", CultureInfo.InvariantCulture) + " elements"
                    : string.Empty)
                + ", " + Headings.Count.ToString("N0", CultureInfo.InvariantCulture)
                + (Headings.Count == 1 ? " column." : " columns.");

            if (Dropped > 0)
                text += " " + Dropped.ToString("N0", CultureInfo.InvariantCulture)
                     + " more did not fit under the ceiling and were left out.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>What one property looks like across the elements in scope.</summary>
    public sealed class FieldSummary
    {
        internal FieldSummary(PropertyRef field, int carried, int filled, int distinct, string? example)
        {
            Field = field;
            Carried = carried;
            Filled = filled;
            Distinct = distinct;
            Example = example;
        }

        /// <summary>Which property.</summary>
        public PropertyRef Field { get; }

        /// <summary>The tab it sits on.</summary>
        public string Category => Field.Category;

        /// <summary>What it is called.</summary>
        public string Name => Field.Name;

        /// <summary>How many elements carry it at all, blank ones included.</summary>
        public int Carried { get; }

        /// <summary>How many carry something in it.</summary>
        public int Filled { get; }

        /// <summary>How many different values there are between them.</summary>
        public int Distinct { get; }

        /// <summary>One of those values, so a person can see what the field looks like.</summary>
        public string? Example { get; }

        /// <summary>True when every element that has it has the same value — which tells nobody anything.</summary>
        public bool IsConstant => Distinct == 1 && Filled > 1;

        /// <summary>True when no two elements share a value, which is an identifier, not a report field.</summary>
        public bool IsIdentifier => Filled > 4 && Distinct == Filled;

        /// <summary>What it looks like, said plainly.</summary>
        /// <param name="total">How many elements were in scope.</param>
        public string Describe(int total)
        {
            var text = "on " + Filled.ToString("N0", CultureInfo.InvariantCulture)
                     + " of " + total.ToString("N0", CultureInfo.InvariantCulture) + ", "
                     + Distinct.ToString("N0", CultureInfo.InvariantCulture)
                     + (Distinct == 1 ? " value" : " values");

            if (IsConstant) text += " — the same one everywhere";
            else if (IsIdentifier) text += " — one each, so it reads as an identifier";

            if (Example != null) text += ", e.g. \"" + Example + "\"";

            return text;
        }

        /// <inheritdoc />
        public override string ToString() =>
            (Category.Length == 0 ? Name : Category + " / " + Name);
    }

    /// <summary>
    /// A report over whatever the model actually carries.
    ///
    /// Every other export here reports on something CamelWorks made — a clash, a change, a takeoff.
    /// This one reports on the model itself: pick the properties, pick the shape, get a sheet. It
    /// is the tool a quantity surveyor, an asset register and a handover schedule all turn out to
    /// be, and none of them is served by a fixed set of columns.
    ///
    /// Two decisions carry it.
    ///
    /// The first is that a template is optional. <see cref="Propose"/> reads the elements in scope
    /// and works out what is worth reporting on — properties that most elements carry, that are not
    /// the same everywhere and are not one-per-element identifiers — so the first press of the
    /// button produces a real report on a model nobody has configured anything for. A saved
    /// template then makes that report repeatable next revision; it is never the price of entry.
    ///
    /// The second is that the sheet is built to come back. The identity column goes on the front by
    /// default and the writable columns are marked, so the file that comes out of here is the file
    /// <see cref="SheetImport"/> reads — which is what turns "export the data" into "fix the data",
    /// and is the only reason a report is worth more than a screenshot.
    /// </summary>
    public static class DataReport
    {
        /// <summary>What an empty cell says, when the template asks it to say something.</summary>
        public const string Blank = "(not set)";

        /// <summary>What a folded cell says when the elements under it disagree.</summary>
        public const string Varies = "(varies)";

        /// <summary>What a folded sum says when the values are not all the same kind of thing.</summary>
        public const string Mixed = "(mixed units)";

        /// <summary>How many values a listed column shows before it stops counting them out.</summary>
        public const int ListCap = 8;

        /// <summary>How many columns <see cref="Propose"/> will suggest by itself.</summary>
        public const int ProposedColumns = 8;

        /// <summary>Where saved templates live in the project store.</summary>
        public const string StoreKey = "reports";

        /// <summary>
        /// What the elements in scope actually carry, ranked by how many of them carry it.
        ///
        /// This is the field picker. A person choosing report columns off a list of every property
        /// name in a federated model is choosing blind; the same list with "on 812 of 1,204, 14
        /// values, e.g. FD-60" beside each one is a different job.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        public static IReadOnlyList<FieldSummary> Fields(IEnumerable<ReportElement>? elements)
        {
            var carried = new Dictionary<PropertyRef, int>();
            var filled = new Dictionary<PropertyRef, int>();
            var values = new Dictionary<PropertyRef, HashSet<string>>();
            var examples = new Dictionary<PropertyRef, string>();

            foreach (var element in elements ?? Enumerable.Empty<ReportElement>())
            {
                if (element == null) continue;

                foreach (var field in element.Fields)
                {
                    carried[field] = carried.TryGetValue(field, out var seen) ? seen + 1 : 1;

                    var value = element.Value(field);

                    if (string.IsNullOrWhiteSpace(value)) continue;

                    var trimmed = value!.Trim();

                    filled[field] = filled.TryGetValue(field, out var had) ? had + 1 : 1;

                    if (!values.TryGetValue(field, out var distinct))
                    {
                        distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        values.Add(field, distinct);
                    }

                    distinct.Add(trimmed);

                    if (!examples.ContainsKey(field)) examples.Add(field, trimmed);
                }
            }

            return carried
                .Select(pair => new FieldSummary(
                    pair.Key,
                    pair.Value,
                    filled.TryGetValue(pair.Key, out var had) ? had : 0,
                    values.TryGetValue(pair.Key, out var distinct) ? distinct.Count : 0,
                    examples.TryGetValue(pair.Key, out var example) ? example : null))
                .OrderByDescending(f => f.Filled)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Work out a report from the elements themselves, so the tool works with nothing set up.
        ///
        /// It always takes name, category and type, and the model when there is more than one. It
        /// then takes the best-covered properties, skipping the ones that say nothing — a value
        /// that is identical on every element, and a value that is different on every element.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="columns">How many property columns to propose.</param>
        public static ReportTemplate Propose(IEnumerable<ReportElement>? elements, int columns = ProposedColumns)
        {
            var scope = Materialise(elements);
            var template = new ReportTemplate("Model data");

            template.Add(ReportColumn.Built(ReportField.Name));

            if (scope.Any(e => e.Category != null)) template.Add(ReportColumn.Built(ReportField.Category));
            if (scope.Any(e => e.Type != null)) template.Add(ReportColumn.Built(ReportField.Type));

            if (scope.Select(e => e.Model)
                     .Where(m => m != null)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Count() > 1)
                template.Add(ReportColumn.Built(ReportField.Model));

            var wanted = columns < 1 ? 1 : columns;
            var floor = Math.Max(1, scope.Count / 10);
            var taken = 0;

            foreach (var field in Worth(Fields(scope), floor))
            {
                if (taken >= wanted) break;

                var column = ReportColumn.Of(field.Field);
                column.Editable = true;

                if (template.Add(column) != null) taken++;
            }

            template.SortBy = "Name";

            Disambiguate(template);

            return template;
        }

        /// <summary>
        /// The column worth folding rows on, or null when nothing here groups well.
        ///
        /// A good grouping column is one most elements carry, with enough different values to be
        /// worth separating and few enough to still fit on a page.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="template">The template whose columns may be grouped on.</param>
        public static string? SuggestGroup(IEnumerable<ReportElement>? elements, ReportTemplate? template)
        {
            if (template == null || template.Columns.Count == 0) return null;

            var scope = Materialise(elements);

            if (scope.Count == 0) return null;

            var best = Fields(scope)
                .Where(f => f.Filled * 2 >= scope.Count && f.Distinct > 1 && f.Distinct <= 50)
                .OrderByDescending(f => f.Filled)
                .FirstOrDefault();

            if (best == null)
            {
                var category = template.Columns.FirstOrDefault(c => c.Field == ReportField.Category);
                return category?.Heading;
            }

            var match = template.Columns.FirstOrDefault(
                c => c.Field == ReportField.Property && c.Source == best.Field);

            return match?.Heading;
        }

        /// <summary>
        /// Build the report.
        ///
        /// A null template is not an error — it is the ordinary first press, and it proposes one.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="template">The template, or null to work one out.</param>
        /// <param name="units">How to write quantities on folded columns.</param>
        public static ReportTable Build(IEnumerable<ReportElement>? elements, ReportTemplate? template,
                                        Measure? units = null)
        {
            var scope = Materialise(elements);
            var recipe = template ?? Propose(scope);
            var notes = new List<string>();
            var none = Array.Empty<ReportRow>();

            if (recipe.Columns.Count == 0)
                return new ReportTable(recipe.Name, Array.Empty<string>(), none, scope.Count, 0,
                    false, false, notes,
                    "That report has no columns yet. Pick the properties you want in it, or press "
                    + "Suggest and CamelWorks will read the model and propose a set.");

            if (scope.Count == 0)
                return new ReportTable(recipe.Name, Headings(recipe, false), none, 0, 0, false, false,
                    notes,
                    "There are no elements in scope, so there is nothing to report on. Widen the "
                    + "scope, or select something in the model first.");

            var groupColumn = -1;

            if (recipe.GroupBy != null)
            {
                groupColumn = IndexOf(recipe, recipe.GroupBy);

                if (groupColumn < 0)
                    notes.Add("There is no \"" + recipe.GroupBy + "\" column any more, so nothing "
                              + "was grouped.");
            }

            var grouped = groupColumn >= 0;
            var identity = recipe.WithIdentity && !grouped;

            if (recipe.WithIdentity && grouped)
                notes.Add("A grouped report has no single element behind each row, so the identity "
                          + "column is left off and this sheet cannot be sent back in.");

            var headings = Headings(recipe, identity);

            var rows = grouped
                ? Fold(scope, recipe, groupColumn, identity, units, notes)
                : Flat(scope, recipe, identity);

            rows = Sort(rows, recipe, headings);

            var ceiling = recipe.Ceiling > 0 ? recipe.Ceiling : ReportTemplate.DefaultCeiling;
            var dropped = 0;

            if (rows.Count > ceiling)
            {
                dropped = rows.Count - ceiling;
                rows = rows.Take(ceiling).ToList();
            }

            return new ReportTable(recipe.Name, headings, rows, scope.Count, dropped, grouped,
                                   identity, notes, null);
        }

        /// <summary>
        /// The report as a worksheet.
        ///
        /// Row one is the headings and nothing else, because that is what
        /// <see cref="SheetImport"/> reads when the sheet comes back.
        /// </summary>
        /// <param name="table">The built report.</param>
        public static XlsxSheet Sheet(ReportTable? table)
        {
            var sheet = new XlsxSheet(XlsxSheet.Sanitise(table?.Name ?? "Model data"));

            if (table == null) return sheet;

            sheet.Row(table.Headings.Select(h => (string?)h).ToArray());

            foreach (var row in table.Rows)
                sheet.Row(row.Cells.ToArray());

            return sheet;
        }

        /// <summary>
        /// The report as a workbook: the data on the first sheet, how it was made on the second.
        ///
        /// The second sheet is not decoration. A sheet that has been round the houses and come back
        /// six weeks later is unreadable without knowing which properties it was cut from, whether
        /// it was grouped, and whether the blanks mean "nothing there" or "not asked for".
        /// </summary>
        /// <param name="table">The built report.</param>
        /// <param name="template">The template it was built from.</param>
        public static IReadOnlyList<XlsxSheet> Workbook(ReportTable? table, ReportTemplate? template)
        {
            var sheets = new List<XlsxSheet> { Sheet(table) };

            var about = new XlsxSheet("How this was made");
            about.FreezeHeader = false;

            about.Row("Report", table?.Name ?? template?.Name ?? "Model data");
            about.Row("What happened", table?.Explain() ?? "It was not built.");
            about.Row(null, null);
            about.Row("Column", "Reads", "When rows are folded", "You may edit it");

            foreach (var column in template?.Columns ?? Array.Empty<ReportColumn>())
                about.Row(column.Heading, Source(column), Folding(column.Roll),
                          column.Editable ? "yes" : "no");

            if (table != null && table.WithIdentity)
            {
                about.Row(null, null);
                about.Row("Sending it back",
                    "Fill in the columns marked editable, keep the \"" + SheetImport.KeyHeading
                    + "\" column exactly as it is, and bring the file back through Data ▸ Bring a "
                    + "sheet back in. Rows whose identity has been changed or removed cannot be "
                    + "matched and will be listed rather than written.");
            }

            sheets.Add(about);

            return sheets;
        }

        /// <summary>
        /// The report as comma-separated text.
        ///
        /// Here because the ask is usually "get this into Access" or "get this into the scheduling
        /// package", and CSV is the one thing every such package reads. CamelWorks does not write
        /// .mdb — that is a Microsoft database format with no cross-platform writer worth trusting,
        /// and an Access import of this file lands in the same place.
        /// </summary>
        /// <param name="table">The built report.</param>
        public static string Csv(ReportTable? table)
        {
            if (table == null) return string.Empty;

            var lines = new List<string>
            {
                string.Join(",", table.Headings.Select(Quote)),
            };

            foreach (var row in table.Rows)
                lines.Add(string.Join(",", row.Cells.Select(c => Quote(c ?? string.Empty))));

            return string.Join("\r\n", lines);
        }

        /// <summary>Every saved template, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<ReportTemplate> Saved(JsonValue? json)
        {
            var saved = new List<ReportTemplate>();

            if (json == null) return saved;

            foreach (var item in json.Items)
            {
                var template = ReportTemplate.FromJson(item);
                if (template.Columns.Count > 0) saved.Add(template);
            }

            return saved;
        }

        /// <summary>
        /// The list with one template put in it, replacing any of the same name.
        /// </summary>
        /// <param name="saved">What is saved now.</param>
        /// <param name="template">The one to keep.</param>
        public static JsonValue Keep(IEnumerable<ReportTemplate>? saved, ReportTemplate? template)
        {
            var kept = (saved ?? Enumerable.Empty<ReportTemplate>()).ToList();

            if (template != null && template.Columns.Count > 0)
            {
                kept.RemoveAll(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
                kept.Add(template);
            }

            return JsonValue.Array(kept.Select(t => t.ToJson()));
        }

        private static IReadOnlyList<ReportElement> Materialise(IEnumerable<ReportElement>? elements) =>
            elements == null
                ? Array.Empty<ReportElement>()
                : elements.Where(e => e != null).ToList();

        private static IEnumerable<FieldSummary> Worth(IReadOnlyList<FieldSummary> fields, int floor) =>
            fields.Where(f => f.Filled >= floor && !f.IsConstant && !f.IsIdentifier);

        private static IReadOnlyList<string> Headings(ReportTemplate template, bool identity)
        {
            var headings = new List<string>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (identity)
            {
                headings.Add(SheetImport.KeyHeading);
                taken.Add(SheetImport.KeyHeading);
            }

            foreach (var column in template.Columns)
            {
                var heading = column.Heading;

                if (taken.Contains(heading))
                {
                    var attempt = 2;
                    while (taken.Contains(heading + " (" + attempt.ToString(CultureInfo.InvariantCulture) + ")"))
                        attempt++;

                    heading += " (" + attempt.ToString(CultureInfo.InvariantCulture) + ")";
                }

                headings.Add(heading);
                taken.Add(heading);
            }

            return headings;
        }

        private static int IndexOf(ReportTemplate template, string heading)
        {
            for (var i = 0; i < template.Columns.Count; i++)
                if (string.Equals(template.Columns[i].Heading, heading, StringComparison.OrdinalIgnoreCase))
                    return i;

            return -1;
        }

        private static IReadOnlyList<ReportRow> Flat(IReadOnlyList<ReportElement> scope,
                                                     ReportTemplate template, bool identity)
        {
            var rows = new List<ReportRow>(scope.Count);

            foreach (var element in scope)
            {
                var cells = new List<string?>(template.Columns.Count + 1);

                if (identity) cells.Add(element.Key.ToString());

                foreach (var column in template.Columns)
                    cells.Add(Show(column.Read(element), template.BlanksShown));

                rows.Add(new ReportRow(element.Key.ToString(), cells, 1));
            }

            return rows;
        }

        private static IReadOnlyList<ReportRow> Fold(IReadOnlyList<ReportElement> scope,
                                                     ReportTemplate template, int groupColumn,
                                                     bool identity, Measure? units, IList<string> notes)
        {
            var order = new List<string>();
            var buckets = new Dictionary<string, List<ReportElement>>(StringComparer.OrdinalIgnoreCase);
            var on = template.Columns[groupColumn];

            foreach (var element in scope)
            {
                var raw = on.Read(element);
                var bucket = string.IsNullOrWhiteSpace(raw) ? Blank : raw!.Trim();

                if (!buckets.TryGetValue(bucket, out var members))
                {
                    members = new List<ReportElement>();
                    buckets.Add(bucket, members);
                    order.Add(bucket);
                }

                members.Add(element);
            }

            var mixed = new List<string>();
            var rows = new List<ReportRow>(order.Count);

            foreach (var bucket in order)
            {
                var members = buckets[bucket];
                var cells = new List<string?>(template.Columns.Count + 1);

                if (identity) cells.Add(bucket);

                for (var i = 0; i < template.Columns.Count; i++)
                {
                    var column = template.Columns[i];

                    if (i == groupColumn) { cells.Add(bucket); continue; }

                    var folded = Roll(column, members, units, out var unaddable);

                    if (unaddable && !mixed.Contains(column.Heading)) mixed.Add(column.Heading);

                    cells.Add(folded);
                }

                rows.Add(new ReportRow(bucket, cells, members.Count));
            }

            if (mixed.Count > 0)
                notes.Add(Sentence(mixed) + " could not be added up, because the values under "
                          + (mixed.Count == 1 ? "it are" : "them are") + " not all numbers of the "
                          + "same kind. Those cells say \"" + Mixed + "\".");

            return rows;
        }

        private static string? Roll(ReportColumn column, IReadOnlyList<ReportElement> members,
                                    Measure? units, out bool unaddable)
        {
            unaddable = false;

            var read = members.Select(column.Read)
                              .Where(v => !string.IsNullOrWhiteSpace(v))
                              .Select(v => v!.Trim())
                              .ToList();

            switch (column.Roll)
            {
                case ReportRoll.Count:
                    return members.Count.ToString("N0", CultureInfo.InvariantCulture);

                case ReportRoll.Distinct:
                    return read.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                               .ToString("N0", CultureInfo.InvariantCulture);

                case ReportRoll.List:
                    return Listed(read);

                case ReportRoll.Sum:
                case ReportRoll.Least:
                case ReportRoll.Most:
                case ReportRoll.Mean:
                    return Arithmetic(column.Roll, read, units, out unaddable);

                default:
                    var distinct = read.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (distinct.Count == 0) return null;

                    return distinct.Count == 1 ? distinct[0] : Varies;
            }
        }

        private static string? Listed(IReadOnlyList<string> read)
        {
            var distinct = read.Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                               .ToList();

            if (distinct.Count == 0) return null;
            if (distinct.Count <= ListCap) return string.Join(", ", distinct);

            var more = distinct.Count - ListCap;

            return string.Join(", ", distinct.Take(ListCap))
                 + " and " + more.ToString("N0", CultureInfo.InvariantCulture) + " more";
        }

        private static string? Arithmetic(ReportRoll roll, IReadOnlyList<string> read, Measure? units,
                                          out bool unaddable)
        {
            unaddable = false;

            if (read.Count == 0) return null;

            var parsed = new List<Quantity>(read.Count);

            foreach (var value in read)
            {
                if (!Quantity.TryParse(value, out var quantity))
                {
                    unaddable = true;
                    return Mixed;
                }

                parsed.Add(quantity);
            }

            var kind = parsed[0].Kind;

            if (parsed.Any(q => q.Kind != kind))
            {
                unaddable = true;
                return Mixed;
            }

            var values = parsed.Select(q => q.Value).ToList();

            var answer = roll switch
            {
                ReportRoll.Least => values.Min(),
                ReportRoll.Most => values.Max(),
                ReportRoll.Mean => values.Average(),
                _ => values.Sum(),
            };

            return Rebuild(answer, kind).Show(units);
        }

        private static Quantity Rebuild(double value, QuantityKind kind) => kind switch
        {
            QuantityKind.Length => Quantity.Metres(value),
            QuantityKind.Area => Quantity.SquareMetres(value),
            QuantityKind.Volume => Quantity.CubicMetres(value),
            QuantityKind.Mass => Quantity.Kilograms(value),
            QuantityKind.Angle => Quantity.Degrees(value),
            _ => Quantity.Scalar(value),
        };

        private static IReadOnlyList<ReportRow> Sort(IReadOnlyList<ReportRow> rows,
                                                     ReportTemplate template,
                                                     IReadOnlyList<string> headings)
        {
            if (template.SortBy == null || rows.Count < 2) return rows;

            var column = -1;

            for (var i = 0; i < headings.Count; i++)
                if (string.Equals(headings[i], template.SortBy, StringComparison.OrdinalIgnoreCase))
                { column = i; break; }

            if (column < 0) return rows;

            var numeric = rows.All(r =>
            {
                var cell = r.Cell(column);
                return string.IsNullOrWhiteSpace(cell)
                    || double.TryParse(cell, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
            });

            var sorted = numeric
                ? rows.OrderBy(r => Number(r.Cell(column))).ToList()
                : rows.OrderBy(r => r.Cell(column) ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();

            if (template.Descending) sorted.Reverse();

            return sorted;
        }

        private static double Number(string? cell) =>
            double.TryParse(cell, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.MinValue;

        private static string? Show(string? value, bool blanksShown) =>
            string.IsNullOrWhiteSpace(value) ? (blanksShown ? Blank : null) : value!.Trim();

        private static void Disambiguate(ReportTemplate template)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in template.Columns)
                counts[column.Heading] = counts.TryGetValue(column.Heading, out var seen) ? seen + 1 : 1;

            foreach (var column in template.Columns)
            {
                if (column.Field != ReportField.Property) continue;
                if (counts[column.Heading] < 2) continue;
                if (column.Source.Category.Length == 0) continue;

                column.Heading = column.Source.Category + " / " + column.Source.Name;
            }
        }

        private static string Source(ReportColumn column) => column.Field switch
        {
            ReportField.Name => "the name in the selection tree",
            ReportField.Category => "the element's category",
            ReportField.Type => "the element's type or family",
            ReportField.Model => "which model it came from",
            ReportField.Path => "the whole selection-tree path",
            ReportField.PathLevel => "selection-tree level "
                                     + column.Level.ToString(CultureInfo.InvariantCulture),
            ReportField.Identity => "the element's CamelWorks identity",
            _ => column.Source.Category.Length == 0
                    ? "the property \"" + column.Source.Name + "\", on any tab"
                    : "the property \"" + column.Source.Name + "\" on the "
                      + column.Source.Category + " tab",
        };

        private static string Folding(ReportRoll roll) => roll switch
        {
            ReportRoll.Count => "counts the elements",
            ReportRoll.Distinct => "counts the different values",
            ReportRoll.List => "lists the different values",
            ReportRoll.Sum => "adds them up",
            ReportRoll.Least => "takes the smallest",
            ReportRoll.Most => "takes the largest",
            ReportRoll.Mean => "averages them",
            _ => "shows the shared value, or \"" + Varies + "\"",
        };

        private static string Sentence(IReadOnlyList<string> names)
        {
            if (names.Count == 1) return "\"" + names[0] + "\"";

            var quoted = names.Select(n => "\"" + n + "\"").ToList();

            return string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[quoted.Count - 1];
        }

        private static string Quote(string cell)
        {
            var needs = cell.IndexOf(',') >= 0 || cell.IndexOf('"') >= 0
                     || cell.IndexOf('\n') >= 0 || cell.IndexOf('\r') >= 0;

            return needs ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }
    }
}
