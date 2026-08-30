using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The register of decisions a read-only document refused.
    ///
    /// These matter more than their size suggests: a published NWD holds its clash results
    /// read-only, so on most days this register <i>is</i> the afternoon's work until somebody
    /// opens the NWF. Losing one is losing a coordination decision.
    /// </summary>
    public class HeldEditsTests
    {
        [Fact]
        public void ForReturnsTheSameEditForTheSameResult()
        {
            var held = new HeldEdits();

            held.For("T1", "R1").Status = "Approved";
            held.For("T1", "R1").Assigned = "Sam";

            Assert.Equal(1, held.Count);
            Assert.Equal("Approved", held.Find("T1", "R1")!.Status);
            Assert.Equal("Sam", held.Find("T1", "R1")!.Assigned);
        }

        [Fact]
        public void ResultsInDifferentTestsDoNotCollide()
        {
            var held = new HeldEdits();

            held.For("T1", "R1").Status = "Approved";
            held.For("T2", "R1").Status = "Resolved";

            Assert.Equal(2, held.Count);
            Assert.Equal("Approved", held.Find("T1", "R1")!.Status);
            Assert.Equal("Resolved", held.Find("T2", "R1")!.Status);
        }

        [Fact]
        public void IdsThatRunTogetherStillDoNotCollide()
        {
            // "AB" + "C" and "A" + "BC" would be one key under naive concatenation.
            var held = new HeldEdits();

            held.For("AB", "C").Status = "Approved";
            held.For("A", "BC").Status = "Resolved";

            Assert.Equal(2, held.Count);
        }

        [Fact]
        public void FindGivesNothingForAResultThatWasNeverEdited()
        {
            var held = new HeldEdits();

            held.For("T1", "R1").Status = "Approved";

            Assert.Null(held.Find("T1", "R2"));
            Assert.Null(held.Find(null, "R1"));
            Assert.Null(held.Find("T1", null));
        }

        [Fact]
        public void ReleaseForgetsOneResultOnly()
        {
            var held = new HeldEdits();

            held.For("T1", "R1").Status = "Approved";
            held.For("T1", "R2").Status = "Resolved";

            Assert.True(held.Release("T1", "R1"));
            Assert.False(held.Release("T1", "R1"));

            Assert.Null(held.Find("T1", "R1"));
            Assert.NotNull(held.Find("T1", "R2"));
        }

        [Fact]
        public void TidyDropsTheRecordsHoldingNothing()
        {
            var held = new HeldEdits();

            held.For("T1", "R1");
            held.For("T1", "R2").Group = "Level 3";

            held.Tidy();

            Assert.Equal(1, held.Count);
            Assert.NotNull(held.Find("T1", "R2"));
        }

        [Fact]
        public void ClearedRegisterIsEmpty()
        {
            var held = new HeldEdits();

            held.For("T1", "R1").Status = "Approved";
            held.Clear();

            Assert.True(held.IsEmpty);
            Assert.Empty(held.All);
        }

        [Fact]
        public void EveryPartOfAnEditSurvivesTheRoundTrip()
        {
            var held = new HeldEdits();

            var edit = held.For("Test A", "42");
            edit.Status = "Approved";
            edit.Assigned = "Sam";
            edit.Group = "Level 3 ducts";
            edit.Name = "Clash 42";
            edit.Comments.Add(new ClashComment("sam", 123456789L, "Raised with the MEP lead."));
            edit.Comments.Add(new ClashComment("ada", 223456789L, "Rerouting."));

            var back = HeldEdits.FromJson(JsonReader.Parse(held.ToJson().ToJson()));

            var read = back.Find("Test A", "42");

            Assert.NotNull(read);
            Assert.Equal("Approved", read!.Status);
            Assert.Equal("Sam", read.Assigned);
            Assert.Equal("Level 3 ducts", read.Group);
            Assert.Equal("Clash 42", read.Name);
            Assert.Equal(2, read.Comments.Count);
            Assert.Equal("sam", read.Comments[0].Author);
            Assert.Equal(123456789L, read.Comments[0].WhenTicks);
            Assert.Equal("Rerouting.", read.Comments[1].Body);
        }

        [Fact]
        public void AnEmptyAssignmentIsKeptApartFromNoAssignment()
        {
            // Clearing an assignee is a decision; never touching it is not. A round trip that
            // confused the two would silently un-assign somebody.
            var held = new HeldEdits();

            held.For("T1", "R1").Assigned = string.Empty;
            held.For("T1", "R2").Status = "Approved";

            var back = HeldEdits.FromJson(JsonReader.Parse(held.ToJson().ToJson()));

            Assert.Equal(string.Empty, back.Find("T1", "R1")!.Assigned);
            Assert.Null(back.Find("T1", "R2")!.Assigned);
        }

        [Fact]
        public void ReadingBackNothingGivesAnEmptyRegister()
        {
            Assert.True(HeldEdits.FromJson(null).IsEmpty);
            Assert.True(HeldEdits.FromJson(JsonValue.Null).IsEmpty);
            Assert.True(HeldEdits.FromJson(JsonValue.Object()).IsEmpty);
        }

        [Fact]
        public void RecordsThatNameNoResultAreSkipped()
        {
            var json = JsonValue.Object().Set("edits", JsonValue.Array(new[]
            {
                JsonValue.Object().Set("status", "Approved"),
                JsonValue.Object().Set("test", "T1").Set("result", "R1").Set("status", "Approved"),
            }));

            var back = HeldEdits.FromJson(json);

            Assert.Equal(1, back.Count);
            Assert.NotNull(back.Find("T1", "R1"));
        }

        [Fact]
        public void DescribeSaysWhatIsWaiting()
        {
            var edit = new HeldEdit("T1", "R1") { Status = "Approved", Assigned = "Sam" };
            edit.Comments.Add(new ClashComment("sam", 1L, "Done."));

            Assert.Equal("status Approved, to Sam, 1 comment", edit.Describe());

            Assert.Equal("unassigned", new HeldEdit("T1", "R1") { Assigned = string.Empty }.Describe());
            Assert.Equal("nothing", new HeldEdit("T1", "R1").Describe());
        }

        [Fact]
        public void AnEditHoldingNothingIsEmpty()
        {
            Assert.True(new HeldEdit("T1", "R1").IsEmpty);
            Assert.False(new HeldEdit("T1", "R1") { Group = string.Empty }.IsEmpty);
        }
    }
}
