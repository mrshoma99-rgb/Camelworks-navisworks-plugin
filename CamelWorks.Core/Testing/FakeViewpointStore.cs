using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
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

        /// <summary>
        /// Define a viewpoint and what applying it does to the scene.
        /// </summary>
        /// <param name="name">What it is called.</param>
        /// <param name="colours">Elements it colours.</param>
        /// <param name="hiddenBranches">Branches it hides, each a tree path from the model down.</param>
        public SavedView Define(string name,
                                IReadOnlyDictionary<ElementKey, Colour>? colours = null,
                                IReadOnlyList<string[]>? hiddenBranches = null)
        {
            var view = new View
            {
                Name = name,
                Colours = colours == null
                    ? new Dictionary<ElementKey, Colour>()
                    : new Dictionary<ElementKey, Colour>(colours.ToDictionary(p => p.Key, p => p.Value)),
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
                Colours = _view.CurrentColours(),
                Hidden = _document.HiddenBranches(),
            };

            _views.Add(view);
            _document.Record("viewpoint.save " + name);

            return Describe(view);
        }

        /// <inheritdoc />
        public bool Apply(string viewId)
        {
            var found = Find(viewId);

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

            foreach (var pair in found.Colours) _view.SeedForeignColour(pair.Key, pair.Value);

            foreach (var branch in found.Hidden) _document.HideBranch(branch);

            Applied++;
            LastApplied = viewId;

            _document.Record("viewpoint.apply " + viewId);

            return true;
        }

        /// <inheritdoc />
        public bool Rename(string viewId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("a viewpoint needs a name", nameof(newName));

            var found = Find(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return false; }

            found.Name = newName;
            LastProblem = null;

            return true;
        }

        /// <inheritdoc />
        public string? MoveTo(string viewId, string? folder)
        {
            var found = Find(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return null; }

            found.Folder = folder;
            LastProblem = null;

            return Describe(found).Id;
        }

        /// <inheritdoc />
        public bool CopyOverrides(string fromViewId, string toViewId)
        {
            var from = Find(fromViewId);
            var to = Find(toViewId);

            if (from == null || to == null) { LastProblem = "No such viewpoint."; return false; }

            to.Colours = new Dictionary<ElementKey, Colour>(from.Colours);
            to.Hidden = from.Hidden.ToList();
            LastProblem = null;

            return true;
        }

        /// <inheritdoc />
        public bool Delete(string viewId)
        {
            var found = Find(viewId);

            if (found == null) { LastProblem = "No such viewpoint."; return false; }

            _views.Remove(found);
            LastProblem = null;

            return true;
        }

        private View? Find(string viewId) =>
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
            internal Dictionary<ElementKey, Colour> Colours = new Dictionary<ElementKey, Colour>();
            internal List<string[]> Hidden = new List<string[]>();
        }
    }
}
