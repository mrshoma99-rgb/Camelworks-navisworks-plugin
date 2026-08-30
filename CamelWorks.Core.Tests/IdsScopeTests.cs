using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Saying who a rule is about without IFC.
    ///
    /// A Navisworks federation is a pile of NWCs from Revit, Tekla and Civil 3D, and most of it
    /// has no IFC class at all. On that model an entity-scoped IDS matches nothing and reports
    /// "not applicable" for everything — useless, and it reads as green. These tests are about the
    /// other way of saying who a rule is about, and about the one thing that had to be fixed for
    /// it to tell the truth.
    /// </summary>
    public class IdsScopeTests
    {
        private static IdsSpecification DuctsCarryASystemName(IdsScope scope)
        {
            var specification = new IdsSpecification("Ducts carry a system name")
            {
                Identifier = "S-01",
            };

            specification.Applicability.Add(new IdsScopeFacet(scope));

            specification.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Element"), IdsValue.Simple("System Name")));

            return specification;
        }

        [Fact]
        public void AScopeReadsAsTheModelsOwnWords()
        {
            Assert.Equal("the \"Level 3 Ductwork\" set", IdsScope.Of("Level 3 Ductwork")!.Describe());
            Assert.Equal("what is selected", IdsScope.Selected.Describe());
            Assert.Equal("in the \"Ducts\" set",
                         new IdsScopeFacet(IdsScope.Of("Ducts")!).Describe());
        }

        [Fact]
        public void ASetNamedSelectionIsNotTheCurrentSelection()
        {
            // The kind is inside the token, so somebody's unfortunately named set cannot silently
            // become "whatever happens to be picked".
            var named = IdsScope.Of("selection")!;

            Assert.NotEqual(IdsScope.Selected.Token, named.Token);
            Assert.NotEqual(IdsScope.Selected, named);
        }

        [Fact]
        public void ATokenReadsBackAsTheScopeItNamed()
        {
            foreach (var scope in new[] { IdsScope.Of("Level 3 Ductwork")!, IdsScope.Selected })
                Assert.Equal(scope, IdsScope.FromToken(scope.Token));

            Assert.Null(IdsScope.FromToken(null));
            Assert.Null(IdsScope.FromToken("   "));
        }

        [Fact]
        public void AnElementInTheSetIsWhatTheRuleIsAbout()
        {
            var facet = new IdsScopeFacet(IdsScope.Of("Ducts")!);

            var inside = new IdsElement("a");
            inside.Scopes.Add(IdsScope.Of("Ducts")!.Token);

            Assert.True(facet.Matches(inside));
            Assert.False(facet.Matches(new IdsElement("b")));
        }

        [Fact]
        public void BeingSomewhereElseIsNotMissingData()
        {
            // Otherwise an element that is simply in another part of the model would be reported
            // as having a wrong value.
            Assert.True(new IdsScopeFacet(IdsScope.Of("Ducts")!).DataIsPresent(new IdsElement("a")));
        }

        // -------------------------------------------------------------------------------------
        // The trap
        // -------------------------------------------------------------------------------------

        [Fact]
        public void AnElementWithNoneOfTheDataIsStillCheckedWhenAScopeClaimedIt()
        {
            // THE ONE THAT MATTERS. The projection drops an element carrying nothing the IDS
            // mentions, which is right for an entity-scoped check on IFC data — a wall with no
            // fire rating still has its class, so it survives and fails properly.
            //
            // On a set-scoped rule over a Revit federation that same drop discards the exact case
            // the check exists for: forty ducts with no system name at all have no IFC class and
            // no matching property, so they would vanish before the checker saw them and the run
            // would report "nothing applied" over a model that is forty failures deep.
            var document = new FakeDocument();
            var model = document.AddModel("Mechanical", @"C:\Jobs\Site\MEP.nwc");

            var duct = document.AddItem(model, "Duct-01").WithProperty("Item", "Type", "Duct");

            var ids = IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!));

            var scopes = Claimed(IdsScope.Of("Ducts")!, duct.Key);
            var projection = IdsProjection.For(ids, null, scopes);

            var element = projection.From(duct);

            Assert.NotNull(element);
            Assert.Null(element!.IfcType);
            Assert.Empty(element.Properties);

            var report = IdsChecker.Check(ids, new[] { element });
            var result = report.Results.Single();

            Assert.Equal(1, result.Applicable);
            Assert.Equal(0, result.Passed);
            Assert.Equal(1, result.Failed);
            Assert.False(result.NothingApplied);
            Assert.Equal(IdsFailure.Missing, result.Violations.Single().Failure);
        }

        [Fact]
        public void AnElementNoScopeClaimedIsStillDropped()
        {
            // The drop is right everywhere else: a federation has tens of millions of property
            // readings and an IDS names a handful, and keeping the rest would only inflate the
            // total the report divides by.
            var document = new FakeDocument();
            var model = document.AddModel("Mechanical", @"C:\Jobs\Site\MEP.nwc");

            var pipe = document.AddItem(model, "Pipe-01").WithProperty("Item", "Type", "Pipe");
            var duct = document.AddItem(model, "Duct-01").WithProperty("Item", "Type", "Duct");

            var ids = IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!));

            var projection = IdsProjection.For(ids, null, Claimed(IdsScope.Of("Ducts")!, duct.Key));

            Assert.Null(projection.From(pipe));
            Assert.NotNull(projection.From(duct));
            Assert.Equal(1, projection.Kept);
        }

        [Fact]
        public void AnElementInTheSetThatHasTheDataPasses()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Mechanical", @"C:\Jobs\Site\MEP.nwc");

            var duct = document.AddItem(model, "Duct-01")
                .WithProperty("Element", "System Name", "SA-01");

            var ids = IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!));

            var element = IdsProjection.For(ids, null, Claimed(IdsScope.Of("Ducts")!, duct.Key))
                                      .From(duct);

            var result = IdsChecker.Check(ids, new[] { element! }).Results.Single();

            Assert.Equal(1, result.Passed);
            Assert.True(result.IsSatisfied);
        }

        [Fact]
        public void AScopedRuleNeedsNoIfcClassAnywhereInTheModel()
        {
            // The whole point, end to end: a rule that works on a federation with no IFC in it.
            var document = new FakeDocument();
            var model = document.AddModel("Mechanical", @"C:\Jobs\Site\MEP.nwc");

            var withName = document.AddItem(model, "Duct-01")
                .WithProperty("Element", "System Name", "SA-01");

            var without = document.AddItem(model, "Duct-02");

            var ids = IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!));

            var scopes = Claimed(IdsScope.Of("Ducts")!, withName.Key, without.Key);
            var projection = IdsProjection.For(ids, null, scopes);

            var elements = projection.FromAll(new IModelItem[] { withName, without }).ToList();
            var result = IdsChecker.Check(ids, elements).Results.Single();

            Assert.Equal(0, projection.Classed);
            Assert.Equal(2, result.Applicable);
            Assert.Equal(1, result.Passed);
            Assert.Equal(1, result.Failed);
        }

        [Fact]
        public void TheScopesARuleSetNamesAreTheOnesWorthResolving()
        {
            // Resolving a set costs a traversal each, so the caller is told exactly which it
            // needs rather than walking every set the document holds.
            var ids = IdsFixture.Document(
                DuctsCarryASystemName(IdsScope.Of("Ducts")!),
                DuctsCarryASystemName(IdsScope.Selected),
                IdsFixture.WallsCarryAFireRating());

            var wanted = IdsProjection.ScopesWanted(ids);

            Assert.Equal(2, wanted.Count);
            Assert.Contains(IdsScope.Of("Ducts")!, wanted);
            Assert.Contains(IdsScope.Selected, wanted);
        }

        [Fact]
        public void NoScopesWantedIsTheOrdinaryAnswer()
        {
            Assert.Empty(IdsProjection.ScopesWanted(IdsFixture.Document(IdsFixture.WallsCarryAFireRating())));
            Assert.Empty(IdsProjection.ScopesWanted(null));
        }

        // -------------------------------------------------------------------------------------
        // The file
        // -------------------------------------------------------------------------------------

        [Fact]
        public void AScopedRuleCannotBeWrittenIntoAnIdsFile()
        {
            // The published schema's applicability is a closed sequence of six facet elements with
            // no extension point. A scope written into one would make the file invalid for every
            // other tool, so the writer leaves it out - and says so, because a requirement that
            // quietly never reaches the person meant to meet it is worse than one they never got.
            var ids = IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!),
                                          IdsFixture.WallsCarryAFireRating());

            var written = IdsWriter.Write(ids);

            Assert.True(written.IsUsable);
            Assert.Equal(1, written.Specifications);
            Assert.DoesNotContain("Ducts carry a system name", written.Text, StringComparison.Ordinal);
            Assert.Contains("Walls carry a fire rating", written.Text, StringComparison.Ordinal);

            var stayed = Assert.Single(written.Stayed);

            Assert.Equal("Ducts carry a system name", stayed.Specification.Name);
            Assert.Contains("the \"Ducts\" set", stayed.Why, StringComparison.Ordinal);
            Assert.Contains("no place in an IDS file", stayed.Why, StringComparison.Ordinal);
            Assert.Contains("is not in the file", string.Join(" ", written.Warnings),
                            StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleSetThatIsAllScopedWillNotPretendToBeAnIdsFile()
        {
            // Writing an empty specifications list would be a file that opens and checks nothing,
            // which is the worst of the three outcomes: it reads as a delivered requirement.
            var written = IdsWriter.Write(
                IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!)));

            Assert.False(written.IsUsable);
            Assert.Contains("has no way to say that", written.Problem!, StringComparison.Ordinal);
            Assert.Contains("run it against this model", written.Problem!, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnscopedRuleSetIsWrittenWhole()
        {
            var written = IdsWriter.Write(IdsFixture.Document(IdsFixture.WallsCarryAFireRating()));

            Assert.True(written.IsUsable);
            Assert.Empty(written.Stayed);
            Assert.True(IdsScopeSpecification.Split(
                IdsFixture.Document(IdsFixture.WallsCarryAFireRating())).IsWhole);
        }

        [Fact]
        public void TheSplitNamesWhatStayedAndWhy()
        {
            var split = IdsScopeSpecification.Split(
                IdsFixture.Document(DuctsCarryASystemName(IdsScope.Of("Ducts")!),
                                    IdsFixture.WallsCarryAFireRating()));

            Assert.False(split.IsWhole);
            Assert.Single(split.Portable);
            Assert.Contains("1 rule stayed here", split.Explain()!, StringComparison.Ordinal);
            Assert.Contains("still run here", split.Explain()!, StringComparison.Ordinal);
        }

        [Fact]
        public void ScopeIsReadOffApplicabilityAndNotOffRequirements()
        {
            // A scope as a requirement would assert "this element must be in that set", which
            // grades a model on how somebody filed it rather than on the building - and the sets
            // are usually made by the person being checked.
            var specification = new IdsSpecification("Odd one");

            specification.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            specification.Requirements.Add(new IdsScopeFacet(IdsScope.Of("Ducts")!));

            Assert.False(IdsScopeSpecification.IsScoped(specification));
            Assert.Null(IdsScopeSpecification.WhyNotPortable(specification));
        }

        [Fact]
        public void NothingToSplitIsNotAFailure()
        {
            Assert.True(IdsScopeSpecification.Split(null).IsWhole);
            Assert.Empty(IdsScopeSpecification.ScopesOf(null));
            Assert.False(IdsScopeSpecification.IsScoped(null));
        }

        /// <summary>The scope map a run builds once, here for one set.</summary>
        private static Dictionary<string, IReadOnlyList<string>> Claimed(IdsScope scope,
                                                                        params ElementKey[] keys)
        {
            var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            foreach (var key in keys) map[key.ToString()] = new[] { scope.Token };

            return map;
        }
    }
}
