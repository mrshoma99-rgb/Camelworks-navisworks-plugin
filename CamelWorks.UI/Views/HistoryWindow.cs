using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Project;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// What has happened on this project, newest first.
    ///
    /// A coordination job changes hands. The person who picks it up inherits a federation, a board
    /// and a set library, and no account at all of what was done to any of them - which rules were
    /// applied, which reports went out, what got written onto the model and when. The log has been
    /// recorded since the first release; it was shown twelve lines at a time on a Home screen that
    /// nothing opened, so in practice it was written and never read.
    /// </summary>
    public static class HistoryWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("history"), () => Body(session), 700, 560);
        }

        private static UIElement Body(Session session)
        {
            var entries = session.Store.Activity.Entries.ToList();

            if (entries.Count == 0)
                return Ui.Stack(
                    Ui.Empty("Nothing has happened on this project yet.",
                             "CamelWorks writes a line here every time it changes the model, "
                             + "produces a report or runs the rules - so a project handed over "
                             + "mid-job can say what was done to it."));

            var rows = entries
                .Select(e => new TableRow(e,
                                          ActivityKind.Title(e.Kind),
                                          e.Summary,
                                          ActivityLog.Ago(e.WhenTicks, session.NowTicks)))
                .ToList();

            var table = Ui.Table(rows, ("What", 120), ("Happened", 0), ("When", 110));

            // Copy rather than export: this is evidence somebody pastes into an email defending a
            // decision, and a file to attach is a worse fit for that than text already on the
            // clipboard.
            var said = Ui.Stack();

            var copy = Ui.Primary("Copy the log", () =>
            {
                said.Children.Clear();

                var text = string.Join("\r\n", entries.Select(
                    e => ActivityLog.Ago(e.WhenTicks, session.NowTicks)
                         + "\t" + ActivityKind.Title(e.Kind)
                         + "\t" + e.Summary));

                try
                {
                    Clipboard.SetText(text);
                    said.Children.Add(Ui.Done("Copied " + Ui.Count(entries.Count, "line")
                                              + ". Paste it wherever the question was asked."));
                }
                catch (Exception e)
                {
                    said.Children.Add(Ui.Problem("Could not copy it: " + Ui.Describe(e)));
                }
            });

            return Ui.Stack(
                Ui.Sub(Ui.Count(entries.Count, "entry") + ", newest first."),
                table,
                copy,
                said);
        }
    }
}
