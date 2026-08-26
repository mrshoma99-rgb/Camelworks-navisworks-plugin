using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The lease as the store uses it.
    ///
    /// ProjectLease itself has been tested since the store went in. What had never been tested -
    /// because nothing outside the tests called it - is the part that matters to a person: that
    /// taking the lease writes a file somebody else can see, that holding it does not stop
    /// anything except saving settings, and that a refused save says what the other person
    /// changed rather than "somebody else is editing".
    /// </summary>
    public class ProjectLeaseStoreTests
    {
        private const string Document = @"C:\Jobs\Tower\Tower.nwf";
        private const string Settings = @"C:\Jobs\Tower\.camelworks\Tower.cwproj";
        private const string Lock = @"C:\Jobs\Tower\.camelworks\Tower.cwlock";

        private static long Now => 638_000_000_000_000_000L;

        private static ProjectStore Open(InMemoryFileSystem fs) =>
            ProjectStore.Open(fs, Document, null);

        [Fact]
        public void Nothing_takes_the_lease_just_by_opening_the_project()
        {
            // Somebody who loads a model to look at it must not be writing lock files into
            // another team's project folder.
            var fs = new InMemoryFileSystem();
            var store = Open(fs);

            Assert.Null(store.Lease);
            Assert.False(fs.Exists(Lock));
            Assert.True(store.MaySaveSettings);
        }

        [Fact]
        public void Taking_the_lease_writes_a_file_beside_the_settings_not_inside_them()
        {
            var fs = new InMemoryFileSystem();
            var store = Open(fs);

            var result = store.TakeLease("anna", Now);

            Assert.Equal(LeaseOutcome.Acquired, result.Outcome);
            Assert.Equal(Lock, store.LeasePath);
            Assert.True(fs.Exists(Lock));
            Assert.False(fs.Exists(Settings));
        }

        [Fact]
        public void Looking_says_who_holds_it_and_writes_nothing()
        {
            // What a screen does when it opens. Claiming here would mean that reading the clash
            // board for two minutes locked a colleague out of settings for ten.
            var fs = new InMemoryFileSystem();
            var store = Open(fs);

            Assert.Equal(LeaseOutcome.Acquired, store.LookAtLease("anna", Now).Outcome);
            Assert.False(fs.Exists(Lock));

            fs.With(Lock, ProjectLease.Write("ben", Now + ProjectLease.DefaultTtlTicks));

            Assert.Equal(LeaseOutcome.HeldByAnother, store.LookAtLease("anna", Now).Outcome);
            Assert.False(store.MaySaveSettings);
        }

        [Fact]
        public void A_lease_somebody_else_holds_stops_the_save_and_says_who_and_for_how_long()
        {
            var fs = new InMemoryFileSystem()
                .With(Lock, ProjectLease.Write("ben", Now + ProjectLease.DefaultTtlTicks));

            var store = Open(fs);
            var result = store.TakeLease("anna", Now);

            Assert.Equal(LeaseOutcome.HeldByAnother, result.Outcome);
            Assert.Equal("ben", result.Holder);
            Assert.False(store.MaySaveSettings);

            store.Section(ProjectStore.ProfileSection).Set("project.author", "anna");

            Assert.False(store.Save());
            Assert.Contains("ben", store.LastSaveProblem!);
            Assert.False(fs.Exists(Settings));
        }

        [Fact]
        public void A_refused_save_names_the_sections_the_other_person_changed()
        {
            // "The file changed" is not something anybody can act on. Which sections is: it says
            // whether to wait, redo the work, or carry on.
            var fs = new InMemoryFileSystem()
                .With(Settings, "{\"schemaVersion\":1,\"clash\":{\"matrix\":\"mine\"}}");

            var store = Open(fs);

            store.Section(ProjectStore.SetsSection).Set("mine", "untouched");

            // Ben edits the matrix and takes the lease while our window is open.
            fs.With(Settings, "{\"schemaVersion\":1,\"clash\":{\"matrix\":\"theirs\"}}");
            fs.With(Lock, ProjectLease.Write("ben", Now + ProjectLease.DefaultTtlTicks));

            store.TakeLease("anna", Now);

            Assert.False(store.Save());
            Assert.Contains("clash", store.LastSaveProblem!);
            Assert.Contains("sets", store.LastSaveProblem!);
        }

        [Fact]
        public void An_expired_lease_is_taken_over_and_the_save_goes_through()
        {
            // A lease that outlives a crashed process must not lock a team out of their own job.
            var fs = new InMemoryFileSystem().With(Lock, ProjectLease.Write("ben", Now - 1));
            var store = Open(fs);

            var result = store.TakeLease("anna", Now);

            Assert.Equal(LeaseOutcome.TakenOverFromStale, result.Outcome);
            Assert.True(store.MaySaveSettings);
            Assert.True(store.Save());
        }

        [Fact]
        public void Taking_it_over_by_force_is_what_the_button_does()
        {
            var fs = new InMemoryFileSystem()
                .With(Lock, ProjectLease.Write("ben", Now + ProjectLease.DefaultTtlTicks));

            var store = Open(fs);

            Assert.Equal(LeaseOutcome.HeldByAnother, store.TakeLease("anna", Now).Outcome);
            Assert.Equal(LeaseOutcome.Acquired, store.TakeLease("anna", Now, force: true).Outcome);
            Assert.True(store.Save());
        }

        [Fact]
        public void Releasing_removes_our_lease_and_leaves_somebody_elses_alone()
        {
            var fs = new InMemoryFileSystem();
            var store = Open(fs);

            store.TakeLease("anna", Now);
            store.ReleaseLease("anna");

            Assert.False(fs.Exists(Lock));
            Assert.Null(store.Lease);

            var other = Open(new InMemoryFileSystem()
                .With(Lock, ProjectLease.Write("ben", Now + ProjectLease.DefaultTtlTicks)));

            other.TakeLease("anna", Now);
            other.ReleaseLease("anna");

            Assert.Equal(LeaseOutcome.HeldByAnother, other.TakeLease("anna", Now).Outcome);
        }

        [Fact]
        public void A_synced_folder_refuses_the_lease_and_still_saves()
        {
            // The failure this guards against is worse than the one it is about: a sync root
            // cannot carry a lease, and treating that as "somebody else has it" would mean
            // CamelWorks saved nothing at all on OneDrive.
            const string synced = @"C:\Users\anna\OneDrive - Contoso\Tower\Tower.nwf";

            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, synced, null);

            Assert.Equal(SubstrateKind.SyncRoot, store.Substrate);
            Assert.Equal(LeaseOutcome.UnsupportedSubstrate, store.TakeLease("anna", Now).Outcome);
            Assert.True(store.MaySaveSettings);
            Assert.True(store.Save());
        }

        [Fact]
        public void A_document_that_was_never_saved_has_nothing_to_contend_over()
        {
            var fs = new InMemoryFileSystem();
            var store = ProjectStore.Open(fs, null, @"C:\Users\anna\AppData\Local\CamelWorks");

            Assert.Equal(LeaseOutcome.Acquired, store.TakeLease("anna", Now).Outcome);
            Assert.True(store.MaySaveSettings);
            Assert.True(store.Save());
        }

        [Fact]
        public void A_corrupt_lock_file_cannot_lock_a_team_out_of_their_own_project()
        {
            // The worst outcome here is not a lost edit, it is a team that cannot save anything
            // until somebody finds and deletes a file they have never heard of.
            var fs = new InMemoryFileSystem().With(Lock, "{not json");
            var store = Open(fs);

            Assert.Equal(LeaseOutcome.Acquired, store.TakeLease("anna", Now).Outcome);
            Assert.True(store.MaySaveSettings);
            Assert.True(store.Save());
        }
    }
}
