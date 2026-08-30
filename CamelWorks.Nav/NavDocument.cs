using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using NavColour = Autodesk.Navisworks.Api.Color;

namespace CamelWorks.Nav
{
    /// <summary>One loaded model.</summary>
    public sealed class NavModelSource : IModelSource
    {
        internal NavModelSource(Model model)
        {
            Model = model;
            SourcePath = NavKeys.SourcePathOf(model) ?? string.Empty;

            // FileName, deliberately, where SourcePathOf prefers SourceFileName: this is the file
            // on disk right now, and re-appending the CAD path would open whatever that file has
            // become rather than the revision this model is.
            try
            {
                LoadedPath = model.FileName;
            }
            catch (Exception)
            {
                LoadedPath = null;
            }
            Scope = NavKeys.ScopeOf(model);
            DisplayName = System.IO.Path.GetFileName(SourcePath);
            if (string.IsNullOrEmpty(DisplayName)) DisplayName = "(unnamed model)";

            // Read once, at load. The manifest is a record of what this run saw, and re-reading the
            // share every time a report is written would make two reports of the same run disagree.
            try
            {
                var file = new System.IO.FileInfo(SourcePath);

                if (file.Exists)
                {
                    SourceModifiedUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    SourceBytes = file.Length;
                }
            }
            catch (Exception)
            {
                // An unreadable path is an ordinary answer, not an error: the source may be on a
                // share that is offline, on a drive nobody mapped, or genuinely gone.
            }

            try
            {
                ModelUnits = model.Units.ToString();

                var transform = model.Transform;

                HasTransform = !transform.IsIdentity();
                Placement = Twelve(transform);
            }
            catch (Exception)
            {
                ModelUnits = null;
            }
        }

        /// <summary>The file the host has open for this model. See <see cref="IModelSource.LoadedPath"/>.</summary>
        public string? LoadedPath { get; }

        /// <summary>The host model.</summary>
        public Model Model { get; }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public string SourcePath { get; }

        /// <inheritdoc />
        public string Scope { get; }

        /// <inheritdoc />
        public DateTimeOffset? SourceModifiedUtc { get; }

        /// <inheritdoc />
        public long? SourceBytes { get; }

        /// <inheritdoc />
        public string? ModelUnits { get; }

        /// <inheritdoc />
        public bool HasTransform { get; }

        /// <inheritdoc />
        public IReadOnlyList<double>? Placement { get; }

        /// <summary>The transform as twelve numbers: the linear part row by row, then translation.</summary>
        internal static IReadOnlyList<double> Twelve(Transform3D transform)
        {
            var linear = transform.Linear;
            var move = transform.Translation;

            return new[]
            {
                linear.Get(0, 0), linear.Get(0, 1), linear.Get(0, 2),
                linear.Get(1, 0), linear.Get(1, 1), linear.Get(1, 2),
                linear.Get(2, 0), linear.Get(2, 1), linear.Get(2, 2),
                move.X, move.Y, move.Z,
            };
        }

        /// <summary>Twelve numbers back into a transform, or null when they are not twelve.</summary>
        /// <param name="numbers">The placement, as <see cref="Placement"/> writes it.</param>
        internal static Transform3D? From(IReadOnlyList<double>? numbers)
        {
            if (numbers == null || numbers.Count != 12) return null;

            var linear = new Matrix3(
                numbers[0], numbers[1], numbers[2],
                numbers[3], numbers[4], numbers[5],
                numbers[6], numbers[7], numbers[8]);

            return new Transform3D(linear, new Vector3D(numbers[9], numbers[10], numbers[11]));
        }
    }

    /// <summary>One element.</summary>
    public sealed class NavModelItem : IModelItem
    {
        private readonly NavDocument _document;
        private readonly int _generation;

        internal NavModelItem(NavDocument document, ModelItem item, NavModelSource model)
        {
            _document = document;
            _generation = document.Generation;
            Item = item;
            Model = model;
            Key = NavKeys.Of(item);
        }

