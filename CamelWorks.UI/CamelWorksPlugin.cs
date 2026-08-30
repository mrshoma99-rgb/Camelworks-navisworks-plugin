using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.UI
{
    /// <summary>
    /// The CamelWorks buttons on the BIMCamel ribbon tab.
    ///
    /// One flat row: the hub, one button per app of the suite, the section-box act and Help. A
    /// button goes to one of three places, decided by the catalogue entry: the hub window, an
    /// app's window on the tab its work stopped at, or a tool that lives outside the apps —
    /// Review's palette, the acts, Help. No button opens a modal, because a modal over a 3D view
    /// hides the very thing the user is deciding about.
    ///
    /// The command ids here must match CamelWorks.xaml exactly. They also appear in
    /// <see cref="CommandCatalogue"/>, which is the one place the routing and the tooltips come
    /// from — a check asserts the three lists agree, because a command present in one and missing
    /// from another is invisible in exactly one place.
    ///
    /// <b>The tab is shared.</b> Dyncamelo and the BIMCamel IFC exporter declare the same tab id,
    /// and <see cref="Shell.RibbonTabMerger"/> folds the separately-composed tabs into one - so a
    /// machine with all three installed gets one BIMCamel tab with three panels on it rather than
    /// three tabs of the same name. The merger is installed by CamelWorksStartup, and again here
    /// as a fallback for the case where the startup plug-in did not load.
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
    [RibbonTab("ID_Tab_BIMCamel")]
    [Command("ID_CW_AppClash", Icon = "Resources\\AppClash_16.png", LargeIcon = "Resources\\AppClash_32.png",
        DisplayName = "Clash Manager", ToolTip = "Find what fights, and decide it")]
    [Command("ID_CW_AppIssueManager", Icon = "Resources\\AppIssueManager_16.png", LargeIcon = "Resources\\AppIssueManager_32.png",
        DisplayName = "Issue Manager", ToolTip = "Everything raised on this job, in one register")]
    [Command("ID_CW_AppAudit", Icon = "Resources\\AppAudit_16.png", LargeIcon = "Resources\\AppAudit_32.png",
        DisplayName = "Model Audit", ToolTip = "Whether this federation is fit to coordinate against")]
    [Command("ID_CW_AppTrack", Icon = "Resources\\AppTrack_16.png", LargeIcon = "Resources\\AppTrack_32.png",
        DisplayName = "Compare Track", ToolTip = "What changed, against what, since when")]
    [Command("ID_CW_AppData", Icon = "Resources\\AppData_16.png", LargeIcon = "Resources\\AppData_32.png",
        DisplayName = "Data Manager", ToolTip = "What the elements carry, and the pen to fix it")]
    [Command("ID_CW_AppQuantify", Icon = "Resources\\AppQuantify_16.png", LargeIcon = "Resources\\AppQuantify_32.png",
        DisplayName = "Quantify", ToolTip = "Numbers out of the model, the way a bill wants them")]
    [Command("ID_CW_AppSets", Icon = "Resources\\AppSets_16.png", LargeIcon = "Resources\\AppSets_32.png",
        DisplayName = "Sets Manager", ToolTip = "The selections everything else works on")]
    [Command("ID_CW_AppScenes", Icon = "Resources\\AppScenes_16.png", LargeIcon = "Resources\\AppScenes_32.png",
        DisplayName = "Scene Kit", ToolTip = "What is visible, and what colour it is")]
    [Command("ID_CW_AppExports", Icon = "Resources\\AppExports_16.png", LargeIcon = "Resources\\AppExports_32.png",
        DisplayName = "Export Kit", ToolTip = "The model as a file somebody else opens")]
    [Command("ID_CW_AppSite", Icon = "Resources\\AppSite_16.png", LargeIcon = "Resources\\AppSite_32.png",
        DisplayName = "Site Pack", ToolTip = "The model reaching the people building it")]
    [Command("ID_CW_AppAutomate", Icon = "Resources\\AppAutomate_16.png", LargeIcon = "Resources\\AppAutomate_32.png",
        DisplayName = "Automate", ToolTip = "Doing all of it again next Friday, in one press")]
    [Command("ID_CW_SectionBox", Icon = "Resources\\SectionBox_16.png", LargeIcon = "Resources\\SectionBox_32.png",
        DisplayName = "Section Box", ToolTip = "Box the selection, or switch sectioning off")]
    [Command("ID_CW_Help", Icon = "Resources\\Help_16.png", LargeIcon = "Resources\\Help_32.png",
        DisplayName = "Help", ToolTip = "Guide, limitations, shortcuts, diagnostics and about")]
    public class CamelWorksPlugin : CommandHandlerPlugin
    {
        /// <summary>
        /// Create the handler.
        ///
        /// Registers the side-by-side assembly resolver here, because by the time a screen needs
        /// CamelWorks.Core the JIT has already tried and failed to find it.
        /// </summary>
        public CamelWorksPlugin() => Host.Ensure();

        /// <inheritdoc />
        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            // Idempotent, and a fallback: CamelWorksStartup installs the merger the moment
            // Navisworks loads, but a startup plug-in that failed to load would otherwise leave
            // the duplicate tabs in place for the whole session.
            Shell.RibbonTabMerger.Install();

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

                if (command.AppId != null)
                {
                    said = AppWindow.Show(command.AppId);
                }
                else if (command.ToolId == null)
                {
                    // A catalogue entry naming neither. There are none, and a button that does
                    // nothing is the one failure a user cannot report, so it says so.
                    said = command.Title + " does not name anything to open. This is a bug in "
                           + "this build rather than anything you did.";
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
        /// "open a model first" has wasted the click and taught nothing. Help is the one that
        /// stays live, because it must work when nothing else does.
        /// </summary>
        /// <param name="commandId">The command being asked about.</param>
        public override CommandState CanExecuteCommand(string commandId)
        {
            if (string.Equals(commandId, "ID_CW_Help", StringComparison.Ordinal))
                return new CommandState(true);

            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            var ready = document != null && !document.IsClear;

            return new CommandState(ready);
        }

        /// <summary>
        /// Say something on the way to a screen that never opened.
        ///
        /// Owned by the Navisworks frame. A modal with no owner disables every top-level window on
        /// the thread and re-enables them from the owner it does not have, which is how a dialog
        /// somebody closes takes the host down with it - the same fault that used to close
        /// Navisworks when a settings window was shut.
        /// </summary>
        /// <param name="message">What to say.</param>
        private static void Warn(string message)
        {
            var owner = Shell.HostWindow.Frame();

            if (owner == null)
            {
                MessageBox.Show(message, "CamelWorks", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(owner, message, "CamelWorks", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
