using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Data
{
    /// <summary>How a row in the sheet finds the element it is about.</summary>
    public enum LinkMatch
    {
        /// <summary>By the identity CamelWorks wrote into the sheet when it exported it.</summary>
        Identity = 0,

        /// <summary>By a property on the element - an asset tag, a mark, a drawing number.</summary>
        Property,

        /// <summary>By the name the selection tree shows.</summary>
        Name,
    }

    /// <summary>What one mapped column does.</summary>
    public enum LinkAction
    {
        /// <summary>Put the sheet's value on the element, over whatever is there.</summary>
        Write = 0,

        /// <summary>Put the sheet's value on the element only where the element holds nothing.</summary>
        Fill,

        /// <summary>Leave the element alone and put its value into the sheet instead.</summary>
        Read,

        /// <summary>Do nothing with it.</summary>
        Ignore,
    }

    /// <summary>One column of the sheet, and what it has to do with the model.</summary>
    public sealed class LinkColumn
    {
        /// <summary>Map a column.</summary>
        /// <param name="heading">The heading in the sheet.</param>
        /// <param name="category">The property tab it lands on.</param>
        /// <param name="property">The property name it lands on.</param>
        /// <param name="action">What it does.</param>
        public LinkColumn(string heading, string? category = null, string? property = null,
                          LinkAction action = LinkAction.Write)
        {
            Heading = (heading ?? string.Empty).Trim();
            Category = string.IsNullOrWhiteSpace(category) ? DataLink.DefaultTab : category!.Trim();
            Property = string.IsNullOrWhiteSpace(property) ? Heading : property!.Trim();
            Action = action;
        }

        /// <summary>The heading in the sheet.</summary>
        public string Heading { get; }

        /// <summary>
        /// The tab the model holds this on.
        ///
        /// Where the value is READ from - which is what a Read column sends back, and what a Fill
        /// column tests to see whether there is a gap to fill. Where a written value LANDS is not
        /// this: a Navisworks plug-in may only write into a tab of its own, so every write goes to
        /// the CamelWorks tab whatever this says. Naming the authoring tool's tab here is still the
        /// right thing to do - it is how "only fill it in where the model has not already said"
        /// means what a person expects it to.
        /// </summary>
        public string Category { get; set; }

        /// <summary>The property name.</summary>
        public string Property { get; set; }

        /// <summary>What it does.</summary>
        public LinkAction Action { get; set; }

        /// <summary>Where the model holds it, as a reference.</summary>
        public PropertyRef Target => new PropertyRef(Category, Property);

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("heading", Heading)
                .Set("category", Category)
                .Set("property", Property)
                .Set("action", Action.ToString());

        /// <summary>Read one back, or null when there is no heading to read.</summary>
        /// <param name="json">What was written.</param>
        public static LinkColumn? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var heading = (json["heading"].AsString() ?? string.Empty).Trim();

            if (heading.Length == 0) return null;

            if (!Enum.TryParse<LinkAction>(json["action"].AsString() ?? "Write", out var action))
                action = LinkAction.Write;

            return new LinkColumn(heading, json["category"].AsString(), json["property"].AsString(),
                                  action);
        }

        /// <inheritdoc />
        public override string ToString() => Heading + " -> " + Category + " / " + Property;
    }

    /// <summary>A saved link between a sheet and the model.</summary>
    public sealed class DataLinkRecipe
    {
        /// <summary>Start a link.</summary>
        /// <param name="name">What to call it.</param>
        public DataLinkRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Data link" : name!.Trim();
        }

        /// <summary>What to call it.</summary>
        public string Name { get; set; }

        /// <summary>The file it reads, so running it again is one press.</summary>
        public string? File { get; set; }

        /// <summary>Which worksheet in that file, or null for the first readable one.</summary>
        public string? Sheet { get; set; }

        /// <summary>How a row finds its element.</summary>
        public LinkMatch Match { get; set; } = LinkMatch.Identity;

        /// <summary>The heading of the column that identifies the element.</summary>
        public string KeyHeading { get; set; } = SheetImport.KeyHeading;

        /// <summary>
        /// Which property the key column is matched against, when matching on a property.
        ///
        /// Separate from the heading because they are almost never the same word: a sheet says
        /// "Asset No." and the model says "Asset Tag", and pretending otherwise is what makes a
        /// link tool fail on the first real file.
        /// </summary>
        public PropertyRef KeyProperty { get; set; }

        /// <summary>The columns, and what each does.</summary>
        public IList<LinkColumn> Columns { get; } = new List<LinkColumn>();

        /// <summary>Add a column, unless the link is already full.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The column, or null when there was no room.</returns>
        public LinkColumn? Add(LinkColumn? column)
        {
            if (column == null || Columns.Count >= DataLink.MaxColumns) return null;

            Columns.Add(column);
            return column;
        }

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public DataLinkRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("name", Name)
                .Set("match", Match.ToString())
                .Set("key", KeyHeading)
                .Set("columns", JsonValue.Array(Columns.Select(c => c.ToJson())));

            if (File != null) json.Set("file", File);
            if (Sheet != null) json.Set("sheet", Sheet);

            if (KeyProperty.Name.Length > 0)
            {
                json.Set("keyProperty", KeyProperty.Name);
                if (KeyProperty.Category.Length > 0) json.Set("keyCategory", KeyProperty.Category);
            }

            return json;
        }

        /// <summary>Read one back. An unreadable one comes back empty rather than throwing.</summary>
        /// <param name="json">What was written.</param>
        public static DataLinkRecipe FromJson(JsonValue? json)
        {
            var recipe = new DataLinkRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            if (Enum.TryParse<LinkMatch>(json["match"].AsString() ?? "Identity", out var match))
                recipe.Match = match;

            var key = (json["key"].AsString() ?? string.Empty).Trim();

            recipe.KeyHeading = key.Length == 0 ? SheetImport.KeyHeading : key;
            recipe.File = json["file"].AsString();
            recipe.Sheet = json["sheet"].AsString();

            recipe.KeyProperty = new PropertyRef((json["keyCategory"].AsString() ?? string.Empty).Trim(),
                                                 (json["keyProperty"].AsString() ?? string.Empty).Trim());

            foreach (var item in json["columns"].Items) recipe.Add(LinkColumn.FromJson(item));

            return recipe;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + Columns.Count(c => c.Action != LinkAction.Ignore).ToString(
                "N0", CultureInfo.InvariantCulture) + " mapped";
    }

    /// <summary>One row of the sheet, as the link reads it.</summary>
    public sealed class LinkRow
    {
        internal LinkRow(int number, string key, IReadOnlyList<string?> cells)
        {
            Number = number;
            Key = key;
            Cells = cells;
        }

        /// <summary>Which row of the sheet, counting the heading row as 1.</summary>
        public int Number { get; }

        /// <summary>What was in the key column.</summary>
        public string Key { get; }

        /// <summary>The cells.</summary>
        public IReadOnlyList<string?> Cells { get; }

        /// <summary>Read one cell.</summary>
        /// <param name="column">Its index.</param>
        public string? Cell(int column) =>
            column >= 0 && column < Cells.Count ? Cells[column] : null;

        /// <inheritdoc />
        public override string ToString() =>
            "row " + Number.ToString(CultureInfo.InvariantCulture) + ": " + Key;
    }

    /// <summary>One property the link would write, and what it would put on each element.</summary>
    public sealed class LinkWrite
    {
        internal LinkWrite(string category, string property, LinkAction action,
                           IReadOnlyDictionary<ElementKey, string?> values)
        {
            Category = category;
            Property = property;
            Action = action;
            Values = values;
        }

        /// <summary>
        /// The tab the model holds this on, which is not necessarily where a write can land.
        ///
        /// See <see cref="LinkColumn.Category"/>: the host decides where a plug-in may write, and
        /// the caller doing the writing is the one that knows what its host allows.
        /// </summary>
        public string Category { get; }

        /// <summary>The property name.</summary>
        public string Property { get; }

        /// <summary>Whether it writes over what is there, or only fills a gap.</summary>
        public LinkAction Action { get; }

        /// <summary>What each element would end up holding.</summary>
        public IReadOnlyDictionary<ElementKey, string?> Values { get; }

        /// <summary>How many elements it touches.</summary>
        public int Count => Values.Count;

        /// <summary>What to look up for one element, or null when the sheet said nothing about it.</summary>
        /// <param name="key">The element.</param>
        public string? For(ElementKey key) => Values.TryGetValue(key, out var value) ? value : null;

        /// <summary>Whether the sheet said anything at all about one element.</summary>
        /// <param name="key">The element.</param>
        public bool Mentions(ElementKey key) => Values.ContainsKey(key);

        /// <inheritdoc />
        public override string ToString() =>
            Category + " / " + Property + " on "
            + Count.ToString("N0", CultureInfo.InvariantCulture)
            + (Count == 1 ? " element" : " elements");
    }

    /// <summary>What a link would do, before it does any of it.</summary>
    public sealed class LinkPlan
    {
        internal LinkPlan(IReadOnlyList<LinkWrite> writes, IReadOnlyList<LinkRow> orphans,
                          IReadOnlyList<ElementKey> untouched, int rows, int matched, int elements,
                          int spread, IReadOnlyList<string> notes, string? refusal)
        {
            Writes = writes;
            Orphans = orphans;
            Untouched = untouched;
            Rows = rows;
            Matched = matched;
            Elements = elements;
            Spread = spread;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>One entry per mapped property.</summary>
        public IReadOnlyList<LinkWrite> Writes { get; }

        /// <summary>Rows whose key matched nothing in the model.</summary>
        public IReadOnlyList<LinkRow> Orphans { get; }

        /// <summary>Elements in scope that no row mentioned.</summary>
        public IReadOnlyList<ElementKey> Untouched { get; }

        /// <summary>How many rows the sheet had, not counting the heading.</summary>
        public int Rows { get; }

        /// <summary>How many rows found at least one element.</summary>
        public int Matched { get; }

        /// <summary>How many elements the link would touch.</summary>
        public int Elements { get; }

        /// <summary>How many rows landed on more than one element.</summary>
        public int Spread { get; }

        /// <summary>Anything worth saying about how it was worked out.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is no plan, when there is none.</summary>
        public string? Refusal { get; }

        /// <summary>True when it would do something.</summary>
        public bool IsUsable => Refusal == null && Writes.Count > 0 && Elements > 0;

        /// <summary>What it would do, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            if (Matched == 0)
                return "None of the " + Rows.ToString("N0", CultureInfo.InvariantCulture)
                     + " rows in that sheet matched anything in the model. "
                     + "Check the column being matched on, and that the scope holds the elements "
                     + "the sheet is about.";

            var text = Matched.ToString("N0", CultureInfo.InvariantCulture) + " of "
                     + Rows.ToString("N0", CultureInfo.InvariantCulture) + " rows matched "
                     + Elements.ToString("N0", CultureInfo.InvariantCulture)
                     + (Elements == 1 ? " element" : " elements") + ", across "
                     + Writes.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Writes.Count == 1 ? " property." : " properties.");

            if (Spread > 0)
                text += " " + Spread.ToString("N0", CultureInfo.InvariantCulture)
                     + (Spread == 1 ? " row covers" : " rows cover")
                     + " more than one element, which is ordinary in a register keyed by tag.";

            if (Orphans.Count > 0)
                text += " " + Orphans.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Orphans.Count == 1 ? " row is" : " rows are")
                     + " about something not in the model.";

            if (Untouched.Count > 0)
                text += " " + Untouched.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Untouched.Count == 1 ? " element is" : " elements are")
                     + " not in the sheet and will be left alone.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>The sheet with the model's own values put back into it.</summary>
    public sealed class LinkReturn
    {
        internal LinkReturn(XlsxSheet sheet, int filled, int changed, int same, int missing,
                            IReadOnlyList<string> columns, string? refusal)
        {
            Sheet = sheet;
            Filled = filled;
            Changed = changed;
            Same = same;
            Missing = missing;
            Columns = columns;
            Refusal = refusal;
        }

        /// <summary>The sheet, ready to be written back over the file it came from.</summary>
        public XlsxSheet Sheet { get; }

        /// <summary>Cells that were empty and now hold something.</summary>
        public int Filled { get; }

        /// <summary>Cells that held something different.</summary>
        public int Changed { get; }

        /// <summary>Cells that already agreed with the model.</summary>
        public int Same { get; }

        /// <summary>Cells left alone because the model had nothing to put there.</summary>
        public int Missing { get; }

        /// <summary>Which columns were sent back.</summary>
        public IReadOnlyList<string> Columns { get; }

        /// <summary>Why there is nothing to send back, when there is nothing.</summary>
        public string? Refusal { get; }

        /// <summary>True when the file would come out different.</summary>
        public bool IsUsable => Refusal == null && (Filled > 0 || Changed > 0);

        /// <summary>What it would do, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            if (Filled == 0 && Changed == 0)
                return "The sheet already agrees with the model - " + Plural(Same, "cell")
                     + " checked, nothing to change"
                     + (Missing > 0
                         ? ", and " + Plural(Missing, "cell") + " the model has no value for."
                         : ".");

            var text = Plural(Filled + Changed, "cell") + " to send back: "
                     + Filled.ToString("N0", CultureInfo.InvariantCulture) + " empty, "
                     + Changed.ToString("N0", CultureInfo.InvariantCulture) + " different. "
                     + Plural(Same, "cell") + " already agreed.";

            if (Missing > 0)
                text += " " + Plural(Missing, "cell") + " left alone - the model holds nothing there.";

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static string Plural(int count, string unit) =>
            count.ToString("N0", CultureInfo.InvariantCulture) + " "
            + (count == 1 ? unit : unit + "s");
    }

    /// <summary>
    /// A spreadsheet and the model, joined on a column, in both directions.
    ///
    /// The one-time import already existed: export a sheet, fill it in, bring it back. That works
    /// because CamelWorks put its own identities in the sheet. It does nothing for a file somebody
    /// else produced — an asset register keyed on tag, a procurement extract keyed on order number, a
    /// programme export keyed on activity code — which is most files that matter.
    ///
    /// So the join is explicit. Say which column identifies the element, say what on the element it
    /// is matched against, and say what each other column does: write it on, fill a gap with it, or
    /// put the model's value into the sheet. That last is the write-back: the register is the system
    /// of record for the tag and the model is the system of record for the level and the size, and
    /// both directions have to work.
    ///
    /// It takes files rather than databases, and reaches a database through the export every
    /// database has. That is a choice: an ODBC link needs a driver installed on each machine and a
    /// DSN configured, it puts a database round trip on the thread that draws the model, and a query
    /// that hangs takes Navisworks with it. A CSV or XLSX drop is what an asset system can produce
    /// unattended, and re-running a saved link against a newer drop is one press.
    ///
    /// Nothing here has to be set up first. <see cref="Propose"/> reads the sheet and the model
    /// together and works out which column is the key by seeing which one's values appear in the
    /// model, then maps the rest by name. A saved link makes that repeatable and is never the price
    /// of the first run.
    /// </summary>
    public static class DataLink
    {
        /// <summary>The tab a linked property lands on when nothing says otherwise.</summary>
        public const string DefaultTab = "CamelWorks";

        /// <summary>How many columns one link will map.</summary>
        public const int MaxColumns = 60;

        /// <summary>How many rows one link will read.</summary>
        public const int MaxRows = 100000;

        /// <summary>
        /// How much of a column's values have to be found in the model before it is believed to be
        /// the key.
        /// </summary>
        public const double KeyConfidence = 0.5;

        /// <summary>Where saved links live in the project store.</summary>
        public const string StoreKey = "links";

        /// <summary>Every heading the sheet has, trimmed, with the blanks left out.</summary>
        /// <param name="sheet">The worksheet.</param>
        public static IReadOnlyList<string> Headings(SheetTable? sheet) =>
            sheet == null
                ? Array.Empty<string>()
                : sheet.Headers
                       .Where(h => !string.IsNullOrWhiteSpace(h))
                       .Select(h => h!.Trim())
                       .ToList();

        /// <summary>
        /// Work out the link from the sheet and the model together.
        ///
        /// The key column is found by seeing which one's values actually turn up in the model, not
        /// by looking for a promising heading - "Tag", "Asset No." and "Equipment Reference" are
        /// the same column on three jobs, and none of them is a name a tool can be taught.
        /// </summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="elements">The elements in scope.</param>
        public static DataLinkRecipe Propose(SheetTable? sheet, IEnumerable<ReportElement>? elements)
        {
            var recipe = new DataLinkRecipe(sheet == null ? "Data link" : sheet.Name);
            var headings = Headings(sheet);

            if (sheet == null || headings.Count == 0) return recipe;

            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();

            var identity = sheet.ColumnOf(SheetImport.KeyHeading);

            if (identity >= 0)
            {
                recipe.Match = LinkMatch.Identity;
                recipe.KeyHeading = SheetImport.KeyHeading;
            }
            else
            {
                var found = FindKey(sheet, headings, scope);

                if (found == null) return recipe;

                recipe.Match = found.Match;
                recipe.KeyHeading = found.Heading;
                recipe.KeyProperty = found.Property;
            }

            var known = scope.SelectMany(e => e.Fields)
                             .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var heading in headings)
            {
                if (string.Equals(heading, recipe.KeyHeading, StringComparison.OrdinalIgnoreCase))
                    continue;

                var landing = known.TryGetValue(heading, out var existing)
                    ? existing
                    : new PropertyRef(DefaultTab, heading);

                recipe.Add(new LinkColumn(heading, landing.Category, landing.Name));
            }

            return recipe;
        }

        /// <summary>
        /// Work out what the link would do.
        ///
        /// Nothing is written here. The plan says which elements would end up holding what, and the
        /// caller turns each entry into an ordinary property write with the ordinary preview -
        /// there is no second, subtly different idea of what a write means in this file.
        /// </summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">The link.</param>
        public static LinkPlan Plan(SheetTable? sheet, IEnumerable<ReportElement>? elements,
                                    DataLinkRecipe? recipe)
        {
            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();
            var link = recipe ?? Propose(sheet, scope);
            var notes = new List<string>();

            var refusal = Refuses(sheet, scope, link);

            if (refusal != null) return Nothing(notes, refusal);

            // Named once, non-null, because the refusal above has already established it and a
            // lambda further down cannot see a null-forgiving operator used up here.
            var table = sheet!;

            var column = table.ColumnOf(link.KeyHeading);
            var rows = Read(table, column, notes);

            if (rows.Count == 0)
                return Nothing(notes,
                    "There are no rows under the \"" + link.KeyHeading + "\" heading, so there is "
                    + "nothing to link. Check that the headings are on the first row of the sheet.");

            var index = Index(scope, link);
            var mapped = link.Columns.Where(c => c.Action == LinkAction.Write
                                                 || c.Action == LinkAction.Fill).ToList();

            if (mapped.Count == 0)
                return Nothing(notes,
                    "No column is set to write anything on to the model. Map at least one, or use "
                    + "Send back to put the model's values into the sheet instead.");

            var values = new Dictionary<PropertyRef, Dictionary<ElementKey, string?>>();
            var orphans = new List<LinkRow>();
            var touched = new HashSet<ElementKey>();
            var matched = 0;
            var spread = 0;

            foreach (var row in rows)
            {
                if (!index.TryGetValue(row.Key, out var hits) || hits.Count == 0)
                {
                    orphans.Add(row);
                    continue;
                }

                matched++;

                if (hits.Count > 1) spread++;

                foreach (var element in hits)
                {
                    touched.Add(element.Key);

                    foreach (var mapping in mapped)
                    {
                        var at = table.ColumnOf(mapping.Heading);

                        if (at < 0) continue;

                        var value = row.Cell(at);

                        if (mapping.Action == LinkAction.Fill
                            && !string.IsNullOrWhiteSpace(element.Value(mapping.Target)))
                            continue;

                        if (!values.TryGetValue(mapping.Target, out var onto))
                        {
                            onto = new Dictionary<ElementKey, string?>();
                            values.Add(mapping.Target, onto);
                        }

                        onto[element.Key] = string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
                    }
                }
            }

            var missing = link.Columns
                .Where(c => c.Action != LinkAction.Ignore && table.ColumnOf(c.Heading) < 0)
                .Select(c => c.Heading)
                .ToList();

            if (missing.Count > 0)
                notes.Add(Sentence(missing) + (missing.Count == 1 ? " is" : " are")
                          + " mapped but not in this sheet any more, so nothing came from "
                          + (missing.Count == 1 ? "it." : "them."));

            var duplicated = rows.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Count() > 1)
                                 .ToList();

            if (duplicated.Count > 0)
                notes.Add(duplicated.Count.ToString("N0", CultureInfo.InvariantCulture)
                          + (duplicated.Count == 1 ? " key appears" : " keys appear")
                          + " on more than one row. The last row wins, which is a guess - fix the "
                          + "sheet if the rows disagree.");

            var writes = mapped
                .Select(m => m.Target)
                .Distinct()
                .Where(values.ContainsKey)
                .Select(target => new LinkWrite(
                    target.Category, target.Name,
                    mapped.First(m => m.Target == target).Action,
                    values[target]))
                .ToList();

            var untouched = scope.Select(e => e.Key).Where(k => !touched.Contains(k)).ToList();

            return new LinkPlan(writes, orphans, untouched, rows.Count, matched, touched.Count,
                                spread, notes, null);
        }

        /// <summary>
        /// The sheet with the model's own values put into the columns marked to be read.
        ///
        /// This is the half that makes a link a link rather than an import. The register owns the
        /// tag and the model owns the size, and a tool that only ever writes one way leaves
        /// somebody retyping the other way.
        /// </summary>
        /// <param name="sheet">The worksheet, as it came in.</param>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">The link.</param>
        public static LinkReturn Back(SheetTable? sheet, IEnumerable<ReportElement>? elements,
                                      DataLinkRecipe? recipe)
        {
            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();
            var link = recipe ?? Propose(sheet, scope);

            var refusal = Refuses(sheet, scope, link);

            if (refusal != null) return Empty(refusal);

            var reading = link.Columns.Where(c => c.Action == LinkAction.Read).ToList();

            if (reading.Count == 0)
                return Empty("No column is set to be sent back. Set one to \"take the model's "
                             + "value\" and it will be filled in from the model.");

            var table = sheet!;
            var column = table.ColumnOf(link.KeyHeading);
            var index = Index(scope, link);

            // Every row is rewritten, matched or not, because what comes out of here goes back over
            // the file it came from. A returned sheet holding only the rows that matched is not the
            // register any more.
            var width = Math.Max(table.Columns, table.Headers.Count);
            var built = new XlsxSheet(XlsxSheet.Sanitise(table.Name));

            built.Row(Line(table.Headers, width));

            var filled = 0;
            var changed = 0;
            var same = 0;
            var absent = 0;

            var at = reading.Select(c => (Column: table.ColumnOf(c.Heading), Mapping: c))
                            .Where(pair => pair.Column >= 0)
                            .ToList();

            for (var r = 1; r < table.Rows.Count; r++)
            {
                var cells = Line(table.Rows[r], width);
                var key = (column >= 0 && column < cells.Length ? cells[column] : null) ?? string.Empty;

                if (index.TryGetValue(key.Trim(), out var hits) && hits.Count > 0)
                {
                    foreach (var pair in at)
                    {
                        var value = hits[0].Value(pair.Mapping.Target);

                        if (string.IsNullOrWhiteSpace(value)) { absent++; continue; }

                        var was = cells[pair.Column];

                        if (string.IsNullOrWhiteSpace(was)) filled++;
                        else if (string.Equals(was!.Trim(), value!.Trim(), StringComparison.Ordinal)) { same++; continue; }
                        else changed++;

                        cells[pair.Column] = value!.Trim();
                    }
                }

                built.Row(cells);
            }

            return new LinkReturn(built, filled, changed, same, absent,
                                  at.Select(p => p.Mapping.Heading).ToList(), null);
        }

        /// <summary>Every saved link, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<DataLinkRecipe> Saved(JsonValue? json)
        {
            var saved = new List<DataLinkRecipe>();

            if (json == null) return saved;

            foreach (var item in json.Items)
            {
                var link = DataLinkRecipe.FromJson(item);
                if (link.Columns.Count > 0) saved.Add(link);
            }

            return saved;
        }

        /// <summary>The list with one link put in it, replacing any of the same name.</summary>
        /// <param name="saved">What is saved now.</param>
        /// <param name="recipe">The one to keep.</param>
        public static JsonValue Keep(IEnumerable<DataLinkRecipe>? saved, DataLinkRecipe? recipe)
        {
            var kept = (saved ?? Enumerable.Empty<DataLinkRecipe>()).ToList();

            if (recipe != null && recipe.Columns.Count > 0)
            {
                kept.RemoveAll(l => string.Equals(l.Name, recipe.Name, StringComparison.OrdinalIgnoreCase));
                kept.Add(recipe);
            }

            return JsonValue.Array(kept.Select(l => l.ToJson()));
        }

        // ---------------------------------------------------------------------------------------
        // Working it out
        // ---------------------------------------------------------------------------------------

        private sealed class KeyGuess
        {
            internal KeyGuess(string heading, LinkMatch match, PropertyRef property, double score)
            {
                Heading = heading;
                Match = match;
                Property = property;
                Score = score;
            }

            internal string Heading { get; }

            internal LinkMatch Match { get; }

            internal PropertyRef Property { get; }

            internal double Score { get; }
        }

        private static KeyGuess? FindKey(SheetTable sheet, IReadOnlyList<string> headings,
                                         IReadOnlyList<ReportElement> scope)
        {
            if (scope.Count == 0) return null;

            var names = new HashSet<string>(scope.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

            // Only the id-shaped properties are worth trying: a key column matches something that is
            // nearly unique per element, and trying every property in a federation against every
            // column is a great deal of work to find that "Phase" is not a key.
            var candidates = DataReport.Fields(scope)
                .Where(f => f.Filled > 0 && f.Distinct * 2 >= f.Filled)
                .OrderByDescending(f => f.Filled)
                .Take(40)
                .Select(f => (Field: f.Field,
                              Values: new HashSet<string>(
                                  scope.Select(e => e.Value(f.Field))
                                       .Where(v => !string.IsNullOrWhiteSpace(v))
                                       .Select(v => v!.Trim()),
                                  StringComparer.OrdinalIgnoreCase)))
                .ToList();

            KeyGuess? best = null;

            foreach (var heading in headings)
            {
                var column = sheet.ColumnOf(heading);

                if (column < 0) continue;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var filled = 0;

                for (var r = 1; r < sheet.Rows.Count; r++)
                {
                    var cell = sheet.Cell(r, column);

                    if (string.IsNullOrWhiteSpace(cell)) continue;

                    filled++;
                    seen.Add(cell!.Trim());
                }

                if (seen.Count == 0) continue;

                // How much of the column turns up in the model, times how close the column is to
                // one value per row. Without that second factor a register whose "Duty" column
                // reads Duty/Standby/Standby scores as highly as its asset number - every value
                // is in the model - and the join lands on the wrong column, quietly, on a file
                // that looks fine. A key is what identifies a row, not merely what is recognised.
                var unique = (double)seen.Count / filled;

                foreach (var candidate in candidates)
                {
                    var found = (double)seen.Count(v => candidate.Values.Contains(v)) / seen.Count;

                    if (found < KeyConfidence) continue;

                    var score = found * unique;

                    if (best == null || score > best.Score)
                        best = new KeyGuess(heading, LinkMatch.Property, candidate.Field, score);
                }

                var byName = (double)seen.Count(names.Contains) / seen.Count;

                if (byName >= KeyConfidence && (best == null || byName * unique > best.Score))
                    best = new KeyGuess(heading, LinkMatch.Name, default, byName * unique);
            }

            return best;
        }

        private static string? Refuses(SheetTable? sheet, IReadOnlyList<ReportElement> scope,
                                       DataLinkRecipe link)
        {
            if (sheet == null || sheet.IsEmpty)
                return "There is nothing in that sheet.";

            if (scope.Count == 0)
                return "There are no elements in scope to link to. Widen the scope, or select "
                     + "something in the model first.";

            if (link.Columns.Count == 0)
                return "Nothing is mapped yet. Press Work it out and CamelWorks will read the sheet "
                     + "against the model and propose the join.";

            if (sheet.ColumnOf(link.KeyHeading) < 0)
                return "This sheet has no \"" + link.KeyHeading + "\" column, so there is no way to "
                     + "tell which element each row is about. Pick the column that identifies the "
                     + "element, or press Work it out.";

            if (link.Match == LinkMatch.Property && link.KeyProperty.Name.Length == 0)
                return "The rows are matched on a property, but no property has been named to match "
                     + "them against. Pick one, or press Work it out.";

            return null;
        }

        private static IReadOnlyList<LinkRow> Read(SheetTable sheet, int column, IList<string> notes)
        {
            var rows = new List<LinkRow>();
            var blank = 0;

            for (var r = 1; r < sheet.Rows.Count && rows.Count < MaxRows; r++)
            {
                var cells = sheet.Rows[r];
                var key = column >= 0 && column < cells.Count ? cells[column] : null;

                if (string.IsNullOrWhiteSpace(key)) { blank++; continue; }

                rows.Add(new LinkRow(r + 1, key!.Trim(), cells));
            }

            if (blank > 0)
                notes.Add(blank.ToString("N0", CultureInfo.InvariantCulture)
                          + (blank == 1
                              ? " row has nothing in the key column and was passed over."
                              : " rows have nothing in the key column and were passed over."));

            return rows;
        }

        private static Dictionary<string, List<ReportElement>> Index(IReadOnlyList<ReportElement> scope,
                                                                    DataLinkRecipe link)
        {
            var index = new Dictionary<string, List<ReportElement>>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in scope)
            {
                var key = link.Match switch
                {
                    LinkMatch.Identity => element.Key.ToString(),
                    LinkMatch.Name => element.Name,
                    _ => element.Value(link.KeyProperty),
                };

                if (string.IsNullOrWhiteSpace(key)) continue;

                var trimmed = key!.Trim();

                if (!index.TryGetValue(trimmed, out var here))
                {
                    here = new List<ReportElement>();
                    index.Add(trimmed, here);
                }

                here.Add(element);
            }

            return index;
        }

        private static LinkPlan Nothing(IReadOnlyList<string> notes, string refusal) =>
            new LinkPlan(Array.Empty<LinkWrite>(), Array.Empty<LinkRow>(), Array.Empty<ElementKey>(),
                         0, 0, 0, 0, notes, refusal);

        private static LinkReturn Empty(string refusal) =>
            new LinkReturn(new XlsxSheet("Nothing"), 0, 0, 0, 0, Array.Empty<string>(), refusal);

        private static string?[] Line(IReadOnlyList<string?> cells, int width)
        {
            var line = new string?[width];

            for (var i = 0; i < width; i++) line[i] = i < cells.Count ? cells[i] : null;

            return line;
        }

        private static string Sentence(IReadOnlyList<string> names)
        {
            if (names.Count == 1) return "\"" + names[0] + "\"";

            var quoted = names.Select(n => "\"" + n + "\"").ToList();

            return string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[quoted.Count - 1];
        }
    }
}
