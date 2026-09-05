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
            var held = (tests as IReadOnlyList<(string Id, string Name)>) ?? tests.ToList();

            // One place per discipline when the field belongs to a side, and one running count
            // down the list otherwise. Which of the two is wanted is decided by what the field
            // is: a row's number is a fact about that discipline, so every test the discipline
            // appears in has to read the same one.
            var places = perSide && side != null ? Placed(book, held, named) : null;

            var at = 0;

            foreach (var test in held)
            {
                var id = (test.Id ?? string.Empty).Trim();

                if (id.Length == 0) continue;

                var name = test.Name ?? string.Empty;
                var read = book.Read(id, name);

                // Counted whether or not it can be written: a test the pattern refuses still
                // occupies its place in the run, so refusing the third of ten does not shift the
                // seven under it.
                var where = at++;

                if (read == null)
                {
                    made.Add(new NamingStep(id, name, null, how.At(where),
                        book.Knows(id) ? NamingRefusal.Unread : NamingRefusal.Unbound));
                    continue;
                }

                var pattern = book.Compiled(read.Pattern);

                if (pattern == null || !pattern.Tokens.Contains(named, StringComparer.Ordinal))
                {
                    made.Add(new NamingStep(id, name, null, how.At(where),
                                            NamingRefusal.NoToken));
                    continue;
                }

                if (places == null)
                {
                    var one = how.At(where);
                    var only = book.Written(id, name, named, one);

                    made.Add(only == null
                        ? new NamingStep(id, name, null, one, NamingRefusal.NoToken)
                        : new NamingStep(id, name, only, one, NamingRefusal.None));

                    continue;
                }

                // Both sides in one write. Numbering only the side the field names left a
                // discipline reading one number as a row and another as a column, which is a
                // project whose names disagree with themselves.
                var values = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var one in pattern.Tokens) values[one] = read.Value(one);

                var said = string.Empty;

                foreach (var each in Sides(named))
                {
                    if (!pattern.Tokens.Contains(each.Token, StringComparer.Ordinal)) continue;

                    var whose = Whose(read, pattern, each);

                    if (!places.TryGetValue(whose, out var place)) continue;

                    values[each.Token] = how.At(place);

                    if (string.Equals(each.Token, named, StringComparison.Ordinal))
                        said = values[each.Token];
                }

                made.Add(new NamingStep(id, name, pattern.Rebuild(values), said,
                                        NamingRefusal.None));
            }

            return made;
        }

        /// <summary>
        /// Where each discipline falls, from zero, in the order they first appear.
        ///
        /// Over both sides at once. A discipline is one thing whether a test has it on the left or
        /// the right, so the walk takes the row's identity then the column's from each test, and a
        /// name already placed keeps the place it has. Counting one side alone left a discipline
        /// reading one number as a row and another as a column.
        /// </summary>
        /// <param name="book">The project's naming.</param>
        /// <param name="tests">The tests, in the order they are counted.</param>
        /// <param name="named">The token being written.</param>
        private static Dictionary<string, int> Placed(
            NamingBook book, IReadOnlyList<(string Id, string Name)> tests, string named)
        {
            var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var test in tests)
            {
                var read = book.Read(test.Id, test.Name);

                if (read == null) continue;

                var pattern = book.Compiled(read.Pattern);

                // Only tests this write can actually reach put disciplines in the run. A test on a
                // pattern with no such field would otherwise consume numbers for a discipline
                // nothing ever writes.
                if (pattern == null || !pattern.Tokens.Contains(named, StringComparer.Ordinal))
                    continue;

                foreach (var each in Sides(named))
                {
                    if (!pattern.Tokens.Contains(each.Token, StringComparer.Ordinal)) continue;

                    var whose = Whose(read, pattern, each);

                    if (whose.Length > 0 && !places.ContainsKey(whose))
                        places[whose] = places.Count;
                }
            }

            return places;
        }

        /// <summary>The row and column forms of a field, as the token each side writes.</summary>
        /// <param name="named">The token being written, one of the two.</param>
        private static IEnumerable<(string Token, string Name)> Sides(string named)
        {
            var side = Side(named);

            if (side == null) yield break;

            var tail = named.Substring(side.Length);

            yield return ("row" + tail, "row");
            yield return ("column" + tail, "column");
        }

        /// <summary>
        /// What names the discipline on one side of a test.
        ///
        /// The side's own name token where the pattern carries one, since that is what a person
        /// calls the discipline and it is the same word on either side. Where it does not, the
        /// field's current value stands in: a pattern of codes alone still has one code per
        /// discipline.
        /// </summary>
        /// <param name="read">What the pattern read.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="each">The side, as its token and its name.</param>
        private static string Whose(NamingRead read, MatrixNamePattern pattern,
                                    (string Token, string Name) each) =>
            pattern.Tokens.Contains(each.Name, StringComparer.Ordinal)
                ? read.Value(each.Name)
                : read.Value(each.Token);

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
