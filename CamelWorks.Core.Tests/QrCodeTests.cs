using System;
using System.Linq;
using CamelWorks.Core.Field;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The reference symbols below were produced away from this code and checked two ways: against
    /// two other encoders, which agreed with them module for module, and by putting them through a
    /// real decoder, which read back out what went in. A QR code that does not scan is worse than
    /// no QR code at all, and there is no cheap way to find that out on site.
    /// </summary>
    public class QrCodeTests
    {
        // The reference symbol for "CW-001" at low correction: version 1, twenty-one modules across.
        private static readonly string[] OneLow = new[]
        {
            "#######.##..#.#######",
            "#.....#..#..#.#.....#",
            "#.###.#.#.#.#.#.###.#",
            "#.###.#.#..#..#.###.#",
            "#.###.#.###...#.###.#",
            "#.....#.......#.....#",
            "#######.#.#.#.#######",
            ".........##..........",
            "####..#.#.#..#..###.#",
            "#.#..#...##.#..#.###.",
            "##.####..#..#####.###",
            "..#.#...#...##......#",
            "..#..####.##.#..#....",
            "........###..#....#.#",
            "#######..#...##.##...",
            "#.....#...#####...#.#",
            "#.###.#..#.#..#...###",
            "#.###.#.###..#.#...#.",
            "#.###.#.#..#.#.#..#..",
            "#.....#.#.###.#..#..#",
            "#######.##.##...###..",
        };

        // One character at the most forgiving level. Still version 1, and mostly check bits.
        private static readonly string[] OneHighest = new[]
        {
            "#######....##.#######",
            "#.....#.##.##.#.....#",
            "#.###.#.###...#.###.#",
            "#.###.#.#.#.#.#.###.#",
            "#.###.#.#.#...#.###.#",
            "#.....#.##....#.....#",
            "#######.#.#.#.#######",
            ".....................",
            "..#..#########.#####.",
            "..#.##.....#.#.#.##..",
            "####..#####.#..###.##",
            "##.#.#..#..##.##.###.",
            "#.#...#.#.##...####.#",
            "........##.####....##",
            "#######.####..#...###",
            "#.....#.##.#....#..#.",
            "#.###.#...####....###",
            "#.###.#..###.#.#.##..",
            "#.###.#.##..######.##",
            "#.....#....#...#.##..",
            "#######...#########.#",
        };

        // A link at medium correction: version 3, the first vector with more than one block.
        private static readonly string[] ThreeMedium = new[]
        {
            "#######..#.....#####..#######",
            "#.....#..........#..#.#.....#",
            "#.###.#.#...#.#.#...#.#.###.#",
            "#.###.#.#..#....###...#.###.#",
            "#.###.#.####.###...##.#.###.#",
            "#.....#.#...######..#.#.....#",
            "#######.#.#.#.#.#.#.#.#######",
            "........###.##.#...##........",
            "#.#####..#.#..##.#.#..#####..",
            "###.#...#...#..##.##.####...#",
            "#.#..###....#....#..##.#.....",
            "##..##.#.#..#.#.#.#.#...##.#.",
            "##...###...##...####.....##..",
            "........#..#####...######...#",
            "####.##.###########.#.#####..",
            "##.....#..#.##.#...##..#...#.",
            ".##...##..##..##.#.#.....##..",
            "##.#.#..#.#....##.#######.#.#",
            "#....###.#..#.....#.#..##.#..",
            "#...#...####..#.#..#.......#.",
            "#..##.#..#.#....#.#.#####.###",
            "........####.###.##.#...#####",
            "#######..#.#######.##.#.###..",
            "#.....#.#..#.#.#.####...#...#",
            "#.###.#.#..#..##....#####.###",
            "#.###.#.#.#.##.##.#.#....##..",
            "#.###.#.#...###...##########.",
            "#.....#...#...#.#...#.#..#.#.",
            "#######.##...#..#.#..#..#.#..",
        };

        private static string[] Drawn(QrSymbol symbol) => symbol.Rows().ToArray();

        // ---- conformance ---------------------------------------------------------------------

        [Fact]
        public void A_short_mark_at_low_correction_matches_the_reference_symbol()
        {
            var symbol = QrCode.Encode("CW-001", QrLevel.Low);

            Assert.NotNull(symbol);
            Assert.Equal(1, symbol!.Version);
            Assert.Equal(21, symbol.Size);
            Assert.Equal(OneLow, Drawn(symbol));
        }

        [Fact]
        public void One_character_at_the_most_forgiving_level_matches_the_reference_symbol()
        {
            var symbol = QrCode.Encode("5", QrLevel.Highest);

            Assert.NotNull(symbol);
            Assert.Equal(OneHighest, Drawn(symbol!));
        }

        [Fact]
        public void A_link_over_one_block_matches_the_reference_symbol()
        {
            var symbol = QrCode.Encode("https://bimcamel.com/e/7f3a", QrLevel.Medium);

            Assert.NotNull(symbol);
            Assert.Equal(3, symbol!.Version);
            Assert.Equal(29, symbol.Size);
            Assert.Equal(ThreeMedium, Drawn(symbol));
        }

        // ---- the parts of a symbol that are the same in every one of them ----------------------

        [Theory]
        [InlineData("CW-001", QrLevel.Low)]
        [InlineData("https://bimcamel.com/e/7f3a", QrLevel.Medium)]
        [InlineData("https://bimcamel.com/e/3f2c1b8a-55d1-4e0e-9a2c-6b7d8e9f0a1b", QrLevel.Highest)]
        [InlineData("PIPE-GF-0221 / chilled water flow / 2nd fix", QrLevel.High)]
        public void Every_symbol_has_its_three_corners(string text, QrLevel level)
        {
            var symbol = QrCode.Encode(text, level);

            Assert.NotNull(symbol);

            var n = symbol!.Size;

            foreach (var (top, left) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
                for (var dr = 0; dr < 7; dr++)
                    for (var dc = 0; dc < 7; dc++)
                    {
                        var ring = dr == 0 || dr == 6 || dc == 0 || dc == 6;
                        var core = dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4;

                        Assert.Equal(ring || core, symbol.IsDark(top + dr, left + dc));
                    }
        }

        [Theory]
        [InlineData("CW-001", QrLevel.Low)]
        [InlineData("https://bimcamel.com/e/3f2c1b8a-55d1-4e0e-9a2c-6b7d8e9f0a1b", QrLevel.Highest)]
        public void Every_symbol_has_its_timing_lines_and_its_one_dark_module(string text, QrLevel level)
        {
            var symbol = QrCode.Encode(text, level);

            Assert.NotNull(symbol);

            var n = symbol!.Size;

            for (var i = 8; i < n - 8; i++)
            {
                Assert.Equal(i % 2 == 0, symbol.IsDark(6, i));
                Assert.Equal(i % 2 == 0, symbol.IsDark(i, 6));
            }

            Assert.True(symbol.IsDark(n - 8, 8));
        }

        [Fact]
        public void A_symbol_is_always_a_size_the_standard_has_a_name_for()
        {
            foreach (var level in new[] { QrLevel.Low, QrLevel.Medium, QrLevel.High, QrLevel.Highest })
                foreach (var length in new[] { 1, 8, 30, 90, 200, 500, 1200 })
                {
                    var symbol = QrCode.Encode(new string('W', length), level);

                    if (symbol == null) continue;

                    Assert.Equal(17 + symbol.Version * 4, symbol.Size);
                    Assert.InRange(symbol.Version, 1, QrCode.MaxVersion);
                    Assert.InRange(symbol.Mask, 0, 7);
                }
        }

        // ---- how big a symbol has to be --------------------------------------------------------

        [Fact]
        public void The_smallest_symbol_that_will_hold_it_is_the_one_it_gets()
        {
            Assert.Equal(1, QrCode.Fits(17, QrLevel.Low));
            Assert.Equal(2, QrCode.Fits(18, QrLevel.Low));
            Assert.Equal(1, QrCode.Fits(7, QrLevel.Highest));
            Assert.Equal(2, QrCode.Fits(8, QrLevel.Highest));
        }

        [Fact]
        public void A_more_forgiving_level_holds_less()
        {
            Assert.True(QrCode.Holds(10, QrLevel.Low) > QrCode.Holds(10, QrLevel.Medium));
            Assert.True(QrCode.Holds(10, QrLevel.Medium) > QrCode.Holds(10, QrLevel.High));
            Assert.True(QrCode.Holds(10, QrLevel.High) > QrCode.Holds(10, QrLevel.Highest));
        }

        [Fact]
        public void What_it_says_it_holds_is_what_it_holds()
        {
            foreach (var level in new[] { QrLevel.Low, QrLevel.Medium, QrLevel.High, QrLevel.Highest })
                foreach (var version in new[] { 1, 6, 9, 10, 26, 27, 40 })
                {
                    var holds = QrCode.Holds(version, level);

                    Assert.Equal(version, QrCode.Fits(holds, level));
                    Assert.True(QrCode.Fits(holds + 1, level) > version
                                || QrCode.Fits(holds + 1, level) == 0);
                }
        }

        [Fact]
        public void The_biggest_symbol_there_is_holds_what_the_standard_says_it_does()
        {
            Assert.Equal(QrCode.MaxBytes, QrCode.Holds(QrCode.MaxVersion, QrLevel.Low));
        }

        // ---- refusals ---------------------------------------------------------------------------

        [Fact]
        public void Nothing_to_encode_is_no_symbol_rather_than_an_empty_one()
        {
            Assert.Null(QrCode.Encode(null));
            Assert.Null(QrCode.Encode(string.Empty));
        }

        [Fact]
        public void More_than_the_biggest_symbol_holds_is_refused_rather_than_truncated()
        {
            Assert.Null(QrCode.Encode(new string('A', QrCode.MaxBytes + 1), QrLevel.Low));
            Assert.NotNull(QrCode.Encode(new string('A', QrCode.MaxBytes), QrLevel.Low));
        }

        [Fact]
        public void A_character_that_takes_more_than_one_byte_is_counted_in_bytes()
        {
            // Twelve hundred characters that are three bytes each is thirty-six hundred bytes,
            // which is more than any symbol holds however few characters it looks like.
            Assert.Null(QrCode.Encode(new string('中', 1200), QrLevel.Low));

            var symbol = QrCode.Encode("Sørensen — 30°", QrLevel.Medium);

            Assert.NotNull(symbol);
            Assert.Equal("Sørensen — 30°", symbol!.Text);
        }

        // ---- what comes out ----------------------------------------------------------------------

        [Fact]
        public void The_svg_is_one_shape_the_size_it_was_asked_for()
        {
            var symbol = QrCode.Encode("CW-001", QrLevel.Low);
            var svg = symbol!.Svg(30, 4);

            Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
            Assert.EndsWith("</svg>", svg);
            Assert.Contains("width=\"30mm\"", svg);
            Assert.Contains("viewBox=\"0 0 29 29\"", svg);
            Assert.Equal(1, svg.Split(new[] { "<path" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void The_quiet_zone_is_clear_of_the_symbol_itself()
        {
            var symbol = QrCode.Encode("CW-001", QrLevel.Low);

            // Four modules of margin each side, so the drawing is eight wider than the symbol and
            // the first dark run cannot start before column four.
            Assert.Contains("viewBox=\"0 0 29 29\"", symbol!.Svg(25, 4));
            Assert.Contains("viewBox=\"0 0 21 21\"", symbol.Svg(25, 0));
        }

        [Fact]
        public void The_drawing_and_the_modules_say_the_same_thing()
        {
            var symbol = QrCode.Encode("https://bimcamel.com/e/7f3a", QrLevel.Medium);
            var rows = symbol!.Rows('X', ' ');

            Assert.Equal(symbol.Size, rows.Count);

            for (var row = 0; row < symbol.Size; row++)
            {
                Assert.Equal(symbol.Size, rows[row].Length);

                for (var column = 0; column < symbol.Size; column++)
                    Assert.Equal(symbol.IsDark(row, column), rows[row][column] == 'X');
            }
        }

        [Fact]
        public void Asking_outside_the_symbol_is_light_rather_than_a_crash()
        {
            var symbol = QrCode.Encode("CW-001", QrLevel.Low);

            Assert.False(symbol!.IsDark(-1, 0));
            Assert.False(symbol.IsDark(0, -1));
            Assert.False(symbol.IsDark(symbol.Size, 0));
            Assert.False(symbol.IsDark(0, symbol.Size));
        }

        [Fact]
        public void A_symbol_says_what_it_is()
        {
            var symbol = QrCode.Encode("CW-001", QrLevel.Highest);

            Assert.Equal(QrLevel.Highest, symbol!.Level);
            Assert.Contains("highest", symbol.ToString(), StringComparison.Ordinal);
            Assert.Contains("modules across", symbol.ToString(), StringComparison.Ordinal);
        }
    }
}
