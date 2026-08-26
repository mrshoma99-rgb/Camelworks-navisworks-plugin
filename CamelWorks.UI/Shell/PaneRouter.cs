using System;
using Autodesk.Navisworks.Api.Plugins;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// Opens the dock panes, from anywhere in the product.
    ///
    /// The beside-the-model tools — the board, changes, the data grid, the set builder,
    /// appearance — live in the dock pane whether they are summoned from the ribbon or from a
    /// desk's rail, so the opening lives in one place both callers reach. It talks to the host
    /// directly, which is why it sits in Shell rather than Views: a desk asks for a tool by name
    /// and never learns what a DockPanePluginRecord is.
    /// </summary>
    public static class PaneRouter
    {
        /// <summary>The main pane's lookup key: "&lt;pluginId&gt;.&lt;developerId&gt;".</summary>
        public const string MainPaneKey = "CamelWorks.Pane.CamelWorks";

        /// <summary>The graph canvas pane's lookup key.</summary>
        public const string AutomatePaneKey = "CamelWorks.AutomatePane.CamelWorks";

        /// <summary>
        /// Show a tool in its dock pane.
        /// </summary>
        /// <param name="tool">A panel-shaped tool.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Open(Tool? tool)
        {
            if (tool == null) return "There is no such tool.";

            // The graph canvas is the one tool that needs the full width, so it has a pane of its
            // own; everything else shares the main one.
            var key = string.Equals(tool.Id, "graph", StringComparison.Ordinal)
                ? AutomatePaneKey
                : MainPaneKey;

            PluginRecord? record;

            try
            {
                record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(key);
            }
            catch (Exception e)
            {
                // No host — the design harness, or a Navisworks in a state the API will not
                // answer from. Said rather than thrown, because a rail row that takes the desk
                // down is a desk nobody reopens.
                return "The CamelWorks panel could not be reached: " + e.Message;
            }

            if (record == null)
            {
                // Nearly always a DLL built against a different Navisworks year: it loads, its
                // ribbon appears, and its dock pane silently fails to register. Worth saying in
                // full, because the symptom points nowhere near the cause.
                return "The CamelWorks panel is not registered with Navisworks (looked up \"" + key
                       + "\" and found nothing).\n\n"
                       + "This almost always means the CamelWorks.UI.dll in this Navisworks year folder "
                       + "was built against a different release. A DLL built for the wrong year still "
                       + "shows its ribbon buttons but its dock pane does not register.\n\n"
                       + "Fix: install the build matching this Navisworks version "
                       + "(2024 = API v21, 2025 = v22, 2026 = v23, 2027 = v24).";
            }

            if (!(record is DockPanePluginRecord dockRecord))
                return "The CamelWorks panel registered as an unexpected plugin type: "
                       + record.GetType().Name + ".";

            if (!dockRecord.IsLoaded) dockRecord.LoadPlugin();

            if (dockRecord.LoadedPlugin is CamelWorksPaneBase pane)
            {
                pane.Visible = true;
                pane.Show(tool.Id);
            }

            return null;
        }
    }
}
