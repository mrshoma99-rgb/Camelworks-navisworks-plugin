using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What a model review found, as findings.
    ///
    /// The review had no persistence at all — it said what it found and forgot it. What is tested
    /// here is the one thing that had to be got right for it to gain any: identity off the rule
    /// rather than off a headline with a count in it, and a parent that keeps its decision when
    /// the number of elements it is about changes.
    /// </summary>
    public class HealthFindingsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("Arch.ifc");

        private static ElementKey Key(int n) =>
            ElementKey.FromInstanceGuid(Scope,
                Guid.Parse(n.ToString("x8") + "-0000-0000-0000-000000000000"));

        private static ReviewFinding Said(int count, string rule = "origin-stack",
                                          string check = "Geometry") =>
            new ReviewFinding(ReviewGrade.Fault, check,
                              count + " elements sit on top of each other at the origin",
                              "That is where an export lands when its coordinates were lost.",
                              "Put them back where they belong, or take them out.",
                              new[] { "a (Arch)", "b (Arch)" },
                              Enumerable.Range(1, count).Select(Key).ToList(),
                              rule);

        [Fact]
        public void ARuleBecomesOneParentAndAChildPerElement()
        {
            var parent = HealthFindings.From(Said(3));

            Assert.Equal(FindingSource.Health, parent.Source);
            Assert.Equal("Geometry", parent.Rule);
            Assert.True(parent.IsParent);
            Assert.Equal(3, parent.Children.Count);
            Assert.Empty(parent.Elements);
            Assert.Equal(3, parent.AllElements().Count);
            Assert.Equal("Fault", parent.Facts[HealthFindings.Grade]);
            Assert.Equal("3", parent.Facts[HealthFindings.Counted]);
            Assert.Equal("Put them back where they belong, or take them out.",
                         parent.Facts[HealthFindings.Fix]);
        }

        [Fact]
        public void TheCountInTheHeadlineDoesNotChangeWhatTheFindingIs()
        {
            // The whole reason the rule id exists. Identifying off a headline would hand every
            // re-run a fresh finding and throw away every decision anybody had taken.
            Assert.Equal(HealthFindings.From(Said(37)).Id, HealthFindings.From(Said(41)).Id);
        }

        [Fact]
        public void TwoRulesOfOneCheckAreTwoFindings()
        {
            Assert.NotEqual(HealthFindings.From(Said(3, "origin-stack")).Id,
                            HealthFindings.From(Said(3, "stray-geometry")).Id);
        }

        [Fact]
        public void ADecisionSurvivesTheElementCountChanging()
        {
            var register = new FindingRegister();

            register.Observe(FindingSource.Health, new[] { HealthFindings.From(Said(400)) }, 1);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;
            held.AssignedTo = "Architecture";

            register.Observe(FindingSource.Health, new[] { HealthFindings.From(Said(401)) }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("Architecture", after.AssignedTo);
            Assert.Equal("401", after.Facts[HealthFindings.Counted]);
        }

        [Fact]
        public void APathologicalRuleCannotWriteAFileNobodyAskedFor()
        {
            // Ninety thousand rows is one decision and a hundred megabytes. The parent still says
            // how many there were, which is the part anybody reads.
            var parent = HealthFindings.From(Said(2000), cap: 500);

            Assert.Equal(500, parent.Children.Count);
            Assert.Equal("2000", parent.Facts[HealthFindings.Counted]);
        }

        [Fact]
        public void AFindingAboutModelsRatherThanElementsIsStillAFinding()
        {
            // "This model was moved" has nothing to select and is still something somebody
            // accepts once and does not want asked again.
            var review = new FederationReview();

            review.Fault("Placement", "1 model has been moved by the federation",
                         null, "Run the clash tests again once it is back.",
                         new[] { "Structure.nwc" }, "model-moved");

            var parent = HealthFindings.For(review).Single();

            Assert.False(parent.IsParent);
            Assert.Empty(parent.AllElements());
            Assert.Equal("Placement", parent.Rule);
        }

        [Fact]
        public void ACleanReviewMakesNoFindings()
        {
            Assert.Empty(HealthFindings.For(new FederationReview()));
            Assert.Empty(HealthFindings.For(null));
        }

        [Fact]
        public void AChildCarriesNoDecisionOfItsOwn()
        {
            // Status, assignee and due date live on the parent, which is the unit of decision.
            var parent = HealthFindings.From(Said(2));

            Assert.All(parent.Children, child =>
            {
                Assert.Equal(FindingStatus.New, child.Status);
                Assert.Null(child.AssignedTo);
                Assert.Single(child.Elements);
            });
        }
    }
}
