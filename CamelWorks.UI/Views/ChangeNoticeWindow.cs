using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The change notice: what has moved since somebody put their name to the scope.
    ///
    /// Measured against the freeze rather than against last week, because a variation is argued
    /// from the agreement, not from the last time anybody happened to look.
    /// </summary>
    public static class ChangeNoticeWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            if (Host.Current == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("notice"),
                                   () => CoordinateViews.NoticeScreen(), 620, 560);
        }
    }
}
