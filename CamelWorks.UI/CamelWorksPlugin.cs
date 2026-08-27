using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.UI
{
    /// <summary>
    /// The CamelWorks ribbon tab and its twenty-two commands.
    ///
    /// A button goes to one of three places, decided by the catalogue entry: Home brings up the
    /// dock pane at rest, a desk button opens that desk's window, and a tool button sends the
    /// tool where it lives — its desk's bench, the dock pane for the beside-the-model tools, or
    /// its own window for Review and Help. No button opens a modal, because a modal over a 3D
    /// view hides the very thing the user is deciding about.
    ///
    /// The command ids here must match CamelWorks.xaml exactly. They also appear in
    /// <see cref="CommandCatalogue"/>, which is the one place the routing and the tooltips come
    /// from — a check asserts the three lists agree, because a command present in one and missing
    /// from another is invisible in exactly one place.
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
    [Command("ID_CW_Home", Icon = "Resources\\Home_16.png", LargeIcon = "Resources\\Home_32.png",
        DisplayName = "Home", ToolTip = "The week, the desks, and the tool search")]
    [Command("ID_CW_Help", Icon = "Resources\\Help_16.png", LargeIcon = "Resources\\Help_32.png",
        DisplayName = "Help", ToolTip = "Guide, limitations, shortcuts, diagnostics and about")]
    [Command("ID_CW_DeskFederation", Icon = "Resources\\DeskFederation_16.png", LargeIcon = "Resources\\DeskFederation_32.png",
        DisplayName = "Federation", ToolTip = "Bring the models in, and know whether to trust them")]
    [Command("ID_CW_HealthCheck", Icon = "Resources\\HealthCheck_16.png", LargeIcon = "Resources\\HealthCheck_32.png",
        DisplayName = "Health Check", ToolTip = "Is this federation fit to coordinate against?")]
    [Command("ID_CW_Compare", Icon = "Resources\\Compare_16.png", LargeIcon = "Resources\\Compare_32.png",
        DisplayName = "Compare Revisions", ToolTip = "What changed against a baseline, and how it knows")]
    [Command("ID_CW_Baseline", Icon = "Resources\\Baseline_16.png", LargeIcon = "Resources\\Baseline_32.png",
        DisplayName = "Baseline", ToolTip = "Record what the model is now, in one press")]
    [Command("ID_CW_DeskCoordination", Icon = "Resources\\DeskCoordination_16.png", LargeIcon = "Resources\\DeskCoordination_32.png",
        DisplayName = "Coordination", ToolTip = "Find what fights, decide it, and send it")]
    [Command("ID_CW_Board", Icon = "Resources\\Board_16.png", LargeIcon = "Resources\\Board_32.png",
        DisplayName = "Clash Board", ToolTip = "Results worked down to owned issues that survive the next run")]
    [Command("ID_CW_ClashTests", Icon = "Resources\\ClashTests_16.png", LargeIcon = "Resources\\ClashTests_32.png",
        DisplayName = "Clash Tests", ToolTip = "What clashes against what, and how close is too close")]
    [Command("ID_CW_Review", Icon = "Resources\\Review_16.png", LargeIcon = "Resources\\Review_32.png",
        DisplayName = "Review", ToolTip = "One issue at a time over the model, on the keyboard")]
    [Command("ID_CW_ClashReport", Icon = "Resources\\ClashReport_16.png", LargeIcon = "Resources\\ClashReport_32.png",
        DisplayName = "Clash Report", ToolTip = "The board as a document somebody without Navisworks can read")]
    [Command("ID_CW_DeskData", Icon = "Resources\\DeskData_16.png", LargeIcon = "Resources\\DeskData_32.png",
        DisplayName = "Data", ToolTip = "What the elements carry, and the pen to fix it")]
    [Command("ID_CW_DataManager", Icon = "Resources\\DataManager_16.png", LargeIcon = "Resources\\DataManager_32.png",
        DisplayName = "Data Manager", ToolTip = "Every property in one grid, and the pen to fix it")]
    [Command("ID_CW_Takeoff", Icon = "Resources\\Takeoff_16.png", LargeIcon = "Resources\\Takeoff_32.png",
        DisplayName = "Takeoff", ToolTip = "Quantities summed how a bill wants them")]
    [Command("ID_CW_DeskScenes", Icon = "Resources\\DeskScenes_16.png", LargeIcon = "Resources\\DeskScenes_32.png",
        DisplayName = "Scenes", ToolTip = "What is selected, what is visible, and what colour it is")]
    [Command("ID_CW_Sets", Icon = "Resources\\Sets_16.png", LargeIcon = "Resources\\Sets_32.png",
        DisplayName = "Set Builder", ToolTip = "The selections everything else works on")]
    [Command("ID_CW_Appearance", Icon = "Resources\\Appearance_16.png", LargeIcon = "Resources\\Appearance_32.png",
        DisplayName = "Appearance", ToolTip = "What is coloured, what is hidden, and which layer did it")]
    [Command("ID_CW_SectionBox", Icon = "Resources\\SectionBox_16.png", LargeIcon = "Resources\\SectionBox_32.png",
        DisplayName = "Section Box", ToolTip = "Box the selection, or switch sectioning off")]
    [Command("ID_CW_DeskDeliver", Icon = "Resources\\DeskDeliver_16.png", LargeIcon = "Resources\\DeskDeliver_32.png",
        DisplayName = "Deliver", ToolTip = "The model leaving the building")]
    [Command("ID_CW_ExportIfc", Icon = "Resources\\ExportIfc_16.png", LargeIcon = "Resources\\ExportIfc_32.png",
        DisplayName = "Export IFC", ToolTip = "IFC4 or 2x3, with mapping and classification")]
    [Command("ID_CW_Compile", Icon = "Resources\\Compile_16.png", LargeIcon = "Resources\\Compile_32.png",
        DisplayName = "Compile", ToolTip = "Supplier drops appended newest-first, with the log")]
    [Command("ID_CW_Batch", Icon = "Resources\\Batch_16.png", LargeIcon = "Resources\\Batch_32.png",
        DisplayName = "Batch", ToolTip = "A list of jobs, run in order, in one press")]
    public class CamelWorksPlugin : CommandHandlerPlugin
    {
        /// <summary>
        /// Create the handler.
        ///
        /// Registers the side-by-side assembly resolver — see <see cref="CamelWorksPaneBase"/> for
        /// why both entry points do it.
        /// </summary>
        public CamelWorksPlugin() => Host.Ensure();

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
                string? said;

                if (command.DeskId != null)
                {
                    said = DeskWindow.Show(command.DeskId);
                }
                else if (command.ToolId == null)
                {
                    // Home: the dock pane at rest.
                    said = OpenHome();
                }
                else if (command.Tool?.Shape == ToolShape.Act)
                {
                    said = Actions.Run(command.Id);
                }
                else
                {
                    said = ToolViews.Open(command.ToolId);
                }

                if (said != null) Warn(said);
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
        /// Nearly every one of these needs a model, so with nothing open they are shown but
        /// disabled — visible, so the tab does not look empty and the user can see what the
        /// product offers, and disabled, because a button that looks available and then says
        /// "open a model first" has wasted the click and taught nothing. Home and Help are the
        /// two that stay live: Home says what the product is, and Help must work when nothing
        /// else does.
        /// </summary>
        /// <param name="commandId">The command being asked about.</param>
        public override CommandState CanExecuteCommand(string commandId)
        {
            if (string.Equals(commandId, "ID_CW_Home", StringComparison.Ordinal)
                || string.Equals(commandId, "ID_CW_Help", StringComparison.Ordinal))
                return new CommandState(true);

            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            var ready = document != null && !document.IsClear;

            return new CommandState(ready);
        }

        /// <summary>Bring up the main pane at rest — the week, the desks and the search.</summary>
        private static string? OpenHome()
        {
            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(PaneRouter.MainPaneKey);

            if (!(record is DockPanePluginRecord dockRecord))
                return "The CamelWorks panel is not registered with Navisworks. This almost always "
                       + "means the CamelWorks.UI.dll in this Navisworks year folder was built "
                       + "against a different release.";

            if (!dockRecord.IsLoaded) dockRecord.LoadPlugin();

            if (dockRecord.LoadedPlugin is CamelWorksPaneBase pane)
            {
                pane.Visible = true;
                pane.Show(null);
            }

            return null;
        }

        private static void Warn(string message) =>
            MessageBox.Show(message, "CamelWorks", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
