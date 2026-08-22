using CamelWorks.Core.Measurement;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class MeasureTests
    {
        [Theory]
        [InlineData(2.45, "2.450 m")]
        [InlineData(0, "0.000 m")]
        [InlineData(-2.45, "-2.450 m")]
        [InlineData(1234.5678, "1,234.568 m")]
        public void Metres_read_to_the_millimetre(double metres, string expected)
        {
            Assert.Equal(expected, Measure.Metric.Length(metres));
        }

        [Theory]
        [InlineData(2.45, "2,450 mm")]
        [InlineData(0.01, "10 mm")]
        [InlineData(0, "0 mm")]
        public void Millimetres_read_whole(double metres, string expected)
        {
            Assert.Equal(expected, Measure.Millimetres.Length(metres));
        }

        [Theory]
        [InlineData(2.45, "8' 0 7/16\"")]
        [InlineData(0.3048, "1' 0\"")]
        [InlineData(0, "0' 0\"")]
        public void Feet_and_inches_read_as_a_tape_measure_does(double metres, string expected)
        {
            Assert.Equal(expected, Measure.Imperial.Length(metres));
        }

        [Fact]
        public void A_length_a_hair_under_a_foot_carries_rather_than_printing_twelve_inches()
        {
            // The whole length is rounded once, in ticks, before it is split into feet and inches.
            // Rounding the inches on their own is how a tool prints 0' 12".
            Assert.Equal("1' 0\"", Measure.Imperial.Length(11.98 * 0.0254));
        }

        [Fact]
        public void The_inch_fraction_is_settable_and_reduced()
        {
            Assert.Equal("1' 7 1/2\"", new Measure(UnitSystem.Imperial, 2).Length(0.5));
            Assert.Equal("1' 7 11/16\"", new Measure(UnitSystem.Imperial, 16).Length(0.5));
        }

        [Fact]
        public void A_fraction_nobody_dimensions_in_falls_back_to_sixteenths()
        {
            Assert.Equal(16, new Measure(UnitSystem.Imperial, 5).InchDenominator);
            Assert.Equal(64, new Measure(UnitSystem.Imperial, 64).InchDenominator);
        }

        [Theory]
        [InlineData(0.012, "+12 mm")]
        [InlineData(-0.012, "-12 mm")]
        [InlineData(0, "0 mm")]
        public void A_delta_always_carries_its_sign_except_at_zero(double metres, string expected)
        {
            Assert.Equal(expected, Measure.Millimetres.Delta(metres));
        }

        [Fact]
        public void A_delta_that_rounds_away_reads_as_zero_rather_than_minus_zero()
        {
            Assert.Equal("0 mm", Measure.Millimetres.Delta(-0.0004));
            Assert.Equal("0.000 m", Measure.Metric.Delta(-0.0000004));
        }

        [Fact]
        public void An_ordinary_length_carries_no_plus_sign()
        {
            Assert.Equal("12 mm", Measure.Millimetres.Length(0.012));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Something_that_is_not_a_number_says_so_without_saying_NaN(double bad)
        {
            Assert.Equal(Measure.Nothing, Measure.Metric.Length(bad));
            Assert.Equal(Measure.Nothing, Measure.Metric.Delta(bad));
            Assert.Equal(Measure.Nothing, Measure.Metric.Area(bad));
            Assert.Equal(Measure.Nothing, Measure.Metric.Volume(bad));
            Assert.Equal(Measure.Nothing, Measure.Metric.Angle(bad));
            Assert.Equal(Measure.Nothing, Measure.Imperial.Length(bad));
        }

        [Fact]
        public void Area_and_volume_convert_but_millimetres_do_not_drag_them_along()
        {
            // A floor is 20 m2 in both metric modes. Nobody has ever wanted 20,000,000 mm2.
            Assert.Equal("20.46 m²", Measure.Metric.Area(20.456));
            Assert.Equal("20.46 m²", Measure.Millimetres.Area(20.456));
            Assert.Equal("1,076.39 ft²", Measure.Imperial.Area(100));

            Assert.Equal("12.346 m³", Measure.Metric.Volume(12.3456));
            Assert.Equal("35.31 ft³", Measure.Imperial.Volume(1));
        }

        [Theory]
        [InlineData(90, "90.00°")]
        [InlineData(359.9, "-0.10°")]
        [InlineData(-0.0001, "0.00°")]
        [InlineData(181, "-179.00°")]
        public void An_angle_reads_the_short_way_round(double degrees, string expected)
        {
            // 359.9 degrees is a tenth of a degree the other way, and reading it as very nearly a
            // full turn has put a model on site facing the wrong way.
            Assert.Equal(expected, Measure.Metric.Angle(degrees));
        }

        [Theory]
        [InlineData("Metres", UnitSystem.Metric)]
        [InlineData("Millimetres", UnitSystem.Millimetres)]
        [InlineData("millimeters", UnitSystem.Millimetres)]
        [InlineData("Metric (mm)", UnitSystem.Millimetres)]
        [InlineData("mm", UnitSystem.Millimetres)]
        [InlineData("Feet and inches", UnitSystem.Imperial)]
        [InlineData("Imperial (ft-in)", UnitSystem.Imperial)]
        [InlineData("inches", UnitSystem.Imperial)]
        [InlineData("", UnitSystem.Metric)]
        [InlineData(null, UnitSystem.Metric)]
        [InlineData("something nobody wrote", UnitSystem.Metric)]
        public void A_written_unit_is_read_forgivingly(string? word, UnitSystem expected)
        {
            // A saved profile that predates a wording change must not silently change a project's
            // units.
            Assert.Equal(expected, Measure.Of(word).System);
        }

        [Fact]
        public void Every_offered_choice_reads_back_as_itself()
        {
            Assert.Equal(3, Measure.Choices.Count);

            Assert.Equal(UnitSystem.Metric, Measure.Of(Measure.Choices[0]).System);
            Assert.Equal(UnitSystem.Millimetres, Measure.Of(Measure.Choices[1]).System);
            Assert.Equal(UnitSystem.Imperial, Measure.Of(Measure.Choices[2]).System);
        }

        [Fact]
        public void A_table_cell_leaves_the_unit_to_its_heading()
        {
            Assert.Equal("2.450", Measure.Metric.Bare(2.45));
            Assert.Equal("2,450", Measure.Millimetres.Bare(2.45));
            Assert.Equal("-2.450", Measure.Metric.Bare(-2.45));
            Assert.Equal("0.000", Measure.Metric.Bare(-0.0000001));

            // Feet and inches have no bare form. The marks are the notation.
            Assert.Equal("8' 0 7/16\"", Measure.Imperial.Bare(2.45));

            Assert.Equal(Measure.Nothing, Measure.Metric.Bare(double.NaN));
        }

        [Fact]
        public void A_column_heading_can_say_the_unit_so_the_cells_do_not_have_to()
        {
            Assert.Equal("m", Measure.Metric.LengthUnit);
            Assert.Equal("mm", Measure.Millimetres.LengthUnit);
            Assert.Equal("ft-in", Measure.Imperial.LengthUnit);
            Assert.Equal("m²", Measure.Metric.AreaUnit);
            Assert.Equal("ft³", Measure.Imperial.VolumeUnit);
        }

        [Fact]
        public void It_says_what_it_does_in_words_a_settings_screen_can_print()
        {
            Assert.Equal("metres, to the millimetre", Measure.Metric.Describe());
            Assert.Equal("whole millimetres", Measure.Millimetres.Describe());
            Assert.Equal("feet and inches, to the nearest 1/16", Measure.Imperial.Describe());
            Assert.Equal("feet and inches, to the nearest 1/32",
                new Measure(UnitSystem.Imperial, 32).Describe());
        }

        [Theory]
        [InlineData("2.1", 2.1)]
        [InlineData("2100 mm", 2.1)]
        [InlineData("2.1m", 2.1)]
        [InlineData("210 cm", 2.1)]
        [InlineData("83 in", 2.1082)]
        [InlineData("7 ft", 2.1336)]
        [InlineData("-2.1", -2.1)]
        [InlineData("  2.1  ", 2.1)]
        public void A_typed_length_is_read_with_its_written_unit_winning(string text, double metres)
        {
            Assert.True(Measure.Metric.TryRead(text, out var read));
            Assert.Equal(metres, read, 6);
        }

        [Theory]
        [InlineData("6' 10 1/2\"", 2.0955)]
        [InlineData("6'-10\"", 2.0828)]
        [InlineData("10 1/2\"", 0.2667)]
        [InlineData("6'", 1.8288)]
        [InlineData("-6' 10 1/2\"", -2.0955)]
        public void Feet_and_inches_are_read_the_way_people_write_them(string text, double metres)
        {
            Assert.True(Measure.Imperial.TryRead(text, out var read));
            Assert.Equal(metres, read, 4);
        }

        [Fact]
        public void A_bare_number_is_read_in_the_project_own_unit()
        {
            // The counterpart of printing: a screen that prints millimetres and asks for metres will
            // be typed into wrongly, and both numbers look plausible.
            Assert.True(Measure.Metric.TryRead("2.1", out var metric));
            Assert.Equal(2.1, metric, 6);

            Assert.True(Measure.Millimetres.TryRead("2100", out var mm));
            Assert.Equal(2.1, mm, 6);

            Assert.True(Measure.Imperial.TryRead("7", out var feet));
            Assert.Equal(2.1336, feet, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("about two metres")]
        [InlineData("2.1 furlongs")]
        [InlineData("-")]
        [InlineData("6' 10 1/2 3/4\"")]
        [InlineData("6' 1/0\"")]
        public void Something_that_is_not_a_length_is_refused_rather_than_guessed_at(string? text)
        {
            Assert.False(Measure.Metric.TryRead(text, out var read));
            Assert.Equal(0, read);

            Assert.False(Measure.Imperial.TryRead(text, out _));
        }

        [Fact]
        public void What_it_prints_it_can_read_back()
        {
            foreach (var units in new[] { Measure.Metric, Measure.Millimetres, Measure.Imperial })
            {
                Assert.True(units.TryRead(units.Length(2.45), out var back), units.Describe());
                Assert.Equal(2.45, back, 3);
            }
        }

        [Fact]
        public void A_count_is_separated_so_a_six_figure_one_can_be_read()
        {
            Assert.Equal("1,234,567", Measure.Count(1234567));
            Assert.Equal("0", Measure.Count(0));
        }
    }
}
