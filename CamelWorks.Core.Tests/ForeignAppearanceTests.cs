using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ForeignAppearanceTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");

        private static ElementKey K(string name) => ElementKey.FromTreePath(Scope, @"C:\p\ARCH.nwc", name);

        private static readonly Colour Red = new Colour(200, 30, 30);
        private static readonly Colour Blue = new Colour(30, 30, 200);

        private static AppearanceState Pristine(ElementKey key) => new AppearanceState(key, null, null, false, false);

        private static AppearanceState Hidden(ElementKey key) => new AppearanceState(key, null, null, true, false);

        private static AppearanceState Painted(ElementKey key, Colour colour) =>
            new AppearanceState(key, colour, null, false, true);

        private static AppearanceState Faded(ElementKey key, double transparency) =>
            new AppearanceState(key, null, transparency, false, true);

        [Fact]
        public void An_untouched_model_reports_nothing_and_says_so()
        {
            var audit = ForeignAppearance.Find(new[] { Pristine(K("a")), Pristine(K("b")) }, null);

            Assert.True(audit.IsClean);
            Assert.Equal(2, audit.Scanned);
            Assert.Equal(0, audit.Total);
            Assert.Contains("none of them touched", audit.Explain());
        }

        [Fact]
        public void Elements_in_the_same_state_come_back_as_one_row()
        {
            var audit = ForeignAppearance.Find(new[]
            {
                Painted(K("a"), Red), Painted(K("b"), Red), Painted(K("c"), Blue), Pristine(K("d")),
            }, null);

            Assert.Equal(2, audit.Groups.Count);
            Assert.Equal(3, audit.Total);

            var biggest = audit.Groups[0];
            Assert.Equal(2, biggest.Keys.Count);
            Assert.Equal(Red, biggest.Colour);
            Assert.Equal("coloured #c81e1e", biggest.Describe());
        }

        [Fact]
        public void Hidden_counts_even_though_nothing_was_painted()
        {
            // The host only flags a colour or transparency override as foreign; a hidden element
            // carries no such flag. It is still not in its original state, and "what is hidden" is
            // the question this screen exists to answer.
            var audit = ForeignAppearance.Find(new[] { Hidden(K("a")), Hidden(K("b")), Pristine(K("c")) }, null);

            Assert.Equal(2, audit.Hidden);
            Assert.Equal("hidden", audit.Groups.Single().Describe());
        }

        [Fact]
        public void A_layer_that_decides_something_owns_its_elements()
        {
            var mine = K("a");
            var theirs = K("b");

            var layer = new AppearanceLayer("l1", "Mine", LayerTarget.Elements(new[] { mine })) { Colour = Red };
            var fold = AppearanceStack.Fold(new[] { layer }, new FakeResolver(mine, theirs));

            var audit = ForeignAppearance.Find(new[] { Painted(mine, Red), Painted(theirs, Red) }, fold);

            Assert.Equal(1, audit.Claimed);
            Assert.Equal(theirs, audit.Groups.Single().Keys.Single());
        }

        [Fact]
        public void A_switched_off_layer_claims_nothing()
        {
            // Disabling a layer must not hand it ownership of an override it never applied — the
            // element goes back to being somebody else's, which is what it was all along.
            var key = K("a");

            var layer = new AppearanceLayer("l1", "Off", LayerTarget.Elements(new[] { key }))
            {
                Colour = Red,
                IsEnabled = false,
            };

            var fold = AppearanceStack.Fold(new[] { layer }, new FakeResolver(key));
            var audit = ForeignAppearance.Find(new[] { Painted(key, Red) }, fold);

            Assert.Equal(0, audit.Claimed);
            Assert.Equal(key, audit.Groups.Single().Keys.Single());
        }

        [Fact]
        public void Transparency_that_reads_back_a_step_off_is_still_one_state()
        {
            // 0.5 written comes back as 128/255. Comparing exactly would split one override into
            // two rows that read identically on screen.
            var audit = ForeignAppearance.Find(new[]
            {
                Faded(K("a"), 0.5), Faded(K("b"), 128.0 / 255.0),
            }, null);

            Assert.Equal(2, audit.Groups.Single().Keys.Count);
        }

        [Fact]
        public void Combined_state_reads_as_one_phrase()
        {
            var state = new AppearanceState(K("a"), Red, 0.5, true, true);
            var audit = ForeignAppearance.Find(new[] { state }, null);

            Assert.Equal("hidden and coloured #c81e1e and 50% transparent", audit.Groups.Single().Describe());
        }

        [Fact]
        public void Past_the_cap_the_rest_are_counted_rather_than_dropped_silently()
        {
            var states = Enumerable.Range(0, 10)
                .Select(n => Painted(K("e" + n), new Colour((byte)n, 0, 0)))
                .ToList();

            var audit = ForeignAppearance.Find(states, null, groupCap: 4);

            Assert.Equal(4, audit.Groups.Count);
            Assert.Equal(6, audit.OtherGroups);
            Assert.Equal(6, audit.OtherElements);
            Assert.Equal(10, audit.Total);
            Assert.Contains("10 distinct states", audit.Explain());
        }

        [Fact]
        public void The_same_model_twice_gives_the_same_order()
        {
            var states = new[]
            {
                Painted(K("a"), Red), Painted(K("b"), Blue),
                Painted(K("c"), Red), Painted(K("d"), Blue), Hidden(K("e")),
            };

            var first = ForeignAppearance.Find(states, null);
            var second = ForeignAppearance.Find(states.Reverse().ToList(), null);

            Assert.Equal(first.Groups.Select(g => g.Describe()), second.Groups.Select(g => g.Describe()));
        }

        [Fact]
        public void Adopting_a_group_reproduces_it_without_moving_anything()
        {
            var group = ForeignAppearance
                .Find(new[] { new AppearanceState(K("a"), Red, 0.25, true, true) }, null)
                .Groups.Single();

            var layer = ForeignAppearance.Adopt(group, "adopted-1");

            Assert.Equal(Red, layer.Colour);
            Assert.Equal(0.25, layer.Transparency!.Value);
            Assert.False(layer.Visible);
            Assert.Equal(group.Keys, layer.Target.Keys);
            Assert.False(layer.Target.ReResolves);
        }

        [Fact]
        public void Adopting_something_visible_does_not_claim_visibility()
        {
            // A layer that asserted "visible" on everything it adopted would start overruling the
            // layers beneath it, and adopting is supposed to change nothing about the picture.
            var group = ForeignAppearance.Find(new[] { Painted(K("a"), Red) }, null).Groups.Single();

            Assert.Null(ForeignAppearance.Adopt(group, "adopted-1").Visible);
        }

        [Fact]
        public void An_adopted_layer_makes_the_state_stop_being_foreign()
        {
            var key = K("a");
            var states = new[] { Painted(key, Red) };

            var group = ForeignAppearance.Find(states, null).Groups.Single();
            var layer = ForeignAppearance.Adopt(group, "adopted-1");
            var fold = AppearanceStack.Fold(new[] { layer }, new FakeResolver(key));

            Assert.True(ForeignAppearance.Find(states, fold).IsClean);
        }
    }
}
