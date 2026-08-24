using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;

namespace CamelWorks.Core.Sets
{
    /// <summary>Which shape the spreadsheet is in.</summary>
    public enum SetSheetLayout
    {
        /// <summary>
        /// One condition per row: folder, set name, category, property, operator, value.
        ///
        /// The general form. Rows sharing a set name are ANDed, so a set with three conditions is
        /// three consecutive rows.
        /// </summary>
        Conditions,

        /// <summary>
        /// One set per row, with a column per property.
        ///
        /// The form a coordination matrix workbook already uses: a set name, then a column for each
        /// property the sets are split on, and a value in the cells that apply. Every filled cell
        /// becomes one Equals condition, and they are ANDed.
        /// </summary>
        Columns,
    }

    /// <summary>Where the set definitions sit inside a worksheet.</summary>
    public sealed class SetSheetMapping
    {
        /// <summary>Which shape the sheet is in.</summary>
        public SetSheetLayout Layout { get; set; } = SetSheetLayout.Conditions;

        /// <summary>The sheet's name. Empty means the first one.</summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>Row holding the headings, and therefore the row the data starts after.</summary>
        public int HeaderRow { get; set; } = 1;

        /// <summary>Column holding the folder, or 0 for none.</summary>
        public int FolderColumn { get; set; }

        /// <summary>Column holding the set name.</summary>
        public int NameColumn { get; set; } = 1;

        /// <summary>Conditions layout: column holding the property category.</summary>
        public int CategoryColumn { get; set; } = 2;

        /// <summary>Conditions layout: column holding the property name.</summary>
        public int PropertyColumn { get; set; } = 3;

        /// <summary>Conditions layout: column holding the operator, or 0 to treat every row as Equals.</summary>
        public int OperatorColumn { get; set; } = 4;

        /// <summary>Conditions layout: column holding the value.</summary>
        public int ValueColumn { get; set; } = 5;

        /// <summary>Columns layout: first column holding a property value.</summary>
        public int FirstValueColumn { get; set; } = 2;

        /// <summary>
        /// Columns layout: the property category every value column belongs to.
        ///
        /// One category for the sheet rather than one per column, because that is how these
        /// workbooks are written: the columns are properties on the same tab.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>How many empty rows in a row end the table.</summary>
        public int BlankRowStop { get; set; } = 3;
    }

    /// <summary>The rows a sheet gave up, and everything that did not read.</summary>
    public sealed class SetSheetResult
    {
        internal SetSheetResult(IReadOnlyList<SetRow> rows, IReadOnlyList<string> warnings)
        {
            Rows = rows; Warnings = warnings;
        }

        /// <summary>The rows, in sheet order.</summary>
        public IReadOnlyList<SetRow> Rows { get; }

        /// <summary>Everything that could not be read cleanly.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>True when at least one row came out.</summary>
        public bool IsUsable => Rows.Count > 0;

        /// <inheritdoc />
        public override string ToString() =>
            Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows, "
            + Warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings";
    }

    /// <summary>
    /// Reads set definitions out of the spreadsheet a project already keeps them in.
    ///
    /// Both layouts exist because both arrive. The condition table is what somebody writes when
    /// they are describing sets; the column form is what falls out of a coordination workbook,
    /// where the sets are the rows and the properties are the columns. Supporting one of the two
    /// means half the people who have a sheet have to retype it.
    /// </summary>
    public static class SheetSets
    {
        /// <summary>Read the definitions.</summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="mapping">Where things are in it.</param>
        public static SetSheetResult Read(SheetTable? sheet, SetSheetMapping? mapping)
        {
            var map = mapping ?? new SetSheetMapping();
            var warnings = new List<string>();
            var rows = new List<SetRow>();

            if (sheet == null)
            {
                warnings.Add("There is no sheet by that name in the workbook.");
                return new SetSheetResult(rows, warnings);
            }

            var blanks = 0;
            var row = Math.Max(1, map.HeaderRow) + 1;

            while (blanks < Math.Max(1, map.BlankRowStop) && row <= sheet.Rows.Count + map.BlankRowStop)
            {
                var name = Text(sheet, row, map.NameColumn);

                if (name.Length == 0) { blanks++; row++; continue; }

                blanks = 0;

                if (map.Layout == SetSheetLayout.Conditions) ReadCondition(sheet, map, row, name, rows, warnings);
                else ReadColumns(sheet, map, row, name, rows, warnings);

                row++;
            }

            if (rows.Count == 0)
                warnings.Add("No set definitions were found below row "
                             + map.HeaderRow.ToString(CultureInfo.InvariantCulture) + ".");

            return new SetSheetResult(rows, warnings);
        }

