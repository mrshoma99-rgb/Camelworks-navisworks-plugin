using System;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Taking back what the two old side registers were holding.
    ///
    /// Somebody who marked up a published NWD before findings existed has an afternoon's
    /// coordination sitting under two keys in the project file. A release that simply stopped
    /// reading them would throw it away without saying so, which is why this exists and why it is
    /// tested for being idempotent: it runs on every read.
    /// </summary>
    public class ClashLegacyTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.ifc");

        private static ElementKey Key(char name) =>
            ElementKey.FromInstanceGuid(Scope, Guid.Parse(name + "0000000-0000-0000-0000-000000000000"));

        private static Finding Made()
        {
            var finding = Finding.Create(FindingSource.Clash, "MEP vs Structure",
                                         "Clash 1", new[] { Key('a'), Key('b') }, "1,2,3");

            finding.Facts[ClashFindings.HostTest] = "T1";
            finding.Facts[ClashFindings.HostId] = "R1";
            finding.TopicGuid = "topic-1";
            finding.HostHasIt = true;

            return finding;
        }

        private static JsonValue Held() =>
            JsonValue.Object().Set(ClashLegacy.HeldKey, JsonValue.Array(new[]
            {
                JsonValue.Object()
                    .Set("test", "T1")
                    .Set("result", "R1")
                    .Set("status", "Reviewed")
                    .Set("assigned", "Structures")
                    .Set("group", "Level 3")
                    .Set("name", "Duct through transfer beam")
                    .Set("comments", JsonValue.Array(new[]
                    {
                        JsonValue.Object()
                            .Set("author", "ada")
                            .Set("when", 12L)
                            .Set("body", "The beam cannot move."),
                    })),
            }));

        private static JsonValue Asides() =>
            JsonValue.Object().Set(ClashLegacy.AsideKey, JsonValue.Array(new[]
            {
                JsonValue.Object()
                    .Set("topic", "topic-1")
                    .Set("priority", "High")
                    .Set("due", new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks),
            }));

        [Fact]
        public void RefusedEditsComeBackAsFieldsThatAreStillWaiting()
        {
            var finding = Made();

            Assert.Equal(1, ClashLegacy.Fold(Held(), null, new[] { finding }));

            Assert.Equal(FindingStatus.Reviewed, finding.Status);
            Assert.Equal("Structures", finding.AssignedTo);
            Assert.Equal("Level 3", finding.Group);
            Assert.Equal("Duct through transfer beam", finding.Title);
            Assert.Equal("The beam cannot move.", finding.Comments.Single().Body);

            // Still waiting: nothing has written them into a document, and saying otherwise would
            // quietly drop them the next time somebody pressed Sync.
            Assert.True(finding.IsHeld);
            Assert.False(finding.HostHasIt);
            Assert.False(finding.Comments.Single().InHost);
        }

        [Fact]
        public void ReviewerFieldsComeBackOntoTheFinding()
        {
            var finding = Made();

            Assert.Equal(1, ClashLegacy.Fold(null, Asides(), new[] { finding }));

            Assert.Equal("High", finding.Priority);
            Assert.Equal(new DateTime(2026, 4, 15), finding.Due);

            // The host never had a field for these, so they were never waiting for it.
            Assert.False(finding.IsHeld);
        }

        [Fact]
        public void FoldingTwiceChangesNothing()
        {
            // It runs on every read, so this is the property that lets the old keys stay on disk.
            var finding = Made();

            ClashLegacy.Fold(Held(), Asides(), new[] { finding });
            ClashLegacy.Fold(Held(), Asides(), new[] { finding });

            Assert.Single(finding.Comments);
            Assert.Equal(FindingStatus.Reviewed, finding.Status);
        }

        [Fact]
        public void ARecordNamingAResultThisRunHasNoFindingForIsLeftAlone()
        {
            var stranger = Finding.Create(FindingSource.Clash, "MEP vs Structure",
                                          "Clash 2", new[] { Key('c'), Key('d') }, "9,9,9");

            stranger.Facts[ClashFindings.HostTest] = "T1";
            stranger.Facts[ClashFindings.HostId] = "R9";

            Assert.Equal(0, ClashLegacy.Fold(Held(), Asides(), new[] { stranger }));
            Assert.Equal(FindingStatus.New, stranger.Status);
        }

        [Fact]
        public void NothingToFoldIsNotAFailure()
        {
            Assert.Equal(0, ClashLegacy.Fold(null, null, new[] { Made() }));
            Assert.Equal(0, ClashLegacy.Fold(Held(), Asides(), Array.Empty<Finding>()));
        }

        [Fact]
        public void ADecisionAlreadyTakenIsNeverDemotedByAnOldRecord()
        {
            // The lattice promotes. A held "Reviewed" from last year must not undo an Approved
            // somebody has since given.
            var finding = Made();

            finding.Status = FindingStatus.Approved;

            ClashLegacy.Fold(Held(), null, new[] { finding });

            Assert.Equal(FindingStatus.Approved, finding.Status);
        }
    }
}
