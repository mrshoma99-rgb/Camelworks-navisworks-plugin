using System;
using System.Collections.Generic;
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
    /// Clash results as BCF, through the one writer.
    ///
    /// The point of the format is that another tool can read the file. The point of these tests is
    /// that a clash loses nothing by being written the way every check is written: what goes out is
    /// a finding, what comes back is read as clash results, and everything BCF has no field for
    /// rides the sidecar.
    /// </summary>
    public class ClashBcfTests
    {
        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("model.nwc"), Guid.Parse(guid));

        /// <summary>One clash, as its producer makes it: keyed, celled and topic-stamped.</summary>
        private static Finding Found(string a, string b, string test = "MEP vs Structure",
                                     double x = 10, double y = 20, double z = 30)
        {
            var key = ClashKey.Create(Key(a), Key(b), x, y, z);

            var finding = Finding.Create(FindingSource.Clash, test, "Clash",
                                         new[] { key.A, key.B }, ClashFindings.CellOf(key));

            finding.TopicGuid = ClashBcf.TopicGuidFor(key);
            finding.X = x;
            finding.Y = y;
            finding.Z = z;

            return finding;
        }

        private static ClashKey KeyOf(Finding finding) =>
            ClashKey.Create(finding.Elements[0], finding.Elements[1],
                            finding.X ?? 0, finding.Y ?? 0, finding.Z ?? 0);

        private static FindingBcfDocument Written(params Finding[] findings) =>
            FindingBcf.Write(findings);

        private static ClashPackRead RoundTrip(params Finding[] findings) =>
            RoundTrip(Written(findings));

        private static ClashPackRead RoundTrip(FindingBcfDocument written)
        {
            using var stream = new MemoryStream(written.Bytes);

            return ClashBcf.Read(stream);
        }

        /// <summary>The topics of a written pack, as any other tool would read them.</summary>
        private static IReadOnlyList<BcfTopic> Topics(FindingBcfDocument written)
        {
            using var stream = new MemoryStream(written.Bytes);

            return BcfReader.Read(stream).Topics;
        }

        /// <summary>A pack re-authored by somebody else's tool: our topics, none of our extras.</summary>
        private static ClashPackRead Foreign(params BcfTopic[] topics)
        {
            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, topics, BcfVersion.V21);
            theirs.Position = 0;

            return ClashBcf.Read(theirs);
        }

        [Fact]
        public void EveryClashBecomesOneTopic()
        {
            var written = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                Found("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"));

            Assert.Equal(2, written.Findings);

            var read = RoundTrip(written);

            Assert.True(read.IsUsable);
            Assert.Equal(2, read.Results.Count);
            Assert.Single(read.Tests);
        }

        [Fact]
        public void TheArchiveIsARealBcfAnyToolCanRead()
        {
            var written = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"));

            using var stream = new MemoryStream(written.Bytes);

            var bcf = BcfReader.Read(stream);

            Assert.Single(bcf.Topics);
            Assert.Equal(FindingSource.Clash, bcf.Topics[0].TopicType);
            Assert.Contains(FindingBcf.Sidecar, bcf.Extras.Keys);
        }

        [Fact]
        public void TheDecisionsSurvive()
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            finding.Status = FindingStatus.Approved;
            finding.AssignedTo = "Sam";
            finding.Group = "Level 3 ducts";
            finding.Comments.Add(new FindingComment("sam", new DateTime(2026, 3, 1).Ticks, "Raised with MEP."));
            finding.Comments.Add(new FindingComment("ada", new DateTime(2026, 3, 2).Ticks, "Rerouting."));

            var result = RoundTrip(finding).Results.Single();

            Assert.Equal(FindingStatus.Approved, result.Status);
            Assert.Equal("Sam", result.AssignedTo);
            Assert.Equal("Level 3 ducts", result.Group);
            Assert.Equal(2, result.Comments.Count);
            Assert.Equal("sam", result.Comments[0].Author);
            Assert.Equal("Rerouting.", result.Comments[1].Body);
        }

        [Fact]
        public void EveryFactTheProducerWroteSurvivesTheRoundTrip()
        {
            // The generic sidecar. The clash writer's sheet named twenty-five clash fields, so a
            // check with any other number to carry travelled with none of them; this dumps the
            // finding's facts, whatever they are and whichever check wrote them.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            finding.Facts["Folder"] = "Coordination";
            finding.Facts[ClashFindings.Distance] = ClashFindings.Written(0.045);
            finding.Facts[ClashFindings.CategoryA] = "Duct";
            finding.Facts[ClashFindings.CategoryB] = "Beam";
            finding.Facts["Level"] = "L03";
            finding.Facts["Grid"] = "C-4";
            finding.Facts["Zone"] = "Plant room";
            finding.Facts["Contractor"] = "Alpha";

            var back = RoundTrip(finding).Items.Single();

            Assert.Equal("Coordination", back.TestFolder);
            Assert.Equal(0.045, back.Distance);
            Assert.Equal("Duct", back.CategoryA);
            Assert.Equal("Beam", back.CategoryB);
            Assert.Equal("L03", back.Level);
            Assert.Equal("C-4", back.Grid);
            Assert.Equal("Plant room", back.Zone);

            // A fact this reader has never heard of, which is the whole point of a generic
            // sidecar: it comes back verbatim rather than being dropped for not being a clash
            // field.
            Assert.Equal("Alpha", back.Properties["Contractor"]);
        }

        [Fact]
        public void TheKeyAndThePointComeBackExactly()
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222", "MEP vs Structure",
                                12.345, -6.5, 3.25);

            var back = RoundTrip(finding).Items.Single();

            Assert.Equal(KeyOf(finding), back.Key);
            Assert.Equal(12.345, back.X);
            Assert.Equal(-6.5, back.Y);
            Assert.Equal(3.25, back.Z);
        }

        [Fact]
        public void WhereTheWritingDocumentHadTheElementsTravelsWithThem()
        {
            // An element key is a hash. Without a route the reading seat has to key the whole
            // federation to find one element, which is why selecting the elements of a clash read
            // from a file used to do nothing at all.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                LocatorOf = key => new ElementLocator(key, "host:" + key.UnscopedValue,
                                                      "Model/Level 3/Duct"),
            });

            var back = RoundTrip(written).Items.Single();

            Assert.StartsWith("host:", back.PathIdA);
            Assert.Equal("Model/Level 3/Duct", back.TreeA);
            Assert.StartsWith("host:", back.PathIdB);
        }

        [Fact]
        public void TheSameClashIsTheSameTopicEveryExport()
        {
            // What makes a reviewer's tool merge this week's file into last week's rather than
            // doubling every issue.
            var first = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"));

            var again = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"));

            Assert.Equal(RoundTrip(first).Results.Single().FileGuid,
                         RoundTrip(again).Results.Single().FileGuid);
        }

        [Fact]
        public void TestsAndTheirFoldersSurvive()
        {
            var a = Found("11111111-1111-1111-1111-111111111111",
                          "22222222-2222-2222-2222-222222222222", "MEP vs Structure");

            a.Facts["Folder"] = "Coordination";

            var b = Found("33333333-3333-3333-3333-333333333333",
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
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            finding.Group = "Level 3 ducts";

            Assert.Equal("Level 3 ducts", RoundTrip(finding).Results.Single().Group);

            var flat = FindingBcf.Write(new[] { finding }, null,
                                        new FindingBcfOptions { KeepGroups = false });

            Assert.Null(RoundTrip(flat).Results.Single().Group);
        }

        [Fact]
        public void ThePacksOwnFolderWinsOverTheOneTheHostFiledItUnder()
        {
            // A board folder is made by the grouping rules; the finding's group is what the host
            // filed the result under. A pack written from a board carries the board's.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            finding.Group = "Test 1 group";

            var written = FindingBcf.Write(new[] { finding }, null,
                                           new FindingBcfOptions { GroupOf = _ => "Level 3 ducts" });

            Assert.Equal("Level 3 ducts", RoundTrip(written).Results.Single().Group);
        }

        [Fact]
        public void AFindingThatNamesNoElementIsStillATopic()
        {
            // Set drift's findings carry no elements at all, so a writer that skipped them would
            // put the one check that never points at geometry outside exchange altogether. It is
            // written, with no viewpoint to select nothing with.
            var finding = Finding.CreateParent(FindingSource.SetDrift, "Level 3 MEP",
                                               "The set returns 40 fewer elements");

            var written = FindingBcf.Write(new[] { finding });

            Assert.Equal(1, written.Findings);
            Assert.Equal(0, written.Blind);

            var topic = Topics(written).Single();

            Assert.Empty(topic.Viewpoints);
        }

        [Fact]
        public void TopicsNoToolCanSelectAreCountedAndSaidOut()
        {
            // The one thing a coordinator cannot see by opening the file they just wrote, and the
            // thing the person they sent it to tells them about.
            var written = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"));

            Assert.Equal(1, written.Blind);
            Assert.Contains(written.Warnings, w => w.Contains("cannot find")
                                                   || w.Contains("can find"));
        }

        [Fact]
        public void AFileThatWentThroughAnotherToolStillReads()
        {
            // What comes back from Solibri or BIMcollab: the topics, edited, and no sidecar. The
            // decisions have to survive that, because that round trip is the entire point of BCF.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var topic = Topics(Written(finding)).Single();

            topic.TopicStatus = "Closed";
            topic.AssignedTo = "Ada";
            topic.Comments.Add(new BcfComment(topic.Guid + "-x", DateTimeOffset.UtcNow, "ada",
                                              "Fixed in this week's issue."));

            var read = Foreign(topic);
            var result = read.Results.Single();

            Assert.Equal(FindingStatus.Resolved, result.Status);
            Assert.Equal("Ada", result.AssignedTo);
            Assert.Single(result.Comments);

            // And it still lands back on the right elements, because BCF's own field for a tool's
            // native id is where the key was written.
            Assert.NotNull(result.Item);
            Assert.Equal(finding.Elements[0], result.Item!.Key.A);
            Assert.Equal(finding.Elements[1], result.Item.Key.B);

            Assert.Contains(read.Warnings, w => w.Contains("another tool"));
        }

        [Theory]
        [InlineData("Open", FindingStatus.Active)]
        [InlineData("In Progress", FindingStatus.Active)]
        [InlineData("Awaiting client", ClashStatusMap.Unknown)]
        [InlineData("Closed", FindingStatus.Resolved)]
        [InlineData("Solved", FindingStatus.Resolved)]
        [InlineData("Verified", FindingStatus.Reviewed)]
        [InlineData("Accepted", FindingStatus.Approved)]
        [InlineData("Approved", FindingStatus.Approved)]
        [InlineData("Resolved", FindingStatus.Resolved)]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        public void AnotherToolsStatusWordIsReadForItsSense(string said, FindingStatus expected)
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var topic = Topics(Written(finding)).Single();

            topic.TopicStatus = said;

            Assert.Equal(expected, Foreign(topic).Results.Single().Status);

            // The same word, through the issue register's leg of the same format. One vocabulary
            // means one answer, or the word is reported by name rather than mapped either way.
            Assert.Equal(BcfResponse.StatusOf(said) ?? expected, expected);
        }

        [Theory]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        [InlineData("Open", FindingStatus.Active)]
        [InlineData("Awaiting client", ClashStatusMap.Unknown)]
        [InlineData("Closed out", FindingStatus.Resolved)]
        public void OnePackReadByEveryLegLandsOnOneStatus(string said, FindingStatus expected)
        {
            // The readers over the same file. The clash leg went through ClashStatusMap, the
            // finding leg parsed the name and the issue leg had a switch of its own, so a word
            // outside the five landed in three places differently: the same pack gave the board,
            // the register and the issue list three statuses. "Closed out" is the case only the
            // guess tier maps.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var topic = Topics(Written(finding)).Single();

            topic.TopicStatus = said;

            var held = Finding.Create(FindingSource.Clash, "MEP vs Structure",
                                      "Duct through beam",
                                      new[] { finding.Elements[0], finding.Elements[1] }, "10,20,30");

            Assert.True(FindingBcf.Apply(topic, held));

            var register = new CamelWorks.Core.Issues.IssueRegister();

            FindingBcf.MergeIssues(register, new[] { topic }, "theirs.bcfzip");

            Assert.Equal(expected, Foreign(topic).Results.Single().Status);
            Assert.Equal(expected, held.Status);
            Assert.Equal(expected, register.Issues.Single().Status);
        }

        [Fact]
        public void ACallerThatMustNotGuessRefusesAWordOnlyTheSenseTierReads()
        {
            // The flag exists so an unattended merge can decline to promote a finding on a word
            // the conventional vocabulary does not hold.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var topic = Topics(Written(finding)).Single();

            topic.TopicStatus = "Closed out";

            var held = Finding.Create(FindingSource.Clash, "MEP vs Structure",
                                      "Duct through beam",
                                      new[] { finding.Elements[0], finding.Elements[1] }, "10,20,30");

            FindingBcf.Apply(topic, held, null, guessStatus: false);

            Assert.Equal(FindingStatus.New, held.Status);
        }

        [Fact]
        public void AWordNobodyKnowsIsCountedRatherThanApplied()
        {
            var tally = ClashStatusMap.Tally(new[] { "Rejected", "rejected", "Closed", null });

            Assert.Equal(3, tally.Count);

            var rejected = tally[0];

            Assert.Equal("Rejected", rejected.Word);
            Assert.Equal(2, rejected.Count);
            Assert.True(rejected.IsGuess);
            Assert.Equal(ClashStatusMap.Unknown, rejected.Lands);

            Assert.False(tally.Single(w => w.Word == "Closed").IsGuess);
            Assert.Equal(ClashStatusMap.Unsaid, tally.Single(w => w.Word.Length == 0).Lands);
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
            var viewpoint = Topics(Written(
                Found("11111111-1111-1111-1111-111111111111",
                      "22222222-2222-2222-2222-222222222222"))).Single().Viewpoints.Single();

            Assert.Equal(2, viewpoint.Selection.Count);
            Assert.Equal(2, viewpoint.Colouring.Count);
            Assert.NotNull(viewpoint.Camera);
            Assert.All(viewpoint.Selection, c => Assert.Equal("CamelWorks", c.OriginatingSystem));
        }

        [Fact]
        public void ASectionBoxIsSixPlanesFacingAwayFromTheFindingAndSurvivesTheRoundTrip()
        {
            var how = new FindingBcfOptions { Section = true, SectionMetres = 2 };

            var written = FindingBcf.Write(new[]
            {
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
            }, null, how);

            var viewpoint = Topics(written).Single().Viewpoints.Single();

            Assert.Equal(6, viewpoint.ClippingPlanes.Count);

            // The six sit at the reach either side of the finding, so their mean is its point. A
            // plane's normal names the side that is cut away, so each of the six points away from
            // that centre: pointed inwards, the six between them cut away everything the topic is
            // about.
            var cx = viewpoint.ClippingPlanes.Average(one => one.X);
            var cy = viewpoint.ClippingPlanes.Average(one => one.Y);
            var cz = viewpoint.ClippingPlanes.Average(one => one.Z);

            foreach (var plane in viewpoint.ClippingPlanes)
            {
                var away = (plane.X - cx) * plane.DirectionX
                         + (plane.Y - cy) * plane.DirectionY
                         + (plane.Z - cz) * plane.DirectionZ;

                Assert.Equal(2, away, 6);
            }
        }

        [Fact]
        public void EachCheckIsSentInTheFramingItsResultsAreReadIn()
        {
            // A clearance breach is an area somebody walks under, so it goes out boxed; a fall
            // hazard is an opening in a floor, which reads from above and nowhere else.
            Assert.False(FindingBcfOptions.For(FindingSource.Clash).Section);
            Assert.True(FindingBcfOptions.For(FindingSource.Headroom).Section);
            Assert.Equal(BcfView.Plan, FindingBcfOptions.For(FindingSource.FallHazard).View);
            Assert.Equal(BcfView.Standing, FindingBcfOptions.For(FindingSource.Clash).View);
        }

        [Fact]
        public void TheIfcGlobalIdReachesTheComponentSoAToolCanSelectTheElement()
        {
            // The whole point of carrying it: without an IfcGuid a receiving tool gets the camera
            // and an empty selection.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0]
                    ? "2O2Fr$t4X7Zf8NOew3FLOH"
                    : "1kTvXnbbzCWw8lcMd1dR4o",
            });

            var selection = Topics(written).Single().Viewpoints.Single().Selection;

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", selection[0].IfcGuid);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", selection[1].IfcGuid);

            // And both are resolvable, which is what the writer counts and reports on.
            Assert.All(selection, c => Assert.True(c.IsResolvable));
            Assert.Equal(0, written.Blind);
            Assert.Equal(0, written.Partial);
        }

        [Fact]
        public void AFindingOnlyOneOfWhoseElementsCanBeFoundIsCountedApart()
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0] ? "2O2Fr$t4X7Zf8NOew3FLOH" : null,
            });

            Assert.Equal(0, written.Blind);
            Assert.Equal(1, written.Partial);
        }

        [Fact]
        public void AFullGuidIsCompressedOnItsWayIntoTheComponent()
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0]
                    ? "3b0a0b0c-0d0e-0f10-1112-131415161718"
                    : null,
            });

            var got = Topics(written).Single().Viewpoints.Single().Selection[0].IfcGuid;

            Assert.Equal(CamelWorks.Core.OpenBim.IfcGuid.Compress(
                             Guid.Parse("3b0a0b0c-0d0e-0f10-1112-131415161718")), got);
        }

        [Fact]
        public void TheGlobalIdsSurviveOurOwnRoundTrip()
        {
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0]
                    ? "2O2Fr$t4X7Zf8NOew3FLOH"
                    : "1kTvXnbbzCWw8lcMd1dR4o",
            });

            var back = RoundTrip(written).Items.Single();

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", back.IfcGuidA);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", back.IfcGuidB);
        }

        [Fact]
        public void AForeignFileGivesItsGlobalIdsBackEvenWithNoSidecar()
        {
            // A file that went through somebody else's tool keeps its components, so the ids come
            // home off the topic and the next export can still select the elements.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var written = FindingBcf.Write(new[] { finding }, null, new FindingBcfOptions
            {
                IfcGuidOf = key => key == finding.Elements[0]
                    ? "2O2Fr$t4X7Zf8NOew3FLOH"
                    : "1kTvXnbbzCWw8lcMd1dR4o",
            });

            var back = Foreign(Topics(written).Single()).Items.Single();

            Assert.Equal("2O2Fr$t4X7Zf8NOew3FLOH", back.IfcGuidA);
            Assert.Equal("1kTvXnbbzCWw8lcMd1dR4o", back.IfcGuidB);
        }

        [Fact]
        public void APictureIsWrittenBesideTheTopicThatCarriesIt()
        {
            // Every coordination tool shows its issue list as thumbnails, so a file without these
            // arrives as rows of grey boxes.
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

            var written = FindingBcf.Write(new[]
                {
                    Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new FindingBcfOptions { Snapshot = _ => png });

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
            var written = FindingBcf.Write(new[]
                {
                    Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new FindingBcfOptions { Snapshot = _ => null });

            Assert.Equal(0, written.Snapshots);
            Assert.Equal(1, written.Findings);
        }

        [Fact]
        public void APictureThatThrowsDoesNotTakeTheExportWithIt()
        {
            var written = FindingBcf.Write(new[]
                {
                    Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                },
                null,
                new FindingBcfOptions { Snapshot = _ => throw new InvalidOperationException("no GPU") });

            Assert.Equal(0, written.Snapshots);
            Assert.Equal(1, written.Findings);
            Assert.True(RoundTrip(written).IsUsable);
        }

        [Fact]
        public void TheTopicGuidIsRecomputableFromTheModelAlone()
        {
            // The contract the whole round trip rests on. A markup that comes home stripped of
            // everything of ours still names its result, because the receiving side derives the
            // same GUID from the clash it already has.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            Assert.Equal(ClashBcf.TopicGuidFor(KeyOf(finding)),
                         Topics(Written(finding)).Single().Guid);
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
            var written = Written(
                Found("11111111-1111-1111-1111-111111111111",
                      "22222222-2222-2222-2222-222222222222", "T", 10, 20, 30),
                Found("11111111-1111-1111-1111-111111111111",
                      "22222222-2222-2222-2222-222222222222", "T", 90, 20, 30));

            Assert.Equal(2, written.Findings);

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
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var guid = ClashBcf.TopicGuidFor(KeyOf(finding));
            var back = RoundTrip(Written(finding)).Items.Single();

            Assert.Equal(guid, back.TopicGuid);

            // The finding rebuilt from what came back goes out under the same name, because the
            // topic it arrived under is stamped on it.
            var rebuilt = Finding.Create(FindingSource.Clash, back.TestName, back.Name ?? "Clash",
                                         new[] { back.Key.A, back.Key.B },
                                         ClashFindings.CellOf(back.Key));

            rebuilt.TopicGuid = back.TopicGuid;

            Assert.Equal(guid, Topics(Written(rebuilt)).Single().Guid);
        }

        [Fact]
        public void AForeignRoundTripKeepsTheTopicIdentityEvenWithNothingElseLeft()
        {
            // Solibri, BIMcollab or Revizto: the sidecar is gone and the components have been
            // re-authored. The topic GUID is the one thing BCF will not let them change, and it
            // is enough.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            var topic = Topics(Written(finding)).Single();

            topic.TopicStatus = "Closed";
            topic.AssignedTo = "Ada";
            topic.Viewpoints.Clear();          // their tool re-authored the viewpoint away

            var returned = Foreign(topic).Results.Single();

            // Nothing of ours survived - and the topic still names the clash it came from.
            Assert.Null(returned.Item);
            Assert.Equal(ClashBcf.TopicGuidFor(KeyOf(finding)), returned.FileGuid);
            Assert.Equal(FindingStatus.Resolved, returned.Status);
            Assert.Equal("Ada", returned.AssignedTo);
        }

        [Fact]
        public void WhatTheModelCannotHoldComesBackAsANote()
        {
            // A reviewer sets a priority and a due date in their tool. A Navisworks clash result
            // has neither field, and dropping them silently is what this round trip exists to
            // avoid - so they become a comment on the clash.
            var topic = Topics(Written(
                Found("11111111-1111-1111-1111-111111111111",
                      "22222222-2222-2222-2222-222222222222"))).Single();

            topic.Priority = "High";
            topic.Stage = "Construction";
            topic.DueDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
            topic.Labels.Add("Site query");

            var back = Foreign(topic).Results.Single();
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
            var back = RoundTrip(Found("11111111-1111-1111-1111-111111111111",
                                       "22222222-2222-2222-2222-222222222222")).Results.Single();

            Assert.Null(ClashBcf.Aside(back, back.Name));
        }

        [Fact]
        public void OurOwnLabelsAreNotReadBackAsTheReviewersTags()
        {
            // Check, rule, folder, group, level, grid and zone all went out of this writer.
            // Saying them back as something the reviewer set would grow the finding's tag list by
            // seven on every round trip, and re-export them next cycle as though somebody had
            // typed them.
            var finding = Found("11111111-1111-1111-1111-111111111111",
                                "22222222-2222-2222-2222-222222222222");

            finding.Group = "Level 3 ducts";
            finding.Facts["Folder"] = "Coordination";
            finding.Facts["Level"] = "L03";
            finding.Facts["Grid"] = "C-4";
            finding.Facts["Zone"] = "Plant room";

            var back = RoundTrip(finding).Results.Single();

            Assert.Empty(back.Labels);
            Assert.Null(ClashBcf.Aside(back, back.Name));

            // And the same file read by the finding leg leaves the finding's own tags alone.
            var topic = Topics(Written(finding)).Single();

            FindingBcf.Apply(topic, finding);

            Assert.Empty(finding.Labels);
        }

        [Fact]
        public void TheTopicsComeBackSoTheOnesWithNoClashCanBeRaised()
        {
            var topic = Topics(Written(
                Found("11111111-1111-1111-1111-111111111111",
                      "22222222-2222-2222-2222-222222222222"))).Single();

            // What a reviewer raises in their own tool: a problem with no clash behind it.
            var raised = new BcfTopic("their-own-finding", "Handrail missing on the east stair",
                                      DateTimeOffset.UtcNow, "ada")
            {
                TopicType = "Issue",
                TopicStatus = "Open",
                Description = "No handrail shown between levels 2 and 3.",
            };

            var read = Foreign(topic, raised);

            Assert.Equal(2, read.Topics.Count);

            // And it lands in the register through the path the Issue Manager already uses.
            var register = new CamelWorks.Core.Issues.IssueRegister();

            var merged = FindingBcf.MergeIssues(
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
            var written = Written(
                Found("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"),
                Found("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"));

            using var stream = new MemoryStream(written.Bytes);
            using var zip = new System.IO.Compression.ZipArchive(stream,
                System.IO.Compression.ZipArchiveMode.Read);

            var topics = new HashSet<string>(
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

            Assert.Contains(FindingBcf.Sidecar, zip.Entries.Select(e => e.FullName));
            Assert.DoesNotContain('/', FindingBcf.Sidecar);
        }

        [Fact]
        public void TheSidecarCannotDisplaceTheSchemasOwnFiles()
        {
            using var output = new MemoryStream();

            var written = BcfWriter.Write(output, Array.Empty<BcfTopic>(), BcfVersion.V21, null,
                new Dictionary<string, string>
                {
                    ["bcf.version"] = "not this",
                });

            Assert.Contains(written.Notes, n => n.Contains("bcf.version"));

            output.Position = 0;

            Assert.Equal(BcfVersion.V21, BcfReader.Read(output).Version);
        }
    }
}
