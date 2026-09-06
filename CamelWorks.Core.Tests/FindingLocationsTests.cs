using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// One finding, many locations: the register's answer to a specification that fails a thousand
    /// elements.
    ///
    /// A location is where a finding is. It is never a register entry, so it has no status and
    /// <see cref="FindingRegister.Find"/> does not answer for it; it is looked up through the
    /// register's own location index instead.
    /// </summary>
    public class FindingLocationsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("ARCH.nwc");

        private static ElementKey Wall(int n) =>
            ElementKey.FromTreePath(Scope, "ARCH.nwc", "Wall " + n);

        private static Finding Spec(int locations)
        {
            var finding = Finding.CreateParent(FindingSource.Ids, "IDS", "Walls missing FireRating",
                                               "spec-fire-rating");

            for (var n = 0; n < locations; n++)
                finding.AddLocation(new FindingLocation("wall-" + n, new[] { Wall(n) },
                                                        "no FireRating", n, 0, 0));

            return finding;
        }

        [Fact]
        public void A_thousand_locations_round_trip_and_do_not_change_the_id()
        {
            var thousand = Spec(1000);
            var register = new FindingRegister();
            register.Put(thousand);

            var back = FindingRegister.FromJson(JsonReader.Parse(register.ToJson().ToJson()));
            var read = back.All.Single();

            Assert.Equal(1000, read.Locations.Count);
            Assert.Equal(thousand.Id, read.Id);
            Assert.Equal(Spec(400).Id, read.Id);

            var first = read.Locations[0];

            Assert.Equal("wall-0", first.Id);
            Assert.Equal("no FireRating", first.Detail);
            Assert.Equal(0, first.X);
            Assert.Single(first.Elements);
        }

        [Fact]
        public void A_note_on_one_place_survives_the_sidecar()
        {
            var finding = Spec(2);
            finding.Locations[1].Notes.Add(
                new FindingComment("A Coordinator", 42, "void former, not a wall"));
            finding.Locations[1].Facts["Why"] = "PropertyMissing";

            var register = new FindingRegister();
            register.Put(finding);

            var read = FindingRegister.FromJson(JsonReader.Parse(register.ToJson().ToJson()))
                                      .All.Single();

            Assert.Equal("void former, not a wall", read.Locations[1].Notes.Single().Body);
            Assert.Equal("PropertyMissing", read.Locations[1].Facts["Why"]);
        }

        [Fact]
        public void A_location_is_found_through_its_owner_and_never_as_a_finding()
        {
            var finding = Spec(3);
            var register = new FindingRegister();
            register.Put(finding);

            Assert.Null(register.Find("wall-1"));
            Assert.Equal("wall-1", register.FindLocation("wall-1")!.Id);
            Assert.Same(finding, register.OwnerOfLocation("wall-1"));

            Assert.Null(register.FindLocation("wall-9"));
            Assert.Null(register.OwnerOfLocation(null));
        }

        [Fact]
        public void Locations_are_counted_apart_from_findings()
        {
            // "37 rules, 1,204 locations" rather than implying 37 problems are 1,204.
            var register = new FindingRegister();
            register.Put(Spec(3));

            Assert.Equal(1, register.Count);
            Assert.Equal(3, register.LocationCount);
            Assert.Equal(1, register.Open);
        }

        [Fact]
        public void The_index_is_dropped_when_the_register_changes()
        {
            var register = new FindingRegister();
            register.Put(Spec(3));

            Assert.Equal(3, register.LocationCount);

            register.Put(Spec(5));
            Assert.Equal(5, register.LocationCount);

            register.Observe(FindingSource.Ids, new[] { Spec(2) }, 1);
            Assert.Equal(2, register.LocationCount);
            Assert.Null(register.FindLocation("wall-4"));

            register.Remove(Spec(1).Id);
            Assert.Equal(0, register.LocationCount);

            register.Put(Spec(1));
            register.Clear();
            Assert.Equal(0, register.LocationCount);
        }
    }
}
