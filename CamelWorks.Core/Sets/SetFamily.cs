using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Sets
{
    /// <summary>One set a family would contain, and whether it is going to be made.</summary>
    public sealed class ProposedSet
    {
        internal ProposedSet(string name, string value, SetExpression expression, int elements, string? note)
        {
            Name = name; Value = value; Expression = expression; Elements = elements; Note = note;
            IsSelected = note == null;
        }

        /// <summary>What the set will be called.</summary>
        public string Name { get; }

        /// <summary>The value it splits on.</summary>
        public string Value { get; }

        /// <summary>What it matches.</summary>
        public SetExpression Expression { get; }

        /// <summary>How many elements carried this value when the model was read.</summary>
        public int Elements { get; }

        /// <summary>Why this one starts unticked, or null when it starts ticked.</summary>
        public string? Note { get; }

        /// <summary>Whether it will be made.</summary>
        public bool IsSelected { get; set; }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " (" + Elements.ToString("N0", CultureInfo.InvariantCulture) + ")"
            + (Note == null ? string.Empty : " - " + Note);
    }

    /// <summary>How a family is named and which values are worth a set.</summary>
    public sealed class FamilyOptions
    {
        /// <summary>Below this many elements a value is proposed but not ticked.</summary>
        public const int DefaultFloor = 1;

        /// <summary>How many sets a single family will produce before it stops.</summary>
        public const int DefaultCap = 200;

        /// <summary>Name template. <c>{value}</c> and <c>{n}</c> are replaced.</summary>
        public string NamePattern { get; set; } = "{value}";

        /// <summary>How few elements a value can have and still be ticked.</summary>
        public int Floor { get; set; } = DefaultFloor;

        /// <summary>Names the library already has, so the generator does not propose a duplicate.</summary>
        public IReadOnlyCollection<string>? Existing { get; set; }

        /// <summary>How many sets to produce before stopping.</summary>
        public int Cap { get; set; } = DefaultCap;
    }

    /// <summary>What the generator would make.</summary>
    public sealed class SetFamilyPlan
    {
        internal SetFamilyPlan(string category, string property, IReadOnlyList<ProposedSet> sets,
                               int blanks, int dropped)
        {
            Category = category; Property = property; Sets = sets; Blanks = blanks; Dropped = dropped;
        }

        /// <summary>The property tab the family splits on.</summary>
        public string Category { get; }

        /// <summary>The property the family splits on.</summary>
        public string Property { get; }

        /// <summary>The sets, largest first.</summary>
        public IReadOnlyList<ProposedSet> Sets { get; }

        /// <summary>
        /// How many elements carried no value at all.
        ///
        /// Reported rather than turned into an "unassigned" set. An element with no level is not a
        /// member of a level called nothing — it is an element somebody has to look at, and a set
        /// that quietly collects them makes the gap look handled.
        /// </summary>
        public int Blanks { get; }

        /// <summary>How many distinct values were found beyond the cap.</summary>
        public int Dropped { get; }

        /// <summary>How many are ticked.</summary>
        public int Selected => Sets.Count(s => s.IsSelected);

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var s = Selected.ToString("N0", CultureInfo.InvariantCulture) + " of "
                  + Sets.Count.ToString("N0", CultureInfo.InvariantCulture) + " sets ticked, split on "
                  + Category + " / " + Property;

            if (Blanks > 0)
                s += " - " + Blanks.ToString("N0", CultureInfo.InvariantCulture)
                   + " elements carry no value and are in none of them";

            if (Dropped > 0)
                s += " - " + Dropped.ToString("N0", CultureInfo.InvariantCulture) + " values beyond the cap";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Makes one set per value of a property.
    ///
    /// One set per level, per discipline, per zone, per system: an hour of clicking that gets done
    /// on every job, by hand, from a list somebody reads off the properties panel. The list is
    /// already in the model — a single pass over it produces both the values and how many elements
    /// carry each one, which is the number that decides whether a set is worth having.
    ///
    /// Each set is a rule, not a list of elements, so a set made today still covers the walls
    /// somebody adds tomorrow. That is the difference between a family that stays true and one
    /// that has to be regenerated after every model issue — and regenerating it is exactly the hour
    /// this exists to stop.
    /// </summary>
    public static class SetFamily
    {
        /// <summary>Work out the family.</summary>
        /// <param name="category">The property tab to split on.</param>
        /// <param name="property">The property to split on.</param>
        /// <param name="values">Each element's value, one entry per element; null or empty counts as blank.</param>
        /// <param name="options">How to name them and which to tick.</param>
        public static SetFamilyPlan Of(string category, string property, IEnumerable<string?> values,
                                       FamilyOptions? options = null)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (values == null) throw new ArgumentNullException(nameof(values));

            options ??= new FamilyOptions();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var blanks = 0;

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) { blanks++; continue; }

                var text = value!.Trim();

                // Counted case-insensitively but named by the first spelling seen. "Level 2" and
                // "LEVEL 2" are one level on every job that has ever existed, and two sets whose
                // names differ only in case are two sets nobody can tell apart in a list.
                counts[text] = counts.TryGetValue(text, out var seen) ? seen + 1 : 1;
                if (!spelling.ContainsKey(text)) spelling[text] = text;
            }

            var existing = new HashSet<string>(
                options.Existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var ordered = counts
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cap = Math.Max(1, options.Cap);
            var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sets = new List<ProposedSet>();
            var number = 0;

            foreach (var entry in ordered.Take(cap))
            {
                number++;

                var value = spelling[entry.Key];
                var name = Name(options.NamePattern, value, number);

                if (!proposed.Add(name))
                {
                    name = name + " (" + number.ToString(CultureInfo.InvariantCulture) + ")";
                    proposed.Add(name);
                }

                var expression = SetExpression.Where(category, property, SetOperator.Equals, value);

                sets.Add(new ProposedSet(name, value, expression, entry.Value,
                                         Why(name, entry.Value, options.Floor, existing)));
            }

            return new SetFamilyPlan(category, property, sets, blanks, Math.Max(0, ordered.Count - cap));
        }

        /// <summary>Fill in a name template.</summary>
        /// <param name="pattern">The template.</param>
        /// <param name="value">The value this set is for.</param>
        /// <param name="number">Which set this is, from one.</param>
        public static string Name(string? pattern, string value, int number)
        {
            var text = string.IsNullOrWhiteSpace(pattern) ? "{value}" : pattern!;

            return text
                .Replace("{value}", value)
                .Replace("{n}", number.ToString(CultureInfo.InvariantCulture))
                .Trim();
        }

        private static string? Why(string name, int elements, int floor, ICollection<string> existing)
        {
            if (existing.Contains(name)) return "a set with this name is already in the library";
            if (elements < Math.Max(1, floor))
                return "only " + elements.ToString(CultureInfo.InvariantCulture)
                     + (elements == 1 ? " element carries this" : " elements carry this");

            return null;
        }
    }
}
