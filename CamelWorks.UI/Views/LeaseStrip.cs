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

            if (session == null || lease == null || lease.Outcome != LeaseOutcome.HeldByAnother)
                return null;

            var strip = new Border();

            strip.Child = Ui.Info(
                lease.Reason ?? "Somebody else is editing project settings.",
                "Take it over",
                () => Take(strip, session, lease.Holder));

            return strip;
        }

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
