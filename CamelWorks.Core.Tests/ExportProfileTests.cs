using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ExportProfileTests
    {
        private static ClashItem Item(string name, string test, FindingStatus status, double x)
        {
            var item = ClashFixture.Item(name, x: x, test: test);
            item.Status = status;
            return item;
        }

        private static ClashGroup Group(string name, string? party, params ClashItem[] members)
        {
            var group = new ClashGroup(name.ToLowerInvariant(), name) { AssignedTo = party };
            foreach (var member in members) group.Items.Add(member);
            return group;
        }

        private static ExportProfile Everything(params string[] tests)
        {
            var profile = new ExportProfile();
            foreach (var test in tests) profile.Add(test);
            return profile;
        }

        // -----------------------------------------------------------------------------------
        // Matching a profile to a document
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_document_that_is_what_the_profile_remembers_is_quiet()
        {
            var profile = Everything("Hard", "Clearance");

            var match = profile.Match(new[] { new ClashTestRef("Hard"), new ClashTestRef("Clearance") });

            Assert.True(match.IsQuiet);
            Assert.Equal(2, match.Present.Count);
            Assert.Contains("where the profile left them", match.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_renamed_test_is_still_the_test_the_profile_chose()
        {
            // The failure this exists to avoid: a test somebody renamed silently dropping out of
            // every export from then on.
            var profile = new ExportProfile();
            profile.Add("MEP v STR", "test-1").Chosen = true;

            var match = profile.Match(new[] { new ClashTestRef("MEP v Structure", "test-1") });

            Assert.Single(match.Present);
            Assert.Empty(match.Missing);
            Assert.Empty(match.Arrived);

            var renamed = Assert.Single(match.Renamed);

            Assert.Equal("MEP v STR", renamed.Was);
            Assert.Equal("MEP v Structure", renamed.Now);

            // Adopted, so the next match is quiet rather than reporting the same rename weekly.
            Assert.Equal("MEP v Structure", profile.Tests[0].Name);
            Assert.True(profile.Match(new[] { new ClashTestRef("MEP v Structure", "test-1") }).IsQuiet);
        }

        [Fact]
        public void An_id_that_matches_beats_a_name_that_matches()
        {
            var profile = new ExportProfile();
            profile.Add("Hard", "test-1");

            var match = profile.Match(new[]
            {
                new ClashTestRef("Hard", "test-9"),      // same name, a different test
                new ClashTestRef("Renamed", "test-1"),   // the one the profile actually chose
            });

            Assert.Equal("Renamed", Assert.Single(match.Present).Name);
            Assert.Equal("Hard", Assert.Single(match.Arrived).Name);
        }

        [Fact]
        public void Without_ids_the_name_is_all_there_is()
        {
            var profile = Everything("Hard");

            var match = profile.Match(new[] { new ClashTestRef("hard") });

            Assert.Single(match.Present);
            Assert.True(match.IsQuiet);
        }

        [Fact]
        public void A_test_the_profile_never_saw_arrives_chosen()
        {
            // A new test is the one thing a baseline must not miss - the results nobody has looked
            // at yet.
            var profile = Everything("Hard");

            var match = profile.Match(new[] { new ClashTestRef("Hard"), new ClashTestRef("New level") });

            Assert.Equal("New level", Assert.Single(match.Arrived).Name);
            Assert.Equal(2, profile.Tests.Count);
            Assert.True(profile.Tests.Single(t => t.Name == "New level").Chosen);
            Assert.Contains("1 new", match.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_test_the_document_no_longer_has_is_reported_and_kept()
        {
            var profile = Everything("Hard", "Retired");

            var match = profile.Match(new[] { new ClashTestRef("Hard") });

            Assert.Equal("Retired", Assert.Single(match.Missing).Name);

            // Kept, because a test missing this week is usually a document somebody opened without
            // running it, not a test deleted for good.
            Assert.Equal(2, profile.Tests.Count);
            Assert.Contains("1 gone", match.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_profile_against_an_empty_document_says_so()
        {
            var match = new ExportProfile().Match(null);

            Assert.True(match.IsQuiet);
            Assert.Contains("no tests in it yet", match.Explain(), StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------------------
        // Choosing what goes in
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Only_the_chosen_tests_go_in_and_the_rest_are_counted()
        {
            var profile = Everything("Hard", "Clearance");
            profile.Tests.Single(t => t.Name == "Clearance").Chosen = false;

            var picked = profile.Choose(new[]
            {
                Item("a", "Hard", FindingStatus.New, 0),
                Item("b", "Clearance", FindingStatus.New, 4),
                Item("c", "Hard", FindingStatus.New, 8),
            });

            Assert.Equal(2, picked.Items.Count);
            Assert.Equal(1, picked.ByTest);
            Assert.Contains("1 from tests not chosen", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_status_filter_leaves_the_rest_behind_and_says_how_many()
        {
            var profile = Everything("Hard");
            profile.Statuses.Add(FindingStatus.New);
            profile.Statuses.Add(FindingStatus.Active);

            var picked = profile.Choose(new[]
            {
                Item("a", "Hard", FindingStatus.New, 0),
                Item("b", "Hard", FindingStatus.Resolved, 4),
                Item("c", "Hard", FindingStatus.Approved, 8),
            });

            Assert.Single(picked.Items);
            Assert.Equal(2, picked.ByStatus);
            Assert.Contains("2 at a status left out", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void No_status_filter_means_every_status()
        {
            var picked = Everything("Hard").Choose(new[]
            {
                Item("a", "Hard", FindingStatus.New, 0),
                Item("b", "Hard", FindingStatus.Resolved, 4),
            });

            Assert.Equal(2, picked.Items.Count);
            Assert.Equal(0, picked.Left);
            Assert.Contains("the whole board", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_party_filter_sends_a_sub_contractor_their_own_work()
        {
            var mine = Item("a", "Hard", FindingStatus.New, 0);
            var theirs = Item("b", "Hard", FindingStatus.New, 4);

            var profile = Everything("Hard");
            profile.Parties.Add("Mechanical");

            var picked = profile.Choose(
                new[] { mine, theirs },
                new[] { Group("Ducts", "Mechanical", mine), Group("Beams", "Structural", theirs) });

            Assert.Same(mine, Assert.Single(picked.Items));
            Assert.Equal(1, picked.ByParty);
            Assert.Contains("1 assigned elsewhere", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_unassigned_result_belongs_to_nobody_so_it_goes_to_everybody()
        {
            // Left out of every party's file is how a result never reaches anyone.
            var assigned = Item("a", "Hard", FindingStatus.New, 0);
            var loose = Item("b", "Hard", FindingStatus.New, 4);

            var profile = Everything("Hard");
            profile.Parties.Add("Mechanical");

            var picked = profile.Choose(new[] { assigned, loose },
                                        new[] { Group("Ducts", "Mechanical", assigned) });

            Assert.Equal(2, picked.Items.Count);
            Assert.Equal(0, picked.ByParty);
        }

        [Fact]
        public void The_folders_come_out_trimmed_to_what_is_going_and_the_board_is_left_alone()
        {
            var stays = Item("a", "Hard", FindingStatus.New, 0);
            var goes = Item("b", "Hard", FindingStatus.Resolved, 4);

            var board = Group("Ducts v beams", "Mechanical", stays, goes);

            var profile = Everything("Hard");
            profile.Statuses.Add(FindingStatus.New);

            var picked = profile.Choose(new[] { stays, goes }, new[] { board });

            var folder = Assert.Single(picked.Groups);

            Assert.Equal("Ducts v beams", folder.Name);
            Assert.Equal("Mechanical", folder.AssignedTo);
            Assert.Same(stays, Assert.Single(folder.Items));

            // Writing a file must not change what the board shows.
            Assert.Equal(2, board.Items.Count);
        }

        [Fact]
        public void A_folder_with_nothing_left_in_it_is_not_written_at_all()
        {
            var goes = Item("b", "Hard", FindingStatus.Resolved, 4);

            var profile = Everything("Hard");
            profile.Statuses.Add(FindingStatus.New);

            var picked = profile.Choose(new[] { goes }, new[] { Group("Empty", null, goes) });

            Assert.Empty(picked.Groups);
            Assert.Empty(picked.Items);
            Assert.False(picked.IsUsable);
            Assert.Contains("Nothing left after the filters", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_profile_that_has_never_met_a_document_exports_everything()
        {
            // Zero setup: the first press must produce a file, not a refusal that says to go and
            // pick some tests first.
            var picked = new ExportProfile().Choose(new[]
            {
                Item("a", "Hard", FindingStatus.New, 0),
                Item("b", "Clearance", FindingStatus.New, 4),
            });

            Assert.Equal(2, picked.Items.Count);
            Assert.Equal(0, picked.Left);
        }

        [Fact]
        public void An_empty_board_says_it_is_empty_rather_than_blaming_the_filters()
        {
            var picked = Everything("Hard").Choose(null);

            Assert.False(picked.IsUsable);
            Assert.Equal("Nothing on the board to export.", picked.Explain());
        }

        // -----------------------------------------------------------------------------------
        // The last-exported column
        // -----------------------------------------------------------------------------------

        [Fact]
        public void What_went_out_is_stamped_and_what_did_not_is_left_alone()
        {
            var profile = Everything("Hard", "Clearance");

            Assert.All(profile.Tests, t => Assert.True(t.NeverExported));
            Assert.Equal("never", ExportProfile.When(profile.Tests[0]));

            var stamped = profile.Stamp(new[] { "Hard" }, 637_000_000_000_000_000L);

            Assert.Equal(1, stamped);
            Assert.False(profile.Tests[0].NeverExported);
            Assert.True(profile.Tests[1].NeverExported);
            Assert.NotEqual("never", ExportProfile.When(profile.Tests[0]));
        }

        [Fact]
        public void A_stamp_with_no_time_and_no_names_changes_nothing()
        {
            var profile = Everything("Hard");

            Assert.Equal(0, profile.Stamp(new[] { "Hard" }, 0));
            Assert.Equal(0, profile.Stamp(null, 637_000_000_000_000_000L));
            Assert.True(profile.Tests[0].NeverExported);
            Assert.Equal("never", ExportProfile.When(null));
        }

        // -----------------------------------------------------------------------------------
        // -----------------------------------------------------------------------------------
        // The filter, and one file per recipient
        // -----------------------------------------------------------------------------------

        [Fact]
        public void The_profiles_filter_narrows_what_the_tests_and_statuses_let_through()
        {
            var mine = Item("a", "Hard", FindingStatus.New, 1);
            var high = Item("b", "Hard", FindingStatus.New, 2);

            mine.Z = 1;
            high.Z = 12;

            var profile = Everything("Hard");
            var band = profile.Pick.Add("elevation", ClashCompare.AtMost);

            band.High = 5;

            var picked = profile.Choose(new[] { mine, high });

            Assert.Equal(1, Assert.Single(picked.Items).Z);
            Assert.Equal(1, picked.ByFilter);
            Assert.Contains("outside the filter", picked.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_run_splits_into_one_pack_per_recipient()
        {
            // One press, six files. Building that by editing the filter six times is how the
            // wrong pack reaches the wrong trade.
            var mech = Item("a", "Hard", FindingStatus.New, 1);
            var also = Item("b", "Hard", FindingStatus.New, 2);
            var structure = Item("c", "Hard", FindingStatus.New, 3);
            var nobody = Item("d", "Hard", FindingStatus.New, 4);

            mech.AssignedTo = "Mechanical";
            also.AssignedTo = "Mechanical";
            structure.AssignedTo = "Structure";

            var profile = Everything("Hard");

            profile.SplitBy = "assignee";

            var rounds = profile.Rounds(new[] { mech, also, structure, nobody });

            Assert.Equal(3, rounds.Count);
            Assert.Equal(new[] { "Mechanical", "Structure", ExportProfile.Nobody },
                         rounds.Select(r => r.Recipient).OrderBy(r => r == ExportProfile.Nobody)
                               .ThenBy(r => r, StringComparer.OrdinalIgnoreCase));

            var mechanical = rounds.Single(r => r.Recipient == "Mechanical");

            Assert.Equal(2, mechanical.Selection.Items.Count);
        }

        [Fact]
        public void A_folder_member_two_recipients_both_claim_reaches_both_packs()
        {
            // Two tests can report the same conflict in the same cell, and both results can be on
            // the board and in one folder. A split that kept only the last recipient for a key
            // dropped the other one's member out of that recipient's group.
            var mine = Item("a", "Hard", FindingStatus.New, 1);
            var theirs = Item("a", "Clearance", FindingStatus.New, 1);

            Assert.Equal(mine.Key, theirs.Key);

            mine.AssignedTo = "Mechanical";
            theirs.AssignedTo = "Structure";

            var folder = Group("Corridor C", null, mine, theirs);

            var profile = Everything("Hard", "Clearance");

            profile.SplitBy = "assignee";
            profile.KeepGroups = true;

            var rounds = profile.Rounds(new[] { mine, theirs }, new[] { folder });

            Assert.Equal(2, rounds.Count);

            foreach (var round in rounds)
            {
                var group = Assert.Single(round.Selection.Groups);

                Assert.Equal(2, group.Items.Count);
            }
        }

        [Fact]
        public void Clashes_nobody_was_assigned_get_their_own_pack_rather_than_none()
        {
            var loose = Item("a", "Hard", FindingStatus.New, 1);

            var profile = Everything("Hard");

            profile.SplitBy = "assignee";

            var round = Assert.Single(profile.Rounds(new[] { loose }));

            Assert.Equal(ExportProfile.Nobody, round.Recipient);
            Assert.Equal("Unassigned", round.FileStem());
        }

        [Fact]
        public void With_nothing_to_split_by_there_is_one_pack()
        {
            var profile = Everything("Hard");

            var round = Assert.Single(profile.Rounds(new[] { Item("a", "Hard", FindingStatus.New, 1) }));

            Assert.Equal(profile.Name, round.Recipient);
        }


        [Fact]
        public void A_split_the_check_has_no_sides_for_falls_back_to_the_assignee()
        {
            // Clearance has an obstruction and a surface, not two trades, so a discipline split
            // over it names every pack "? v ?" - one pack addressed to nobody. The assignee is the
            // one recipient every check has, and the round says which split it used.
            var mine = Item("a", "Hard", FindingStatus.New, 1);
            var theirs = Item("b", "Hard", FindingStatus.New, 2);

            mine.DisciplineA = null;
            mine.DisciplineB = null;
            theirs.DisciplineA = null;
            theirs.DisciplineB = null;

            mine.AssignedTo = "Mechanical";
            theirs.AssignedTo = "Structure";

            var profile = Everything("Hard");

            profile.SplitBy = "disciplines";

            var rounds = profile.Rounds(new[] { mine, theirs });

            Assert.Equal(2, rounds.Count);
            Assert.Equal(new[] { "Mechanical", "Structure" },
                         rounds.Select(r => r.Recipient).OrderBy(r => r, StringComparer.Ordinal));

            Assert.All(rounds, round =>
            {
                Assert.Equal("Assigned to", round.SplitBy);
                Assert.Equal("Disciplines", round.Asked);
            });
        }

        [Fact]
        public void A_split_nothing_answers_and_nobody_is_assigned_is_one_pack_that_says_so()
        {
            // The branch the tab draws a sentence from. SplitBy naming nothing left that sentence
            // reading "so the split fell back to ."
            var loose = Item("a", "Hard", FindingStatus.New, 1);

            loose.DisciplineA = null;
            loose.DisciplineB = null;

            var profile = Everything("Hard");

            profile.SplitBy = "disciplines";

            var round = Assert.Single(profile.Rounds(new[] { loose }));

            Assert.Equal(profile.Name, round.Recipient);
            Assert.Equal(ExportProfile.OnePack, round.SplitBy);
            Assert.Equal("Disciplines", round.Asked);
        }

        [Fact]
        public void A_split_the_check_does_have_sides_for_is_the_split_that_is_used()
        {
            // The fallback is not a default. A clash has two trades, so a discipline split splits
            // by discipline and nothing is said about a fallback.
            var mine = Item("a", "Hard", FindingStatus.New, 1);
            var theirs = Item("b", "Hard", FindingStatus.New, 2);

            mine.DisciplineA = "Mechanical";
            mine.DisciplineB = "Structural";
            theirs.DisciplineA = "Electrical";
            theirs.DisciplineB = "Structural";

            mine.AssignedTo = "Ada";
            theirs.AssignedTo = "Ada";

            var profile = Everything("Hard");

            profile.SplitBy = "disciplines";

            var rounds = profile.Rounds(new[] { mine, theirs });

            Assert.Equal(2, rounds.Count);
            Assert.All(rounds, round => Assert.Null(round.Asked));
            Assert.All(rounds, round => Assert.Equal("Disciplines", round.SplitBy));
        }

        [Fact]
        public void A_recipients_name_becomes_a_filename_a_file_system_will_take()
        {
            var awkward = Item("a", "Hard", FindingStatus.New, 1);

            awkward.AssignedTo = "M&E / Sub-contractor";

            var profile = Everything("Hard");

            profile.SplitBy = "assignee";

            Assert.Equal("M E Sub-contractor",
                         Assert.Single(profile.Rounds(new[] { awkward })).FileStem());
        }

        // Living in the project file
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_profile_survives_the_project_file()
        {
            var profile = new ExportProfile
            {
                Name = "Weekly to Mechanical",
                KeepGroups = false,
            };

            profile.Add("Hard", "test-1").Chosen = false;
            profile.Add("Clearance");
            profile.Statuses.Add(FindingStatus.New);
            profile.Statuses.Add(FindingStatus.Active);
            profile.Parties.Add("Mechanical");
            profile.Stamp(new[] { "Clearance" }, 637_000_000_000_000_000L);

            var read = ExportProfile.FromJson(JsonReader.Parse(profile.ToJson().ToString()));

            Assert.Equal("Weekly to Mechanical", read.Name);
            Assert.False(read.KeepGroups);

            Assert.Equal(2, read.Tests.Count);
            Assert.False(read.Tests[0].Chosen);
            Assert.Equal("test-1", read.Tests[0].Id);
            Assert.True(read.Tests[1].Chosen);
            Assert.Equal(637_000_000_000_000_000L, read.Tests[1].LastExportedTicks);

            Assert.Equal(new[] { FindingStatus.New, FindingStatus.Active },
                         read.Statuses.OrderBy(s => (int)s));
            Assert.Equal(new[] { "Mechanical" }, read.Parties);
        }

        [Fact]
        public void An_older_or_hand_edited_project_file_reads_as_defaults()
        {
            var read = ExportProfile.FromJson(JsonReader.Parse(
                "{\"tests\":[{\"name\":\"Hard\"},{\"chosen\":true},{\"name\":\"  \"}],"
                + "\"statuses\":[\"Reviewed\",\"Nonsense\"]}"));

            Assert.Equal("Everything", read.Name);
            Assert.True(read.KeepGroups);

            // A test with no name is not a test; a status nobody recognises is dropped rather than
            // defaulted, because a filter that silently grows is worse than one that lost a line.
            Assert.Equal("Hard", Assert.Single(read.Tests).Name);
            Assert.Equal(FindingStatus.Reviewed, Assert.Single(read.Statuses));
        }

        [Fact]
        public void Nothing_at_all_reads_as_a_fresh_profile()
        {
            var read = ExportProfile.FromJson(null);

            Assert.Empty(read.Tests);
            Assert.Empty(read.Statuses);
            Assert.Empty(read.Parties);
            Assert.Equal("Everything", read.Name);
        }

        // -----------------------------------------------------------------------------------
        // End to end
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_profile_picks_the_board_apart_and_the_file_holds_exactly_that()
        {
            var mine = Item("a", "Hard", FindingStatus.New, 0);
            var stale = Item("b", "Hard", FindingStatus.Resolved, 4);
            var elsewhere = Item("c", "Clearance", FindingStatus.New, 8);

            var profile = Everything("Hard", "Clearance");
            profile.Tests.Single(t => t.Name == "Clearance").Chosen = false;
            profile.Statuses.Add(FindingStatus.New);

            var picked = profile.Choose(new[] { mine, stale, elsewhere },
                                        new[] { Group("Ducts", "Mechanical", mine, stale) });

            var how = profile.BcfOptions("Federation.nwf");
            var folders = picked.Groups.SelectMany(g => g.Items.Select(i => (i.Key, g.Name)))
                                       .ToDictionary(one => one.Key, one => one.Name);

            var going = picked.Items.Select(ClashFixture.Found).ToList();

            how.GroupOf = finding => folders.TryGetValue(
                ClashKey.Create(finding.Elements[0], finding.Elements[1],
                                finding.X ?? 0, finding.Y ?? 0, finding.Z ?? 0),
                out var named) ? named : null;

            var written = FindingBcf.Write(going, null, how);

            Assert.Equal(1, written.Findings);

            var read = ClashFile.Read(written.Bytes);

            Assert.Equal(mine.Key, Assert.Single(read.Items).Key);
            Assert.Equal("Ducts", read.Items[0].CarriedGroup);
        }
    }
}
