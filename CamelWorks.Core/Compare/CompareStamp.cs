using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Sets;

namespace CamelWorks.Core.Compare
{
    /// <summary>One element, and what the stamp would write on it.</summary>
    public sealed class StampRow
    {
        internal StampRow(ElementKey key, ChangeBucket bucket, string detail)
        {
            Key = key;
            Bucket = bucket;
            Detail = detail;
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>Which bucket it is in — the value written to <see cref="CompareStamp.ChangeName"/>.</summary>
        public ChangeBucket Bucket { get; }

        /// <summary>The one-line summary written to <see cref="CompareStamp.DetailName"/>.</summary>
        public string Detail { get; }

        /// <inheritdoc />
        public override string ToString() => ElementChange.Show(Bucket) + " — " + Detail;
    }

    /// <summary>What stamping a comparison onto the model would do.</summary>
    public sealed class StampPlan
    {
        internal StampPlan(IReadOnlyList<StampRow> rows, string against, int unreachable, bool includesUnchanged)
        {
            Rows = rows;
            Against = against;
            Unreachable = unreachable;
            IncludesUnchanged = includesUnchanged;
        }

        /// <summary>One row per element that would be written to.</summary>
        public IReadOnlyList<StampRow> Rows { get; }

        /// <summary>What the comparison was against, written to <see cref="CompareStamp.AgainstName"/>.</summary>
        public string Against { get; }

        /// <summary>
        /// How many changes have no element in this document to write to.
        ///
        /// The removed ones. Their properties would have to be written to elements that are not
        /// here, which is the one thing a stamp genuinely cannot do — so it is counted and said out
        /// loud rather than quietly making the numbers on two screens disagree.
        /// </summary>
        public int Unreachable { get; }

        /// <summary>Whether unchanged elements are stamped too.</summary>
        public bool IncludesUnchanged { get; }

        /// <summary>How many elements would be written to.</summary>
        public int Count => Rows.Count;

        /// <summary>True when it would write nothing.</summary>
        public bool IsEmpty => Rows.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsEmpty) return "nothing to stamp";

            var s = ComparePolicy.Count(Count) + " elements would be stamped with three properties";
            if (Unreachable > 0) s += "; " + ComparePolicy.Count(Unreachable) + " removed elements cannot be, being no longer in the model";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Writes a comparison onto the model as ordinary properties, so the rest of Navisworks can see
    /// it.
    ///
    /// This is what makes a comparison outlast the session. A coloured view lives in one session on
    /// one machine. A property lives in the model: it shows in the properties palette, it can be
    /// searched with the host's own search, a selection set built from it re-resolves, a clash test
    /// can be pointed at "only the things that changed", and a schedule can be filtered by it. None
    /// of that is possible with a colour override.
    ///
    /// Unchanged elements are not stamped by default. Two hundred thousand writes saying "nothing
    /// happened" cost a long wait, a large file and an unreadable journal, and buy nothing, because
    /// carrying the property at all already means "changed", which is what <see cref="AnyChange"/>
    /// uses.
    ///
    /// Writes go through <see cref="IModelWriteTransaction"/> like every other change CamelWorks
    /// makes, so there is a preview first and a journal entry after. Ctrl+Z will not reverse them;
    /// <see cref="Clear"/> is the way back.
    /// </summary>
    public static class CompareStamp
    {
        /// <summary>
        /// The property tab the stamp writes under.
        ///
        /// Not a free choice. The host adapter writes every custom property into one user-defined
        /// tab of its own — <c>NavWriteTransaction.TabName</c> — because that is the only shape the
        /// COM API offers. A stamp naming any other tab would be written to that one anyway and
        /// would then be invisible to the search sets it exists to enable. Core cannot reference the
        /// adapter to assert this, so the caller checks the two agree and the screen reports it if
        /// they stop agreeing.
        /// </summary>
        public const string Category = "CamelWorks";

        /// <summary>The property naming which bucket the element is in.</summary>
        public const string ChangeName = "Change";

        /// <summary>The property carrying the one-line summary of what changed.</summary>
        public const string DetailName = "Change Detail";

        /// <summary>The property naming what the comparison was against.</summary>
        public const string AgainstName = "Compared Against";

        /// <summary>Work out what stamping would write.</summary>
        /// <param name="result">What the comparison found.</param>
        /// <param name="includeUnchanged">Whether to stamp the elements that did not change.</param>
        public static StampPlan Plan(CompareResult? result, bool includeUnchanged = false)
        {
            var rows = new List<StampRow>();

            if (result == null)
                return new StampPlan(rows, string.Empty, 0, includeUnchanged);

            var details = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var change in result.Changes)
            {
                if (change.After == null) continue;
                if (change.After.TryKey(out var key)) details[key.ToString()] = change.Summary();
            }

