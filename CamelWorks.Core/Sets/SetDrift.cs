using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Sets
{
    /// <summary>How a set's membership changed, worst first.</summary>
    public enum SetDriftKind
    {
        /// <summary>
        /// Nothing worth saying: it holds what it held, or lost too few for that to be news on a
        /// model somebody is working in.
        /// </summary>
        Steady = 0,

        /// <summary>It holds more than it did, and nothing left it.</summary>
        Grew = 1,

        /// <summary>Some of what it held is no longer in it.</summary>
        Shrank = 2,

        /// <summary>It held something and now holds nothing.</summary>
        Emptied = 3,

        /// <summary>The document no longer has a set of that name.</summary>
        Gone = 4,
    }

    /// <summary>
    /// What one set held at one moment.
    ///
    /// Membership rather than the query behind it, because the query is not the thing that goes
    /// wrong. A search set's definition sits unchanged for months while what it returns moves
    /// under it - a discipline re-exports, a property is renamed, a mapping shifts - and the
    /// definition still reads exactly as it did the day somebody wrote it.
    /// </summary>
    public sealed class SetReading
    {
        /// <summary>Read a set.</summary>
        /// <param name="name">The set's name, as the document holds it.</param>
        /// <param name="members">What it returned.</param>
        /// <param name="whenTicks">When it was read, as UTC ticks.</param>
        /// <param name="whole">
        /// Whether <paramref name="members"/> is all of them. False when a cap cut the list short,
        /// which the count still reflects honestly.
        /// </param>
        /// <param name="held">
        /// How many it returned, when that differs from the number kept. Negative or zero takes
        /// the count of what was kept.
        /// </param>
        public SetReading(string name, IEnumerable<ElementKey>? members, long whenTicks = 0,
                          bool whole = true, int held = 0)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("a set reading needs the set's name", nameof(name))
                : name.Trim();

            Members = (members ?? Array.Empty<ElementKey>()).Where(k => !k.IsEmpty).ToList();
            WhenTicks = whenTicks;
            IsWhole = whole;
            Held = held > 0 ? held : Members.Count;
        }

        /// <summary>The set's name.</summary>
        public string Name { get; }

        /// <summary>The members that were kept.</summary>
        public IReadOnlyList<ElementKey> Members { get; }

        /// <summary>How many it returned, which is more than <see cref="Members"/> when capped.</summary>
        public int Held { get; }

        /// <summary>Whether the members are all of them.</summary>
        public bool IsWhole { get; }

        /// <summary>When it was read, as UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>Whether the set returned nothing at all.</summary>
        public bool IsEmpty => Held == 0;

        /// <summary>A reading of a set the document no longer has.</summary>
        /// <param name="name">The set's name.</param>
        /// <param name="whenTicks">When it was looked for, as UTC ticks.</param>
        public static SetReading Missing(string name, long whenTicks = 0) =>
            new SetReading(name, null, whenTicks) { Absent = true };

        /// <summary>Whether the document had no set of this name when it was read.</summary>
        public bool Absent { get; private set; }

        /// <inheritdoc />
        public override string ToString() =>
            Name + ": " + (Absent ? "not in the document" : Held + " elements");
    }

    /// <summary>What happened to one set between two readings.</summary>
    public sealed class SetDriftResult
    {
        internal SetDriftResult(string name, SetDriftKind kind, int was, int now,
                                IReadOnlyList<ElementKey> left, IReadOnlyList<ElementKey> joined,
                                bool certain)
        {
            Name = name;
            Kind = kind;
            Was = was;
            Now = now;
            Left = left;
            Joined = joined;
            IsCertain = certain;
        }

        /// <summary>The set.</summary>
        public string Name { get; }

        /// <summary>What happened.</summary>
        public SetDriftKind Kind { get; }

        /// <summary>How many it held before.</summary>
        public int Was { get; }

        /// <summary>How many it holds now.</summary>
        public int Now { get; }

        /// <summary>Which elements are no longer in it, as far as the readings can say.</summary>
        public IReadOnlyList<ElementKey> Left { get; }

        /// <summary>Which elements are in it that were not.</summary>
        public IReadOnlyList<ElementKey> Joined { get; }

        /// <summary>
        /// Whether the named elements are the whole story.
        ///
        /// False when either reading was capped: the counts are still right, and the lists are
        /// then a sample rather than the set of what moved. A report that presented a sample as
        /// the whole would send somebody looking for elements it never mentioned.
        /// </summary>
        public bool IsCertain { get; }

        /// <summary>How much of it is gone, between nothing and all of it.</summary>
        public double Lost => Was <= 0 ? 0 : Math.Min(1.0, (double)(Was - Math.Min(Now, Was)) / Was);

        /// <summary>Whether this is worth anybody's attention.</summary>
        public bool Matters => Kind == SetDriftKind.Emptied || Kind == SetDriftKind.Gone
                               || Kind == SetDriftKind.Shrank;

        /// <summary>How it reads in a report.</summary>
        public string Explain()
        {
            switch (Kind)
            {
                case SetDriftKind.Gone:
                    return "\"" + Name + "\" is not in this document any more. It held "
                           + Count(Was) + " when it was last read.";

                case SetDriftKind.Emptied:
                    return "\"" + Name + "\" returns nothing. It held " + Count(Was)
                           + " when it was last read.";

                case SetDriftKind.Shrank:
                    return "\"" + Name + "\" returns " + Count(Now) + ", down from "
                           + Was.ToString(CultureInfo.InvariantCulture) + " - "
                           + Percent(Lost) + " of it is gone.";

                case SetDriftKind.Grew:
                    return "\"" + Name + "\" returns " + Count(Now) + ", up from "
                           + Was.ToString(CultureInfo.InvariantCulture) + ".";

                default:
                    return "\"" + Name + "\" returns the " + Count(Now) + " it did.";
            }
        }

        private static string Count(int n) =>
            n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " element" : " elements");

        private static string Percent(double part) =>
            Math.Round(part * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// What a saved set used to return, and what it returns now.
    ///
    /// <b>Why this is worth a check of its own.</b> Everything downstream leans on sets: a clash
    /// test takes them as its two sides, the clearance check takes them as its inputs, a scoped
    /// IDS rule names one. A search set is a saved query, so what it returns moves whenever the
    /// model does - a discipline re-exports, a property is renamed, a parameter mapping shifts -
    /// and the definition still reads exactly as it did the day it was written.
    ///
    /// When that happens the set does not fail. It returns fewer elements, or none, and every
    /// check pointing at it then runs over what is left and <b>passes</b>. A clash test with no
    /// elements finds no clashes. A clearance check over nothing finds nothing too low. A scoped
    /// rule reports "not applicable". The board goes green over a building nobody checked, which
    /// is the one failure this whole product exists to prevent, arriving through the side door.
    ///
    /// <b>Why it is graded rather than diffed.</b> A live model changes every day, and a check
    /// that reported every change would be a check nobody reads - which is the same as no check.
    /// So growth is ordinary and said in a count; a set that lost a quarter of itself is worth a
    /// look; a set that emptied, or that the document no longer has, is a fault, because those are
    /// the two that silently disarm everything pointing at them.
    /// </summary>
    public static class SetDrift
    {
        /// <summary>
        /// How much of a set has to go before it is worth saying, as a fraction.
        ///
        /// A quarter. Below that a busy model produces a page of rows nobody reads and the real
        /// ones hide among them; above it, a set quietly halving would pass unremarked. Neither
        /// number is provable, and this one is a setting rather than a law for that reason.
        /// </summary>
        public const double DefaultLoss = 0.25;

        /// <summary>
        /// How many elements are worth keeping per set to say which ones moved.
        ///
        /// Enough that a set somebody actually coordinates with is named in full, and few enough
        /// that a set holding the whole model does not write a file nobody asked for. Past it the
        /// counts stay exact and the lists become a sample, which the result says.
        /// </summary>
        public const int DefaultCap = 5000;

        /// <summary>
        /// What happened to one set.
        /// </summary>
        /// <param name="was">What it held before, or null when it was never read.</param>
        /// <param name="now">What it holds now.</param>
        /// <param name="loss">How much of it has to go before that is worth saying.</param>
        public static SetDriftResult? Between(SetReading? was, SetReading? now,
                                              double loss = DefaultLoss)
        {
            if (now == null) return null;

            // Never read before. Not drift - there is nothing to have drifted from, and calling a
            // first sight a change would make every new set a finding on the day it was made.
            if (was == null) return null;

            if (now.Absent)
                return new SetDriftResult(now.Name, SetDriftKind.Gone, was.Held, 0,
                                          was.Members, Array.Empty<ElementKey>(), was.IsWhole);

            var certain = was.IsWhole && now.IsWhole;

            var before = new HashSet<ElementKey>(was.Members);
            var after = new HashSet<ElementKey>(now.Members);

            var left = was.Members.Where(k => !after.Contains(k)).ToList();
            var joined = now.Members.Where(k => !before.Contains(k)).ToList();

            return new SetDriftResult(now.Name, Grade(was, now, Bounded(loss)),
                                      was.Held, now.Held, left, joined, certain);
        }

        /// <summary>
        /// What happened to every set that was read before, and to any that has gone.
        /// </summary>
        /// <param name="was">The last readings, by set name.</param>
        /// <param name="now">This run's readings.</param>
        /// <param name="loss">How much of a set has to go before that is worth saying.</param>
        public static IReadOnlyList<SetDriftResult> Across(
            IEnumerable<SetReading>? was, IEnumerable<SetReading>? now, double loss = DefaultLoss)
        {
            var before = By(was);
            var after = By(now);
            var drifted = new List<SetDriftResult>();

            foreach (var reading in after.Values)
                if (before.TryGetValue(reading.Name, out var earlier)
                    && Between(earlier, reading, loss) is SetDriftResult result)
                    drifted.Add(result);

            // A set nothing read this time is one the document no longer has - which is the same
            // disarming as one that emptied, and easier to miss because there is no row for it.
            foreach (var earlier in before.Values)
                if (!after.ContainsKey(earlier.Name)
                    && Between(earlier, SetReading.Missing(earlier.Name), loss)
                        is SetDriftResult gone)
                    drifted.Add(gone);

            return drifted
                .OrderByDescending(d => (int)d.Kind)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Which of the five this is.</summary>
        private static SetDriftKind Grade(SetReading was, SetReading now, double loss)
        {
            // Emptied first: it is a shrink of everything, and it is the one that disarms rather
            // than merely narrows whatever points at the set.
            if (now.IsEmpty && was.Held > 0) return SetDriftKind.Emptied;

            if (was.Held > 0 && (double)(was.Held - now.Held) / was.Held >= loss)
                return SetDriftKind.Shrank;

            // Everything else is ordinary movement on a live model - it grew, or it lost a few
            // and a few is not news.
            return now.Held > was.Held ? SetDriftKind.Grew : SetDriftKind.Steady;
        }

        private static Dictionary<string, SetReading> By(IEnumerable<SetReading>? readings)
        {
            var map = new Dictionary<string, SetReading>(StringComparer.OrdinalIgnoreCase);

            foreach (var reading in readings ?? Array.Empty<SetReading>())
                if (reading != null) map[reading.Name] = reading;

            return map;
        }

        private static double Bounded(double loss) =>
            loss <= 0 || loss > 1 ? DefaultLoss : loss;
    }
}
