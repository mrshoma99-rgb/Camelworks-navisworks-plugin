using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One ribbon command: a button, and the one place it goes.</summary>
    public sealed class RibbonCommand
    {
        internal RibbonCommand(string id, string title, string? toolId, string? deskId, string tooltip)
        {
            Id = id;
            Title = title;
            ToolId = toolId;
            DeskId = deskId;
            Tooltip = tooltip;
        }

        /// <summary>The host command id, matching the ribbon XAML.</summary>
        public string Id { get; }

        /// <summary>How it reads on the button.</summary>
        public string Title { get; }

        /// <summary>The tool it opens, or null for a desk button and for Home.</summary>
        public string? ToolId { get; }

        /// <summary>The desk it opens, or null for a tool button and for Home.</summary>
        public string? DeskId { get; }

        /// <summary>The tool this opens, or null when it opens a desk or Home.</summary>
        public Tool? Tool => Tools.Find(ToolId);

        /// <summary>The tooltip.</summary>
        public string Tooltip { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Every ribbon command, as data.
    ///
    /// Twenty-two buttons where there were thirty-seven: Home, Help, one button per desk, and the
    /// daily tools as direct entries that land inside their desk with the tool already on the
    /// bench. Every other tool is one desk-click away and findable by <see cref="Finder"/> under
    /// the words somebody would actually use.
    ///
    /// One list, because the ribbon handler and the markup would otherwise each carry their own
    /// copy, and a command present in one and missing from the other is invisible in exactly one
    /// place — which is the hardest kind of gap to notice. A check holds the markup, the
    /// [Command] attributes and this list to the same set.
    /// </summary>
    public static class CommandCatalogue
    {
        /// <summary>All twenty-two, in ribbon order.</summary>
        public static IReadOnlyList<RibbonCommand> All { get; } = new[]
        {
            // Panel — CamelWorks
            Home("ID_CW_Home", "Home",
                "The week, the five desks, what has happened lately, and the tool search."),

            Cmd("ID_CW_Help", "Help", "help",
                "The guide, what this cannot do, the shortcuts, a sample project and the "
                + "diagnostics."),

            // Panel — Federation
            Desk("ID_CW_DeskFederation", "Federation", "federation",
                "Bring the models in, and know whether to trust them — every arrival, trust and "
                + "tracking tool on one desk."),

            Cmd("ID_CW_HealthCheck", "Health Check", "health",
                "Whether this federation is fit to coordinate against, before anybody spends a "
                + "week finding out it was not."),

            Cmd("ID_CW_Compare", "Compare Revisions", "compare",
                "What changed against a baseline: added, removed, moved, retyped — and how it "
                + "knows."),

            Cmd("ID_CW_Baseline", "Baseline", "baseline",
                "Record what the model is right now, so next week has something to compare "
                + "against."),

            // Panel — Coordination
            Desk("ID_CW_DeskCoordination", "Coordination", "coordination",
                "Find what fights, decide it, and send it — the whole clash week on one desk."),

            Cmd("ID_CW_Board", "Clash Board", "triage",
                "Every result the tests found, worked down to the issues somebody fixes — and "
                + "your decisions survive the next run."),

            Cmd("ID_CW_ClashTests", "Clash Tests", "tests",
                "What gets tested against what, and how close is too close."),

            Cmd("ID_CW_Review", "Review", "review",
                "One issue at a time in front of the geometry, both hands on the keyboard."),

            Cmd("ID_CW_ClashReport", "Clash Report", "clashreport",
                "The board as a document somebody without Navisworks can read."),

            // Panel — Data
            Desk("ID_CW_DeskData", "Data", "data",
                "What the elements carry and the pen to fix it — reading, writing and reporting "
                + "properties on one desk."),

            Cmd("ID_CW_DataManager", "Data Manager", "data",
                "Every property the elements carry, in one grid — and the pen to fix what is "
                + "missing."),

            Cmd("ID_CW_Takeoff", "Takeoff", "takeoff",
                "The quantities the model carries, summed how a bill wants them."),

            // Panel — Scenes
            Desk("ID_CW_DeskScenes", "Scenes", "scenes",
                "What is selected, what is visible, and what colour it is — sets, appearance and "
                + "viewpoints on one desk."),

            Cmd("ID_CW_Sets", "Set Builder", "sets",
                "The selections everything else works on, said in plain rows."),

            Cmd("ID_CW_Appearance", "Appearance", "appearance",
                "What is coloured, what is hidden, and which layer did it — stacked, and every "
                + "layer takes back off."),

            Cmd("ID_CW_SectionBox", "Section Box", "sectionbox",
                "Boxes whatever is selected. With nothing selected it switches sectioning off "
                + "and back on without losing the box."),

            // Panel — Deliver
            Desk("ID_CW_DeskDeliver", "Deliver", "deliver",
                "The model leaving the building — cuts, files, the site pack and the batch jobs "
                + "on one desk."),

            Cmd("ID_CW_ExportIfc", "Export IFC", "ifc",
                "The model as IFC, with the mapping and the classification it should carry."),

            Cmd("ID_CW_Compile", "Compile", "compile",
                "A folder of supplier drops appended into one deliverable, newest revision of "
                + "each, with a log of what went in."),

            Cmd("ID_CW_Batch", "Batch", "batch",
                "A saved list of jobs run in order over the document that is open, in one press."),
        };

        /// <summary>Look one up by its host command id.</summary>
        /// <param name="commandId">The command id.</param>
        public static RibbonCommand? Find(string? commandId) =>
            All.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.OrdinalIgnoreCase));

        private static RibbonCommand Cmd(string id, string title, string toolId, string tooltip) =>
            new RibbonCommand(id, title, toolId, null, tooltip);

        private static RibbonCommand Desk(string id, string title, string deskId, string tooltip) =>
            new RibbonCommand(id, title, null, deskId, tooltip);

        private static RibbonCommand Home(string id, string title, string tooltip) =>
            new RibbonCommand(id, title, null, null, tooltip);
    }
}
