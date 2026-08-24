using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>What a write would do to one element.</summary>
    public enum WriteChange
    {
        /// <summary>There is no value there now, so the write adds one.</summary>
        Added = 0,

        /// <summary>A different value is there now, so the write replaces it.</summary>
        Changed = 1,

        /// <summary>The value there now is already the one being written.</summary>
        Unchanged = 2,

        /// <summary>A value is there now and the write would empty it.</summary>
        Cleared = 3,
    }

    /// <summary>Whether a write is allowed to replace a value that is already there.</summary>
    public enum ExistingValues
    {
        /// <summary>
        /// Leave them. The default, and the reason this type exists: a second person running the
        /// same rule must not silently replace the first person's answers.
        /// </summary>
        Keep = 0,

        /// <summary>Replace them, having been shown how many and what they were.</summary>
        Overwrite = 1,
    }

    /// <summary>One element's before and after.</summary>
    public sealed class PropertyChange
    {
        internal PropertyChange(ElementKey key, string label, string? before, string? after, WriteChange kind)
        {
            Key = key; Label = label; Before = before; After = after; Kind = kind;
        }

        /// <summary>The element.</summary>
        public ElementKey Key { get; }

        /// <summary>How the element reads on screen, so a diff row is a name and not a key.</summary>
        public string Label { get; }

        /// <summary>What it holds now, or null when it holds nothing.</summary>
        public string? Before { get; }

        /// <summary>What it would hold.</summary>
        public string? After { get; }

        /// <summary>Which of the four this is.</summary>
        public WriteChange Kind { get; }

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            WriteChange.Added => Label + ": (nothing) → " + Show(After),
            WriteChange.Changed => Label + ": " + Show(Before) + " → " + Show(After),
            WriteChange.Cleared => Label + ": " + Show(Before) + " → (nothing)",
            _ => Label + ": " + Show(After) + " already",
        };

        private static string Show(string? value) =>
            string.IsNullOrEmpty(value) ? "(empty)" : "\"" + value + "\"";
    }

    /// <summary>One value that appears in the scope now, and how many elements hold it.</summary>
    public sealed class ValueCount
    {
        internal ValueCount(string? value, int count) { Value = value; Count = count; }

        /// <summary>The value, or null for elements that hold none.</summary>
        public string? Value { get; }

        /// <summary>How many hold it.</summary>
        public int Count { get; }

        /// <inheritdoc />
        public override string ToString() =>
            (Value == null ? "(nothing)" : "\"" + Value + "\"")
            + " × " + Count.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What writing a property would actually change, element by element.
    ///
    /// The seam's <c>WritePreview</c> answers "how many", which is enough to stop somebody writing
    /// to a hundred thousand elements by accident. It is not enough to answer the question people
    /// actually have in front of a write: <i>is anything already there, and is it mine?</i> A
    /// coordinator who runs a zoning rule on Tuesday and a colleague who runs their own on
    /// Wednesday will otherwise discover the collision in a report a month later.
    ///
    /// So the three cases are separated and counted. Added is free. Unchanged is skipped entirely
    /// — a re-run over an unchanged federation should be no host writes, not two hundred thousand
    /// of them to the values they already hold. Changed is the one that needs a decision, and
    /// <see cref="ExistingValues"/> makes the decision explicit rather than implied by pressing a
    /// button.
    /// </summary>
    public sealed class WriteDiff
    {
        internal WriteDiff(string category, string name, IReadOnlyList<PropertyChange> changes,
                           IReadOnlyList<PropertyChange> every, IReadOnlyList<ValueCount> before,
                           IReadOnlyList<ElementKey> missing, int exampleCap)
        {
            Category = category; Name = name; Changes = changes; Every = every; Before = before;
            Missing = missing; ExampleCap = exampleCap;
        }

        /// <summary>The property tab.</summary>
        public string Category { get; }

        /// <summary>The property name.</summary>
        public string Name { get; }

        /// <summary>
        /// Before and after for the screen, capped at <see cref="ExampleCap"/>.
        ///
        /// For showing, never for writing. <see cref="Every"/> is the list a write walks - the
        /// difference between the two is the difference between a preview and the job.
        /// </summary>
        public IReadOnlyList<PropertyChange> Changes { get; }

        /// <summary>
        /// Every element that would change, uncapped.
        ///
        /// Here because two screens wrote from <see cref="Changes"/>, which is the first twenty-five
        /// rows: a code table applied to eight hundred elements landed on twenty-five of them, the
        /// button said "Write 25 elements", and the count agreed with itself all the way down. A
        /// preview list and a work list read the same at the call site and are not the same thing,
        /// so they are two properties now.
        /// </summary>
        public IReadOnlyList<PropertyChange> Every { get; }

        /// <summary>What the scope holds now, by distinct value, largest first.</summary>
        public IReadOnlyList<ValueCount> Before { get; }

        /// <summary>Keys that no longer resolve. Counted, never silently dropped.</summary>
        public IReadOnlyList<ElementKey> Missing { get; }

        /// <summary>How many example rows are kept.</summary>
        public int ExampleCap { get; }

        /// <summary>How many elements would gain a value they do not have.</summary>
        public int Added { get; internal set; }

        /// <summary>How many hold a different value already.</summary>
        public int Changed { get; internal set; }

        /// <summary>How many already hold exactly this value.</summary>
        public int Unchanged { get; internal set; }

        /// <summary>How many hold a value that would be emptied.</summary>
        public int Cleared { get; internal set; }

        /// <summary>
        /// How many elements carry a non-empty value for this property already.
        ///
        /// Not derivable from the four counts above: an element that already holds exactly the
        /// value being written is Unchanged, and so is one that holds nothing and is having
        /// nothing written to it.
        /// </summary>
        public int Present { get; internal set; }

        /// <summary>How many elements were looked at.</summary>
        public int Scope => Added + Changed + Unchanged + Cleared + Missing.Count;

        /// <summary>True when somebody's existing answer is in the way.</summary>
        public bool WouldOverwrite => Changed > 0 || Cleared > 0;

        /// <summary>True when there is nothing to do either way.</summary>
        public bool IsEmpty => Added == 0 && Changed == 0 && Cleared == 0;

        /// <summary>How many elements a write would actually touch under the given policy.</summary>
        /// <param name="existing">Whether values already there may be replaced.</param>
        public int Writes(ExistingValues existing) =>
            existing == ExistingValues.Overwrite ? Added + Changed + Cleared : Added;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var parts = new List<string>();

            if (Added > 0) parts.Add(Added.ToString("N0", CultureInfo.InvariantCulture) + " to add");
            if (Changed > 0) parts.Add(Changed.ToString("N0", CultureInfo.InvariantCulture) + " already hold something else");
            if (Cleared > 0) parts.Add(Cleared.ToString("N0", CultureInfo.InvariantCulture) + " would be emptied");
            if (Unchanged > 0) parts.Add(Unchanged.ToString("N0", CultureInfo.InvariantCulture) + " already correct");
            if (Missing.Count > 0) parts.Add(Missing.Count.ToString("N0", CultureInfo.InvariantCulture) + " no longer found");

            return parts.Count == 0
                ? "nothing in scope"
                : Category + " / " + Name + " — " + string.Join(" · ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Works out a write's before and after without writing anything.
    ///
    /// Reads the property once per element and compares. That read is the expensive part on a
    /// federation, which is why the result carries every count a screen needs rather than making
    /// the caller walk the model again to answer a follow-up question.
    /// </summary>
    public static class PropertyDiff
    {
        /// <summary>How many before-and-after rows are kept for display.</summary>
        public const int DefaultExampleCap = 25;

        /// <summary>How many distinct existing values are kept.</summary>
        public const int DefaultValueCap = 12;

        /// <summary>
        /// Pair each key with the element it resolves to, or null when it no longer resolves.
        ///
        /// Streamed rather than listed: on a scope of a hundred thousand keys the diff wants one
        /// element at a time, and materialising every resolved item first would hold the whole
        /// federation in memory to look at each one once.
        /// </summary>
        /// <param name="model">The document to resolve against.</param>
        /// <param name="keys">The scope.</param>
        public static IEnumerable<KeyValuePair<ElementKey, IModelItem?>> Resolve(
            IModelDocument model, IEnumerable<ElementKey> keys)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            foreach (var key in keys)
                yield return new KeyValuePair<ElementKey, IModelItem?>(key, model.Resolve(key));
        }

        /// <summary>Diff a write of one value across a scope.</summary>
        /// <param name="category">The property tab.</param>
        /// <param name="name">The property name.</param>
        /// <param name="items">The elements in scope. A null entry is a key that stopped resolving.</param>
        /// <param name="value">What each element would end up holding.</param>
        /// <param name="exampleCap">How many before-and-after rows to keep.</param>
        public static WriteDiff Of(string category, string name,
                                   IEnumerable<KeyValuePair<ElementKey, IModelItem?>> items,
                                   Func<IModelItem, string?> value,
                                   int exampleCap = DefaultExampleCap)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (exampleCap < 0) throw new ArgumentOutOfRangeException(nameof(exampleCap), "the cap cannot be negative");

            var changes = new List<PropertyChange>();
            var every = new List<PropertyChange>();
            var missing = new List<ElementKey>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            var added = 0;
            var changed = 0;
            var unchanged = 0;
            var cleared = 0;
            var present = 0;

            foreach (var entry in items)
            {
                var item = entry.Value;

                if (item == null) { missing.Add(entry.Key); continue; }

                var before = item.Property(category, name);
                var after = value(item);

                var bucket = before ?? "\0";
                counts[bucket] = counts.TryGetValue(bucket, out var seen) ? seen + 1 : 1;
                values[bucket] = before;

                if (!string.IsNullOrEmpty(before)) present++;

                var kind = Classify(before, after);

                switch (kind)
                {
                    case WriteChange.Added: added++; break;
                    case WriteChange.Changed: changed++; break;
                    case WriteChange.Cleared: cleared++; break;
                    default: unchanged++; break;
                }

                // Unchanged rows are counted but never shown. A diff whose first twenty rows all
                // say "already correct" hides the one row that does not.
                if (kind == WriteChange.Unchanged) continue;

                var change = new PropertyChange(item.Key, item.DisplayName, before, after, kind);

                every.Add(change);

                if (changes.Count < exampleCap) changes.Add(change);
            }

            var before_ = counts
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Take(DefaultValueCap)
                .Select(c => new ValueCount(values[c.Key], c.Value))
                .ToList();

            return new WriteDiff(category, name, changes, every, before_, missing, exampleCap)
            {
                Added = added,
                Changed = changed,
                Unchanged = unchanged,
                Cleared = cleared,
                Present = present,
            };
        }

        /// <summary>Diff a write of the same value to everything in scope.</summary>
        /// <param name="category">The property tab.</param>
        /// <param name="name">The property name.</param>
        /// <param name="items">The elements in scope.</param>
        /// <param name="value">The value every one of them would end up holding.</param>
        /// <param name="exampleCap">How many before-and-after rows to keep.</param>
        public static WriteDiff Of(string category, string name,
                                   IEnumerable<KeyValuePair<ElementKey, IModelItem?>> items,
                                   string? value,
                                   int exampleCap = DefaultExampleCap) =>
            Of(category, name, items, _ => value, exampleCap);

        /// <summary>
        /// What a column holds right now, without proposing to change it.
        ///
        /// Written as a diff of the property against itself, which makes every element Unchanged
        /// and leaves <see cref="WriteDiff.Present"/> and <see cref="WriteDiff.Before"/> saying
        /// what a rename or a delete is about to affect. Rather than a second traversal with its
        /// own subtly different idea of what "empty" means.
        /// </summary>
        /// <param name="category">The property tab.</param>
        /// <param name="name">The property name.</param>
        /// <param name="items">The elements in scope.</param>
        public static WriteDiff Census(string category, string name,
                                       IEnumerable<KeyValuePair<ElementKey, IModelItem?>> items)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (name == null) throw new ArgumentNullException(nameof(name));

            return Of(category, name, items, item => item.Property(category, name));
        }

        /// <summary>
        /// Whether a write should actually queue this element.
        ///
        /// Under <see cref="ExistingValues.Keep"/> only the elements with nothing there are
        /// written, and nothing else — which is what makes running the same rule twice safe, and
        /// running it after a colleague did non-destructive.
        /// </summary>
        /// <param name="kind">What the write would do to this element.</param>
        /// <param name="existing">Whether values already there may be replaced.</param>
        public static bool ShouldWrite(WriteChange kind, ExistingValues existing) => kind switch
        {
            WriteChange.Added => true,
            WriteChange.Changed => existing == ExistingValues.Overwrite,
            WriteChange.Cleared => existing == ExistingValues.Overwrite,
            _ => false,
        };

        /// <summary>Which of the four a single before-and-after is.</summary>
        /// <param name="before">What is there now.</param>
        /// <param name="after">What would be written.</param>
        public static WriteChange Classify(string? before, string? after)
        {
            var had = !string.IsNullOrEmpty(before);
            var wants = !string.IsNullOrEmpty(after);

            if (!had) return wants ? WriteChange.Added : WriteChange.Unchanged;
            if (!wants) return WriteChange.Cleared;

            return string.Equals(before, after, StringComparison.Ordinal)
                ? WriteChange.Unchanged
                : WriteChange.Changed;
        }
    }
}