        private static void ReadCondition(SheetTable sheet, SetSheetMapping map, int row, string name,
                                          List<SetRow> rows, List<string> warnings)
        {
            var written = map.OperatorColumn > 0 ? Text(sheet, row, map.OperatorColumn) : string.Empty;
            var op = SetOperator.Equals;

            if (written.Length > 0 && !TryOperator(written, out op))
            {
                warnings.Add("Row " + row.ToString(CultureInfo.InvariantCulture) + " says \"" + written
                             + "\", which is not an operator. It was read as Equals.");
                op = SetOperator.Equals;
            }

            rows.Add(new SetRow(name,
                                Text(sheet, row, map.CategoryColumn),
                                Text(sheet, row, map.PropertyColumn),
                                op,
                                Text(sheet, row, map.ValueColumn),
                                map.FolderColumn > 0 ? Text(sheet, row, map.FolderColumn) : null));
        }

        private static void ReadColumns(SheetTable sheet, SetSheetMapping map, int row, string name,
                                        List<SetRow> rows, List<string> warnings)
        {
            var folder = map.FolderColumn > 0 ? Text(sheet, row, map.FolderColumn) : null;
            var made = 0;

            for (var column = Math.Max(1, map.FirstValueColumn); column <= sheet.Columns; column++)
            {
                var value = Text(sheet, row, column);
                if (value.Length == 0) continue;

                var property = Text(sheet, Math.Max(1, map.HeaderRow), column);

                if (property.Length == 0)
                {
                    warnings.Add("Row " + row.ToString(CultureInfo.InvariantCulture) + " has a value in column "
                                 + column.ToString(CultureInfo.InvariantCulture)
                                 + ", which has no heading, so nothing says what property it is.");
                    continue;
                }

                rows.Add(new SetRow(name, map.Category, property, SetOperator.Equals, value, folder));
                made++;
            }

            if (made == 0)
                warnings.Add("\"" + name + "\" has no values in any of its columns, so it defines nothing.");
        }

        /// <summary>An operator as somebody would write it in a spreadsheet.</summary>
        private static bool TryOperator(string text, out SetOperator op)
        {
            var word = text.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);

            switch (word.ToUpperInvariant())
            {
                case "=":
                case "==":
                case "IS":
                case "EQUALS": op = SetOperator.Equals; return true;
                case "CONTAINS":
                case "HAS":
                case "LIKE": op = SetOperator.Contains; return true;
                case "STARTSWITH":
                case "BEGINSWITH": op = SetOperator.StartsWith; return true;
                case "ENDSWITH": op = SetOperator.EndsWith; return true;
                case "MATCHES":
                case "WILDCARD":
                case "WILDCARDMATCH": op = SetOperator.WildcardMatch; return true;
                case ">":
                case "GREATERTHAN": op = SetOperator.GreaterThan; return true;
                case ">=":
                case "GREATERTHANOREQUAL": op = SetOperator.GreaterThanOrEqual; return true;
                case "<":
                case "LESSTHAN": op = SetOperator.LessThan; return true;
                case "<=":
                case "LESSTHANOREQUAL": op = SetOperator.LessThanOrEqual; return true;
                case "EXISTS":
                case "DEFINED": op = SetOperator.Defined; return true;
                case "HASCATEGORY": op = SetOperator.HasCategory; return true;
            }

            op = SetOperator.Equals;
            return false;
        }

        private static string Text(SheetTable sheet, int row, int column)
        {
            if (row <= 0 || column <= 0 || row > sheet.Rows.Count) return string.Empty;

            var cells = sheet.Rows[row - 1];
            var cell = column > cells.Count ? null : cells[column - 1];

            return (cell ?? string.Empty).Trim();
        }
    }
}
