using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashPickTests
    {
        private static ClashItem Item(string name, FindingStatus status = FindingStatus.New,
                                      string? party = null, double z = 0,
                                      string test = "Hard", string? level = "L03")
        {
            var item = ClashFixture.Item(name, level: level, z: z, test: test);

            item.Name = name;
            item.Status = status;
            item.AssignedTo = party;

            return item;
        }

        private static LevelPlan Storeys()
        {
            var plan = new LevelPlan();

            plan.Add("Ground", 0);
            plan.Add("Level 1", 4);
            plan.Add("Level 2", 8);

            return plan;
        }

        // -----------------------------------------------------------------------------------
        // Narrowing
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_pick_with_no_clauses_takes_everything()
        {
            var pick = new ClashPick();

            Assert.True(pick.IsEverything);
            Assert.Equal(3, pick.Take(new[] { Item("a"), Item("b"), Item("c") }).Count);
            Assert.Equal("Every clash.", pick.Describe());
        }

        [Fact]
        public void One_clause_can_name_several_values()
        {
            // "New or active" is one thought, so it is one clause. Said as two clauses they would
            // both have to hold, and between them they would match nothing.
            var pick = new ClashPick();
            var clause = pick.Add("status");

            clause.Values.Add("New");
            clause.Values.Add("Active");

            var kept = pick.Take(new[]
            {
                Item("a", FindingStatus.New),
                Item("b", FindingStatus.Active),
                Item("c", FindingStatus.Resolved),
            });

            Assert.Equal(new[] { "a", "b" }, kept.Select(one => one.Name));
        }

        [Fact]
        public void Every_clause_has_to_hold()
        {
            var pick = new ClashPick();

            pick.Add("status").Values.Add("New");
            pick.Add("assignee").Values.Add("Mechanical");

            var kept = pick.Take(new[]
            {
                Item("a", FindingStatus.New, "Mechanical"),
                Item("b", FindingStatus.New, "Structure"),
                Item("c", FindingStatus.Resolved, "Mechanical"),
            });

            Assert.Equal("a", Assert.Single(kept).Name);
        }

        [Fact]
        public void A_clause_naming_a_facet_nothing_carries_matches_nothing()
        {
            // Not everything. A saved filter whose property has gone out of the models must not
            // quietly widen into sending a stakeholder the whole job.
            var pick = new ClashPick();

            pick.Add(ClashFacets.PropertyPrefix + "Phase").Values.Add("P2");

            Assert.Empty(pick.Take(new[] { Item("a"), Item("b") }));
        }

        [Fact]
        public void A_number_clause_refuses_a_result_that_reported_no_number()
        {
            // No overlap volume is not a volume of zero, so a result whose engine reported none
            // cannot answer a question about it either way.
            var pick = new ClashPick();
            var clause = pick.Add("overlap", ClashCompare.AtMost);

            clause.High = 0.01;

            var small = Item("a");
            var silent = Item("b");

            small.OverlapVolume = 0.001;

            Assert.Equal("a", Assert.Single(pick.Take(new[] { small, silent })).Name);
        }

        [Fact]
        public void Empty_and_filled_are_askable()
        {
            var unassigned = new ClashPick();
            var assigned = new ClashPick();

            unassigned.Add("assignee", ClashCompare.IsEmpty);
            assigned.Add("assignee", ClashCompare.IsFilled);

            var items = new[] { Item("a", party: "Mechanical"), Item("b") };

            Assert.Equal("b", Assert.Single(unassigned.Take(items)).Name);
            Assert.Equal("a", Assert.Single(assigned.Take(items)).Name);
        }

        // -----------------------------------------------------------------------------------
        // Storeys
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Clashes_can_be_picked_by_the_storey_they_stand_on()
        {
            // The one a coordinator asks for by name. Elevation is a number in the file; the
            // storey is what the project called that band of it.
            var plan = Storeys();
            var pick = new ClashPick();

            pick.Add("storey").Values.Add("Level 1");

            var kept = pick.Take(new[]
            {
                Item("a", z: 1),
                Item("b", z: 5),
                Item("c", z: 9),
            }, plan);

            Assert.Equal("b", Assert.Single(kept).Name);
        }

        [Fact]
        public void Without_a_plan_there_is_no_storey_facet_to_offer()
        {
            Assert.DoesNotContain(ClashFacets.Standing(), one => one.Key == "storey");
            Assert.Contains(ClashFacets.Standing(Storeys()), one => one.Key == "storey");
        }

        [Fact]
        public void Elevation_can_be_picked_as_a_band()
        {
            var pick = new ClashPick();
            var clause = pick.Add("elevation", ClashCompare.Between);

            clause.Low = 4;
            clause.High = 8;

            var kept = pick.Take(new[] { Item("a", z: 1), Item("b", z: 6), Item("c", z: 12) });

            Assert.Equal("b", Assert.Single(kept).Name);
        }

        // -----------------------------------------------------------------------------------
        // Facets from the models
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_property_the_models_carry_is_a_facet()
        {
            var one = Item("a");
            var two = Item("b");

            one.Properties["Element/Phase"] = "P2";
            two.Properties["Element/Phase"] = "P3";

            var facets = ClashFacets.Every(new[] { one, two });

            Assert.Contains(facets, f => f.Key == ClashFacets.PropertyPrefix + "Element/Phase");

            var pick = new ClashPick();

            pick.Add(ClashFacets.PropertyPrefix + "Element/Phase").Values.Add("P2");

            Assert.Equal("a", Assert.Single(pick.Take(new[] { one, two })).Name);
        }

        [Fact]
        public void The_values_a_facet_holds_are_offered_without_repeats()
        {
            var items = new[]
            {
                Item("a", party: "Mechanical"),
                Item("b", party: "mechanical"),
                Item("c", party: "Structure"),
                Item("d"),
            };

            var facet = ClashFacets.Of("assignee");

            Assert.Equal(new[] { "Mechanical", "Structure" }, ClashFacets.Values(facet, items));
        }

        // -----------------------------------------------------------------------------------
        // Ordering
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_number_facet_sorts_as_numbers()
        {
            // Sorted as text, elevation runs 10, 100, 2 - which is the one order nobody asked for.
            var pick = new ClashPick { Sort = "elevation" };

            var sorted = pick.Take(new[] { Item("a", z: 100), Item("b", z: 2), Item("c", z: 10) });

            Assert.Equal(new[] { "b", "c", "a" }, sorted.Select(one => one.Name));
        }

        [Fact]
        public void The_order_can_run_backwards()
        {
            var pick = new ClashPick { Sort = "name", Descending = true };

            var sorted = pick.Take(new[] { Item("a"), Item("c"), Item("b") });

            Assert.Equal(new[] { "c", "b", "a" }, sorted.Select(one => one.Name));
        }

        // -----------------------------------------------------------------------------------
        // Saving
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_pick_survives_the_project_file()
        {
            var pick = new ClashPick { Sort = "elevation", Descending = true };

            pick.Add("status").Values.Add("New");

            var band = pick.Add("elevation", ClashCompare.Between);

            band.Low = 4;
            band.High = 8;

            var read = ClashPick.FromJson(JsonReader.Parse(pick.ToJson()!.ToString()));

            Assert.Equal("elevation", read.Sort);
            Assert.True(read.Descending);
            Assert.Equal(2, read.Clauses.Count);
            Assert.Equal("New", Assert.Single(read.Clauses[0].Values));
            Assert.Equal(ClashCompare.Between, read.Clauses[1].Compare);
            Assert.Equal(4, read.Clauses[1].Low);
            Assert.Equal(8, read.Clauses[1].High);
        }

        [Fact]
        public void A_pick_that_narrows_nothing_writes_nothing()
        {
            Assert.Null(new ClashPick().ToJson());
            Assert.True(ClashPick.FromJson(null).IsEverything);
        }

        [Fact]
        public void A_pick_says_what_it_is_doing()
        {
            var pick = new ClashPick();
            var status = pick.Add("status");

            status.Values.Add("New");
            status.Values.Add("Active");

            pick.Add("assignee", ClashCompare.IsEmpty);

            Assert.Equal("Status is “New” or “Active”, Assigned to is empty.",
                         pick.Describe());
        }
    }
}
