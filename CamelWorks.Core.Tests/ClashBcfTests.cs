using System;
using System.IO;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Clash results as BCF.
    ///
    /// The point of the format change is that another tool can read the file. The point of these
    /// tests is that changing format cost nothing: everything the Navisworks clash XML carried has
    /// to survive the round trip, including the half of it BCF has no field for.
    /// </summary>
    public class ClashBcfTests
    {
        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("model.nwc"), Guid.Parse(guid));

        private static ClashItem Item(string a, string b, string test = "MEP vs Structure")
        {
            var first = Key(a);
            var second = Key(b);

            return new ClashItem(ClashKey.Create(first, second, 10, 20, 30), test)
            {
                X = 10, Y = 20, Z = 30,
            };
        }

        private static ClashXmlRead RoundTrip(params ClashItem[] items) =>
            RoundTrip(ClashBcf.Write(items));

        private static ClashXmlRead RoundTrip(ClashBcfDocument written)
        {
            using var stream = new MemoryStream(written.Bytes);

            return ClashBcf.Read(stream);
        }

        [Fact]
        public void EveryClashBecomesOneTopic()
        {
            var written = ClashBcf.Write(new[]
            {
                Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                Item("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"),
            });

            Assert.Equal(2, written.Results);

            var read = RoundTrip(written);

            Assert.True(read.IsUsable);
            Assert.Equal(2, read.Results.Count);
            Assert.Single(read.Tests);
        }

        [Fact]
        public void TheArchiveIsARealBcfAnyToolCanRead()
        {
            var written = ClashBcf.Write(new[]
            {
                Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
            });

            using var stream = new MemoryStream(written.Bytes);

            var bcf = BcfReader.Read(stream);

            Assert.Single(bcf.Topics);
            Assert.Equal("Clash", bcf.Topics[0].TopicType);
            Assert.Contains(ClashBcf.Sidecar, bcf.Extras.Keys);
        }

        [Fact]
        public void TheDecisionsSurvive()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.Status = FindingStatus.Approved;
            item.AssignedTo = "Sam";
            item.CarriedGroup = "Level 3 ducts";
            item.Comments.Add(new ClashComment("sam", new DateTime(2026, 3, 1).Ticks, "Raised with MEP."));
            item.Comments.Add(new ClashComment("ada", new DateTime(2026, 3, 2).Ticks, "Rerouting."));

            var read = RoundTrip(item);
            var result = read.Results.Single();

            Assert.Equal(FindingStatus.Approved, result.Status);
            Assert.Equal("Sam", result.AssignedTo);
            Assert.Equal("Level 3 ducts", result.Group);
            Assert.Equal(2, result.Comments.Count);
            Assert.Equal("sam", result.Comments[0].Author);
            Assert.Equal("Rerouting.", result.Comments[1].Body);
        }

        [Fact]
        public void EverythingBcfHasNoFieldForSurvivesToo()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.TestFolder = "Coordination";
            item.Distance = 0.045;
            item.OverlapVolume = 0.0031;
            item.CrossingAngleDegrees = 87;
            item.Level = "L03";
            item.Grid = "C-4";
            item.Zone = "Plant room";
            item.ModelA = "MEP.nwc";
            item.ModelB = "Structure.nwc";
            item.CategoryA = "Duct";
            item.CategoryB = "Beam";
            item.DisciplineA = "M";
            item.DisciplineB = "S";
            item.SystemA = "Supply air";
            item.SystemB = "Primary frame";
            item.SetsA.Add("Level 3 MEP");
            item.SetsB.Add("Level 3 frame");
            item.Properties["Contractor"] = "Alpha";

            var back = RoundTrip(item).Items.Single();

            Assert.Equal("Coordination", back.TestFolder);
            Assert.Equal(0.045, back.Distance);
            Assert.Equal(0.0031, back.OverlapVolume);
            Assert.Equal(87, back.CrossingAngleDegrees);
            Assert.Equal("L03", back.Level);
            Assert.Equal("C-4", back.Grid);
            Assert.Equal("Plant room", back.Zone);
            Assert.Equal("MEP.nwc", back.ModelA);
            Assert.Equal("Structure.nwc", back.ModelB);
            Assert.Equal("Duct", back.CategoryA);
            Assert.Equal("Beam", back.CategoryB);
            Assert.Equal("M", back.DisciplineA);
            Assert.Equal("S", back.DisciplineB);
            Assert.Equal("Supply air", back.SystemA);
            Assert.Equal("Primary frame", back.SystemB);
            Assert.Contains("Level 3 MEP", back.SetsA);
            Assert.Contains("Level 3 frame", back.SetsB);
            Assert.Equal("Alpha", back.Properties["Contractor"]);
        }

        [Fact]
        public void TheKeyAndThePointComeBackExactly()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.X = 12.345;
            item.Y = -6.5;
            item.Z = 3.25;

            var back = RoundTrip(item).Items.Single();

            Assert.Equal(item.Key, back.Key);
            Assert.Equal(12.345, back.X);
            Assert.Equal(-6.5, back.Y);
            Assert.Equal(3.25, back.Z);
        }

        [Fact]
        public void TheSameClashIsTheSameTopicEveryExport()
        {
            // What makes a reviewer's tool merge this week's file into last week's rather than
            // doubling every issue.
            var first = ClashBcf.Write(new[]
            {
                Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
            });

            var again = ClashBcf.Write(new[]
            {
                Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
            });

            Assert.Equal(RoundTrip(first).Results.Single().FileGuid,
                         RoundTrip(again).Results.Single().FileGuid);
        }

        [Fact]
        public void TestsAndTheirFoldersSurvive()
        {
            var a = Item("11111111-1111-1111-1111-111111111111",
                         "22222222-2222-2222-2222-222222222222", "MEP vs Structure");
            a.TestFolder = "Coordination";

            var b = Item("33333333-3333-3333-3333-333333333333",
                         "44444444-4444-4444-4444-444444444444", "Ducts vs Ceiling");

            var read = RoundTrip(a, b);

            Assert.Equal(2, read.Tests.Count);

            var test = read.Tests.Single(t => t.Name == "MEP vs Structure");

            Assert.Equal("Coordination", test.Folder);
            Assert.Equal(1, test.Total);
        }

        [Fact]
        public void GroupsTravelWhenTheyAreKept()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");
            item.CarriedGroup = "Level 3 ducts";

            Assert.Equal("Level 3 ducts", RoundTrip(item).Results.Single().Group);

            var flat = ClashBcf.Write(new[] { item }, null, new ClashBcfOptions { KeepGroups = false });

            Assert.Null(RoundTrip(flat).Results.Single().Group);
        }

        [Fact]
        public void AResultThatNamesNoElementIsLeftOutAndSaidSo()
        {
            var written = ClashBcf.Write(new[] { new ClashItem(default, "Nothing") });

            Assert.Equal(0, written.Results);
            Assert.Contains(written.Warnings, w => w.Contains("names no element"));
        }

        [Fact]
        public void AFileThatWentThroughAnotherToolStillReads()
        {
            // What comes back from Solibri or BIMcollab: the topics, edited, and no sidecar. The
            // decisions have to survive that, because that round trip is the entire point of BCF.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(stream).Topics.Single();

            topic.TopicStatus = "Closed";
            topic.AssignedTo = "Ada";
            topic.Comments.Add(new BcfComment(topic.Guid + "-x", DateTimeOffset.UtcNow, "ada",
                                              "Fixed in this week's issue."));

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            var read = ClashBcf.Read(theirs);
            var result = read.Results.Single();

            Assert.Equal(FindingStatus.Resolved, result.Status);
            Assert.Equal("Ada", result.AssignedTo);
            Assert.Single(result.Comments);

            // And it still lands back on the right elements, because BCF's own field for a tool's
            // native id is where the key was written.
            Assert.NotNull(result.Item);
            Assert.Equal(item.Key.A, result.Item!.Key.A);
            Assert.Equal(item.Key.B, result.Item.Key.B);

            Assert.Contains(read.Warnings, w => w.Contains("another tool"));
        }

        [Theory]
        [InlineData("Open", FindingStatus.New)]
        [InlineData("In Progress", FindingStatus.Active)]
        [InlineData("Closed", FindingStatus.Resolved)]
        [InlineData("Solved", FindingStatus.Resolved)]
        [InlineData("Verified", FindingStatus.Reviewed)]
        [InlineData("Accepted", FindingStatus.Approved)]
        [InlineData("Approved", FindingStatus.Approved)]
        [InlineData("Resolved", FindingStatus.Resolved)]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        public void AnotherToolsStatusWordIsReadForItsSense(string said, FindingStatus expected)
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(stream).Topics.Single();
            topic.TopicStatus = said;

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            Assert.Equal(expected, ClashBcf.Read(theirs).Results.Single().Status);
        }

        [Fact]
        public void NothingReadableIsSaidPlainlyRatherThanThrown()
        {
            Assert.False(ClashBcf.Read(null).IsUsable);
            Assert.NotNull(ClashBcf.Read(null).Problem);

            using var rubbish = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

            var read = ClashBcf.Read(rubbish);

            Assert.False(read.IsUsable);
            Assert.NotNull(read.Problem);
        }

        [Fact]
        public void BothElementsAreNamedInTheViewpointSoAReviewerCanSelectThem()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var viewpoint = BcfReader.Read(stream).Topics.Single().Viewpoints.Single();

            Assert.Equal(2, viewpoint.Selection.Count);
            Assert.Equal(2, viewpoint.Colouring.Count);
            Assert.NotNull(viewpoint.Camera);
            Assert.All(viewpoint.Selection, c => Assert.Equal("CamelWorks", c.OriginatingSystem));
        }

        [Fact]
        public void TheArchiveHasNoTopLevelFolderThatIsNotATopic()
        {
            // The invariant that keeps another tool's importer from meeting something new. A
            // .bcfzip's top-level directories are its topics: importers enumerate them and read a
            // markup.bcf out of each, so a directory that is not a topic - and whose name will not
            // parse as a GUID - is the shape most likely to throw rather than be skipped. Our
            // extra data is a file at the root instead, beside bcf.version and project.bcfp,
            // which is a shape every importer already handles.
            var written = ClashBcf.Write(new[]
            {
                Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                Item("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"),
            });

            using var stream = new MemoryStream(written.Bytes);
            using var zip = new System.IO.Compression.ZipArchive(stream,
                System.IO.Compression.ZipArchiveMode.Read);

            var topics = new System.Collections.Generic.HashSet<string>(
                zip.Entries.Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal))
                           .Select(e => e.FullName.Substring(0, e.FullName.IndexOf('/'))),
                StringComparer.Ordinal);

            Assert.Equal(2, topics.Count);

            foreach (var entry in zip.Entries)
            {
                var slash = entry.FullName.IndexOf('/');

                if (slash < 0) continue;   // a root-level file, which the format already has

                Assert.Contains(entry.FullName.Substring(0, slash), topics);
            }

            Assert.Contains(ClashBcf.Sidecar, zip.Entries.Select(e => e.FullName));
            Assert.DoesNotContain('/', ClashBcf.Sidecar);
        }

        [Fact]
        public void AFileWrittenBeforeTheSidecarMovedStillReads()
        {
            // The sidecar used to sit in a folder. Anything written then has to keep opening.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");
            item.Distance = 0.045;

            var written = ClashBcf.Write(new[] { item });

            byte[] moved;

            using (var source = new MemoryStream(written.Bytes))
            using (var zip = new System.IO.Compression.ZipArchive(source,
                       System.IO.Compression.ZipArchiveMode.Read))
            using (var target = new MemoryStream())
            {
                using (var rebuilt = new System.IO.Compression.ZipArchive(target,
                           System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var name = entry.FullName == ClashBcf.Sidecar
                            ? "camelworks/clash.json"
                            : entry.FullName;

                        using var from = entry.Open();
                        using var to = rebuilt.CreateEntry(name).Open();

                        from.CopyTo(to);
                    }
                }

                moved = target.ToArray();
            }

            using var stream = new MemoryStream(moved);

            Assert.Equal(0.045, ClashBcf.Read(stream).Items.Single().Distance);
        }

        [Fact]
        public void TheSidecarCannotDisplaceTheSchemasOwnFiles()
        {
            using var output = new MemoryStream();

            var written = BcfWriter.Write(output, Array.Empty<BcfTopic>(), BcfVersion.V21, null,
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["bcf.version"] = "not this",
                });

            Assert.Contains(written.Notes, n => n.Contains("bcf.version"));

            output.Position = 0;

            Assert.Equal(BcfVersion.V21, BcfReader.Read(output).Version);
        }
    }
}
