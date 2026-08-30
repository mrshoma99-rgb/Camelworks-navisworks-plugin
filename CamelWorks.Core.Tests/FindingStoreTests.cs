using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The findings file, beside the project file.
    ///
    /// It is a separate file so that remembering a textbox does not rewrite three and a half
    /// megabytes of clashes. These check it lands where the job can find it and that a bad read
    /// never destroys what is on disk.
    /// </summary>
    public class FindingStoreTests
    {
        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("MEP.nwc"), Guid.Parse(guid));

        private static Finding One() =>
            Finding.Create(FindingSource.Clash, "MEP vs Structure", "Clash1",
                new[] { Key("11111111-1111-1111-1111-111111111111"),
                        Key("22222222-2222-2222-2222-222222222222") });

        [Fact]
        public void ItSitsBesideTheProjectFileUnderTheSameStem()
        {
            Assert.Equal(@"C:\job\.camelworks\Tower.findings.json",
                         FindingStore.PathFor(@"C:\job\.camelworks\Tower.cwproj"));

            Assert.Null(FindingStore.PathFor(null));
            Assert.Null(FindingStore.PathFor("   "));
        }

        [Fact]
        public void FindingsSurviveBeingClosedAndOpenedAgain()
        {
            var fs = new InMemoryFileSystem();
            var project = ProjectStore.Open(fs, @"C:\job\Tower.nwf", null);

            var store = FindingStore.Open(fs, project);
            var finding = One();
            finding.Status = FindingStatus.Approved;

            store.Findings.Put(finding);

            Assert.True(store.Save());

            // A week later.
            var again = FindingStore.Open(fs, ProjectStore.Open(fs, @"C:\job\Tower.nwf", null));

            Assert.Equal(1, again.Findings.Count);
            Assert.Equal(FindingStatus.Approved, again.Findings.Find(finding.Id)!.Status);
        }

        [Fact]
        public void AJobWithNoFindingsYetOpensEmptyRatherThanFailing()
        {
            var fs = new InMemoryFileSystem();
            var store = FindingStore.Open(fs, ProjectStore.Open(fs, @"C:\job\Tower.nwf", null));

            Assert.True(store.Findings.IsEmpty);
            Assert.Empty(store.Notes);
            Assert.False(store.IsMemoryOnly);
        }

        [Fact]
        public void AnUnsavedDocumentKeepsFindingsInMemoryRatherThanLosingThem()
        {
            var fs = new InMemoryFileSystem();
            var project = ProjectStore.Open(fs, null, null);

            var store = FindingStore.Open(fs, project);

            store.Findings.Put(One());

            Assert.True(store.IsMemoryOnly);
            Assert.Equal(1, store.Findings.Count);

            // Saving says why rather than throwing.
            Assert.False(store.Save());
            Assert.NotNull(store.LastSaveProblem);
        }

        [Fact]
        public void ARubbishFileIsReportedAndLeftAloneRatherThanOverwritten()
        {
            // Somebody's findings are in there. Refusing to read is recoverable; silently
            // truncating is not.
            var fs = new InMemoryFileSystem();
            var project = ProjectStore.Open(fs, @"C:\job\Tower.nwf", null);
            var path = FindingStore.PathFor(project.Path)!;

            fs.WriteAllText(path, "this is not json");

            var store = FindingStore.Open(fs, project);

            Assert.True(store.Findings.IsEmpty);
            Assert.NotEmpty(store.Notes);
            Assert.Equal("this is not json", fs.ReadAllText(path));
        }

        [Fact]
        public void ThePathIsNamedForTheModelSoTwoJobsInOneFolderDoNotShare()
        {
            var fs = new InMemoryFileSystem();

            var one = FindingStore.Open(fs, ProjectStore.Open(fs, @"C:\job\Tower.nwf", null));
            var two = FindingStore.Open(fs, ProjectStore.Open(fs, @"C:\job\Podium.nwf", null));

            Assert.NotEqual(one.Path, two.Path);

            one.Findings.Put(One());
            one.Save();

            Assert.True(FindingStore.Open(fs, ProjectStore.Open(fs, @"C:\job\Podium.nwf", null))
                                    .Findings.IsEmpty);
        }
    }
}
