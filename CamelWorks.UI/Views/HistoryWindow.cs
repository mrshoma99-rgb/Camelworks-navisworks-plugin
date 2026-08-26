using System;
using System.Collections.Generic;
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
        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            Body(session);

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

            var said = Ui.Stack();

            // Picking rather than one Copy button: the answer to "what happened on this project in
            // August" is a handful of lines out of two hundred, and pasting the lot leaves whoever
            // asked to do the filtering.
            var picking = Ui.Picking(table,
                new Ui.OnPicked("Copy {n}", (picked, say) => Copy(session, picked, say), primary: true));

            return Ui.Stack(
                Ui.Sub(Ui.Count(entries.Count, "entry") + ", newest first."),
                picking,
                Ui.Quiet("Copy the whole log",
                         () => Copy(session, entries.Cast<object>().ToList(), said)),
                said);
        }

        /// <summary>The picked entries, as lines somebody can paste into the argument.</summary>
        private static void Copy(Session session, IReadOnlyList<object> picked, Panel said) =>
            Ui.Copy(picked.OfType<Activity>()
                        .Select(e => ActivityLog.Ago(e.WhenTicks, session.NowTicks)
                                     + "\t" + ActivityKind.Title(e.Kind)
                                     + "\t" + e.Summary),
                    said, "line");
    }
}
