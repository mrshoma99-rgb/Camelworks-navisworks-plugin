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
    /// The one save door: the findings sidecar and the project file, in that order.
    ///
    /// An issue in the project file names finding ids and the findings are in their own file
    /// beside it. There is no cross-file transaction and none is claimed; what is asserted here
    /// is the order and the refusals, because those are what stop an issue naming a finding that
    /// was never written.
    /// </summary>
    public class WorkSaveTests
    {
        private const string Model = @"C:\job\Tower.nwf";

        private static string ProjectPath => ProjectStore.Locate(Model, null).Path!;
        private static string SidecarPath => FindingStore.PathFor(ProjectPath)!;

        private static Finding One() =>
            Finding.Create(FindingSource.Clash, "MEP vs Structure", "Clash1",
                new[]
                {
                    ElementKey.FromInstanceGuid(ElementKey.ScopeOf("MEP.nwc"),
                        Guid.Parse("11111111-1111-1111-1111-111111111111")),
                });

        [Fact]
        public void Both_files_are_written_and_the_sidecar_goes_first()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            findings.Findings.Put(One());
            store.Section(ProjectStore.IssuesSection).Set("register", JsonValue.Array());

            Assert.True(WorkSave.Save(store, findings, out var problem));
            Assert.Null(problem);

            var log = fs.Log.ToList();

            var sidecar = log.IndexOf("replace " + SidecarPath);
            var project = log.IndexOf("replace " + ProjectPath);

            Assert.True(sidecar >= 0 && project >= 0);
            Assert.True(sidecar < project);
        }

        [Fact]
        public void A_read_only_project_file_writes_neither_file_and_says_why()
        {
            // A project file from a newer build. Refused before the sidecar is touched: a
            // rewritten sidecar beside a project file that refused the write is work half done
            // for no gain, since nothing that referenced it landed.
            var fs = new InMemoryFileSystem()
                .With(ProjectPath, "{\"schemaVersion\":999,\"issues\":{}}");

            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            findings.Findings.Put(One());

            Assert.True(store.IsReadOnly);
            Assert.False(WorkSave.Save(store, findings, out var problem));
            Assert.Contains("newer CamelWorks", problem);

            Assert.Equal("{\"schemaVersion\":999,\"issues\":{}}", fs.ReadAllText(ProjectPath));
            Assert.False(fs.Exists(SidecarPath));
        }

        [Fact]
        public void A_lease_somebody_else_holds_refuses_before_the_sidecar_is_written()
        {
            var fs = new InMemoryFileSystem()
                .With(ProjectStore.Locate(Model, null).Path!.Replace(ProjectStore.Extension,
                                                                     ProjectStore.LeaseExtension),
                      ProjectLease.Write("ben", DateTime.UtcNow.Ticks
                                                + ProjectLease.DefaultTtlTicks));

            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            findings.Findings.Put(One());
            store.TakeLease("anna", DateTime.UtcNow.Ticks);

            Assert.False(store.MaySaveSettings);
            Assert.False(WorkSave.Save(store, findings, out var problem));
            Assert.Contains("ben", problem);

            Assert.False(fs.Exists(SidecarPath));
            Assert.False(fs.Exists(ProjectPath));
        }

        [Fact]
        public void A_sidecar_that_will_not_write_leaves_the_project_file_as_it_was()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            // A project file with something in it already, so "unchanged" is a claim with bytes
            // behind it rather than an absent file.
            store.Section(ProjectStore.IssuesSection).Set("register", JsonValue.Array());
            Assert.True(store.Save());

            var before = fs.ReadAllText(ProjectPath);

            findings.Findings.Put(One());
            store.Section(ProjectStore.IssuesSection).Set("register",
                JsonValue.Array(new[] { JsonValue.String("CW-1") }));

            fs.FailOnce = "write " + SidecarPath;

            Assert.False(WorkSave.Save(store, findings, out var problem));
            Assert.Equal(findings.LastSaveProblem, problem);
            Assert.Contains(SidecarPath, problem);

            Assert.Equal(before, fs.ReadAllText(ProjectPath));
            Assert.False(fs.Exists(SidecarPath));
        }

        [Fact]
        public void A_memory_only_register_refuses_rather_than_writing_a_project_file()
        {
            // A project file that would take a write, over a register with nowhere to go. The
            // refusal is what stops the project file recording issues that name findings which
            // are nowhere and will still be nowhere after Navisworks closes.
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, null);

            findings.Findings.Put(One());

            Assert.False(store.IsMemoryOnly);
            Assert.True(findings.IsMemoryOnly);

            Assert.False(WorkSave.Save(store, findings, out var problem));
            Assert.Contains("nowhere to save findings", problem);
            Assert.Empty(fs.AllPaths);
        }

        [Fact]
        public void Nothing_to_save_to_at_all_is_answered_rather_than_thrown()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, null, null);
            var findings = FindingStore.Open(fs, store);

            Assert.False(WorkSave.Save(store, findings, out var problem));
            Assert.NotNull(problem);
            Assert.Empty(fs.AllPaths);
        }

        [Fact]
        public void The_door_refuses_a_missing_store_rather_than_saving_half_of_it()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            Assert.Throws<ArgumentNullException>(() => WorkSave.Save(null!, findings, out _));
            Assert.Throws<ArgumentNullException>(() => WorkSave.Save(store, null!, out _));
        }

        [Fact]
        public void What_was_saved_reads_back_with_the_issue_and_the_finding_it_names()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, Model, null);
            var findings = FindingStore.Open(fs, store);

            var finding = One();

            findings.Findings.Put(finding);

            store.Section(ProjectStore.IssuesSection).Set("register",
                JsonValue.Object().Set("issues", JsonValue.Array(new[]
                {
                    JsonValue.Object()
                        .Set("id", "CW-1")
                        .Set("title", "Riser clash")
                        .Set("findings", JsonValue.Array(new[] { JsonValue.String(finding.Id) })),
                })));

            Assert.True(WorkSave.Save(store, findings, out _));

            // A week later, from disk: the id the issue names is a finding that is there.
            var again = ProjectStore.Open(fs, Model, null);
            var kept = FindingStore.Open(fs, again);

            var named = again.Section(ProjectStore.IssuesSection)["register"]["issues"]
                .Items.First()["findings"].Items.First().AsString();

            Assert.NotNull(kept.Findings.Find(named));
        }
    }
}
