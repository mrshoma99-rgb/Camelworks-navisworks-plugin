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
    /// Keeping a scan so clicking back to a scene does not run it again.
    ///
    /// Three things can make a kept reading wrong, and they are not equally knowable. The model
    /// changing is knowable and the hard case, because the element keys and the tree itself have
    /// moved. The scene being written to is knowable, because this product did the writing. A
    /// viewpoint being re-saved with different state is NOT knowable: a SavedViewpoint carries no
    /// version and no timestamp. So the cache catches the first two and never pretends to catch the
    /// third — the reading carries the time it was taken and the screen shows it.
    /// </summary>
    public class SceneCacheTests
    {
        private static readonly Colour Red = new Colour(200, 30, 30);
        private static readonly Colour Blue = new Colour(30, 30, 200);

        private const long Minute = 600_000_000L;

        /// <summary>One node coloured, named the way a viewpoint names it.</summary>
        private static IReadOnlyList<KeyValuePair<string[], Colour>> One(FakeItem item, Colour colour) =>
            new[] { new KeyValuePair<string[], Colour>(item.TreePath.ToArray(), colour) };

        private static (FakeDocument doc, FakeViewSession view, FakeViewpointStore views,
                        List<FakeItem> items) Model(int parts)
        {
            var doc = new FakeDocument();
            var arch = doc.AddModel("ARCH", @"C:\proj\ARCH.nwc");

            var items = new List<FakeItem>();

            for (var i = 0; i < parts; i++)
                items.Add(doc.AddItem(arch, "Part " + i.ToString(), "Generic", null,
                                      default, true, "ARCH", "Level 03"));

            for (var i = 0; i < 5; i++)
                doc.AddItem(arch, "Other " + i.ToString(), "Generic", null,
                            default, true, "ARCH", "Level 04");

            var view = new FakeViewSession(doc);

            return (doc, view, new FakeViewpointStore(doc, view), items);
        }

        [Fact]
        public void A_second_read_of_the_same_scene_does_not_scan_again()
        {
            var (doc, view, _, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);

            var cache = new SceneCache();

            var first = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null);
            var second = cache.Read(view, SceneCache.LiveScene, doc.Generation, 2 * Minute, false, null);

            Assert.NotNull(first);
            Assert.NotNull(second);

            // The same reading object, not an equal one: nothing was walked the second time, and
            // the kept reading still carries the time it was actually taken.
            Assert.Same(first, second);
            Assert.Equal(Minute, second!.WhenTicks);
        }

        [Fact]
        public void Asking_again_rescans_even_when_there_is_a_reading()
        {
            var (doc, view, _, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);

            var cache = new SceneCache();
            var first = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null);

            // Somebody else coloured something; the user presses it again.
            view.SeedForeignColour(items[1].Key, Blue);

            var second = cache.Read(view, SceneCache.LiveScene, doc.Generation, 2 * Minute, true, null);

            Assert.NotSame(first, second);
            Assert.Equal(2 * Minute, second!.WhenTicks);
            Assert.Equal(2, second.Report.Origins.Count);
        }

        [Fact]
        public void A_model_that_changed_voids_every_reading_and_not_just_one()
        {
            var (doc, view, views, items) = Model(40);

            views.Define("Looking north", One(items[1], Blue));
            views.Define("Looking south", One(items[2], Red));

            var cache = new SceneCache();

            // Two viewpoints rather than the live scene and one, because reading a viewpoint drops
            // the live scene's reading by design -- it replaced it.
            cache.Put("Looking north", views.ReadOrigins("Looking north")!, doc.Generation, Minute);
            cache.Put("Looking south", views.ReadOrigins("Looking south")!, doc.Generation, Minute);

            Assert.Equal(2, cache.Count);

            // A model appended. Every reading describes a tree that no longer exists, and the items
            // inside them are handles into it.
            doc.BumpGeneration("append MEP");

            Assert.Null(cache.Get("Looking north", doc.Generation));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void A_stopped_scan_is_never_kept()
        {
            var (doc, view, _, items) = Model(40);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var cache = new SceneCache();

            var stopped = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute,
                                     false, seen => seen < 5);

            // A stopped scan describes the part of a model somebody lost patience with. Handing
            // that back later as the answer is the one failure a cache must not have.
            Assert.Null(stopped);
            Assert.Equal(0, cache.Count);
            Assert.Null(cache.Get(SceneCache.LiveScene, doc.Generation));
        }

        [Fact]
        public void Writing_to_the_scene_is_what_drops_the_scenes_reading()
        {
            var (doc, view, _, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);

            var cache = new SceneCache();
            cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null);

            Assert.NotNull(cache.Get(SceneCache.LiveScene, doc.Generation));

            Assert.True(cache.Forget(SceneCache.LiveScene));
            Assert.Null(cache.Get(SceneCache.LiveScene, doc.Generation));

            // Nothing to drop the second time, and that is not an error.
            Assert.False(cache.Forget(SceneCache.LiveScene));
        }

        [Fact]
        public void Reading_a_viewpoint_needs_no_apply_and_is_kept_like_any_other_scene()
        {
            var (doc, view, views, items) = Model(40);

            views.Define("Looking north", One(items[1], Blue));

            var cache = new SceneCache();

            var records = views.ReadOrigins("Looking north")!;
            var first = cache.Put("Looking north", records, doc.Generation, Minute);

            var second = cache.Get("Looking north", doc.Generation);

            // Kept, and nothing was applied to get it. Reading a viewpoint reads its own records,
            // so the live scene is untouched and the view has not moved.
            Assert.Same(first, second);
            Assert.Equal(0, views.Applied);
        }

        [Fact]
        public void Reading_a_viewpoint_leaves_the_live_scenes_reading_alone()
        {
            var (doc, view, views, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);
            views.Define("Looking north", One(items[1], Blue));

            var cache = new SceneCache();

            cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null);
            Assert.NotNull(cache.Get(SceneCache.LiveScene, doc.Generation));

            cache.Put("Looking north", views.ReadOrigins("Looking north")!, doc.Generation, Minute);

            // Still there. A viewpoint is read from its own records, so nothing about the live
            // scene changed and its reading is still of the model.
            Assert.NotNull(cache.Get(SceneCache.LiveScene, doc.Generation));
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void Reading_a_viewpoint_reports_what_that_viewpoint_put_on_the_model()
        {
            var (doc, view, views, items) = Model(40);

            // A viewpoint that colours one storey and hides another. Two records, because that
            // is two acts — the host stores an override against the item it was applied to, so a
            // storey-wide colour is one entry naming the storey and not forty naming its elements.
            views.Define("Structure only",
                         new[] { new KeyValuePair<string[], Colour>(new[] { "ARCH", "Level 03" }, Red) },
                         new[] { new[] { "ARCH", "Level 04" } });

            var records = views.ReadOrigins("Structure only");

            Assert.NotNull(records);

            var origins = records!.Origins;

            Assert.Equal(2, origins.Count);

            var hiding = origins.Single(o => o.Origin.IsHidden);
            Assert.Equal("ARCH / Level 04", hiding.Path);
            Assert.True(hiding.Origin.IsRead);

            var colour = origins.Single(o => !o.Origin.IsHidden);
            Assert.Equal("ARCH / Level 03", colour.Path);
            Assert.Equal(40, colour.Origin.Covers);
        }

        [Fact]
        public void A_viewpoint_that_is_not_there_reads_as_null_and_says_why()
        {
            var (doc, view, views, _) = Model(10);

            var cache = new SceneCache();

            Assert.Null(views.ReadOrigins("Nowhere"));
            Assert.Contains("Nowhere", views.LastProblem);
            Assert.Equal(0, views.Applied);

            _ = cache;
        }

        [Fact]
        public void A_reading_says_how_old_it_is_in_words()
        {
            var (doc, view, _, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);

            var cache = new SceneCache();
            var reading = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null)!;

            Assert.Equal(TimeSpan.Zero, reading.Age(Minute));
            Assert.Contains("a moment ago", reading.Describe(Minute + 10 * 10_000_000L));
            Assert.Contains("20 min ago", reading.Describe(Minute + 20 * Minute));
            Assert.Contains("3 h ago", reading.Describe(Minute + 180 * Minute));

            // The size of the thing that produced it travels with it, so a cached table can still
            // say what it was read over.
            Assert.Contains("45 elements", reading.Describe(Minute));
        }

        [Fact]
        public void A_viewpoints_level_is_read_rather_than_worked_out_from_the_model()
        {
            var (doc, _, views, _) = Model(40);

            views.Define("Structure red",
                         new[] { new KeyValuePair<string[], Colour>(new[] { "ARCH", "Level 03" }, Red) });

            var only = Assert.Single(views.ReadOrigins("Structure red")!.Origins);

            // One record naming the storey, covering its forty elements. This is the thing a scan
            // of the live scene cannot manage: the document keeps a colour on the geometry and no
            // note of the level, so scanning has to infer it back from the subtree. A viewpoint
            // keeps the note, so there is nothing to infer.
            Assert.Equal("ARCH / Level 03", only.Path);
            Assert.Equal(40, only.Origin.Covers);
            Assert.Equal(Red, only.Origin.Colour);
            Assert.True(only.Origin.IsRead);
        }

        [Fact]
        public void A_viewpoint_that_records_forty_separate_acts_reports_forty()
        {
            var (_, _, views, items) = Model(40);

            // Nobody coloured the storey: forty elements coloured one at a time, which is forty
            // records. Rolling them up to the storey would claim an act that did not happen, and
            // the viewpoint is not guessing - it knows which it was.
            views.Define("Spotty",
                         items.Select(i => new KeyValuePair<string[], Colour>(
                             i.TreePath.ToArray(), Red)).ToList());

            var origins = views.ReadOrigins("Spotty")!.Origins;

            Assert.Equal(40, origins.Count);
            Assert.All(origins, o => Assert.Equal(1, o.Origin.Covers));
        }

        [Fact]
        public void Reading_a_viewpoint_applies_nothing_and_leaves_the_model_as_it_was()
        {
            var (doc, view, views, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);

            views.Define("Elsewhere",
                         new[] { new KeyValuePair<string[], Colour>(new[] { "ARCH", "Level 04" }, Blue) },
                         new[] { new[] { "ARCH", "Level 03" } });

            var records = views.ReadOrigins("Elsewhere");

            Assert.NotNull(records);
            Assert.Equal(2, records!.Origins.Count);

            // Nothing applied, nothing hidden, and the one override that was on the model before
            // is still the only one on it. A read that moved the view and replaced what was
            // coloured would be a destructive answer to a question.
            Assert.Equal(0, views.Applied);
            Assert.False(doc.IsBranchHidden(new[] { "ARCH", "Level 03" }));

            var live = Assert.Single(view.ScanAppearance(TraversalScope.WholeDocument, null));
            Assert.Equal(items[0].Key, live.Key);
        }

        [Fact]
        public void A_viewpoints_records_report_no_element_count_because_nothing_was_walked()
        {
            var (_, _, views, _) = Model(40);

            views.Define("Structure red",
                         new[] { new KeyValuePair<string[], Colour>(new[] { "ARCH", "Level 03" }, Red) });

            var records = views.ReadOrigins("Structure red")!;

            // Zero, and not forty-five. The records are sparse and name their own nodes, so there
            // is no "of N elements scanned" to report — claiming one would describe a walk that
            // never happened.
            Assert.Equal(0, records.Elements);
            Assert.False(records.Stopped);
        }

        // -----------------------------------------------------------------------------------
        // Following: a scene's stack going on when the host arrives at its viewpoint
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_watcher_hears_the_user_going_to_a_viewpoint()
        {
            var (_, _, views, _) = Model(10);

            views.Define("Looking north");
            views.Define("Looking south");

            var heard = new List<string?>();

            views.WatchViewpoint(v => heard.Add(v));

            views.UserWentTo("Looking north");
            views.UserWentTo("Looking south");

            Assert.Equal(new[] { "Looking north", "Looking south" }, heard.ToArray());
        }

        [Fact]
        public void A_watcher_does_not_hear_this_product_applying_a_viewpoint()
        {
            var (_, _, views, _) = Model(10);

            views.Define("Looking north");

            var heard = 0;

            views.WatchViewpoint(_ => heard++);

            views.Apply("Looking north");

            // The one property that makes following safe. Apply is how a stack gets onto a
            // viewpoint, so hearing our own Apply would apply the stack again, and again.
            Assert.Equal(0, heard);
            Assert.Equal("Looking north", views.OnViewpoint);
        }

        [Fact]
        public void A_second_watcher_replaces_the_first_rather_than_joining_it()
        {
            var (_, _, views, _) = Model(10);

            views.Define("Looking north");

            var first = 0;
            var second = 0;

            views.WatchViewpoint(_ => first++);
            views.WatchViewpoint(_ => second++);

            views.UserWentTo("Looking north");

            // One watcher at a time, so a screen rebuilt ten times does not apply a stack ten
            // times on the next navigation. A subscribe/unsubscribe pair would need every screen
            // to remember to let go, and the failure when one forgets is silent.
            Assert.Equal(0, first);
            Assert.Equal(1, second);
        }

        [Fact]
        public void Nothing_is_heard_once_the_watching_stops()
        {
            var (_, _, views, _) = Model(10);

            views.Define("Looking north");

            var heard = 0;

            views.WatchViewpoint(_ => heard++);
            views.WatchViewpoint(null);

            views.UserWentTo("Looking north");

            Assert.Equal(0, heard);
        }

        [Fact]
        public void The_viewpoint_a_seat_is_on_is_none_before_anybody_goes_anywhere()
        {
            var (_, _, views, _) = Model(10);

            views.Define("Looking north");

            // Null means do nothing, which is what a stack applied automatically has to do when
            // the host cannot say which viewpoint it is on: applying the wrong scene's stack
            // would overwrite a scheme with somebody else's.
            Assert.Null(views.OnViewpoint);
        }

        [Fact]
        public void The_live_scene_and_a_viewpoint_are_kept_apart()
        {
            var (doc, view, views, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);
            views.Define("Looking north", One(items[1], Blue));

            var cache = new SceneCache();

            var live = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null)!;

            Assert.True(live.IsLiveScene);

            cache.Put("Looking north", views.ReadOrigins("Looking north")!, doc.Generation, Minute);

            var kept = cache.Get("Looking north", doc.Generation)!;

            Assert.False(kept.IsLiveScene);
            Assert.Equal("Looking north", kept.Key);
        }
    }
}
