using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class FindingStatusTests
    {
        [Theory]
        [InlineData(FindingStatus.New, FindingStatus.Resolved, FindingStatus.Resolved)]
        [InlineData(FindingStatus.Resolved, FindingStatus.New, FindingStatus.Resolved)]
        [InlineData(FindingStatus.Active, FindingStatus.Approved, FindingStatus.Approved)]
        [InlineData(FindingStatus.Reviewed, FindingStatus.Reviewed, FindingStatus.Reviewed)]
        public void A_merge_never_loses_progress(FindingStatus a, FindingStatus b, FindingStatus expected)
        {
            Assert.Equal(expected, FindingStatusLattice.Merge(a, b));
            Assert.Equal(expected, FindingStatusLattice.Merge(b, a));   // and is order-independent
        }

        [Fact]
        public void A_stale_Resolved_cannot_bury_a_live_Active()
        {
            // The asymmetry that matters. Losing a Resolved costs somebody a second look at
            // something already fixed. Burying an Active behind a green row means nobody looks
            // again — and only one of those two errors ends up in the building.
            Assert.Equal(FindingStatus.Resolved,
                FindingStatusLattice.Merge(FindingStatus.Active, FindingStatus.Resolved));

            Assert.True(FindingStatusLattice.IsDemotion(FindingStatus.Resolved, FindingStatus.Active));
        }

        [Fact]
        public void Only_the_two_statuses_the_host_does_not_recompute_count_as_human_judgement()
        {
            // The clash engine recomputes New, Active and Resolved on every run, so reading those
            // back from the host as truth would overwrite a person's decision with a machine's.
            Assert.True(FindingStatusLattice.IsHumanJudgement(FindingStatus.Reviewed));
            Assert.True(FindingStatusLattice.IsHumanJudgement(FindingStatus.Approved));

            Assert.False(FindingStatusLattice.IsHumanJudgement(FindingStatus.New));
            Assert.False(FindingStatusLattice.IsHumanJudgement(FindingStatus.Active));
            Assert.False(FindingStatusLattice.IsHumanJudgement(FindingStatus.Resolved));
        }

        [Theory]
        [InlineData("Resolved", FindingStatus.Resolved)]
        [InlineData("  approved  ", FindingStatus.Approved)]
        [InlineData("NEW", FindingStatus.New)]
        public void Status_names_parse_case_insensitively(string text, FindingStatus expected)
        {
            Assert.True(FindingStatusLattice.TryParse(text, out var s));
            Assert.Equal(expected, s);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Closed")]
        [InlineData("Done")]
        public void An_unknown_status_name_is_refused_rather_than_guessed(string? text)
        {
            // A BCF file from another tool may carry statuses we do not have. Mapping "Closed" to
            // a guess is how a status silently changes meaning crossing a tool boundary.
            Assert.False(FindingStatusLattice.TryParse(text, out _));
        }

        [Fact]
        public void Every_status_round_trips_through_its_name()
        {
            foreach (FindingStatus s in Enum.GetValues(typeof(FindingStatus)))
            {
                Assert.True(FindingStatusLattice.TryParse(FindingStatusLattice.ToName(s), out var back));
                Assert.Equal(s, back);
            }
        }
    }

    public class FindingTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");

        private static ElementKey E(string name) =>
            ElementKey.FromTreePath(Scope, @"C:\p\ARCH.nwc", "L03", name);

        // ---------------------------------------------------------------------------------
        // Derived identity — what makes status survive a re-run
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_same_problem_next_week_has_the_same_id()
        {
            var a = Finding.Create(FindingSource.Health, "Missing property", "Fire rating missing", new[] { E("Wall 1") });
            var b = Finding.Create(FindingSource.Health, "Missing property", "Fire rating missing", new[] { E("Wall 1") });

            Assert.Equal(a.Id, b.Id);
        }

        [Fact]
        public void Element_order_does_not_change_identity()
        {
            // Two producers listing the same pair the other way round are reporting the same thing.
            var a = Finding.Create(FindingSource.Clash, "L03 MEP v STR", "Duct through beam", new[] { E("Duct"), E("Beam") });
            var b = Finding.Create(FindingSource.Clash, "L03 MEP v STR", "Duct through beam", new[] { E("Beam"), E("Duct") });

            Assert.Equal(a.Id, b.Id);
        }

        [Fact]
        public void Different_rules_on_the_same_element_are_different_findings()
        {
            var missing = Finding.Create(FindingSource.Health, "Missing property", "x", new[] { E("Wall 1") });
            var naming = Finding.Create(FindingSource.Health, "Naming convention", "x", new[] { E("Wall 1") });

            Assert.NotEqual(missing.Id, naming.Id);
        }

        [Fact]
        public void The_discriminator_separates_two_findings_the_rule_and_elements_cannot()
        {
            var a = Finding.Create(FindingSource.Clash, "T1", "x", new[] { E("Duct"), E("Slab") }, "cell:40,16,36");
            var b = Finding.Create(FindingSource.Clash, "T1", "x", new[] { E("Duct"), E("Slab") }, "cell:56,16,36");

            Assert.NotEqual(a.Id, b.Id);
        }

        [Fact]
        public void The_title_is_display_text_and_does_not_affect_identity()
        {
            // Rewording a rule's message in a later build must not orphan every finding it made.
            var a = Finding.Create(FindingSource.Health, "R", "Fire rating missing", new[] { E("Wall 1") });
            var b = Finding.Create(FindingSource.Health, "R", "Fire rating is missing", new[] { E("Wall 1") });

            Assert.Equal(a.Id, b.Id);
        }

        [Fact]
        public void Confidence_is_carried_as_the_weakest_rung_of_its_elements()
        {
            var strong = ElementKey.FromInstanceGuid(Scope, Guid.NewGuid());
            var weak = ElementKey.FromGeometry(Scope, "Ducts", "Rect", 0.4, 0.25, 3);

            Assert.Equal(KeyRung.InstanceGuid,
                Finding.Create(FindingSource.Clash, "T", "x", new[] { strong }).WeakestRung);
            Assert.Equal(KeyRung.Geometry,
                Finding.Create(FindingSource.Clash, "T", "x", new[] { strong, weak }).WeakestRung);
        }

        // ---------------------------------------------------------------------------------
        // One finding, many locations
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_rule_failing_many_elements_is_one_decision_and_many_locations()
        {
            var finding = Finding.CreateParent(FindingSource.Ids, "IDS: fire rating required", "400 walls missing FireRating");
            foreach (var n in new[] { "Wall 1", "Wall 2", "Wall 3" })
                finding.AddLocation(new FindingLocation(n, new[] { E(n) }));

            Assert.True(finding.HasLocations);
            Assert.Equal(3, finding.Locations.Count);
            Assert.Equal(3, finding.AllElements().Count);
        }

        [Fact]
        public void A_findings_identity_does_not_depend_on_how_many_locations_it_has()
        {
            // 400 failures this week and 401 next week is the same rule still failing — it keeps
            // its assignee and its sign-off.
            var week1 = Finding.CreateParent(FindingSource.Ids, "IDS: fire rating", "t");
            week1.AddLocation(new FindingLocation("a", new[] { E("Wall 1") }));

            var week2 = Finding.CreateParent(FindingSource.Ids, "IDS: fire rating", "t");
            week2.AddLocation(new FindingLocation("a", new[] { E("Wall 1") }));
            week2.AddLocation(new FindingLocation("b", new[] { E("Wall 2") }));

            Assert.Equal(week1.Id, week2.Id);
        }

        [Fact]
        public void Four_hundred_locations_and_a_thousand_are_the_same_finding()
        {
            // The number the plan reasons about. A specification failing 400 elements and the same
            // specification failing 1,000 is one thing to decide about, and re-deriving the id from
            // the locations would throw the decision away every run.
            Finding Made(int howMany)
            {
                var finding = Finding.CreateParent(FindingSource.Ids, "IDS: fire rating", "t", "spec-1");

                for (var n = 0; n < howMany; n++)
                    finding.AddLocation(new FindingLocation("wall-" + n, new[] { E("Wall " + n) }));

                return finding;
            }

            var small = Made(400);
            var large = Made(1000);

            Assert.Equal(small.Id, large.Id);
            Assert.Equal(1000, large.Locations.Count);
        }

        [Fact]
        public void A_location_replaces_rather_than_accumulates()
        {
            var finding = Finding.CreateParent(FindingSource.Health, "R", "t");
            finding.AddLocation(new FindingLocation("a", new[] { E("Wall 1") }));

            finding.ReplaceLocations(new[] { new FindingLocation("b", new[] { E("Wall 2") }) });

            Assert.Equal("b", Assert.Single(finding.Locations).Id);

            finding.ReplaceLocations(null);

            Assert.False(finding.HasLocations);
        }

        [Fact]
        public void A_location_with_no_id_cannot_be_made()
        {
            // A location nothing can name is a location nothing can send anybody to, and an id
            // invented here would collide with every other one that lost its own.
            Assert.Throws<ArgumentException>(() => new FindingLocation(" ", new[] { E("Wall 1") }));
        }

        [Fact]
        public void A_location_carries_a_note_but_no_decision()
        {
            // A note records "this one is a false positive" without claiming the location was
            // decided. There is no status, assignee or due date on it to set.
            var location = new FindingLocation("a", new[] { E("Wall 1") });

            location.Notes.Add(new FindingComment("A Coordinator", 1, "void former, not a wall"));

            Assert.Equal("void former, not a wall", Assert.Single(location.Notes).Body);
        }

        [Fact]
        public void All_elements_deduplicates_across_locations()
        {
            var finding = Finding.CreateParent(FindingSource.Headroom, "Headroom 2.1m", "t");
            finding.AddLocation(new FindingLocation("a", new[] { E("Duct"), E("Slab") }));
            finding.AddLocation(new FindingLocation("b", new[] { E("Duct"), E("Beam") }));

            Assert.Equal(3, finding.AllElements().Count);   // Duct once
        }

        // ---------------------------------------------------------------------------------
        // Merging concurrent edits
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Merging_promotes_status_and_takes_the_newer_assignment()
        {
            var mine = Finding.Create(FindingSource.Clash, "T", "x", new[] { E("Duct") });
            mine.Status = FindingStatus.Active;
            mine.AssignedTo = "MEP";

            var theirs = Finding.Create(FindingSource.Clash, "T", "x", new[] { E("Duct") });
            theirs.Status = FindingStatus.Resolved;
            theirs.AssignedTo = "STR";
            theirs.Priority = "High";

            mine.MergeFrom(theirs, otherIsNewer: true);

            Assert.Equal(FindingStatus.Resolved, mine.Status);
            Assert.Equal("STR", mine.AssignedTo);
            Assert.Equal("High", mine.Priority);
        }

        [Fact]
        public void An_older_edit_promotes_status_but_does_not_overwrite_a_newer_assignment()
        {
            var mine = Finding.Create(FindingSource.Clash, "T", "x", new[] { E("Duct") });
            mine.Status = FindingStatus.Active;
            mine.AssignedTo = "MEP";

            var stale = Finding.Create(FindingSource.Clash, "T", "x", new[] { E("Duct") });
            stale.Status = FindingStatus.Approved;
            stale.AssignedTo = "Nobody";

            mine.MergeFrom(stale, otherIsNewer: false);

            Assert.Equal(FindingStatus.Approved, mine.Status);   // status still promotes
            Assert.Equal("MEP", mine.AssignedTo);                // assignment does not
        }

        [Fact]
        public void Merging_two_different_findings_is_refused()
        {
            var a = Finding.Create(FindingSource.Clash, "T1", "x", new[] { E("Duct") });
            var b = Finding.Create(FindingSource.Clash, "T2", "x", new[] { E("Duct") });

            Assert.Throws<ArgumentException>(() => a.MergeFrom(b, true));
        }

        // ---------------------------------------------------------------------------------
        // Every producer uses the same record
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Every_producer_emits_the_same_shape()
        {
            // Counted rather than listed, so adding a producer is a decision somebody makes here
            // rather than something that slips in. Eight: clash, manual, health, headroom, IDS,
            // set drift, compare and fall hazard.
            var sources = FindingSource.Known;
            Assert.Equal(8, sources.Count);

            var findings = sources
                .Select(s => Finding.Create(s, "rule", "title", new[] { E("Wall 1") }))
                .ToList();

            // Same record, same triage vocabulary — one board, one report, one exporter.
            Assert.All(findings, f => Assert.Equal(FindingStatus.New, f.Status));
            Assert.Equal(8, findings.Select(f => f.Id).Distinct().Count());   // provenance separates them
        }

        [Fact]
        public void A_finding_with_no_elements_is_allowed_because_some_rules_are_about_the_model()
        {
            // "no grid system found in this document" points at no element at all.
            var f = Finding.Create(FindingSource.Health, "No grid system", "This model has no grids",
                Array.Empty<ElementKey>());

            Assert.Empty(f.Elements);
            Assert.NotEqual(string.Empty, f.Id);
        }

        [Fact]
        public void A_rule_name_is_required()
        {
            Assert.Throws<ArgumentException>(() =>
                Finding.Create(FindingSource.Health, "  ", "t", Array.Empty<ElementKey>()));
        }
    }
}
