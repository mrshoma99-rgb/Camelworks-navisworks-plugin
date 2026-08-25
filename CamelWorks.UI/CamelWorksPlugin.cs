using System;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.UI
{
    /// <summary>
    /// The CamelWorks ribbon tab and its twenty-seven commands.
    ///
    /// Every button either opens the pane on a named workspace and tab, acts immediately, or opens
    /// a small menu. <b>No button opens a modal that duplicates a pane screen.</b> True modals are
    /// reserved for terminal file operations that need a save path and nothing else, because a
    /// modal over a 3D view hides the very thing the user is deciding about.
    ///
    /// The command ids here must match CamelWorks.xaml exactly. They also appear in
    /// <see cref="CommandCatalogue"/>, which is the one place the routing, the tooltips and the
    /// Find a Tool index come from — a test asserts the two lists agree, because a command present
    /// in one and missing from the other is invisible in exactly one place.
    ///
    /// <b>Icons are Google Material Symbols</b>, Apache-2.0, rasterised into the Resources folder by
    /// <c>tools/icons.py</c> and committed. The tool is in the repository rather than the icons
    /// alone, because a folder of PNGs with no provenance is a folder nobody dares change: it says
    /// which symbol each button uses, where it came from and how to make a matching one.
    /// </summary>
    [Plugin("CamelWorks.Command", "CamelWorks",
        DisplayName = "CamelWorks",
        ToolTip = "Coordination, data and delivery tools for Navisworks — bimcamel.com")]
    [RibbonLayout("CamelWorks.xaml")]
    [RibbonTab("ID_Tab_CamelWorks")]
    [Command("ID_CW_HealthCheck", Icon = "Resources\\HealthCheck_16.png", LargeIcon = "Resources\\HealthCheck_32.png",
        DisplayName = "Health Check", ToolTip = "One scorecard over models, sets and data")]
    [Command("ID_CW_ProjectSetup", Icon = "Resources\\ProjectSetup_16.png", LargeIcon = "Resources\\ProjectSetup_32.png",
        DisplayName = "Project Setup", ToolTip = "What CamelWorks already derived, with an override on each line")]
    [Command("ID_CW_FixLinks", Icon = "Resources\\FixLinks_16.png", LargeIcon = "Resources\\FixLinks_32.png",
        DisplayName = "Fix Broken Links", ToolTip = "Repoint broken paths and find missing sources")]
    [Command("ID_CW_ProjectProfile", Icon = "Resources\\ProjectProfile_16.png", LargeIcon = "Resources\\ProjectProfile_32.png",
        DisplayName = "Project Profile", ToolTip = "Save, load, or start from a template pack")]
    [Command("ID_CW_Triage", Icon = "Resources\\Triage_16.png", LargeIcon = "Resources\\Triage_32.png",
        DisplayName = "Clash Triage", ToolTip = "The board — opens populated and grouped")]
    [Command("ID_CW_Review", Icon = "Resources\\Review_16.png", LargeIcon = "Resources\\Review_32.png",
        DisplayName = "Review", ToolTip = "Review mode over the board's current filter")]
    [Command("ID_CW_ClashTests", Icon = "Resources\\ClashTests_16.png", LargeIcon = "Resources\\ClashTests_32.png",
        DisplayName = "Clash Tests", ToolTip = "The matrix builder and Run")]
    [Command("ID_CW_ClashRules", Icon = "Resources\\ClashRules_16.png", LargeIcon = "Resources\\ClashRules_32.png",
        DisplayName = "Clash Rules", ToolTip = "Suppress and flag, then group, then assign")]
    [Command("ID_CW_Headroom", Icon = "Resources\\Headroom_16.png", LargeIcon = "Resources\\Headroom_32.png",
        DisplayName = "Headroom", ToolTip = "Floor set against target set, onto the board")]
    [Command("ID_CW_ClashReport", Icon = "Resources\\ClashReport_16.png", LargeIcon = "Resources\\ClashReport_32.png",
        DisplayName = "Clash Report", ToolTip = "PDF, XLSX or HTML from a built-in template")]
    [Command("ID_CW_Bcf", Icon = "Resources\\Bcf_16.png", LargeIcon = "Resources\\Bcf_32.png",
        DisplayName = "BCF", ToolTip = "Export and import, BCF 2.1 and 3.0")]
    [Command("ID_CW_Merge", Icon = "Resources\\Merge_16.png", LargeIcon = "Resources\\Merge_32.png",
        DisplayName = "Merge", ToolTip = "One conflict preview for everything inbound")]
    [Command("ID_CW_DataManager", Icon = "Resources\\DataManager_16.png", LargeIcon = "Resources\\DataManager_32.png",
        DisplayName = "Data Manager", ToolTip = "Browse and edit properties in one place")]
    [Command("ID_CW_Excel", Icon = "Resources\\Excel_16.png", LargeIcon = "Resources\\Excel_32.png",
        DisplayName = "Excel", ToolTip = "Properties out, properties back in, or import a sheet")]
    [Command("ID_CW_Zones", Icon = "Resources\\Zones_16.png", LargeIcon = "Resources\\Zones_32.png",
        DisplayName = "Levels & Zones", ToolTip = "Derive levels from whatever the model has")]
    [Command("ID_CW_Takeoff", Icon = "Resources\\Takeoff_16.png", LargeIcon = "Resources\\Takeoff_32.png",
        DisplayName = "Takeoff", ToolTip = "Sum the numeric properties it finds, grouped")]
    [Command("ID_CW_Sets", Icon = "Resources\\Sets_16.png", LargeIcon = "Resources\\Sets_32.png",
        DisplayName = "Sets", ToolTip = "Boolean set building, compiled to native search sets")]
    [Command("ID_CW_Appearance", Icon = "Resources\\Appearance_16.png", LargeIcon = "Resources\\Appearance_32.png",
        DisplayName = "Appearance", ToolTip = "The layers system — what is hidden and overridden, and why")]
    [Command("ID_CW_Viewpoints", Icon = "Resources\\Viewpoints_16.png", LargeIcon = "Resources\\Viewpoints_32.png",
        DisplayName = "Viewpoints", ToolTip = "Bulk rename, renumber, re-folder and render")]
    [Command("ID_CW_SectionBox", Icon = "Resources\\SectionBox_16.png", LargeIcon = "Resources\\SectionBox_32.png",
        DisplayName = "Section Box", ToolTip = "Box the selection, a clash or a group — and toggle without losing it")]
    [Command("ID_CW_Batch", Icon = "Resources\\Batch_16.png", LargeIcon = "Resources\\Batch_32.png",
        DisplayName = "Batch", ToolTip = "Jobs and their run history")]
    [Command("ID_CW_Graph", Icon = "Resources\\Graph_16.png", LargeIcon = "Resources\\Graph_32.png",
        DisplayName = "Graph Editor", ToolTip = "The graph canvas")]
    [Command("ID_CW_ExportIfc", Icon = "Resources\\ExportIfc_16.png", LargeIcon = "Resources\\ExportIfc_32.png",
        DisplayName = "Export IFC", ToolTip = "The IFC exporter, full pane")]
    [Command("ID_CW_Export", Icon = "Resources\\Export_16.png", LargeIcon = "Resources\\Export_32.png",
        DisplayName = "Export", ToolTip = "glTF, GLB, CSV or XLSX")]
    [Command("ID_CW_Compare", Icon = "Resources\\Compare_16.png", LargeIcon = "Resources\\Compare_32.png",
        DisplayName = "Compare", ToolTip = "What changed since a baseline — added, removed, moved, changed")]
    [Command("ID_CW_Baseline", Icon = "Resources\\Baseline_16.png", LargeIcon = "Resources\\Baseline_32.png",
        DisplayName = "Baseline", ToolTip = "Record what the model is right now, to compare against later")]
    [Command("ID_CW_Help", Icon = "Resources\\Help_16.png", LargeIcon = "Resources\\Help_32.png",
        DisplayName = "Help", ToolTip = "Guide, limitations, shortcuts, diagnostics and about")]
    public class CamelWorksPlugin : CommandHandlerPlugin
    {
        /// <summary>
        /// Create the handler.
        ///
        /// Registers the side-by-side assembly resolver — see <see cref="CamelWorksPaneBase"/> for
        /// why both entry points do it.
        /// </summary>
        public CamelWorksPlugin() => Host.Ensure();

        /// <summary>The dock-pane lookup key: "&lt;pluginId&gt;.&lt;developerId&gt;".</summary>
        public const string MainPaneKey = "CamelWorks.Pane.CamelWorks";

        /// <summary>The graph canvas pane's lookup key.</summary>
        public const string AutomatePaneKey = "CamelWorks.AutomatePane.CamelWorks";

        /// <inheritdoc />
        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            var command = CommandCatalogue.Find(commandId);

            if (command == null)
            {
                // A button in the XAML with no entry in the catalogue. Named rather than ignored:
                // silently doing nothing is the one behaviour a user cannot report usefully.
                Warn("CamelWorks has no handler for the command \"" + commandId + "\".\n\n"
                     + "This means the ribbon layout and the command catalogue have got out of step, "
                     + "which is a bug in this build rather than anything you did.");
                return 0;
            }

            try
            {
                var tool = command.Tool;

                if (tool == null)
                {
                    NotYetWired(command);
                    return 0;
                }

                // The tool says what shape it is; the button does not have to know. That is the
                // whole of the change: a button names a tool, and a tool that is a panel takes
                // over the pane while a tool that is a window opens one.
                switch (tool.Shape)
                {
                    case ToolShape.Act:
                        var said = Actions.Run(command.Id);
                        if (said != null) Warn(said);
                        break;

                    case ToolShape.Panel:
                        OpenPane(tool);
                        break;

                    default:
                        var problem = ToolViews.Open(tool.Id, panel => OpenPane(panel));
                        if (problem != null) Warn(problem);
                        break;
                }
            }
            catch (Exception e)
            {
                // A plug-in command that throws takes the host's message loop with it in some
                // versions. Whatever went wrong, it stops here and is shown as text.
                Warn(command.Title + " could not run.\n\n" + e.Message);
            }

            return 0;
        }

        /// <summary>
        /// Whether a command is available right now.
        ///
        /// Every one of these needs a model, so with nothing open they are shown but disabled —
        /// visible, so the tab does not look empty and the user can see what the product offers,
        /// and disabled, because a button that looks available and then says "open a model first"
        /// has wasted the click and taught nothing.
        /// </summary>
        /// <param name="commandId">The command being asked about.</param>
        public override CommandState CanExecuteCommand(string commandId)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            var ready = document != null && !document.IsClear;

            return new CommandState(ready);
        }

        private void OpenPane(Tool? tool)
        {
            if (tool == null) return;

            // The graph canvas is the one tool that needs the full width, so it has a pane of its
            // own; everything else shares the main one.
            var key = string.Equals(tool.Id, "graph", StringComparison.Ordinal)
                ? AutomatePaneKey
                : MainPaneKey;

            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(key);

            if (record == null)
            {
                // Nearly always a DLL built against a different Navisworks year: it loads, its
                // ribbon appears, and its dock pane silently fails to register. Worth saying in
                // full, because the symptom points nowhere near the cause.
                Warn("The CamelWorks panel is not registered with Navisworks (looked up \"" + key
                     + "\" and found nothing).\n\n"
                     + "This almost always means the CamelWorks.UI.dll in this Navisworks year folder was "
                     + "built against a different release. A DLL built for the wrong year still shows its "
                     + "ribbon buttons but its dock pane does not register.\n\n"
                     + "Fix: install the build matching this Navisworks version "
                     + "(2024 = API v21, 2025 = v22, 2026 = v23, 2027 = v24).");
                return;
            }

            if (!(record is DockPanePluginRecord dockRecord))
            {
                Warn("The CamelWorks panel registered as an unexpected plugin type: " + record.GetType().Name + ".");
                return;
            }

            if (!dockRecord.IsLoaded) dockRecord.LoadPlugin();

            if (dockRecord.LoadedPlugin is CamelWorksPaneBase pane)
            {
                pane.Visible = true;
                pane.Show(tool.Id);
            }
        }

        private void NotYetWired(RibbonCommand command)
        {
            // No command reaches this today. It stays because a button added to the ribbon and the
            // catalogue but not to the switch above would otherwise do nothing at all, which reads
            // as a broken product rather than an unfinished one.
            Warn(command.Title + " is not connected in this build yet.\n\n" + command.Tooltip);
        }

        private static void Warn(string message) =>
            MessageBox.Show(message, "CamelWorks", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
