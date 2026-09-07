using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// IDS results as findings.
    ///
    /// The check could select the failures and write a workbook and remember nothing: run it
    /// Monday, agree the forty doors are being scheduled separately, run it Tuesday, and it said
    /// forty again with nowhere to have said so. What is tested is the identity that lets an
    /// agreement survive — the specification, not the count — and that a specification which
    /// checked nothing is still reported rather than buried in a green tick.
    /// </summary>
    public class IdsFindingsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("Arch.ifc");

        private static string Id(int n) =>
            ElementKey.FromInstanceGuid(Scope,
                Guid.Parse(n.ToString("x8") + "-0000-0000-0000-000000000000")).ToString();

        private static ElementKey KeyOf(string id) =>
            ElementKey.TryParse(id, out var key) ? key : default;

        /// <summary>Walls that must carry a fire rating, and some that do not.</summary>
        private static IdsReport Run(int failing, string? identifier = "S-01",
                                     string name = "Walls carry a fire rating")
        {
            var specification = IdsFixture.WallsCarryAFireRating();

            // The identity has to survive the specification being reworded, so the name is a
            // parameter and the identifier is what the file gives it.
            var renamed = new IdsSpecification(name) { Identifier = identifier };

            foreach (var facet in specification.Applicability) renamed.Applicability.Add(facet);
            foreach (var facet in specification.Requirements) renamed.Requirements.Add(facet);

            var walls = Enumerable.Range(0, failing)
                .Select(n => IdsFixture.Wall(Id(n + 1), fireRating: null))
                .Concat(new[] { IdsFixture.Wall(Id(9999), "60") })
                .ToList();

            return IdsChecker.Check(IdsFixture.Document(renamed), walls);
        }

        private static Finding Only(IdsReport report) =>
            IdsFindings.For(report, "riverside.ids", KeyOf).Single();

        [Fact]
        public void AFailedSpecificationCarriesWhatTheRowShows()
        {
            var finding = Only(Run(2));

            Assert.Equal(FindingSource.Ids, finding.Source);
            Assert.Equal(IdsFindings.Rule, finding.Rule);
            Assert.Contains("2 elements fail it", finding.Title);
            Assert.Equal("riverside.ids", finding.Facts[IdsFindings.Document]);
            Assert.Equal("3", finding.Facts[IdsFindings.Applicable]);
            Assert.Equal("1", finding.Facts[IdsFindings.Passed]);
            Assert.Equal("2", finding.Facts[IdsFindings.Failed]);
            Assert.Equal(2, finding.Locations.Count);
        }

        [Fact]
        public void HowManyFailIsThisRunsAnswerAndNotADifferentRequirement()
        {
            // The agreement about the doors has to survive the number changing.
            Assert.Equal(Only(Run(2)).Id, Only(Run(5)).Id);
        }

        [Fact]
        public void RewordingASpecificationDoesNotMakeItANewOne()
        {
            Assert.Equal(Only(Run(2)).Id,
                         Only(Run(2, name: "Every wall carries a fire rating")).Id);
        }

        [Fact]
        public void WithNoIdentifierTheNameIsAllThereIs()
        {
            var byName = Only(Run(2, identifier: null));
            var reworded = Only(Run(2, identifier: null, name: "Something else entirely"));

            Assert.NotEqual(byName.Id, reworded.Id);
        }

        [Fact]
        public void AnAgreementSurvivesTheNextRun()
        {
            var register = new FindingRegister();

            register.Observe(FindingSource.Ids, new[] { Only(Run(40)) }, 1);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;
            held.Comments.Add(new FindingComment("ada", 1, "Being scheduled separately."));

            register.Observe(FindingSource.Ids, new[] { Only(Run(41)) }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Single(after.Comments);
            Assert.Equal("41", after.Facts[IdsFindings.Failed]);
        }

        [Fact]
        public void ASpecificationTheModelMeetsMakesNoFinding()
        {
            Assert.Empty(IdsFindings.For(Run(0), "riverside.ids", KeyOf));
        }

        [Fact]
        public void ASpecificationThatCheckedNothingIsStillReported()
        {
            // A requirement that quietly stops being checked is worse than one that fails, so it
            // does not get to hide inside a green tick.
            var specification = new IdsSpecification("Ducts carry a system name")
            {
                Identifier = "S-09",
            };

            specification.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCDUCTSEGMENT")));

            specification.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_DuctSegmentCommon"), IdsValue.Simple("SystemName")));

            var report = IdsChecker.Check(IdsFixture.Document(specification),
                                          new[] { IdsFixture.Wall(Id(1)) });

            var finding = IdsFindings.For(report, "riverside.ids", KeyOf).Single();

            Assert.Contains("nothing in this model was checked by it", finding.Title);
            Assert.Contains("quietly stops being checked", finding.Facts[IdsFindings.Why]);
            Assert.False(finding.HasLocations);
        }

        [Fact]
        public void AnElementIdThatWillNotParseGetsNoChildRatherThanAWrongOne()
        {
            // The checker speaks in strings and the model speaks in keys. A child pointing at the
            // wrong element is worse than no child.
            var report = IdsChecker.Check(
                IdsFixture.Document(IdsFixture.WallsCarryAFireRating()),
                new[] { IdsFixture.Wall("not-a-key", fireRating: null) });

            var finding = IdsFindings.For(report, "riverside.ids", KeyOf).Single();

            Assert.False(finding.HasLocations);
            Assert.Equal("1", finding.Facts[IdsFindings.Failed]);
        }

        [Fact]
        public void WithNoWayToKeysThereAreNoChildrenAndStillAFinding()
        {
            var finding = IdsFindings.For(Run(2), "riverside.ids").Single();

            Assert.False(finding.HasLocations);
            Assert.Equal("2", finding.Facts[IdsFindings.Failed]);
        }

        [Fact]
        public void AScopedRuleKeepsItsAgreementLikeAnyOther()
        {
            // A rule that says who it is about in the model's own terms is a finding like any
            // other: the specification identifies it, so an agreement reached about it survives
            // the set growing, which is the whole point of scoping by a search set.
            var specification = new IdsSpecification("Ducts carry a system name")
            {
                Identifier = "S-01",
            };

            specification.Applicability.Add(new IdsScopeFacet(IdsScope.Of("Level 3 Ductwork")!));

            specification.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Element"), IdsValue.Simple("System Name")));

            IdsReport Run(int failing)
            {
                var ducts = Enumerable.Range(1, failing)
                    .Select(n => Scoped(Id(n)))
                    .ToList();

                return IdsChecker.Check(IdsFixture.Document(specification), ducts);
            }

            var register = new FindingRegister();

            var first = IdsFindings.For(Run(3), "this project's rules", KeyOf).Single();

            register.Observe(FindingSource.Ids, new[] { first }, 1);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;

            var later = IdsFindings.For(Run(9), "this project's rules", KeyOf).Single();

            register.Observe(FindingSource.Ids, new[] { later }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("9", after.Facts[IdsFindings.Failed]);
            Assert.Equal(9, after.Locations.Count);
        }

        /// <summary>An element the Level 3 Ductwork set claimed, carrying nothing else.</summary>
        private static IdsElement Scoped(string id)
        {
            var element = new IdsElement(id);

            element.Scopes.Add(IdsScope.Of("Level 3 Ductwork")!.Token);

            return element;
        }

        [Fact]
        public void NothingToReportIsNotAFailure()
        {
            Assert.Empty(IdsFindings.For(null));
        }
    }
}
