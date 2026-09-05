using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// What happened to one element, as a set of facts rather than one label.
    ///
    /// Flags, because an element does more than one thing at a time. A duct that was moved up,
    /// resized and had its system name corrected is all three, and a comparison that has to pick one
    /// calls it "modified" and discards the other two.
    /// </summary>
    [Flags]
    public enum ChangeKind
    {
        /// <summary>Matched, and nothing about it differs.</summary>
        None = 0,

        /// <summary>In the current model and not in the baseline.</summary>
        Added = 1,

        /// <summary>In the baseline and not in the current model.</summary>
        Removed = 2,

        /// <summary>Its centre moved further than the tolerance.</summary>
        Moved = 4,

        /// <summary>Its extents changed, whichever way round they sit.</summary>
        Resized = 8,

        /// <summary>
        /// Its extents are the same three numbers in a different order.
        ///
        /// Only a bounding box is available, so this is a quarter turn about an axis and not an
        /// arbitrary rotation. It is worth a flag of its own because it is invisible to every other
        /// signal — same size, same centre, same properties — and a rotated fitting is a real
        /// problem on site.
        /// </summary>
        Rotated = 16,

        /// <summary>Its display name changed.</summary>
        Renamed = 32,

        /// <summary>Its category or its type changed.</summary>
        Retyped = 64,

        /// <summary>At least one watched property changed.</summary>
        PropertyChanged = 128,

        /// <summary>
        /// Its triangles changed while its bounding box did not.
        ///
        /// Only ever set when both sides carry a mesh hash — see
        /// <see cref="ElementFingerprint.MeshHash"/>. Absence of this flag is never a claim that
        /// the shape is unchanged, only that nothing looked.
        /// </summary>
        Remodelled = 256,
    }

    /// <summary>The five buckets a change list is grouped and coloured by.</summary>
    public enum ChangeBucket
    {
        /// <summary>Matched, nothing differs.</summary>
        Unchanged = 0,

        /// <summary>New in the current model.</summary>
        Added = 1,

        /// <summary>Gone from the current model.</summary>
        Removed = 2,

        /// <summary>The same element, somewhere else — and nothing more than that.</summary>
        Moved = 3,

        /// <summary>The same element, different in some way other than only where it sits.</summary>
        Changed = 4,
    }

    /// <summary>One property's before and after.</summary>
    public sealed class ValueChange
    {
        internal ValueChange(PropertyRef property, string? before, string? after)
        {
            Property = property;
            Before = before;
            After = after;
        }

        /// <summary>Which property.</summary>
        public PropertyRef Property { get; }

        /// <summary>What it held, or null when the element did not carry it.</summary>
        public string? Before { get; }

        /// <summary>What it holds now, or null when the element no longer carries it.</summary>
        public string? After { get; }

        /// <summary>True when the element did not carry this property before.</summary>
        public bool IsNew => Before == null;

        /// <summary>True when the element no longer carries it.</summary>
        public bool IsGone => After == null;

        /// <inheritdoc />
        public override string ToString() =>
            Property + ": " + Show(Before) + " → " + Show(After);

        private static string Show(string? value) =>
            value == null ? "(absent)" : value.Length == 0 ? "(empty)" : "\"" + value + "\"";
    }

    /// <summary>What a comparison found about one element.</summary>
    public sealed class ElementChange
    {
        internal ElementChange(ChangeKind kind, ElementFingerprint? before, ElementFingerprint? after,
                              string model, KeyRung? matchedOn, double distance,
                              IReadOnlyList<ValueChange> properties)
        {
            Kind = kind;
            Before = before;
            After = after;
            Model = model;
            MatchedOn = matchedOn;
            Distance = distance;
            Properties = properties;
        }

        /// <summary>Everything that is different about it.</summary>
        public ChangeKind Kind { get; }

        /// <summary>How it was in the baseline, or null when it is new.</summary>
        public ElementFingerprint? Before { get; }

        /// <summary>How it is now, or null when it is gone.</summary>
        public ElementFingerprint? After { get; }

        /// <summary>The model it belongs to, as the report groups by.</summary>
        public string Model { get; }

        /// <summary>
        /// Which rung the match rested on, or null when nothing matched it.
        ///
        /// Carried onto every row because a rung-3 match is a proposal, not a fact — two identical
        /// brackets are indistinguishable by category, type and size, and the honest thing is to
        /// say which rows rest on that and let a person weigh them.
        /// </summary>
        public KeyRung? MatchedOn { get; }

        /// <summary>How far its centre moved, in model units. Zero when it did not, or could not, move.</summary>
        public double Distance { get; }

        /// <summary>Every watched property that differs.</summary>
        public IReadOnlyList<ValueChange> Properties { get; }

        /// <summary>Whichever side of the comparison exists — the current one when both do.</summary>
        public ElementFingerprint Print => (After ?? Before)!;

        /// <summary>True when the element matched and nothing about it differs.</summary>
        public bool IsUnchanged => Kind == ChangeKind.None;

        /// <summary>True when the match rested on the weakest rung.</summary>
        public bool IsWeakMatch => MatchedOn == KeyRung.Geometry;

        /// <summary>Which of the five buckets this belongs in.</summary>
        public ChangeBucket Bucket
        {
            get
            {
                if ((Kind & ChangeKind.Added) != 0) return ChangeBucket.Added;
                if ((Kind & ChangeKind.Removed) != 0) return ChangeBucket.Removed;
                if (Kind == ChangeKind.None) return ChangeBucket.Unchanged;

                // Moved is its own bucket only when moving is ALL that happened. An element that
                // moved and was also resized belongs with the things that changed shape, because
                // that is the one a person has to look at.
                var positionOnly = (Kind & ~(ChangeKind.Moved | ChangeKind.Rotated)) == 0;

                return positionOnly ? ChangeBucket.Moved : ChangeBucket.Changed;
            }
        }

        /// <summary>How it reads on a change list — the element, then what happened to it.</summary>
        public string Title => Print.Label + " — " + Summary();

        /// <summary>Everything that changed, in the order a person reads it.</summary>
        public string Summary()
        {
            if (Kind == ChangeKind.None) return "unchanged";

            var parts = new List<string>();

            if ((Kind & ChangeKind.Added) != 0) parts.Add("new");
            if ((Kind & ChangeKind.Removed) != 0) parts.Add("gone");

            if ((Kind & ChangeKind.Moved) != 0)
                parts.Add("moved " + Distance.ToString("0.###", CultureInfo.InvariantCulture));

            if ((Kind & ChangeKind.Rotated) != 0) parts.Add("rotated");

            if ((Kind & ChangeKind.Resized) != 0 && Before != null && After != null)
                parts.Add("resized " + Before.ShowSize() + " → " + After.ShowSize());
            else if ((Kind & ChangeKind.Resized) != 0)
                parts.Add("resized");

            if ((Kind & ChangeKind.Remodelled) != 0) parts.Add("remodelled inside the same envelope");

            if ((Kind & ChangeKind.Retyped) != 0 && Before != null && After != null)
                parts.Add("type " + Blank(Before.Type) + " → " + Blank(After.Type));

            if ((Kind & ChangeKind.Renamed) != 0 && Before != null && After != null)
                parts.Add("renamed " + Blank(Before.Name) + " → " + Blank(After.Name));

            if ((Kind & ChangeKind.PropertyChanged) != 0)
                parts.Add(Properties.Count == 1
                    ? Properties[0].ToString()
                    : ComparePolicy.Count(Properties.Count) + " properties changed");

            return string.Join(", ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Title;

        private static string Blank(string? value) =>
            string.IsNullOrEmpty(value) ? "(none)" : value!;

        /// <summary>How a bucket reads on a heading.</summary>
        /// <param name="bucket">The bucket.</param>
        public static string Show(ChangeBucket bucket) => bucket switch
        {
            ChangeBucket.Added => "Added",
            ChangeBucket.Removed => "Removed",
            ChangeBucket.Moved => "Moved",
            ChangeBucket.Changed => "Changed",
            _ => "Unchanged",
        };

        /// <summary>
        /// Every flag there is, once.
        ///
        /// Held rather than asked for, because the alternative is <c>Enum.GetValues</c> inside the
        /// per-element loop — a fresh array for every element in the federation, to answer a
        /// question whose answer never changes.
        /// </summary>
        public static IReadOnlyList<ChangeKind> Flags { get; } = new[]
        {
            ChangeKind.Added,
            ChangeKind.Removed,
            ChangeKind.Moved,
            ChangeKind.Resized,
            ChangeKind.Rotated,
            ChangeKind.Renamed,
            ChangeKind.Retyped,
            ChangeKind.PropertyChanged,
            ChangeKind.Remodelled,
        };

        /// <summary>Every flag that is set, named.</summary>
        /// <param name="kind">The flags.</param>
        public static IReadOnlyList<string> Names(ChangeKind kind)
        {
            if (kind == ChangeKind.None) return new[] { "unchanged" };

            var names = new List<string>();

            foreach (var flag in Flags)
            {
                if ((kind & flag) == 0) continue;

                names.Add(flag switch
                {
                    ChangeKind.PropertyChanged => "property changed",
                    _ => flag.ToString().ToLowerInvariant(),
                });
            }

            return names;
        }
    }
}
