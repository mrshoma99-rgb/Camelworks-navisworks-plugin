using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Echoing a decision into the host document, for a check whose results live there too.
    ///
    /// Every edit is written to the findings register first and unconditionally; the echo is
    /// attempted afterwards where a check offers one. A refused echo marks the field pending on the
    /// finding and the board says which field is waiting, because the database is the record and
    /// the document is a projection of it.
    ///
    /// The methods take findings rather than host ids. The host handle a check needs is its own —
    /// clash reads it back off the finding's facts — so the seam never names a host id and a check
    /// with a different handle shape needs no new signature.
    ///
    /// Renaming a run or a result is not here. Those are edits to the record, they are kept whether
    /// or not the document takes them, and they belong on the register path.
    /// </summary>
    public interface ICheckHostEcho
    {
        /// <summary>False when the document will take no edit at all — a published file, a read-only open.</summary>
        bool CanEdit { get; }

        /// <summary>Why the last echo did not land, or null.</summary>
        string? LastProblem { get; }

        /// <summary>Echo a status.</summary>
        /// <param name="findings">The findings.</param>
        /// <param name="status">The status they now hold.</param>
        bool SetStatus(IReadOnlyList<Finding> findings, FindingStatus status);

        /// <summary>Echo an assignment.</summary>
        /// <param name="findings">The findings.</param>
        /// <param name="who">Who they are now with.</param>
        bool Assign(IReadOnlyList<Finding> findings, string who);

        /// <summary>Echo a remark.</summary>
        /// <param name="findings">The findings.</param>
        /// <param name="body">What was written.</param>
        /// <param name="author">Who wrote it.</param>
        bool Comment(IReadOnlyList<Finding> findings, string body, string author);

        /// <summary>Echo a grouping.</summary>
        /// <param name="findings">The findings.</param>
        /// <param name="groupName">The group, or null to take them out of one.</param>
        bool SetGroup(IReadOnlyList<Finding> findings, string? groupName);

        /// <summary>Echo a retitle.</summary>
        /// <param name="findings">The findings.</param>
        /// <param name="titles">The new titles, one per finding and in the same order.</param>
        bool SetTitle(IReadOnlyList<Finding> findings, IReadOnlyList<string> titles);

        /// <summary>The remarks the host holds against one finding.</summary>
        /// <param name="finding">The finding.</param>
        IReadOnlyList<FindingComment> CommentsOn(Finding finding);

        /// <summary>Reword a remark the host holds.</summary>
        /// <param name="finding">The finding it is on.</param>
        /// <param name="commentId">The host's id for the remark.</param>
        /// <param name="body">The new wording.</param>
        bool EditComment(Finding finding, long commentId, string body);

        /// <summary>Delete a remark the host holds.</summary>
        /// <param name="finding">The finding it is on.</param>
        /// <param name="commentId">The host's id for the remark.</param>
        bool DeleteComment(Finding finding, long commentId);
    }

    /// <summary>
    /// Nesting results into the host document's own group structure.
    ///
    /// Its own capability rather than part of <see cref="ICheckHostEcho"/>, because only clash has
    /// host groups: six checks return null here, and an echo whose signature named clash types
    /// would have kept a clash-shaped branch on the board for every one of them.
    /// </summary>
    public interface ICheckGrouping
    {
        /// <summary>Put results where the trails say, and report what the document actually did.</summary>
        /// <param name="paths">Each trail of group names and the results that belong at the end of it.</param>
        ClashNesting Regroup(IReadOnlyList<ClashGroupPath> paths);

        /// <summary>Whether a group made by hand can be pinned so a re-group does not re-derive it.</summary>
        bool CanPin { get; }
    }

    /// <summary>Running a run again from the tab that draws it.</summary>
    public interface ICheckRunner
    {
        /// <summary>False when nothing here can be run — a file-read board, a seat with no engine.</summary>
        bool CanRun { get; }

        /// <summary>Why not, in one sentence, or null when it can.</summary>
        string? CannotRun { get; }

        /// <summary>Run one run again.</summary>
        /// <param name="runId">Which run.</param>
        /// <param name="onProgress">Called with how far it has got; returning false stops it. Null does not report.</param>
        bool Run(string runId, Func<int, bool>? onProgress);

        /// <summary>Delete runs and whatever they hold.</summary>
        /// <param name="runIds">Which runs.</param>
        bool Delete(IReadOnlyList<string> runIds);

        /// <summary>Drop the results of runs, keeping the runs themselves.</summary>
        /// <param name="runIds">Which runs.</param>
        bool Compact(IReadOnlyList<string> runIds);
    }

    /// <summary>Renaming and refiling runs.</summary>
    public interface ICheckNaming
    {
        /// <summary>Call a run something.</summary>
        /// <param name="runId">Which run.</param>
        /// <param name="name">The name.</param>
        bool Rename(string runId, string name);

        /// <summary>File a run in a folder, or take it out of one.</summary>
        /// <param name="runId">Which run.</param>
        /// <param name="folder">The folder, or null for the root.</param>
        bool Refile(string runId, string? folder);

        /// <summary>
        /// True when this check has a naming window of its own. Only the clash matrix has one.
        ///
        /// A fact rather than the window itself. This assembly targets netstandard2.0 and a test
        /// asserts it references only the framework, so no type here can name a WPF element; the
        /// board looks the window up on the UI side, keyed on <see cref="ICheck.Key"/>.
        /// </summary>
        bool HasBook { get; }
    }

    /// <summary>Focus, isolate, viewpoints and snapshots, for a check whose findings are somewhere.</summary>
    public interface ICheckGeometry
    {
        /// <summary>
        /// Select what the producer already holds, and say how many elements it reached. Null when
        /// it holds nothing and the caller must fall back to <see cref="Locate"/>.
        /// </summary>
        /// <param name="findings">The findings.</param>
        /// <param name="add">True to add to the selection rather than replace it.</param>
        int? Select(IReadOnlyList<Finding> findings, bool add);

        /// <summary>Where each finding's elements are, for a caller that has to find them itself.</summary>
        /// <param name="findings">The findings.</param>
        IReadOnlyList<ElementLocator> Locate(IReadOnlyList<Finding> findings);

        /// <summary>Point the camera at one finding the way its producer saw it.</summary>
        /// <param name="finding">The finding.</param>
        bool ApplyView(Finding finding);

        /// <summary>An image of one finding, or null when none could be taken.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        byte[]? Snapshot(Finding finding, int width, int height);
    }

    /// <summary>Element properties, for the columns a person adds to a check's results themselves.</summary>
    public interface ICheckProperties
    {
        /// <summary>What one element of one finding says under a property.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="elementIndex">Which of its elements.</param>
        /// <param name="category">The property category.</param>
        /// <param name="name">The property name.</param>
        IReadOnlyList<PropertyValue> ValuesFor(Finding finding, int elementIndex,
                                               string category, string name);
    }
}
