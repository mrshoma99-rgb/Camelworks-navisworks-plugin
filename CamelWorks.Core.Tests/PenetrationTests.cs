using System;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class PenetrationTests
    {
        [Theory]
        [InlineData("Ducts", true)]
        [InlineData("Pipe Fittings", true)]
        [InlineData("Cable Trays", true)]
        [InlineData("Conduits", true)]
        [InlineData("Sprinklers", true)]
        [InlineData("Walls", false)]
        [InlineData("Furniture", false)]
        [InlineData(null, false)]
        public void A_service_is_something_that_runs_through_a_building(string? category, bool expected)
        {
            Assert.Equal(expected, Penetrations.IsService(category));
        }

        [Theory]
        [InlineData("Walls", true)]
        [InlineData("Floors", true)]
        [InlineData("Structural Columns", true)]
        [InlineData("Structural Framing", true)]
        [InlineData("Roofs", true)]
        [InlineData("Ducts", false)]
        [InlineData("Furniture", false)]
        public void Structure_is_something_a_service_would_have_to_go_through(string category, bool expected)
        {
            Assert.Equal(expected, Penetrations.IsStructure(category));
        }

        [Fact]
        public void A_type_name_counts_where_the_category_says_nothing_useful()
        {
            // Plenty of exports put everything under "Generic Models" and leave the type to say
            // what it is.
            Assert.True(Penetrations.IsService("Generic Models", "600x400 Rect Duct"));
            Assert.True(Penetrations.IsStructure("Generic Models", "300mm Concrete Wall"));
        }

        [Fact]
        public void Only_a_mixed_pair_is_a_penetration()
        {
            // A duct through a wall is a penetration. A duct through a duct is a clash, and a wall
            // through a wall is a modelling error.
            Assert.True(Penetrations.ServiceIsA("Ducts", null, "Walls", null));
            Assert.False(Penetrations.ServiceIsA("Walls", null, "Ducts", null));
            Assert.Null(Penetrations.ServiceIsA("Ducts", null, "Pipes", null));
            Assert.Null(Penetrations.ServiceIsA("Walls", null, "Floors", null));
            Assert.Null(Penetrations.ServiceIsA("Furniture", null, "Walls", null));
        }

        [Fact]
        public void An_element_that_reads_as_both_is_not_guessed_at()
        {
            // "Pipe Sleeve Wall" reads as a service and as structure at once. Picking one would be
            // inventing a decision from a naming accident.
            Assert.Null(Penetrations.ServiceIsA("Pipe Sleeve Wall", null, "Walls", null));
        }

        [Fact]
        public void A_beam_arrives_as_structural_framing_and_has_to_be_recognised_as_such()
        {
            // The Revit category for a beam says nothing about beams, and a word list that missed
            // it would drop every penetration through a steel frame.
            Assert.True(Penetrations.ServiceIsA("Ducts", null, "Structural Framing", null));
        }

        [Fact]
        public void The_schedule_reads_by_level_then_by_service()
        {
            var schedule = Penetrations.Of(new[]
            {
                Line("L03", "Duct B"),
                Line(null, "Duct C"),
                Line("L01", "Duct A"),
                Line("L01", "Duct Z"),
            });

            Assert.Equal(new[] { "Duct A", "Duct Z", "Duct B", "Duct C" },
                schedule.Lines.Select(l => l.Service).ToArray());
        }

        [Fact]
        public void What_the_model_says_is_what_the_schedule_carries()
        {
            var schedule = Penetrations.Of(new[]
            {
                new PenetrationLine("L03 MEP v STR", "Supply duct", "Core wall")
                {
                    Size = "600x400",
                    SystemName = "Supply Air",
                    HostType = "300mm Concrete",
                    Level = "L03",
                    Party = "Mechanical",
                    X = 12.34, Y = 5.6, Z = 3.15,
                    Status = FindingStatus.Active,
                },
            });

            var row = schedule.Table(Measure.Millimetres).Rows.Single();

            Assert.Equal("L03", row[0]);
            Assert.Equal("Supply duct", row[1]);
            Assert.Equal("600x400", row[2]);
            Assert.Equal("Supply Air", row[3]);
            Assert.Equal("Core wall", row[4]);
            Assert.Equal("300mm Concrete", row[5]);
            Assert.Equal("12,340", row[6]);
            Assert.Equal("5,600", row[7]);
            Assert.Equal("3,150", row[8]);
            Assert.Equal("Mechanical", row[9]);
        }

        [Fact]
        public void A_service_with_no_stated_size_says_so_rather_than_getting_one_derived()
        {
            // No opening size derived and no host thickness inferred: both are numbers somebody
            // cuts concrete from, and neither is this tool's to guess.
            var schedule = Penetrations.Of(new[] { Line("L01", "Duct A") });

            Assert.Equal(Measure.Nothing, schedule.Table().Rows.Single()[2]);
            Assert.Contains("1 with no size on the service", schedule.Explain());
        }

        [Fact]
        public void An_unassigned_penetration_reads_as_unassigned_rather_than_blank()
        {
            Assert.Equal(PartyLedger.Nobody, Penetrations.Of(new[] { Line("L01", "Duct A") })
                .Table().Rows.Single()[9]);
        }

        [Fact]
        public void Nothing_qualifying_is_a_readable_answer_and_says_how_much_was_looked_at()
        {
            var schedule = Penetrations.Of(Array.Empty<PenetrationLine>(), considered: 412);

            Assert.True(schedule.IsEmpty);
            Assert.Equal(412, schedule.Considered);
            Assert.Contains("412 looked at", schedule.Explain());
        }

        [Fact]
        public void The_count_of_levels_is_of_distinct_levels()
        {
            var schedule = Penetrations.Of(new[]
            {
                Line("L01", "A"), Line("L01", "B"), Line("L02", "C"),
            });

            Assert.Contains("3 penetrations across 2 levels", schedule.Explain());
        }

        private static PenetrationLine Line(string? level, string service) =>
            new PenetrationLine("group", service, "Core wall") { Level = level };
    }
}
