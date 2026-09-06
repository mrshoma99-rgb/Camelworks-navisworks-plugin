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

        private static ClashPackRead RoundTrip(params ClashItem[] items) =>
            RoundTrip(ClashBcf.Write(items));

        private static ClashPackRead RoundTrip(ClashBcfDocument written)
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
        public void TheIfcGlobalIdReachesTheComponentSoAToolCanSelectTheElement()
        {
            // The whole point of carrying it: without an IfcGuid a receiving tool gets the camera
            // and an empty selection.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.IfcGuidA = "2O2Fr$t4X7Zf8NOew3FLOH";
            item.IfcGuidB = "1kTvXnbbzCWw8lcMd1dR4o";

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var selection = BcfReader.Read(stream).Topics.Single().Viewpoints.Single().Selection;

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", selection[0].IfcGuid);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", selection[1].IfcGuid);

            // And both are resolvable, which is what the writer counts and reports on.
            Assert.All(selection, c => Assert.True(c.IsResolvable));
        }

        [Fact]
        public void AFullGuidIsCompressedOnItsWayIntoTheComponent()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.IfcGuidA = "3b0a0b0c-0d0e-0f10-1112-131415161718";

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var written = BcfReader.Read(stream).Topics.Single().Viewpoints.Single().Selection[0].IfcGuid;

            Assert.Equal(CamelWorks.Core.OpenBim.IfcGuid.Compress(
                             Guid.Parse("3b0a0b0c-0d0e-0f10-1112-131415161718")), written);
        }

        [Fact]
        public void TheGlobalIdsSurviveOurOwnRoundTrip()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.IfcGuidA = "2O2Fr$t4X7Zf8NOew3FLOH";
            item.IfcGuidB = "1kTvXnbbzCWw8lcMd1dR4o";

            var back = RoundTrip(item).Items.Single();

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", back.IfcGuidA);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", back.IfcGuidB);
        }

        [Fact]
        public void AForeignFileGivesItsGlobalIdsBackEvenWithNoSidecar()
        {
            // A file that went through somebody else's tool keeps its components, so the ids come
            // home off the topic and the next export can still select the elements.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.IfcGuidA = "2O2Fr$t4X7Zf8NOew3FLOH";
            item.IfcGuidB = "1kTvXnbbzCWw8lcMd1dR4o";

            using var mine = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(mine).Topics.Single();

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            var back = ClashBcf.Read(theirs).Items.Single();

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", back.IfcGuidA);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", back.IfcGuidB);
        }

        [Fact]
        public void APictureIsWrittenBesideTheTopicThatCarriesIt()
        {
            // Every coordination tool shows its issue list as thumbnails, so a file without these
            // arrives as rows of grey boxes.
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

            var written = ClashBcf.Write(new[]
                {
                    Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new ClashBcfOptions { Snapshot = _ => png });

            Assert.Equal(1, written.Snapshots);

            using var stream = new MemoryStream(written.Bytes);
            using var zip = new System.IO.Compression.ZipArchive(stream,
                System.IO.Compression.ZipArchiveMode.Read);

            var image = zip.Entries.Single(e => e.FullName.EndsWith(".png", StringComparison.Ordinal));

            using var got = image.Open();
            using var buffer = new MemoryStream();

            got.CopyTo(buffer);

            Assert.Equal(png, buffer.ToArray());

            // Beside its own topic, and named for its viewpoint, which is what makes a reader
            // able to find it.
            var topic = zip.Entries.Single(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal));

            Assert.Equal(topic.FullName.Substring(0, topic.FullName.IndexOf('/')),
                         image.FullName.Substring(0, image.FullName.IndexOf('/')));
        }

        [Fact]
        public void AClashThatWillNotDrawIsWrittenWithoutAPicture()
        {
            // A document that cannot render off-screen, or one clash the engine refuses: the
            // topic is written anyway. Never a failed export over a missing thumbnail.
            var written = ClashBcf.Write(new[]
                {
                    Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new ClashBcfOptions { Snapshot = _ => null });

            Assert.Equal(0, written.Snapshots);
            Assert.Equal(1, written.Results);
        }

        [Fact]
        public void APictureThatThrowsDoesNotTakeTheExportWithIt()
        {
            var written = ClashBcf.Write(new[]
                {
                    Item("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new ClashBcfOptions { Snapshot = _ => throw new InvalidOperationException("no GPU") });

            Assert.Equal(0, written.Snapshots);
            Assert.Equal(1, written.Results);
            Assert.True(RoundTrip(written).IsUsable);
        }

        [Fact]
        public void TheTopicGuidIsRecomputableFromTheModelAlone()
        {
            // The contract the whole round trip rests on. A markup that comes home stripped of
            // everything of ours still names its result, because the receiving side derives the
            // same GUID from the clash it already has.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            Assert.Equal(ClashBcf.TopicGuidFor(item.Key),
                         BcfReader.Read(stream).Topics.Single().Guid);
        }

        [Fact]
        public void TwoCrossingsOfOnePairAreTwoTopics()
        {
            // A duct that crosses one beam twice is two results, and coarsely decomposed models -
            // a whole system as one item - make that ordinary. Keying the topic on the element
            // pair alone put both at one path in the archive: two markup.bcf entries with one
            // name, which is malformed, and whose second topic most readers silently drop.
            var a = Key("11111111-1111-1111-1111-111111111111");
            var b = Key("22222222-2222-2222-2222-222222222222");

            var first = ClashKey.Create(a, b, 10, 20, 30);
            var second = ClashKey.Create(a, b, 90, 20, 30);

            Assert.NotEqual(ClashBcf.TopicGuidFor(first), ClashBcf.TopicGuidFor(second));
        }

        [Fact]
        public void AnArchiveNeverHasTwoEntriesAtOnePath()
        {
            // The invariant behind it: whatever the board holds, the file stays a valid archive.
            var a = Key("11111111-1111-1111-1111-111111111111");
            var b = Key("22222222-2222-2222-2222-222222222222");

            var written = ClashBcf.Write(new[]
            {
                new ClashItem(ClashKey.Create(a, b, 10, 20, 30), "T") { X = 10, Y = 20, Z = 30 },
                new ClashItem(ClashKey.Create(a, b, 90, 20, 30), "T") { X = 90, Y = 20, Z = 30 },
            });

            Assert.Equal(2, written.Results);

            using var stream = new MemoryStream(written.Bytes);
            using var zip = new System.IO.Compression.ZipArchive(stream,
                System.IO.Compression.ZipArchiveMode.Read);

            var paths = zip.Entries.Select(e => e.FullName).ToList();

            Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(2, paths.Count(p => p.EndsWith("markup.bcf", StringComparison.Ordinal)));
        }

        [Fact]
        public void AClashThatMovedIsStillFoundThroughItsNeighbours()
        {
            // Identity is exact so the archive stays valid; tolerance lives at match time. A
            // conflict whose point drifted a cell still has to come home.
            var a = Key("11111111-1111-1111-1111-111111111111");
            var b = Key("22222222-2222-2222-2222-222222222222");

            var sent = ClashKey.Create(a, b, 10.0, 20.0, 30.0);
            var moved = ClashKey.Create(a, b, 10.3, 20.0, 30.0);

            Assert.NotEqual(ClashBcf.TopicGuidFor(sent), ClashBcf.TopicGuidFor(moved));

            // The model, holding the moved clash, still offers the id the file went out under.
            Assert.Contains(ClashBcf.TopicGuidFor(sent), ClashBcf.NearbyTopicGuidsFor(moved));

            // And it does not reach so far that a genuinely different crossing is swept in.
            var elsewhere = ClashKey.Create(a, b, 90.0, 20.0, 30.0);

            Assert.DoesNotContain(ClashBcf.TopicGuidFor(elsewhere),
                                  ClashBcf.NearbyTopicGuidsFor(moved));
        }

        [Fact]
        public void AMarkupGoesHomeUnderTheNameItCameOutUnder()
        {
            // Read a file and save it back: the topic keeps its identity rather than getting a
            // freshly derived one, so the seat syncing it can still place it.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            var sent = ClashBcf.Write(new[] { item });

            var back = RoundTrip(sent).Items.Single();

            Assert.Equal(ClashBcf.TopicGuidFor(item.Key), back.TopicGuid);

            using var again = new MemoryStream(ClashBcf.Write(new[] { back }).Bytes);

            Assert.Equal(ClashBcf.TopicGuidFor(item.Key),
                         BcfReader.Read(again).Topics.Single().Guid);
        }

        [Fact]
        public void AForeignRoundTripKeepsTheTopicIdentityEvenWithNothingElseLeft()
        {
            // Solibri, BIMcollab or Revizto: the sidecar is gone and the components have been
            // re-authored. The topic GUID is the one thing BCF will not let them change, and it
            // is enough.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var mine = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(mine).Topics.Single();

            topic.TopicStatus = "Closed";
            topic.AssignedTo = "Ada";
            topic.Viewpoints.Clear();          // their tool re-authored the viewpoint away

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            var returned = ClashBcf.Read(theirs).Results.Single();

            // Nothing of ours survived - and the topic still names the clash it came from.
            Assert.Null(returned.Item);
            Assert.Equal(ClashBcf.TopicGuidFor(item.Key), returned.FileGuid);
            Assert.Equal(FindingStatus.Resolved, returned.Status);
            Assert.Equal("Ada", returned.AssignedTo);
        }

        [Fact]
        public void WhatTheModelCannotHoldComesBackAsANote()
        {
            // A reviewer sets a priority and a due date in their tool. A Navisworks clash result
            // has neither field, and dropping them silently is what this round trip exists to
            // avoid - so they become a comment on the clash.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var mine = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(mine).Topics.Single();

            topic.Priority = "High";
            topic.Stage = "Construction";
            topic.DueDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
            topic.Labels.Add("Site query");

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            var back = ClashBcf.Read(theirs).Results.Single();
            var aside = ClashBcf.Aside(back, back.Name);

            Assert.NotNull(aside);
            Assert.Contains("priority High", aside!);
            Assert.Contains("stage Construction", aside);
            Assert.Contains("due 2026-04-15", aside);
            Assert.Contains("Site query", aside);
        }

        [Fact]
        public void AnOrdinarySyncAddsNoNote()
        {
            // Nothing set means nothing said. A note on every clash every week is noise nobody
            // reads, which is how a useful note stops being read.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            var back = RoundTrip(item).Results.Single();

            Assert.Null(ClashBcf.Aside(back, back.Name));
        }

        [Fact]
        public void OurOwnLabelsAreNotReadBackAsTheReviewersTags()
        {
            // Test, folder, group, level, grid and zone went out of this writer. Saying them back
            // as something the reviewer set would be a note about nothing.
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            item.TestFolder = "Coordination";
            item.CarriedGroup = "Level 3 ducts";
            item.Level = "L03";
            item.Grid = "C-4";
            item.Zone = "Plant room";

            var back = RoundTrip(item).Results.Single();

            Assert.Empty(back.Labels);
            Assert.Null(ClashBcf.Aside(back, back.Name));
        }

        [Fact]
        public void TheTopicsComeBackSoTheOnesWithNoClashCanBeRaised()
        {
            var item = Item("11111111-1111-1111-1111-111111111111",
                            "22222222-2222-2222-2222-222222222222");

            using var mine = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(mine).Topics.Single();

            // What a reviewer raises in their own tool: a problem with no clash behind it.
            var raised = new BcfTopic("their-own-finding", "Handrail missing on the east stair",
                                      DateTimeOffset.UtcNow, "ada")
            {
                TopicType = "Issue",
                TopicStatus = "Open",
                Description = "No handrail shown between levels 2 and 3.",
            };

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic, raised }, BcfVersion.V21);
            theirs.Position = 0;

            var read = ClashBcf.Read(theirs);

            Assert.Equal(2, read.Topics.Count);

            // And it lands in the register through the path the Issue Manager already uses.
            var register = new CamelWorks.Core.Issues.IssueRegister();

            var merged = CamelWorks.Core.Issues.IssueBcf.Merge(
                register,
                read.Topics.Where(t => t.Guid == raised.Guid).ToList(),
                "ada.bcfzip");

            Assert.Equal(1, merged.Added);
            Assert.Equal("Handrail missing on the east stair", register.Issues.Single().Title);
        }

        [Fact]
        public void AFileThatIsNotAnArchiveCarriesNoTopicsAndThatIsNotAFault()
        {
            // A caller asking for the topics of something that could not be read gets an empty
            // list rather than a null to guard.
            var read = ClashFile.Read(System.Text.Encoding.UTF8.GetBytes("<exchange />"));

            Assert.NotNull(read.Topics);
            Assert.Empty(read.Topics);
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
