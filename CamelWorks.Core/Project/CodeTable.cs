using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>One row of a code table.</summary>
    public sealed class CodeEntry
    {
        /// <summary>Create an entry.</summary>
        /// <param name="code">The code itself, which is what gets written onto elements.</param>
        /// <param name="description">What it means, for a person reading the table.</param>
        /// <param name="parent">The code above it, when the table is a hierarchy.</param>
        public CodeEntry(string code, string? description = null, string? parent = null)
        {
            Code = string.IsNullOrWhiteSpace(code)
                ? throw new ArgumentException("a code is required", nameof(code))
                : code.Trim();

            Description = Trimmed(description);
            Parent = Trimmed(parent);
        }

        /// <summary>The code itself.</summary>
        public string Code { get; }

        /// <summary>What it means.</summary>
        public string? Description { get; }

        /// <summary>The code above it, when the table is a hierarchy.</summary>
        public string? Parent { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Description == null ? Code : Code + " - " + Description;

        private static string? Trimmed(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }

    /// <summary>How much of a scope is coded, and what is wrong with the rest.</summary>
    public sealed class CodeCoverage
    {
        internal CodeCoverage(int coded, int uncoded, int invalid, IReadOnlyList<string> examples)
        {
            Coded = coded;
            Uncoded = uncoded;
            Invalid = invalid;
            InvalidValues = examples;
        }

        /// <summary>How many carry a value the table knows.</summary>
        public int Coded { get; }

        /// <summary>How many carry nothing at all.</summary>
        public int Uncoded { get; }

        /// <summary>How many carry a value the table does not know.</summary>
        public int Invalid { get; }

        /// <summary>The values that are not in the table, most common first.</summary>
        public IReadOnlyList<string> InvalidValues { get; }

        /// <summary>True when everything that carries a value carries one the table knows.</summary>
        public bool IsClean => Invalid == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var total = Coded + Uncoded + Invalid;

            var s = Measure.Count(Coded) + " of " + Measure.Count(total) + " coded - "
                  + Measure.Count(Uncoded) + " uncoded - " + Measure.Count(Invalid) + " invalid";

            if (Invalid > 0 && InvalidValues.Count > 0)
                s += " (" + string.Join(", ", InvalidValues.Take(5)) + ")";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// A named list of the values a property is allowed to hold.
    ///
    /// The point is the refusal. A lookup that silently passes an unlisted value is a lookup that
    /// does nothing — the whole reason a project keeps a code list is that "L3-MECH-01" and
    /// "L03 MECH 1" are the same thing to a person and two different things to every downstream
    /// tool, and the only moment either can be caught is the moment somebody writes one.
    ///
    /// So "assign code from table" writes only values the table lists, refuses the rest, and says
    /// what it refused. A coverage figure with an invalid count beside it is worth more than a
    /// green tick over a column full of near-misses.
    /// </summary>
    public sealed class CodeTable
    {
        private readonly Dictionary<string, CodeEntry> _byCode;

        /// <summary>Create a table.</summary>
        /// <param name="name">What the table is called.</param>
        /// <param name="entries">Its rows.</param>
        public CodeTable(string name, IEnumerable<CodeEntry>? entries = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Codes" : name.Trim();

            _byCode = new Dictionary<string, CodeEntry>(StringComparer.OrdinalIgnoreCase);

            var ordered = new List<CodeEntry>();

            foreach (var entry in entries ?? Array.Empty<CodeEntry>())
            {
                if (entry == null || _byCode.ContainsKey(entry.Code)) continue;

                _byCode[entry.Code] = entry;
                ordered.Add(entry);
            }

            Entries = ordered;
        }

        /// <summary>What the table is called.</summary>
        public string Name { get; }

        /// <summary>Its rows, in the order they were given.</summary>
        public IReadOnlyList<CodeEntry> Entries { get; }

        /// <summary>True when nothing is in it.</summary>
        public bool IsEmpty => Entries.Count == 0;

        /// <summary>
        /// True when the table lists this value.
        ///
        /// Case-insensitive and trimmed, because a code typed by a person has a trailing space and
        /// a lowercase letter in it about a third of the time, and refusing those would make the
        /// table an obstacle rather than a standard.
        /// </summary>
        /// <param name="value">The value to check.</param>
        public bool Knows(string? value) =>
            !string.IsNullOrWhiteSpace(value) && _byCode.ContainsKey(value!.Trim());

        /// <summary>The entry for a value, or null.</summary>
        /// <param name="value">The value to look up.</param>
        public CodeEntry? Find(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null
                : _byCode.TryGetValue(value!.Trim(), out var entry) ? entry : null;

        /// <summary>
        /// The canonical spelling of a value the table knows, or null when it does not.
        ///
        /// The other half of being forgiving on input: what gets written is the table's spelling,
        /// not the one somebody typed, so a column ends up with one form of each code rather than
        /// four.
        /// </summary>
        /// <param name="value">The value as it was typed.</param>
        public string? Canonical(string? value) => Find(value)?.Code;

        /// <summary>How much of a scope this table covers.</summary>
        /// <param name="values">One value per element, null where the element carries nothing.</param>
        public CodeCoverage Check(IEnumerable<string?>? values)
        {
            var coded = 0;
            var uncoded = 0;
            var invalid = 0;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in values ?? Array.Empty<string?>())
            {
                if (string.IsNullOrWhiteSpace(value)) { uncoded++; continue; }

                if (Knows(value)) { coded++; continue; }

                invalid++;

                var text = value!.Trim();
                seen[text] = seen.TryGetValue(text, out var n) ? n + 1 : 1;
            }

            var examples = seen.OrderByDescending(e => e.Value)
                               .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                               .Take(20)
                               .Select(e => e.Key)
                               .ToList();

            return new CodeCoverage(coded, uncoded, invalid, examples);
        }

        /// <summary>
        /// Read a table from what somebody pasted in.
        ///
        /// One row per line, fields separated by a comma or a tab: code, description, parent. A
        /// project's code list arrives as a column out of a spreadsheet or a page out of a
        /// standard, and asking somebody to retype it into a grid is how a feature goes unused.
        /// </summary>
        /// <param name="name">What the table is called.</param>
        /// <param name="text">The pasted rows.</param>
        public static CodeTable Parse(string name, string? text)
        {
            var entries = new List<CodeEntry>();

            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var row = line.Trim('\r', ' ', '\t');

                if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal)) continue;

                var fields = row.Split(new[] { '\t', ',', ';' }, 3);

                var code = fields[0].Trim();
                if (code.Length == 0) continue;

                entries.Add(new CodeEntry(code,
                    fields.Length > 1 ? fields[1] : null,
                    fields.Length > 2 ? fields[2] : null));
            }

            return new CodeTable(name, entries);
        }

        /// <summary>The table as text, in the form <see cref="Parse"/> reads.</summary>
        public string ToText() =>
            string.Join(Environment.NewLine, Entries.Select(Line));

        private static string Line(CodeEntry entry)
        {
            if (entry.Parent != null)
                return entry.Code + ", " + (entry.Description ?? string.Empty) + ", " + entry.Parent;

            return entry.Description == null ? entry.Code : entry.Code + ", " + entry.Description;
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Array(Entries.Select(e =>
            {
                var json = JsonValue.Object().Set("code", JsonValue.String(e.Code));

                if (e.Description != null) json.Set("is", JsonValue.String(e.Description));
                if (e.Parent != null) json.Set("under", JsonValue.String(e.Parent));

                return json;
            }));

        /// <summary>Read one back.</summary>
        /// <param name="name">What the table is called.</param>
        /// <param name="json">What was written.</param>
        public static CodeTable FromJson(string name, JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Array) return new CodeTable(name);

            var entries = new List<CodeEntry>();

            foreach (var item in json.Items)
            {
                var code = item["code"].AsString();
                if (string.IsNullOrWhiteSpace(code)) continue;

                entries.Add(new CodeEntry(code!, item["is"].AsString(), item["under"].AsString()));
            }

            return new CodeTable(name, entries);
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " (" + Entries.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Every code table a project keeps.</summary>
    public sealed class CodeTables
    {
        private readonly Dictionary<string, CodeTable> _tables =
            new Dictionary<string, CodeTable>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Their names, in alphabetical order.</summary>
        public IReadOnlyList<string> Names =>
            _tables.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>True when the project keeps none.</summary>
        public bool IsEmpty => _tables.Count == 0;

        /// <summary>One by name, or null.</summary>
        /// <param name="name">The table's name.</param>
        public CodeTable? Find(string? name) =>
            name != null && _tables.TryGetValue(name.Trim(), out var table) ? table : null;

        /// <summary>Add or replace one. An empty table is removed rather than kept.</summary>
        /// <param name="table">The table.</param>
        public void Put(CodeTable? table)
        {
            if (table == null) return;

            if (table.IsEmpty) _tables.Remove(table.Name);
            else _tables[table.Name] = table;
        }

        /// <summary>Remove one. Removing something absent is not an error.</summary>
        /// <param name="name">The table's name.</param>
        public void Remove(string? name)
        {
            if (name != null) _tables.Remove(name.Trim());
        }

        /// <summary>Write them all down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            foreach (var name in Names) json.Set(name, _tables[name].ToJson());

            return json;
        }

        /// <summary>Read them back.</summary>
        /// <param name="json">What was written.</param>
        public static CodeTables FromJson(JsonValue? json)
        {
            var tables = new CodeTables();

            if (json == null || json.Kind != JsonKind.Object) return tables;

            foreach (var name in json.Keys) tables.Put(CodeTable.FromJson(name, json[name]));

            return tables;
        }

        /// <inheritdoc />
        public override string ToString() =>
            IsEmpty ? "no code tables" : string.Join(", ", Names);
    }
}
