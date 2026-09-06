using System;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Clear-height breaches as findings.
    ///
    /// The behaviour that matters is that a decision survives a re-run — the check used to carry
    /// its judgement in memory, so closing Navisworks reopened everything anybody had closed. The
    /// two identity choices that make that work are tested directly: the minimum is not part of
    /// what a finding is, and the position is quantised so a breach that drifts is still itself.
    /// </summary>
    public class ClearanceFindingsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("Arch.ifc");

        private static ElementKey Key(char name) =>
            ElementKey.FromInstanceGuid(Scope, Guid.Parse(name + "0000000-0000-0000-0000-000000000000"));

        private static HeadroomElement Box(string id, string name,
                                           double minX, double minY, double minZ,
                                           double maxX, double maxY, double maxZ) =>
            new HeadroomElement(id, name)
            {
                MinX = minX, MinY = minY, MinZ = minZ,
                MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
            };

        /// <summary>A slab with a duct over it, low enough to fail. Optionally shifted.</summary>
        private static HeadroomResult Run(double soffit = 1.85, double minimum = 2.1,
                                          double shift = 0)
        {
            var floor = Box("floor", "Slab L3", 0, 0, -0.2, 10, 10, 0);
            var duct = Box("duct", "Duct 400x250", 2 + shift, 2, soffit, 6 + shift, 4, soffit + 0.3);

            return Headroom.Check(new[] { floor }, new[] { duct }, minimum);
        }

        private static Finding One(HeadroomResult result, double minimum = 2.1) =>
            ClearanceFindings.From(result.Spans.Single(), Key('a'), Key('b'), minimum);

        [Fact]
        public void ALengthFactIsMetresAtMoreThanTheResolutionItIsPrintedAt()
        {
            // A soffit 0.4 mm below the one next to it. Both sentences read "2.099 m"; the facts
            // do not, because a column reading one back has to be able to tell them apart.
            var lower = One(Run(soffit: 2.0994), 2.1);
            var higher = One(Run(soffit: 2.0998), 2.1);

            Assert.NotEqual(lower.Facts[ClearanceFindings.Measured],
                            higher.Facts[ClearanceFindings.Measured]);

            var read = ClearanceFindings.MetresOf(lower, ClearanceFindings.Measured);

            Assert.NotNull(read);
            Assert.Equal(2.0994, read!.Value, 6);

            // Metres, which is what the check measured in and what the fact carries. Nothing
            // between the two converts.
            Assert.Equal(2.1, ClearanceFindings.MetresOf(lower, ClearanceFindings.Wanted)!.Value, 6);
        }

        [Fact]
        public void ABreachCarriesWhatTheRowShows()
        {
            var result = Run();
            var finding = One(result);

            Assert.Equal(FindingSource.Headroom, finding.Source);
            Assert.Equal(ClearanceFindings.Rule, finding.Rule);
            Assert.Equal(new[] { Key('a'), Key('b') }, finding.Elements);
            Assert.Equal("Duct 400x250", finding.Facts[ClearanceFindings.Obstruction]);
            Assert.Equal("1.85", finding.Facts[ClearanceFindings.Measured]);
            Assert.Equal("2.1", finding.Facts[ClearanceFindings.Wanted]);
            Assert.Equal("0.25", finding.Facts[ClearanceFindings.Shortfall]);
            Assert.Contains("1.85 m clear where 2.1 m was asked for", finding.Detail);
        }

        [Fact]
        public void ThePointIsInTheGapRatherThanInTheSlab()
        {
            // The camera has to end up where a person's head would not fit, not inside the floor.
            var finding = One(Run());

            Assert.Equal(0.925, finding.Z!.Value, 3);
        }

        [Fact]
        public void RaisingTheMinimumDoesNotMakeItANewFinding()
        {
            // Re-running at 2.4 after running at 2.1 finds the same low soffit. Presenting it as
            // new would lose last week's decision on the strength of a settings change.
            var at21 = ClearanceFindings.From(Run(minimum: 2.1).Spans.Single(),
                                              Key('a'), Key('b'), 2.1);

            var at24 = ClearanceFindings.From(Run(minimum: 2.4).Spans.Single(),
                                              Key('a'), Key('b'), 2.4);

            Assert.Equal(at21.Id, at24.Id);
            Assert.NotEqual(at21.Facts[ClearanceFindings.Wanted],
                            at24.Facts[ClearanceFindings.Wanted]);
        }

        [Fact]
        public void ABreachThatDriftsInsideItsCellIsStillItself()
        {
            var still = One(Run());
            var nudged = One(Run(shift: 0.4));

            Assert.Equal(still.Id, nudged.Id);
        }

        [Fact]
        public void ABreachSomewhereElseIsSomethingElse()
        {
            var here = One(Run());
            var over = One(Run(shift: 6));

            Assert.NotEqual(here.Id, over.Id);
        }

        [Fact]
        public void ADecisionSurvivesTheNextRun()
        {
            // The whole point: closing a breach on Tuesday and re-running on Wednesday used to
            // reopen it, because the judgement lived on the row.
            var register = new FindingRegister();

            register.Observe(FindingSource.Headroom, new[] { One(Run()) }, 1);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;
            held.AssignedTo = "Structures";

            register.Observe(FindingSource.Headroom, new[] { One(Run(shift: 0.3)) }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("Structures", after.AssignedTo);
        }

        [Fact]
        public void ABreachNobodyReportsAnyMoreIsMarkedGoneRatherThanDeleted()
        {
            // It is the record of the thing having been fixed, and an issue may point at it.
            var register = new FindingRegister();

            register.Observe(FindingSource.Headroom, new[] { One(Run()) }, 1);
            register.Observe(FindingSource.Headroom, Array.Empty<Finding>(), 2);

            Assert.Single(register.All);
            Assert.False(register.All.Single().IsPresent);
        }

        [Fact]
        public void AWholeRunBecomesFindings()
        {
            var result = Run();

            var found = ClearanceFindings.For(result,
                id => id == "floor" ? Key('a') : Key('b'));

            Assert.Single(found);
            Assert.Equal(new[] { Key('a'), Key('b') }, found[0].Elements);
        }

        [Fact]
        public void NothingToReportIsNotAFailure()
        {
            Assert.Empty(ClearanceFindings.For(null, _ => default));
            Assert.Empty(ClearanceFindings.For(Run(soffit: 3.0), _ => default));
        }
    }
}
