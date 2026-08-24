using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Data
{
    /// <summary>One element's contribution to a pivot: which cell it lands in, and what it measures.</summary>
    public sealed class PivotLine
    {
        /// <summary>Create a line.</summary>
        /// <param name="id">How the element reads when a value cannot be parsed.</param>
        /// <param name="row">Which row it belongs to.</param>
        /// <param name="column">Which column it belongs to.</param>
        /// <param name="value">The measured value, as the host gave it.</param>
        public PivotLine(string id, string? row, string? column, string? value)
        {
            Id = id;
            Row = Label(row);
            Column = Label(column);
            Value = value;
        }

        /// <summary>How the element reads.</summary>
        public string Id { get; }

        /// <summary>Which row it belongs to.</summary>
        public string Row { get; }

        /// <summary>Which column it belongs to.</summary>
        public string Column { get; }

        /// <summary>The measured value, as the host gave it.</summary>
        public string? Value { get; }

        /// <summary>
        /// What an axis reads as when the element carries nothing for it.
        ///
        /// Named rather than blank. A row headed by an empty string looks like a rendering fault;
        /// one headed "(none)" is a fact about the model, and usually the most interesting row on
        /// the table.
        /// </summary>
        public const string None = "(none)";

        private static string Label(string? text) =>
            string.IsNullOrWhiteSpace(text) ? None : text!.Trim();
    }

    /// <summary>A takeoff across two axes at once.</summary>
    public sealed class TakeoffPivotTable
    {
        private readonly Dictionary<string, TakeoffGroup> _cells;
        private readonly Dictionary<string, TakeoffGroup> _rowTotals;
        private readonly Dictionary<string, TakeoffGroup> _columnTotals;

        internal TakeoffPivotTable(IReadOnlyList<string> rows, IReadOnlyList<string> columns,
                                   Dictionary<string, TakeoffGroup> cells,
                                   Dictionary<string, TakeoffGroup> rowTotals,
                                   Dictionary<string, TakeoffGroup> columnTotals,
                                   TakeoffGroup grand, int droppedRows, int droppedColumns)
        {
            Rows = rows; Columns = columns;
            _cells = cells; _rowTotals = rowTotals; _columnTotals = columnTotals;
            Grand = grand; DroppedRows = droppedRows; DroppedColumns = droppedColumns;
        }

        /// <summary>Row headings, biggest first.</summary>
        public IReadOnlyList<string> Rows { get; }

        /// <summary>Column headings, biggest first.</summary>
        public IReadOnlyList<string> Columns { get; }

        /// <summary>Everything, added up.</summary>
        public TakeoffGroup Grand { get; }

        /// <summary>How many rows fell outside the cap.</summary>
        public int DroppedRows { get; }

        /// <summary>How many columns fell outside the cap.</summary>
        public int DroppedColumns { get; }

        /// <summary>True when nothing in scope carried the measure at all.</summary>
        public bool IsEmpty => Rows.Count == 0 || Columns.Count == 0;

        /// <summary>One cell, or null when nothing lands in it.</summary>
        /// <param name="row">The row heading.</param>
        /// <param name="column">The column heading.</param>
        public TakeoffGroup? Cell(string row, string column) =>
            _cells.TryGetValue(At(row, column), out var cell) ? cell : null;

        /// <summary>One row, added up across every column including the ones beyond the cap.</summary>
        /// <param name="row">The row heading.</param>
        public TakeoffGroup? RowTotal(string row) =>
            _rowTotals.TryGetValue(row, out var total) ? total : null;

        /// <summary>One column, added up down every row including the ones beyond the cap.</summary>
        /// <param name="column">The column heading.</param>
        public TakeoffGroup? ColumnTotal(string column) =>
            _columnTotals.TryGetValue(column, out var total) ? total : null;

        /// <summary>The one-line readout.</summary>
        public string Explain() => Explain(null);

        /// <summary>
        /// The same readout with the grand total in the project's units.
        /// </summary>
        /// <param name="units">The formatter, or null for base SI.</param>
        public string Explain(Measure? units)
        {
            if (IsEmpty) return "nothing in scope carries this measure";

            var s = Grand.Count.ToString("N0", CultureInfo.InvariantCulture) + " elements across "
                  + Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows and "
                  + Columns.Count.ToString("N0", CultureInfo.InvariantCulture) + " columns";

            if (Grand.Total != null) s += " - " + Grand.Total.Value.Show(units);
            else if (Grand.MixedKinds) s += " - mixed units, so no total";

            if (Grand.Unreadable > 0)
                s += " - " + Grand.Unreadable.ToString("N0", CultureInfo.InvariantCulture) + " unreadable";

            if (DroppedRows > 0 || DroppedColumns > 0)
                s += " - " + DroppedRows.ToString("N0", CultureInfo.InvariantCulture) + " rows and "
                   + DroppedColumns.ToString("N0", CultureInfo.InvariantCulture)
                   + " columns beyond the cap, still counted in the totals";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        internal static string At(string row, string column) => row.Length + ":" + row + column;
    }

    /// <summary>
    /// A takeoff across two axes: concrete by level, ductwork by system, doors by fire rating.
    ///
    /// One axis answers "how much of this is there". Two answers "where is it", which is the
    /// question a programme, a procurement schedule or a phasing plan is actually built from — and
    /// producing it today means running the takeoff once per level and typing the results into a
    /// spreadsheet.
    ///
    /// Every cell is added up by the same summer the single-axis takeoff uses, so a mixed-unit cell
    /// refuses to total and an unreadable value is counted and named, exactly as it is there.
    /// Nothing about being in a grid makes a wrong number more acceptable.
    ///
    /// Row and column totals are computed from all the lines rather than from the visible cells, so
    /// a table trimmed to its largest hundred rows still adds up to the truth.
    /// </summary>
    public static class TakeoffPivot
    {
        /// <summary>How many rows a table shows before the rest are counted only.</summary>
        public const int DefaultRowCap = 100;

        /// <summary>How many columns a table shows before the rest are counted only.</summary>
        public const int DefaultColumnCap = 40;

        /// <summary>Build the table.</summary>
        /// <param name="lines">One per element.</param>
        /// <param name="rowCap">How many rows to show.</param>
        /// <param name="columnCap">How many columns to show.</param>
        public static TakeoffPivotTable Of(IEnumerable<PivotLine> lines,
                                           int rowCap = DefaultRowCap, int columnCap = DefaultColumnCap)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            var all = lines.Where(l => l != null).ToList();

            var rows = Ranked(all.Select(l => l.Row), Math.Max(1, rowCap), out var droppedRows);
            var columns = Ranked(all.Select(l => l.Column), Math.Max(1, columnCap), out var droppedColumns);

            var visibleRows = new HashSet<string>(rows, StringComparer.Ordinal);
            var visibleColumns = new HashSet<string>(columns, StringComparer.Ordinal);

            var cells = new Dictionary<string, TakeoffGroup>(StringComparer.Ordinal);

            foreach (var cell in all
                         .Where(l => visibleRows.Contains(l.Row) && visibleColumns.Contains(l.Column))
                         .GroupBy(l => TakeoffPivotTable.At(l.Row, l.Column), StringComparer.Ordinal))
            {
                cells[cell.Key] = Add(cell);
            }

            // From every line, not from the visible cells: a table trimmed to its largest hundred
            // rows must still add up to the truth, or the totals are a different number from the
            // one the model contains and nobody can tell which.
            var rowTotals = all.GroupBy(l => l.Row, StringComparer.Ordinal)
                               .ToDictionary(g => g.Key, Add, StringComparer.Ordinal);

            var columnTotals = all.GroupBy(l => l.Column, StringComparer.Ordinal)
                                  .ToDictionary(g => g.Key, Add, StringComparer.Ordinal);

            return new TakeoffPivotTable(rows, columns, cells, rowTotals, columnTotals,
                                         Add(all), droppedRows, droppedColumns);
        }

        /// <summary>Add up one bucket, using the single-axis summer so the rules cannot drift apart.</summary>
        private static TakeoffGroup Add(IEnumerable<PivotLine> bucket)
        {
            const string one = "total";

            var summed = Takeoff.Sum(bucket.Select(l => new TakeoffLine(l.Id, one, l.Value)));

            // An empty bucket has no group to take, and inventing one from a placeholder line would
            // report a count of one where there is nothing at all.
            return summed.Groups.Count > 0
                ? summed.Groups[0]
                : new TakeoffGroup(one, 0, null, 0, false, Array.Empty<string>());
        }

        /// <summary>The distinct labels, most-populated first, capped.</summary>
        private static IReadOnlyList<string> Ranked(IEnumerable<string> labels, int cap, out int dropped)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var label in labels)
                counts[label] = counts.TryGetValue(label, out var seen) ? seen + 1 : 1;

            var ordered = counts
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => c.Key)
                .ToList();

            dropped = Math.Max(0, ordered.Count - cap);

            return ordered.Take(cap).ToList();
        }
    }
}
