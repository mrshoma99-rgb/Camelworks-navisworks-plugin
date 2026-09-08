using System.Linq;
using CamelWorks.Core.Findings;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The board's status keys.
    ///
    /// The risk this covers is not a wrong digit today; it is the map and the sheet that lists it
    /// drifting apart after a status is added or the lattice reordered. Both are generated from
    /// the lattice, and these tests say so.
    /// </summary>
    public class FindingStatusKeyTests
    {
        [Fact]
        public void EveryStatusHasExactlyOneDigit()
        {
            var digits = FindingStatusLattice.All.Select(FindingStatusKeys.Digit).ToList();

            Assert.Equal(FindingStatusLattice.All.Count, digits.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, FindingStatusLattice.All.Count), digits);
        }

        [Fact]
        public void ADigitReadsBackAsTheStatusItStandsFor()
        {
            foreach (var status in FindingStatusLattice.All)
                Assert.Equal(status, FindingStatusKeys.Of(FindingStatusKeys.Digit(status)));
        }

        [Fact]
        public void ADigitOutsideTheLatticeMeansNothing()
        {
            Assert.Null(FindingStatusKeys.Of(0));
            Assert.Null(FindingStatusKeys.Of(FindingStatusLattice.All.Count + 1));
        }

        [Fact]
        public void TheListedMapIsTheLatticeInOrder()
        {
            Assert.Equal(FindingStatusLattice.All, FindingStatusKeys.All.Select(one => one.Value));
            Assert.Equal(Enumerable.Range(1, FindingStatusLattice.All.Count),
                         FindingStatusKeys.All.Select(one => one.Key));
        }
    }
}
