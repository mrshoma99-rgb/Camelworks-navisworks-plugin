using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav
{
    /// <summary>
    /// Property writes, batched and previewed before anything touches the document.
    ///
    /// Nothing is written until <see cref="Commit"/>. That is not tidiness: writing properties goes
    /// through the COM bridge, one call per element, and a half-applied batch on a federation is
    /// not something anybody can undo by hand. So the whole batch is accumulated, previewed —
    /// including which keys no longer resolve — and then applied in one pass, or not at all.
    ///
    /// <b>Main thread only.</b>
    /// </summary>
    public sealed class NavWriteTransaction : IModelWriteTransaction
    {
        private readonly NavDocument _document;
        private readonly Dictionary<string, List<Write>> _writes = new Dictionary<string, List<Write>>(StringComparer.Ordinal);
        private readonly Dictionary<string, ElementKey> _keys = new Dictionary<string, ElementKey>(StringComparer.Ordinal);
        private bool _finished;
        private string? _problem;

        /// <summary>Begin a batch.</summary>
        /// <param name="document">The document to write to.</param>
        /// <param name="description">What the batch is for, shown in any progress or error message.</param>
        public NavWriteTransaction(NavDocument document, string description)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            Description = string.IsNullOrWhiteSpace(description) ? "CamelWorks" : description;
        }

        /// <summary>
        /// The property tab written to.
        ///
        /// One tab, named for the product, and never mixed into the host's own or the authoring
        /// tool's categories. A coordinator has to be able to tell at a glance which properties
        /// came from here — and be able to ignore all of them at once when comparing two exports.
        /// </summary>
        public const string TabName = "CamelWorks";

        /// <inheritdoc />
        public string Description { get; }

        /// <inheritdoc />
        public string? LastProblem => _problem;

        /// <inheritdoc />
        public void SetProperty(ElementKey key, string category, string name, string? value)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (name == null) throw new ArgumentNullException(nameof(name));

            Record(key, new Write(category, name, value, remove: false));
        }

        /// <inheritdoc />
        public void RemoveProperty(ElementKey key, string category, string name)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (name == null) throw new ArgumentNullException(nameof(name));

            Record(key, new Write(category, name, null, remove: true));
        }

        /// <inheritdoc />
        public WritePreview Preview()
        {
            var unresolved = new List<ElementKey>();
            var resolved = 0;
            var sets = 0;
            var removals = 0;

            foreach (var pair in _writes)
            {
                var key = _keys[pair.Key];

                if (_document.Resolve(key) == null)
                {
                    // Named rather than dropped. A batch that silently skipped the elements it
                    // could not find would report success and change less than it said.
                    unresolved.Add(key);
                    continue;
                }

                resolved++;
                sets += pair.Value.Count(w => !w.Remove);
                removals += pair.Value.Count(w => w.Remove);
            }

            return new WritePreview(resolved, sets, removals, unresolved);
        }

        /// <inheritdoc />
        public int Commit()
        {
            if (_finished) throw new InvalidOperationException("this transaction has already been finished");

            var written = 0;
            var refused = 0;

            _problem = null;

            try
            {
                using (var transaction = _document.Document.BeginTransaction(Description))
                {
                    foreach (var pair in _writes)
                    {
                        var key = _keys[pair.Key];
                        var item = _document.Resolve(key) as NavModelItem;
                        if (item == null) continue;

                        if (Apply(item.Item, pair.Value)) written++;
                        else refused++;
                    }

                    transaction.Commit();
                }
            }
            catch (Exception error)
            {
                // The document would not even open a transaction. Read-only is the usual reason,
                // and it has to reach the screen rather than look like a batch of zero.
                _problem = Explain(error);
                _finished = true;
                return written;
            }

            if (refused > 0 && _problem == null)
                _problem = Ended(refused) + " refused by the document, with no reason given.";

            _finished = true;
            return written;
        }

        /// <inheritdoc />
        public void Rollback()
        {
            _writes.Clear();
            _keys.Clear();
            _finished = true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Disposing without committing rolls back. A batch abandoned by an exception must not
            // be half-applied, and must not be applied later by accident.
            if (!_finished) Rollback();
        }

        private void Record(ElementKey key, Write write)
        {
            if (_finished) throw new InvalidOperationException("this transaction has already been finished");
            if (key.IsEmpty) return;

            var id = key.ToString();
            _keys[id] = key;

            if (!_writes.TryGetValue(id, out var list))
            {
                list = new List<Write>();
                _writes[id] = list;
            }

            // Last write wins on the same property, so a service that revises its mind mid-batch
            // does not produce two conflicting values on one element.
            list.RemoveAll(w => string.Equals(w.Category, write.Category, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(w.Name, write.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(write);
        }

        /// <summary>
        /// The one call that actually touches the host.
        ///
        /// Isolated deliberately: everything above is ordinary logic that can be reasoned about,
        /// and this is the COM bridge, which cannot. It writes the whole tab in one go because the
        /// bridge replaces a user-defined tab wholesale rather than merging into it — so a removal
        /// is simply an omission from the rebuilt tab, and the existing values have to be read back
        /// and re-sent alongside the new ones.
        /// </summary>
        private bool Apply(ModelItem item, List<Write> writes)
        {
            try
            {
                var state = ComApiBridge.State;
                var path = ComApiBridge.ToInwOaPath(item);

                var vector = (InwOaPropertyVec)state.ObjectFactory(
                    nwEObjectType.eObjectType_nwOaPropertyVec, null, null);

                foreach (var write in Merge(item, writes))
                {
                    var property = (InwOaProperty)state.ObjectFactory(
                        nwEObjectType.eObjectType_nwOaProperty, null, null);

                    property.name = write.Name;
                    property.UserName = write.Name;
                    property.value = write.Value;

                    vector.Properties().Add(property);
                }

                // SetUserDefined is on the GUI property node, not on the state. The node is the
                // per-item property panel, and asking for it with create:true is what makes a tab
                // exist on an element that has none.
                var node = (InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
                node.SetUserDefined(0, TabName, TabName, vector);
                return true;
            }
            catch (Exception error)
            {
                // One element the bridge refuses must not take the batch with it. The count
                // returned by Commit is what the caller reports, and it will be short by this one
                // - but the reason is kept, because a count that is short for no stated reason is
                // the message nobody can act on.
                _problem ??= Explain(error);
                return false;
            }
        }

        // The bridge replaces the tab rather than merging, so whatever is already in it has to be
        // carried forward — otherwise setting one property silently deletes every other one that
        // CamelWorks wrote earlier.
        private static IEnumerable<Write> Merge(ModelItem item, List<Write> writes)
        {
            var merged = new List<Write>();

            foreach (var category in item.PropertyCategories)
            {
                var categoryName = category.DisplayName ?? category.Name;
                if (!string.Equals(categoryName, TabName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var property in category.Properties)
                {
                    var name = property.DisplayName ?? property.Name;
                    if (string.IsNullOrEmpty(name)) continue;

                    merged.Add(new Write(TabName, name!, NavValues.ToText(property.Value), remove: false));
                }
            }

            foreach (var write in writes)
            {
                merged.RemoveAll(w => string.Equals(w.Name, write.Name, StringComparison.OrdinalIgnoreCase));
                if (!write.Remove) merged.Add(write);
            }

            return merged.Where(w => w.Value != null);
        }

        /// <summary>
        /// A host refusal, in words a coordinator can act on.
        ///
        /// The COM bridge throws the same kind of exception for a read-only document as for a
        /// genuine fault, so the read-only case is recognised and named: it is the one the user
        /// can do something about, and it is by far the commonest.
        /// </summary>
        private string Explain(Exception error)
        {
            var said = (error.Message ?? string.Empty).Trim();

            if (ReadOnly())
                return "This document is read-only, so nothing can be written into it. Published "
                       + "files hold their data that way. Open the NWF the models were federated "
                       + "in and the same write lands."
                       + (said.Length == 0 ? string.Empty : " The host said: " + said);

            return said.Length == 0 ? "The document refused the write." : said;
        }

        /// <summary>Whether the host says this document may not be written to.</summary>
        private bool ReadOnly()
        {
            try
            {
                return _document.Document.IsClear == false && _document.Document.FileName != null
                       && System.IO.File.Exists(_document.Document.FileName)
                       && new System.IO.FileInfo(_document.Document.FileName).IsReadOnly;
            }
            catch (Exception)
            {
                // No file, no rights to look at it, or a host mid-change: none of them is worth
                // a wrong claim about why the write failed.
                return false;
            }
        }

        private static string Ended(int count) =>
            count == 1 ? "One element was" : count + " elements were";

        private readonly struct Write
        {
            internal Write(string category, string name, string? value, bool remove)
            {
                Category = category; Name = name; Value = value; Remove = remove;
            }

            internal string Category { get; }

            internal string Name { get; }

            internal string? Value { get; }

            internal bool Remove { get; }
        }
    }
}
