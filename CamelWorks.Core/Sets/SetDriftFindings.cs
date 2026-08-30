using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Sets
{
    /// <summary>
    /// What a set was feeding, so a drift finding can say what it disarmed.
    ///
    /// The whole reason the check is worth running. "The Level 3 Ductwork set returns nothing" is
    /// a curiosity; "the Level 3 Ductwork set returns nothing, and three clash tests and a
    /// clearance check are pointed at it" is a morning's work somebody has to do before they
    /// trust another green board.
    /// </summary>
    public sealed class SetUse
    {
        /// <summary>Name a use.</summary>
        /// <param name="what">What kind of thing leans on it — a clash test, a rule.</param>
        /// <param name="named">Which one, by the name a person would recognise.</param>
        public SetUse(string what, string named)
        {
            What = string.IsNullOrWhiteSpace(what) ? "something" : what.Trim();
            Named = string.IsNullOrWhiteSpace(named) ? "(unnamed)" : named.Trim();
        }

        /// <summary>What kind of thing leans on it.</summary>
        public string What { get; }

        /// <summary>Which one.</summary>
        public string Named { get; }

        /// <inheritdoc />
        public override string ToString() => What + " \"" + Named + "\"";
    }

    /// <summary>
    /// Set drift, as findings.
    ///
    /// <b>Identity is the set's name.</b> Not its membership, which is the thing that moves: a
    /// decision somebody takes about a set - "yes, that one emptied on purpose, we deleted the
    /// mezzanine" - has to survive the next run, and a finding keyed on what the set held would be
    /// a new finding every time the answer changed. The name is also how everything downstream
    /// addresses a set, so it is the identity the rest of the product already uses.
    /// </summary>
    public static class SetDriftFindings
    {
        /// <summary>The rule name every drift finding carries.</summary>
        public const string Rule = "Set drift";

        /// <summary>Where how many it held before is kept.</summary>
        public const string Was = "Held before";

        /// <summary>Where how many it holds now is kept.</summary>
        public const string Now = "Holds now";

        /// <summary>Where how many left it is kept.</summary>
        public const string Left = "No longer in it";

        /// <summary>Where what leans on the set is kept.</summary>
        public const string Feeding = "What uses it";

        /// <summary>Where the warning that the named elements are a sample is kept.</summary>
        public const string Sampled = "Named elements";

        /// <summary>
        /// How many of the departed elements to name as children.
        ///
        /// A set that lost nine thousand elements is one thing to decide about and nine thousand
        /// rows nobody scrolls. The parent still says how many there were.
        /// </summary>
        public const int DefaultCap = 500;

        /// <summary>
        /// One drifted set, as a finding, or null when nothing about it is worth saying.
        /// </summary>
        /// <param name="drift">What happened to it.</param>
        /// <param name="uses">What leans on the set, when the caller could find out.</param>
        /// <param name="cap">How many departed elements to name.</param>
        public static Finding? From(SetDriftResult? drift, IReadOnlyList<SetUse>? uses = null,
                                    int cap = DefaultCap)
        {
            if (drift == null || !drift.Matters) return null;

            var parent = Finding.CreateParent(FindingSource.SetDrift, Rule,
                                              drift.Explain(), drift.Name);

            parent.Detail = Detail(drift, uses);

            parent.Facts[Was] = Count(drift.Was);
            parent.Facts[Now] = Count(drift.Now);

            if (drift.Left.Count > 0) parent.Facts[Left] = Count(drift.Left.Count);

            if (!drift.IsCertain)
                parent.Facts[Sampled] = "a sample — the counts are exact, the list is not";

            if (uses != null && uses.Count > 0)
                parent.Facts[Feeding] = string.Join("; ", uses.Select(u => u.ToString()));

            // The elements that went, so somebody can look at them and see why. A set that lost
            // its ductwork because the ductwork was deleted is a different morning from one that
            // lost it because a property was renamed, and only the elements tell them apart.
            var made = 0;

            foreach (var element in drift.Left)
            {
                if (element.IsEmpty || made >= Math.Max(0, cap)) continue;

                parent.AddChild(Finding.Create(FindingSource.SetDrift, Rule,
                                               drift.Explain(), new[] { element }, drift.Name));
                made++;
            }

            return parent;
        }

        /// <summary>A whole run's drift, as findings. A set holding steady makes none.</summary>
        /// <param name="drifted">What every set did.</param>
        /// <param name="usesOf">What leans on a set, by name.</param>
        /// <param name="cap">How many departed elements to name per set.</param>
        public static IReadOnlyList<Finding> For(IEnumerable<SetDriftResult>? drifted,
                                                 Func<string, IReadOnlyList<SetUse>>? usesOf = null,
                                                 int cap = DefaultCap)
        {
            var found = new List<Finding>();

            foreach (var drift in drifted ?? Array.Empty<SetDriftResult>())
            {
                if (drift == null) continue;

                var uses = usesOf == null ? null : usesOf(drift.Name);

                if (From(drift, uses, cap) is Finding finding) found.Add(finding);
            }

            return found;
        }

        /// <summary>
        /// The second line: what this cost, in the terms somebody has to act on.
        /// </summary>
        /// <param name="drift">What happened.</param>
        /// <param name="uses">What leans on the set.</param>
        public static string Detail(SetDriftResult drift, IReadOnlyList<SetUse>? uses)
        {
            if (drift == null) return string.Empty;

            var said = new List<string>();

            if (uses != null && uses.Count > 0)
            {
                said.Add(Ones(uses) + " pointed at this set, and "
                         + (drift.Kind == SetDriftKind.Shrank
                             ? "now cover less of the model than when they were set up."
                             : "now cover nothing. A check over nothing does not fail — it passes."));
            }
            else
            {
                said.Add(drift.Kind == SetDriftKind.Shrank
                    ? "Anything pointed at this set now covers less of the model than it did."
                    : "Anything pointed at this set now covers nothing, and a check over nothing "
                      + "does not fail — it passes.");
            }

            if (drift.Kind == SetDriftKind.Gone)
                said.Add("A set is addressed by name, so this reads the same whether it was "
                         + "deleted or renamed.");

            return string.Join(" ", said);
        }

        private static string Ones(IReadOnlyList<SetUse> uses)
        {
            var named = string.Join(", ", uses.Take(4).Select(u => u.ToString()));

            return uses.Count <= 4
                ? named + (uses.Count == 1 ? " is" : " are")
                : named + " and " + (uses.Count - 4).ToString(CultureInfo.InvariantCulture)
                  + " more are";
        }

        private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
