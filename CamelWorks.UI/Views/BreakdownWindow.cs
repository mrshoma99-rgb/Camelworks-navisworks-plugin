using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Work breakdown: the selection tree's own shape, written onto the elements as properties.
    ///
    /// This was a card at the bottom of Levels and Zones, which is where nobody looked for it -
    /// levels and a work breakdown are different questions, and a coordinator hunting for a WBS
    /// has no reason to open a window about storeys. It had a ribbon button in neither the old
    /// tab layout nor the tool catalogue, so the only way to reach a shipped, tested feature was
    /// to open an unrelated tool and scroll.
    /// </summary>
    public static class BreakdownWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("wbs"),
                                   () => DataViews.BreakdownStamp(session), 680, 600);
        }
    }
}
