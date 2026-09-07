using System;
using System.Collections.Generic;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Abstractions
{
    /// <summary>
    /// Saved viewpoints, behind the seam.
    ///
    /// Behind this interface because saved viewpoints are a second appearance write channel: a
    /// viewpoint captures overrides, so restoring one changes what the Appearance Manager's stack
    /// says is true. Routing both through seams is what lets the arbitration between them be
    /// written once, in logic, and tested without a host.
    /// </summary>
    public interface IViewpointStore
    {
        /// <summary>Every saved viewpoint, in host order, flattened across folders.</summary>
        IReadOnlyList<SavedView> All();

        /// <summary>
        /// Save the current camera and appearance under a name, returning the new view - or null
        /// when the document would not take it, with <see cref="LastProblem"/> saying why.
        /// </summary>
        SavedView? SaveCurrent(string name, string? folder = null);

        /// <summary>Apply a saved viewpoint. This restores its overrides too — see the type remarks.</summary>
        bool Apply(string viewId);

        /// <summary>Rename a saved viewpoint.</summary>
        bool Rename(string viewId, string newName);

        /// <summary>
        /// Move a saved viewpoint into a folder, making the folder if it is not there.
        /// </summary>
        /// <param name="viewId">The viewpoint's path.</param>
        /// <param name="folder">The folder path, or null to move it to the top.</param>
        /// <returns>The viewpoint's new path, or null when it could not be moved.</returns>
        string? MoveTo(string viewId, string? folder);

        /// <summary>
        /// Give one viewpoint the overrides another one carries, keeping its own camera.
        ///
        /// The host cannot set a saved viewpoint's overrides directly, so this restores the source,
        /// puts the camera back where the target had it, and re-captures the target from that. The
        /// target's redlines are therefore replaced by the source's; the host's own replace does the
        /// same and there is no call that does not. Stated here because redlines cannot be
        /// recovered.
        /// </summary>
        /// <param name="fromViewId">The viewpoint whose overrides are wanted.</param>
        /// <param name="toViewId">The viewpoint to put them on.</param>
        /// <returns>True when it happened.</returns>
        bool CopyOverrides(string fromViewId, string toViewId);

        /// <summary>Delete a saved viewpoint.</summary>
        bool Delete(string viewId);

        /// <summary>
        /// Why the document turned the last write away, or null when it took it.
        ///
        /// Saved viewpoints live in the document, so a published NWD refuses every one of them.
        /// A batch that made forty viewpoints in a file that kept none of them, and said it made
        /// forty, is worse than one that made none: the coordinator finds out next week.
        /// </summary>
        string? LastProblem { get; }
    }

    /// <summary>One saved viewpoint.</summary>
    public readonly struct SavedView
    {
        private readonly string? _id;
        private readonly string? _name;

        /// <summary>Host identity, stable for the life of the document.</summary>
        public string Id => _id ?? string.Empty;

        /// <summary>Display name.</summary>
        public string Name => _name ?? string.Empty;

        /// <summary>Containing folder, or null at the root.</summary>
        public string? Folder { get; }

        /// <summary>True when the viewpoint carries appearance overrides of its own.</summary>
        public bool HasOverrides { get; }

        /// <summary>
        /// True when the host reports native redlines on this viewpoint.
        ///
        /// Always false against Navisworks, and the reason is worth recording where somebody will
        /// look for it: a saved viewpoint's redlines come back as an <c>LcOpRedlineList</c>, which
        /// exposes no public member at all — not a count, not an enumerator. There is no call that
        /// answers "does this viewpoint have redlines on it", so nothing here can claim one does.
        /// The field stays because a host that could answer would fill it in.
        /// </summary>
        public bool HasRedlines { get; }

        /// <summary>Create a descriptor.</summary>
        public SavedView(string id, string name, string? folder, bool hasOverrides, bool hasRedlines)
        {
            _id = id; _name = name; Folder = folder; HasOverrides = hasOverrides; HasRedlines = hasRedlines;
        }

        /// <inheritdoc />
        public override string ToString() => Folder == null ? Name : Folder + "/" + Name;
    }
}
