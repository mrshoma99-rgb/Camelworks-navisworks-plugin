using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Testing
{
    /// <summary>
    /// An in-memory <see cref="IModelDocument"/> for tests, with an ordered effect log.
    ///
    /// The log is the point. Asserting final state proves a service reached the right answer;
    /// asserting the ordered effects proves it got there the right way — that it previewed before
    /// writing, that it did not write twice, that it re-resolved after a generation bump instead of
    /// reusing a handle. Those are the failures that survive state-only tests.
    /// </summary>
    public sealed class FakeDocument : IModelDocument, ITriangleSource
    {
        private readonly List<FakeItem> _items = new List<FakeItem>();
        private readonly List<FakeSource> _models = new List<FakeSource>();
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<string, List<ElementKey>> _sets =
            new Dictionary<string, List<ElementKey>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ElementKey, TriangleMesh?> _meshes =
            new Dictionary<ElementKey, TriangleMesh?>();
        private List<ElementKey> _selection = new List<ElementKey>();

        /// <inheritdoc />
        public int Generation { get; private set; } = 1;

        /// <inheritdoc />
        public bool IsEmpty => _items.Count == 0;

        /// <inheritdoc />
        public IReadOnlyList<IModelSource> Models => _models;

        /// <summary>The ordered effect log. Cleared by <see cref="ClearLog"/>.</summary>
        public IReadOnlyList<string> Log => _log;

        internal void Record(string effect) => _log.Add(effect);

        /// <summary>Drop everything recorded so far, so a test can assert only what follows.</summary>
        public void ClearLog() => _log.Clear();

        /// <summary>Add a model and return it, for use as an item's owner.</summary>
        public FakeSource AddModel(string displayName, string sourcePath)
        {
            var source = new FakeSource(displayName, sourcePath);
            _models.Add(source);
            Record("model.add " + displayName);
            return source;
        }

        /// <summary>Add an element to a model.</summary>
        public FakeItem AddItem(
            FakeSource model,
            string displayName,
            string? category = null,
            string? typeName = null,
            BoundingBox bounds = default,
            bool hasGeometry = true,
            params string[] treePathAbove)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var path = new List<string>(treePathAbove) { displayName };
            var key = ElementKey.FromTreePath(model.Scope, model.SourcePath, path.Select(p => (string?)p).ToArray());
            var item = new FakeItem(this, key, model, displayName, category, typeName, path, bounds, hasGeometry);
            _items.Add(item);
            return item;
        }

        /// <summary>Define a named set.</summary>
        public void DefineSet(string name, params ElementKey[] keys)
        {
            _sets[name] = new List<ElementKey>(keys);
            Record("set.define " + name + " n=" + keys.Length);
        }

        /// <summary>Set the host selection.</summary>
        public void Select(params ElementKey[] keys)
        {
            _selection = new List<ElementKey>(keys);
            Record("selection.set n=" + keys.Length);
        }

        /// <summary>
        /// Bump the generation, as an append or refresh would. Every item handed out before this
        /// call becomes stale and throws on next touch.
        /// </summary>
        public void BumpGeneration(string reason = "refresh")
        {
            Generation++;
            Record("document.generation " + Generation + " (" + reason + ")");
        }

        /// <summary>Remove an element, as a revision that deleted it would.</summary>
        public void RemoveItem(ElementKey key)
        {
            _items.RemoveAll(i => i.Key == key);
            Record("item.remove " + key);
        }

        /// <inheritdoc />
        public IModelItem? Resolve(ElementKey key)
        {
            var item = _items.FirstOrDefault(i => i.Key == key);
            Record("resolve " + key + (item == null ? " -> null" : " -> ok"));
            if (item == null) return null;
            item.RefreshGeneration(Generation);
            return item;
        }

        /// <inheritdoc />
        public IEnumerable<IModelItem> Traverse(TraversalScope scope)
        {
            Record("traverse " + scope);

            IEnumerable<FakeItem> chosen = scope.Kind switch
            {
                TraversalScopeKind.WholeDocument => _items,
                TraversalScopeKind.CurrentSelection => _items.Where(i => _selection.Contains(i.Key)),
                TraversalScopeKind.Set => _sets.TryGetValue(scope.SetName ?? string.Empty, out var keys)
                    ? _items.Where(i => keys.Contains(i.Key))
                    : Enumerable.Empty<FakeItem>(),
                TraversalScopeKind.Keys => _items.Where(i => scope.Keys!.Contains(i.Key)),
                _ => Enumerable.Empty<FakeItem>(),
            };

            foreach (var item in chosen)
            {
                item.RefreshGeneration(Generation);
                yield return item;
            }
        }

        /// <summary>Open a write transaction against this document.</summary>
        public IModelWriteTransaction BeginWrite(string description) => new FakeWriteTransaction(this, description);

        internal FakeItem? Find(ElementKey key) => _items.FirstOrDefault(i => i.Key == key);

        // ---- Triangles ------------------------------------------------------------------
        //
        // The seam's far side is a COM callback that cannot run here, so everything the walkable
        // surface detector and the fall-hazard raster are asserted on is asserted against these.

        /// <summary>
        /// Whether this seat can produce triangles at all. Settable, so the path a caller takes
        /// when it cannot — element boxes instead of surfaces — is tested rather than assumed.
        /// </summary>
        public bool CanReadTriangles { get; set; } = true;

        /// <inheritdoc />
        public string? Unavailable { get; set; }

        /// <summary>
        /// Whether a mesh is welded on the way out, as the host adapter welds.
        ///
        /// Settable so a test can watch what happens without it: the host emits a fresh vertex per
        /// triangle corner, the detector's flood fill walks shared vertex indices, and unwelded
        /// input therefore produces one cluster per triangle and no surfaces at all.
        /// </summary>
        public bool WeldsOnRead { get; set; } = true;

        /// <summary>Give an element triangles, exactly as supplied.</summary>
        /// <param name="key">Which element.</param>
        /// <param name="positions">Vertex positions, three doubles each, in metres.</param>
        /// <param name="indices">Triangle indices, three per triangle.</param>
        public void SetMesh(ElementKey key, IReadOnlyList<double> positions, IReadOnlyList<int> indices)
        {
            _meshes[key] = new TriangleMesh(positions, indices);
            Record("mesh.set " + key + " t=" + (indices?.Count ?? 0) / 3);
        }

        /// <summary>Give an element a horizontal slab, one that can be walked on.</summary>
        /// <param name="key">Which element.</param>
        /// <param name="x">Low corner, X, in metres.</param>
        /// <param name="y">Low corner, Y, in metres.</param>
        /// <param name="z">Underside, in metres.</param>
        /// <param name="width">Size along X, in metres.</param>
        /// <param name="depth">Size along Y, in metres.</param>
        /// <param name="thickness">Size along Z, in metres.</param>
        public void SetSlab(ElementKey key, double x, double y, double z,
                            double width, double depth, double thickness) =>
            SetBox(key, new BoundingBox(x, y, z, x + width, y + depth, z + thickness));

        /// <summary>
        /// Give an element a box, as twelve triangles wound outwards — so its top face is
        /// up-facing and its underside is not, which is what separates a slab from its own soffit.
        /// </summary>
        /// <param name="key">Which element.</param>
        /// <param name="box">The box, in metres.</param>
        public void SetBox(ElementKey key, BoundingBox box)
        {
            var positions = new[]
            {
                box.MinX, box.MinY, box.MinZ,
                box.MaxX, box.MinY, box.MinZ,
                box.MaxX, box.MaxY, box.MinZ,
                box.MinX, box.MaxY, box.MinZ,
                box.MinX, box.MinY, box.MaxZ,
                box.MaxX, box.MinY, box.MaxZ,
                box.MaxX, box.MaxY, box.MaxZ,
                box.MinX, box.MaxY, box.MaxZ,
            };

            var indices = new[]
            {
                4, 5, 6, 4, 6, 7,   // top, facing up
                0, 2, 1, 0, 3, 2,   // underside, facing down
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            };

            _meshes[key] = new TriangleMesh(positions, indices);
            Record("mesh.box " + key);
        }

        /// <summary>Have the host refuse this element's triangles, as a COM failure does.</summary>
        /// <param name="key">Which element.</param>
        public void RefuseTriangles(ElementKey key)
        {
            _meshes[key] = null;
            Record("mesh.refuse " + key);
        }

        /// <inheritdoc />
        public bool TryRead(ElementKey key, out TriangleMesh? mesh)
        {
            mesh = null;

            if (!CanReadTriangles) return false;

            // An element nobody gave triangles to was read and has none, which is not a refusal:
            // a grouping node and an annotation both read that way.
            if (!_meshes.TryGetValue(key, out var held)) { mesh = TriangleMesh.Empty; return true; }

            if (held == null) return false;

            mesh = WeldsOnRead ? held.Weld() : held;
            return true;
        }

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> Read(TraversalScope scope,
                                                                       Func<int, bool>? onProgress)
        {
            var read = 0;

            foreach (var item in Traverse(scope))
            {
                if (onProgress != null && !onProgress(read)) yield break;

                read++;

                if (TryRead(item.Key, out var mesh) && mesh != null)
                    yield return new KeyValuePair<ElementKey, TriangleMesh>(item.Key, mesh);
            }
        }
    }

    /// <summary>A model in a <see cref="FakeDocument"/>.</summary>
    public sealed class FakeSource : IModelSource
    {
        internal FakeSource(string displayName, string sourcePath)
        {
            DisplayName = displayName;
            SourcePath = sourcePath;
            Scope = ElementKey.ScopeOf(sourcePath);
        }

        /// <summary>When the source was last written. Settable, so a test can stage a revision.</summary>
        public DateTimeOffset? SourceModifiedUtc { get; set; }

        /// <summary>How big the source is. Settable, so a test can stage a revision.</summary>
        public long? SourceBytes { get; set; }

        /// <summary>The unit the model was authored in.</summary>
        public string? ModelUnits { get; set; }

        /// <summary>True when the host is holding this model somewhere other than where it was authored.</summary>
        public bool HasTransform { get; set; }

        /// <summary>Where the federation is holding it. Settable, so a test can stage a move.</summary>
        public IReadOnlyList<double>? Placement { get; set; }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public string SourcePath { get; }

        /// <summary>The file the host has open for this model. Defaults to the source path.</summary>
        public string? LoadedPath { get; set; }

        /// <inheritdoc />
        public string Scope { get; }
    }

    /// <summary>An element in a <see cref="FakeDocument"/>.</summary>
    public sealed class FakeItem : IModelItem
    {
        private readonly FakeDocument _document;
        private readonly Dictionary<string, string?> _properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        private int _resolvedAtGeneration;

        internal FakeItem(FakeDocument document, ElementKey key, FakeSource model, string displayName,
                          string? category, string? typeName, IReadOnlyList<string> treePath,
                          BoundingBox bounds, bool hasGeometry)
        {
            _document = document;
            Key = key;
            Model = model;
            DisplayName = displayName;
            Category = category;
            TypeName = typeName;
            TreePath = treePath;
            Bounds = bounds;
            HasGeometry = hasGeometry;
            _resolvedAtGeneration = document.Generation;
        }

        internal void RefreshGeneration(int generation) => _resolvedAtGeneration = generation;

        private void GuardStale()
        {
            if (_resolvedAtGeneration != _document.Generation)
                throw new StaleItemException(
                    "This item was resolved in generation " + _resolvedAtGeneration.ToString(CultureInfo.InvariantCulture) +
                    " but the document is now at " + _document.Generation.ToString(CultureInfo.InvariantCulture) +
                    ". Hold (generation, ElementKey) and re-resolve; never hold the item.");
        }

        /// <inheritdoc />
        public ElementKey Key { get; }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public string? Category { get; }

        /// <inheritdoc />
        public string? TypeName { get; }

        /// <inheritdoc />
        public IModelSource Model { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> TreePath { get; }

        /// <inheritdoc />
        public bool HasGeometry { get; }

        /// <inheritdoc />
        public BoundingBox Bounds { get; }

        /// <inheritdoc />
        public bool IsHidden { get; internal set; }

        /// <summary>Seed a property, as the source model would carry it.</summary>
        public FakeItem WithProperty(string category, string name, string? value)
        {
            _properties[category + "/" + name] = value;
            return this;
        }

        /// <inheritdoc />
        public string? Property(string category, string name)
        {
            GuardStale();
            return _properties.TryGetValue(category + "/" + name, out var v) ? v : null;
        }

        /// <inheritdoc />
        public IEnumerable<PropertyValue> Properties()
        {
            GuardStale();
            foreach (var kv in _properties)
            {
                var slash = kv.Key.IndexOf('/');
                yield return new PropertyValue(kv.Key.Substring(0, slash), kv.Key.Substring(slash + 1), kv.Value);
            }
        }

        internal void ApplyWrite(string category, string name, string? value) =>
            _properties[category + "/" + name] = value;

        internal void ApplyRemove(string category, string name) =>
            _properties.Remove(category + "/" + name);

        internal bool Has(string category, string name) => _properties.ContainsKey(category + "/" + name);
    }
}
