using System.Collections.Generic;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Levels that do not separate anything are not levels.
    ///
    /// From a shipped build on a real federation: the coordination grid reported three levels with
    /// every elevation at 0.000. Three bands, one height. Every element falls in the first band, so
    /// grouping by level does nothing at all - but a chain that asks only "are there any?" takes it
    /// over a histogram that would have worked.
    /// </summary>
    public class LevelSeparationTests
    {
        [Fact]
        public void ThreeLevelsAtOneHeightDoNotSeparate()
        {
            var set = LevelSet.FromGrid(new[]
            {
                new KeyValuePair<string, double>("Level 1", 0),
                new KeyValuePair<string, double>("Level 2", 0),
                new KeyValuePair<string, double>("Level 3", 0),
            });

            Assert.Equal(3, set.Bands.Count);
            Assert.Equal(1, set.DistinctElevations);
            Assert.False(set.Separates);
        }

        [Fact]
        public void OneLevelSeparatesBecauseASingleStoreyIsAnAnswer()
        {
            var set = LevelSet.FromGrid(new[]
            {
                new KeyValuePair<string, double>("Ground", 0),
            });

            Assert.True(set.Separates);
        }

        [Fact]
        public void RealElevationsSeparate()
        {
            var set = LevelSet.FromGrid(new[]
            {
                new KeyValuePair<string, double>("Level 1", 0),
                new KeyValuePair<string, double>("Level 2", 4.2),
                new KeyValuePair<string, double>("Level 3", 8.4),
            });

            Assert.Equal(3, set.DistinctElevations);
            Assert.True(set.Separates);
        }

        [Fact]
        public void NothingAtAllDoesNotSeparate()
        {
            Assert.False(LevelSet.Empty.Separates);
        }
    }
}
