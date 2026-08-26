using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Put one property onto every element in scope.
    ///
    /// The writers shipped inside the Data Manager, below a table of every property the model
    /// already carries. Somebody who knows exactly which property they are setting - which is
    /// most of the times anybody writes one - had to be shown all the others first, then scroll.
    ///
    /// Same writers, same preview, same two-stage commit. What this window drops is the browse
    /// table, because browsing is a different question from writing.
    /// </summary>
    public static class WritePropertiesWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            if (Host.Current == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("write"),
                                   () => DataViews.WriteProperties(), 720, 660);
        }
    }
}
