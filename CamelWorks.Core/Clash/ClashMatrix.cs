using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// One axis entry: something a clash test can be run against, and what to call it.
    ///
    /// Deliberately not "a model". A matrix is as often discipline against discipline, or a saved
    /// set against a level, and forcing everything through one of those loses the others. What a
    /// side is made of lives in <see cref="Source"/>, which the host resolves; everything above
    /// this line only needs the name and the size.
    /// </summary>
    public sealed class MatrixSide
    {
        /// <summary>Create a side.</summary>
        /// <param name="name">How it reads in a test name.</param>
        /// <param name="source">What the host should select for it.</param>
        /// <param name="elements">How many elements it covers, or -1 when nobody counted.</param>
        public MatrixSide(string name, ClashSide source, int elements = -1)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim();
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Elements = elements;
        }

        /// <summary>How it reads in a test name.</summary>
        public string Name { get; }

        /// <summary>What the host should select for it.</summary>
        public ClashSide Source { get; }

        /// <summary>How many elements it covers, or -1 when nobody counted.</summary>
        public int Elements { get; }

        /// <summary>True when it is known to cover nothing. Unknown is not empty.</summary>
        public bool IsEmpty => Elements == 0;

        /// <inheritdoc />
        public override string ToString() =>
            Elements < 0 ? Name : Name + " (" + Elements.ToString("N0", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>One test the matrix proposes, and whether it is going to be made.</summary>
    public sealed class ProposedTest
    {
        internal ProposedTest(MatrixSide a, MatrixSide b, string name, double tolerance, string? note)
        {
            A = a; B = b; Name = name; Tolerance = tolerance; Note = note;
            IsSelected = note == null;
        }

        /// <summary>The first side.</summary>
        public MatrixSide A { get; }

        /// <summary>The second side.</summary>
        public MatrixSide B { get; }

        /// <summary>What the test will be called.</summary>
        public string Name { get; }

        /// <summary>Tolerance in metres.</summary>
        public double Tolerance { get; }

        /// <summary>
        /// Why this row starts unticked, or null when it starts ticked.
        ///
        /// A row is never removed for being a bad idea — it is shown, unticked, with the reason.
        /// A builder that silently produced 34 tests out of a 6x6 grid would leave the person
        /// wondering which two it decided against and why.
        /// </summary>
        public string? Note { get; }

        /// <summary>Whether it will be created. Starts false when there is a <see cref="Note"/>.</summary>
        public bool IsSelected { get; set; }

        /// <summary>The spec the host needs to make it.</summary>
        /// <param name="folder">Which clash folder to put it in, or null for the root.</param>
        public ClashTestSpec ToSpec(string? folder = null) =>
            new ClashTestSpec(Name, A.Source, B.Source, Tolerance, folder);

        /// <inheritdoc />
        public override string ToString() => Name + (Note == null ? string.Empty : " - " + Note);
    }

    /// <summary>How the grid is turned into tests.</summary>
    public sealed class MatrixOptions
    {
        /// <summary>The default tolerance, in metres. A centimetre, the usual first pass.</summary>
        public const double DefaultTolerance = 0.01;

        /// <summary>How many tests a single build will produce before it stops.</summary>
        public const int DefaultCap = 400;

        /// <summary>Tolerance for every test, in metres.</summary>
        public double Tolerance { get; set; } = DefaultTolerance;

        /// <summary>
        /// Whether a side is paired with itself.
        ///
        /// Off by default. A model against itself is a legitimate test — it finds the pipe running
        /// through its own hanger — but it is not what anybody means by a discipline matrix, and
        /// turning it on by default makes the diagonal of every grid a test nobody asked for.
        /// </summary>
        public bool SelfPairs { get; set; }

        /// <summary>Name template. <c>{a}</c>, <c>{b}</c> and <c>{n}</c> are replaced.</summary>
        public string NamePattern { get; set; } = "{a} vs {b}";

        /// <summary>Test names the document already has, so the builder does not propose a duplicate.</summary>
        public IReadOnlyCollection<string>? Existing { get; set; }

        /// <summary>How many tests to produce before stopping.</summary>
        public int Cap { get; set; } = DefaultCap;
    }

    /// <summary>Every test the grid proposes, and what it decided against.</summary>
    public sealed class MatrixPlan
    {
        internal MatrixPlan(IReadOnlyList<ProposedTest> tests, int mirrored, int dropped)
        {
            Tests = tests; Mirrored = mirrored; Dropped = dropped;
        }

        /// <summary>The proposed tests, in grid order.</summary>
        public IReadOnlyList<ProposedTest> Tests { get; }

        /// <summary>
        /// How many pairings were the reverse of one already in the list.
        ///
        /// Counted rather than listed: "STR vs ARC" sitting greyed out under "ARC vs STR" is noise
        /// on every row of a square grid, and the number is the only part worth saying.
        /// </summary>
        public int Mirrored { get; }

        /// <summary>How many pairings were not produced because the cap was reached.</summary>
        public int Dropped { get; }

        /// <summary>How many are ticked.</summary>
        public int Selected => Tests.Count(t => t.IsSelected);

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var s = Selected.ToString("N0", CultureInfo.InvariantCulture) + " of "
                  + Tests.Count.ToString("N0", CultureInfo.InvariantCulture) + " pairings ticked";

            if (Mirrored > 0)
                s += " - " + Mirrored.ToString("N0", CultureInfo.InvariantCulture)
                   + " were the same pair the other way round";

            if (Dropped > 0)
                s += " - " + Dropped.ToString("N0", CultureInfo.InvariantCulture) + " beyond the cap";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Turns two lists into every clash test between them.
    ///
    /// The job it replaces is an afternoon: a coordinator opens Clash Detective, presses Add Test
    /// thirty-six times, and picks two selections and a tolerance on each one. It is not difficult
    /// work and that is exactly the problem — it is long enough to be done once and then never
    /// redone when a model is added, which is how a matrix quietly stops covering the federation it
    /// was drawn for.
    ///
    /// Three decisions do the work, and each is visible on screen rather than implied:
    ///
    /// <list type="bullet">
    /// <item>A pair and its reverse are one test. "Architecture vs Structure" and "Structure vs
    /// Architecture" find the same conflicts, and a square grid is half redundant.</item>
    /// <item>A test whose name the document already has is proposed but not ticked. Running the
    /// builder twice must not produce two of everything, and the second run is the common one:
    /// a model was added and the matrix needs its row.</item>
    /// <item>An empty side is shown, not hidden. A discipline with nothing in it usually means a
    /// model failed to load, and that is worth seeing on the screen where it matters.</item>
    /// </list>
    /// </summary>
    public static class ClashMatrix
    {
        /// <summary>Build the grid.</summary>
        /// <param name="rows">The first axis.</param>
        /// <param name="columns">The second axis.</param>
        /// <param name="options">How to turn the grid into tests.</param>
        public static MatrixPlan Build(IReadOnlyList<MatrixSide> rows, IReadOnlyList<MatrixSide> columns,
                                       MatrixOptions? options = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            options ??= new MatrixOptions();

            var existing = new HashSet<string>(
                options.Existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tests = new List<ProposedTest>();

            var mirrored = 0;
            var dropped = 0;
            var number = 0;

            foreach (var a in rows.Where(r => r != null))
            {
                foreach (var b in columns.Where(c => c != null))
                {
                    var same = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

                    if (same && !options.SelfPairs) continue;

                    if (!seen.Add(Unordered(a.Name, b.Name))) { mirrored++; continue; }

                    if (tests.Count >= Math.Max(1, options.Cap)) { dropped++; continue; }

                    number++;
                    var name = Name(options.NamePattern, a, b, number);

                    // A name the grid produces twice is worse than a duplicate of an existing test:
                    // the host would take both and nobody could tell them apart afterwards.
                    if (!proposed.Add(name))
                    {
                        name = name + " (" + number.ToString(CultureInfo.InvariantCulture) + ")";
                        proposed.Add(name);
                    }

                    tests.Add(new ProposedTest(a, b, name, options.Tolerance, Why(a, b, name, same, existing)));
                }
            }

            return new MatrixPlan(tests, mirrored, dropped);
        }

        /// <summary>Fill in a name template.</summary>
        /// <param name="pattern">The template.</param>
        /// <param name="a">The first side.</param>
        /// <param name="b">The second side.</param>
        /// <param name="number">Which test this is, from one.</param>
        public static string Name(string? pattern, MatrixSide a, MatrixSide b, int number)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var text = string.IsNullOrWhiteSpace(pattern) ? "{a} vs {b}" : pattern!;

            return text
                .Replace("{a}", a.Name)
                .Replace("{b}", b.Name)
                .Replace("{n}", number.ToString(CultureInfo.InvariantCulture))
                .Trim();
        }

        private static string? Why(MatrixSide a, MatrixSide b, string name, bool same,
                                   ICollection<string> existing)
        {
            if (existing.Contains(name)) return "a test with this name is already in the document";

            if (a.IsEmpty && b.IsEmpty) return "neither side has anything in it";
            if (a.IsEmpty) return "\"" + a.Name + "\" has nothing in it";
            if (b.IsEmpty) return "\"" + b.Name + "\" has nothing in it";

            if (same) return "a side against itself, which is real but rarely what a matrix is for";

            return null;
        }

        // Ordinal-ignore-case ordering so the pair key is the same whichever way round it arrives.
        // Length-prefixed rather than joined by a separator: any separator can turn up inside a
        // model name, and ("AB", "C") colliding with ("A", "BC") would silently drop a real test.
        private static string Unordered(string a, string b)
        {
            var first = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a : b;
            var second = ReferenceEquals(first, a) ? b : a;

            return first.Length.ToString(CultureInfo.InvariantCulture) + ":" + first + second;
        }
    }
}
