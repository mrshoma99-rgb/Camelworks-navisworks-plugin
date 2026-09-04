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
    /// <b>A pattern rather than a fixed shape.</b> Every job names its tests differently and the
    /// naming is not decoration: it is what somebody sorts by, filters on and reads down a rail
    /// of a hundred and sixty. "ST-01 Steel vs ME-04 Ductwork (A)" and "Steel | Ductwork" are
    /// both right, on different jobs, and neither is worth hard-coding.
    ///
    /// The tokens are the things a cell actually knows. Anything else in the pattern is kept as
    /// typed, so a prefix, a separator and a job code all work without needing a token of their
    /// own.
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
        public static string Fill(string? pattern, MatrixLine row, MatrixLine column,
                                  MatrixSymbol? symbol, int at)
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
                .Replace("{n}", at.ToString(CultureInfo.InvariantCulture))
                .ToString();

            // Two spaces where a token was empty read as a typo in every export. Collapsed, and
            // the ends trimmed, so a pattern that carries a number nobody filled in still names
            // something a person would have written.
            while (filled.Contains("  ")) filled = filled.Replace("  ", " ");

            return filled.Trim().Trim('-', '|', '·', ' ');
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
