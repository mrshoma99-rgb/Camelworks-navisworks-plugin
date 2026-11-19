using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Testing
{
    /// <summary>
    /// In-memory <see cref="IViewpointStore"/>, where a viewpoint is a name and what the scene
    /// looks like once you apply it.
    ///
    /// Modelling that is the point. A saved viewpoint is the host's second appearance write
    /// channel, so applying one replaces what is coloured and what is hidden — and a reader of
    /// viewpoints cannot be tested at all against a store that only remembers names.
    ///
    /// One thing is deliberately NOT faithful, because the host's behaviour here is not a fact
    /// about the API: whether <see cref="SaveCurrent"/> captures the current overrides depends on
    /// a Navisworks option ("Save Hide/Required Attributes"), outside any plug-in's control. This
    /// fake captures them, which is the generous case. Nothing in the product may rely on it, and
    /// the viewpoint reader does not: it does not try to put the scene back by saving and
    /// restoring a temporary viewpoint, because on a seat with that option off the restore would
    /// return the camera and leave the scene in the viewpoint's state.
    /// </summary>
    public sealed class FakeViewpointStore : IViewpointStore
    {
        private readonly FakeDocument _document;
        private readonly FakeViewSession _view;
        private readonly List<View> _views = new List<View>();

        /// <summary>Create a store over a document and the session a viewpoint writes through.</summary>
        public FakeViewpointStore(FakeDocument document, FakeViewSession view)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        /// <summary>How many times a viewpoint has been applied, so a test can prove it was.</summary>
        public int Applied { get; private set; }

        /// <summary>
        /// The viewpoint applied most recently, or null before any.
        ///
        /// Not called Current. checks/validate-nullable-args.py learns which member names are
        /// nullable strings from their declarations across the whole repository, and treats a name
        /// it has seen as `string?` and never as plain `string` as nullable everywhere — so a new
        /// `string? Current` here made every unrelated `.Current` in the product look nullable and
        /// failed the check on two files nothing had touched. A distinctive name costs nothing.
        /// </summary>
        public string? LastApplied { get; private set; }

        /// <inheritdoc />
        public string? LastProblem { get; private set; }

        /// <summary>Whether the document refuses viewpoints, as a published NWD does.</summary>
        public bool Refuses { get; set; }

        private Action<string?>? _watcher;

        /// <summary>
        /// Define a viewpoint: the nodes it colours, and the nodes it hides.
        ///
        /// Nodes, named by tree path, and not elements — which is the thing that matters about a
        /// viewpoint and the reason this fake exists. The host records an override against the item
        /// it was applied to, so a storey-wide colour is one record naming the storey. Applying it
        /// then pushes the colour down onto the geometry under that storey, which is why the live
        /// scene keeps no note of the level and a viewpoint does.
        /// </summary>
        /// <param name="name">What it is called.</param>
        /// <param name="colours">The nodes it colours, each a tree path from the model down.</param>
        /// <param name="hiddenBranches">The nodes it hides, each a tree path from the model down.</param>
        public SavedView Define(string name,
                                IReadOnlyList<KeyValuePair<string[], Colour>>? colours = null,
                                IReadOnlyList<string[]>? hiddenBranches = null)
        {
            var view = new View
            {
                Name = name,
                Colours = colours == null
                    ? new List<KeyValuePair<string[], Colour>>()
                    : colours.ToList(),
                Hidden = hiddenBranches == null ? new List<string[]>() : hiddenBranches.ToList(),
            };

            _views.Add(view);
            _document.Record("viewpoint.define " + name);

            return Describe(view);
        }

        /// <inheritdoc />
        public IReadOnlyList<SavedView> All() => _views.Select(Describe).ToList();

        /// <inheritdoc />
        public SavedView? SaveCurrent(string name, string? folder = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a viewpoint needs a name", nameof(name));

            if (Refuses)
            {
                LastProblem = "This document will not keep saved viewpoints.";
                return null;
            }

            LastProblem = null;

            // The generous case; see the type remarks for why nothing may rely on it.
            var view = new View
            {
                Name = name,
                Folder = folder,
                // Only what the leaves carry, because that is all the live scene knows: the
                // level an override was applied at is not recorded on the document. A capture is
                // therefore flatter than a viewpoint somebody built by hand, which is true of the
                // host too.
                Colours = _view.CurrentColours()
                               .Select(c => new KeyValuePair<string[], Colour>(
                                   _document.PathOf(c.Key), c.Value))
                               .Where(c => c.Key.Length > 0)
                               .ToList(),
                Hidden = _document.HiddenBranches(),
            };

            _views.Add(view);
            _document.Record("viewpoint.save " + name);

            return Describe(view);
        }

        /// <inheritdoc />
        public bool Apply(string viewId)
        {
            var found = FindView(viewId);

            if (found == null)
            {
                LastProblem = "There is no saved viewpoint at \"" + viewId + "\".";
                return false;
            }

            LastProblem = null;

            // A viewpoint replaces the scene's appearance rather than adding to it, which is why
            // reading one is destructive and why the reader says so instead of pretending.
            _view.ClearAllOverrides();
            _document.ShowAllBranches();

            // Pushed down onto the geometry under each node, which is what the host does and why
            // the live scene afterwards carries no note of the level it was applied at.
            foreach (var pair in found.Colours)
                foreach (var key in _document.GeometryKeysUnder(pair.Key))
                    _view.SeedForeignColour(key, pair.Value);

            foreach (var branch in found.Hidden) _document.HideBranch(branch);

            Applied++;
            LastApplied = viewId;

            _document.Record("viewpoint.apply " + viewId);

            return true;
        }

        /// <inheritdoc />
        public OriginReport? ReadOrigins(string viewId)
        {
            var found = FindView(viewId);

            if (found == null)
            {
                LastProblem = "There is no saved viewpoint at \"" + viewId + "\".";
                return null;
            }

            LastProblem = null;

            _document.Record("viewpoint.read " + viewId);

            var origins = new List<ScannedOrigin>();
            var node = 0;

            // Hidden first, and hidden branches are nodes, so each is one origin covering whatever
            // sits under it. The host records hiding against the item it was applied to, which is
            // why this is read rather than rolled up.
            foreach (var branch in found.Hidden)
            {
                var item = _document.BranchItem(branch);

                if (item == null) continue;

                origins.Add(new ScannedOrigin(item, new AppearanceOrigin(
                    node++, branch.Length - 1, _document.GeometryUnder(branch),
                    true, null, null, isRead: true)));
            }

            foreach (var pair in found.Colours)
            {
                var item = _document.BranchItem(pair.Key);

                if (item == null) continue;

                origins.Add(new ScannedOrigin(item, new AppearanceOrigin(
                    node++, pair.Key.Length - 1, _document.GeometryUnder(pair.Key),
                    false, pair.Value, null, isRead: true)));
            }

            // Deepest first, as a scan reports them, because that is the order layers go in.
            var ordered = origins.OrderByDescending(o => o.Origin.Depth)
                                 .ThenByDescending(o => o.Origin.Covers)
                                 .ToList();

            // Nothing was walked: a viewpoint's records are sparse and name their own nodes, so
            // there is no element count to report and nothing to have stopped.
            return new OriginReport(ordered, 0, ordered.Count, false);
        }

        /// <inheritdoc />
        public bool Rename(string viewId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("a viewpoint needs a name", nameof(newName));

            var found = FindView(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return false; }

            found.Name = newName;
            LastProblem = null;

            return true;
        }

        /// <inheritdoc />
        public string? MoveTo(string viewId, string? folder)
        {
            var found = FindView(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return null; }

            found.Folder = folder;
            LastProblem = null;

            return Describe(found).Id;
        }

        /// <inheritdoc />
        public bool CopyOverrides(string fromViewId, string toViewId)
        {
            var from = FindView(fromViewId);
            var to = FindView(toViewId);

            if (from == null || to == null) { LastProblem = "No such viewpoint."; return false; }

            to.Colours = from.Colours.ToList();
            to.Hidden = from.Hidden.ToList();
            LastProblem = null;

            return true;
        }

        /// <inheritdoc />
        public bool Delete(string viewId)
        {
            var found = FindView(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return false; }

            _views.Remove(found);
            LastProblem = null;

            return true;
        }

        /// <inheritdoc />
        public string? OnViewpoint => LastApplied;

        /// <inheritdoc />
        public void WatchViewpoint(Action<string?>? onWent) => _watcher = onWent;

        /// <summary>
        /// The user navigating to a viewpoint, as against this product applying one.
        ///
        /// The host tells those apart by suppression: Apply is ours and raises nothing, and a click
        /// in the viewpoint tree raises the event. The fake has to offer both or a watcher cannot
        /// be tested at all — and the distinction is the whole reason the watcher is safe, since a
        /// callback for our own Apply would make auto-applying a loop.
        /// </summary>
        /// <param name="viewId">Which viewpoint the user went to.</param>
        public bool UserWentTo(string viewId)
        {
            if (!Apply(viewId)) return false;

            _watcher?.Invoke(OnViewpoint);

            return true;
        }

        private View? FindView(string viewId) =>
            _views.FirstOrDefault(v => string.Equals(Describe(v).Id, viewId, StringComparison.Ordinal));

        private static SavedView Describe(View view) =>
            new SavedView(view.Folder == null ? view.Name : view.Folder + "/" + view.Name,
                          view.Name, view.Folder,
                          view.Colours.Count > 0 || view.Hidden.Count > 0,
                          false);

        private sealed class View
        {
            internal string Name = string.Empty;
            internal string? Folder;
            internal List<KeyValuePair<string[], Colour>> Colours =
                new List<KeyValuePair<string[], Colour>>();
            internal List<string[]> Hidden = new List<string[]>();
        }
    }
}
