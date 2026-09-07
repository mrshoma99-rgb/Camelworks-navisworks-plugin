using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Drift as findings, and the baseline it is measured against.
    ///
    /// The finding has to say what the drift cost, not merely that it happened: a set going quiet
    /// is a curiosity until somebody knows three clash tests were pointed at it, at which point it
    /// is a morning's work before anybody trusts a green board again.
    /// </summary>
    public class SetDriftFindingsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.nwc");

        private static ElementKey Key(int n) =>
            ElementKey.FromInstanceGuid(Scope,
                Guid.Parse(n.ToString("x8") + "-0000-0000-0000-000000000000"));

        private static SetReading Read(string name, int count, long when = 1) =>
            new SetReading(name, Enumerable.Range(1, count).Select(Key), when);

        private static SetDriftResult Emptied(string name = "Level 3 Ductwork", int was = 40) =>
            SetDrift.Between(Read(name, was), new SetReading(name, null, 2))!;

        [Fact]
        public void AnEmptiedSetSaysWhatItDisarmed()
        {
            var uses = new[]
            {
                new SetUse("clash test", "MEP vs Structure"),
                new SetUse("clearance check", "Level 3 corridors"),
            };

            var finding = SetDriftFindings.From(Emptied(), uses)!;

            Assert.Equal(FindingSource.SetDrift, finding.Source);
            Assert.Equal(SetDriftFindings.Rule, finding.Rule);
            Assert.Contains("returns nothing", finding.Title);

            Assert.Contains("clash test \"MEP vs Structure\"", finding.Detail);
            Assert.Contains("now cover nothing", finding.Detail);
            Assert.Contains("does not fail", finding.Detail);

            Assert.Contains("clash test \"MEP vs Structure\"", finding.Facts[SetDriftFindings.Feeding]);
            Assert.Equal("40", finding.Facts[SetDriftFindings.Was]);
            Assert.Equal("0", finding.Facts[SetDriftFindings.Now]);
        }

        [Fact]
        public void WithNothingKnownToUseItTheCostIsStillSaid()
        {
            var finding = SetDriftFindings.From(Emptied())!;

            Assert.Contains("Anything pointed at this set now covers nothing", finding.Detail);
            Assert.False(finding.Facts.ContainsKey(SetDriftFindings.Feeding));
        }

        [Fact]
        public void TheElementsThatWentAreNamed()
        {
            // A set that lost its ductwork because the ductwork was deleted is a different
            // morning from one that lost it because a property was renamed, and only the elements
            // tell them apart.
            var finding = SetDriftFindings.From(Emptied())!;

            Assert.True(finding.HasLocations);
            Assert.Equal(40, finding.Locations.Count);
            Assert.Equal(40, finding.AllElements().Count);
        }

        [Fact]
        public void ASetThatLostNineThousandIsOneThingToDecideAbout()
        {
            var finding = SetDriftFindings.From(Emptied(was: 2000), null, cap: 500)!;

            Assert.Equal(500, finding.Locations.Count);
            Assert.Equal("2000", finding.Facts[SetDriftFindings.Was]);
        }

        [Fact]
        public void ASetHoldingSteadyMakesNoFinding()
        {
            Assert.Null(SetDriftFindings.From(SetDrift.Between(Read("Ducts", 40), Read("Ducts", 41))));
            Assert.Null(SetDriftFindings.From(null));
            Assert.Empty(SetDriftFindings.For(null));
        }

        [Fact]
        public void ADecisionAboutASetSurvivesItsMembershipMoving()
        {
            // The reason identity is the name. "Yes, that one emptied on purpose, we deleted the
            // mezzanine" has to survive the next run.
            var register = new FindingRegister();

            register.Observe(FindingSource.SetDrift,
                             new[] { SetDriftFindings.From(Emptied(was: 40))! }, 1);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;

            register.Observe(FindingSource.SetDrift,
                             new[] { SetDriftFindings.From(Emptied(was: 55))! }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("55", after.Facts[SetDriftFindings.Was]);
        }

        [Fact]
        public void TwoSetsAreTwoFindings()
        {
            var drifted = new[] { Emptied("Ducts"), Emptied("Pipes") };

            var found = SetDriftFindings.For(drifted,
                name => name == "Ducts"
                    ? new[] { new SetUse("clash test", "MEP vs Structure") }
                    : Array.Empty<SetUse>());

            Assert.Equal(2, found.Count);
            Assert.Equal(2, found.Select(f => f.Id).Distinct().Count());
            Assert.Contains(found, f => f.Facts.ContainsKey(SetDriftFindings.Feeding));
        }

        [Fact]
        public void ASampledDriftSaysItsListIsNotTheWhole()
        {
            var was = new SetReading("Ducts", new[] { Key(1) }, 1, whole: false, held: 900);
            var now = new SetReading("Ducts", null, 2);

            var finding = SetDriftFindings.From(SetDrift.Between(was, now))!;

            Assert.Contains("the counts are exact",
                            finding.Facts[SetDriftFindings.Sampled]);
        }

        // -------------------------------------------------------------------------------------
        // The baseline
        // -------------------------------------------------------------------------------------

        private static SetBaselineStore Store(out InMemoryFileSystem fs)
        {
            fs = new InMemoryFileSystem();

            var project = ProjectStore.Open(fs, @"C:\Jobs\Site\Site.nwf", null);

            return SetBaselineStore.Open(fs, project);
        }

        [Fact]
        public void WhatASetHeldSurvivesTheFile()
        {
            var store = Store(out var fs);

            store.Keep(new[] { Read("Ducts", 40, when: 123L) });

            Assert.True(store.Save());

            var again = SetBaselineStore.Open(fs,
                ProjectStore.Open(fs, @"C:\Jobs\Site\Site.nwf", null));

            var back = again.Find("Ducts");

            Assert.NotNull(back);
            Assert.Equal(40, back!.Held);
            Assert.Equal(40, back.Members.Count);
            Assert.Equal(123L, back.WhenTicks);
            Assert.True(back.IsWhole);
        }

        [Fact]
        public void ASampledBaselineComesBackSayingSo()
        {
            var store = Store(out var fs);

            store.Keep(new[]
            {
                new SetReading("Everything", new[] { Key(1), Key(2) }, 1, whole: false, held: 90000),
            });

            store.Save();

            var back = SetBaselineStore
                .Open(fs, ProjectStore.Open(fs, @"C:\Jobs\Site\Site.nwf", null))
                .Find("Everything")!;

            Assert.False(back.IsWhole);
            Assert.Equal(90000, back.Held);
            Assert.Equal(2, back.Members.Count);
        }

        [Fact]
        public void ASetTheDocumentLostIsDroppedRatherThanReportedForEver()
        {
            // Once is the honest number of times to say a thing.
            var store = Store(out _);

            store.Keep(new[] { Read("Ducts", 40) });
            Assert.Equal(1, store.Count);

            store.Keep(new[] { SetReading.Missing("Ducts", 2) });
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void ASetNobodyHasReadIsNotThere()
        {
            var store = Store(out _);

            Assert.Null(store.Find("Ducts"));
            Assert.Null(store.Find(null));
        }

        [Fact]
        public void NowhereToWriteIsAnsweredRatherThanThrown()
        {
            var store = SetBaselineStore.Open(new InMemoryFileSystem(), null);

            Assert.True(store.IsMemoryOnly);
            Assert.False(store.Save());
            Assert.Contains("nowhere to keep", store.LastSaveProblem!);
        }

        [Fact]
        public void AnUnreadableFileIsReportedAndLeftAlone()
        {
            var fs = new InMemoryFileSystem();
            var project = ProjectStore.Open(fs, @"C:\Jobs\Site\Site.nwf", null);
            var path = SetBaselineStore.PathFor(project.Path)!;

            fs.WriteAllText(path, "{ this is not json");

            var store = SetBaselineStore.Open(fs, project);

            Assert.Equal(0, store.Count);
            Assert.Contains("not readable JSON", string.Join(" ", store.Notes));
            Assert.Equal("{ this is not json", fs.ReadAllText(path));
        }

        [Fact]
        public void TheBaselineSitsBesideTheProjectAndNotInIt()
        {
            // Membership is element keys, and the project file is rewritten whenever any tool
            // remembers a textbox.
            Assert.EndsWith(SetBaselineStore.Extension,
                            SetBaselineStore.PathFor(@"C:\Jobs\Site\.camelworks\Site.cwproj")!);

            Assert.Null(SetBaselineStore.PathFor(null));
            Assert.Null(SetBaselineStore.PathFor("   "));
        }
    }
}
