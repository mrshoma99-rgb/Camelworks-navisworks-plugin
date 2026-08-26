using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Freeze: the record that a scope was agreed, and what it was at the moment of agreeing.
    ///
    /// It used to share a card with the change notice, two runners over one output panel, so
    /// pressing either wiped what the other had just said. Worse, the card asked somebody who had
    /// never frozen anything to choose between two buttons before it had explained what a freeze
    /// was for. They are two jobs on two different days - one is signing, the other is finding out
    /// what moved since - so they are two windows.
    /// </summary>
    public static class FreezeWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            if (Host.Current == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("freeze"),
                                   () => CoordinateViews.FreezeScreen(), 620, 560);
        }
    }
}
