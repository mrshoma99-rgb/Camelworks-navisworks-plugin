using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Appearance
{
    /// <summary>
    /// One layer stack per scene, which is how a per-view graphics system gets onto a host that
    /// does not have one.
    ///
    /// Revit keeps visibility and graphics on the view: switch view and the graphics switch with
    /// it. Navisworks does not. Its overrides are document state — there is exactly one current
    /// appearance, shared by every view — and a saved viewpoint can capture and restore it but does
    /// not own it. That is why moving the camera never loses an override, and why two viewpoints
    /// cannot show different overrides at the same time.
    ///
    /// So the per-view behaviour is provided here rather than read from the host: a stack is stored
    /// against the scene it was built for, and going to that scene is what puts it on the model.
    /// The stack is the record of intent, which the host keeps none of.
    ///
    /// The default 3D view keeps the key the single stack always used, so a project written before
    /// this existed opens with its stack intact and as the default view's rather than as nobody's.
    /// </summary>
    public static class StackLibrary
    {
        /// <summary>
        /// The key the default 3D view's stack lives under.
        ///
        /// Unprefixed, and that is not tidiness: this is the key the one global stack was written
        /// to before stacks were per scene, so every project that already has one opens with it.
        /// </summary>
        public const string DefaultViewKey = "stack";

        private const string Prefix = "stack:";

        /// <summary>Which key a scene's stack lives under.</summary>
        /// <param name="viewId">A viewpoint id, or null for the default 3D view.</param>
        public static string KeyFor(string? viewId) =>
            string.IsNullOrEmpty(viewId) ? DefaultViewKey : Prefix + viewId;

        /// <summary>A scene's stack, bottom layer first, or empty when it has none.</summary>
        /// <param name="section">The project's layers section.</param>
        /// <param name="viewId">A viewpoint id, or null for the default 3D view.</param>
        public static List<AppearanceLayer> Read(JsonValue? section, string? viewId)
        {
            if (section == null) return new List<AppearanceLayer>();

            return LayerStackJson.Read(section[KeyFor(viewId)]).ToList();
        }

        /// <summary>
        /// Store a scene's stack, or remove it when the stack is empty.
        ///
        /// Removed rather than written as an empty array, so a viewpoint somebody built a stack for
        /// and then emptied stops being listed as having one. A list of scenes where half the
        /// entries hold nothing is a list nobody reads.
        /// </summary>
        /// <param name="section">The project's layers section.</param>
        /// <param name="viewId">A viewpoint id, or null for the default 3D view.</param>
        /// <param name="layers">The stack, bottom layer first.</param>
        public static void Write(JsonValue section, string? viewId, IReadOnlyList<AppearanceLayer> layers)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (layers == null) throw new ArgumentNullException(nameof(layers));

            var key = KeyFor(viewId);

            if (layers.Count == 0) section.Remove(key);
            else section.Set(key, LayerStackJson.Write(layers));
        }

        /// <summary>
        /// Which scenes have a stack: null for the default 3D view, then each viewpoint id.
        ///
        /// In the order the store holds them, except that the default view comes first when it has
        /// one, because it is the scene somebody is in when they have not chosen a viewpoint.
        /// </summary>
        /// <param name="section">The project's layers section.</param>
        public static IReadOnlyList<string?> Scenes(JsonValue? section)
        {
            var scenes = new List<string?>();

            if (section == null) return scenes;

            if (section.Has(DefaultViewKey)) scenes.Add(null);

            foreach (var key in section.Keys)
                if (key.StartsWith(Prefix, StringComparison.Ordinal) && key.Length > Prefix.Length)
                    scenes.Add(key.Substring(Prefix.Length));

            return scenes;
        }

        /// <summary>
        /// Copy a stack, so it can be put on another scene.
        ///
        /// Fresh ids, because an id names a layer within the stack it is in: copying a stack onto a
        /// scene that already holds a copy of it would otherwise put two layers with one id in the
        /// same stack, and the fold's explanations name the layer that won by its id. Two layers
        /// answering to one name is a report that cannot be read.
        ///
        /// Everything else travels, the order included, since the order is what decides which layer
        /// wins and a copy that reordered them would look like a different scheme.
        /// </summary>
        /// <param name="layers">The stack to copy, bottom layer first.</param>
        /// <param name="ticks">A number that makes the new ids distinct from any existing ones.</param>
        public static List<AppearanceLayer> Copy(IReadOnlyList<AppearanceLayer> layers, long ticks)
        {
            if (layers == null) throw new ArgumentNullException(nameof(layers));

            var copies = new List<AppearanceLayer>(layers.Count);

            for (var i = 0; i < layers.Count; i++)
            {
                var from = layers[i];

                var copy = new AppearanceLayer(
                    "copied-" + ticks.ToString(CultureInfo.InvariantCulture)
                    + "-" + i.ToString(CultureInfo.InvariantCulture),
                    from.Name,
                    from.Target)
                {
                    IsEnabled = from.IsEnabled,
                    Visible = from.Visible,
                    Colour = from.Colour,
                    Transparency = from.Transparency,
                    Note = from.Note,
                };

                copies.Add(copy);
            }

            return copies;
        }
    }
}
