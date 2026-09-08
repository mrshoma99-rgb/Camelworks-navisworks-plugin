using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Checks
{
    /// <summary>Where a source's elements come from.</summary>
    public enum CheckOrigin
    {
        /// <summary>Nothing is named, so the source reads nothing.</summary>
        Nothing = 0,

        /// <summary>Saved selection and search sets, by name.</summary>
        Sets = 1,

        /// <summary>Whole loaded models, by the name the host shows.</summary>
        Models = 2,

        /// <summary>What was selected in the host when the source was picked.</summary>
        Selection = 3,

        /// <summary>Everything in every loaded model.</summary>
        Everything = 4,

        /// <summary>Everything the other side of the same run did not already take.</summary>
        TheRest = 5,
    }

    /// <summary>
    /// What one side of a check reads: several saved sets, several whole models, or a selection.
    ///
    /// A clearance run's surface side and a fall-hazard run's three sides each used to be a single
    /// string - one set name, or one of a few reserved words. A discipline is rarely one set, so a
    /// run over structural floors on three levels could not be set up at all. This holds what the
    /// clash source picker already produces.
    ///
    /// It writes back into the same settings key. A source that names exactly one set is written as
    /// that set's name, which is what the old builds wrote, so an existing project file reads back
    /// unchanged and a project saved here still opens in a build that predates this type as long as
    /// the source is one set. Anything the old shape cannot express is written with the
    /// <see cref="Marker"/> prefix, which <see cref="Read"/> recognises and the old parse does not.
    /// </summary>
    public sealed class CheckSource
    {
        /// <summary>What a written source that is not a bare set name starts with.</summary>
        public const string Marker = "source:";

        /// <summary>What a side is set to for the host's current selection.</summary>
        public const string TheSelection = "Current selection";

        /// <summary>What a side is set to for everything loaded.</summary>
        public const string Everywhere = "Everything loaded";

        /// <summary>What a clearance run's obstruction side is set to for everything but the surfaces.</summary>
        public const string Remainder = "Everything except the surfaces";

        /// <summary>What a side is set to when it names nothing at all.</summary>
        public const string Nowhere = "None";

        private static readonly char[] Bar = { '|' };

        /// <summary>Where this source's elements come from.</summary>
        public CheckOrigin Origin { get; set; } = CheckOrigin.Nothing;

        /// <summary>
        /// The sets or the models this source names, by the name the host shows.
        ///
        /// Empty for every other origin. A model is matched on its display name and on its scope
        /// hash, so a source written by an older build that stored scopes still resolves.
        /// </summary>
        public IList<string> Names { get; } = new List<string>();

        /// <summary>
        /// The elements, when the source is a selection that was read at pick time.
        ///
        /// Empty on a source that says "current selection" without listing them, which is what an
        /// older build wrote: that one is re-read from the host every time the check runs.
        /// </summary>
        public IList<ElementKey> Keys { get; } = new List<ElementKey>();

        /// <summary>True when this source would read nothing.</summary>
        public bool IsEmpty =>
            Origin == CheckOrigin.Nothing
            || ((Origin == CheckOrigin.Sets || Origin == CheckOrigin.Models) && Names.Count == 0);

        /// <summary>True when reading this source means walking every loaded model.</summary>
        public bool NeedsEverything =>
            Origin == CheckOrigin.Everything || Origin == CheckOrigin.TheRest;

        /// <summary>A source that names everything loaded.</summary>
        public static CheckSource Everything() =>
            new CheckSource { Origin = CheckOrigin.Everything };

        /// <summary>A source that names everything the other side did not take.</summary>
        public static CheckSource Rest() =>
            new CheckSource { Origin = CheckOrigin.TheRest };

        /// <summary>A source that names nothing.</summary>
        public static CheckSource None() => new CheckSource();

        /// <summary>A source over saved sets.</summary>
        /// <param name="names">The set names.</param>
        public static CheckSource Sets(params string[] names) =>
            Named(CheckOrigin.Sets, names);

        /// <summary>A source over whole loaded models.</summary>
        /// <param name="names">The model names, as the host shows them.</param>
        public static CheckSource Models(params string[] names) =>
            Named(CheckOrigin.Models, names);

        /// <summary>A source over an explicit list of elements.</summary>
        /// <param name="keys">The elements.</param>
        public static CheckSource Selection(IEnumerable<ElementKey> keys)
        {
            var source = new CheckSource { Origin = CheckOrigin.Selection };

            if (keys != null)
                foreach (var key in keys)
                    if (!key.IsEmpty) source.Keys.Add(key);

            return source;
        }

        private static CheckSource Named(CheckOrigin origin, IEnumerable<string>? names)
        {
            var source = new CheckSource { Origin = origin };

            if (names != null)
                foreach (var name in names)
                    if (!string.IsNullOrWhiteSpace(name)) source.Names.Add(name.Trim());

            return source;
        }

        /// <summary>
        /// Read a written source, in either shape.
        /// </summary>
        /// <param name="written">What the settings bag holds, or null.</param>
        /// <param name="fallback">What an absent key means, or null for nothing.</param>
        public static CheckSource Read(string? written, CheckSource? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(written)) return fallback ?? None();

            var text = written!.Trim();

            if (text.StartsWith(Marker, StringComparison.Ordinal)) return Parsed(text);

            if (Same(text, Everywhere)) return Everything();
            if (Same(text, Remainder)) return Rest();
            if (Same(text, Nowhere)) return None();
            if (Same(text, TheSelection)) return new CheckSource { Origin = CheckOrigin.Selection };

            return Sets(text);
        }

        private static CheckSource Parsed(string text)
        {
            var parts = text.Substring(Marker.Length)
                            .Split(Bar, StringSplitOptions.None);

            var origin = parts.Length == 0 ? string.Empty : parts[0].Trim();
            var rest = parts.Skip(1).Select(Plain).Where(one => one.Length > 0);

            if (Same(origin, "sets")) return Named(CheckOrigin.Sets, rest);
            if (Same(origin, "models")) return Named(CheckOrigin.Models, rest);
            if (Same(origin, "everything")) return Everything();
            if (Same(origin, "rest")) return Rest();

            if (Same(origin, "selection"))
            {
                var source = new CheckSource { Origin = CheckOrigin.Selection };

                foreach (var one in rest)
                    if (ElementKey.TryParse(one, out var key)) source.Keys.Add(key);

                return source;
            }

            return None();
        }

        /// <summary>
        /// This source as one settings value.
        ///
        /// A single set is written as its bare name, so the common case leaves a project file
        /// looking exactly as it did before this type existed.
        /// </summary>
        public string Written()
        {
            switch (Origin)
            {
                case CheckOrigin.Everything: return Everywhere;
                case CheckOrigin.TheRest: return Remainder;

                case CheckOrigin.Selection:
                    return Keys.Count == 0
                        ? TheSelection
                        : Joined("selection", Keys.Select(key => key.ToString()));

                case CheckOrigin.Sets:
                    if (Names.Count == 0) return Nowhere;
                    if (Names.Count == 1 && !Reserved(Names[0])) return Names[0];
                    return Joined("sets", Names);

                case CheckOrigin.Models:
                    return Names.Count == 0 ? Nowhere : Joined("models", Names);

                default: return Nowhere;
            }
        }

        /// <summary>
        /// Every element this source names, or nothing when it names nothing.
        ///
        /// Sets are walked one at a time and deduplicated by key, because two sets in one source
        /// routinely overlap and an element read twice is measured twice. Whole models are read by
        /// walking the document and keeping what belongs to them: there is no per-model traversal
        /// at the seam, and the alternative is a host-side change this cannot verify.
        /// </summary>
        /// <param name="document">The document to walk.</param>
        public IEnumerable<IModelItem> Items(IModelDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            switch (Origin)
            {
                case CheckOrigin.Everything:
                case CheckOrigin.TheRest:
                    return document.Traverse(TraversalScope.WholeDocument);

                case CheckOrigin.Models:
                    return Names.Count == 0
                        ? Enumerable.Empty<IModelItem>()
                        : document.Traverse(TraversalScope.WholeDocument).Where(Holds);

                case CheckOrigin.Selection:
                    return document.Traverse(Keys.Count == 0
                        ? TraversalScope.CurrentSelection
                        : TraversalScope.FromKeys(Keys.ToList()));

                case CheckOrigin.Sets:
                    return Names.Count == 0
                        ? Enumerable.Empty<IModelItem>()
                        : Once(document);

                default: return Enumerable.Empty<IModelItem>();
            }
        }

        /// <summary>Each named set in turn, with an element that is in two of them read once.</summary>
        /// <param name="document">The document to walk.</param>
        private IEnumerable<IModelItem> Once(IModelDocument document)
        {
            var seen = new HashSet<ElementKey>();

            foreach (var name in Names)
                foreach (var item in document.Traverse(TraversalScope.Set(name)))
                    if (item.Key.IsEmpty || seen.Add(item.Key))
                        yield return item;
        }

        /// <summary>Whether an element belongs to one of the models this source names.</summary>
        /// <param name="item">The element.</param>
        private bool Holds(IModelItem item)
        {
            var model = item?.Model;

            if (model == null) return false;

            return Names.Any(name => Same(name, model.DisplayName)
                                     || string.Equals(name, model.Scope, StringComparison.Ordinal));
        }

        /// <summary>
        /// This source as the rail's words.
        /// </summary>
        public string Said()
        {
            switch (Origin)
            {
                case CheckOrigin.Everything: return "everything loaded";
                case CheckOrigin.TheRest: return "everything except the surfaces";

                case CheckOrigin.Selection:
                    return Keys.Count == 0
                        ? "what is selected"
                        : Count(Keys.Count, "selected element");

                case CheckOrigin.Sets:
                case CheckOrigin.Models:
                    if (Names.Count == 0) return "nothing";
                    if (Names.Count <= 3) return string.Join(", ", Names);
                    return string.Join(", ", Names.Take(2))
                           + " and " + (Names.Count - 2) + " more";

                default: return "nothing";
            }
        }

        /// <inheritdoc />
        public override string ToString() => Written();

        /// <summary>Whether the two sources name the same thing.</summary>
        /// <param name="other">The other source, or null.</param>
        public bool SameAs(CheckSource? other) =>
            other != null && string.Equals(Written(), other.Written(), StringComparison.Ordinal);

        private static string Count(int many, string one) =>
            many + " " + one + (many == 1 ? string.Empty : "s");

        /// <summary>Whether a set name would be misread as one of the reserved words.</summary>
        /// <param name="name">The set name.</param>
        private static bool Reserved(string name) =>
            Same(name, Everywhere) || Same(name, Remainder) || Same(name, Nowhere)
            || Same(name, TheSelection)
            || name.StartsWith(Marker, StringComparison.Ordinal);

        private static bool Same(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        /// <summary>A written part with its bars and percent signs put back.</summary>
        /// <param name="part">The part as written.</param>
        private static string Plain(string part) =>
            part.Replace("%7C", "|").Replace("%25", "%").Trim();

        /// <summary>The origin and its parts, with any bar in a name escaped.</summary>
        /// <param name="origin">The origin's word.</param>
        /// <param name="parts">The names or keys.</param>
        private static string Joined(string origin, IEnumerable<string> parts) =>
            Marker + origin + "|"
            + string.Join("|", parts.Select(one => one.Replace("%", "%25").Replace("|", "%7C")));
    }
}
