using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Nav
{
    /// <summary>
    /// The document's saved viewpoints.
    ///
    /// Identified by their folder path rather than by index. The host's own collection renumbers
    /// whenever anything is added or removed, so an index captured a moment ago points somewhere
    /// else by the time it is used — which is how a "go to viewpoint" button ends up showing the
    /// wrong view.
    ///
    /// <b>Main thread only.</b>
    /// </summary>
    public sealed class NavViewpointStore : IViewpointStore
    {
        private readonly NavDocument _document;

        /// <summary>Wrap a document.</summary>
        public NavViewpointStore(NavDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        private Document Host => _document.Document;

        private string? _problem;

        /// <inheritdoc />
        public string? LastProblem => _problem;

        /// <summary>
        /// Do something to the document's saved viewpoints, and keep the reason when it will not.
        ///
        /// Every write here goes through this. Saved viewpoints live in the document, so on a
        /// published NWD every one of them is refused - and the batches that make forty at a time
        /// used to count forty successes they had never checked, or let a COM exception out into a
        /// panel as a stack trace.
        /// </summary>
        /// <param name="act">The write.</param>
        private bool Try(Action act)
        {
            _problem = null;

            try
            {
                act();
                return true;
            }
            catch (Exception error)
            {
                _problem = Explain(error);
                return false;
            }
        }

        /// <summary>A host refusal, in words a coordinator can act on.</summary>
        private string Explain(Exception error)
        {
            var said = (error.Message ?? string.Empty).Trim();

            var head = _document.Document.IsClear
                ? "There is no document to save a viewpoint into."
                : "This document would not take the viewpoint. A published NWD is a snapshot: "
                  + "saved viewpoints live in the document, so they cannot be written into one. "
                  + "Open the NWF the models were federated in.";

            return said.Length == 0 ? head : head + " The host said: " + said;
        }

        /// <inheritdoc />
        public IReadOnlyList<SavedView> All()
        {
            var views = new List<SavedView>();

            try
            {
                Collect(Host.SavedViewpoints.ToSavedItemCollection(), null, views);
            }
            catch (Exception)
            {
                // A document with no saved viewpoints. Not an error.
            }

            return views;
        }

        /// <inheritdoc />
        public SavedView? SaveCurrent(string name, string? folder = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a viewpoint needs a name", nameof(name));

            var saved = Try(() =>
            {
                // Document.CurrentViewpoint is a Viewpoint, not a SavedViewpoint — the two are
                // unrelated types, and only SavedViewpoint can go into the collection. So the live
                // camera is copied and then wrapped, rather than cast.
                var copy = new SavedViewpoint(Host.CurrentViewpoint.CreateCopy())
                {
                    DisplayName = name,
                };

                Host.SavedViewpoints.AddCopy(copy);
            });

            return saved ? new SavedView(PathOf(folder, name), name, folder, false, false) : null;
        }

        /// <inheritdoc />
        public bool Apply(string viewId)
        {
            var found = Find(viewId);

            if (found == null)
            {
                _problem = "There is no saved viewpoint at \"" + viewId + "\".";
                return false;
            }

            return Try(() => Host.SavedViewpoints.CurrentSavedViewpoint = found);
        }

        /// <inheritdoc />
        public bool Rename(string viewId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("a viewpoint needs a name", nameof(newName));

            var found = Find(viewId);

            if (found == null)
            {
                _problem = "There is no saved viewpoint at \"" + viewId + "\".";
                return false;
            }

            // EditDisplayName, not remove-then-add. The collection edits the item where it stands,
            // so a bulk rename of forty viewpoints leaves all forty in the order the coordinator
            // put them in — where remove-then-add moves each one to the end of its folder and
            // reverses the list as a side effect of doing its job.
            return Try(() => Host.SavedViewpoints.EditDisplayName(found, newName));
        }

        /// <inheritdoc />
        public string? MoveTo(string viewId, string? folder)
        {
            var found = Find(viewId);
            if (found == null) return null;

            var name = found.DisplayName ?? string.Empty;

            // Compared as paths rather than as objects. The host hands back a fresh wrapper around
            // the same native folder on every access, so ReferenceEquals says "different" about
            // two handles on one folder — and the viewpoint would be taken out of its position and
            // put back at the end of the folder it was already in.
            var here = FolderOf(viewId);
            var there = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim().Trim('/');

            if (string.Equals(here ?? string.Empty, there ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return PathOf(there, name);

            // Added first, then the original removed. The other order loses the viewpoint
            // outright when the document takes the removal and refuses the copy - which is
            // exactly what a read-only document does.
            var moved = Try(() =>
            {
                var wanted = Folder(there);

                var copy = (SavedViewpoint)found.CreateCopy();
                copy.DisplayName = name;

                Host.SavedViewpoints.AddCopy(wanted, copy);
                Host.SavedViewpoints.Remove(Parent(found), found);
            });

            return moved ? PathOf(there, name) : null;
        }

        /// <inheritdoc />
        public bool CopyOverrides(string fromViewId, string toViewId)
        {
            if (string.Equals(fromViewId, toViewId, StringComparison.OrdinalIgnoreCase)) return false;

            var source = Find(fromViewId);
            var target = Find(toViewId);

            if (source == null || target == null) return false;

            var parent = Parent(target);
            var index = IndexOf(parent, target);

            if (index < 0) return false;

            var name = target.DisplayName ?? string.Empty;

            // The camera has to be read before the source is applied, because applying it is what
            // moves the camera.
            var camera = target.Viewpoint?.CreateCopy();

            // Restore the source: this is what puts its overrides into the running view, and it is
            // the only way to get at them — GetAppearanceOverrides reads them but nothing writes
            // them onto another viewpoint.
            Host.SavedViewpoints.CurrentSavedViewpoint = source;

            // Camera back to where the target had it, so this copies the overrides and not the
            // view. A "copy overrides" that quietly moved the camera would be a different feature.
            if (camera != null) Host.CurrentViewpoint.CopyFrom(camera);

            var made = Host.SavedViewpoints.CaptureRuntimeOverrides();

            // CaptureRuntimeOverrides rather than ReplaceFromCurrentView: the latter is documented
            // as updating the viewpoint, redlines and visibility, and says nothing about material
            // overrides. Half of "copy overrides" is not the feature.
            if (made == null) return false;

            made.DisplayName = name;

            // In place, at the same index, so the target keeps its position in the folder.
            Host.SavedViewpoints.ReplaceWithCopy(parent, index, made);
            return true;
        }

        /// <summary>Find or make the folder chain, and hand back the group to add into.</summary>
        private GroupItem Folder(string? path)
        {
            GroupItem parent = Host.SavedViewpoints.RootItem;

            if (string.IsNullOrWhiteSpace(path)) return parent;

            foreach (var wanted in path!.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(wanted)) continue;

                var name = wanted.Trim();

                var found = Children(parent).OfType<FolderItem>().FirstOrDefault(
                    f => string.Equals(f.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    Host.SavedViewpoints.AddCopy(parent, new FolderItem { DisplayName = name });

                    found = Children(parent).OfType<FolderItem>().LastOrDefault(
                        f => string.Equals(f.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                    // The host refused the folder. The root is a worse answer than the folder asked
                    // for and a much better one than losing the viewpoint.
                    if (found == null) return parent;
                }

                parent = found;
            }

            return parent;
        }

        /// <summary>The group an item sits in, or the root.</summary>
        private GroupItem Parent(SavedItem item)
        {
            try { return item.Parent ?? Host.SavedViewpoints.RootItem; }
            catch (Exception) { return Host.SavedViewpoints.RootItem; }
        }

        /// <summary>Where an item sits in its group, or -1.</summary>
        private static int IndexOf(GroupItem group, SavedItem item)
        {
            var children = Children(group);

            for (var i = 0; i < children.Count; i++)
                if (ReferenceEquals(children[i], item)
                    || string.Equals(children[i].DisplayName, item.DisplayName, StringComparison.Ordinal))
                    return i;

            return -1;
        }

        /// <summary>The children of a group, as a list, and empty rather than null.</summary>
        private static IReadOnlyList<SavedItem> Children(GroupItem? group)
        {
            var items = new List<SavedItem>();

            if (group?.Children == null) return items;

            foreach (SavedItem child in group.Children) items.Add(child);

            return items;
        }

        /// <inheritdoc />
        public bool Delete(string viewId)
        {
            var found = Find(viewId);

            if (found == null)
            {
                _problem = "There is no saved viewpoint at \"" + viewId + "\".";
                return false;
            }

            return Try(() => Host.SavedViewpoints.Remove(found));
        }

        private SavedViewpoint? Find(string viewId)
        {
            if (string.IsNullOrWhiteSpace(viewId)) return null;

            SavedViewpoint? match = null;
            Walk(Host.SavedViewpoints.ToSavedItemCollection(), null, (item, path) =>
            {
                if (match == null && string.Equals(path, viewId, StringComparison.OrdinalIgnoreCase)) match = item;
            });

            return match;
        }

        private static void Collect(SavedItemCollection items, string? folder, List<SavedView> views)
        {
            Walk(items, folder, (item, path) =>
                views.Add(new SavedView(path, item.DisplayName ?? string.Empty, FolderOf(path),
                                        HasOverrides(item),
                                        // Not knowable. See SavedView.HasRedlines for why.
                                        false)));
        }

        /// <summary>
        /// Whether a viewpoint carries appearance state of its own.
        ///
        /// Both kinds count. A viewpoint that only hides things changes what the Appearance stack
        /// says is true just as surely as one that recolours them, and a list that flagged the
        /// second but not the first would let the more common one through unannounced.
        /// </summary>
        private static bool HasOverrides(SavedViewpoint item)
        {
            try { return item.ContainsAppearanceOverrides || item.ContainsVisibilityOverrides; }
            catch (Exception) { return false; }
        }

        private static void Walk(SavedItemCollection items, string? folder, Action<SavedViewpoint, string> visit)
        {
            foreach (SavedItem item in items)
            {
                if (item is SavedViewpoint viewpoint)
                {
                    visit(viewpoint, PathOf(folder, viewpoint.DisplayName ?? string.Empty));
                }
                else if (item is FolderItem nested && nested.Children != null)
                {
                    Walk(nested.Children, PathOf(folder, nested.DisplayName ?? string.Empty), visit);
                }
            }
        }

        private static string PathOf(string? folder, string name) =>
            string.IsNullOrEmpty(folder) ? name : folder + "/" + name;

        private static string? FolderOf(string path)
        {
            var cut = path.LastIndexOf('/');
            return cut < 0 ? null : path.Substring(0, cut);
        }
    }
}
