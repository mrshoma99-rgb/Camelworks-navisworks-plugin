using System;
using System.Linq;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ModelPairingTests
    {
        private static ModelRecord M(string scope, string name, string path) =>
            new ModelRecord(scope, name, path);

        [Theory]
        [InlineData("STR-MODEL-R03.nwc", "STR-MODEL")]
        [InlineData("STR-MODEL_rev4.nwc", "STR-MODEL")]
        [InlineData("STR-MODEL rev 12.nwc", "STR-MODEL")]
        [InlineData("STR-MODEL-v2.nwd", "STR-MODEL")]
        [InlineData("ABC-XYZ-ZZ-XX-M3-S-0001-S3-P04.nwc", "ABC-XYZ-ZZ-XX-M3-S-0001")]
        [InlineData("Model (2).nwc", "MODEL")]
        [InlineData("Model - Copy.nwc", "MODEL")]
        [InlineData("Model-2024-01-15.nwc", "MODEL")]
        [InlineData("Model_20240115.nwc", "MODEL")]
        [InlineData("PlainModel.nwc", "PLAINMODEL")]
        public void Revision_markers_come_off_the_end(string name, string expected) =>
            Assert.Equal(expected, ModelPairing.Stem(name));

        [Fact]
        public void The_same_file_pairs_with_certainty()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Structure", @"C:\job\STR.nwc") },
                new[] { M("b", "Structure", @"c:/JOB/str.nwc") });

            Assert.Single(pairing.Matched);
            Assert.Equal(PairStrength.SamePath, pairing.Matched[0].Strength);
        }

        [Fact]
        public void The_same_name_in_a_new_issue_folder_pairs()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Structure", @"C:\job\issue-C\STR.nwc") },
                new[] { M("b", "Structure", @"C:\job\issue-D\STR.nwc") });

            Assert.Equal(PairStrength.SameName, pairing.Matched[0].Strength);
        }

        [Fact]
        public void A_weekly_re_issue_that_renames_every_file_still_pairs()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Structure", @"C:\job\STR-MODEL-R03.nwc") },
                new[] { M("b", "Structure", @"C:\job\STR-MODEL-R04.nwc") });

            Assert.Equal(PairStrength.SameStem, pairing.Matched[0].Strength);
            Assert.Contains("revision marker", pairing.Matched[0].Reason);
        }

        [Fact]
        public void Two_models_that_reduce_to_the_same_stem_pair_nothing_and_ask()
        {
            // The stem rule is allowed to over-strip precisely because of this: BLOCK-A1 and
            // BLOCK-A2 both reduce to BLOCK, and guessing between them would report a whole
            // building as rebuilt.
            var pairing = ModelPairing.Of(
                new[] { M("a", "Block A", @"C:\job\BLOCK-A1.nwc"), M("b", "Block B", @"C:\job\BLOCK-A2.nwc") },
                new[] { M("c", "Zone 1", @"C:\job\BLOCK-A1-R02.nwc"), M("d", "Zone 2", @"C:\job\BLOCK-A2-R02.nwc") });

            Assert.NotEmpty(pairing.Ambiguities);
            Assert.Empty(pairing.Matched);
        }

        [Fact]
        public void Two_models_competing_for_one_are_not_settled_by_whichever_was_listed_first()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Model", @"C:\one\SAME.nwc"), M("b", "Model", @"C:\two\SAME.nwc") },
                new[] { M("c", "Model", @"C:\three\SAME.nwc") });

            Assert.Empty(pairing.Matched);
            Assert.NotEmpty(pairing.Ambiguities);
        }

        [Fact]
        public void One_model_on_each_side_pairs_even_when_the_names_do_not_match()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Old name", @"C:\job\OLD.nwc") },
                new[] { M("b", "New name", @"C:\job\COMPLETELY-DIFFERENT.nwc") });

            Assert.Equal(PairStrength.OnlyCandidate, pairing.Matched[0].Strength);
        }

        [Fact]
        public void One_left_over_on_each_side_of_a_bigger_federation_is_not_guessed_at()
        {
            // The dangerous version of the rule above. Structure and services both renamed on a
            // five-model job must not be paired with each other.
            var pairing = ModelPairing.Of(
                new[] { M("a", "Shared", @"C:\job\ARC.nwc"), M("b", "Structure", @"C:\job\STR.nwc") },
                new[] { M("c", "Shared", @"C:\job\ARC.nwc"), M("d", "Services", @"C:\job\MEP.nwc") });

            Assert.Single(pairing.Matched);
            Assert.Single(pairing.GoneModels);
            Assert.Single(pairing.NewModels);
        }

        [Fact]
        public void The_display_name_pairs_when_the_file_name_will_not()
        {
            var pairing = ModelPairing.Of(
                new[] { M("a", "Structure", @"C:\job\a1.nwc"), M("b", "Other", @"C:\job\b1.nwc") },
                new[] { M("c", "Structure", @"C:\job\a2.nwc"), M("d", "Other", @"C:\job\b2.nwc") });

            Assert.Equal(2, pairing.Matched.Count);
            Assert.All(pairing.Matched, p => Assert.Equal(PairStrength.SameDisplayName, p.Strength));
        }

        [Fact]
        public void The_current_side_of_a_pairing_can_be_looked_up_by_the_baseline_scope()
        {
            var pairing = ModelPairing.Of(
                new[] { M("aaa", "Structure", @"C:\job\STR.nwc") },
                new[] { M("bbb", "Structure", @"C:\job\STR.nwc") });

            Assert.Equal("bbb", pairing.After("aaa"));
            Assert.Null(pairing.After("nope"));
        }

        [Fact]
        public void Nothing_on_either_side_is_not_an_error()
        {
            var pairing = ModelPairing.Of(null, null);

            Assert.Empty(pairing.Pairs);
            Assert.True(pairing.IsComplete);
        }

        [Fact]
        public void An_empty_stem_falls_back_rather_than_matching_everything()
        {
            // A file called R03.nwc strips to nothing. Returning an empty stem would make it match
            // every other file that also strips to nothing.
            Assert.Equal("R03", ModelPairing.Stem("R03.nwc"));
        }
    }
}
