using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Data
{
    /// <summary>One data row of a returned sheet, with the identity it claims.</summary>
    public sealed class SheetRow
    {
        internal SheetRow(int number, string key, IReadOnlyList<string?> cells)
        {
            Number = number;
            Key = key;
            Cells = cells;
        }

        /// <summary>Which row of the sheet it is, numbered as Excel numbers them.</summary>
        public int Number { get; }

        /// <summary>The identity in the key column.</summary>
        public string Key { get; }

        /// <summary>Every cell in the row.</summary>
        public IReadOnlyList<string?> Cells { get; }

        /// <summary>One cell, or null when the row is shorter than that.</summary>
        /// <param name="column">Zero-based column.</param>
        public string? Cell(int column) =>
            column < 0 || column >= Cells.Count ? null : Cells[column];

        /// <inheritdoc />
        public override string ToString() =>
            "row " + Number.ToString(CultureInfo.InvariantCulture) + ": " + Key;
    }

    /// <summary>What came back in a returned sheet, and what could not be placed.</summary>
    public sealed class SheetImportReport
    {
        internal SheetImportReport(IReadOnlyList<string?> headers, int keyColumn,
                                   IReadOnlyList<SheetRow> matched, IReadOnlyList<SheetRow> unmatched,
                                   IReadOnlyList<SheetRow> duplicates, int blank, string? problem)
        {
            Headers = headers;
            KeyColumn = keyColumn;
            Matched = matched;
            Unmatched = unmatched;
            Duplicates = duplicates;
            Blank = blank;
            Problem = problem;
        }

        /// <summary>The sheet's headings.</summary>
        public IReadOnlyList<string?> Headers { get; }

        /// <summary>Which column carried the identity, or -1 when none did.</summary>
        public int KeyColumn { get; }

        /// <summary>Rows whose identity is in scope.</summary>
        public IReadOnlyList<SheetRow> Matched { get; }

        /// <summary>
        /// Rows whose identity is not in scope.
        ///
        /// Never a silent drop. Somebody added rows at the bottom, or sent back a sheet from
        /// another package, or the model has been re-exported and those elements are gone — all
        /// three are things a person has to see and decide about, and all three look identical to
        /// a tool that just skips them.
        /// </summary>
        public IReadOnlyList<SheetRow> Unmatched { get; }

        /// <summary>Rows repeating an identity an earlier row already claimed. The first row wins.</summary>
        public IReadOnlyList<SheetRow> Duplicates { get; }

        /// <summary>How many rows carried no identity at all.</summary>
        public int Blank { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem { get; }

        /// <summary>True when there is something to apply.</summary>
        public bool IsUsable => Problem == null && Matched.Count > 0;

        /// <summary>Which column a heading is in, or -1.</summary>
        /// <param name="heading">The heading to look for.</param>
        public int ColumnOf(string? heading)
        {
            if (string.IsNullOrWhiteSpace(heading)) return -1;

            var wanted = heading!.Trim();

            for (var c = 0; c < Headers.Count; c++)
                if (string.Equals((Headers[c] ?? string.Empty).Trim(), wanted,
                                  StringComparison.OrdinalIgnoreCase))
                    return c;

            return -1;
        }

        /// <summary>
        /// The headings that are not the identity column, for a picker.
        ///
        /// The identity column is left out on purpose: it is the one column a returned sheet may
        /// not write back, because writing it would let a typo in a key rename the element it
        /// points at.
        /// </summary>
        public IReadOnlyList<string> Writable =>
            Headers.Select((h, i) => (Heading: h, Index: i))
                   .Where(c => c.Index != KeyColumn && !string.IsNullOrWhiteSpace(c.Heading))
                   .Select(c => c.Heading!.Trim())
                   .ToList();

        /// <summary>One column's values, keyed by identity. Matched rows only.</summary>
        /// <param name="column">Zero-based column.</param>
        public IReadOnlyDictionary<string, string?> Column(int column)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (column < 0) return values;

            foreach (var row in Matched) values[row.Key] = row.Cell(column);

            return values;
        }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Problem != null) return Problem;

            var s = Measure.Count(Matched.Count)
                  + (Matched.Count == 1 ? " row matches an element" : " rows match elements");

            if (Unmatched.Count > 0) s += ", " + Measure.Count(Unmatched.Count) + " match nothing in scope";
            if (Duplicates.Count > 0) s += ", " + Measure.Count(Duplicates.Count) + " repeat an earlier row";
            if (Blank > 0) s += ", " + Measure.Count(Blank) + " carry no identity";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Taking a sheet back.
    ///
    /// Sending properties out is a courtesy; taking them back is the feature. The reason a project
    /// exports a schedule is that somebody will fill in a column, and a tool that cannot read it back
    /// has moved the retyping rather than removed it.
    ///
    /// Matched on a written identity rather than on a name. Matching on a mark or a description
    /// breaks the first time two doors are called "D-12" or somebody fixes a typo. The exported
    /// sheet carries an identity column and the import matches on that; that column may never be
    /// written back, because a typo in a key would rename the element it points at.
    ///
    /// Nothing is dropped quietly. Rows that match nothing, rows that repeat an identity and rows
    /// with no identity are all counted and shown. Rows added at the bottom, a sheet from another
    /// package, and a re-exported model all need a decision and all look the same to a tool that
    /// skips them.
    /// </summary>
    public static class SheetImport
    {
        /// <summary>
        /// What the identity column is called on an exported sheet.
        ///
        /// Named for what it is rather than something friendlier, so nobody deletes it thinking it
        /// is a working column.
        /// </summary>
        public const string KeyHeading = "CamelWorks id (do not edit)";

        /// <summary>Read a returned sheet.</summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="known">The identities in scope, as <c>ElementKey.ToString()</c> writes them.</param>
        /// <param name="keyHeading">The identity column's heading.</param>
        /// <param name="comparer">
        /// How two identities are compared. Ordinal by default, because an element key is a hash
        /// and case is part of it; a caller whose identities are case-insensitive says so.
        /// </param>
        /// <param name="advice">
        /// What to do about a sheet with no identity column. Said by the caller, because "export it
        /// from here first" points at a different screen depending on what was exported.
        /// </param>
        public static SheetImportReport Of(SheetTable? sheet, IReadOnlyCollection<string>? known,
                                           string keyHeading = KeyHeading,
                                           StringComparer? comparer = null,
                                           string? advice = null)
        {
            var identity = comparer ?? StringComparer.Ordinal;

            var none = Array.Empty<SheetRow>();

            if (sheet == null || sheet.IsEmpty)
                return new SheetImportReport(Array.Empty<string?>(), -1, none, none, none, 0,
                    "there is nothing in that sheet");

            var keyColumn = sheet.ColumnOf(keyHeading);

            if (keyColumn < 0)
                return new SheetImportReport(sheet.Headers, -1, none, none, none, 0,
                    "that sheet has no \"" + keyHeading + "\" column, so there is no way to tell which "
                    + "element each row is about. "
                    + (advice ?? "Export the properties from here first, fill in the sheet that comes "
                                 + "out, and send that back."));

            var inScope = new HashSet<string>(known ?? Array.Empty<string>(), identity);

            var matched = new List<SheetRow>();
            var unmatched = new List<SheetRow>();
            var duplicates = new List<SheetRow>();
            var seen = new HashSet<string>(identity);
            var blank = 0;

            for (var r = 1; r < sheet.Rows.Count; r++)
            {
                var cells = sheet.Rows[r];
                var key = keyColumn < cells.Count ? cells[keyColumn] : null;

                if (string.IsNullOrWhiteSpace(key))
                {
                    // A row of nothing at all is a spacer, not a mistake. A row with content but no
                    // identity is something somebody typed and expects to see happen.
                    if (cells.Any(c => !string.IsNullOrWhiteSpace(c))) blank++;
                    continue;
                }

                var row = new SheetRow(r + 1, key!.Trim(), cells);

                if (!seen.Add(row.Key)) duplicates.Add(row);
                else if (inScope.Contains(row.Key)) matched.Add(row);
                else unmatched.Add(row);
            }

            return new SheetImportReport(sheet.Headers, keyColumn, matched, unmatched, duplicates,
                                         blank, null);
        }
    }
}
