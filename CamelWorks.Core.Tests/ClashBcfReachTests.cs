using System;
using System.Linq;
using CamelWorks.Core.Clash;
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
        private static ClashItem Item(string a, string b, string? ifcA = null, string? ifcB = null)
        {
            var scope = ElementKey.ScopeOf("Structure.rvt");

            var first = ElementKey.FromInstanceGuid(scope, Guid.Parse(a));
            var second = ElementKey.FromInstanceGuid(scope, Guid.Parse(b));

            return new ClashItem(ClashKey.Create(first, second, 1, 2, 3), "MEP vs Structure")
            {
                IfcGuidA = ifcA,
                IfcGuidB = ifcB,
            };
        }

        private const string A = "11111111-1111-1111-1111-111111111111";
        private const string B = "22222222-2222-2222-2222-222222222222";

        // A real compressed GlobalId: 22 characters of the IFC base-64 alphabet.
        private const string GuidA = "0Zx3aQr9j5RfW1tK8mPqLd";
        private const string GuidB = "1Yw2bPq8i4QeV0sJ7lOpKc";

        [Fact]
        public void ATopicWithNoIfcIdsIsCountedBlind()
        {
            var written = ClashBcf.Write(new[] { Item(A, B) });

            Assert.Equal(1, written.Results);
            Assert.Equal(1, written.Blind);
            Assert.Equal(0, written.Partial);
            Assert.Equal(0, written.Resolvable);
        }

        [Fact]
        public void OneSideNamedIsPartialNotBlind()
        {
            var written = ClashBcf.Write(new[] { Item(A, B, GuidA) });

            Assert.Equal(0, written.Blind);
            Assert.Equal(1, written.Partial);
            Assert.Equal(0, written.Resolvable);
        }

        [Fact]
        public void BothSidesNamedReachTheReceivingTool()
        {
            var written = ClashBcf.Write(new[] { Item(A, B, GuidA, GuidB) });

            Assert.Equal(0, written.Blind);
            Assert.Equal(0, written.Partial);
            Assert.Equal(1, written.Resolvable);
        }

        [Fact]
        public void TheCountIsSaidOutLoudWhenAnythingCannotBeReached()
        {
            var blind = ClashBcf.Write(new[] { Item(A, B) });

            Assert.Contains(blind.Warnings,
                w => w.Contains("no elements", StringComparison.OrdinalIgnoreCase)
                     || w.Contains("no element", StringComparison.OrdinalIgnoreCase));

            // And says what fixes it, because "cannot be selected" without "here is why" is a
            // warning somebody reads once and never acts on.
            Assert.Contains(blind.Warnings, w => w.Contains("GlobalId", StringComparison.Ordinal));

            // Nothing is said when every topic reaches.
            var fine = ClashBcf.Write(new[] { Item(A, B, GuidA, GuidB) });

            Assert.DoesNotContain(fine.Warnings, w => w.Contains("GlobalId", StringComparison.Ordinal));
        }

        // -----------------------------------------------------------------------------------
        // Where the writing seat had the elements, so the reading seat need not search

        [Fact]
        public void TheLocatorsSurviveTheRoundTrip()
        {
            var item = Item(A, B);

            item.PathIdA = "3/1/44";
            item.PathIdB = "3/1/91";
            item.TreeA = "Structure.rvt\u001FLevel 02\u001FBeams\u001FUB 305x165";
            item.TreeB = "Structure.rvt\u001FLevel 02\u001FDucts\u001FSupply 400x200";

            using var stream = new System.IO.MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var back = ClashBcf.Read(stream).Results.Single();

            Assert.Equal("3/1/44", back.Item.PathIdA);
            Assert.Equal("3/1/91", back.Item.PathIdB);
            Assert.Equal("Structure.rvt\u001FLevel 02\u001FBeams\u001FUB 305x165", back.Item.TreeA);
            Assert.Equal("Structure.rvt\u001FLevel 02\u001FDucts\u001FSupply 400x200", back.Item.TreeB);
        }

        [Fact]
        public void AFileWrittenWithoutThemStillReads()
        {
            // Every file written before the locators existed. Absent is an ordinary answer, and
            // the keys are what the reading seat falls back to.
            using var stream = new System.IO.MemoryStream(ClashBcf.Write(new[] { Item(A, B) }).Bytes);

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
            var written = ClashBcf.Write(new[] { Item(A, B) });

            using var stream = new System.IO.MemoryStream(written.Bytes);

            var topic = CamelWorks.Core.OpenBim.BcfReader.Read(stream).Topics.Single();

            Assert.NotNull(topic.Viewpoints.Single().Camera);
        }
    }
}
