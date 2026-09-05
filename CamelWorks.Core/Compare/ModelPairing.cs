using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Compare
{
    /// <summary>How much a pairing of two models can be relied on. Lower is stronger.</summary>
    public enum PairStrength
    {
        /// <summary>The same file, by path. Nothing to decide.</summary>
        SamePath = 0,

        /// <summary>The same file name in a different folder — the ordinary issue-folder pattern.</summary>
        SameName = 1,

        /// <summary>The same name once a revision marker is taken off the end. See <see cref="ModelPairing.Stem"/>.</summary>
        SameStem = 2,

        /// <summary>The same display name, whatever the file is called.</summary>
        SameDisplayName = 3,

        /// <summary>One model on each side and nothing else to confuse it with.</summary>
        OnlyCandidate = 4,

        /// <summary>Nothing paired it. The model is wholly new, or wholly gone.</summary>
        None = 5,
    }

    /// <summary>
    /// Two models held to be two revisions of one thing — or one model held to have no counterpart.
    /// </summary>
    public sealed class ModelPair
    {
        internal ModelPair(ModelRecord? before, ModelRecord? after, PairStrength strength, string reason)
        {
            Before = before;
            After = after;
            Strength = strength;
            Reason = reason;
        }

        /// <summary>The model on the baseline side, or null when this one is new.</summary>
        public ModelRecord? Before { get; }

        /// <summary>The model on the current side, or null when this one is gone.</summary>
        public ModelRecord? After { get; }

        /// <summary>How much the pairing can be relied on.</summary>
        public PairStrength Strength { get; }

        /// <summary>Why these two were put together, in words a person can disagree with.</summary>
        public string Reason { get; }

        /// <summary>True when both sides are present.</summary>
        public bool IsMatched => Before != null && After != null;

        /// <summary>True when the model only exists on the current side.</summary>
        public bool IsNewModel => Before == null && After != null;

        /// <summary>True when the model only existed on the baseline side.</summary>
        public bool IsGoneModel => Before != null && After == null;

        /// <summary>How it reads on a line.</summary>
        public string Title =>
            IsMatched
                ? (string.Equals(Before!.DisplayName, After!.DisplayName, StringComparison.OrdinalIgnoreCase)
                    ? Before.DisplayName
                    : Before.DisplayName + " → " + After!.DisplayName)
                : (Before ?? After)!.DisplayName;

        /// <inheritdoc />
        public override string ToString() => Title + " — " + Reason;
    }

    /// <summary>
    /// Which model on the baseline side is which model on the current side.
    ///
    /// Comparison depends on this step. An <see cref="Identity.ElementKey"/> is scoped to its
    /// model's source path, so an element in <c>STR-R03.nwc</c> and the same element in
    /// <c>STR-R04.nwc</c> have keys that can never be equal. Matching them means dropping the scope,
    /// and a bare unscoped value would as readily match an element in the architectural model to one
    /// in the structural model that sits at the same place in its tree.
    ///
    /// So the scope is dropped only inside a pair, and a pair is only made for a stated reason.
    /// Every pairing carries the reason it was made and how far it can be relied on, and a reason
    /// the user disagrees with can be overridden.
    ///
    /// Paired in strength order rather than greedily model by model, so a certain pairing is never
    /// taken by a weaker one considered first.
    /// </summary>
    public sealed class ModelPairing
    {
        private ModelPairing(IReadOnlyList<ModelPair> pairs, IReadOnlyList<string> ambiguities)
        {
            Pairs = pairs;
            Ambiguities = ambiguities;
        }

        /// <summary>Every pairing, matched ones first, then new models, then gone ones.</summary>
        public IReadOnlyList<ModelPair> Pairs { get; }

        /// <summary>
        /// Pairings that were NOT made because more than one candidate fit equally well.
        ///
        /// Reported rather than guessed. Picking one of two equally good candidates produces a
        /// comparison that is confidently wrong about half a federation, and the symptom — a whole
        /// discipline reported as replaced — looks like a modelling problem rather than a matching
        /// one, which is the worst possible place for it to look like.
        /// </summary>
        public IReadOnlyList<string> Ambiguities { get; }

        /// <summary>The pairings with a model on both sides.</summary>
        public IReadOnlyList<ModelPair> Matched => Pairs.Where(p => p.IsMatched).ToList();

        /// <summary>Models that only exist on the current side.</summary>
        public IReadOnlyList<ModelPair> NewModels => Pairs.Where(p => p.IsNewModel).ToList();

        /// <summary>Models that only existed on the baseline side.</summary>
        public IReadOnlyList<ModelPair> GoneModels => Pairs.Where(p => p.IsGoneModel).ToList();

        /// <summary>True when every model on both sides found a counterpart.</summary>
        public bool IsComplete => Pairs.All(p => p.IsMatched);

        /// <summary>The current-side scope for a baseline scope, or null when it has no counterpart.</summary>
        /// <param name="beforeScope">The baseline model's scope hash.</param>
        public string? After(string? beforeScope)
        {
            foreach (var pair in Pairs)
                if (pair.IsMatched && string.Equals(pair.Before!.Scope, beforeScope, StringComparison.Ordinal))
                    return pair.After!.Scope;

            return null;
        }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var parts = new List<string>();
            var matched = Matched.Count;
            var gone = GoneModels.Count;
            var added = NewModels.Count;

            parts.Add(matched == 1 ? "1 model paired" : ComparePolicy.Count(matched) + " models paired");
            if (added > 0) parts.Add(ComparePolicy.Count(added) + " only in the current model");
            if (gone > 0) parts.Add(ComparePolicy.Count(gone) + " only in the baseline");
            if (Ambiguities.Count > 0) parts.Add(ComparePolicy.Count(Ambiguities.Count) + " needing a decision");

            return string.Join(" · ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        /// <summary>Work out the pairing between two sets of models.</summary>
        /// <param name="before">The baseline side's models.</param>
        /// <param name="after">The current side's models.</param>
        public static ModelPairing Of(IReadOnlyList<ModelRecord>? before, IReadOnlyList<ModelRecord>? after)
        {
            var left = (before ?? Array.Empty<ModelRecord>()).ToList();
            var right = (after ?? Array.Empty<ModelRecord>()).ToList();

            var pairs = new List<ModelPair>();
            var ambiguities = new List<string>();
            var takenLeft = new HashSet<int>();
            var takenRight = new HashSet<int>();

            Round(left, right, takenLeft, takenRight, pairs, ambiguities,
                  PairStrength.SamePath, r => Normalise(r.SourcePath),
                  "the same file");

            Round(left, right, takenLeft, takenRight, pairs, ambiguities,
                  PairStrength.SameName, r => FileName(r.SourcePath),
                  "the same file name in a different folder");

            Round(left, right, takenLeft, takenRight, pairs, ambiguities,
                  PairStrength.SameStem, r => Stem(FileName(r.SourcePath)),
                  "the same name once the revision marker is taken off");

            Round(left, right, takenLeft, takenRight, pairs, ambiguities,
                  PairStrength.SameDisplayName, r => r.DisplayName.Trim().ToUpperInvariant(),
                  "the same name in the model tree");

            // The last resort, and deliberately the narrowest one there is: ONE model on each
            // side in total, neither of which paired by name.
            //
            // The tempting version of this rule is "one left unpaired on each side", and it is
            // wrong. On a job with five models where the structural and the services model were
            // both renamed, it pairs structure against services with total confidence and reports
            // both disciplines as entirely rebuilt — a comparison that is not merely unhelpful but
            // actively misleading, and whose symptom looks like a modelling problem rather than a
            // matching one. Two models left over means two answers and no way to choose, so both
            // are reported unpaired and somebody is asked.
            if (left.Count == 1 && right.Count == 1 && takenLeft.Count == 0 && takenRight.Count == 0)
            {
                pairs.Add(new ModelPair(left[0], right[0], PairStrength.OnlyCandidate,
                                        "the only model on either side, though the names do not match"));
                takenLeft.Add(0);
                takenRight.Add(0);
            }

            for (var i = 0; i < right.Count; i++)
                if (!takenRight.Contains(i))
                    pairs.Add(new ModelPair(null, right[i], PairStrength.None, "no model in the baseline matches it"));

            for (var i = 0; i < left.Count; i++)
                if (!takenLeft.Contains(i))
                    pairs.Add(new ModelPair(left[i], null, PairStrength.None, "no model in the current document matches it"));

            return new ModelPairing(pairs, ambiguities);
        }

        /// <summary>
        /// One pass: pair the models whose key is shared by exactly one model on each side.
        ///
        /// Symmetric on purpose. Walking the baseline side and taking the first candidate would
        /// pair two baseline models competing for one current model by whichever was listed first,
        /// which is not a decision — it is an ordering. A key with two on either side pairs nothing
        /// and is reported.
        /// </summary>
        private static void Round(IReadOnlyList<ModelRecord> left, IReadOnlyList<ModelRecord> right,
                                  HashSet<int> takenLeft, HashSet<int> takenRight,
                                  List<ModelPair> pairs, List<string> ambiguities,
                                  PairStrength strength, Func<ModelRecord, string> keyOf, string reason)
        {
            var leftBy = Group(left, takenLeft, keyOf);
            var rightBy = Group(right, takenRight, keyOf);

            foreach (var entry in leftBy.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (!rightBy.TryGetValue(entry.Key, out var theirs)) continue;

                var mine = entry.Value;

                if (mine.Count > 1 || theirs.Count > 1)
                {
                    ambiguities.Add(
                        Join(left, mine) + " and " + Join(right, theirs)
                        + " share " + reason + ", so nothing here can say which is which");
                    continue;
                }

                pairs.Add(new ModelPair(left[mine[0]], right[theirs[0]], strength, reason));
                takenLeft.Add(mine[0]);
                takenRight.Add(theirs[0]);
            }
        }

        private static Dictionary<string, List<int>> Group(IReadOnlyList<ModelRecord> records,
                                                           HashSet<int> taken,
                                                           Func<ModelRecord, string> keyOf)
        {
            var by = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < records.Count; i++)
            {
                if (taken.Contains(i)) continue;

                var key = keyOf(records[i]);
                if (key.Length == 0) continue;

                if (!by.TryGetValue(key, out var list)) by[key] = list = new List<int>();
                list.Add(i);
            }

            return by;
        }

        private static string Join(IReadOnlyList<ModelRecord> records, IReadOnlyList<int> indices) =>
            string.Join(", ", indices.Select(i => records[i].DisplayName));

        // Applied to the END of a name, over and over until nothing more comes off, so that
        // ABC-XYZ-ZZ-XX-M3-S-0001-S3-P04 reduces past both its suitability code and its revision.
        private static readonly Regex[] Markers =
        {
            // rev 3, -R04, _v2, .Rev12a
            new Regex(@"[\s_\-\.]*(?:rev(?:ision)?|ver(?:sion)?|[rv])[\s_\-\.]*\d{1,3}[a-z]?$",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

            // ISO 19650 suitability and revision: -S3, -P04, -C01, -A2, -D1, -T02
            new Regex(@"[\s_\-]+[psctda]\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

            // 2024-01-15, 20240115, 240115
            new Regex(@"[\s_\-]*\d{4}[\-_]?\d{2}[\-_]?\d{2}$", RegexOptions.CultureInvariant),

            // Windows' own duplicate suffixes
            new Regex(@"\s*\(\d{1,3}\)$", RegexOptions.CultureInvariant),
            new Regex(@"[\s_\-]*copy$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        };

        /// <summary>
        /// A file name with any trailing revision marker removed.
        ///
        /// <c>STR-MODEL-R04</c>, <c>STR-MODEL_rev4</c> and <c>STR-MODEL-S3-P04</c> all reduce to
        /// <c>STR-MODEL</c>, which is how a pairing survives the re-issue that renames every file on
        /// the job.
        ///
        /// It is allowed to over-strip. <c>BLOCK-A1</c> and <c>BLOCK-A2</c> both reduce to
        /// <c>BLOCK</c>, and those are different models. That is safe only because a stem shared by
        /// more than one candidate pairs nothing and is reported as needing a decision. An
        /// over-eager rule that cannot act alone costs a question; a cautious one that misses the
        /// rename costs the feature.
        /// </summary>
        /// <param name="fileName">A file name, with or without its extension.</param>
        public static string Stem(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

            var name = fileName!.Trim();

            var dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);

            for (var round = 0; round < 4; round++)
            {
                var before = name;

                foreach (var marker in Markers)
                    name = marker.Replace(name, string.Empty);

                name = name.TrimEnd(' ', '-', '_', '.');

                if (string.Equals(name, before, StringComparison.Ordinal)) break;
                if (name.Length == 0) return before.ToUpperInvariant();
            }

            return name.ToUpperInvariant();
        }

        /// <summary>The file name out of a path, in either separator.</summary>
        /// <param name="path">The path.</param>
        public static string FileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            var s = path!.Trim();
            var at = s.LastIndexOfAny(new[] { '\\', '/' });

            return (at < 0 ? s : s.Substring(at + 1)).ToUpperInvariant();
        }

        private static string Normalise(string? path) =>
            string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path!.Trim().Replace('\\', '/').ToUpperInvariant();

        /// <summary>The strength as a report prints it.</summary>
        /// <param name="strength">The strength.</param>
        public static string Show(PairStrength strength) => strength switch
        {
            PairStrength.SamePath => "certain",
            PairStrength.SameName => "strong",
            PairStrength.SameStem => "strong",
            PairStrength.SameDisplayName => "likely",
            PairStrength.OnlyCandidate => "assumed",
            _ => "unpaired",
        };

        /// <summary>A count as this screen prints one.</summary>
        /// <param name="value">The count.</param>
        public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
