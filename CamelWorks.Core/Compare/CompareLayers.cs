using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// The five colours a comparison paints the model in.
    ///
    /// Chosen to survive the host's grey background and to stay apart from each other for the
    /// commonest colour blindness: the green and the red differ in lightness as well as hue, and
    /// the amber sits well clear of both. Not configurable in the first instance and deliberately
    /// so — a compare view that means something different on every machine is one nobody can
    /// screenshot into a meeting.
    /// </summary>
    public static class ComparePalette
    {
        /// <summary>New in the current model.</summary>
        public static Colour Added { get; } = new Colour(0x2E, 0x9E, 0x5B);

        /// <summary>Gone from the current model.</summary>
        public static Colour Removed { get; } = new Colour(0xD6, 0x45, 0x45);

        /// <summary>The same element, somewhere else.</summary>
        public static Colour Moved { get; } = new Colour(0xE0, 0xA0, 0x20);

        /// <summary>The same element, different.</summary>
        public static Colour Changed { get; } = new Colour(0x3B, 0x82, 0xC4);

        /// <summary>
        /// How see-through everything that did not change is.
        ///
        /// Ghosted rather than hidden. Hiding it gives four hundred coloured objects floating in
        /// space with nothing to locate them against, which looks decisive and is unreadable; at
        /// this transparency the building is still there to point at.
        /// </summary>
        public const double UnchangedTransparency = 0.85;

        /// <summary>The colour for a bucket, or null for the one that has none.</summary>
        /// <param name="bucket">Which bucket.</param>
        public static Colour? Of(ChangeBucket bucket) => bucket switch
        {
            ChangeBucket.Added => Added,
            ChangeBucket.Removed => Removed,
            ChangeBucket.Moved => Moved,
            ChangeBucket.Changed => Changed,
            _ => null,
        };
    }

    /// <summary>What building the compare view produced, and what it could not.</summary>
    public sealed class CompareView
    {
        internal CompareView(IReadOnlyList<AppearanceLayer> layers, IReadOnlyList<string> notes,
                             int coloured, int uncolourable)
        {
            Layers = layers;
            Notes = notes;
            Coloured = coloured;
            Uncolourable = uncolourable;
        }

        /// <summary>The layers, bottom of the stack first.</summary>
        public IReadOnlyList<AppearanceLayer> Layers { get; }

        /// <summary>What the view cannot show, in plain words.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>How many elements the view actually paints.</summary>
        public int Coloured { get; }

        /// <summary>
        /// How many changes have no element in this document to paint.
        ///
        /// Always the removed ones, unless the baseline model is also loaded. This is the single
        /// most confusing thing about any compare view — "it says 200 removed and I can see none
        /// of them" — so it is a number the screen can put next to the legend rather than
        /// something a person has to work out.
        /// </summary>
        public int Uncolourable { get; }

        /// <summary>True when there is nothing to paint.</summary>
        public bool IsEmpty => Coloured == 0;

        /// <inheritdoc />
        public override string ToString() =>
            ComparePolicy.Count(Coloured) + " elements painted across "
            + ComparePolicy.Count(Layers.Count) + " layers";
    }

    /// <summary>
    /// Turns a comparison into appearance layers.
    ///
    /// <b>Layers, not overrides.</b> The host's own way of showing a comparison is to write colour
    /// overrides straight onto the model, which cannot be undone selectively, cannot be explained
    /// afterwards, and is destroyed by the next person who presses Unhide All. Going through the
    /// appearance stack means the compare view is one row somebody can switch off, that says what
    /// it is doing and why, and that leaves whatever was underneath it exactly as it was.
    ///
    /// <b>Removed elements cannot be painted.</b> They are not in this document — that is what
    /// removed means. Navisworks holds one version of a model at a time, so the only way to see
    /// them in three dimensions is to append the baseline model alongside the current one, and the
    /// view says so rather than quietly leaving them out of the legend.
    /// </summary>
    public static class CompareLayers
    {
        /// <summary>The id prefix every layer this builds carries, so they can be found and removed.</summary>
        public const string IdPrefix = "compare.";

        /// <summary>Build the compare view from a result.</summary>
        /// <param name="result">What the comparison found.</param>
        /// <param name="ghostUnchanged">Whether to fade everything that did not change.</param>
        public static CompareView Build(CompareResult? result, bool ghostUnchanged = true)
        {
            var layers = new List<AppearanceLayer>();
            var notes = new List<string>();

            if (result == null || result.IsEmpty)
                return new CompareView(layers, new[] { "Nothing was compared, so there is nothing to show." }, 0, 0);

            // Bottom of the stack, so every coloured layer above it wins on the elements it names.
            // Ghosting is applied to EVERYTHING rather than to the unchanged elements alone: a
            // layer that named only the unchanged ones would have to be rebuilt every time the
            // comparison is re-run, and would leave anything the comparison skipped fully opaque
            // and looking deliberate.
            if (ghostUnchanged)
            {
                layers.Add(new AppearanceLayer(IdPrefix + "ghost", "Compare — everything else",
                                               LayerTarget.Everything())
                {
                    Transparency = ComparePalette.UnchangedTransparency,
                    Note = "Faded so the changes stand out. Switch this layer off to see the model "
                           + "solid again.",
                });
            }

            var coloured = 0;

            foreach (var bucket in new[] { ChangeBucket.Moved, ChangeBucket.Changed, ChangeBucket.Added })
            {
                var keys = result.KeysIn(bucket);
                if (keys.Count == 0) continue;

                layers.Add(Layer(bucket, keys, result));
                coloured += keys.Count;
            }

            var removed = result.KeysIn(ChangeBucket.Removed);

            if (removed.Count > 0)
            {
                // Added to the stack even though it will paint nothing today. The moment somebody
                // appends the baseline model beside the current one — which is exactly how you look
                // at a deletion in three dimensions — this layer starts working with no further
                // action, and the appearance panel shows it as covering nothing until then rather
                // than the compare view silently not having one.
                layers.Add(Layer(ChangeBucket.Removed, removed, result));

                notes.Add(ComparePolicy.Count(removed.Count) + " removed elements are not in this "
                          + "document, so nothing here can paint them. Append the baseline model "
                          + "beside the current one and this layer will show them in red.");
            }

            if (coloured == 0 && removed.Count == 0)
                notes.Add("Nothing changed, so the compare view has nothing to paint.");

            return new CompareView(layers, notes, coloured, removed.Count);
        }

        /// <summary>The id a bucket's layer carries.</summary>
        /// <param name="bucket">Which bucket.</param>
        public static string IdOf(ChangeBucket bucket) =>
            IdPrefix + bucket.ToString().ToLowerInvariant();

        /// <summary>True when a layer is one of these, and so is a compare view rather than somebody's work.</summary>
        /// <param name="layer">The layer.</param>
        public static bool IsCompareLayer(AppearanceLayer? layer) =>
            layer != null && layer.Id.StartsWith(IdPrefix, StringComparison.Ordinal);

        private static AppearanceLayer Layer(ChangeBucket bucket, IReadOnlyList<ElementKey> keys,
                                             CompareResult result)
        {
            var name = "Compare — " + ElementChange.Show(bucket).ToLowerInvariant();

            return new AppearanceLayer(IdOf(bucket), name,
                                       LayerTarget.Elements(keys, ComparePolicy.Count(keys.Count) + " "
                                                            + ElementChange.Show(bucket).ToLowerInvariant()))
            {
                Colour = ComparePalette.Of(bucket),
                Note = Why(bucket) + " " + result.Between() + ".",
            };
        }

        private static string Why(ChangeBucket bucket) => bucket switch
        {
            ChangeBucket.Added => "Not in the model at",
            ChangeBucket.Removed => "In the model at, and no longer:",
            ChangeBucket.Moved => "Moved since",
            ChangeBucket.Changed => "Changed since",
            _ => "Unchanged since",
        };

        /// <summary>
        /// The legend, so a screenshot of the model means something to whoever it is sent to.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        public static IReadOnlyList<(string Label, Colour? Colour, int Count)> Legend(CompareResult? result)
        {
            var rows = new List<(string, Colour?, int)>();
            if (result == null) return rows;

            foreach (var bucket in new[]
                     {
                         ChangeBucket.Added, ChangeBucket.Removed,
                         ChangeBucket.Moved, ChangeBucket.Changed, ChangeBucket.Unchanged,
                     })
            {
                var count = result.CountIn(bucket);
                if (count == 0 && bucket != ChangeBucket.Unchanged) continue;

                rows.Add((ElementChange.Show(bucket), ComparePalette.Of(bucket), count));
            }

            return rows;
        }
    }
}
