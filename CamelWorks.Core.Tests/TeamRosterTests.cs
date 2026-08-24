using System;
using System.Linq;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class TeamRosterTests
    {
        private static TeamRoster Team()
        {
            var roster = new TeamRoster();

            roster.Add("Jo Green", "Mechanical", "jo@mech.example");
            roster.Add("Sam Patel", "Mechanical", "sam@mech.example");
            roster.Add("Alex Reid", "Structural", "alex@struct.example");
            roster.Add("Chris Doyle", "Structural");
            roster.Add("Pat Moore");

            return roster;
        }

        // -----------------------------------------------------------------------------------
        // Who is on the job
        // -----------------------------------------------------------------------------------

        [Fact]
        public void An_empty_roster_changes_nothing_and_says_so()
        {
            var roster = new TeamRoster();

            Assert.True(roster.IsEmpty);
            Assert.Empty(roster.Parties);
            Assert.Contains("whatever somebody types", roster.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_organisations_come_out_biggest_first_because_that_is_what_a_picker_wants()
        {
            Assert.Equal(new[] { "Mechanical", "Structural" }, Team().Parties);
        }

        [Fact]
        public void A_name_already_on_the_roster_is_an_edit_rather_than_a_second_person()
        {
            // Two rows with one name would make every assignment to that name ambiguous, since the
            // name is the identity.
            var roster = Team();

            roster.Add("jo green", email: "jo.green@mech.example");

            Assert.Equal(5, roster.Members.Count);

            var jo = roster.Find("Jo Green")!;

            Assert.Equal("jo.green@mech.example", jo.Email);
            Assert.Equal("Mechanical", jo.Party);
        }

        [Fact]
        public void Somebody_is_found_by_name_by_id_or_by_address()
        {
            var roster = Team();

            Assert.Equal("Jo Green", roster.Find("Jo Green")!.Name);
            Assert.Equal("Jo Green", roster.Find("jo green")!.Name);
            Assert.Equal("Jo Green", roster.Find("jo@mech.example")!.Name);

            Assert.Null(roster.Find("Nobody"));
            Assert.Null(roster.Find(null));
            Assert.Null(roster.Find("   "));
        }

        [Fact]
        public void An_organisation_lists_its_own_people_and_the_unattached_are_their_own_group()
        {
            var roster = Team();

            Assert.Equal(new[] { "Jo Green", "Sam Patel" },
                         roster.In("mechanical").Select(m => m.Name));

            Assert.Equal(new[] { "Pat Moore" }, roster.In(null).Select(m => m.Name));
            Assert.Empty(roster.In("Nobody Ltd"));
        }

        [Fact]
        public void Somebody_off_the_job_stays_on_the_roster_and_out_of_everything_else()
        {
            var roster = Team();

            roster.Find("Chris Doyle")!.IsActive = false;

            Assert.Equal(5, roster.Members.Count);
            Assert.Equal(4, roster.Active.Count);
            Assert.Equal(new[] { "Alex Reid" }, roster.In("Structural").Select(m => m.Name));
            Assert.Contains("1 no longer on the job", roster.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_readout_names_the_people_a_BCF_file_cannot_reach()
        {
            var line = Team().Explain();

            Assert.Contains("5 people across 2 organisations", line, StringComparison.Ordinal);
            Assert.Contains("2 with no address", line, StringComparison.Ordinal);
        }

        [Fact]
        public void Taking_somebody_off_says_whether_there_was_anybody_to_take()
        {
            var roster = Team();

            Assert.True(roster.Remove("Pat Moore"));
            Assert.False(roster.Remove("Pat Moore"));
            Assert.False(roster.Remove(null));
            Assert.Equal(4, roster.Members.Count);
        }

        // -----------------------------------------------------------------------------------
        // Who is on what
        // -----------------------------------------------------------------------------------

        [Fact]
        public void More_than_one_person_can_be_on_a_group()
        {
            var book = new AssignmentBook();

            book.Assign("level-03-ducts", new[] { "jo green", "sam patel" });

            Assert.Equal(new[] { "jo green", "sam patel" }, book.For("level-03-ducts"));
            Assert.Equal(1, book.Count);
            Assert.Empty(book.For("something-else"));
            Assert.Empty(book.For(null));
        }

        [Fact]
        public void Assigning_replaces_rather_than_adds_because_the_control_is_a_list_of_ticks()
        {
            var book = new AssignmentBook();

            book.Assign("g", new[] { "jo green", "sam patel" });
            book.Assign("g", new[] { "sam patel" });

            Assert.Equal(new[] { "sam patel" }, book.For("g"));
        }

        [Fact]
        public void Unticking_everybody_takes_the_group_out_of_the_book_entirely()
        {
            var book = new AssignmentBook();

            book.Assign("g", new[] { "jo green" });
            book.Assign("g", Array.Empty<string>());

            Assert.Equal(0, book.Count);
            Assert.Empty(book.For("g"));
        }

        [Fact]
        public void The_same_person_twice_is_the_same_person_once()
        {
            var book = new AssignmentBook();

            book.Assign("g", new[] { "jo green", "Jo Green", " jo green " });

            Assert.Equal(new[] { "jo green" }, book.For("g"));
        }

        [Fact]
        public void What_somebody_is_carrying_is_countable()
        {
            var book = new AssignmentBook();

            book.Assign("a", new[] { "jo green", "sam patel" });
            book.Assign("b", new[] { "jo green" });
            book.Assign("c", new[] { "alex reid" });

            Assert.Equal(2, book.Load("jo green"));
            Assert.Equal(1, book.Load("Sam Patel"));
            Assert.Equal(0, book.Load("pat moore"));
            Assert.Equal(0, book.Load(null));
        }

        // -----------------------------------------------------------------------------------
        // Putting the two together
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_suggestion_spreads_the_work_rather_than_piling_it_on_the_first_name()
        {
            var roster = Team();
            var book = new AssignmentBook();

            book.Assign("a", new[] { "jo green" });
            book.Assign("b", new[] { "jo green" });

            Assert.Equal(new[] { "Sam Patel", "Jo Green" },
                         roster.Suggest("Mechanical", book).Select(m => m.Name));

            // With nothing carried yet it is simply the organisation, in roster order.
            Assert.Equal(new[] { "Jo Green", "Sam Patel" },
                         roster.Suggest("Mechanical").Select(m => m.Name));

            Assert.Empty(roster.Suggest("Nobody Ltd", book));
        }

        [Fact]
        public void Who_is_on_a_group_comes_back_as_people_rather_than_as_names()
        {
            var roster = Team();
            var book = new AssignmentBook();

            book.Assign("g", new[] { "jo green", "alex reid" });

            Assert.Equal(new[] { "Jo Green", "Alex Reid" }, roster.On(book, "g").Select(m => m.Name));

            Assert.Equal(new[] { "jo@mech.example", "alex@struct.example" },
                         roster.Addresses(book, "g"));

            Assert.Empty(roster.On(null, "g"));
            Assert.Empty(roster.Addresses(book, "nothing"));
        }

        [Fact]
        public void Somebody_who_left_the_job_shows_as_a_stranger_rather_than_being_swept_up()
        {
            // Their work did not leave with them, and the row saying so is how it gets reassigned
            // instead of forgotten.
            var roster = Team();
            var book = new AssignmentBook();

            book.Assign("g", new[] { "jo green", "someone who left" });

            Assert.Equal(new[] { "Jo Green" }, roster.On(book, "g").Select(m => m.Name));
            Assert.Equal(new[] { "someone who left" }, roster.Strangers(book, "g"));
            Assert.Empty(roster.Strangers(book, "nothing"));
            Assert.Empty(roster.Strangers(null, "g"));
        }

        [Fact]
        public void An_address_only_reaches_somebody_who_has_one()
        {
            var roster = Team();
            var book = new AssignmentBook();

            book.Assign("g", new[] { "chris doyle" });

            Assert.Single(roster.On(book, "g"));
            Assert.Empty(roster.Addresses(book, "g"));
        }

        // -----------------------------------------------------------------------------------
        // The project file
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_roster_survives_the_project_file()
        {
            var roster = Team();

            roster.Find("Pat Moore")!.Note = "On site Tuesdays.";
            roster.Find("Chris Doyle")!.IsActive = false;

            var read = TeamRoster.FromJson(JsonReader.Parse(roster.ToJson().ToString()));

            Assert.Equal(5, read.Members.Count);
            Assert.Equal(4, read.Active.Count);
            Assert.Equal("On site Tuesdays.", read.Find("Pat Moore")!.Note);
            Assert.Equal("jo@mech.example", read.Find("Jo Green")!.Email);
            Assert.False(read.Find("Chris Doyle")!.IsActive);
            Assert.Equal(new[] { "Mechanical", "Structural" }, read.Parties);
        }

        [Fact]
        public void A_file_naming_one_person_twice_reads_as_one_person()
        {
            var read = TeamRoster.FromJson(JsonReader.Parse(
                "[{\"name\":\"Jo Green\",\"party\":\"Mechanical\"},"
                + "{\"name\":\"jo green\",\"email\":\"jo@mech.example\"},"
                + "{\"party\":\"Nobody\"}]"));

            var jo = Assert.Single(read.Members);

            Assert.Equal("Jo Green", jo.Name);
            Assert.Equal("Mechanical", jo.Party);
            Assert.Equal("jo@mech.example", jo.Email);
        }

        [Fact]
        public void A_book_survives_the_project_file()
        {
            var book = new AssignmentBook();

            book.Assign("level 03 / ducts", new[] { "jo green", "sam patel" });
            book.Assign("level 04 / beams", new[] { "alex reid" });

            var read = AssignmentBook.FromJson(JsonReader.Parse(book.ToJson().ToString()));

            Assert.Equal(2, read.Count);
            Assert.Equal(new[] { "jo green", "sam patel" }, read.For("level 03 / ducts"));
            Assert.Equal(2, read.Load("jo green") + read.Load("alex reid"));
        }

        [Fact]
        public void Nothing_at_all_reads_as_an_empty_roster_and_an_empty_book()
        {
            Assert.True(TeamRoster.FromJson(null).IsEmpty);
            Assert.Equal(0, AssignmentBook.FromJson(null).Count);
        }

        [Fact]
        public void Somebody_with_no_name_is_not_somebody()
        {
            Assert.Throws<ArgumentException>(() => new TeamMember("   "));
            Assert.Null(TeamMember.FromJson(JsonReader.Parse("{\"party\":\"Mechanical\"}")));
            Assert.Null(TeamMember.FromJson(null));
        }
    }
}
