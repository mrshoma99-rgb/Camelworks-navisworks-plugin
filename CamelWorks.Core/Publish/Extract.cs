using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Publish
{
    /// <summary>What decides whether an element is in an extract.</summary>
    public enum ExtractAxis
    {
        /// <summary>Whatever is selected in the model right now.</summary>
        Selection = 0,

        /// <summary>Whatever a saved viewpoint shows.</summary>
        Viewpoint,

        /// <summary>Whatever a saved set holds.</summary>
        Set,

        /// <summary>Everything sitting on one level.</summary>
        Level,

        /// <summary>Everything carrying one value of one property - a zone, a room, a package.</summary>
        Where,

        /// <summary>Everything inside a box - a section, an area, a floor plate.</summary>
        Box,

        /// <summary>Everything from one model of the federation.</summary>
        Model,
    }

    /// <summary>How much of an element has to be inside a box for it to count.</summary>
    public enum BoxReach
    {
        /// <summary>Any overlap at all.</summary>
        Touching = 0,

        /// <summary>The whole element.</summary>
        Inside,

        /// <summary>Its centre.</summary>
        Centre,
    }

    /// <summary>A box in world space, said in metres.</summary>
    public sealed class ExtractBox
    {
        /// <summary>Make a box. The corners are sorted, so either order works.</summary>
        /// <param name="minX">One corner, X.</param>
        /// <param name="minY">One corner, Y.</param>
        /// <param name="minZ">One corner, Z.</param>
        /// <param name="maxX">The other corner, X.</param>
        /// <param name="maxY">The other corner, Y.</param>
        /// <param name="maxZ">The other corner, Z.</param>
        public ExtractBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = Math.Min(minX, maxX); MaxX = Math.Max(minX, maxX);
            MinY = Math.Min(minY, maxY); MaxY = Math.Max(minY, maxY);
            MinZ = Math.Min(minZ, maxZ); MaxZ = Math.Max(minZ, maxZ);
        }

        /// <summary>Minimum corner, X.</summary>
        public double MinX { get; }

        /// <summary>Minimum corner, Y.</summary>
        public double MinY { get; }

        /// <summary>Minimum corner, Z.</summary>
        public double MinZ { get; }

        /// <summary>Maximum corner, X.</summary>
        public double MaxX { get; }

        /// <summary>Maximum corner, Y.</summary>
        public double MaxY { get; }

        /// <summary>Maximum corner, Z.</summary>
        public double MaxZ { get; }

        /// <summary>True when the box has no size in some direction, so nothing can be in it.</summary>
        public bool IsFlat => MaxX <= MinX || MaxY <= MinY || MaxZ <= MinZ;

        /// <summary>Whether an element's box counts as in this one.</summary>
        /// <param name="bounds">The element's box.</param>
        /// <param name="reach">How much of it has to be in.</param>
        public bool Holds(BoundingBox bounds, BoxReach reach)
        {
            switch (reach)
            {
                case BoxReach.Inside:
                    return bounds.MinX >= MinX && bounds.MaxX <= MaxX
                        && bounds.MinY >= MinY && bounds.MaxY <= MaxY
                        && bounds.MinZ >= MinZ && bounds.MaxZ <= MaxZ;

                case BoxReach.Centre:
                    return Between((bounds.MinX + bounds.MaxX) / 2, MinX, MaxX)
                        && Between((bounds.MinY + bounds.MaxY) / 2, MinY, MaxY)
                        && Between((bounds.MinZ + bounds.MaxZ) / 2, MinZ, MaxZ);

                default:
                    return bounds.MinX <= MaxX && bounds.MaxX >= MinX
                        && bounds.MinY <= MaxY && bounds.MaxY >= MinY
                        && bounds.MinZ <= MaxZ && bounds.MaxZ >= MinZ;
            }
        }

        /// <summary>The box everything in a list sits inside, or null when nothing has geometry.</summary>
        /// <param name="elements">The elements.</param>
        /// <param name="margin">How far out to push each face, in metres.</param>
        public static ExtractBox? Around(IEnumerable<ReportElement>? elements, double margin = 0)
        {
            var any = false;
            double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;

            foreach (var element in elements ?? Enumerable.Empty<ReportElement>())
            {
                if (element == null || !element.HasGeometry) continue;

                var box = element.Bounds;

                if (!any)
                {
                    minX = box.MinX; minY = box.MinY; minZ = box.MinZ;
                    maxX = box.MaxX; maxY = box.MaxY; maxZ = box.MaxZ;
                    any = true;
                    continue;
                }

                minX = Math.Min(minX, box.MinX); maxX = Math.Max(maxX, box.MaxX);
                minY = Math.Min(minY, box.MinY); maxY = Math.Max(maxY, box.MaxY);
                minZ = Math.Min(minZ, box.MinZ); maxZ = Math.Max(maxZ, box.MaxZ);
            }

            if (!any) return null;

            var pad = Math.Max(0, margin);

            return new ExtractBox(minX - pad, minY - pad, minZ - pad,
                                  maxX + pad, maxY + pad, maxZ + pad);
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("minX", MinX).Set("minY", MinY).Set("minZ", MinZ)
                .Set("maxX", MaxX).Set("maxY", MaxY).Set("maxZ", MaxZ);

        /// <summary>Read one back, or null when there is nothing readable there.</summary>
        /// <param name="json">What was written.</param>
        public static ExtractBox? FromJson(JsonValue? json)
        {
            if (json == null || !json.Has("minX")) return null;

            return new ExtractBox(json["minX"].AsDouble(), json["minY"].AsDouble(), json["minZ"].AsDouble(),
                                  json["maxX"].AsDouble(), json["maxY"].AsDouble(), json["maxZ"].AsDouble());
        }

        /// <inheritdoc />
        public override string ToString() =>
            Size(MaxX - MinX) + " x " + Size(MaxY - MinY) + " x " + Size(MaxZ - MinZ) + " m";

        private static bool Between(double value, double low, double high) =>
            value >= low && value <= high;

        private static string Size(double metres) =>
            metres.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>What one extract is, saved so the next revision is one press.</summary>
    public sealed class ExtractRecipe
    {
        /// <summary>How many elements one extract will take before it stops and says so.</summary>
        public const int DefaultCeiling = 250000;

        /// <summary>Start an extract.</summary>
        /// <param name="name">What to call it.</param>
        public ExtractRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Extract" : name!.Trim();
        }

        /// <summary>What to call it, and what the file gets named after.</summary>
        public string Name { get; set; }

        /// <summary>What decides whether an element is in.</summary>
        public ExtractAxis Axis { get; set; }

        /// <summary>Which viewpoint, set, level or model, depending on the axis.</summary>
        public string? Named { get; set; }

        /// <summary>Which property, when the axis is a property value.</summary>
        public PropertyRef Property { get; set; }

        /// <summary>Which value of it.</summary>
        public string? Value { get; set; }

        /// <summary>The box, when the axis is a box.</summary>
        public ExtractBox? Box { get; set; }

        /// <summary>How much of an element has to be in the box.</summary>
        public BoxReach Reach { get; set; } = BoxReach.Touching;

        /// <summary>
        /// Whether grouping nodes are dropped.
        ///
        /// On by default. A tree node carries no geometry and no quantities; taking it into a
        /// deliverable inflates every count on the cover sheet and puts nothing in the file.
        /// </summary>
        public bool GeometryOnly { get; set; } = true;

        /// <summary>How many elements before it stops.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public ExtractRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("name", Name)
                .Set("axis", Axis.ToString())
                .Set("reach", Reach.ToString())
                .Set("geometryOnly", GeometryOnly)
                .Set("ceiling", Ceiling);

            if (Named != null) json.Set("named", Named);
            if (Value != null) json.Set("value", Value);
            if (Box != null) json.Set("box", Box.ToJson());

            if (Property.Name.Length > 0)
            {
                json.Set("property", Property.Name);
                if (Property.Category.Length > 0) json.Set("category", Property.Category);
            }

            return json;
        }

        /// <summary>Read one back. An unreadable one comes back as a plain selection extract.</summary>
        /// <param name="json">What was written.</param>
        public static ExtractRecipe FromJson(JsonValue? json)
        {
            var recipe = new ExtractRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            if (Enum.TryParse<ExtractAxis>(json["axis"].AsString() ?? "Selection", out var axis))
                recipe.Axis = axis;

            if (Enum.TryParse<BoxReach>(json["reach"].AsString() ?? "Touching", out var reach))
                recipe.Reach = reach;

            recipe.Named = json["named"].AsString();
            recipe.Value = json["value"].AsString();
            recipe.Box = ExtractBox.FromJson(json["box"]);
            recipe.GeometryOnly = json["geometryOnly"].AsBool(true);

            recipe.Property = new PropertyRef((json["category"].AsString() ?? string.Empty).Trim(),
                                              (json["property"].AsString() ?? string.Empty).Trim());

            var ceiling = (int)json["ceiling"].AsLong();
            recipe.Ceiling = ceiling > 0 ? ceiling : DefaultCeiling;

            return recipe;
        }

        /// <summary>What it takes, said in a phrase.</summary>
        public string Describe() => Axis switch
        {
            ExtractAxis.Viewpoint => "what the viewpoint \"" + (Named ?? "?") + "\" shows",
            ExtractAxis.Set => "what the set \"" + (Named ?? "?") + "\" holds",
            ExtractAxis.Level => "everything on " + (Named ?? "one level"),
            ExtractAxis.Where => "everything whose " + (Property.Name.Length == 0 ? "property" : Property.Name)
                                 + " is \"" + (Value ?? string.Empty) + "\"",
            ExtractAxis.Box => "everything " + Word(Reach) + " a box of " + (Box?.ToString() ?? "no size"),
            ExtractAxis.Model => "everything from " + (Named ?? "one model"),
            _ => "what is selected",
        };

        /// <inheritdoc />
        public override string ToString() => Name + " — " + Describe();

        private static string Word(BoxReach reach) => reach switch
        {
            BoxReach.Inside => "wholly inside",
            BoxReach.Centre => "centred in",
            _ => "touching",
        };
    }

    /// <summary>How much of an extract came from one model.</summary>
    public sealed class ExtractShare
    {
        internal ExtractShare(string model, int count)
        {
            Model = model;
            Count = count;
        }

        /// <summary>The model.</summary>
        public string Model { get; }

        /// <summary>How many of its elements are in.</summary>
        public int Count { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Model + " × " + Count.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>What an extract would contain.</summary>
    public sealed class ExtractPlan
    {
        internal ExtractPlan(ExtractRecipe recipe, IReadOnlyList<ElementKey> keys, int scope,
                             int dropped, int withoutGeometry, ExtractBox? bounds,
                             IReadOnlyList<ExtractShare> models, IReadOnlyList<string> notes,
                             string? refusal)
        {
            Recipe = recipe;
            Keys = keys;
            Scope = scope;
            Dropped = dropped;
            WithoutGeometry = withoutGeometry;
            Bounds = bounds;
            Models = models;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What was asked for.</summary>
        public ExtractRecipe Recipe { get; }

        /// <summary>What is in.</summary>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>How many elements were looked at.</summary>
        public int Scope { get; }

        /// <summary>How many matched but did not fit under the ceiling.</summary>
        public int Dropped { get; }

        /// <summary>How many matched but carry no geometry, and so were left out.</summary>
        public int WithoutGeometry { get; }

        /// <summary>The box everything in it sits inside, or null when nothing has geometry.</summary>
        public ExtractBox? Bounds { get; }

        /// <summary>Which models it came from, biggest first.</summary>
        public IReadOnlyList<ExtractShare> Models { get; }

        /// <summary>Anything worth saying about how it was worked out.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is nothing to extract, when there is nothing.</summary>
        public string? Refusal { get; }

        /// <summary>How many are in.</summary>
        public int Count => Keys.Count;

        /// <summary>True when there is something to write.</summary>
        public bool IsUsable => Refusal == null && Keys.Count > 0;

        /// <summary>What it holds, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            if (Keys.Count == 0)
                return "Nothing matched " + Recipe.Describe() + ". "
                     + Scope.ToString("N0", CultureInfo.InvariantCulture)
                     + " elements were looked at.";

            var text = Keys.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Keys.Count == 1 ? " element" : " elements") + " out of "
                     + Scope.ToString("N0", CultureInfo.InvariantCulture) + " — "
                     + Recipe.Describe() + ".";

            if (Models.Count > 1)
                text += " From " + string.Join(", ", Models.Take(4).Select(m => m.ToString()))
                     + (Models.Count > 4 ? " and others." : ".");

            if (WithoutGeometry > 0)
                text += " " + WithoutGeometry.ToString("N0", CultureInfo.InvariantCulture)
                     + (WithoutGeometry == 1 ? " matching node carries" : " matching nodes carry")
                     + " no geometry and " + (WithoutGeometry == 1 ? "was" : "were") + " left out.";

            if (Dropped > 0)
                text += " " + Dropped.ToString("N0", CultureInfo.InvariantCulture)
                     + " more matched but did not fit under the ceiling.";

            if (Bounds != null) text += " It measures " + Bounds + ".";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// A piece of the federation, chosen by something other than clicking on it.
    ///
    /// This is what every deliverable that is not the whole model asks for: this level, this zone,
    /// this area, what that viewpoint shows, that supplier's model. Doing it by hand is a selection
    /// made by eye that cannot be reproduced next revision, so it is remade every month and is
    /// subtly different each time.
    ///
    /// The axis is saved rather than the answer. An extract named "Level 03 mechanical" re-runs
    /// against whatever is loaded today and takes whatever is on Level 03 today.
    ///
    /// It does not write a sliced NWD. The Navisworks .NET API has no call that writes a subset of a
    /// document to NWD: a plug-in can save the document it has, whole, and nothing narrower. What is
    /// here is the reusable part — working out what belongs in the piece, reporting it, and writing
    /// it to the formats CamelWorks does write (IFC, glTF, DXF, CSV and XLSX) — plus making the
    /// selection in the host so Navisworks' own export can take it from there.
    /// </summary>
    public static class Extract
    {
        /// <summary>Where saved extracts live in the project store.</summary>
        public const string StoreKey = "extracts";

        /// <summary>
        /// Work out what is in.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">What to take.</param>
        /// <param name="named">
        /// The keys the named thing holds, for the axes that are a name: a viewpoint's elements, a
        /// set's members, or the current selection. Resolved by the caller, because what a
        /// viewpoint shows is a question only the host can answer.
        /// </param>
        /// <param name="levels">The level bands, when the axis is a level.</param>
        public static ExtractPlan Plan(IEnumerable<ReportElement>? elements, ExtractRecipe? recipe,
                                       IReadOnlyCollection<ElementKey>? named = null,
                                       LevelSet? levels = null)
        {
            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();
            var wanted = recipe ?? new ExtractRecipe();
            var notes = new List<string>();

            var refusal = Refuses(scope, wanted, named, levels);

            if (refusal != null) return Nothing(wanted, scope.Count, notes, refusal);

            var reachable = named == null
                ? null
                : new HashSet<ElementKey>(named);

            var keys = new List<ElementKey>();
            var taken = new List<ReportElement>();
            var ceiling = wanted.Ceiling > 0 ? wanted.Ceiling : ExtractRecipe.DefaultCeiling;
            var dropped = 0;
            var bodiless = 0;

            foreach (var element in scope)
            {
                if (!Wants(element, wanted, reachable, levels)) continue;

                if (wanted.GeometryOnly && !element.HasGeometry) { bodiless++; continue; }

                if (keys.Count >= ceiling) { dropped++; continue; }

                keys.Add(element.Key);
                taken.Add(element);
            }

            var models = taken
                .GroupBy(e => e.Model ?? "(no model)", StringComparer.OrdinalIgnoreCase)
                .Select(g => new ExtractShare(g.Key, g.Count()))
                .OrderByDescending(m => m.Count)
                .ThenBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (wanted.Axis == ExtractAxis.Box && wanted.Reach == BoxReach.Touching && keys.Count > 0)
                notes.Add("An element counts as in when any part of it is in the box, so things that "
                          + "run through it - a duct, a beam - come whole rather than cut.");

            return new ExtractPlan(wanted, keys, scope.Count, dropped, bodiless,
                                   ExtractBox.Around(taken), models, notes, null);
        }

        /// <summary>
        /// The values one property takes across the scope, so a screen can offer them as a list.
        ///
        /// The axis that reads "everything whose Zone is Plant Room 2" is only usable if the tool
        /// knows the model says Plant Room 2 - typing it and getting nothing back is the failure
        /// this exists to prevent.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="property">Which property.</param>
        public static IReadOnlyList<string> Values(IEnumerable<ReportElement>? elements, PropertyRef property)
        {
            if (property.Name.Length == 0) return Array.Empty<string>();

            return (elements ?? Enumerable.Empty<ReportElement>())
                .Where(e => e != null)
                .Select(e => e.Value(property))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Every saved extract, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<ExtractRecipe> Saved(JsonValue? json)
        {
            var saved = new List<ExtractRecipe>();

            if (json == null) return saved;

            foreach (var item in json.Items) saved.Add(ExtractRecipe.FromJson(item));

            return saved;
        }

        /// <summary>The list with one extract put in it, replacing any of the same name.</summary>
        /// <param name="saved">What is saved now.</param>
        /// <param name="recipe">The one to keep.</param>
        public static JsonValue Keep(IEnumerable<ExtractRecipe>? saved, ExtractRecipe? recipe)
        {
            var kept = (saved ?? Enumerable.Empty<ExtractRecipe>()).ToList();

            if (recipe != null)
            {
                kept.RemoveAll(e => string.Equals(e.Name, recipe.Name, StringComparison.OrdinalIgnoreCase));
                kept.Add(recipe);
            }

            return JsonValue.Array(kept.Select(e => e.ToJson()));
        }

        /// <summary>
        /// A file name for the extract, safe on every platform anybody will open it on.
        /// </summary>
        /// <param name="recipe">The extract.</param>
        /// <param name="extension">The extension, with its dot.</param>
        public static string FileName(ExtractRecipe? recipe, string extension)
        {
            var name = (recipe?.Name ?? "Extract").Trim();
            var safe = new char[name.Length];
            var at = 0;

            foreach (var letter in name)
                safe[at++] = Bad.IndexOf(letter) >= 0 ? '-' : letter;

            var cleaned = new string(safe, 0, at).Trim('-', ' ', '.');

            return (cleaned.Length == 0 ? "Extract" : cleaned) + extension;
        }

        private const string Bad = "\\/:*?\"<>|\r\n\t";

        private static bool Wants(ReportElement element, ExtractRecipe recipe,
                                  HashSet<ElementKey>? reachable, LevelSet? levels)
        {
            switch (recipe.Axis)
            {
                case ExtractAxis.Selection:
                case ExtractAxis.Viewpoint:
                case ExtractAxis.Set:
                    return reachable != null && reachable.Contains(element.Key);

                case ExtractAxis.Model:
                    return string.Equals(element.Model, recipe.Named, StringComparison.OrdinalIgnoreCase);

                case ExtractAxis.Where:
                    var held = element.Value(recipe.Property);

                    return string.IsNullOrWhiteSpace(recipe.Value)
                        ? !string.IsNullOrWhiteSpace(held)
                        : string.Equals((held ?? string.Empty).Trim(), recipe.Value!.Trim(),
                                        StringComparison.OrdinalIgnoreCase);

                case ExtractAxis.Level:
                    if (levels == null || !element.HasGeometry) return false;

                    // Placed by its base rather than its centre, which is what LevelOf does: a
                    // column that runs floor to floor belongs to the level it stands on, not to
                    // the one its middle happens to fall in.
                    return string.Equals(levels.LevelOf(element.Bounds.MinZ, element.Bounds.MaxZ),
                                         recipe.Named, StringComparison.OrdinalIgnoreCase);

                case ExtractAxis.Box:
                    return recipe.Box != null && element.HasGeometry
                        && recipe.Box.Holds(element.Bounds, recipe.Reach);

                default:
                    return false;
            }
        }

        private static string? Refuses(IReadOnlyList<ReportElement> scope, ExtractRecipe recipe,
                                       IReadOnlyCollection<ElementKey>? named, LevelSet? levels)
        {
            if (scope.Count == 0)
                return "There are no elements in scope, so there is nothing to take a piece of. "
                     + "Append a model first.";

            switch (recipe.Axis)
            {
                case ExtractAxis.Selection:
                    if (named == null || named.Count == 0)
                        return "Nothing is selected, so an extract of the selection would be empty. "
                             + "Select something in the model, or pick another way of choosing.";
                    break;

                case ExtractAxis.Viewpoint:
                case ExtractAxis.Set:
                    if (string.IsNullOrWhiteSpace(recipe.Named))
                        return "Pick which " + (recipe.Axis == ExtractAxis.Set ? "set" : "viewpoint")
                             + " the extract follows.";

                    if (named == null || named.Count == 0)
                        return "\"" + recipe.Named + "\" holds nothing in this model, so the extract "
                             + "would be empty. It may have been made against a different revision.";
                    break;

                case ExtractAxis.Level:
                    if (levels == null || levels.Bands.Count == 0)
                        return "There are no levels to extract by. Work the levels out on the Data "
                             + "screen first, or pick another way of choosing.";

                    if (string.IsNullOrWhiteSpace(recipe.Named))
                        return "Pick which level the extract takes.";
                    break;

                case ExtractAxis.Where:
                    if (recipe.Property.Name.Length == 0)
                        return "Pick the property the extract follows - a zone, a room, a package, "
                             + "a system.";
                    break;

                case ExtractAxis.Box:
                    if (recipe.Box == null)
                        return "There is no box yet. Take one from what is selected, or type the "
                             + "corners in.";

                    if (recipe.Box.IsFlat)
                        return "That box has no thickness in one direction, so nothing can be "
                             + "inside it.";
                    break;

                case ExtractAxis.Model:
                    if (string.IsNullOrWhiteSpace(recipe.Named))
                        return "Pick which model the extract takes.";
                    break;
            }

            return null;
        }

        private static ExtractPlan Nothing(ExtractRecipe recipe, int scope, IReadOnlyList<string> notes,
                                           string refusal) =>
            new ExtractPlan(recipe, Array.Empty<ElementKey>(), scope, 0, 0, null,
                            Array.Empty<ExtractShare>(), notes, refusal);
    }
}
