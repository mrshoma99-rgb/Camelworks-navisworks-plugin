using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One stage of a desk's job: a small-caps heading over the tools that do it.</summary>
    public sealed class DeskStage
    {
        internal DeskStage(string title, params string[] toolIds)
        {
            Title = title;
            ToolIds = toolIds;
        }

        /// <summary>The stage's heading, printed in small caps on the rail.</summary>
        public string Title { get; }

        /// <summary>The tools that do this stage, in rail order.</summary>
        public IReadOnlyList<string> ToolIds { get; }
    }

    /// <summary>
    /// One desk: a job a coordinator has, and every tool that belongs to it.
    /// </summary>
    public sealed class Desk
    {
        internal Desk(string id, string title, string job, string family, params DeskStage[] stages)
        {
            Id = id;
            Title = title;
            Job = job;
            Family = family;
            Stages = stages;
        }

        /// <summary>What the code calls it.</summary>
        public string Id { get; }

        /// <summary>What the ribbon button and the title bar call it.</summary>
        public string Title { get; }

        /// <summary>The job, in one sentence, shown under the desk's name.</summary>
        public string Job { get; }

        /// <summary>The skin family its rail markers are tinted with, such as <c>Coordinate</c>.</summary>
        public string Family { get; }

        /// <summary>The stages of the job, in the order the rail prints them.</summary>
        public IReadOnlyList<DeskStage> Stages { get; }

        /// <summary>Every tool on this desk, in rail order.</summary>
        public IEnumerable<Tool> Tools =>
            Stages.SelectMany(s => s.ToolIds).Select(Shell.Tools.Find).Where(t => t != null)!;

        /// <inheritdoc />
        public override string ToString() => Title + " — " + Job;
    }

    /// <summary>
    /// The five desks, and which one each tool sits on.
    ///
    /// <b>This is the third shape of the product and the first one organised by the job.</b> The
    /// first design was one pane holding seven workspaces and eighteen tabs; the second was a
    /// ribbon of thirty-seven buttons opening forty-two separate windows. Both treated every tool
    /// as a peer of every other, which is how a coordinator ends up reading a list of forty-two
    /// nouns to find the three they use on a Monday.
    ///
    /// A coordinator has five jobs, not forty-two: bring the models in and trust them, find what
    /// fights and decide it, fix what the elements carry, control what is selected and seen, and
    /// get the model out of the building. Each is a desk — one window, every tool of that job on
    /// a permanent rail, nothing folded. `CAMELWORKS_DESIGN.md` is the law this file implements.
    ///
    /// Tools keep their identity from <see cref="Shell.Tools"/> — a desk decides where a tool
    /// appears, never what it is.
    /// </summary>
    public static class Desks
    {
        /// <summary>The five desks, in ribbon order.</summary>
        public static IReadOnlyList<Desk> All { get; } = new[]
        {
            new Desk("federation", "Federation",
                "Bring the models in, and know whether to trust them.", "Project",
                new DeskStage("Arrive", "models", "links"),
                new DeskStage("Trust", "health", "ids"),
                new DeskStage("Track", "baseline", "compare", "changes", "notice", "history"),
                new DeskStage("Project", "project")),

            new Desk("coordination", "Coordination",
                "Find what fights, decide it, and send it to whoever fixes it.", "Coordinate",
                new DeskStage("Find", "tests", "matrix", "headroom"),
                new DeskStage("Refine", "rules"),
                new DeskStage("Decide", "triage", "review"),
                new DeskStage("Send", "clashreport", "bcf", "clashxml", "penetrations"),
                new DeskStage("Return", "merge"),
                new DeskStage("Record", "freeze")),

            new Desk("data", "Data",
                "What the elements carry, and the pen to fix what is missing.", "Data",
                new DeskStage("Read", "data"),
                new DeskStage("Write", "write", "excel", "datalink", "levels", "wbs"),
                new DeskStage("Out", "takeoff", "report", "exportsheet")),

            new Desk("scenes", "Scenes",
                "What is selected, what is visible, and what colour it is.", "Sets",
                new DeskStage("Select", "sets", "schedule", "setlib", "restructure"),
                new DeskStage("Show", "appearance", "viewpoints", "sectionbox")),

            new Desk("deliver", "Deliver",
                "The model leaving the building, in whatever shape the receiver needs.", "Batch",
                new DeskStage("Build", "compile", "extract"),
                new DeskStage("Files", "ifc", "exportglb", "exportdxf"),
                new DeskStage("Site", "setout", "dimensions", "weight", "labels", "paperwork"),
                new DeskStage("Again", "batch", "graph")),
        };

        /// <summary>Find a desk by id.</summary>
        /// <param name="deskId">The desk's id.</param>
        public static Desk? Find(string? deskId) =>
            deskId == null ? null : All.FirstOrDefault(
                d => string.Equals(d.Id, deskId, StringComparison.OrdinalIgnoreCase));

        /// <summary>The desk a tool sits on, or null for the two that sit on none — Review's palette host and Help.</summary>
        /// <param name="toolId">The tool's id.</param>
        public static Desk? Of(string? toolId)
        {
            if (toolId == null) return null;

            return All.FirstOrDefault(d => d.Stages.Any(
                s => s.ToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase)));
        }
    }
}
