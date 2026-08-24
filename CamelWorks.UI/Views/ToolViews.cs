using System;
using System.Windows;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Opens a tool, in whatever shape that tool takes.
    ///
    /// This replaces a switchboard keyed on "workspace/tab". A tool is addressed by its own name
    /// now: six of them are panels and come up in the dock pane, and everything else opens its own
    /// window through <see cref="ToolWindow"/> or through a window it already had.
    ///
    /// <b>Nothing runs when a tool is opened.</b> Every screen here can end up walking a
    /// federation of a few hundred thousand elements, and doing that because somebody pressed a
    /// ribbon button freezes the host with no warning and no way to stop it. Each one shows what
    /// it will do and the button to do it - which is also what makes the zero-setup rule honest,
    /// since that button works on a raw model with nothing configured.
    /// </summary>
    public static class ToolViews
    {
        /// <summary>
        /// Open a tool.
        /// </summary>
        /// <param name="toolId">Which tool.</param>
        /// <param name="panel">Show a panel tool in the dock pane.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Open(string? toolId, Action<Tool>? panel = null)
        {
            var tool = Tools.Find(toolId);

            if (tool == null) return "There is no tool called \"" + (toolId ?? "nothing") + "\".";

            if (tool.Shape == ToolShape.Panel)
            {
                if (panel == null) return "That tool needs the CamelWorks pane, which is not open.";

                panel(tool);
                return null;
            }

            switch (tool.Id)
            {
                // Tools that already had a window of their own, before there was a host for them.
                case "review": return ReviewWindow.Show();
                case "links": return FixLinksWindow.Show();
                case "matrix": return MatrixWindow.Show();
                case "clashxml": return ClashExportWindow.Show();
                case "datalink": return DataLinkWindow.Show();
                case "report": return ReportBuilderWindow.Show();
                case "extract": return ExtractWindow.Show();
                case "compile": return CompileWindow.Show();
                case "restructure": return RestructureWindow.Show();
                case "ids": return IdsWindow.Show();
                // The one window that returns nothing: Help is the screen that has to work when
                // the rest of the product will not, so it never refuses.
                case "help": HelpWindow.Show(); return null;
                case "export": return ExportWindow.Show();

                // The five that were five collapsible sections of one window. They share a
                // geometry read, so they still share a window until each is lifted out of it.
                case "weight":
                case "dimensions":
                case "setout":
                case "paperwork":
                case "labels": return SiteWindow.Show();

                default: return Window(tool, Surface(tool), Wide(tool));
            }
        }

        /// <summary>The surface for a tool that lives in the dock pane.</summary>
        /// <param name="tool">Which tool, or null for the launcher.</param>
        /// <param name="open">Open another tool by id.</param>
        public static UIElement Panel(Tool? tool, Action<string> open)
        {
            if (tool == null) return Launcher(open);

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

                    default: return Launcher(open);
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

        /// <summary>
        /// What the pane shows when no tool is open: every tool, and what each one is for.
        ///
        /// The switcher this replaces named seven workspaces and told you nothing about any of
        /// them, and the tab strip under it named eighteen tabs the same way. What each tool is
        /// trying to do is the thing somebody actually needs, and only at the moment they have not
        /// chosen yet - so it lives here, at rest, rather than taking a strip off every screen
        /// forever.
        /// </summary>
        /// <param name="open">Open a tool by id.</param>
        public static UIElement Launcher(Action<string> open)
        {
            var stack = Ui.Stack(
                Ui.Heading("What would you like to do?"),
                Ui.Sub("Every one of these is on the ribbon too. This is the same list, with what "
                       + "each one is for and what it will do."));

            foreach (var tool in Tools.All)
            {
                if (tool.Shape == ToolShape.Act || tool.Shape == ToolShape.Menu) continue;

                var here = tool;

                stack.Children.Add(Ui.Row(here.Title, here.Purpose, here.MainAction,
                                          () => open(here.Id)));
            }

            return Ui.Scroll(stack);
        }

        /// <summary>The screen a windowed tool shows, or null when it has none of its own.</summary>
        private static Func<UIElement>? Surface(Tool tool)
        {
            switch (tool.Id)
            {
                case "health": return ProjectViews.Health;
                case "setup": return ProjectViews.Setup;
                case "headroom": return CoordinateViews.ClearHeight;
                case "merge": return CoordinateViews.Merge;
                case "excel": return DataViews.Excel;
                case "project": return ProjectViews.Profile;
                case "tests": return CoordinateViews.Tests;
                case "rules": return CoordinateViews.Rules;
                case "clashreport": return CoordinateViews.Report;
                case "bcf": return CoordinateViews.Bcf;
                case "compare": return CompareViews.Run;
                case "levels": return DataViews.Zones;
                case "takeoff": return DataViews.Takeoff;
                case "viewpoints": return SetsViews.Viewpoints;
                case "ifc": return ProjectViews.ExportIfc;
                case "batch": return BatchViews.Jobs;
                default: return null;
            }
        }

        /// <summary>How wide a tool wants to be. Tables and grids want more than a form does.</summary>
        private static int Wide(Tool tool)
        {
            switch (tool.Id)
            {
                case "tests":
                case "rules":
                case "compare":
                case "takeoff": return 1100;

                case "bcf":
                case "merge":
                case "excel":
                case "viewpoints":
                case "levels": return 1000;

                case "headroom": return 900;

                default: return 0;
            }
        }

        private static string? Window(Tool? tool, Func<UIElement>? body, int width)
        {
            if (tool == null) return "There is no such tool.";

            if (body == null)
                return "\"" + tool.Title + "\" has no screen of its own yet.";

            return ToolWindow.Show(tool, body, width);
        }
    }
}
