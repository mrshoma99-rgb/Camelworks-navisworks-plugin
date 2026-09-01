using CamelWorks.Core.Clash;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What this project calls a clash test when the document will not be told.
    ///
    /// The seat this matters on is a published NWD, whose clash data is read-only - so renaming a
    /// test and filing it in a folder both simply refused, and were the only decisions on the
    /// board with nowhere to go when the document said no.
    /// </summary>
    public class ClashTestNamesTests
    {
        [Fact]
        public void AFreshOneSaysNothingAboutAnyTest()
        {
            var names = new ClashTestNames();

            Assert.Equal(0, names.Count);
            Assert.Null(names.For("test-1"));
        }

        [Fact]
        public void ARenameIsRememberedAgainstTheTestId()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "  L04 MEP v STR  ");

            Assert.Equal("L04 MEP v STR", names.For("test-1")!.Name);
            Assert.Null(names.For("test-2"));
        }

        /// <summary>The id survives a rename, so renaming twice replaces rather than orphans.</summary>
        [Fact]
        public void RenamingTwiceKeepsOnlyTheLastName()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "First");
            names.Rename("test-1", "Second");

            Assert.Equal(1, names.Count);
            Assert.Equal("Second", names.For("test-1")!.Name);
        }

        [Fact]
        public void ANameThatIsNothingIsNotARename()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "   ");
            names.Rename("test-2", null);

            Assert.Equal(0, names.Count);
        }

        /// <summary>
        /// Cleared and never-set are different answers: one overrides the folder the document
        /// reports, the other defers to it.
        /// </summary>
        [Fact]
        public void TakingATestToTheRootIsNotTheSameAsSayingNothing()
        {
            var names = new ClashTestNames();

            names.Refile("test-1", "Level 04");
            names.Refile("test-2", string.Empty);

            Assert.Equal("Level 04", names.For("test-1")!.Folder);
            Assert.False(names.For("test-1")!.FolderCleared);

            Assert.Null(names.For("test-2")!.Folder);
            Assert.True(names.For("test-2")!.FolderCleared);
        }

        [Fact]
        public void ARenameAndARefileLiveOnTheSameEntry()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "Riser check");
            names.Refile("test-1", "Level 04");

            Assert.Equal(1, names.Count);
            Assert.Equal("Riser check", names.For("test-1")!.Name);
            Assert.Equal("Level 04", names.For("test-1")!.Folder);
        }

        /// <summary>
        /// The document taking the edit is what ends this project's override - otherwise a name
        /// the model now carries would go on being laid over the model's own.
        /// </summary>
        [Fact]
        public void WhatTheDocumentAcceptsIsForgotten()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "Riser check");
            names.Refile("test-1", "Level 04");

            names.Landed("test-1", name: true);

            Assert.Equal(1, names.Count);
            Assert.Null(names.For("test-1")!.Name);
            Assert.Equal("Level 04", names.For("test-1")!.Folder);

            names.Landed("test-1", folder: true);

            Assert.Equal(0, names.Count);
            Assert.Null(names.For("test-1"));
        }

        [Fact]
        public void ItSurvivesTheProjectFile()
        {
            var names = new ClashTestNames();

            names.Rename("test-1", "Riser check");
            names.Refile("test-1", "Level 04");
            names.Refile("test-2", string.Empty);

            var back = ClashTestNames.FromJson(names.ToJson());

            Assert.Equal(2, back.Count);
            Assert.Equal("Riser check", back.For("test-1")!.Name);
            Assert.Equal("Level 04", back.For("test-1")!.Folder);
            Assert.True(back.For("test-2")!.FolderCleared);
        }

        [Fact]
        public void NothingStoredReadsBackAsNothing()
        {
            Assert.Equal(0, ClashTestNames.FromJson(null).Count);
            Assert.Equal(0, new ClashTestNames().ToJson().Keys.Count);
        }
    }
}
