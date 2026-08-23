using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>What happened inside one model, or one pair of models.</summary>
    public sealed class ModelTally
    {
        internal ModelTally(string title, PairStrength strength, string reason)
        {
            Title = title;
            Strength = strength;
            Reason = reason;
        }

        /// <summary>How the model, or the pair, reads on a line.</summary>
        public string Title { get; }

        /// <summary>How much the pairing behind these numbers can be relied on.</summary>
        public PairStrength Strength { get; }

        /// <summary>Why the two models were put together.</summary>
        public string Reason { get; }

        /// <summary>How many elements are new.</summary>
        public int Added { get; internal set; }

        /// <summary>How many are gone.</summary>
        public int Removed { get; internal set; }

        /// <summary>How many only moved.</summary>
        public int Moved { get; internal set; }

        /// <summary>How many changed in some other way.</summary>
        public int Changed { get; internal set; }

        /// <summary>How many matched with nothing different about them.</summary>
        public int Unchanged { get; internal set; }

        /// <summary>How many elements were looked at on either side.</summary>
        public int Total => Added + Removed + Moved + Changed + Unchanged;

        /// <summary>How many are not the same as they were.</summary>
        public int Differences => Added + Removed + Moved + Changed;

        /// <summary>True when this model is exactly as it was.</summary>
        public bool IsQuiet => Differences == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Total == 0) return Title + " — nothing to compare";
            if (IsQuiet) return Title + " — unchanged (" + ComparePolicy.Count(Unchanged) + " elements)";

            var parts = new List<string>();

            if (Added > 0) parts.Add(ComparePolicy.Count(Added) + " added");
            if (Removed > 0) parts.Add(ComparePolicy.Count(Removed) + " removed");
            if (Moved > 0) parts.Add(ComparePolicy.Count(Moved) + " moved");
            if (Changed > 0) parts.Add(ComparePolicy.Count(Changed) + " changed");

            return Title + " — " + string.Join(", ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// What a comparison found, and what it could not answer.
    ///
    /// <b>The second half of that sentence is the point.</b> Every comparison rests on a matching,
    /// and a matching that quietly fails looks exactly like a model that was quietly rebuilt: a
    /// whole discipline reported as removed and re-added. So the counts come with the pairing that
    /// produced them, the rung each match rested on, and a plain list of the questions this
    /// particular baseline is not able to answer.
    /// </summary>
    public sealed class CompareResult
    {
        internal CompareResult(CompareOptions options, ModelPairing pairing,
                               IReadOnlyList<ElementChange> changes,
                               IReadOnlyList<ModelTally> models,
                               IReadOnlyList<string> notes)
        {
            Options = options;
            Pairing = pairing;
            Changes = changes;
            Models = models;
            Notes = notes;
        }

        /// <summary>The settings it ran under.</summary>
        public CompareOptions Options { get; }

        /// <summary>Which model was held to be which.</summary>
        public ModelPairing Pairing { get; }

        /// <summary>
        /// The changes, capped at <see cref="CompareOptions.ExampleCap"/> per bucket.
        ///
        /// Capped rather than complete because a screen showing four hundred thousand rows is a
        /// screen nobody scrolls. <see cref="Dropped"/> says what was left out, so a short list is
        /// never mistaken for a quiet week.
        /// </summary>
        public IReadOnlyList<ElementChange> Changes { get; }

        /// <summary>One tally per model or pair of models.</summary>
        public IReadOnlyList<ModelTally> Models { get; }

        /// <summary>
        /// What this comparison cannot answer, in plain words.
        ///
        /// Not warnings about failures — statements about reach. "The baseline recorded no
        /// properties, so property changes cannot be reported" is not an error; it is the single
        /// most useful line in the report for somebody about to ask why nothing was flagged.
        /// </summary>
        public IReadOnlyList<string> Notes { get; internal set; }

        /// <summary>How it reads on screen — a date, a milestone, an issue number.</summary>
        public string BaselineLabel { get; internal set; } = string.Empty;

        /// <summary>When the baseline was taken.</summary>
        public DateTimeOffset? BaselineTakenUtc { get; internal set; }

        /// <summary>How the current side reads on screen.</summary>
        public string CurrentLabel { get; internal set; } = string.Empty;

        /// <summary>When the current side was taken.</summary>
        public DateTimeOffset? CurrentTakenUtc { get; internal set; }

        /// <summary>How many elements are new.</summary>
        public int Added { get; internal set; }

        /// <summary>How many are gone.</summary>
        public int Removed { get; internal set; }

        /// <summary>How many only moved.</summary>
        public int Moved { get; internal set; }

        /// <summary>How many changed in some other way.</summary>
        public int Changed { get; internal set; }

        /// <summary>How many matched with nothing different about them.</summary>
        public int Unchanged { get; internal set; }

        /// <summary>
        /// How many elements took no part — grouping nodes, or identities weaker than allowed.
        ///
        /// Counted rather than dropped. An element nobody compared is not an element that did not
        /// change, and a comparison that says "2,113 elements were not compared" is one somebody
        /// can act on.
        /// </summary>
        public int Skipped { get; internal set; }

        /// <summary>How many rows were left out of <see cref="Changes"/> by the cap, per bucket.</summary>
        public IReadOnlyDictionary<ChangeBucket, int> Dropped => _dropped;

        internal readonly Dictionary<ChangeBucket, int> _dropped = new Dictionary<ChangeBucket, int>();

        /// <summary>How many matches rested on each rung.</summary>
        public IReadOnlyDictionary<KeyRung, int> MatchesByRung => _byRung;

        internal readonly Dictionary<KeyRung, int> _byRung = new Dictionary<KeyRung, int>();

        /// <summary>How many times each individual flag was set.</summary>
        public IReadOnlyDictionary<ChangeKind, int> ByKind => _byKind;

        internal readonly Dictionary<ChangeKind, int> _byKind = new Dictionary<ChangeKind, int>();

        /// <summary>How many elements were looked at.</summary>
        public int Total => Added + Removed + Moved + Changed + Unchanged;

        /// <summary>How many are not the same as they were.</summary>
        public int Differences => Added + Removed + Moved + Changed;

        /// <summary>True when nothing differs.</summary>
        public bool IsQuiet => Differences == 0;

        /// <summary>True when nothing was compared at all.</summary>
        public bool IsEmpty => Total == 0;

        /// <summary>
        /// How many matches rested on the weakest rung.
        ///
        /// Worth a number of its own on the front of the report. A comparison whose matches are
        /// mostly rung 3 is comparing shapes, not elements, and the difference matters the moment
        /// somebody acts on a row.
        /// </summary>
        public int WeakMatches => _byRung.TryGetValue(KeyRung.Geometry, out var n) ? n : 0;

        /// <summary>How many rows the cap kept out altogether.</summary>
        public int TotalDropped => _dropped.Values.Sum();

        /// <summary>
        /// Every element in one bucket, uncapped.
        ///
        /// <b>Not derivable from <see cref="Changes"/>, and that is the whole reason it exists.</b>
        /// The change list is capped so a screen stays usable; colouring the model from that capped
        /// list would leave every element past the five-hundredth uncoloured and looking unchanged
        /// — a comparison that is visibly, confidently wrong about most of the model.
        ///
        /// For <see cref="ChangeBucket.Removed"/> these are baseline keys, which do not resolve in
        /// the current document. See <see cref="CompareLayers"/> for what that means on screen.
        /// </summary>
        /// <param name="bucket">Which bucket.</param>
        public IReadOnlyList<ElementKey> KeysIn(ChangeBucket bucket) =>
            _keys.TryGetValue(bucket, out var keys) ? keys : (IReadOnlyList<ElementKey>)Array.Empty<ElementKey>();

        internal readonly Dictionary<ChangeBucket, List<ElementKey>> _keys =
            new Dictionary<ChangeBucket, List<ElementKey>>();

        /// <summary>The changes in one bucket.</summary>
        /// <param name="bucket">Which bucket.</param>
        public IReadOnlyList<ElementChange> In(ChangeBucket bucket) =>
            Changes.Where(c => c.Bucket == bucket).ToList();

        /// <summary>How many are in one bucket, counting the ones the cap kept out.</summary>
        /// <param name="bucket">Which bucket.</param>
        public int CountIn(ChangeBucket bucket) => bucket switch
        {
            ChangeBucket.Added => Added,
            ChangeBucket.Removed => Removed,
            ChangeBucket.Moved => Moved,
            ChangeBucket.Changed => Changed,
            _ => Unchanged,
        };

        /// <summary>The headline, as the top of a screen or a report says it.</summary>
        public string Explain()
        {
            if (IsEmpty) return "nothing was compared";
            if (IsQuiet) return "no differences across " + ComparePolicy.Count(Unchanged) + " elements";

            var parts = new List<string>();

            if (Added > 0) parts.Add(ComparePolicy.Count(Added) + " added");
            if (Removed > 0) parts.Add(ComparePolicy.Count(Removed) + " removed");
            if (Moved > 0) parts.Add(ComparePolicy.Count(Moved) + " moved");
            if (Changed > 0) parts.Add(ComparePolicy.Count(Changed) + " changed");

            return string.Join(" · ", parts) + " · " + ComparePolicy.Count(Unchanged) + " unchanged";
        }

        /// <summary>The two sides, named, for the top of a report.</summary>
        public string Between()
        {
            var from = BaselineLabel.Length > 0
                ? BaselineLabel
                : When(BaselineTakenUtc) ?? "the baseline";

            var to = CurrentLabel.Length > 0
                ? CurrentLabel
                : When(CurrentTakenUtc) ?? "the current model";

            return from + " → " + to;
        }

        /// <inheritdoc />
        public override string ToString() => Between() + ": " + Explain();

        private static string? When(DateTimeOffset? at) =>
            at.HasValue
                ? at.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                : null;
    }
}
