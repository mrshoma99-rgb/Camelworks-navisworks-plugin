using System;
using System.Collections.Generic;
using CamelWorks.Core.Clash;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The trail is the contract between the reader that walks a nested clash tree and the
    /// writer that builds one. If the two spell it differently, a board that groups by section
    /// and material shows one thing before a re-read and another after.
    /// </summary>
    public class ClashTrailTests
    {
        [Fact]
        public void JoinsWithTheOneSeparator()
        {
            Assert.Equal("Level 02 · Steel · Beam",
                         ClashTrail.Join(new[] { "Level 02", "Steel", "Beam" }));
        }

        [Fact]
        public void JoiningOneNameGivesThatName()
        {
            Assert.Equal("Steel", ClashTrail.Join(new[] { "Steel" }));
        }

        [Fact]
        public void JoiningNothingGivesNothing()
        {
            Assert.Equal(string.Empty, ClashTrail.Join(null));
            Assert.Equal(string.Empty, ClashTrail.Join(Array.Empty<string>()));
        }

        [Fact]
        public void EmptySegmentsAreNotSeparators()
        {
            // A band whose value is empty must not leave a dangling separator in the group's
            // name, which is what a document would then carry for ever.
            Assert.Equal("Level 02 · Beam",
                         ClashTrail.Join(new[] { "Level 02", string.Empty, "Beam" }));
        }

        [Fact]
        public void GrowingATrailOneNameAtATimeMatchesJoiningItAtOnce()
        {
            var names = new[] { "Level 02", "Steel", "Beam", "W-Section" };

            var grown = string.Empty;
            foreach (var name in names) grown = ClashTrail.Under(grown, name);

            Assert.Equal(ClashTrail.Join(names), grown);
        }

        [Fact]
        public void TheFirstNameOfATrailIsJustTheName()
        {
            Assert.Equal("Steel", ClashTrail.Under(string.Empty, "Steel"));
            Assert.Equal("Steel", ClashTrail.Under(null, "Steel"));
        }

        [Fact]
        public void AddingNothingLeavesTheTrailAlone()
        {
            Assert.Equal("Level 02", ClashTrail.Under("Level 02", string.Empty));
            Assert.Equal("Level 02", ClashTrail.Under("Level 02", null));
        }

        [Fact]
        public void TwoBranchesEndingInTheSameWordAreTwoTrails()
        {
            // The reason the flat fallback names a group for its whole trail rather than its
            // innermost segment: these two must not collapse into one group called "Beam".
            var steel = ClashTrail.Join(new[] { "Level 02", "Steel", "Beam" });
            var timber = ClashTrail.Join(new[] { "Level 02", "Timber", "Beam" });

            Assert.NotEqual(steel, timber);
        }

        [Fact]
        public void APathCarriesItsTrailAndItsResults()
        {
            var path = new ClashGroupPath(new[] { "Level 02", "Steel" }, new[] { "a", "b" });

            Assert.Equal(new[] { "Level 02", "Steel" }, path.Trail);
            Assert.Equal(new[] { "a", "b" }, path.ResultIds);
        }

        [Fact]
        public void APathWithNothingInIsEmptyRatherThanNull()
        {
            var path = new ClashGroupPath(null!, null!);

            Assert.Empty(path.Trail);
            Assert.Empty(path.ResultIds);
        }

        [Fact]
        public void TheAnswerSaysWhatTheDocumentActuallyDid()
        {
            var flattened = new ClashNesting(true, false, 12, "no groups inside groups");

            Assert.True(flattened.Took);
            Assert.False(flattened.Nested);
            Assert.Equal(12, flattened.Groups);
            Assert.Equal("no groups inside groups", flattened.Problem);

            var nested = new ClashNesting(true, true, 12, null);

            Assert.True(nested.Nested);
            Assert.Null(nested.Problem);
        }
    }
}
