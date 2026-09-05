using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.Core.Clash
{
    /// <summary>Why one test's name could not be rewritten.</summary>
    public enum NamingRefusal
    {
        /// <summary>It was rewritten.</summary>
        None = 0,

        /// <summary>No pattern is bound to it, so nothing can read its name.</summary>
        Unbound = 1,

        /// <summary>A pattern is bound but the name does not fit it.</summary>
        Unread = 2,

        /// <summary>The pattern is bound and fits, but carries no such token.</summary>
        NoToken = 3,
    }

    /// <summary>What one test's name would become.</summary>
    public sealed class NamingStep
    {
        internal NamingStep(string id, string was, string? now, string value, NamingRefusal why)
        {
            Id = id;
            Was = was;
            Now = now;
            Value = value;
            Why = why;
        }

        /// <summary>The host's id for the test.</summary>
        public string Id { get; }

        /// <summary>Its name now.</summary>
        public string Was { get; }

        /// <summary>Its name after the write, or null when it was refused.</summary>
        public string? Now { get; }

        /// <summary>What the token would be set to.</summary>
        public string Value { get; }

        /// <summary>Why it was refused, or <see cref="NamingRefusal.None"/>.</summary>
        public NamingRefusal Why { get; }

        /// <summary>Whether the name would actually change.</summary>
        public bool Changes =>
            Why == NamingRefusal.None
            && !string.Equals(Was, Now, StringComparison.Ordinal);
    }

    /// <summary>
    /// What writing one token across a run of tests would do, worked out before anything is
    /// written.
    ///
    /// Renumbering is the reason this exists. A coordinator with sixty tests wants them numbered
    /// 010, 020, 030 in the order the rail shows them, and every one of those is a rename of the
    /// host's test - so the plan is shown first, refusals and all, rather than half of it being
    /// applied and the rest reported afterwards.
    ///
    /// The values come from a <see cref="MatrixField"/>, which already knows how to count in
    /// numbers and in letters, how to pad and what "the same on every line" means. A test's
    /// position in the run is its position in the list it was given, so the order on screen is the
    /// order that gets numbered.
    /// </summary>
    public static class NamingWrite
    {
        /// <summary>Work out what writing a token across these tests would do.</summary>
        /// <param name="book">The project's naming.</param>
        /// <param name="tests">The tests, in the order they should be counted.</param>
        /// <param name="token">The token to write, without braces.</param>
        /// <param name="how">Where the values come from.</param>
        /// <param name="perSide">
        /// True to give every test that shares a side the same value, rather than counting down
        /// the list. What a field of the side wants: a discipline's number is the same wherever
        /// that discipline appears.
        /// </param>
        public static IReadOnlyList<NamingStep> Plan(
            NamingBook? book, IEnumerable<(string Id, string Name)>? tests, string? token,
            MatrixField? how, bool perSide = false)
        {
            var made = new List<NamingStep>();

            if (book == null || tests == null || how == null) return made;

            var named = (token ?? string.Empty).Trim().Trim('{', '}');

            if (named.Length == 0) return made;

            var side = Side(named);

            // One counter per distinct side when the field belongs to the side rather than to the
            // test, and one running counter otherwise. Which of the two is wanted is decided by
            // what the field is: a row's number is a fact about that discipline, so every test
            // the discipline appears in has to read the same one.
            var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var at = 0;

            foreach (var test in tests)
            {
                var id = (test.Id ?? string.Empty).Trim();

                if (id.Length == 0) continue;

                var name = test.Name ?? string.Empty;

                var read = book.Read(id, name);

                // Counted whether or not it can be written: a test the pattern refuses still
                // occupies its place in the run, so refusing the third of ten does not shift the
                // seven under it.
                var where = at++;

                if (perSide && side != null)
                {
                    var whose = read?.Value(side) ?? string.Empty;

                    if (!places.TryGetValue(whose, out var held))
                        places[whose] = held = places.Count;

                    where = held;
                }

                var value = how.At(where);

                if (read == null)
                {
                    made.Add(new NamingStep(id, name, null, value,
                        book.Knows(id) ? NamingRefusal.Unread : NamingRefusal.Unbound));
                    continue;
                }

                var written = book.Written(id, name, named, value);

                made.Add(written == null
                    ? new NamingStep(id, name, null, value, NamingRefusal.NoToken)
                    : new NamingStep(id, name, written, value, NamingRefusal.None));
            }

            return made;
        }

        /// <summary>
        /// The token naming the side a field belongs to, or null when the field is the test's own.
        ///
        /// A token beginning "row" or "column" is a fact about that discipline, not about the
        /// pair. Numbering such a field down a list of tests gives one discipline a different
        /// number in every test it appears in, which is the opposite of what the number is for.
        /// </summary>
        /// <param name="token">The token being written, without braces.</param>
        public static string? Side(string? token)
        {
            var said = (token ?? string.Empty).Trim();

            if (string.Equals(said, "row", StringComparison.OrdinalIgnoreCase)
                || string.Equals(said, "column", StringComparison.OrdinalIgnoreCase))
                return said.ToLowerInvariant();

            if (said.StartsWith("row", StringComparison.OrdinalIgnoreCase)) return "row";
            if (said.StartsWith("column", StringComparison.OrdinalIgnoreCase)) return "column";

            return null;
        }

        /// <summary>What to tell somebody about a refusal.</summary>
        /// <param name="why">The refusal.</param>
        public static string Said(NamingRefusal why) => why switch
        {
            NamingRefusal.Unbound => "No naming pattern",
            NamingRefusal.Unread => "Its pattern does not fit this name",
            NamingRefusal.NoToken => "Its pattern does not carry that field",
            _ => string.Empty,
        };

        /// <summary>The steps that would change a name.</summary>
        /// <param name="steps">The plan.</param>
        public static IReadOnlyList<NamingStep> Changing(IEnumerable<NamingStep>? steps) =>
            (steps ?? Array.Empty<NamingStep>()).Where(one => one.Changes).ToList();

        /// <summary>The steps that were refused, in the order they were planned.</summary>
        /// <param name="steps">The plan.</param>
        public static IReadOnlyList<NamingStep> Refused(IEnumerable<NamingStep>? steps) =>
            (steps ?? Array.Empty<NamingStep>())
            .Where(one => one.Why != NamingRefusal.None).ToList();
    }
}
