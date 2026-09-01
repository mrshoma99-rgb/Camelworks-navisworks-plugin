using System;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Store;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The blue strip that says somebody else has this project open.
    ///
    /// The lease has existed in Core since the store went in and nothing ever showed it. That is
    /// the worst shape a concurrency guard can take: two coordinators edit the clash matrix at
    /// the same time, one save quietly wins, and the other person finds out a week later when
    /// their rules are not the ones in the report.
    ///
    /// So it says so, on every screen, before the work rather than after it. Three properties
    /// matter more than the mechanism:
    ///
    /// * <b>It never blocks reading.</b> Losing the lease costs the ability to save project
    ///   settings and nothing else. The board, the reports and the exports carry on.
    /// * <b>It offers the way out.</b> A strip that says "you cannot save" and leaves you there is
    ///   how people learn to delete lock files by hand. "Take it over" is one press.
    /// * <b>It never takes a screen down.</b> A strip is the least important thing on any panel,
    ///   so everything here is inside a try and an unreadable lease shows nothing at all.
    /// </summary>
    internal static class LeaseStrip
    {
        /// <summary>
        /// The strip, or null when there is nothing worth saying — which is almost always.
        ///
        /// Looks rather than claims. Claiming here would mean that reading the clash board for two
        /// minutes locked a colleague out of settings for ten, which is the behaviour that makes
        /// people stop trusting locks. The claim happens on the way to a write, in
        /// <see cref="Session.Record"/>.
        /// </summary>
        internal static UIElement? For()
        {
            Session? session = null;
            LeaseResult? lease = null;

            try
            {
                session = Host.Current;
                lease = session?.LookAtLease();
            }
            catch (Exception)
            {
                return null;
            }

            if (session == null) return null;

            if (lease != null && lease.Outcome == LeaseOutcome.HeldByAnother)
            {
                var strip = new Border();

                strip.Child = Ui.Info(
                    lease.Reason ?? "Somebody else is editing project settings.",
                    "Take it over",
                    () => Take(strip, session, lease.Holder));

                return strip;
            }

            return Published(session);
        }

        /// <summary>
        /// The strip that says this document is a snapshot, before anybody works an afternoon
        /// into one.
        ///
        /// Everything in this product still runs on an NWD - reading, checking, reporting and
        /// exporting are most of what it does, and none of them needs a writable file. What
        /// changes is where an edit ends up: the clash manager keeps the decisions it cannot
        /// write and sends them out as XML, and every property write says so rather than
        /// reporting a count of nothing. This is that fact said once, up front, instead of
        /// fourteen tools each discovering it separately.
        /// </summary>
        private static UIElement? Published(Session session)
        {
            try
            {
                if (!session.IsPublished) return null;
            }
            catch (Exception)
            {
                return null;
            }

            if (_publishedRead) return null;

            var strip = new Border();

            strip.Child = Ui.Info("This is a published NWD, so it is a snapshot: nothing written "
                + "here can be saved back into it. Reading, checking, reporting and exporting all "
                + "work as normal. Clash decisions are kept and travel out as clash XML; open the "
                + "NWF the models were federated in when you need a write to land in the file.",
                "Dismiss",
                () =>
                {
                    _publishedRead = true;
                    strip.Visibility = Visibility.Collapsed;
                });

            return strip;
        }

        /// <summary>
        /// Whether somebody has read the published-NWD strip and said so.
        ///
        /// <b>For the rest of the session, not for ever.</b> It is a standing fact about the
        /// document rather than an event, so it appears on every screen and would otherwise be
        /// dismissed on every screen - which is how a banner stops being read at all. Kept in
        /// memory rather than on disk because opening a different document, or coming back
        /// tomorrow, is exactly when somebody needs telling again.
        /// </summary>
        private static bool _publishedRead;

        /// <summary>Put a strip above a screen, or hand the screen back untouched.</summary>
        /// <param name="surface">The screen.</param>
        internal static UIElement Over(UIElement surface)
        {
            var strip = For();

            if (strip == null) return surface;

            // A Grid, not a stack: most screens are a ScrollViewer, and a StackPanel would give it
            // its whole desired height and scroll nothing.
            var grid = new Grid();

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(strip, 0);
            Grid.SetRow(surface, 1);

            grid.Children.Add(strip);
            grid.Children.Add(surface);

            return grid;
        }

        private static void Take(Border strip, Session session, string? from)
        {
            try
            {
                var taken = session.Claim(force: true);

                strip.Child = taken.MayWrite
                    ? Ui.Done("You have project settings now. "
                              + (string.IsNullOrWhiteSpace(from) ? "The other person" : from!)
                              + " will be told the next time they try to save, and nothing they "
                              + "have already saved is lost.")
                    : Ui.Problem(taken.Reason ?? "Could not take it over.");
            }
            catch (Exception e)
            {
                strip.Child = Ui.Problem("Could not take it over: " + Ui.Describe(e));
            }
        }
    }
}
