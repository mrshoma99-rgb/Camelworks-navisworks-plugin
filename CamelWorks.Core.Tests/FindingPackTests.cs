using System;
using System.IO;
using System.Linq;
using System.Text;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The one writer, exercised on a check that is not clash.
    ///
    /// Clearance is the case the three writers could not serve. Its findings went out through a
    /// writer that thought in clash items, under a second label vocabulary, with a sidecar that
    /// named twenty-five clash fields and none of its own — so a breach came home with its
    /// measurements gone, six of the writer's own labels adopted as the reviewer's tags, and a
    /// status that depended on which reader opened the file.
    /// </summary>
    public class FindingPackTests
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

        /// <summary>A slab with a duct too low over it. Shifted, the breach moves cell.</summary>
        private static Finding Breach(double shift = 0, double minimum = 2.1)
        {
            var floor = Box("floor", "Slab L3", 0, 0, -0.2, 20, 10, 0);
            var duct = Box("duct", "Duct 400x250", 2 + shift, 2, 1.85, 6 + shift, 4, 2.15);

            var result = Headroom.Check(new[] { floor }, new[] { duct }, minimum);

            return ClearanceFindings.From(result.Spans.Single(), Key('a'), Key('b'), minimum);
        }

        private static FindingPackRead Read(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);

            return FindingBcf.Read(stream);
        }

        /// <summary>A pack as somebody else's tool hands it back: the topics, and nothing of ours.</summary>
        private static byte[] Reauthored(byte[] bytes, Action<BcfTopic>? edited = null)
        {
            var read = Read(bytes);

            foreach (var topic in read.Topics) edited?.Invoke(topic);

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, read.Topics, BcfVersion.V21);

            return theirs.ToArray();
        }

        // -----------------------------------------------------------------------------------
        // One label vocabulary
        // -----------------------------------------------------------------------------------

        [Fact]
        public void APackReadBackAndSentAgainCarriesTheLabelsItLeftWithAndNoMore()
        {
            // The bug this kills: a file written by one writer and read by another took that
            // writer's own labels as the reviewer's tags, appended them to the finding for good
            // and sent them out again next cycle. Three cycles of that and a clearance breach
            // carries eighteen tags nobody typed.
            var finding = Breach();

            finding.Group = "Corridor L3";
            finding.Facts["Level"] = "L03";
            finding.Facts["Grid"] = "C-4";
            finding.Facts["Zone"] = "East wing";
            finding.Labels.Add("Site query");

            var sent = FindingBcf.Write(new[] { finding }).Bytes;
            var labels = Read(sent).Topics.Single().Labels.OrderBy(one => one, StringComparer.Ordinal)
                                  .ToList();

            // Every label this writer put on carries a declared prefix, and the reviewer's own
            // tag is the only bare one.
            Assert.Contains(FindingLabels.Check + FindingSource.Headroom, labels);
            Assert.Contains(FindingLabels.Rule + ClearanceFindings.Rule, labels);
            Assert.Contains(FindingLabels.Group + "Corridor L3", labels);
            Assert.Contains(FindingLabels.Level + "L03", labels);
            Assert.Contains(FindingLabels.Grid + "C-4", labels);
            Assert.Contains(FindingLabels.Zone + "East wing", labels);
            Assert.Equal(new[] { "Site query" }, labels.Where(one => !FindingLabels.Ours(one)));

            // Home again, through the reader that lays a returned topic onto its finding.
            var back = Read(Reauthored(sent)).Topics.Single();

            FindingBcf.Apply(back, finding, "theirs.bcfzip");

            Assert.Equal(new[] { "Site query" }, finding.Labels);

            // And out again: the same labels, not one more.
            var again = Read(FindingBcf.Write(new[] { finding }).Bytes).Topics.Single().Labels
                            .OrderBy(one => one, StringComparer.Ordinal)
                            .ToList();

            Assert.Equal(labels, again);
        }

        [Fact]
        public void EveryNumberTheCheckMeasuredComesHomeWithIt()
        {
            // The generic sidecar. The clash sidecar named clash fields, so a clearance breach
            // travelled with no clear height, no minimum and no shortfall at all.
            var finding = Breach();

            var record = Read(FindingBcf.Write(new[] { finding }).Bytes)
                             .Record(finding.TopicGuid)!;

            Assert.Equal(finding.Id, record.FindingId);
            Assert.Equal(FindingSource.Headroom, record.Source);
            Assert.Equal(ClearanceFindings.Rule, record.Rule);
            Assert.Equal(finding.Discriminator, record.Discriminator);
            Assert.Equal(finding.Elements, record.Elements);
            Assert.Equal(finding.X, record.X);
            Assert.Equal(finding.Z, record.Z);

            foreach (var fact in finding.Facts)
                Assert.Equal(fact.Value, record.Facts[fact.Key]);

            Assert.Equal(finding.Facts[ClearanceFindings.Measured],
                         record.Facts[ClearanceFindings.Measured]);
        }

        // -----------------------------------------------------------------------------------
        // One status
        // -----------------------------------------------------------------------------------

        [Theory]
        [InlineData("Closed", FindingStatus.Resolved)]
        [InlineData("Closed out", FindingStatus.Resolved)]
        [InlineData("Awaiting client", FindingStatus.Active)]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        public void OneReturnedFileYieldsOneStatusThroughEveryPath(string said, FindingStatus lands)
        {
            // Three readers over one format read a status word three ways. The same file now
            // lands the same answer whether it is applied to a finding, merged into the findings
            // register, or read as an issue.
            var finding = Breach();
            var returned = Reauthored(FindingBcf.Write(new[] { finding }).Bytes,
                                      topic => topic.TopicStatus = said);

            var topic = Read(returned).Topics.Single();

            var applied = Breach();

            Assert.True(FindingBcf.Apply(topic, applied));

            var register = new FindingRegister();

            register.Put(Breach());

            var merged = FindingBcf.Merge(register, Read(returned).Topics, "theirs.bcfzip");

            var issues = new IssueRegister();

            FindingBcf.MergeIssues(issues, Read(returned).Topics, "theirs.bcfzip");

            Assert.Equal(lands, applied.Status);
            Assert.Equal(1, merged.Answered);
            Assert.Equal(lands, register.All.Single().Status);
            Assert.Equal(lands, issues.Issues.Single().Status);
        }

        [Fact]
        public void AStaleFileCannotUndoADecisionTakenSinceItLeft()
        {
            // The lattice, on the one reader. Without it a trade returning last month's pack
            // reopens everything approved since.
            var finding = Breach();
            var returned = Reauthored(FindingBcf.Write(new[] { finding }).Bytes,
                                      topic => topic.TopicStatus = "Open");

            finding.Status = FindingStatus.Approved;

            FindingBcf.Apply(Read(returned).Topics.Single(), finding);

            Assert.Equal(FindingStatus.Approved, finding.Status);
        }

        // -----------------------------------------------------------------------------------
        // A finding whose geometry moved between the export and the return
        // -----------------------------------------------------------------------------------

        [Fact]
        public void AnAnswerToAFindingThatMovedIsMatchedOrReportedRatherThanDropped()
        {
            // A clearance finding is re-derived on every run, so a breach whose obstruction moved
            // far enough to change cell comes home under a topic guid the register no longer
            // holds. The trade's answer must not vanish: it is either laid onto the successor or
            // named as unmatched.
            var sent = Breach();
            var returned = Reauthored(FindingBcf.Write(new[] { sent }).Bytes,
                                      topic => topic.TopicStatus = "Closed");

            // The re-run, with the duct four metres along: a different cell, a different id.
            var successor = Breach(shift: 4);

            Assert.NotEqual(sent.Id, successor.Id);

            var register = new FindingRegister();

            register.Put(successor);

            var merged = FindingBcf.Merge(register, Read(returned).Topics, "theirs.bcfzip");

            Assert.Equal(0, merged.Answered);

            // Named, with everything a coordinator needs to place it by hand.
            var stranger = Assert.Single(merged.Strangers);

            Assert.Equal(FindingBcf.TopicGuidFor(sent), stranger.Guid);
            Assert.Equal(FindingStatus.Resolved, ClashStatusMapped(stranger));
            Assert.Equal(FindingStatus.New, successor.Status);
        }

        [Fact]
        public void AnAnswerToAFindingThatStayedPutLandsOnIt()
        {
            // The other half: the same test would pass trivially if nothing ever matched.
            var sent = Breach();
            var returned = Reauthored(FindingBcf.Write(new[] { sent }).Bytes,
                                      topic => topic.TopicStatus = "Closed");

            var register = new FindingRegister();

            register.Put(Breach());

            var merged = FindingBcf.Merge(register, Read(returned).Topics, "theirs.bcfzip");

            Assert.Equal(1, merged.Answered);
            Assert.Empty(merged.Strangers);
            Assert.Equal(FindingStatus.Resolved, register.All.Single().Status);
        }

        private static FindingStatus ClashStatusMapped(BcfTopic topic) =>
            Core.Clash.ClashStatusMap.Read(topic.TopicStatus);

        // -----------------------------------------------------------------------------------
        // The archive the schema validator checks
        // -----------------------------------------------------------------------------------

        [Fact]
        public void AClearanceFindingItsIssueAndTheRelationBothWaysAreWritten()
        {
            var (bytes, finding, issue) = Round();

            var read = Read(bytes);

            Assert.Equal(2, read.Topics.Count);

            var breach = read.Topics.Single(one => one.Guid == FindingBcf.TopicGuidFor(finding));
            var conversation = read.Topics.Single(one => one.Guid != breach.Guid);

            Assert.Contains(conversation.Guid, breach.RelatedTopics);
            Assert.Contains(breach.Guid, conversation.RelatedTopics);
            Assert.Contains(FindingLabels.Issue + issue.Id, conversation.Labels);

            // And a viewpoint on the breach, which is what makes the topic worth opening.
            var viewpoint = breach.Viewpoints.Single();

            Assert.NotNull(viewpoint.Camera);
            Assert.Equal(2, viewpoint.Selection.Count);

            // Clearance is sent boxed: a breach is a space somebody walks under, and a camera on
            // it inside a plant room otherwise shows the wall in front of it.
            Assert.Equal(6, viewpoint.ClippingPlanes.Count);
        }

        [Fact]
        public void WritesTheFilesTheSchemaValidatorChecks()
        {
            // Unit tests cannot prove a BCF file is valid: it can be well-formed, round-trip
            // through our own reader, pass every assertion here, and still be rejected by the
            // receiving tool. So the real output is written out and xmllint checks it against the
            // published XSDs. See CamelWorks/schemas/bcf/validate.sh.
            var root = Path.Combine(AppContext.BaseDirectory, "bcf-validation");

            foreach (var version in new[] { BcfVersion.V21, BcfVersion.V30 })
            {
                var folder = Path.Combine(root, version == BcfVersion.V21 ? "v21" : "v30");

                Directory.CreateDirectory(folder);

                var (bytes, _, _) = Round(version);
                var seen = 0;

                using var stream = new MemoryStream(bytes);
                using var zip = new System.IO.Compression.ZipArchive(
                    stream, System.IO.Compression.ZipArchiveMode.Read);

                foreach (var entry in zip.Entries)
                {
                    var suffix =
                        entry.FullName.EndsWith("markup.bcf", StringComparison.Ordinal) ? ".markup.xml"
                        : entry.FullName.EndsWith(".bcfv", StringComparison.Ordinal) ? ".bcfv.xml"
                        : entry.FullName == "bcf.version" ? ".version.xml"
                        : null;

                    if (suffix == null) continue;

                    using var got = entry.Open();
                    using var text = new StreamReader(got);

                    File.WriteAllText(
                        Path.Combine(folder, "clearance_" + entry.FullName.Replace('/', '_') + suffix),
                        text.ReadToEnd(), new UTF8Encoding(false));

                    seen++;
                }

                // A version file, two markups and the breach's viewpoint.
                Assert.True(seen >= 4, "the dump should hold the version file, both topics and a viewpoint");
            }
        }

        /// <summary>One clearance breach, the issue it is argued in, and the pack holding both.</summary>
        private static (byte[] Bytes, Finding Finding, Issue Issue) Round(
            BcfVersion version = BcfVersion.V21)
        {
            var finding = Breach();

            finding.Group = "Corridor L3";
            finding.AssignedTo = "Mechanical";
            finding.Priority = "High";
            finding.DueDayNumber = Finding.DayNumberFor(new DateTime(2026, 4, 15));
            finding.Comments.Add(new FindingComment("ada", new DateTime(2026, 3, 2).Ticks,
                                                    "The duct can drop 150."));

            var issue = new Issue("CW-7", "Corridor headroom on level 3", FindingSource.Headroom)
            {
                Author = "coordinator",
                CreatedTicks = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
                Description = "Three breaches under the same duct run.",
            };

            IssueFindings.Attach(issue, new[] { finding });

            var how = FindingBcfOptions.For(FindingSource.Headroom);

            how.Version = version;
            how.ProjectName = "Riverside";
            how.SourceFile = "Federation.nwf";
            how.WhenTicks = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc).Ticks;
            how.IfcGuidOf = _ => "2O2Fr$t4X7Zf8NOew3FLOH";

            return (FindingBcf.Write(new[] { finding }, new[] { issue }, how).Bytes, finding, issue);
        }
    }
}
