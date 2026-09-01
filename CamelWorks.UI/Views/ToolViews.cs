using System;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Sends a tool where it lives.
    ///
    /// Every tool is a tab of one app of the suite, and opening it opens that app on that tab.
    /// Four live outside the table: Review is a floating palette over the 3D view, Section Box
    /// and Baseline are instant acts, and Help is the window that must work when the rest of the
    /// product will not. See <c>CAMELWORKS_DESIGN.md</c>.
    ///
    /// <b>Nothing runs when a tool is opened.</b> Every surface can end up walking a federation of
    /// a few hundred thousand elements, and doing that because somebody pressed a ribbon button
    /// freezes the host with no warning and no way to stop it. Each one shows what it will do and
    /// the button to do it — which is also what makes the zero-setup rule honest, since that
    /// button works on a raw model with nothing configured.
    /// </summary>
    public static class ToolViews
    {
        /// <summary>
        /// Open a tool.
        /// </summary>
        /// <param name="toolId">Which tool.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Open(string? toolId)
        {
            var tool = Tools.Find(toolId);

            if (tool == null) return "There is no tool called \"" + (toolId ?? "nothing") + "\".";

            if (tool.Shape == ToolShape.Act)
                return Actions.Run(tool.Id == "baseline" ? "ID_CW_Baseline" : "ID_CW_SectionBox");

            switch (tool.Id)
            {
                // The one window that is not an app: Help must work when the rest of the product
                // will not.
                case "help": return HelpWindow.Show();
            }

            var app = Apps.Of(tool.Id);

            if (app == null)
                return "\"" + tool.Title + "\" is in no app. This is a routing mistake in this "
                       + "build, not anything you did.";

            return AppWindow.Show(app.Id, tool.Id);
        }
    }
}
