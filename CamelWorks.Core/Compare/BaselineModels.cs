using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Compare
{
    /// <summary>Whether a baseline model can be put back in front of somebody, and if not, why not.</summary>
    public enum BaselineStanding
    {
        /// <summary>The file is there and is not this revision. Appending it shows what was removed.</summary>
        CanBring = 0,

        /// <summary>It is already loaded — brought in earlier, or never taken out.</summary>
        AlreadyIn = 1,

        /// <summary>
        /// The baseline named the same file the current model is loaded from.
        ///
        /// Nothing can be done about this one and it is worth saying plainly: the old revision was
        /// overwritten in place, so the bytes that held those elements are gone. Issuing each
        /// revision into its own folder is what makes the difference, and it is the reason to.
        /// </summary>
        Overwritten = 2,

        /// <summary>The path is no longer on disk, or the snapshot never recorded one.</summary>
        Gone = 3,
    }

    /// <summary>One baseline model, and what can be done with it.</summary>
    public sealed class BaselineModel
    {
        internal BaselineModel(ModelRecord record, string? path, BaselineStanding standing, string reason)
        {
            Record = record;
            Path = path;
            Standing = standing;
            Reason = reason;
        }

        /// <summary>What the baseline recorded about it.</summary>
        public ModelRecord Record { get; }

        /// <summary>The file to append, when there is one.</summary>
        public string? Path { get; }

        /// <summary>Whether it can be brought in.</summary>
        public BaselineStanding Standing { get; }

        /// <summary>Why, in words.</summary>
        public string Reason { get; }

        /// <summary>How it reads on a line.</summary>
        public string Title => Record.DisplayName;

        /// <inheritdoc />
        public override string ToString() => Title + " — " + Reason;
    }

    /// <summary>
    /// Bringing the old revision back into the document, so a deletion can be looked at.
    ///
    /// A removed element can be shown once the old file is open. It is in last month's model in last
    /// month's issue folder, and Navisworks appends that file beside the current one. The snapshot
    /// already records the path of every model it was taken from, so nothing has to be found or
    /// asked for.
    ///
    /// What makes this work rather than merely load two models: the host's element index is
    /// first-wins in document order and appended models land at the end. Every key that exists in
    /// the current model still resolves to the current element, so the added, moved and changed
    /// layers keep pointing where they did, and the only keys the appended revision claims are the
    /// ones nothing else has — the removed set.
    ///
    /// It cannot work where a model was issued by overwriting one file in place, because there is no
    /// old revision left to open. That is worth reporting rather than leaving to be discovered.
    /// </summary>
    public static class BaselineModels
    {
        /// <summary>
        /// Work out, for each model in a baseline, whether it can be brought back into the document.
        /// </summary>
        /// <param name="baseline">The baseline snapshot.</param>
        /// <param name="loaded">The models the document currently holds.</param>
        /// <param name="fs">Used to ask whether a path is still on disk. Null skips the check.</param>
        public static IReadOnlyList<BaselineModel> Of(ModelSnapshot? baseline,
                                                      IReadOnlyList<IModelSource>? loaded,
                                                      IFileSystem? fs = null)
        {
            var models = new List<BaselineModel>();
            if (baseline == null) return models;

            var here = loaded ?? Array.Empty<IModelSource>();

            foreach (var record in baseline.Models)
            {
                // The loaded path first: it names a revision, where the source path names a CAD
                // file that has been every revision in turn.
                var path = !string.IsNullOrWhiteSpace(record.LoadedPath)
                    ? record.LoadedPath
                    : (string.IsNullOrWhiteSpace(record.SourcePath) ? null : record.SourcePath);

                if (path == null)
                {
                    models.Add(new BaselineModel(record, null, BaselineStanding.Gone,
                        "the baseline did not record a file for it"));
                    continue;
                }

                var openFromThisFile = here
                    .Where(m => string.Equals(Normalise(m.LoadedPath ?? m.SourcePath),
                                              Normalise(path), StringComparison.Ordinal))
                    .ToList();

                if (openFromThisFile.Count > 0)
                {
                    // The file is open. Two very different situations look identical at this point,
                    // and telling them apart is what the rest of this method is for:
                    //
                    //   brought in already — this revision is open ALONGSIDE another revision of
                    //                        the same thing, which is what a bring-in produces;
                    //   overwritten        — it is the only copy, because the current revision was
                    //                        issued by writing over this very file.
                    //
                    // Told apart by the authoring path, not by the element scope: a scope is a hash
                    // of a path and a baseline taken on another machine can carry a scope this
                    // document has never produced, whereas two revisions of one model agree on the
                    // CAD file they came from because that is what makes them two revisions.
                    var another = record.SourcePath.Length > 0
                                  && here.Any(m => !openFromThisFile.Contains(m)
                                                   && string.Equals(Normalise(m.SourcePath),
                                                                    Normalise(record.SourcePath),
                                                                    StringComparison.Ordinal));

                    models.Add(another
                        ? new BaselineModel(record, path, BaselineStanding.AlreadyIn,
                            "it is open beside the current revision")
                        : new BaselineModel(record, path, BaselineStanding.Overwritten,
                            "the current model is loaded from this same file, so the older revision "
                            + "was overwritten in place and there is nothing left to open"));
                    continue;
                }

                if (fs != null && !fs.Exists(path))
                {
                    models.Add(new BaselineModel(record, path, BaselineStanding.Gone,
                        "that file is not there any more: " + path));
                    continue;
                }

                models.Add(new BaselineModel(record, path, BaselineStanding.CanBring,
                    "append " + path + " and its removed elements can be painted"));
            }

            return models;
        }

        /// <summary>The files to append, in a stable order.</summary>
        /// <param name="models">What <see cref="Of"/> worked out.</param>
        public static IReadOnlyList<string> ToBring(IReadOnlyList<BaselineModel>? models) =>
            (models ?? Array.Empty<BaselineModel>())
                .Where(m => m.Standing == BaselineStanding.CanBring && m.Path != null)
                .Select(m => m.Path!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>The files that are in and could be taken back out.</summary>
        /// <param name="models">What <see cref="Of"/> worked out.</param>
        public static IReadOnlyList<string> ToSendBack(IReadOnlyList<BaselineModel>? models) =>
            (models ?? Array.Empty<BaselineModel>())
                .Where(m => m.Standing == BaselineStanding.AlreadyIn && m.Path != null)
                .Select(m => m.Path!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>The one-line readout for the screen.</summary>
        /// <param name="models">What <see cref="Of"/> worked out.</param>
        public static string Explain(IReadOnlyList<BaselineModel>? models)
        {
            var all = models ?? Array.Empty<BaselineModel>();
            if (all.Count == 0) return "the baseline named no models";

            var parts = new List<string>();

            var can = all.Count(m => m.Standing == BaselineStanding.CanBring);
            var inAlready = all.Count(m => m.Standing == BaselineStanding.AlreadyIn);
            var overwritten = all.Count(m => m.Standing == BaselineStanding.Overwritten);
            var gone = all.Count(m => m.Standing == BaselineStanding.Gone);

            if (can > 0) parts.Add(ComparePolicy.Count(can) + " can be brought in");
            if (inAlready > 0) parts.Add(ComparePolicy.Count(inAlready) + " already in");
            if (overwritten > 0) parts.Add(ComparePolicy.Count(overwritten) + " overwritten in place");
            if (gone > 0) parts.Add(ComparePolicy.Count(gone) + " no longer on disk");

            return string.Join(", ", parts);
        }

        /// <summary>
        /// True when a comparison run right now would be comparing the baseline against itself.
        ///
        /// Worth a banner on the Run tab rather than a surprise in the results: with the old
        /// revision appended, the next snapshot contains both revisions, the pairing matches the
        /// baseline model to itself, and the whole thing reads as "nothing changed".
        /// </summary>
        /// <param name="models">What <see cref="Of"/> worked out.</param>
        public static bool IsHoldingBoth(IReadOnlyList<BaselineModel>? models) =>
            (models ?? Array.Empty<BaselineModel>()).Any(m => m.Standing == BaselineStanding.AlreadyIn);

        private static string Normalise(string? path) =>
            string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path!.Trim().Replace('\\', '/').ToUpperInvariant();
    }
}
