using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One ribbon command: a button, and the one place it goes.</summary>
    public sealed class RibbonCommand
    {
        internal RibbonCommand(string id, string title, string? toolId, string? appId, string tooltip)
        {
            Id = id;
            Title = title;
            ToolId = toolId;
            AppId = appId;
            Tooltip = tooltip;
        }

        /// <summary>The host command id, matching the ribbon XAML.</summary>
        public string Id { get; }

        /// <summary>How it reads on the button.</summary>
        public string Title { get; }

        /// <summary>The tool it opens, or null for an app button.</summary>
        public string? ToolId { get; }

        /// <summary>The app it opens, or null for a tool button.</summary>
        public string? AppId { get; }

        /// <summary>The tool this opens, or null when it opens an app.</summary>
        public Tool? Tool => Tools.Find(ToolId);

        /// <summary>The tooltip.</summary>
        public string Tooltip { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Every ribbon command, as data: the suite, flat.
    ///
    /// One button per app, at one size, in one row — the shape of the plug-in collections the
    /// client named — plus the one instant act and Help. Thirteen commands. Every button opens
    /// the app it names; nothing on the ribbon opens a menu or somebody else's window, and every
    /// tool that has no button of its own is one tab inside the app that does.
    ///
    /// There was a fourteenth, a CamelWorks button opening a front door: the week, a search, and
    /// a list of the apps whose buttons were already beside it. A door onto a menu, in a row that
    /// is the menu. The week and the trail moved onto Project, where the project file is, and the
    /// search into every app's own title strip.
    ///
    /// One list, because the ribbon handler and the markup would otherwise each carry their own
    /// copy, and a command present in one and missing from the other is invisible in exactly one
    /// place. A check holds the markup, the [Command] attributes and this list to the same set.
    /// </summary>
    public static class CommandCatalogue
    {
        /// <summary>All thirteen, in ribbon order.</summary>
        public static IReadOnlyList<RibbonCommand> All { get; } = new[]
        {
            AppCmd("ID_CW_AppClash", "Clash Manager", "clash",
                "Find what fights, and decide it — tests, results, the matrix, clearance, "
                + "filters, and the ways a board leaves and comes back."),

            AppCmd("ID_CW_AppIssueManager", "Issue Manager", "issuemgr",
                "Every issue on this job in one register: raised by hand, from clash results, "
                + "from the clearance check, or imported from BCF."),

            AppCmd("ID_CW_AppAudit", "Model Audit", "audit",
                "Whether this federation is fit to coordinate against — health, IDS, the model "
                + "register, broken links and the project profile."),

            AppCmd("ID_CW_AppTrack", "Compare Track", "track",
                "What changed, against what, since when — compare, changes, the change notice "
                + "and the history."),

            AppCmd("ID_CW_AppData", "Data Manager", "data",
                "What the elements carry and the pen to fix it — properties, writing, Excel, "
                + "external data, levels and the breakdown."),

            AppCmd("ID_CW_AppQuantify", "Quantify", "quantify",
                "Numbers out of the model — takeoff, the report builder and the data export."),

            AppCmd("ID_CW_AppSets", "Sets Manager", "sets",
                "The selections everything else works on — the builder, schedules, the library "
                + "generator and reorganising."),

            AppCmd("ID_CW_AppScenes", "Scene Kit", "scenes",
                "What is visible and what colour it is — the appearance stack and the "
                + "viewpoints."),

            AppCmd("ID_CW_AppExports", "Export Kit", "exports",
                "The model as a file for other applications: IFC, GLB, a drawing, an extract or "
                + "a compiled deliverable."),

            AppCmd("ID_CW_AppSite", "Site Pack", "site",
                "The model reaching the people building it — setting out, dimensions, weight, "
                + "labels, penetrations and documents."),

            AppCmd("ID_CW_AppAutomate", "Automate", "automate",
                "Doing all of it again next Friday — saved jobs and the graph canvas."),

            Cmd("ID_CW_SectionBox", "Section Box", "sectionbox",
                "Boxes whatever is selected. With nothing selected it switches sectioning off "
                + "and back on without losing the box."),

            Cmd("ID_CW_Help", "Help", "help",
                "The guide, what this cannot do, the shortcuts, a sample project and the "
                + "diagnostics."),
        };

        /// <summary>Look one up by its host command id.</summary>
        /// <param name="commandId">The command id.</param>
        public static RibbonCommand? Find(string? commandId) =>
            All.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.OrdinalIgnoreCase));

        private static RibbonCommand Cmd(string id, string title, string toolId, string tooltip) =>
            new RibbonCommand(id, title, toolId, null, tooltip);

        private static RibbonCommand AppCmd(string id, string title, string appId, string tooltip) =>
            new RibbonCommand(id, title, null, appId, tooltip);
    }
}
