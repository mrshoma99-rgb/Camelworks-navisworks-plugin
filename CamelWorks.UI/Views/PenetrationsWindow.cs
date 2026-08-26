using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Every service that goes through structure, as a schedule.
    ///
    /// On a concrete frame the pour date is the one immovable date on the programme, and this list
    /// is what has to be right before it. It existed only as the third entry in a "Contents"
    /// picker inside Clash Report - so the structural engineer's weekly ask was reachable by
    /// opening a window about clash reports and changing a dropdown, which is not a route anybody
    /// finds without being told.
    ///
    /// Same engine, same writers, same revision letters. What changes is that the window says on
    /// its title bar what it is for.
    /// </summary>
    public static class PenetrationsWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            if (Host.Current == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("penetrations"),
                                   () => CoordinateViews.PenetrationsReport(), 760, 640);
        }
    }
}
