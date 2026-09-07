using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CamelWorks.Core.Clash
{
    /// <summary>One piece a test name can be built out of.</summary>
    public sealed class NameToken
    {
        internal NameToken(string token, string title, string why)
        {
            Token = token; Title = title; Why = why;
        }

        /// <summary>What goes in the pattern, braces and all.</summary>
        public string Token { get; }

        /// <summary>What it is called on a button.</summary>
        public string Title { get; }

        /// <summary>What it stands for, for the tooltip.</summary>
        public string Why { get; }

        /// <inheritdoc />
        public override string ToString() => Token;
    }

    /// <summary>
    /// How a test gets its name, as a pattern the person building the matrix writes.
    ///
    /// A pattern rather than a fixed shape. Every job names its tests differently, and the naming is
    /// what is sorted by, filtered on and read down a rail of a hundred and sixty. "ST-01 Steel vs
    /// ME-04 Ductwork (A)" and "Steel | Ductwork" are both right on different jobs.
    ///
    /// The tokens are the things a cell knows. Anything else in the pattern is kept as typed, so a
    /// prefix, a separator and a job code all work without needing a token of their own.
    /// </summary>
    public static class MatrixNaming
    {
        /// <summary>The pattern used when nobody has written one.</summary>
        public const string Default = "{row} vs {column}";

        /// <summary>Every token a pattern can carry, in the order a picker should offer them.</summary>
        public static IReadOnlyList<NameToken> Tokens { get; } = new[]
        {
            new NameToken("{row}", "Row name", "The row's name — the first side of the test."),
            new NameToken("{column}", "Column name", "The column's name — the second side."),
            new NameToken("{rowNumber}", "Row number", "The row's number, when it has one."),
            new NameToken("{columnNumber}", "Column number", "The column's number, when it has one."),
            new NameToken("{rowTitle}", "Row, numbered", "The row's number and name together."),
            new NameToken("{columnTitle}", "Column, numbered", "The column's number and name together."),
            new NameToken("{rowCode}", "Row code", "The row's discipline code, when it has one."),
            new NameToken("{columnCode}", "Column code", "The column's discipline code, when it has one."),
            new NameToken("{rowFolder}", "Row folder", "The folder the row is filed in, when it is in one."),
            new NameToken("{columnFolder}", "Column folder", "The folder the column is filed in."),
            new NameToken("{symbol}", "Symbol", "What is in the cell — A, B, the sheet's own mark."),
            new NameToken("{test}", "Test kind", "Hard, Clearance, Duplicate — what the symbol means."),
            new NameToken("{tolerance}", "Tolerance", "The symbol's tolerance, in millimetres."),
            new NameToken("{level}", "Level", "The storey the board is about, when it names one."),
            new NameToken("{n}", "Number", "Where it falls in the build, from one."),
        };

        /// <summary>
        /// Fill a pattern in for one cell.
        ///
        /// Unknown braces are left exactly as typed rather than blanked. Somebody halfway through
        /// writing "{ro" should see "{ro", not a name that has quietly lost a word — and a job
        /// whose names really do contain braces keeps them.
        /// </summary>
        /// <param name="pattern">The pattern, or null for the default.</param>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <param name="symbol">The cell's symbol, and what it means.</param>
        /// <param name="at">Where this falls in the build, from one.</param>
        /// <param name="scheme">The board's scheme, for its custom fields. Null for none.</param>
        /// <param name="level">The storey the board is about, or null when it names none.</param>
        public static string Fill(string? pattern, MatrixLine row, MatrixLine column,
                                  MatrixSymbol? symbol, int at, MatrixScheme? scheme = null,
                                  string? level = null)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (column == null) throw new ArgumentNullException(nameof(column));

            var said = string.IsNullOrWhiteSpace(pattern) ? Default : pattern!;

            var millimetres = symbol == null
                ? string.Empty
                : (symbol.Tolerance * 1000).ToString("0.###", CultureInfo.InvariantCulture);

            var filled = new StringBuilder(said)
                .Replace("{row}", row.Name)
                .Replace("{column}", column.Name)
                .Replace("{rowNumber}", row.Number)
                .Replace("{columnNumber}", column.Number)
                .Replace("{rowCode}", row.Code)
                .Replace("{columnCode}", column.Code)
                .Replace("{rowFolder}", row.Folder)
                .Replace("{columnFolder}", column.Folder)
                .Replace("{rowTitle}", row.Titled)
                .Replace("{columnTitle}", column.Titled)
                .Replace("{symbol}", symbol?.Symbol ?? string.Empty)
                .Replace("{test}", symbol == null ? string.Empty : Worded(symbol.Test))
                .Replace("{tolerance}", millimetres)
                .Replace("{level}", level ?? string.Empty)
                .Replace("{n}", at.ToString(CultureInfo.InvariantCulture));

            // The scheme's own fields, after the built-in ones so a project that names a field
            // "code" cannot shadow the token the rest of the product already writes.
            foreach (var field in Custom(scheme))
            {
                filled.Replace(field.RowToken, row.Value(field.Key));
                filled.Replace(field.ColumnToken, column.Value(field.Key));
            }

            var made = filled.ToString();

            // Two spaces where a token was empty read as a typo in every export. Collapsed, and
            // the ends trimmed, so a pattern that carries a number nobody filled in still names
            // something a person would have written.
            while (made.Contains("  ")) made = made.Replace("  ", " ");

            return made.Trim().Trim('-', '|', '·', ' ');
        }

        /// <summary>The scheme's fields that are not one of the four the tokens already cover.</summary>
        /// <param name="scheme">The board's scheme, or null.</param>
        public static IEnumerable<MatrixField> Custom(MatrixScheme? scheme) =>
            (scheme?.Fields ?? Array.Empty<MatrixField>()).Where(one => !one.IsBuiltIn);

        /// <summary>
        /// Every token a pattern can carry on this board: the built-in list, then one row and one
        /// column token per custom field.
        ///
        /// A token picker reads from here rather than from <see cref="Tokens"/>, so a field added
        /// to the scheme is offered everywhere a pattern is written without any surface having to
        /// be told about it.
        /// </summary>
        /// <param name="scheme">The board's scheme, or null for the built-in list alone.</param>
        public static IReadOnlyList<NameToken> Offered(MatrixScheme? scheme)
        {
            var held = new List<NameToken>(Tokens);

            foreach (var field in Custom(scheme))
            {
                held.Add(new NameToken(field.RowToken, "Row " + field.Title.ToLowerInvariant(),
                                       "The row's " + field.Title.ToLowerInvariant() + "."));

                held.Add(new NameToken(field.ColumnToken, "Column " + field.Title.ToLowerInvariant(),
                                       "The column's " + field.Title.ToLowerInvariant() + "."));
            }

            return held;
        }

        /// <summary>A test kind as a person writes it.</summary>
        /// <param name="test">The kind.</param>
        public static string Worded(MatrixTest test) => test switch
        {
            MatrixTest.Hard => "Hard",
            MatrixTest.HardConservative => "Hard conservative",
            MatrixTest.Clearance => "Clearance",
            MatrixTest.Duplicate => "Duplicate",
            _ => test.ToString(),
        };

        /// <summary>
        /// A name nothing else in the build is called, by adding a count when it has to.
        ///
        /// Two cells naming one test is caught before this by the planner; what reaches here is
        /// two different tests a pattern happens to name the same - a pattern of "{symbol}" over
        /// a grid of forty cells, say. Numbering them is better than refusing to build, and
        /// better than making forty tests called "A" that nobody can tell apart afterwards.
        /// </summary>
        /// <param name="wanted">The name the pattern produced.</param>
        /// <param name="taken">The names already used, which this adds to.</param>
        public static string Distinct(string wanted, ISet<string> taken)
        {
            if (taken == null) throw new ArgumentNullException(nameof(taken));

            var stem = string.IsNullOrWhiteSpace(wanted) ? "Test" : wanted.Trim();

            if (taken.Add(stem)) return stem;

            for (var at = 2; at < 10000; at++)
            {
                var tried = stem + " (" + at.ToString(CultureInfo.InvariantCulture) + ")";

                if (taken.Add(tried)) return tried;
            }

            return stem;
        }
    }
}