            var buckets = includeUnchanged
                ? new[] { ChangeBucket.Added, ChangeBucket.Moved, ChangeBucket.Changed, ChangeBucket.Unchanged }
                : new[] { ChangeBucket.Added, ChangeBucket.Moved, ChangeBucket.Changed };

            foreach (var bucket in buckets)
            {
                foreach (var key in result.KeysIn(bucket))
                {
                    // The detail comes off the capped change list, so past the cap an element is
                    // stamped with its bucket and a plain fallback rather than its own summary.
                    // The bucket is the part anybody searches on; the detail is a courtesy.
                    var detail = details.TryGetValue(key.ToString(), out var text)
                        ? text
                        : ElementChange.Show(bucket).ToLowerInvariant();

                    rows.Add(new StampRow(key, bucket, detail));
                }
            }

            return new StampPlan(rows, Against(result), result.KeysIn(ChangeBucket.Removed).Count, includeUnchanged);
        }

        /// <summary>Queue the stamp onto a transaction. The caller previews and commits.</summary>
        /// <param name="transaction">The open write transaction.</param>
        /// <param name="plan">What to write.</param>
        public static int Apply(IModelWriteTransaction transaction, StampPlan? plan)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (plan == null || plan.IsEmpty) return 0;

            foreach (var row in plan.Rows)
            {
                transaction.SetProperty(row.Key, Category, ChangeName, ElementChange.Show(row.Bucket));
                transaction.SetProperty(row.Key, Category, DetailName, ComparePolicy.Trim(row.Detail));
                transaction.SetProperty(row.Key, Category, AgainstName, ComparePolicy.Trim(plan.Against));
            }

            return plan.Count;
        }

        /// <summary>
        /// Queue removal of the stamp from the elements it was put on.
        ///
        /// The way back, and the reason stamping is safe to try. Ctrl+Z does not reverse a custom
        /// property write — the host's undo does not reach them at all — so a feature that can only
        /// be applied is one nobody applies twice.
        /// </summary>
        /// <param name="transaction">The open write transaction.</param>
        /// <param name="keys">The elements to clear.</param>
        public static int Clear(IModelWriteTransaction transaction, IEnumerable<ElementKey>? keys)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (keys == null) return 0;

            var cleared = 0;

            foreach (var key in keys)
            {
                transaction.RemoveProperty(key, Category, ChangeName);
                transaction.RemoveProperty(key, Category, DetailName);
                transaction.RemoveProperty(key, Category, AgainstName);
                cleared++;
            }

            return cleared;
        }

        /// <summary>A set matching one bucket of a stamped comparison.</summary>
        /// <param name="bucket">Which bucket.</param>
        public static SetExpression For(ChangeBucket bucket) =>
            SetExpression.Where(Category, ChangeName, SetOperator.Equals, ElementChange.Show(bucket));

        /// <summary>
        /// A set matching everything the comparison found, whatever kind of change it was.
        ///
        /// Written as "carries the property at all" rather than as a list of values, which is what
        /// makes leaving the unchanged elements unstamped the right default rather than a
        /// shortcut: absence is the answer.
        /// </summary>
        public static SetExpression AnyChange { get; } =
            SetExpression.Where(Category, ChangeName, SetOperator.Defined);

        /// <summary>
        /// The sets a comparison is worth publishing as, in the order they read.
        ///
        /// Named for the comparison so that last week's sets and this week's sit beside each other
        /// in the tree rather than one silently replacing the other.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        public static IReadOnlyList<(string Name, SetExpression Expression, int Count)> Sets(CompareResult? result)
        {
            var sets = new List<(string, SetExpression, int)>();
            if (result == null) return sets;

            var against = Against(result);

            foreach (var bucket in new[] { ChangeBucket.Added, ChangeBucket.Moved, ChangeBucket.Changed })
            {
                var count = result.CountIn(bucket);
                if (count == 0) continue;

                sets.Add(("Changes — " + ElementChange.Show(bucket).ToLowerInvariant() + " since " + against,
                          For(bucket), count));
            }

            var total = result.Added + result.Moved + result.Changed;

            if (total > 0)
                sets.Add(("Changes — everything since " + against, AnyChange, total));

            return sets;
        }

        private static string Against(CompareResult result)
        {
            if (result.BaselineLabel.Length > 0) return result.BaselineLabel;

            return result.BaselineTakenUtc.HasValue
                ? result.BaselineTakenUtc.Value.UtcDateTime.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture)
                : "the baseline";
        }
    }
}
