using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
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
            Assert.NotNull(register.FindLocation("wall-4"));

            register.Observe(FindingSource.Ids, new[] { Spec(2) }, 1);
            Assert.Equal(2, register.LocationCount);
            Assert.Null(register.FindLocation("wall-4"));

            register.Remove(Spec(1).Id);
            Assert.Equal(0, register.LocationCount);

            // The lookup is built before the Clear, so the Clear has an index to drop. Asserting
            // the count alone would pass on a register that never built one.
            register.Put(Spec(1));
            Assert.NotNull(register.FindLocation("wall-0"));

            register.Clear();
            Assert.Equal(0, register.LocationCount);
            Assert.Null(register.FindLocation("wall-0"));
        }

        [Fact]
        public void A_note_survives_a_run_that_still_reports_the_place()
        {
            // The producers build fresh locations with no notes on them, so a run that reports the
            // same place again must carry the note across or every re-read wipes what a coordinator
            // wrote.
            var register = new FindingRegister();
            register.Put(Spec(3));

            register.FindLocation("wall-1")!.Notes.Add(
                new FindingComment("A Coordinator", 42, "void former, not a wall"));

            register.Observe(FindingSource.Ids, new[] { Spec(3) }, 100);

            Assert.Equal("void former, not a wall",
                         register.FindLocation("wall-1")!.Notes.Single().Body);

            // And is not doubled by the run after that.
            register.Observe(FindingSource.Ids, new[] { Spec(3) }, 200);

            Assert.Single(register.FindLocation("wall-1")!.Notes);
        }

        [Fact]
        public void A_note_goes_with_the_place_the_run_stops_reporting()
        {
            // Stated rather than worked around: a note is written against one place, and a place the
            // run no longer reports is not on the finding any more.
            var register = new FindingRegister();
            register.Put(Spec(3));

            register.FindLocation("wall-2")!.Notes.Add(
                new FindingComment("A Coordinator", 42, "void former, not a wall"));

            register.Observe(FindingSource.Ids, new[] { Spec(2) }, 100);

            Assert.Null(register.FindLocation("wall-2"));
            Assert.Equal(2, register.LocationCount);
        }

        [Fact]
        public void Three_findings_over_one_element_are_three_places()
        {
            // Location ids are scoped to the finding that made them. Three specifications each
            // failing the same five hundred walls are fifteen hundred places to visit, and an id
            // that was only the element key would report five hundred and hand back whichever
            // finding sorted first for every one of them.
            var register = new FindingRegister();

            var specs = new[] { "fire-rating", "acoustic", "load-bearing" }
                .Select(Fails).ToList();

            foreach (var spec in specs) register.Put(spec);

            Assert.Equal(3, register.Count);
            Assert.Equal(6, register.LocationCount);

            foreach (var spec in specs)
                foreach (var location in spec.Locations)
                {
                    Assert.Same(spec, register.OwnerOfLocation(location.Id));
                    Assert.Same(location, register.FindLocation(location.Id));
                }
        }

        // One specification, failing the same two walls every other specification fails.
        private static Finding Fails(string specification)
        {
            var finding = Finding.CreateParent(FindingSource.Ids, "IDS",
                                               "Walls failed " + specification, specification);

            for (var n = 0; n < 2; n++)
                finding.AddLocation(new FindingLocation(finding.Id + "/" + Wall(n),
                                                        new[] { Wall(n) }, specification));

            return finding;
        }

        [Fact]
        public void A_location_is_not_a_topic_and_cannot_be_attached_to_an_issue()
        {
            // Two of the decisions in 2.2, pinned. A location is not a register entry: it is not
            // written as its own BCF topic, and an issue that names one names nothing.
            var register = new FindingRegister();
            var spec = Spec(3);
            register.Put(spec);

            var topics = FindingBcf.Together(new[] { spec }, null, null);

            Assert.Single(topics);
            Assert.Equal(FindingBcf.TopicGuidFor(spec), topics[0].Guid);

            // IssueFindings.Attach takes findings, so a location cannot be handed to it at all.
            // What can happen is an id typed or carried in by hand, and that resolves to nothing.
            var issue = new Issue("CW-1", "The fire-rated walls");
            issue.Findings.Add("wall-1");

            Assert.Null(register.Find("wall-1"));
            Assert.Empty(IssueFindings.Attached(issue, register));
        }
    }
}
