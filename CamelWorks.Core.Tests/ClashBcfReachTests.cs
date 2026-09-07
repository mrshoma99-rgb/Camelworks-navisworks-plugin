using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What a written clash BCF says about whether the tool receiving it can select anything.
    ///
    /// The one thing a coordinator cannot see by opening the file they just wrote, and the thing
    /// the person they sent it to finds out first: a topic whose components carry no IFC GlobalId
    /// opens without complaint and selects nothing.
    /// </summary>
    public class ClashBcfReachTests
    {
        private static Finding Found(string a, string b)
        {
            var scope = ElementKey.ScopeOf("Structure.rvt");

            var key = ClashKey.Create(ElementKey.FromInstanceGuid(scope, Guid.Parse(a)),
                                      ElementKey.FromInstanceGuid(scope, Guid.Parse(b)), 1, 2, 3);

            var finding = Finding.Create(FindingSource.Clash, "MEP vs Structure", "Clash",
                                         new[] { key.A, key.B }, ClashFindings.CellOf(key));

            finding.TopicGuid = ClashBcf.TopicGuidFor(key);
            finding.X = 1;
            finding.Y = 2;
            finding.Z = 3;

            return finding;
        }

        /// <summary>Write one clash, naming as many of its two elements as the caller says.</summary>
        private static FindingBcfDocument Written(Finding finding, string? ifcA = null,
                                                  string? ifcB = null,
                                                  Func<ElementKey, ElementLocator>? at = null) =>
            FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0] ? ifcA
                                 : key == finding.Elements[1] ? ifcB
                                 : null,
                LocatorOf = at,
            });

        private const string A = "11111111-1111-1111-1111-111111111111";
        private const string B = "22222222-2222-2222-2222-222222222222";

        // A real compressed GlobalId: 22 characters of the IFC base-64 alphabet.
        private const string GuidA = "0Zx3aQr9j5RfW1tK8mPqLd";
        private const string GuidB = "1Yw2bPq8i4QeV0sJ7lOpKc";

        [Fact]
        public void ATopicWithNoIfcIdsIsCountedBlind()
        {
            var written = Written(Found(A, B));

            Assert.Equal(1, written.Findings);
            Assert.Equal(1, written.Blind);
            Assert.Equal(0, written.Partial);
            Assert.Equal(0, written.Resolvable);
        }

        [Fact]
        public void OneSideNamedIsPartialNotBlind()
        {
            var written = Written(Found(A, B), GuidA);

            Assert.Equal(0, written.Blind);
            Assert.Equal(1, written.Partial);
            Assert.Equal(0, written.Resolvable);
        }

        [Fact]
        public void BothSidesNamedReachTheReceivingTool()
        {
            var written = Written(Found(A, B), GuidA, GuidB);

            Assert.Equal(0, written.Blind);
            Assert.Equal(0, written.Partial);
            Assert.Equal(1, written.Resolvable);
        }

        [Fact]
        public void TheCountIsSaidOutLoudWhenAnythingCannotBeReached()
        {
            var blind = Written(Found(A, B));

            Assert.Contains(blind.Warnings,
                w => w.Contains("no elements", StringComparison.OrdinalIgnoreCase)
                     || w.Contains("no element", StringComparison.OrdinalIgnoreCase));

            // And says what fixes it, because "cannot be selected" without "here is why" is a
            // warning somebody reads once and never acts on.
            Assert.Contains(blind.Warnings, w => w.Contains("GlobalId", StringComparison.Ordinal));

            // Nothing is said when every topic reaches.
            var fine = Written(Found(A, B), GuidA, GuidB);

            Assert.DoesNotContain(fine.Warnings, w => w.Contains("GlobalId", StringComparison.Ordinal));
        }

        // -----------------------------------------------------------------------------------
        // Where the writing seat had the elements, so the reading seat need not search

        [Fact]
        public void TheLocatorsSurviveTheRoundTrip()
        {
            var finding = Found(A, B);

            var routes = new Dictionary<ElementKey, ElementLocator>
            {
                [finding.Elements[0]] = new ElementLocator(
                    finding.Elements[0], "3/1/44",
                    "Structure.rvt\u001FLevel 02\u001FBeams\u001FUB 305x165"),
                [finding.Elements[1]] = new ElementLocator(
                    finding.Elements[1], "3/1/91",
                    "Structure.rvt\u001FLevel 02\u001FDucts\u001FSupply 400x200"),
            };

            using var stream = new System.IO.MemoryStream(
                Written(finding, at: key => routes[key]).Bytes);

            var back = ClashBcf.Read(stream).Results.Single();

            Assert.Equal("3/1/44", back.Item.PathIdA);
            Assert.Equal("3/1/91", back.Item.PathIdB);
            Assert.Equal("Structure.rvt\u001FLevel 02\u001FBeams\u001FUB 305x165", back.Item.TreeA);
            Assert.Equal("Structure.rvt\u001FLevel 02\u001FDucts\u001FSupply 400x200", back.Item.TreeB);
        }

        [Fact]
        public void AFileWrittenWithoutThemStillReads()
        {
            // A seat that could not resolve the elements writes no route. Absent is an ordinary
            // answer, and the keys are what the reading seat falls back to.
            using var stream = new System.IO.MemoryStream(Written(Found(A, B)).Bytes);

            var back = ClashBcf.Read(stream).Results.Single();

            Assert.Null(back.Item.PathIdA);
            Assert.Null(back.Item.TreeA);
            Assert.False(back.Item.Key.IsEmpty);
        }

        [Fact]
        public void ALocatorKnowsWhetherItSavesASearch()
        {
            var key = ElementKey.FromInstanceGuid(ElementKey.ScopeOf("Structure.rvt"), Guid.Parse(A));

            Assert.True(new ElementLocator(key, "3/1/44", null).HasRoute);
            Assert.True(new ElementLocator(key, null, "a\u001Fb").HasRoute);
            Assert.False(new ElementLocator(key, null, null).HasRoute);
        }

        [Fact]
        public void ABlindTopicStillCarriesACameraSoItIsWorthSending()
        {
            var written = Written(Found(A, B));

            using var stream = new System.IO.MemoryStream(written.Bytes);

            var topic = CamelWorks.Core.OpenBim.BcfReader.Read(stream).Topics.Single();

            Assert.NotNull(topic.Viewpoints.Single().Camera);
        }
    }
}
