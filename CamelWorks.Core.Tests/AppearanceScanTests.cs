using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The scan that reads an existing scene's appearance.
    ///
    /// Its contract is a performance contract, which is why it is tested rather than measured. The
    /// Appearance Manager used to list the federation into keys and hand the keys back to be read,
    /// and reading a key means the host searching the document for it — so a federation of two
    /// hundred thousand elements asked for two hundred thousand searches of two hundred thousand
    /// elements, inside one call, with one progress report per five thousand of them. Navisworks
    /// stopped answering and the progress bar froze with it.
    ///
    /// Three properties are what make the replacement safe, and each one is a way the old shape was
    /// wrong rather than merely slow:
    ///
    ///   * Only elements in some state are handed back, so a key — the expensive part — is built
    ///     for the few hundred that carry something and not the two hundred thousand that do not.
    ///   * The progress count is elements walked, not elements handed back, and it is reported per
    ///     element. That is what keeps the bar moving and Stop answering for the whole scan.
    ///   * The item is handed over with the reading, so a caller wanting a name or a tree path has
    ///     it already and does not put the search back.
    /// </summary>
    public class AppearanceScanTests
    {
        private static readonly Colour Red = new Colour(200, 30, 30);
        private static readonly Colour Blue = new Colour(30, 30, 200);

        private static (FakeDocument doc, FakeViewSession view, List<FakeItem> items) Model(int count)
        {
            var doc = new FakeDocument();
            var arch = doc.AddModel("ARCH", @"C:\proj\ARCH.nwc");

            var items = new List<FakeItem>();

            for (var i = 0; i < count; i++)
                items.Add(doc.AddItem(arch, "Part " + i.ToString(), "Generic", null,
                                      default, true, "Level 03"));

            return (doc, new FakeViewSession(doc), items);
        }

        [Fact]
        public void Only_the_elements_in_some_state_come_back()
        {
            var (_, view, items) = Model(500);

            view.SeedForeignColour(items[7].Key, Red);
            view.SetVisible(new[] { items[9].Key }, false);

            var found = view.ScanAppearance(TraversalScope.WholeDocument, null).ToList();

            // Two, not five hundred. The other four hundred and ninety-eight never cost a key.
            Assert.Equal(2, found.Count);
            Assert.Equal(new[] { items[7].Key, items[9].Key }, found.Select(f => f.Key).ToArray());
            Assert.All(found, f => Assert.False(f.State.IsPristine));
        }

        [Fact]
        public void An_untouched_model_hands_back_nothing_at_all()
        {
            var (_, view, _) = Model(200);

            Assert.Empty(view.ScanAppearance(TraversalScope.WholeDocument, null));
        }

        [Fact]
        public void Progress_counts_what_was_walked_and_not_what_came_back()
        {
            var (_, view, items) = Model(300);

            view.SeedForeignColour(items[0].Key, Red);

            var last = 0;
            var calls = 0;

            var found = view.ScanAppearance(TraversalScope.WholeDocument, seen =>
            {
                calls++;
                last = seen;
                return true;
            }).ToList();

            // One element came back; three hundred were looked at, and the caller was told so three
            // hundred times. A count of what came back would have left the bar on 1 of 300 for the
            // whole scan, which is the freeze the user actually sees.
            Assert.Single(found);
            Assert.Equal(300, last);
            Assert.Equal(300, calls);
        }

        [Fact]
        public void Progress_is_reported_per_element_so_the_bar_can_move_and_Stop_can_answer()
        {
            var (_, view, _) = Model(64);

            var seen = new List<int>();

            view.ScanAppearance(TraversalScope.WholeDocument, n => { seen.Add(n); return true; })
                .ToList();

            // Every element, in order, with no gaps. The old reading reported once per five
            // thousand, and five thousand host searches is minutes with nothing on the glass.
            Assert.Equal(Enumerable.Range(1, 64), seen);
        }

        [Fact]
        public void Stop_stops_the_walk_where_it_was_asked_to()
        {
            var (_, view, items) = Model(400);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var walked = 0;

            var found = view.ScanAppearance(TraversalScope.WholeDocument, seen =>
            {
                walked = seen;
                return seen < 10;
            }).ToList();

            // Stopped at ten, and nothing past it was read. A scan that ran on past the stop is a
            // Stop button that does not stop.
            Assert.Equal(10, walked);
            Assert.Equal(9, found.Count);
        }

        [Fact]
        public void The_walk_is_lazy_so_a_caller_that_stops_early_pays_for_what_it_took()
        {
            var (_, view, items) = Model(1000);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var walked = 0;

            // Taking three off the front must not walk a thousand. A materialised scan would, and
            // would freeze the window doing it.
            var first = view.ScanAppearance(TraversalScope.WholeDocument, seen => { walked = seen; return true; })
                            .Take(3)
                            .ToList();

            Assert.Equal(3, first.Count);
            Assert.Equal(3, walked);
        }

        [Fact]
        public void The_item_comes_back_with_the_reading_so_nothing_is_looked_up_twice()
        {
            var (_, view, items) = Model(50);

            view.SeedForeignColour(items[11].Key, Blue);

            var found = view.ScanAppearance(TraversalScope.WholeDocument, null).Single();

            // The tree path is what taking a scheme off a model needs, and it is here already.
            // Asking for it by key afterwards is the search this seam member exists to remove.
            Assert.Equal(items[11].Key, found.Item.Key);
            Assert.Equal(new[] { "Level 03", "Part 11" }, found.Item.TreePath.ToArray());
            Assert.Equal(found.Item.Key, found.State.Key);
        }

        [Fact]
        public void The_temporary_layer_is_refused_at_the_call_and_not_at_the_first_step()
        {
            var (_, view, _) = Model(10);

            view.Layer = OverrideLayer.Temporary;

            // Thrown by the call itself. An iterator that deferred this would hand back a sequence
            // that looks fine until something enumerates it, and the refusal would surface
            // somewhere that has no idea which layer was selected.
            Assert.Throws<NotSupportedException>(
                () => view.ScanAppearance(TraversalScope.WholeDocument, null));
        }

        [Fact]
        public void A_scope_narrows_the_walk_rather_than_filtering_its_result()
        {
            var (doc, view, items) = Model(100);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            doc.DefineSet("Just three", items[0].Key, items[1].Key, items[2].Key);

            var walked = 0;

            var found = view.ScanAppearance(TraversalScope.Set("Just three"),
                                            seen => { walked = seen; return true; }).ToList();

            // Three walked, not a hundred walked and three kept.
            Assert.Equal(3, found.Count);
            Assert.Equal(3, walked);
        }

        [Fact]
        public void The_audit_reports_the_model_it_walked_and_not_the_rows_it_kept()
        {
            var (_, view, items) = Model(5000);

            view.SeedForeignColour(items[1].Key, Red);
            view.SeedForeignColour(items[2].Key, Red);

            var walked = 0;

            var states = view.ScanAppearance(TraversalScope.WholeDocument,
                                             seen => { walked = seen; return true; })
                             .Select(f => f.State)
                             .ToList();

            var audit = ForeignAppearance.FindInScan(states, null, walked);

            // "2 of 5,000", not "2 of 2". The second reads as a model where everything is
            // overridden, which is the opposite of what was found.
            Assert.Equal(5000, audit.Scanned);
            Assert.Equal(2, audit.Total);
            Assert.Contains("of 5,000", audit.Explain());
        }

        [Fact]
        public void A_clean_model_still_says_how_much_of_it_was_looked_at()
        {
            var audit = ForeignAppearance.FindInScan(Array.Empty<AppearanceState>(), null, 230000);

            Assert.True(audit.IsClean);

            // The empty result has to carry the size of the thing that produced it, or "nothing
            // found" is indistinguishable from "nothing looked at".
            Assert.Equal(230000, audit.Scanned);
            Assert.Contains("230,000", audit.Explain());
        }

        [Fact]
        public void A_total_below_its_own_part_is_corrected_rather_than_reported()
        {
            var keys = Enumerable.Range(0, 4)
                .Select(i => ElementKey.FromTreePath(
                    ElementKey.ScopeOf(@"C:\p\A.nwc"), @"C:\p\A.nwc", "e" + i.ToString()))
                .ToList();

            var states = keys.Select(k => new AppearanceState(k, Red, null, false, true)).ToList();

            // A caller that under-reported its walk. Four of one is the one answer that cannot be
            // true, so the part wins over the total.
            var audit = ForeignAppearance.FindInScan(states, null, 1);

            Assert.Equal(4, audit.Scanned);
            Assert.Equal(4, audit.Total);
        }
    }
}
