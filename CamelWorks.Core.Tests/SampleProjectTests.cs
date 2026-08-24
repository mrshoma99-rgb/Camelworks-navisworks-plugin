using System;
using System.Linq;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SampleProjectTests
    {
        private const long When = 638_000_000_000_000_000L;

        private const string Document = @"C:\jobs\Riverside.nwd";

        /// <summary>Write the sample to a fake disk and open it the way the product would.</summary>
        private static ProjectStore Opened()
        {
            var fs = new InMemoryFileSystem();

            // Asked where the sidecar belongs rather than assumed. Hardcoding the path put the file
            // somewhere the store did not look and every assertion below came back empty - which
            // said "the sample is wrong" when what was wrong was the test's idea of the layout.
            var (path, _) = ProjectStore.Locate(Document, null);

            Assert.NotNull(path);

            fs.WriteAllText(path!, SampleProject.Write(When));

            return ProjectStore.Open(fs, Document, null);
        }

        [Fact]
        public void It_is_a_project_file_the_product_can_open()
        {
            var store = Opened();

            Assert.Equal(LoadOutcome.Ok, store.Outcome);
            Assert.False(store.UsedBackup);
        }

        [Fact]
        public void The_profile_carries_parties_priorities_and_numbers_that_are_not_the_defaults()
        {
            // Read as the stored section rather than through ProjectProfile, which derives itself
            // from a live document. What matters here is what is in the file.
            var profile = Opened().Section(ProjectStore.ProfileSection);

            Assert.Equal(SampleProject.JobName, profile[ProfileKeys.ProjectName].AsString());

            // Not the built-in numbers, deliberately: a sample whose settings match the defaults
            // teaches nothing about which ones are yours to change.
            Assert.Equal(0.015, profile[ProfileKeys.ClashTolerance].AsDouble(), 6);
            Assert.Equal(0.25, profile[ProfileKeys.ClashProximity].AsDouble(), 6);
            Assert.NotEqual(0.010, profile[ProfileKeys.ClashTolerance].AsDouble());

            var parties = profile[ProfileKeys.Parties].AsString() ?? string.Empty;

            Assert.Contains("(ARC)", parties);
            Assert.Contains("(MEP)", parties);
            Assert.Equal(4, parties.Split('\n').Length);

            Assert.Equal(3, (profile[ProfileKeys.Priorities].AsString() ?? string.Empty).Split('\n').Length);

            // The discipline map: two letters out of a model's file name, which is how every naming
            // standard in the industry actually gets used.
            Assert.Equal("MEP", profile[ProfileKeys.DisciplinePrefix + "ME"].AsString());
            Assert.Equal("MEP", profile[ProfileKeys.DisciplinePrefix + "EL"].AsString());
        }

        [Fact]
        public void The_code_tables_read_back_as_code_tables()
        {
            var store = Opened();
            var tables = CodeTables.FromJson(store.Section(ProjectStore.ProfileSection)["codeTables"]);

            Assert.False(tables.IsEmpty);
            Assert.Equal(new[] { "Discipline", "Zone" }, tables.Names);

            var discipline = tables.Find("Discipline");

            Assert.NotNull(discipline);
            Assert.True(discipline!.Knows("MEP"));
            Assert.Contains("Mechanical", discipline.Find("MEP")!.Description);
        }

        [Fact]
        public void Every_sample_set_is_a_rule_rather_than_a_list_and_survives_the_round_trip()
        {
            var store = Opened();
            var library = SetLibrary.FromJson(store.Section(ProjectStore.SetsSection)["items"]);

            Assert.Equal(4, library.Sets.Count);

            foreach (var set in library.Sets)
            {
                Assert.False(string.IsNullOrWhiteSpace(set.Name));
                Assert.False(string.IsNullOrWhiteSpace(set.Note));

                // A set that compiled to nothing would look fine in the list and find nothing when
                // pressed, which is the worst way for a sample to be wrong.
                var plan = DnfCompiler.Compile(set.Expression);
                Assert.False(plan.MatchesNothing, set.Name + " compiles to nothing");
            }

            var anded = library.Sets.Single(s => s.Name == "Level 01 structure");
            Assert.Contains(" and ", anded.Expression.Describe());

            var negated = library.Sets.Single(s => s.Name.StartsWith("Services", StringComparison.Ordinal));
            Assert.Contains("not", negated.Expression.Describe(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_appearance_layer_targets_a_rule_so_it_means_something_in_a_model_it_has_not_seen()
        {
            var store = Opened();
            var layers = LayerStackJson.Read(store.Section(ProjectStore.LayersSection)["stack"]);

            var layer = Assert.Single(layers);

            Assert.Equal("Structure, greyed back", layer.Name);
            Assert.True(layer.IsEnabled);

            // The whole point: saved element keys would resolve to nothing in anybody else's model
            // and leave a layer that claims to cover things and covers none.
            Assert.NotNull(layer.Target.Expression);
            Assert.True(layer.Target.ReResolves);
            Assert.Empty(layer.Target.Keys);

            Assert.Equal("#9aa0a6", layer.Colour?.ToString());
            Assert.Equal(0.65, layer.Transparency ?? 0, 6);
        }

        [Fact]
        public void The_history_reads_back_as_history_rather_than_as_an_empty_log()
        {
            var store = Opened();

            Assert.Equal(3, store.Activity.Entries.Count);
            Assert.All(store.Activity.Entries, e => Assert.True(e.WhenTicks < When));
            Assert.All(store.Activity.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Summary)));

            Assert.NotNull(store.Activity.Latest(ActivityKind.Export));
        }

        [Fact]
        public void The_same_timestamp_gives_the_same_file_byte_for_byte()
        {
            Assert.Equal(SampleProject.Write(When), SampleProject.Write(When));
            Assert.NotEqual(SampleProject.Write(When), SampleProject.Write(When + 1));
        }

        [Fact]
        public void The_note_beside_it_says_what_it_is_and_that_it_is_made_up()
        {
            var readme = SampleProject.ReadMe();

            Assert.StartsWith(SampleProject.JobName, readme);
            Assert.Contains("made up", readme);
            Assert.Contains("nothing here came from anybody's project", readme);

            // Every claim on the card is in the note, so the two cannot drift.
            foreach (var claim in SampleProject.Explains) Assert.Contains(claim, readme);
        }
    }
}
