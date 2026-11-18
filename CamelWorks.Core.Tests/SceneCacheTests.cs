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

            views.Define("Looking north", new Dictionary<ElementKey, Colour> { [items[1].Key] = Blue });
            views.Define("Looking south", new Dictionary<ElementKey, Colour> { [items[2].Key] = Red });

            var cache = new SceneCache();

            // Two viewpoints rather than the live scene and one, because reading a viewpoint drops
            // the live scene's reading by design -- it replaced it.
            cache.ReadViewpoint(views, view, "Looking north", doc.Generation, Minute, false, null);
            cache.ReadViewpoint(views, view, "Looking south", doc.Generation, Minute, false, null);

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
        public void Reading_a_viewpoint_applies_it_even_when_the_reading_is_already_kept()
        {
            var (doc, view, views, items) = Model(40);

            views.Define("Looking north", new Dictionary<ElementKey, Colour> { [items[1].Key] = Blue });

            var cache = new SceneCache();

            var first = cache.ReadViewpoint(views, view, "Looking north", doc.Generation, Minute, false, null);
            Assert.Equal(1, views.Applied);

            var second = cache.ReadViewpoint(views, view, "Looking north", doc.Generation, 2 * Minute, false, null);

            // The scan was skipped; the apply was not. A table that appeared without the model
            // moving would describe a scene that is not on screen.
            Assert.Same(first, second);
            Assert.Equal(2, views.Applied);
        }

        [Fact]
        public void Reading_a_viewpoint_drops_the_live_scenes_reading_because_it_replaced_it()
        {
            var (doc, view, views, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);
            views.Define("Looking north", new Dictionary<ElementKey, Colour> { [items[1].Key] = Blue });

            var cache = new SceneCache();

            cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null);
            Assert.NotNull(cache.Get(SceneCache.LiveScene, doc.Generation));

            cache.ReadViewpoint(views, view, "Looking north", doc.Generation, Minute, false, null);

            // Applying a viewpoint replaces what is coloured and what is hidden, so whatever was
            // read of the live scene describes something no longer on the model.
            Assert.Null(cache.Get(SceneCache.LiveScene, doc.Generation));
        }

        [Fact]
        public void Reading_a_viewpoint_reports_what_that_viewpoint_put_on_the_model()
        {
            var (doc, view, views, items) = Model(40);

            // A viewpoint that colours the storey and hides another one.
            var colours = items.ToDictionary(i => i.Key, _ => Red);

            views.Define("Structure only", colours, new[] { new[] { "ARCH", "Level 04" } });

            var reading = new SceneCache().ReadViewpoint(views, view, "Structure only",
                                                         doc.Generation, Minute, false, null);

            Assert.NotNull(reading);

            var origins = reading!.Report.Origins;

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

            Assert.Null(cache.ReadViewpoint(views, view, "Nowhere", doc.Generation, Minute, false, null));
            Assert.Contains("Nowhere", views.LastProblem);
            Assert.Equal(0, views.Applied);
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
        public void The_live_scene_and_a_viewpoint_are_kept_apart()
        {
            var (doc, view, views, items) = Model(40);

            view.SeedForeignColour(items[0].Key, Red);
            views.Define("Looking north", new Dictionary<ElementKey, Colour> { [items[1].Key] = Blue });

            var cache = new SceneCache();

            var live = cache.Read(view, SceneCache.LiveScene, doc.Generation, Minute, false, null)!;

            Assert.True(live.IsLiveScene);

            cache.ReadViewpoint(views, view, "Looking north", doc.Generation, Minute, false, null);

            var kept = cache.Get("Looking north", doc.Generation)!;

            Assert.False(kept.IsLiveScene);
            Assert.Equal("Looking north", kept.Key);
        }
    }
}