        /// <summary>The host item.</summary>
        public ModelItem Item { get; }

        /// <inheritdoc />
        public ElementKey Key { get; }

        /// <inheritdoc />
        public IModelSource Model { get; }

        /// <inheritdoc />
        public string DisplayName => Fresh().DisplayName ?? string.Empty;

        /// <inheritdoc />
        public string? Category => Fresh().ClassDisplayName ?? Fresh().ClassName;

        /// <inheritdoc />
        public string? TypeName
        {
            get
            {
                // The host has no single "type" concept, so the two places a type name actually
                // turns up are tried in the order they are usually right in.
                var type = Property("Item", "Type");
                if (!string.IsNullOrWhiteSpace(type)) return type;

                return Fresh().ClassName;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> TreePath => NavKeys.TreePathOf(Fresh());

        /// <inheritdoc />
        public bool HasGeometry => Fresh().HasGeometry;

        /// <inheritdoc />
        public BoundingBox Bounds => NavKeys.BoundsOf(Fresh());

        /// <inheritdoc />
        public bool IsHidden => Fresh().IsHidden;

        /// <inheritdoc />
        public string? Property(string category, string name)
        {
            if (category == null || name == null) return null;

            foreach (var value in Properties())
                if (string.Equals(value.Category, category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))
                    return value.Value;

            return null;
        }

        /// <inheritdoc />
        public IEnumerable<PropertyValue> Properties()
        {
            foreach (var category in Fresh().PropertyCategories)
            {
                var categoryName = category.DisplayName ?? category.Name ?? "Properties";

                foreach (var property in category.Properties)
                {
                    var propertyName = property.DisplayName ?? property.Name;
                    if (string.IsNullOrEmpty(propertyName)) continue;

                    yield return new PropertyValue(categoryName, propertyName!, NavValues.ToText(property.Value));
                }
            }
        }

        /// <inheritdoc />
        public override string ToString() => DisplayName;

        // Every read checks the generation first. A ModelItem held across a document change is a
        // handle into a tree that no longer exists, and the host's behaviour then ranges from
        // wrong answers to a hard crash — so this throws instead, and the caller re-resolves by
        // key, which is exactly what keys are for.
        private ModelItem Fresh()
        {
            if (_document.Generation != _generation)
                throw new StaleItemException(
                    "this element was read before the document changed; resolve it again by key");

            return Item;
        }
    }

    /// <summary>
    /// The active document, behind the seam.
    ///
    /// <b>Main thread only.</b> Everything here goes through the host's single-threaded read API,
    /// and the seam exists precisely so that nothing above it has to know that.
    /// </summary>
    public sealed class NavDocument : IModelDocument
    {
        private readonly Dictionary<string, ModelItem> _index = new Dictionary<string, ModelItem>(StringComparer.Ordinal);
        private readonly List<NavModelSource> _models = new List<NavModelSource>();
        private bool _indexed;

        // What a key resolved to last time, including the keys that resolved to nothing. Cleared
        // with the index when the document changes.
        private readonly Dictionary<string, ModelItem?> _found =
            new Dictionary<string, ModelItem?>(StringComparer.Ordinal);

        /// <summary>Wrap a host document.</summary>
        public NavDocument(Document document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Rescan();
        }

        /// <summary>The host document.</summary>
        public Document Document { get; }

        /// <inheritdoc />
        public int Generation { get; private set; }

        /// <inheritdoc />
        public bool IsEmpty => _models.Count == 0;

        /// <inheritdoc />
        public IReadOnlyList<IModelSource> Models => _models;

        /// <summary>
        /// Note that the document has changed: a model appended, removed, or refreshed.
        ///
        /// Bumps the generation, which invalidates every item handed out so far. The plug-in calls
        /// this from the host's document-changed events; nothing detects it automatically, because
        /// a stale index that looks fresh is worse than one that says it is stale.
        /// </summary>
        public void Invalidate()
        {
            Generation++;
            _indexed = false;
            _index.Clear();
            _found.Clear();
            _ifc.Clear();
            Rescan();
        }

        private readonly Dictionary<string, string?> _ifc =
            new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// The IFC GlobalId an element carries, or null.
        ///
        /// Cached per element, because an export asks for the same duct once per clash it appears
        /// in and the answer is a property enumeration each time. Cleared with everything else when
        /// the document changes underneath.
        /// </summary>
        /// <param name="key">The element.</param>
        public string? IfcGuidFor(ElementKey key)
        {
            if (key.IsEmpty) return null;

            var text = key.ToString();

            if (_ifc.TryGetValue(text, out var known)) return known;

            var found = CamelWorks.Core.OpenBim.IfcIdentity.Of(Resolve(key));

            _ifc[text] = found;
            return found;
        }

        /// <inheritdoc />
        public IModelItem? Resolve(ElementKey key)
        {
            var item = Item(key);

            if (item == null) return null;

            var model = ModelFor(item);
            return model == null ? null : new NavModelItem(this, item, model);
        }

        /// <summary>
        /// The host item a key names, without walking the federation when it can be helped.
        ///
        /// <b>The index was the wrong first answer.</b> Resolving used to mean building a
        /// dictionary of every geometry leaf in the document, keyed by asking each one for its
        /// GUID, its tree path or its bounds - minutes of managed work on a real federation, paid
        /// by whoever first pressed Focus. The host can already do this: a search by instance GUID
        /// runs natively, lazily, and stops at the first match, and most elements that carry a
        /// clash carry a GUID.
        ///
        /// So a GUID key is a native search; anything else falls back to the index, and only a
        /// document whose elements have no GUIDs ever pays for one. Matches are remembered, so the
        /// second verb on the same rows costs nothing at all.
        /// </summary>
        /// <param name="key">The key to resolve.</param>
        private ModelItem? Item(ElementKey key)
        {
            if (key.IsEmpty) return null;

            var text = key.ToString();

            if (_found.TryGetValue(text, out var remembered)) return remembered;

            // The index, when something has already built it: a dictionary hit beats any search.
            if (_indexed) return _index.TryGetValue(text, out var known) ? known : null;

            if (key.Rung == KeyRung.InstanceGuid && Guid.TryParse(key.UnscopedValue, out var guid)
                && guid != Guid.Empty)
            {
                try
                {
                    foreach (var hit in Document.Models.RootItemDescendantsAndSelf.WhereInstanceGuid(guid))
                    {
                        _found[text] = hit;
                        return hit;
                    }

                    // A GUID this document does not carry. Remembered as absent so a row of
                    // stale results does not re-search for every one of them on every press.
                    _found[text] = null;
                    return null;
                }
                catch (Exception)
                {
                    // The host refused the search. Fall through to the index rather than
                    // reporting an element this model does have as missing.
                }
            }

            // A tree-path or geometry key. There is no native search for either, so this is where
            // the index is genuinely earned - once, for documents whose elements have no GUIDs.
            EnsureIndexed();

            return _index.TryGetValue(text, out var item) ? item : null;
        }

        /// <summary>
        /// How many host enumerations are open on this thread right now.
        ///
        /// <b>Why anything needs to know.</b> A long walk reports progress, and reporting progress
        /// means pumping the message loop - which lets Navisworks process input while we are part
        /// way through enumerating its own tree. The user clicks the model tree, the host mutates
        /// the collection under the iterator, and the process dies in native code where no
        /// try/catch can reach it. So the pump asks this first and stays quiet while a walk is
        /// open: a window that does not repaint for twenty seconds is a cost; a Navisworks that
        /// disappears is not a cost, it is a bug report.
        /// </summary>
        public static int Walking { get; private set; }

        /// <summary>True while any host enumeration is open - do not pump the message loop.</summary>
        public static bool IsWalking => Walking > 0;

        /// <summary>
        /// Every item in a scope, read into a list before the caller does anything else.
        ///
        /// For the loops that report progress. Materialising costs a wrapper per element and gives
        /// back the ability to pump safely for the expensive half - reading properties, hashing,
        /// measuring - because by then no host enumerator is open.
        /// </summary>
        /// <param name="scope">What to walk.</param>
        public IReadOnlyList<IModelItem> Collect(TraversalScope scope) => Traverse(scope).ToList();

        /// <inheritdoc />
        public IEnumerable<IModelItem> Traverse(TraversalScope scope)
        {
            switch (scope.Kind)
            {
                case TraversalScopeKind.CurrentSelection:
                    return Wrap(Leaves(Document.CurrentSelection.SelectedItems));

                case TraversalScopeKind.Set:
                    return Wrap(Leaves(ItemsOfSet(scope.SetName)));

                case TraversalScopeKind.Keys:
                    return ResolveMany(scope.Keys);

                default:
                    return Wrap(Leaves(Document.Models.RootItems));
            }
        }

        /// <summary>Every saved selection and search set in the document, folders included.</summary>
        public IReadOnlyList<SelectionSet> Sets()
        {
            var sets = new List<SelectionSet>();

            try
            {
                Collect(Document.SelectionSets.ToSavedItemCollection(), sets);
            }
            catch (Exception)
            {
                // A document with no sets at all. Not an error, and not worth a message.
            }

            return sets;
        }

        /// <summary>The host items behind a key list, in the order given, skipping what no longer resolves.</summary>
        public ModelItemCollection ItemsFor(IReadOnlyList<ElementKey> keys)
        {
            var collection = new ModelItemCollection();
            if (keys == null) return collection;

            // One key at a time through the same resolver a single verb uses: a GUID is a native
            // search and costs nothing, and only a key that is not a GUID makes anybody pay for
            // the index. Ten picked clash results are twenty native searches, which is instant -
            // where indexing first was a walk of the whole federation before the first of them.
            foreach (var key in keys)
            {
                var item = Item(key);

                if (item != null) collection.Add(item);
            }

            return collection;
        }

        /// <summary>
        /// Put a model back where it was.
        ///
        /// The counterpart of recording the placement, and the reason recording it is worth doing.
        /// The host takes units and transform together, so the model's current units are handed
        /// straight back — this moves a model and never re-scales one.
        /// </summary>
        /// <param name="model">The model to move.</param>
        /// <param name="placement">Twelve numbers, as <c>IModelSource.Placement</c> writes them.</param>
        /// <returns>Null when it worked, or why it did not.</returns>
        public string? Restore(IModelSource model, IReadOnlyList<double>? placement)
        {
            if (!(model is NavModelSource source)) return "that is not a model this document holds";

            var transform = NavModelSource.From(placement);

            if (transform == null)
                return "there is no recorded placement for " + model.DisplayName + " to go back to";

            try
            {
                // On the model collection, not on the document: the document's own overload takes
                // an index pair and is not the one that moves a model.
                Document.Models.SetModelUnitsAndTransform(source.Model, source.Model.Units, transform,
                                                          source.Model.IsTransformReflected);

                // Everything handed out so far points into a tree that has just moved. A stale
                // handle that looks fresh is worse than one that says it is stale.
                Invalidate();

                return null;
            }
            catch (Exception error)
            {
                return "the host refused to move " + model.DisplayName + ": " + error.Message;
            }
        }

        /// <summary>
        /// Make the given elements the host's current selection.
        ///
        /// Every board and list in the product goes through here rather than reaching for
        /// <c>CurrentSelection</c> itself, so that "clicking a row selects the thing" behaves the
        /// same everywhere and there is one place to change if it should ever stop.
        /// </summary>
        /// <param name="keys">What to select. Keys that no longer resolve are skipped.</param>
        public void Select(IReadOnlyList<ElementKey> keys) =>
            Document.CurrentSelection.CopyFrom(ItemsFor(keys));

        /// <summary>The keys of whatever is selected right now, geometry-bearing nodes only.</summary>
        public IReadOnlyList<ElementKey> SelectedKeys()
        {
            var keys = new List<ElementKey>();

            foreach (var item in Leaves(Document.CurrentSelection.SelectedItems))
            {
                var key = NavKeys.Of(item);
                if (!key.IsEmpty) keys.Add(key);
            }

            return keys;
        }

        private IEnumerable<IModelItem> ResolveMany(IReadOnlyList<ElementKey>? keys)
        {
            if (keys == null) yield break;

            foreach (var key in keys)
            {
                var item = Resolve(key);
                if (item != null) yield return item;
            }
        }

        private IEnumerable<IModelItem> Wrap(IEnumerable<ModelItem> items)
        {
            foreach (var item in items)
            {
                var model = ModelFor(item);
                if (model != null) yield return new NavModelItem(this, item, model);
            }
        }

        private IEnumerable<ModelItem> ItemsOfSet(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Enumerable.Empty<ModelItem>();

            var set = Sets().FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            if (set == null) return Enumerable.Empty<ModelItem>();

            try
            {
                // A search set has to be re-run; a selection set already holds its items. Both live
                // in the same collection and only this distinguishes them.
                return set.Search != null
                    ? set.Search.FindAll(Document, false).Cast<ModelItem>()
                    : set.GetSelectedItems().Cast<ModelItem>();
            }
            catch (Exception)
            {
                return Enumerable.Empty<ModelItem>();
            }
        }

        private NavModelSource? ModelFor(ModelItem item)
        {
            var model = item.Model;
            if (model == null) return null;

            foreach (var source in _models)
                if (ReferenceEquals(source.Model, model)) return source;

            // A model that appeared without the generation being bumped. Rescanning here rather
            // than dropping the item, since losing elements silently is the worse failure.
            Rescan();

            foreach (var source in _models)
                if (ReferenceEquals(source.Model, model)) return source;

            return null;
        }

        private void Rescan()
        {
            _models.Clear();

            foreach (Model model in Document.Models)
                _models.Add(new NavModelSource(model));
        }

        /// <summary>
        /// Whether the element index has been built for the federation as it stands.
        ///
        /// Worth asking before a verb that resolves keys: the first one to ask pays for the whole
        /// walk, and a screen that knows can pay for it somewhere the user is watching a progress
        /// bar instead of inside a button click.
        /// </summary>
        public bool IsIndexed => _indexed;

        /// <summary>
        /// Build the element index, reporting as it goes.
        ///
        /// <b>Why this is public.</b> Resolving an element key means looking it up in an index of
        /// every geometry leaf in the federation, and building that index is a full walk that keys
        /// each item. Left to happen lazily, the whole cost landed on whoever first pressed Focus
        /// - synchronously, inside a click handler, with no progress and no way to stop: on a real
        /// federation that is Navisworks saying Not Responding for minutes, and the honest reading
        /// of it is that the button hung.
        ///
        /// So the long operations that are about to need it - reading a clash board, running a
        /// check - build it inside their own job, where there is a progress bar and a Stop, and
        /// every row verb afterwards is instant.
        /// </summary>
        /// <param name="onProgress">Called with the running count, for a progress line.</param>
        /// <param name="keepGoing">Asked between items; false stops and leaves the index unbuilt.</param>
        public void Index(Action<int>? onProgress = null, Func<bool>? keepGoing = null)
        {
            if (_indexed) return;

            _index.Clear();

            var seen = 0;

            foreach (var item in Leaves(Document.Models.RootItems))
            {
                var key = NavKeys.Of(item).ToString();
                if (key.Length > 0 && !_index.ContainsKey(key)) _index[key] = item;

                seen++;

                // Every few thousand: the callback usually pumps, and asking on every element of a
                // half-million-element federation costs more than the keying does.
                if (seen % 2000 != 0) continue;

                onProgress?.Invoke(seen);

                if (keepGoing != null && !keepGoing())
                {
                    // Stopped half way. Left unmarked so the next caller builds it properly rather
                    // than resolving against a third of a federation and reporting the rest as
                    // elements this model does not have.
                    _index.Clear();
                    return;
                }
            }

            _indexed = true;
        }

        private void EnsureIndexed()
        {
            // Indexed once and kept: keying an item is not free, and resolving a thousand-row clash
            // board one key at a time against an unindexed tree is the difference between a board
            // that opens and one that does not.
            if (!_indexed) Index();
        }

        private static void Collect(SavedItemCollection items, List<SelectionSet> result)
        {
            foreach (SavedItem item in items)
            {
                if (item is SelectionSet set) result.Add(set);
                else if (item is FolderItem folder && folder.Children != null) Collect(folder.Children, result);
            }
        }

        // Geometry-bearing nodes, taking a node's own geometry when nothing beneath it produced
        // any. Some formats hang reference planes and annotations under a part that carries the
        // mesh itself, and the naive "only childless nodes" rule loses the entire part.
        /// <summary>
        /// The geometry leaves under a set of roots, depth first, without a host enumerator open
        /// across the yield.
        ///
        /// <b>Why it is a stack and not recursion.</b> The recursive version held one live host
        /// enumerator per level of the tree for the whole walk. Callers report progress as they go,
        /// reporting progress pumps the message loop, and pumping while the host's own collections
        /// are being enumerated lets Navisworks act on a click that mutates them - which ends the
        /// process in native code where no handler can reach it. Here each node's children are
        /// read into a list and the host's enumerator is finished with immediately, so at the
        /// moment a caller pumps, nothing of the host's is open.
        /// </summary>
        private static IEnumerable<ModelItem> Leaves(IEnumerable<ModelItem> roots)
        {
            // Materialised for the same reason: this is usually Document.Models.RootItems, and it
            // must not stay open for the length of the walk.
            Stack<ModelItem> stack;

            Walking++;

            try
            {
                stack = new Stack<ModelItem>(roots.Reverse().ToList());
            }
            finally
            {
                Walking--;
            }

            while (stack.Count > 0)
            {
                var item = stack.Pop();

                List<ModelItem> children;

                Walking++;

                try
                {
                    children = item.Children.ToList();
                }
                catch (Exception)
                {
                    // A node the host will not open. Treated as a leaf rather than losing the
                    // branch: one awkward node must not end a walk of two hundred thousand.
                    children = new List<ModelItem>();
                }
                finally
                {
                    Walking--;
                }

                if (children.Count == 0)
                {
                    if (item.HasGeometry) yield return item;
                    continue;
                }

                for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            }
        }
    }

    /// <summary>Turns the host's variant values into text without throwing.</summary>
    public static class NavValues
    {
        /// <summary>
        /// A property value as text.
        ///
        /// Every branch is guarded. A federation contains property values the host itself cannot
        /// render, and one of them must not stop a traversal of two hundred thousand elements.
        /// </summary>
        public static string? ToText(VariantData value)
        {
            if (value == null) return null;

            try
            {
                if (value.IsDisplayString) return value.ToDisplayString();
                if (value.IsNamedConstant) return value.ToNamedConstant().DisplayName;
                if (value.IsBoolean) return value.ToBoolean() ? "Yes" : "No";
                if (value.IsInt32) return value.ToInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (value.IsDouble) return value.ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (value.IsDateTime) return value.ToDateTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

                return value.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A host colour as a CamelWorks one.</summary>
        public static Colour ToColour(NavColour colour) =>
            new Colour(Channel(colour.R), Channel(colour.G), Channel(colour.B));

        /// <summary>A CamelWorks colour as a host one.</summary>
        public static NavColour FromColour(Colour colour) =>
            NavColour.FromByteRGB(colour.R, colour.G, colour.B);

        private static byte Channel(double value)
        {
            var scaled = Math.Round(value * 255, MidpointRounding.AwayFromZero);
            if (scaled < 0) return 0;
            if (scaled > 255) return 255;
            return (byte)scaled;
        }
    }
}
