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
