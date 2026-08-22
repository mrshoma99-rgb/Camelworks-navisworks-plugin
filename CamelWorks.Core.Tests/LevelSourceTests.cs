using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class LevelSourceTests
    {
        private static IEnumerable<KeyValuePair<string, double>> Storeys() => new[]
        {
            new KeyValuePair<string, double>("Level 00", 0.0),
            new KeyValuePair<string, double>("Level 01", 3.6),
            new KeyValuePair<string, double>("Level 02", 7.2),
        };

        [Fact]
        public void A_grid_says_it_came_from_the_grid()
        {
            var levels = LevelSet.FromGrid(Storeys());

            Assert.Equal(LevelSource.Grid, levels.Source);
            Assert.True(levels.IsFromModel);
            Assert.Equal(3, levels.Bands.Count);
            Assert.Contains("coordination grid", levels.ToString());
            Assert.Contains("coordination grid", levels.Provenance());
        }

        [Fact]
        public void A_property_says_it_came_from_the_property()
        {
            var levels = LevelSet.FromModel(Storeys());

            Assert.Equal(LevelSource.Property, levels.Source);
            Assert.Contains("level property", levels.ToString());

            // And names the blind spot: one authored fact per model, so two spellings are two
            // levels. A source named without its blind spot is half a disclosure.
            Assert.Contains("each author separately", levels.Provenance());
        }

        [Fact]
        public void A_histogram_says_it_guessed()
        {
            var elevations = Enumerable.Repeat(0.0, 50)
                .Concat(Enumerable.Repeat(3.6, 50))
                .Concat(Enumerable.Repeat(7.2, 50));

            var levels = LevelSet.Derive(elevations);

            Assert.Equal(LevelSource.Inferred, levels.Source);
            Assert.False(levels.IsFromModel);
            Assert.Contains("inferred from geometry", levels.ToString());
            Assert.Contains("histogram", levels.Provenance());

            // Named after the elevation, never numbered.
            Assert.All(levels.Bands, b => Assert.DoesNotContain("Level 2", b.Name));
        }

        [Fact]
        public void A_derivation_with_one_flat_band_still_says_it_guessed()
        {
            var levels = LevelSet.Derive(Enumerable.Range(0, 200).Select(i => i * 0.37));

            Assert.Equal(LevelSource.Inferred, levels.Source);
            Assert.Contains("histogram", levels.Provenance());
        }

        [Fact]
        public void Nothing_at_all_has_no_source_and_says_so()
        {
            Assert.Equal(LevelSource.None, LevelSet.Empty.Source);
            Assert.Contains("carries a grid, a level property, or geometry",
                            LevelSet.Empty.Provenance());

            var fromNothing = LevelSet.FromGrid(new KeyValuePair<string, double>[0]);

            Assert.Equal(LevelSource.None, fromNothing.Source);
            Assert.Equal("no levels", fromNothing.ToString());
        }

        [Fact]
        public void A_blank_grid_level_name_is_dropped_rather_than_numbered()
        {
            var levels = LevelSet.FromGrid(new[]
            {
                new KeyValuePair<string, double>("Level 00", 0.0),
                new KeyValuePair<string, double>("   ", 3.6),
                new KeyValuePair<string, double>("Level 02", 7.2),
            });

            Assert.Equal(new[] { "Level 00", "Level 02" }, levels.Bands.Select(b => b.Name));
        }

        [Fact]
        public void Grid_levels_band_the_same_way_the_others_do()
        {
            var levels = LevelSet.FromGrid(Storeys());

            Assert.Equal("Level 00", levels.LevelAt(1.2));
            Assert.Equal("Level 01", levels.LevelAt(3.6));
            Assert.Equal("Level 01", levels.LevelAt(7.19));
            Assert.Equal("Level 02", levels.LevelAt(400.0));

            // An element that spans two storeys is filed at its base, which is where a person
            // standing beside it would say it is.
            Assert.Equal("Level 00", levels.LevelOf(1.0, 5.0));
            Assert.Equal(new[] { "Level 00", "Level 01" }, levels.Spans(1.0, 5.0));
        }

        [Fact]
        public void The_chain_prefers_the_grid_then_the_property_then_the_guess()
        {
            var grid = LevelSet.FromGrid(Storeys());
            var property = LevelSet.FromModel(Storeys());
            var guess = LevelSet.Derive(Enumerable.Repeat(0.0, 50));

            // The order the screen walks: whichever comes first with bands on it wins.
            Assert.Equal(LevelSource.Grid, Pick(grid, property, guess).Source);
            Assert.Equal(LevelSource.Property, Pick(LevelSet.Empty, property, guess).Source);
            Assert.Equal(LevelSource.Inferred, Pick(LevelSet.Empty, LevelSet.Empty, guess).Source);
        }

        private static LevelSet Pick(LevelSet grid, LevelSet property, LevelSet guess) =>
            grid.Bands.Count > 0 ? grid : property.Bands.Count > 0 ? property : guess;
    }
}
