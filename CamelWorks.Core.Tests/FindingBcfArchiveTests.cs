using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The archive a findings-and-issues export actually writes.
    ///
    /// Asserted on the zip rather than on the topic objects, because the failure this guards
    /// against is one only the file shows: two topics at one path, a relation written one way,
    /// a viewpoint referenced but not written. The schemas themselves are checked by hand against
    /// the vendored XSDs; what is automated here is the shape those checks were run on.
    /// </summary>
    public class FindingBcfArchiveTests
    {
        private static (Finding Finding, Issue Issue) Pair()
        {
            var finding = Finding.Create(FindingSource.Clash, "MEP vs Structure",
                                         "Duct through beam",
                                         new[]
                                         {
                                             ElementKey.FromInstanceGuid(
                                                 ElementKey.ScopeOf("MEP.ifc"),
                                                 Guid.Parse("11111111-1111-1111-1111-111111111111")),
                                         },
                                         "1,2,3");

            finding.Priority = "High";
            finding.Stage = "Construction";
            finding.DueDayNumber = Finding.DayNumberFor(new DateTime(2026, 4, 15));
            finding.Labels.Add("Site query");
            finding.Comments.Add(new FindingComment("ada", 1, "The beam cannot move."));

            var issue = new Issue("I1", "Duct run through the transfer beam", IssueOrigin.Clash);

            IssueFindings.Attach(issue, new[] { finding });

            return (finding, issue);
        }

        private static byte[] Written(BcfVersion version)
        {
            var (finding, issue) = Pair();

            var topics = FindingBcf.Together(new[] { finding }, new[] { IssueBcf.ToTopic(issue) },
                                             _ => issue.Findings, "CamelWorks",
                                             _ => "11111111-1111-1111-1111-111111111111");

            using var stream = new MemoryStream();
            BcfWriter.Write(stream, topics, version);
            return stream.ToArray();
        }

        [Theory]
        [InlineData(BcfVersion.V21)]
        [InlineData(BcfVersion.V30)]
        public void EveryTopicGetsItsOwnFolderAndNothingIsWrittenTwice(BcfVersion version)
        {
            // The defect this exists for: two topics at one zip path, which is an archive some
            // readers refuse outright and others silently keep half of.
            using var archive = new ZipArchive(new MemoryStream(Written(version)), ZipArchiveMode.Read);

            var names = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(2, names.Count(n => n.EndsWith("markup.bcf", StringComparison.Ordinal)));
        }

        [Theory]
        [InlineData(BcfVersion.V21)]
        [InlineData(BcfVersion.V30)]
        public void TheRelationIsWrittenBothWays(BcfVersion version)
        {
            // A reader that opened the clash first should be able to get to the conversation, not
            // only the other way about.
            using var archive = new ZipArchive(new MemoryStream(Written(version)), ZipArchiveMode.Read);

            var markups = archive.Entries
                .Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal))
                .Select(e =>
                {
                    using var reader = new StreamReader(e.Open());
                    return reader.ReadToEnd();
                })
                .ToList();

            Assert.Equal(2, markups.Count);
            Assert.All(markups, m => Assert.Contains("<RelatedTopic Guid=", m, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(BcfVersion.V21)]
        [InlineData(BcfVersion.V30)]
        public void AViewpointNamedInTheMarkupIsAlsoInTheArchive(BcfVersion version)
        {
            // A markup pointing at a .bcfv that is not there is a topic that opens with no
            // selection in every tool that reads it.
            using var archive = new ZipArchive(new MemoryStream(Written(version)), ZipArchiveMode.Read);

            var views = archive.Entries
                .Where(e => e.FullName.EndsWith(".bcfv", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Single(views);

            using var reader = new StreamReader(views[0].Open());
            var text = reader.ReadToEnd();

            Assert.Contains("<Components", text, StringComparison.Ordinal);
            Assert.Contains("IfcGuid=", text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(BcfVersion.V21)]
        [InlineData(BcfVersion.V30)]
        public void WhatAReviewerCanSetIsInTheFile(BcfVersion version)
        {
            using var archive = new ZipArchive(new MemoryStream(Written(version)), ZipArchiveMode.Read);

            var finding = archive.Entries
                .First(e => e.FullName.EndsWith("markup.bcf", StringComparison.Ordinal)
                            && Text(e).Contains("Duct through beam", StringComparison.Ordinal));

            var text = Text(finding);

            Assert.Contains("High", text, StringComparison.Ordinal);
            Assert.Contains("Construction", text, StringComparison.Ordinal);
            Assert.Contains("2026-04-15", text, StringComparison.Ordinal);
            Assert.Contains("Site query", text, StringComparison.Ordinal);
            Assert.Contains("The beam cannot move.", text, StringComparison.Ordinal);
        }

        private static string Text(ZipArchiveEntry entry)
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
    }
}
