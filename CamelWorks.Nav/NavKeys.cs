using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav
{
    /// <summary>
    /// Turns a host item into an <see cref="ElementKey"/>, walking down the three rungs until one of
    /// them works.
    ///
    /// The rungs are tried in order of strength and never mixed: an instance GUID is a fact, a tree
    /// path is usually right, and a geometry signature is a proposal. Which rung a key rests on
    /// travels with it, so everything downstream can weigh a match rather than assume one.
    ///
    /// Main thread only. Every read here goes through the host's single-threaded API.
    /// </summary>
    public static class NavKeys
    {
        /// <summary>
        /// The scope for a model — the hash of its source path.
        ///
        /// Memoised per <c>Model</c>. A federation holds a handful of models and this is asked once
        /// per element: reading the source path and hashing it 500,000 times to get 40 answers was
        /// most of the cost of indexing a document. The table is by reference and the host hands
        /// out the same <c>Model</c> objects for the life of a document, so it is bounded by the
        /// models a session opens; a re-cached model is a different path and hashes to a different
        /// scope, which is the behaviour a memo by reference preserves.
        ///
        /// Main thread only, like everything else here.
        /// </summary>
        /// <param name="model">The model.</param>
        public static string ScopeOf(Model? model)
        {
            if (model == null) return ElementKey.ScopeOf(null);

            if (_scopes.TryGetValue(model, out var known)) return known;

            var scope = ElementKey.ScopeOf(SourcePathOf(model));

            _scopes[model] = scope;

            return scope;
        }

        private static readonly Dictionary<Model, string> _scopes =
            new Dictionary<Model, string>();

        /// <summary>
        /// Forget the memoised scopes.
        ///
        /// Called when a document closes, so a long session does not hold the models it has
        /// finished with.
        /// </summary>
        public static void Forget() => _scopes.Clear();

        /// <summary>
        /// Where a model was loaded from.
        ///
        /// <c>SourceFileName</c> in preference to <c>FileName</c>: an appended NWD keeps the
        /// original CAD path in the former and the cache path in the latter, and a key scoped to a
        /// cache path changes the moment somebody re-caches.
        /// </summary>
        public static string? SourcePathOf(Model? model)
        {
            if (model == null) return null;

            var source = model.SourceFileName;
            return string.IsNullOrWhiteSpace(source) ? model.FileName : source;
        }

        /// <summary>Build the strongest key this item supports.</summary>
        public static ElementKey Of(ModelItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var model = ModelOf(item);
            var scope = ScopeOf(model);
            var path = SourcePathOf(model);

            // Rung 1. An instance GUID survives anything that preserves the element, so it is worth
            // asking for even though plenty of formats never supply one.
            var guid = InstanceGuidOf(item);
            if (guid != Guid.Empty) return ElementKey.FromInstanceGuid(scope, guid);

            // Rung 2. The tree path is stable across a re-export that did not restructure the
            // model, which is the common case.
            //
            // Only a path the walk finished. A walk the host cut short partway up gives a path
            // that starts somewhere arbitrary, and two different elements whose walks were cut at
            // the same depth would key the same - which is a wrong match, and a wrong match is
            // worse than a weak one. A cut-short walk falls through to rung 3.
            var treePath = TreePathOf(item, out var whole);

            if (whole && treePath.Count > 0)
                return ElementKey.FromTreePath(scope, path, ToArray(treePath));

            // Rung 3. Weak by construction and says so: this cannot separate two identical parts,
            // and a match on it is a proposal rather than a fact.
            var bounds = BoundsOf(item);

            return ElementKey.FromGeometry(
                scope,
                Said(() => item.ClassDisplayName) ?? Said(() => item.ClassName),
                Said(() => item.DisplayName),
                bounds.SizeX, bounds.SizeY, bounds.SizeZ);
        }

        /// <summary>
        /// The model an item belongs to, or null when the host will not say.
        ///
        /// Guarded, like every other property read here. This one was not, and it is the property
        /// a clash result's composite item is least likely to answer: the caller in the clash
        /// adapter treats any throw as "this element cannot be keyed" and drops the whole result,
        /// so one unguarded read here took eighteen thousand clashes off a board and left a note
        /// saying they could not be identified.
        /// </summary>
        /// <param name="item">The item.</param>
        public static Model? ModelOf(ModelItem item)
        {
            try
            {
                return item.Model;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>One string the host may refuse to give, or null when it does.</summary>
        /// <param name="read">How to read it.</param>
        private static string? Said(Func<string?> read)
        {
            try
            {
                var said = read();

                return string.IsNullOrEmpty(said) ? null : said;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The item's instance GUID, or <see cref="Guid.Empty"/> when it has none.
        ///
        /// Guarded because the host throws on some item kinds rather than returning empty, and one
        /// unusual node in a federation should not stop the whole document being keyed.
        /// </summary>
        public static Guid InstanceGuidOf(ModelItem item)
        {
            try
            {
                return item.InstanceGuid;
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

        /// <summary>Display names from the model root down to this item.</summary>
        public static IReadOnlyList<string> TreePathOf(ModelItem item) =>
            TreePathOf(item, out _);

        /// <summary>
        /// Display names from the model root down to this item, saying whether it reached the root.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="whole">True when the walk reached the top rather than being cut short.</param>
        public static IReadOnlyList<string> TreePathOf(ModelItem item, out bool whole)
        {
            var path = new List<string>();

            whole = false;

            try
            {
                for (var node = item; node != null; node = node.Parent)
                {
                    var name = node.DisplayName;

                    path.Add(string.IsNullOrEmpty(name)
                        ? node.ClassDisplayName ?? string.Empty
                        : name);
                }

                whole = true;
            }
            catch (Exception)
            {
                // Whatever the walk reached, with the flag left false. The host refuses on some
                // node kinds, and a partial path is not an identity - see the caller.
            }

            path.Reverse();

            return path;
        }

        /// <summary>
        /// The item's bounding box, or an empty one when it has no geometry.
        ///
        /// Guarded for the same reason as the GUID: asking a node with nothing under it for a box
        /// throws in some host versions, and the answer we want in that case is "no size" rather
        /// than a failed traversal.
        /// </summary>
        public static CamelWorks.Core.Abstractions.BoundingBox BoundsOf(ModelItem item)
        {
            try
            {
                var box = item.BoundingBox();
                if (box == null) return default;

                return new CamelWorks.Core.Abstractions.BoundingBox(
                    box.Min.X, box.Min.Y, box.Min.Z,
                    box.Max.X, box.Max.Y, box.Max.Z);
            }
            catch (Exception)
            {
                return default;
            }
        }

        private static string?[] ToArray(IReadOnlyList<string> path)
        {
            var array = new string?[path.Count];
            for (var i = 0; i < path.Count; i++) array[i] = path[i];
            return array;
        }
    }
}
