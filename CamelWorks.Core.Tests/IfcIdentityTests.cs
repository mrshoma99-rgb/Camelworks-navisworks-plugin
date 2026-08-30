using System;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The IFC GlobalId, found and compressed.
    ///
    /// This is what decides whether a clash exported to Solibri or Revizto highlights the two
    /// elements or shows an empty selection, so the two failure directions both matter: missing a
    /// GlobalId that is there, and writing something that is not one.
    /// </summary>
    public class IfcIdentityTests
    {
        [Fact]
        public void AnAlreadyCompressedGlobalIdIsPassedThrough()
        {
            const string given = "2O2Fr$t4X7Zf8NOew3FLOH";

            Assert.True(IfcGuid.IsCompressed(given));
            Assert.Equal(given, IfcGuid.From(given));
        }

        [Fact]
        public void AllZeroesCompressesToTwentyTwoZeroes()
        {
            Assert.Equal(new string('0', 22), IfcGuid.Compress(Guid.Empty));
        }

        [Fact]
        public void TheLowestByteLandsInTheLastDigit()
        {
            // Hand-computed: the final three bytes are 00 00 01, which is 1, which is "0001".
            Assert.Equal("0000000000000000000001",
                         IfcGuid.Compress(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        }

        [Fact]
        public void TheFirstByteLandsInTheFirstTwoDigits()
        {
            // The leading byte is 0x01, so the first chunk is 1, which is "01" in two digits.
            Assert.Equal("01", IfcGuid.Compress(Guid.Parse("01000000-0000-0000-0000-000000000000"))
                                       .Substring(0, 2));

            // 0xFF is 3*64 + 63, so "3$" - the last letter of the alphabet after a leading 3.
            Assert.Equal("3$", IfcGuid.Compress(Guid.Parse("ff000000-0000-0000-0000-000000000000"))
                                       .Substring(0, 2));
        }

        [Fact]
        public void CompressionUsesTheCanonicalByteOrderNotDotNetsInternalOne()
        {
            // Guid.ToByteArray puts the first three fields little-endian. Using it would compress
            // this to a legal-looking string naming a different element, which is the one failure
            // nobody would ever notice.
            var compressed = IfcGuid.Compress(Guid.Parse("01020304-0000-0000-0000-000000000000"));

            // 0x01 -> "01"; then bytes 02 03 04 -> 0x020304 = 132356 -> base64 digits 0,569... hand
            // checked below by re-deriving the value rather than trusting a remembered string.
            var value = (0x02 << 16) | (0x03 << 8) | 0x04;
            const string Alphabet =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

            var expected = new char[4];
            for (var i = 3; i >= 0; i--) { expected[i] = Alphabet[value % 64]; value /= 64; }

            Assert.Equal("01" + new string(expected), compressed.Substring(0, 6));
        }

        [Fact]
        public void EveryCompressionIsItselfAValidGlobalId()
        {
            for (var i = 0; i < 200; i++)
                Assert.True(IfcGuid.IsCompressed(IfcGuid.Compress(Guid.NewGuid())));
        }

        [Fact]
        public void DifferentGuidsCompressDifferently()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 500; i++) Assert.True(seen.Add(IfcGuid.Compress(Guid.NewGuid())));
        }

        [Theory]
        [InlineData("{11111111-1111-1111-1111-111111111111}")]
        [InlineData("11111111-1111-1111-1111-111111111111")]
        [InlineData("11111111111111111111111111111111")]
        public void AGuidInAnyOfItsWrittenFormsIsAccepted(string written)
        {
            Assert.Equal(IfcGuid.Compress(Guid.Parse(written)), IfcGuid.From(written));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123456")]
        [InlineData("Duct Segment")]
        [InlineData("348295")]
        public void AnythingThatIsNotAGuidIsRefused(string? given)
        {
            // A Revit element id written into a BCF component produces a topic pointing at an
            // empty selection. Refusing is better: the export counts unresolvable components and
            // says how many.
            Assert.Null(IfcGuid.From(given));
        }

        /// <summary>One element from an IFC-derived model, with nothing on it yet.</summary>
        private static FakeItem Item()
        {
            var doc = new FakeDocument();

            return doc.AddItem(doc.AddModel("MEP", @"C:\proj\MEP.ifc"), "Duct Segment", "Ducts");
        }

        // -----------------------------------------------------------------------------------

        [Theory]
        [InlineData("GlobalId")]
        [InlineData("GLOBALID")]
        [InlineData("global_id")]
        [InlineData("Global Id")]
        [InlineData("IfcGUID")]
        [InlineData("IFC GUID")]
        [InlineData("IfcGloballyUniqueId")]
        public void TheNamesAnExporterFilesItUnderAreAllFound(string name)
        {
            Assert.True(IfcIdentity.Wanted(name));
        }

        [Theory]
        [InlineData("GUID")]
        [InlineData("Id")]
        [InlineData("Element ID")]
        [InlineData("Instance GUID")]
        public void APropertyMerelyCalledGuidIsNotTreatedAsOne(string name)
        {
            // Navisworks gives every element an instance GUID of its own. Writing that into a BCF
            // component names nothing in the receiving model.
            Assert.False(IfcIdentity.Wanted(name));
        }

        [Fact]
        public void TheGlobalIdIsReadOffTheElement()
        {
            var item = Item().WithProperty("IFC", "GlobalId", "2O2Fr$t4X7Zf8NOew3FLOH");

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", IfcIdentity.Of(item));
        }

        [Fact]
        public void AFullGuidOnTheElementIsCompressedOnTheWayOut()
        {
            var item = Item().WithProperty("IFC", "IfcGUID", "11111111-1111-1111-1111-111111111111");

            Assert.Equal(IfcGuid.Compress(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                         IfcIdentity.Of(item));
        }

        [Fact]
        public void AnElementWithNoGlobalIdGivesNothing()
        {
            var item = Item()
                .WithProperty("Element", "Element ID", "348295")
                .WithProperty("Item", "GUID", "11111111-1111-1111-1111-111111111111");

            Assert.Null(IfcIdentity.Of(item));
            Assert.Null(IfcIdentity.Of(null));
        }
    }
}
