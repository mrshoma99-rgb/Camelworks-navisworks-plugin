using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>One test the matrix asks for, with the kind and tolerance its own cell chose.</summary>
    public sealed class PlannedTest
    {
        internal PlannedTest(MatrixCategory a, MatrixCategory b, string name,
                             MatrixTest test, double tolerance, string symbol)
        {
            A = a; B = b; Name = name; Test = test; Tolerance = tolerance; Symbol = symbol;
        }

        /// <summary>One side.</summary>
        public MatrixCategory A { get; }

        /// <summary>The other.</summary>
        public MatrixCategory B { get; }

        /// <summary>What the test is called.</summary>
        public string Name { get; }

        /// <summary>Which kind of test the cell's symbol asked for.</summary>
        public MatrixTest Test { get; }

        /// <summary>Its tolerance, in metres.</summary>
        public double Tolerance { get; }

        /// <summary>The symbol in the cell that produced it.</summary>
        public string Symbol { get; }

        /// <summary>True when both sides are the same category.</summary>
        public bool IsSelf => ReferenceEquals(A, B);

        /// <summary>What to hand the host.</summary>
        /// <param name="folder">Which clash folder to put it in, or null for the root.</param>
        public ClashTestSpec Spec(string? folder = null) =>
            new ClashTestSpec(Name, ClashSide.Set(A.SetName), ClashSide.Set(B.SetName), Tolerance, folder);

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>Every test the grid asks for, and everything it could not ask for.</summary>
    public sealed class MatrixBuild
    {
        internal MatrixBuild(IReadOnlyList<PlannedTest> tests, IReadOnlyList<string> skipped, int already)
        {
            Tests = tests; Skipped = skipped; Already = already;
        }

        /// <summary>The tests, in grid order.</summary>
        public IReadOnlyList<PlannedTest> Tests { get; }

        /// <summary>Why each cell that produced no test produced none.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>How many were left alone because the document already has that name.</summary>
        public int Already { get; }

        /// <summary>True when there is something to make.</summary>
        public bool IsEmpty => Tests.Count == 0;

        /// <summary>One line saying what the grid came to.</summary>
        public string Explain()
        {
            if (Tests.Count == 0 && Skipped.Count == 0)
                return "Nothing is filled in, so there is nothing to make.";

            var line = Tests.Count == 0
                ? "No tests to make"
                : Tests.Count.ToString(CultureInfo.InvariantCulture)
                  + (Tests.Count == 1 ? " test" : " tests") + " to make";

            if (Already > 0)
                line += ", " + Already.ToString(CultureInfo.InvariantCulture) + " already in the document";

            if (Skipped.Count > 0)
                line += ", " + Skipped.Count.ToString(CultureInfo.InvariantCulture)
                        + (Skipped.Count == 1 ? " cell" : " cells") + " skipped";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Turns a filled-in matrix into the tests it is asking for.
    ///
    /// Every cell decides its own kind and tolerance through the legend, which is the whole point:
    /// a matrix says "hard clash here, twenty-five millimetres of clearance there, and check these
    /// two for duplicates", and a builder that applied one tolerance to the grid would be throwing
    /// away most of what the sheet says.
    ///
    /// Nothing is skipped silently. A category nobody backed with a set, a symbol whose legend row
    /// is switched off, and a name the document already carries are three different reasons for a
    /// cell to produce nothing, and a reader who cannot tell them apart cannot fix any of them.
    /// </summary>
    public static class MatrixPlanner
    {
        /// <summary>Plan the tests.</summary>
        /// <param name="grid">The filled-in matrix.</param>
        /// <param name="legend">What its symbols mean.</param>
        /// <param name="existing">Test names the document already has.</param>
        /// <param name="namePattern">Name template. <c>{a}</c>, <c>{b}</c> and <c>{n}</c> are replaced.</param>
        /// <param name="cap">How many tests to plan before stopping.</param>
        public static MatrixBuild Plan(MatrixGrid? grid, MatrixLegend? legend,
                                       IReadOnlyCollection<string>? existing = null,
                                       string namePattern = "{a} vs {b}",
                                       int cap = MatrixOptions.DefaultCap)
        {
            var tests = new List<PlannedTest>();
            var skipped = new List<string>();
            var already = 0;

            if (grid == null || grid.Categories.Count == 0)
                return new MatrixBuild(tests, skipped, 0);

            var key = legend ?? new MatrixLegend();

            var have = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var number = 0;

            foreach (var (a, b, symbol) in grid.Pairs())
            {
                var first = grid.Categories[a];
                var second = grid.Categories[b];

                var pair = first.Name + " against " + second.Name;

                // A side with no saved set cannot resolve to anything, so the host would refuse the
                // test. Said here, once per cell, rather than as a run of identical refusals later.
                if (first.IsUnbacked || second.IsUnbacked)
                {
                    skipped.Add(pair + ": "
                                + (first.IsUnbacked && second.IsUnbacked
                                    ? "neither category names a saved set."
                                    : (first.IsUnbacked ? first.Name : second.Name)
                                      + " names no saved set."));
                    continue;
                }

                var meaning = key.Meaning(symbol);

                if (meaning == null)
                {
                    skipped.Add(pair + ": nothing says what \"" + symbol + "\" means.");
                    continue;
                }

                if (!meaning.Enabled)
                {
                    skipped.Add(pair + ": \"" + symbol + "\" is switched off in the key.");
                    continue;
                }

                if (tests.Count >= Math.Max(1, cap))
                {
                    skipped.Add(pair + ": the grid asks for more than "
                                + cap.ToString(CultureInfo.InvariantCulture) + " tests.");
                    continue;
                }

                number++;
                var name = Name(namePattern, first, second, number);

                if (have.Contains(name)) { already++; continue; }

                // A name the grid produces twice is worse than one that clashes with an existing
                // test: the host would take both and nobody could tell them apart afterwards.
                if (!made.Add(name))
                {
                    name += " (" + number.ToString(CultureInfo.InvariantCulture) + ")";
                    made.Add(name);
                }

                tests.Add(new PlannedTest(first, second, name, meaning.Test, meaning.Tolerance, symbol));
            }

            return new MatrixBuild(tests, skipped, already);
        }

        private static string Name(string? pattern, MatrixCategory a, MatrixCategory b, int number)
        {
            // Narrowed explicitly. IsNullOrWhiteSpace does not narrow a string? on netstandard2.0,
            // which is the rule written on the Nullable setting in the csproj and which this line
            // broke on its first draft.
            var text = pattern ?? string.Empty;
            if (text.Trim().Length == 0) text = "{a} vs {b}";

            return text.Replace("{a}", a.Name)
                       .Replace("{b}", b.Name)
                       .Replace("{n}", number.ToString("00", CultureInfo.InvariantCulture))
                       .Trim();
        }
    }
}
