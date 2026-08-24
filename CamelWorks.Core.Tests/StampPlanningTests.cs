using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The planning half of a stamp — what Zones now runs on its first press, before anything is
    /// written.
    ///
    /// The rule these pin down is the one that made the two-press shape safe to adopt: an element
    /// the rule cannot place is left <em>out of scope</em>, never carried in holding a null. A null
    /// in a diff is a proposal to blank whatever the element already has, and the elements a grid
    /// or a level band cannot place are exactly the ones most likely to be holding a value somebody
    /// entered by hand.
    /// </summary>
    public class StampPlanningTests
    {
        private const string Tab = "CamelWorks";

        private static (FakeDocument Doc, List<ElementKey> Keys) Model(params string?[] existing)
        {
            var doc = new FakeDocument();
            var source = doc.AddModel("structure", "S.nwc");
            var keys = new List<ElementKey>();

            for (var i = 0; i < existing.Length; i++)
            {
                var item = doc.AddItem(source, "Element " + i);
                if (existing[i] != null) item.WithProperty(Tab, "Grid", existing[i]);
                keys.Add(item.Key);
            }

            return (doc, keys);
        }

        [Fact]
        public void An_element_the_rule_cannot_place_is_left_out_of_scope()
        {
            var (doc, keys) = Model(null, "B-2", null);

            // Only the middle one can be placed. The other two are dropped from scope entirely,
            // which is what the panel does with a null reference.
            var placed = new Dictionary<ElementKey, string> { [keys[1]] = "C-4" };

            var diff = PropertyDiff.Of(Tab, "Grid",
                PropertyDiff.Resolve(doc, placed.Keys),
                item => placed[item.Key]);

            Assert.Equal(1, diff.Scope);
            Assert.Equal(0, diff.Cleared);
            Assert.Equal(1, diff.Writes(ExistingValues.Overwrite));
        }

        [Fact]
        public void Carrying_an_unplaceable_element_in_would_propose_to_blank_it()
        {
            // The shape the panel must not use, pinned so the reason the other one exists stays
            // visible: pass every element and let the rule return null, and the two it could not
            // place turn into a proposal to empty a value somebody typed.
            var (doc, keys) = Model("A-1", "B-2", null);

            var placed = new Dictionary<ElementKey, string?>
            {
                [keys[0]] = null,
                [keys[1]] = "C-4",
                [keys[2]] = null,
            };

            var diff = PropertyDiff.Of(Tab, "Grid",
                PropertyDiff.Resolve(doc, placed.Keys),
                item => placed[item.Key]);

            Assert.Equal(1, diff.Cleared);
        }

        [Fact]
        public void A_value_already_equal_to_the_derived_one_is_not_a_write()
        {
            var (doc, keys) = Model("C-4", "A-1");

            var placed = new Dictionary<ElementKey, string> { [keys[0]] = "C-4", [keys[1]] = "C-4" };

            var diff = PropertyDiff.Of(Tab, "Grid",
                PropertyDiff.Resolve(doc, placed.Keys),
                item => placed[item.Key]);

            Assert.Equal(1, diff.Unchanged);
            Assert.Equal(1, diff.Writes(ExistingValues.Overwrite));
        }

        [Fact]
        public void Nothing_placeable_means_no_second_press()
        {
            var (doc, _) = Model(null, null);

            var diff = PropertyDiff.Of(Tab, "Grid",
                PropertyDiff.Resolve(doc, new List<ElementKey>()),
                item => "unused");

            Assert.Equal(0, diff.Scope);
            Assert.Equal(0, diff.Writes(ExistingValues.Overwrite));
        }
    }
}
