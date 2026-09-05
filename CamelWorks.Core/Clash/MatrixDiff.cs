using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>What has to happen to one test to make the document match the matrix.</summary>
    public enum MatrixChange
    {
        /// <summary>The document already has it, as the matrix asks for it.</summary>
        Unchanged = 0,

        /// <summary>The matrix asks for it and the document has nothing like it.</summary>
        Added = 1,

        /// <summary>The document has the pair, but at a different tolerance or under another name.</summary>
        Altered = 2,

        /// <summary>The document has a test the matrix does not ask for.</summary>
        Extra = 3,
    }

    /// <summary>One line of the difference between a matrix and a document.</summary>
    public sealed class MatrixLineChange
    {
        internal MatrixLineChange(MatrixChange change, string name, string what,
                                  MatrixTestPlan? plan, string? testId,
                                  bool renames = false, bool reshapes = false)
        {
            Change = change; Name = name; What = what; Plan = plan; TestId = testId;
            Renames = renames; Reshapes = reshapes;
        }

        /// <summary>
        /// True when building this line only has to rename the test the document already has.
        ///
        /// Stated here rather than re-derived by the builder. The difference is computed once, and
        /// what has to be done about it is part of the difference: a caller that re-derives it from
        /// the words in <see cref="What"/> will get it wrong.
        /// </summary>
        public bool Renames { get; }

        /// <summary>True when the test's tolerance or its kind has to change.</summary>
        public bool Reshapes { get; }

        /// <summary>What has to happen.</summary>
        public MatrixChange Change { get; }

        /// <summary>The test's name - the matrix's for a new one, the document's otherwise.</summary>
        public string Name { get; }

        /// <summary>What is different, in a few words.</summary>
        public string What { get; }

        /// <summary>The test the matrix asks for, or null for one only the document has.</summary>
        public MatrixTestPlan? Plan { get; }

        /// <summary>The document's test, when it has one.</summary>
        public string? TestId { get; }

        /// <inheritdoc />
        public override string ToString() => Change + ": " + Name;
    }

    /// <summary>
    /// The difference between what a matrix asks for and what the document already holds.
    ///
    /// Without it, changing one cell means deleting every test on the job and remaking them all,
    /// which discards every status, assignment, comment and group put on the results. A coordination
    /// matrix is edited constantly and the results are the work; the tests are how the results got
    /// there.
    ///
    /// A test is matched by what it is between rather than by what it is called: two sources and a
    /// kind of test, in a settled order. A rename is therefore not a new test but the same test
    /// under a new name, which is what changing the naming pattern should do.
    /// </summary>
    public static class MatrixDiff
    {
        /// <summary>
        /// Work out what would have to change.
        ///
        /// Tests the document holds that no cell asks for come back as <see cref="MatrixChange.Extra"/>
        /// rather than being deleted by anybody here. A federation usually carries tests nobody
        /// made from this matrix, and a sync that removed them would be a matrix quietly deciding
        /// it owns the whole clash detective.
        /// </summary>
        /// <param name="plan">What the matrix asks for.</param>
        /// <param name="already">What the document holds, with what each test is between.</param>
        public static IReadOnlyList<MatrixLineChange> Between(
            MatrixPlan plan, IReadOnlyList<ClashTestRead> already)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var found = already ?? Array.Empty<ClashTestRead>();
            var lines = new List<MatrixLineChange>();

            // Two ways of asking "is this test already here", tried in that order.
            //
            // First by what it is between and how it is tested, which is the exact match. Then,
            // for whatever is left, by what it is between alone - so a symbol changed from a
            // hard clash to a clearance finds the test that has been carrying the review and
            // alters it, instead of leaving it behind and making a second one.
            var byPairing = new Dictionary<string, ClashTestRead>(StringComparer.Ordinal);
            var bySides = new Dictionary<string, List<ClashTestRead>>(StringComparer.Ordinal);

            foreach (var read in found)
            {
                var pairing = Pairing(read.Shape);

                if (pairing == null) continue;

                if (!byPairing.ContainsKey(pairing)) byPairing[pairing] = read;

                var sides = Sided(read.Shape);

                if (sides == null) continue;

                if (!bySides.TryGetValue(sides, out var kept))
                    bySides[sides] = kept = new List<ClashTestRead>();

                kept.Add(read);
            }

            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var matched = new Dictionary<MatrixTestPlan, ClashTestRead>();

            foreach (var test in plan.Tests)
                if (byPairing.TryGetValue(test.Pairing(), out var read) && claimed.Add(read.Test.Id))
                    matched[test] = read;

            foreach (var test in plan.Tests)
            {
                if (matched.ContainsKey(test)) continue;
                if (!bySides.TryGetValue(test.Sides(), out var kept)) continue;

                foreach (var read in kept)
                    if (claimed.Add(read.Test.Id)) { matched[test] = read; break; }
            }

            foreach (var test in plan.Tests)
            {
                if (!matched.TryGetValue(test, out var read))
                {
                    lines.Add(new MatrixLineChange(MatrixChange.Added, test.Name,
                        "not in the document yet", test, null));
                    continue;
                }

                var differences = new List<string>();

                var renames = !string.Equals(read.Test.Name, test.Name, StringComparison.Ordinal);

                if (renames)
                    differences.Add("named " + Quoted(read.Test.Name) + " in the document");

                var reshapes = false;

                if (read.Shape != null)
                {
                    var kind = Kinded(read.Shape.Kind);

                    if (kind != test.Symbol.Test)
                    {
                        reshapes = true;

                        differences.Add("a " + MatrixNaming.Worded(kind).ToLowerInvariant()
                            + " test in the document rather than "
                            + MatrixNaming.Worded(test.Symbol.Test).ToLowerInvariant());
                    }

                    if (Math.Abs(read.Shape.ToleranceMetres - test.Symbol.Tolerance) > 0.0000005)
                    {
                        reshapes = true;

                        differences.Add("tolerance is "
                            + Millimetres(read.Shape.ToleranceMetres) + " rather than "
                            + Millimetres(test.Symbol.Tolerance));
                    }
                }

                lines.Add(differences.Count == 0
                    ? new MatrixLineChange(MatrixChange.Unchanged, test.Name,
                        "already there, as asked", test, read.Test.Id)
                    : new MatrixLineChange(MatrixChange.Altered, test.Name,
                        string.Join("; ", differences), test, read.Test.Id, renames, reshapes));
            }

            foreach (var read in found)
            {
                if (claimed.Contains(read.Test.Id)) continue;

                lines.Add(new MatrixLineChange(MatrixChange.Extra, read.Test.Name,
                    Pairing(read.Shape) == null
                        ? "the document will not say what it is between"
                        : "in the document, but no cell asks for it",
                    null, read.Test.Id));
            }

            return lines;
        }

        /// <summary>How many lines of each kind, for the sentence above the list.</summary>
        /// <param name="lines">The difference.</param>
        /// <param name="change">Which kind to count.</param>
        public static int Count(IReadOnlyList<MatrixLineChange> lines, MatrixChange change) =>
            lines == null ? 0 : lines.Count(l => l.Change == change);

        /// <summary>
        /// The same fingerprint a plan makes, off a test the host has read back.
        ///
        /// Null when the host will not say what a test is between, which is the honest answer for
        /// a test whose side is a hand-picked selection: there is nothing to match on, so it is
        /// neither claimed nor proposed for deletion.
        /// </summary>
        /// <param name="shape">What the test is between, or null.</param>
        private static string? Pairing(ClashTestSides? shape)
        {
            if (shape == null || !shape.KnowsA || !shape.KnowsB) return null;

            var one = Side(shape.SetA, shape.ScopesA);
            var two = Side(shape.SetB, shape.ScopesB);

            var first = string.CompareOrdinal(one, two) <= 0 ? one : two;
            var second = string.CompareOrdinal(one, two) <= 0 ? two : one;

            return first + "\u001f" + second + "\u001f" + (int)Kinded(shape.Kind);
        }

        /// <summary>What a test the document holds is between, without its kind.</summary>
        /// <param name="shape">What the test is between, or null.</param>
        private static string? Sided(ClashTestSides? shape)
        {
            if (shape == null || !shape.KnowsA || !shape.KnowsB) return null;

            var one = Side(shape.SetA, shape.ScopesA);
            var two = Side(shape.SetB, shape.ScopesB);

            return string.CompareOrdinal(one, two) <= 0
                ? one + "\u001f" + two
                : two + "\u001f" + one;
        }

        private static string Side(string? set, IReadOnlyList<string> scopes) =>
            !string.IsNullOrWhiteSpace(set)
                ? "set:" + set!.Trim().ToLowerInvariant()
                : "models:" + string.Join("|", scopes.Select(s => s.ToLowerInvariant())
                                                     .OrderBy(s => s, StringComparer.Ordinal));

        /// <summary>The host's own word for a test kind, as one of ours.</summary>
        /// <param name="kind">What the host called it.</param>
        public static MatrixTest Kinded(string? kind)
        {
            var said = (kind ?? string.Empty).Trim();

            if (said.IndexOf("clearance", StringComparison.OrdinalIgnoreCase) >= 0)
                return MatrixTest.Clearance;

            if (said.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
                return MatrixTest.Duplicate;

            if (said.IndexOf("conservative", StringComparison.OrdinalIgnoreCase) >= 0)
                return MatrixTest.HardConservative;

            return MatrixTest.Hard;
        }

        private static string Millimetres(double metres) =>
            (metres * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm";

        private static string Quoted(string said) => "\u201c" + said + "\u201d";
    }
}
