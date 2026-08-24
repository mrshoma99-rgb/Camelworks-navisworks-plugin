using System;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// A finding has to keep the elements it is about, or the reader's next move is impossible.
    ///
    /// The health checks used to count the affected elements, format five of them into strings for
    /// display, and drop the rest on the floor. That is why the tab could say "37 elements sit on
    /// top of each other at the origin" and offer no way to look at one: by the time the sentence
    /// existed, the elements did not.
    /// </summary>
    public class FindingReachTests
    {
        private static HealthElement At(string id, double x, double y, double z)
        {
            return new HealthElement(id, "S.nwc")
            {
                Key = ElementKey.FromTreePath("structure", "S.nwc", id),
                Name = "Element " + id,
                X = x,
                Y = y,
                Z = z,
                SizeX = 1,
                SizeY = 1,
                SizeZ = 1,
            };
        }

        /// <summary>
        /// A model whose real geometry sits well away from zero, plus <paramref name="stranded"/>
        /// elements left on the origin.
        ///
        /// The rule only fires when the origin is an outlier — a plant room authored around zero is
        /// entirely normal — so a test model needs a body of geometry somewhere else for the
        /// elements at zero to be strange compared with.
        /// </summary>
        private static System.Collections.Generic.List<HealthElement> Stranded(int stranded)
        {
            var all = Enumerable.Range(0, stranded).Select(i => At("origin-" + i, 0, 0, 0)).ToList();

            // Tightly clustered 140m out: a large centre, a small spread, so the origin is an
            // outlier by the factor of ten the rule asks for.
            for (var i = 0; i < 10; i++)
                all.Add(At("real-" + i, 100 + i * 0.5, 100, 0));

            return all;
        }

        [Fact]
        public void A_finding_keeps_every_element_not_just_the_examples()
        {
            // Twelve at the origin — more than the five the examples show, which is the whole point.
            var report = ModelHealth.Check(Stranded(12));
            var finding = report.Findings.Single(f => f.Rule == "origin-stack");

            Assert.Equal(12, finding.Count);
            Assert.Equal(ModelHealth.ExampleCount, finding.Examples.Count);
            Assert.Equal(12, finding.Affected.Count);
        }

        [Fact]
        public void The_elements_it_keeps_are_the_ones_it_found()
        {
            var all = Stranded(8);
            var report = ModelHealth.Check(all);
            var finding = report.Findings.Single(f => f.Rule == "origin-stack");

            // The keys of exactly the elements left on the origin — a hashed key does not carry
            // its id, so the set is compared rather than the strings inspected.
            var stranded = all.Where(e => e.Id.StartsWith("origin-", StringComparison.Ordinal)).Select(e => e.Key).ToHashSet();

            Assert.Equal(8, stranded.Count);
            Assert.Equal(stranded, finding.Affected.ToHashSet());
        }

        [Fact]
        public void An_element_with_no_key_is_not_offered_as_somewhere_to_go()
        {
            // A caller that never set Key — the shape before this change. The finding still counts
            // and still reads; it just does not claim to be a place.
            var all = Stranded(6);

            // Strip the keys the walk would have set, leaving the shape this change replaced.
            foreach (var element in all) element.Key = default;

            var report = ModelHealth.Check(all);
            var finding = report.Findings.Single(f => f.Rule == "origin-stack");

            Assert.Equal(6, finding.Count);
            Assert.Empty(finding.Affected);
        }

        [Fact]
        public void A_review_finding_about_models_has_nothing_to_go_to()
        {
            var review = new FederationReview();
            review.Fault("Federation", "2 models have no file on disk");

            var finding = review.Findings.Single();

            Assert.Empty(finding.Affected);
        }

        [Fact]
        public void A_review_finding_about_elements_carries_them_through()
        {
            var keys = new[]
            {
                ElementKey.FromTreePath("structure", "S.nwc", "a"),
                ElementKey.FromTreePath("structure", "S.nwc", "b"),
            };

            var review = new FederationReview();
            review.Add(new ReviewFinding(ReviewGrade.Fault, "Geometry", "2 elements sit at the origin",
                                         null, "Set a shared origin.", new[] { "a", "b" }, keys));

            Assert.Equal(2, review.Findings.Single().Affected.Count);
        }
    }
}
