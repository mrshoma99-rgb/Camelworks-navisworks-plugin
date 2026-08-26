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
        DisplayName = "Health Check", ToolTip = "Is this federation fit to coordinate against?")]
    [Command("ID_CW_ProjectSettings", Icon = "Resources\\ProjectSettings_16.png", LargeIcon = "Resources\\ProjectSettings_32.png",
        DisplayName = "Project Settings", ToolTip = "What CamelWorks worked out, and an override on every line")]
    [Command("ID_CW_FixLinks", Icon = "Resources\\FixLinks_16.png", LargeIcon = "Resources\\FixLinks_32.png",
        DisplayName = "Fix Broken Links", ToolTip = "Repoint broken paths and find missing sources")]
    [Command("ID_CW_Help", Icon = "Resources\\Help_16.png", LargeIcon = "Resources\\Help_32.png",
        DisplayName = "Help", ToolTip = "Guide, limitations, shortcuts, diagnostics and about")]
    [Command("ID_CW_Board", Icon = "Resources\\Board_16.png", LargeIcon = "Resources\\Board_32.png",
        DisplayName = "Clash Board", ToolTip = "Results worked down to owned issues that survive the next run")]
    [Command("ID_CW_Review", Icon = "Resources\\Review_16.png", LargeIcon = "Resources\\Review_32.png",
        DisplayName = "Review", ToolTip = "One issue at a time over the model, on the keyboard")]
    [Command("ID_CW_ClashTests", Icon = "Resources\\ClashTests_16.png", LargeIcon = "Resources\\ClashTests_32.png",
        DisplayName = "Clash Tests", ToolTip = "What clashes against what — including from their matrix sheet")]
    [Command("ID_CW_ClashRules", Icon = "Resources\\ClashRules_16.png", LargeIcon = "Resources\\ClashRules_32.png",
        DisplayName = "Clash Rules", ToolTip = "Suppress, group and assign, previewed before it applies")]
    [Command("ID_CW_Clearance", Icon = "Resources\\Clearance_16.png", LargeIcon = "Resources\\Clearance_32.png",
        DisplayName = "Clearance", ToolTip = "Clear height where nothing is touching")]
    [Command("ID_CW_ClashReport", Icon = "Resources\\ClashReport_16.png", LargeIcon = "Resources\\ClashReport_32.png",
        DisplayName = "Clash Report", ToolTip = "The board as a document — or the penetration schedule")]
    [Command("ID_CW_Bcf", Icon = "Resources\\Bcf_16.png", LargeIcon = "Resources\\Bcf_32.png",
        DisplayName = "BCF", ToolTip = "Issues out and answers back, BCF 2.1 or 3.0")]
    [Command("ID_CW_Merge", Icon = "Resources\\Merge_16.png", LargeIcon = "Resources\\Merge_32.png",
        DisplayName = "Merge Markups", ToolTip = "Several reviewers, one board, disagreements shown")]
    [Command("ID_CW_ClashXml", Icon = "Resources\\ClashXml_16.png", LargeIcon = "Resources\\ClashXml_32.png",
        DisplayName = "Clash XML", ToolTip = "The interchange format other tools speak, in or out")]
    [Command("ID_CW_Compare", Icon = "Resources\\Compare_16.png", LargeIcon = "Resources\\Compare_32.png",
        DisplayName = "Compare Revisions", ToolTip = "What changed against a baseline, and how it knows")]
    [Command("ID_CW_Changes", Icon = "Resources\\Changes_16.png", LargeIcon = "Resources\\Changes_32.png",
        DisplayName = "Changes", ToolTip = "The change list walked on the model")]
    [Command("ID_CW_Baseline", Icon = "Resources\\Baseline_16.png", LargeIcon = "Resources\\Baseline_32.png",
        DisplayName = "Baseline", ToolTip = "Record what the model is now — or freeze an agreed scope")]
    [Command("ID_CW_ChangeNotice", Icon = "Resources\\ChangeNotice_16.png", LargeIcon = "Resources\\ChangeNotice_32.png",
        DisplayName = "Change Notice", ToolTip = "What changed since the scope was frozen")]
    [Command("ID_CW_DataManager", Icon = "Resources\\DataManager_16.png", LargeIcon = "Resources\\DataManager_32.png",
        DisplayName = "Data Manager", ToolTip = "Every property in one grid, and the pen to fix it")]
    [Command("ID_CW_Zones", Icon = "Resources\\Zones_16.png", LargeIcon = "Resources\\Zones_32.png",
        DisplayName = "Levels and Zones", ToolTip = "Level and zone per element, from what the model carries")]
    [Command("ID_CW_Takeoff", Icon = "Resources\\Takeoff_16.png", LargeIcon = "Resources\\Takeoff_32.png",
        DisplayName = "Takeoff", ToolTip = "Quantities summed how a bill wants them")]
    [Command("ID_CW_Wbs", Icon = "Resources\\Wbs_16.png", LargeIcon = "Resources\\Wbs_32.png",
        DisplayName = "Work Breakdown", ToolTip = "The tree's own shape, written on as properties")]
    [Command("ID_CW_Ids", Icon = "Resources\\Ids_16.png", LargeIcon = "Resources\\Ids_32.png",
        DisplayName = "IDS", ToolTip = "What models must carry, written and checked")]
    [Command("ID_CW_Sets", Icon = "Resources\\Sets_16.png", LargeIcon = "Resources\\Sets_32.png",
        DisplayName = "Set Builder", ToolTip = "The selections everything else works on")]
    [Command("ID_CW_Appearance", Icon = "Resources\\Appearance_16.png", LargeIcon = "Resources\\Appearance_32.png",
        DisplayName = "Appearance", ToolTip = "What is coloured, what is hidden, and which layer did it")]
    [Command("ID_CW_Viewpoints", Icon = "Resources\\Viewpoints_16.png", LargeIcon = "Resources\\Viewpoints_32.png",
        DisplayName = "Viewpoints", ToolTip = "Rename, renumber, re-folder and re-camera, in bulk")]
    [Command("ID_CW_SectionBox", Icon = "Resources\\SectionBox_16.png", LargeIcon = "Resources\\SectionBox_32.png",
        DisplayName = "Section Box", ToolTip = "Box the selection, or switch sectioning off")]
    [Command("ID_CW_Export", Icon = "Resources\\Export_16.png", LargeIcon = "Resources\\Export_32.png",
        DisplayName = "Export", ToolTip = "Geometry as GLB, properties as a sheet, plan as DXF")]
    [Command("ID_CW_ExportIfc", Icon = "Resources\\ExportIfc_16.png", LargeIcon = "Resources\\ExportIfc_32.png",
        DisplayName = "Export IFC", ToolTip = "IFC4 or 2x3, with mapping and classification")]
    [Command("ID_CW_Extract", Icon = "Resources\\Extract_16.png", LargeIcon = "Resources\\Extract_32.png",
        DisplayName = "Extract", ToolTip = "A level, zone or room, out as something others can open")]
    [Command("ID_CW_Compile", Icon = "Resources\\Compile_16.png", LargeIcon = "Resources\\Compile_32.png",
        DisplayName = "Compile", ToolTip = "Supplier drops appended newest-first, with the log")]
    [Command("ID_CW_SetOut", Icon = "Resources\\SetOut_16.png", LargeIcon = "Resources\\SetOut_32.png",
        DisplayName = "Setting Out", ToolTip = "Points a total station can walk, in site coordinates")]
    [Command("ID_CW_Dimensions", Icon = "Resources\\Dimensions_16.png", LargeIcon = "Resources\\Dimensions_32.png",
        DisplayName = "Dimensions", ToolTip = "Lengths, offsets and centres, for everything at once")]
    [Command("ID_CW_Weight", Icon = "Resources\\Weight_16.png", LargeIcon = "Resources\\Weight_32.png",
        DisplayName = "Centre of Gravity", ToolTip = "Where the module's weight sits, so the hook is right")]
    [Command("ID_CW_Labels", Icon = "Resources\\Labels_16.png", LargeIcon = "Resources\\Labels_32.png",
        DisplayName = "Site Labels", ToolTip = "Stickers with a code and the identifier underneath")]
    [Command("ID_CW_Paperwork", Icon = "Resources\\Paperwork_16.png", LargeIcon = "Resources\\Paperwork_32.png",
        DisplayName = "Documents and Photos", ToolTip = "What the model already points at, found and checked")]
    [Command("ID_CW_Batch", Icon = "Resources\\Batch_16.png", LargeIcon = "Resources\\Batch_32.png",
        DisplayName = "Batch", ToolTip = "A list of jobs, run in order, unattended")]
    [Command("ID_CW_Graph", Icon = "Resources\\Graph_16.png", LargeIcon = "Resources\\Graph_32.png",
        DisplayName = "Graph Editor", ToolTip = "The same jobs as a graph, when a list is not enough")]
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
