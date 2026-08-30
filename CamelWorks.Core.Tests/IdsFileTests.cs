using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The two kinds of rule set file, and the one reader that takes both.
    ///
    /// A rule scoped to a set in this model cannot be written as IDS, so a project that writes one
    /// has a choice: a file everyone reads without that rule, or a file with everything that only
    /// CamelWorks reads. What is tested is that both are real - the portable one validates, the
    /// BIMCamel one degrades to a genuine IDS on any machine with an unzip - and that reading is
    /// decided by what the bytes are rather than by what somebody called the file.
    /// </summary>
    public class IdsFileTests
    {
        private static IdsSpecification Scoped(string name = "Ducts carry a system name")
        {
            var specification = new IdsSpecification(name) { Identifier = "S-01" };

            specification.Applicability.Add(new IdsScopeFacet(IdsScope.Of("Level 3 Ductwork")!));

            specification.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Element"), IdsValue.Simple("System Name"),
                IdsValue.Restriction(pattern: "SA-[0-9]+"))
            {
                Instructions = "Name it before issue.",
            });

            return specification;
        }

        private static IdsDocument Mixed()
        {
            var document = new IdsDocument("Riverside rules")
            {
                Version = "2.0",
                Author = "coordination@bimcamel.com",
            };

            document.Specifications.Add(Scoped());
            document.Specifications.Add(IdsFixture.WallsCarryAFireRating());

            return document;
        }

        [Fact]
        public void ABimcamelRuleSetCarriesEverything()
        {
            var packed = IdsFile.Write(Mixed());

            Assert.True(packed.IsUsable);
            Assert.Equal(1, packed.Portable);
            Assert.Equal(1, packed.Scoped);
            Assert.Contains("about a set in this model", packed.Explain());

            var read = IdsFile.Read(packed.Bytes);

            Assert.True(read.IsUsable);
            Assert.Equal(2, read.Document!.Specifications.Count);

            var back = read.Document.Specifications
                .Single(s => IdsScopeSpecification.IsScoped(s));

            Assert.Equal("Ducts carry a system name", back.Name);
            Assert.Equal("S-01", back.Identifier);
            Assert.Equal(IdsScope.Of("Level 3 Ductwork"),
                         IdsScopeSpecification.ScopesOf(back).Single());
        }

        [Fact]
        public void TheRestOfTheRuleSurvivesTheJourney()
        {
            // Not just the scope: the requirement, its pattern, and the instruction somebody wrote
            // for whoever has to fix it.
            var read = IdsFile.Read(IdsFile.Write(Mixed()).Bytes);

            var back = read.Document!.Specifications.Single(IdsScopeSpecification.IsScoped);
            var want = Assert.IsType<IdsPropertyFacet>(back.Requirements.Single());

            Assert.Equal("Element", want.PropertySet.Literal);
            Assert.Equal("System Name", want.BaseName.Literal);
            Assert.Equal("SA-[0-9]+", want.Value!.Pattern);
            Assert.Equal("Name it before issue.", want.Instructions);
        }

        [Fact]
        public void SomebodyWithNoCamelworksCanStillOpenTheIdsInside()
        {
            // The degradation path is a real file rather than a shrug: rename, unzip, open.
            using var buffer = new MemoryStream(IdsFile.Write(Mixed()).Bytes!);
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

            var rules = zip.GetEntry(IdsFile.RulesEntry);

            Assert.NotNull(rules);

            using var stream = rules!.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);

            var text = reader.ReadToEnd();

            Assert.Contains("<ids", text, StringComparison.Ordinal);
            Assert.Contains("Walls carry a fire rating", text, StringComparison.Ordinal);

            // And the rule that could not travel is not smuggled into it.
            Assert.DoesNotContain("Ducts carry a system name", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Level 3 Ductwork", text, StringComparison.Ordinal);
        }

        [Fact]
        public void AnOrdinaryIdsFileStillReads()
        {
            var written = IdsWriter.Write(IdsFixture.Document(IdsFixture.WallsCarryAFireRating()));

            var read = IdsFile.Read(new UTF8Encoding(false).GetBytes(written.Text));

            Assert.True(read.IsUsable);
            Assert.Equal("Walls carry a fire rating", read.Document!.Specifications.Single().Name);
        }

        [Fact]
        public void WhatItIsDecidesHowItIsRead_NotWhatItIsCalled()
        {
            // A file renamed on the way through an email is still the file it was.
            var packed = IdsFile.Write(Mixed()).Bytes!;

            Assert.True(IdsFile.IsArchive(packed));
            Assert.True(IdsFile.Read(packed).IsUsable);

            Assert.False(IdsFile.IsArchive(new UTF8Encoding(false).GetBytes("<ids/>")));
        }

        [Fact]
        public void ARuleSetWithNothingPortableIsStillAWholeFile()
        {
            // The case an IDS file refuses outright. This one carries it.
            var only = new IdsDocument("All ours");
            only.Specifications.Add(Scoped());

            var packed = IdsFile.Write(only);

            Assert.True(packed.IsUsable);
            Assert.Equal(0, packed.Portable);
            Assert.Equal(1, packed.Scoped);

            Assert.Single(IdsFile.Read(packed.Bytes).Document!.Specifications);
        }

        [Fact]
        public void TwoPeopleCanSendTheSameRulesBackAndForwards()
        {
            // Out, in, out again. A rule set that degraded a little on each cycle would be worse
            // than one that never travelled.
            var once = IdsFile.Read(IdsFile.Write(Mixed()).Bytes).Document!;
            var twice = IdsFile.Read(IdsFile.Write(once).Bytes).Document!;

            Assert.Equal(2, twice.Specifications.Count);
            Assert.Single(twice.Specifications.Where(IdsScopeSpecification.IsScoped));

            Assert.Equal(IdsScope.Of("Level 3 Ductwork"),
                IdsScopeSpecification.ScopesOf(
                    twice.Specifications.Single(IdsScopeSpecification.IsScoped)).Single());
        }

        [Fact]
        public void NothingToWriteIsSaidRatherThanWritten()
        {
            Assert.False(IdsFile.Write(null).IsUsable);
            Assert.False(IdsFile.Write(new IdsDocument("Empty")).IsUsable);
            Assert.Contains("nothing to write", IdsFile.Write(null).Explain(),
                            StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AFileThatIsNotOneIsAnswered()
        {
            Assert.False(IdsFile.Read(null).IsUsable);
            Assert.False(IdsFile.Read(Array.Empty<byte>()).IsUsable);

            // Begins like a zip and is not one.
            var lying = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };

            var read = IdsFile.Read(lying);

            Assert.False(read.IsUsable);
            Assert.NotEmpty(read.Warnings);
        }

        [Fact]
        public void AnArchiveWithNoRulesInItSaysSo()
        {
            byte[] bytes;

            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                    zip.CreateEntry("readme.txt");

                bytes = buffer.ToArray();
            }

            var read = IdsFile.Read(bytes);

            Assert.False(read.IsUsable);
            Assert.Contains("neither an IDS nor any CamelWorks rules",
                            string.Join(" ", read.Warnings), StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleThisVersionCannotReadIsCountedRatherThanSwallowed()
        {
            byte[] bytes;

            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = zip.CreateEntry(IdsFile.ExtraEntry);

                    using var stream = entry.Open();
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));

                    writer.Write(JsonValue.Object()
                        .Set("title", "Half readable")
                        .Set("rules", JsonValue.Array(new[]
                        {
                            IdsJson.Write(Scoped()),
                            JsonValue.Object().Set("name", "From a later version"),
                        }))
                        .ToJson());
                }

                bytes = buffer.ToArray();
            }

            var read = IdsFile.Read(bytes);

            Assert.True(read.IsUsable);
            Assert.Single(read.Document!.Specifications);
            Assert.Contains("could not be read and were left out",
                            string.Join(" ", read.Warnings), StringComparison.Ordinal);
        }

        [Fact]
        public void EitherNameIsARuleSet()
        {
            Assert.True(IdsFile.Names(@"C:\Jobs\rules.ids"));
            Assert.True(IdsFile.Names(@"C:\Jobs\rules.bcids"));
            Assert.True(IdsFile.Names("RULES.BCIDS"));
            Assert.False(IdsFile.Names(@"C:\Jobs\rules.txt"));
            Assert.False(IdsFile.Names(null));
        }
    }
}
