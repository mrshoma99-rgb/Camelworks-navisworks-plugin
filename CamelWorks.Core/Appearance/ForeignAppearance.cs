using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Appearance
{
    /// <summary>
    /// Elements sharing one appearance that no layer accounts for.
    ///
    /// Grouped rather than listed because the answer to "what is hidden" is never a list of two
    /// thousand element names — it is "two thousand elements are hidden, and here they are". The
    /// group is also the unit every action works on: select them, adopt them as a layer, put them
    /// back.
    /// </summary>
    public sealed class ForeignGroup
    {
        internal ForeignGroup(bool isHidden, Colour? colour, double? transparency, IReadOnlyList<ElementKey> keys)
        {
            IsHidden = isHidden; Colour = colour; Transparency = transparency; Keys = keys;
        }

        /// <summary>Whether these elements are hidden.</summary>
        public bool IsHidden { get; }

        /// <summary>The colour override they carry, if any.</summary>
        public Colour? Colour { get; }

        /// <summary>The transparency they carry, if any.</summary>
        public double? Transparency { get; }

        /// <summary>The elements.</summary>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>What this state is, in a phrase.</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (IsHidden) parts.Add("hidden");
            if (Colour != null) parts.Add("coloured " + Colour.Value.ToString());
            if (Transparency != null)
                parts.Add((Transparency.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "% transparent");

            return parts.Count == 0 ? "untouched" : string.Join(" and ", parts);
        }

        /// <inheritdoc />
        public override string ToString() =>
            Describe() + " × " + Keys.Count.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What the model looks like that CamelWorks did not ask for.
    ///
    /// This is the half of the Appearance Manager that a layer list cannot give you. A layer stack
    /// says what CamelWorks intends; it says nothing about the isolate somebody left switched on in
    /// Clash Detective, the transparency a colleague applied last Thursday, or the elements a saved
    /// viewpoint hid. Those are the reason a federation looks wrong to one person and fine to
    /// another, and until they are on screen nobody can tell which they are looking at.
    /// </summary>
    public sealed class ForeignAudit
    {
        internal ForeignAudit(int scanned, int claimed, IReadOnlyList<ForeignGroup> groups,
                              int otherGroups, int otherElements)
        {
            Scanned = scanned; Claimed = claimed; Groups = groups;
            OtherGroups = otherGroups; OtherElements = otherElements;
        }

        /// <summary>How many elements were looked at.</summary>
        public int Scanned { get; }

        /// <summary>How many of those a layer decides something about, and so were skipped.</summary>
        public int Claimed { get; }

        /// <summary>The distinct states found, largest first.</summary>
        public IReadOnlyList<ForeignGroup> Groups { get; }

        /// <summary>How many further states were found beyond the cap.</summary>
        public int OtherGroups { get; }

        /// <summary>How many elements are in those.</summary>
        public int OtherElements { get; }

        /// <summary>How many elements carry an appearance nothing accounts for.</summary>
        public int Total => Groups.Sum(g => g.Keys.Count) + OtherElements;

        /// <summary>How many are hidden.</summary>
        public int Hidden => Groups.Where(g => g.IsHidden).Sum(g => g.Keys.Count);

        /// <summary>True when the model looks exactly like the stack says it should.</summary>
        public bool IsClean => Total == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsClean)
                return Scanned.ToString("N0", CultureInfo.InvariantCulture)
                     + " elements scanned, none of them touched by anything but this stack";

            var s = Total.ToString("N0", CultureInfo.InvariantCulture) + " of "
                  + Scanned.ToString("N0", CultureInfo.InvariantCulture)
                  + " elements carry an appearance no layer accounts for, in "
                  + (Groups.Count + OtherGroups).ToString("N0", CultureInfo.InvariantCulture) + " distinct states";

            if (Hidden > 0)
                s += " · " + Hidden.ToString("N0", CultureInfo.InvariantCulture) + " hidden";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Finds appearance in the document that the layer stack did not put there.
    ///
    /// The rule is the planner's, deliberately: an element is somebody else's when it is not in its
    /// original state and no enabled layer <i>decides</i> anything about it. A layer that is
    /// switched off still covers its elements so its own report can show a count, and treating that
    /// as a claim would let disabling a layer quietly take ownership of an override it never had.
    /// </summary>
    public static class ForeignAppearance
    {
        /// <summary>How many distinct states are kept before the rest are counted only.</summary>
        public const int DefaultGroupCap = 100;

        /// <summary>Find it, over a reading of every element.</summary>
        /// <param name="states">What the document is in, element by element.</param>
        /// <param name="fold">What the stack decides, or null when there is no stack.</param>
        /// <param name="groupCap">How many distinct states to keep.</param>
        public static ForeignAudit Find(IEnumerable<AppearanceState> states, AppearanceFold? fold,
                                        int groupCap = DefaultGroupCap) =>
            Find(states, fold, null, groupCap);

        /// <summary>
        /// Find it, over a reading of only the elements that are in some state.
        ///
        /// Its own name rather than another overload of Find: a scanned count and a group cap are
        /// both a bare int in third place, so the two would be ambiguous at every call site that
        /// passed one positionally, and the name is also the thing a reader needs to know is
        /// different about this one.
        ///
        /// The overload above counts what it was given, which is right when it was given the whole
        /// model. A scan that walks a federation and hands back only the elements carrying
        /// something — which is the only way to walk a federation without the host freezing — knows
        /// how many it looked at but no longer hands them over, so it says.
        ///
        /// Getting this wrong is not a cosmetic matter: "41 of 41 elements carry an appearance no
        /// layer accounts for" invites somebody to conclude the whole model is overridden, when
        /// what happened is that 41 of 230,000 were.
        /// </summary>
        /// <param name="states">The elements that are in some state. Untouched ones may be absent.</param>
        /// <param name="fold">What the stack decides, or null when there is no stack.</param>
        /// <param name="scanned">
        /// How many elements were looked at, which is not how many were handed over. Below the
        /// number handed over it is ignored in favour of that, since a total smaller than its own
        /// part is the one answer that cannot be true.
        /// </param>
        /// <param name="groupCap">How many distinct states to keep.</param>
        public static ForeignAudit FindInScan(IEnumerable<AppearanceState> states, AppearanceFold? fold,
                                              int scanned, int groupCap = DefaultGroupCap) =>
            Find(states, fold, (int?)scanned, groupCap);

        private static ForeignAudit Find(IEnumerable<AppearanceState> states, AppearanceFold? fold,
                                         int? walked, int groupCap)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            if (groupCap < 1) throw new ArgumentOutOfRangeException(nameof(groupCap), "the cap must be at least one");

            var decided = new HashSet<ElementKey>();

            if (fold != null)
                foreach (var element in fold.Elements)
                    if (!element.IsUntouched) decided.Add(element.Key);

            var buckets = new Dictionary<string, List<ElementKey>>(StringComparer.Ordinal);
            var shapes = new Dictionary<string, AppearanceState>(StringComparer.Ordinal);
            var scanned = 0;
            var claimed = 0;

            foreach (var state in states)
            {
                if (state.Key.IsEmpty) continue;

                scanned++;

                if (state.IsPristine) continue;

                if (decided.Contains(state.Key)) { claimed++; continue; }

                var shape = Shape(state);

                if (!buckets.TryGetValue(shape, out var bucket))
                {
                    bucket = new List<ElementKey>();
                    buckets[shape] = bucket;
                    shapes[shape] = state;
                }

                bucket.Add(state.Key);
            }

            // Largest first, then by shape, so the same model twice gives the same order — a table
            // that reshuffles between two identical runs reads as the model having changed.
            var ordered = buckets
                .OrderByDescending(b => b.Value.Count)
                .ThenBy(b => b.Key, StringComparer.Ordinal)
                .ToList();

            var kept = ordered.Take(groupCap)
                .Select(b => new ForeignGroup(shapes[b.Key].IsHidden, shapes[b.Key].Colour,
                                              shapes[b.Key].Transparency, b.Value))
                .ToList();

            var tail = ordered.Skip(groupCap).ToList();

            // The walked figure when there is one, and never a total below the part of it that was
            // handed back: a caller that under-reported its walk would otherwise produce "41 of 12".
            var looked = walked == null ? scanned : Math.Max(walked.Value, scanned);

            return new ForeignAudit(looked, claimed, kept, tail.Count, tail.Sum(b => b.Value.Count));
        }

        /// <summary>
        /// A layer that reproduces one of these groups, so it stops being somebody else's.
        ///
        /// Adopting is the honest way to take responsibility for an override: it does not change
        /// what the model looks like, it changes who the stack says put it there. Visibility is
        /// only claimed when the elements are actually hidden — a layer that asserted "visible" on
        /// everything it adopted would start overruling the layers beneath it.
        /// </summary>
        /// <param name="group">The state to adopt.</param>
        /// <param name="id">The new layer's id.</param>
        /// <param name="name">The new layer's name.</param>
        public static AppearanceLayer Adopt(ForeignGroup group, string id, string? name = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));

            var target = LayerTarget.Elements(group.Keys, group.Describe());

            return new AppearanceLayer(id, name ?? "Adopted: " + group.Describe(), target)
            {
                Visible = group.IsHidden ? false : (bool?)null,
                Colour = group.Colour,
                Transparency = group.Transparency,
                Note = "Adopted from what the model was already in. Nothing moved when this was made.",
            };
        }

        private static string Shape(AppearanceState state) =>
            (state.IsHidden ? "h" : "-")
            + "|" + (state.Colour == null
                     ? "-"
                     : state.Colour.Value.Packed.ToString(CultureInfo.InvariantCulture))
            + "|" + (state.Transparency == null
                     ? "-"
                     : Quantise(state.Transparency.Value).ToString(CultureInfo.InvariantCulture));

        // The planner's resolution, for the same reason: a value written as 0.5 reads back as
        // 128/255, and comparing exactly would split one state into two rows that look identical.
        private static int Quantise(double transparency) =>
            (int)Math.Round(Math.Max(0, Math.Min(1, transparency)) * AppearancePlanner.TransparencySteps);
    }
}
