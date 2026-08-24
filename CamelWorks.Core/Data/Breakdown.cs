using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>How the selection tree's shape becomes properties.</summary>
    public sealed class BreakdownRecipe
    {
        /// <summary>What each level's property is called, before its number.</summary>
        public string Prefix { get; set; } = "WBS";

        /// <summary>What the joined-up key is called.</summary>
        public string CompositeName { get; set; } = "WBS";

        /// <summary>What joins the levels in the composite key.</summary>
        public string Separator { get; set; } = ".";

        /// <summary>How many levels to write. Deeper elements keep only the outermost this many.</summary>
        public int Levels { get; set; } = 6;

        /// <summary>
        /// Whether to drop the outermost path entry.
        ///
        /// In a federation that entry is the file, which every element in a model shares and which
        /// says nothing the model name does not. On a single model it is the only thing keeping two
        /// disciplines apart, so this is a choice rather than a rule.
        /// </summary>
        public bool SkipRoot { get; set; } = true;

        /// <summary>Whether to drop the element's own name, which is the last path entry.</summary>
        public bool SkipLeaf { get; set; } = true;
    }

    /// <summary>What one element's breakdown comes to.</summary>
    public sealed class BreakdownEntry
    {
        internal BreakdownEntry(ElementKey key, IReadOnlyList<string> levels, string composite)
        {
            Key = key; Levels = levels; Composite = composite;
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>Its levels, outermost first.</summary>
        public IReadOnlyList<string> Levels { get; }

        /// <summary>The levels joined up — the key a schedule actually joins on.</summary>
        public string Composite { get; }

        /// <inheritdoc />
        public override string ToString() => Composite;
    }

    /// <summary>Every element's breakdown, and everything that had none.</summary>
    public sealed class BreakdownPlan
    {
        internal BreakdownPlan(IReadOnlyList<BreakdownEntry> entries, int shallow, int deepest)
        {
            Entries = entries; Shallow = shallow; Deepest = deepest;
        }

        /// <summary>The breakdowns.</summary>
        public IReadOnlyList<BreakdownEntry> Entries { get; }

        /// <summary>How many elements sat too shallow in the tree to have one.</summary>
        public int Shallow { get; }

        /// <summary>How many levels the deepest element reached.</summary>
        public int Deepest { get; }

        /// <summary>True when there is nothing to write.</summary>
        public bool IsEmpty => Entries.Count == 0;

        /// <summary>How many distinct composite keys came out.</summary>
        public int Distinct =>
            Entries.Select(e => e.Composite).Distinct(StringComparer.Ordinal).Count();

        /// <summary>One line saying what it comes to.</summary>
        public string Explain()
        {
            if (Entries.Count == 0)
                return Shallow > 0
                    ? "Every element sits too shallow in the tree to break down."
                    : "Nothing to break down.";

            var line = Entries.Count.ToString("N0", CultureInfo.InvariantCulture)
                       + (Entries.Count == 1 ? " element" : " elements") + " across "
                       + Distinct.ToString("N0", CultureInfo.InvariantCulture)
                       + (Distinct == 1 ? " breakdown" : " breakdowns") + ", "
                       + Deepest.ToString(CultureInfo.InvariantCulture) + " levels deep";

            if (Shallow > 0)
                line += ", " + Shallow.ToString("N0", CultureInfo.InvariantCulture) + " too shallow to place";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The selection tree's shape, written onto the elements as properties.
    ///
    /// Everything downstream of a model — a schedule, a cost plan, a progress claim, a 4D sequence —
    /// joins on a breakdown key, and the model almost never carries one. What it carries is the
    /// shape: a tree somebody built when they authored it, where an element sits under its system,
    /// under its zone, under its package. That shape is the breakdown; it is just not readable as a
    /// property, so it cannot be filtered on, grouped by, scheduled against or exported.
    ///
    /// This writes it down. One property per level, and one joined key, which is the column a
    /// spreadsheet actually joins on.
    /// </summary>
    public static class Breakdown
    {
        /// <summary>The most levels worth writing. Beyond this the key stops being readable.</summary>
        public const int MaxLevels = 12;

        /// <summary>Work out each element's breakdown.</summary>
        /// <param name="elements">Each element and its tree path, outermost first.</param>
        /// <param name="recipe">How the shape becomes properties.</param>
        public static BreakdownPlan Of(IEnumerable<KeyValuePair<ElementKey, IReadOnlyList<string>>>? elements,
                                       BreakdownRecipe? recipe = null)
        {
            var entries = new List<BreakdownEntry>();
            var shallow = 0;
            var deepest = 0;

            if (elements == null) return new BreakdownPlan(entries, shallow, deepest);

            var how = recipe ?? new BreakdownRecipe();

            var wanted = how.Levels < 1 ? 1 : how.Levels > MaxLevels ? MaxLevels : how.Levels;
            var separator = how.Separator ?? string.Empty;

            foreach (var element in elements)
            {
                var path = element.Value;

                if (element.Key.IsEmpty || path == null || path.Count == 0) { shallow++; continue; }

                // Both trims apply unconditionally, and an element left with nothing between them
                // is counted as too shallow rather than given a level. Guarding the trims to avoid
                // an empty result sounds kinder and is worse: it hands back the element's own name
                // as its breakdown, so every unbranched element gets a unique key and the grouping
                // everything downstream does on that key falls apart one element at a time.
                var from = how.SkipRoot ? 1 : 0;
                var to = how.SkipLeaf ? path.Count - 1 : path.Count;

                var levels = new List<string>();

                for (var i = from; i < to && levels.Count < wanted; i++)
                {
                    var name = (path[i] ?? string.Empty).Trim();

                    // A tree with an unnamed group in it still has a shape; the gap is kept rather
                    // than closed up, because closing it silently moves everything below it up a
                    // level and two elements at different depths come out with the same key.
                    levels.Add(name.Length == 0 ? "(unnamed)" : name);
                }

                if (levels.Count == 0) { shallow++; continue; }

                if (levels.Count > deepest) deepest = levels.Count;

                entries.Add(new BreakdownEntry(element.Key, levels, string.Join(separator, levels)));
            }

            return new BreakdownPlan(entries, shallow, deepest);
        }

        /// <summary>What each level's property is called.</summary>
        /// <param name="recipe">The recipe, for its prefix.</param>
        /// <param name="level">Which level, from 1.</param>
        public static string LevelName(BreakdownRecipe? recipe, int level)
        {
            var prefix = recipe?.Prefix;
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "WBS";

            return prefix!.Trim() + " " + level.ToString(CultureInfo.InvariantCulture);
        }
    }
}
