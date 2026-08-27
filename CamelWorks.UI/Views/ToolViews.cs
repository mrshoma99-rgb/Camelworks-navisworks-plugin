using System;
using System.Windows;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Sends a tool where it lives.
    ///
    /// Three destinations, decided by the tool rather than the caller: the beside-the-model tools
    /// take the dock pane, Review and Help keep windows of their own, and everything else lands on
    /// the bench of its desk — one window per job, every tool of the job on its rail. See
    /// <c>CAMELWORKS_DESIGN.md</c>.
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
        /// <param name="panel">Show a panel tool in the dock pane; null routes it through the host.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Open(string? toolId, Action<Tool>? panel = null)
        {
            var tool = Tools.Find(toolId);

            if (tool == null) return "There is no tool called \"" + (toolId ?? "nothing") + "\".";

            switch (tool.Shape)
            {
                case ToolShape.Panel:
                    if (panel == null) return PaneRouter.Open(tool);

                    panel(tool);
                    return null;

                case ToolShape.Act:
                    return Actions.Run(tool.Id == "baseline" ? "ID_CW_Baseline" : "ID_CW_SectionBox");
            }

            switch (tool.Id)
            {
                // The two windows that are not desks: Review is a floating palette over the 3D
                // view, and Help must work when the rest of the product will not.
                case "review": return ReviewWindow.Show();
                case "help": return HelpWindow.Show();
            }

            var desk = Desks.Of(tool.Id);

            if (desk == null)
                return "\"" + tool.Title + "\" is on no desk. This is a routing mistake in this "
                       + "build, not anything you did.";

            return DeskWindow.Show(desk.Id, tool.Id);
        }

        /// <summary>The surface for a tool that lives in the dock pane.</summary>
        /// <param name="tool">Which tool, or null for Home.</param>
        /// <param name="open">Open another tool by id.</param>
        public static UIElement Panel(Tool? tool, Action<string> open)
        {
            if (tool == null) return HomeView.Build(open);

            try
            {
                switch (tool.Id)
                {
                    case "triage": return CoordinateViews.Triage();
                    case "changes": return CompareViews.Changes();
                    case "data": return DataViews.Manager();
                    case "sets": return SetsViews.Builder();
                    case "appearance": return SetsViews.Appearance();
                    case "graph": return AutomateView.Build();

                    default: return HomeView.Build(open);
                }
            }
            catch (Exception e)
            {
                // A screen that throws while being built would otherwise take the pane with it and
                // leave somebody with an empty panel and no idea which tool did it.
                return Ui.Scroll(Ui.Stack(
                    Ui.Heading(tool.Title),
                    Ui.Problem("This tool could not be opened: " + Ui.Describe(e))));
            }
        }
    }
}
