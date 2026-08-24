using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ResolutionTests
    {
        [Fact]
        public void A_group_nobody_has_instructed_says_so_rather_than_leaving_a_gap()
        {
            // A blank line under a group image is something the reader has to interpret. "Resolution
            // not stated" is a fact about the coordination, and usually an actionable one.
            Assert.False(Resolution.None.IsStated);
            Assert.Equal("Resolution not stated", Resolution.None.Describe());
            Assert.Null(Resolution.None.Direction);
        }

        [Fact]
        public void A_move_reads_as_an_instruction_to_a_person()
        {
            var instruction = new Resolution(ResolutionAction.Move, "the 400 duct",
                                             ResolutionAxis.ZMinus, 0.15);

            Assert.Equal("Move the 400 duct 150 mm down", instruction.Describe(Measure.Millimetres));
        }

        [Fact]
        public void The_length_is_written_in_the_project_own_units()
        {
            var instruction = new Resolution(ResolutionAction.Move, "the 400 duct",
                                             ResolutionAxis.ZMinus, 0.15);

            Assert.Equal("Move the 400 duct 0.150 m down", instruction.Describe(Measure.Metric));
            Assert.Equal("Move the 400 duct 0' 5 7/8\" down", instruction.Describe(Measure.Imperial));
        }

        [Theory]
        [InlineData(ResolutionAxis.XPlus, "in +X")]
        [InlineData(ResolutionAxis.XMinus, "in -X")]
        [InlineData(ResolutionAxis.YPlus, "in +Y")]
        [InlineData(ResolutionAxis.YMinus, "in -Y")]
        [InlineData(ResolutionAxis.ZPlus, "up")]
        [InlineData(ResolutionAxis.ZMinus, "down")]
        public void Z_reads_as_up_and_down_because_that_is_what_it_is(ResolutionAxis axis, string word)
        {
            var instruction = new Resolution(ResolutionAction.Move, "it", axis, 0.1);

            Assert.EndsWith(word, instruction.Describe(Measure.Millimetres));
        }

        [Fact]
        public void The_other_four_verbs_each_read_as_a_sentence()
        {
            Assert.Equal("Resize the 400 duct by 100 mm",
                new Resolution(ResolutionAction.Resize, "the 400 duct", amount: 0.1)
                    .Describe(Measure.Millimetres));

            Assert.Equal("Reroute the sprinkler main",
                new Resolution(ResolutionAction.Reroute, "the sprinkler main").Describe());

            Assert.Equal("Hold the riser - no change until this is decided",
                new Resolution(ResolutionAction.Hold, "the riser").Describe());

            Assert.Equal("the beam stays - the other trade moves",
                new Resolution(ResolutionAction.OtherTradeMoves, "the beam").Describe());
        }

        [Fact]
        public void An_instruction_about_nothing_in_particular_is_about_the_group()
        {
            Assert.Equal("Reroute this element", new Resolution(ResolutionAction.Reroute).Describe());
        }

        [Fact]
        public void A_note_rides_along_where_the_typed_fields_cannot_say_it()
        {
            Assert.Equal("Move it 150 mm down (after the valve is relocated)",
                new Resolution(ResolutionAction.Move, "it", ResolutionAxis.ZMinus, 0.15,
                               "after the valve is relocated").Describe(Measure.Millimetres));
        }

        [Fact]
        public void An_arrow_points_even_when_nobody_said_how_far()
        {
            // "That way" is worth drawing. The caller scales it to whatever reads at the view's zoom.
            var instruction = new Resolution(ResolutionAction.Move, axis: ResolutionAxis.ZMinus);

            Assert.Equal((0d, 0d, -1d), instruction.Direction!.Value);
            Assert.Null(instruction.Amount);
        }

        [Fact]
        public void A_blank_field_is_nothing_rather_than_an_empty_string()
        {
            var instruction = new Resolution(ResolutionAction.Hold, "   ", note: "  ", subjectKey: " ");

            Assert.Null(instruction.Subject);
            Assert.Null(instruction.Note);
            Assert.Null(instruction.SubjectKey);
        }

        [Fact]
        public void An_amount_that_is_not_a_number_is_dropped_rather_than_printed()
        {
            Assert.Null(new Resolution(ResolutionAction.Move, amount: double.NaN).Amount);
            Assert.Null(new Resolution(ResolutionAction.Move, amount: double.PositiveInfinity).Amount);
        }

        [Fact]
        public void It_writes_down_and_reads_back_whole()
        {
            var instruction = new Resolution(ResolutionAction.Move, "the 400 duct",
                                             ResolutionAxis.ZMinus, 0.15, "after the valve moves",
                                             "abc/12/3");

            var back = Resolution.FromJson(JsonReader.Parse(instruction.ToJson().ToJson()));

            Assert.Equal(ResolutionAction.Move, back.Action);
            Assert.Equal("the 400 duct", back.Subject);
            Assert.Equal(ResolutionAxis.ZMinus, back.Axis);
            Assert.Equal(0.15, back.Amount!.Value, 6);
            Assert.Equal("after the valve moves", back.Note);
            Assert.Equal("abc/12/3", back.SubjectKey);
        }

        [Fact]
        public void Anything_unreadable_reads_back_as_nothing_said_rather_than_throwing()
        {
            Assert.False(Resolution.FromJson(null).IsStated);
            Assert.False(Resolution.FromJson(JsonValue.Array()).IsStated);
            Assert.False(Resolution.FromJson(JsonValue.Object()).IsStated);
            Assert.False(Resolution.FromJson(JsonValue.Object().Set("action", "Levitate")).IsStated);
        }

        [Theory]
        [InlineData("Move", ResolutionAction.Move)]
        [InlineData("move", ResolutionAction.Move)]
        [InlineData("Other trade moves", ResolutionAction.OtherTradeMoves)]
        [InlineData("Not stated", ResolutionAction.NotStated)]
        [InlineData("something nobody wrote", ResolutionAction.NotStated)]
        [InlineData("", ResolutionAction.NotStated)]
        [InlineData(null, ResolutionAction.NotStated)]
        public void A_verb_is_read_from_what_a_picker_or_a_file_says(string? word, ResolutionAction expected)
        {
            Assert.Equal(expected, Resolution.ActionOf(word));
        }

        [Theory]
        [InlineData("up", ResolutionAxis.ZPlus)]
        [InlineData("+Z", ResolutionAxis.ZPlus)]
        [InlineData("down", ResolutionAxis.ZMinus)]
        [InlineData("-x", ResolutionAxis.XMinus)]
        [InlineData("ZMinus", ResolutionAxis.ZMinus)]
        [InlineData("—", ResolutionAxis.None)]
        [InlineData("sideways", ResolutionAxis.None)]
        public void A_direction_is_read_from_what_a_picker_or_a_file_says(string word, ResolutionAxis expected)
        {
            Assert.Equal(expected, Resolution.AxisOf(word));
        }

        [Fact]
        public void Every_word_a_picker_offers_reads_back_as_itself()
        {
            // The pickers and the parsers are two lists that have to agree, and nothing but a test
            // makes them.
            for (var i = 0; i < Resolution.Actions.Count; i++)
                Assert.Equal((ResolutionAction)i, Resolution.ActionOf(Resolution.Actions[i]));

            for (var i = 0; i < Resolution.Directions.Count; i++)
                Assert.Equal((ResolutionAxis)i, Resolution.AxisOf(Resolution.Directions[i]));

            var instruction = new Resolution(ResolutionAction.OtherTradeMoves, axis: ResolutionAxis.ZPlus);

            Assert.Equal("Other trade moves", instruction.ActionWord);
            Assert.Equal("up", instruction.DirectionWord);
        }

        // ---------------------------------------------------------------------------------
        // On the board
        // ---------------------------------------------------------------------------------

        [Fact]
        public void An_instruction_survives_a_re_run_no_rule_can_restate_it()
        {
            var week1 = new[] { ClashFixture.Item("a") };
            var first = ClashPipeline.Run(week1, new ClashPipelineOptions());

            first.Groups[0].Instruct(new Resolution(ResolutionAction.Move, "the duct",
                                                    ResolutionAxis.ZMinus, 0.15));

            var snapshot = ClashSnapshot.Of(first);

            var week2 = new[] { ClashFixture.Item("a") };
            var second = ClashPipeline.Run(week2, new ClashPipelineOptions { Decisions = snapshot.Decisions });

            var group = Assert.Single(second.Groups);

            Assert.True(group.Instruction.IsStated);
            Assert.Equal("Move the duct 150 mm down", group.Instruction.Describe(Measure.Millimetres));
        }

        [Fact]
        public void Carrying_an_instruction_does_not_promote_a_rule_guess_to_a_hand_assignment()
        {
            // The group is assigned by a rule and instructed by a person. Carrying the party across
            // as a decision would make next week's run treat the machine's guess as untouchable.
            var week1 = new[] { ClashFixture.Item("a") };

            var first = ClashPipeline.Run(week1, new ClashPipelineOptions
            {
                Assigns = new[] { new AssignRule(ClashPredicates.Always(), "Mechanical") },
            });

            Assert.Equal("Mechanical", first.Groups[0].AssignedTo);
            Assert.False(first.Groups[0].AssignedByHand);

            first.Groups[0].Instruct(new Resolution(ResolutionAction.Hold));

            var decision = Assert.Single(ClashSnapshot.Of(first).Decisions).Value;

            Assert.Null(decision.AssignedTo);
            Assert.Equal(ResolutionAction.Hold, decision.Instruction.Action);
            Assert.True(decision.IsSomething);
        }

        [Fact]
        public void A_group_nobody_touched_carries_no_decision_at_all()
        {
            var run = ClashPipeline.Run(new[] { ClashFixture.Item("a") }, new ClashPipelineOptions());

            Assert.Empty(ClashSnapshot.Of(run).Decisions);
        }

        [Fact]
        public void A_raised_issue_carries_its_instruction_through_the_project_file()
        {
            // An issue somebody raised deserves the typed instruction more than a clash does:
            // nobody's engine is going to restate it next week.
            var key = ClashKey.Create(ClashFixture.Mep("valve"), ClashFixture.Arch("wall"), 1, 2, 3);

            var issue = new ManualIssue("issue-1", key, "Door swings into the escape route")
            {
                Instruction = new Resolution(ResolutionAction.Reroute, "the door swing"),
            };

            var list = new ManualIssueList();
            list.Put(issue);

            var back = ManualIssueList.FromJson(list.ToJson());

            Assert.Equal(ResolutionAction.Reroute, back.Issues.Single().Instruction.Action);
            Assert.Equal("the door swing", back.Issues.Single().Instruction.Subject);
        }
    }
}
