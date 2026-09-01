using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The set of balls hanging in the view.
    ///
    /// The store is static, because the render plug-in and the screens live in assemblies that
    /// must not know about each other and this is the one thing both touch. So every test here
    /// clears it first and leaves it clear — a leaked marker would show up as a failure in
    /// whichever test happened to run next.
    /// </summary>
    [Collection("marker overlay")]
    public class MarkerOverlayTests : IDisposable
    {
        private static readonly Colour Red = new Colour(0xC8, 0x10, 0x2E);

        /// <summary>Start from nothing.</summary>
        public MarkerOverlayTests() => MarkerOverlay.Clear();

        /// <summary>Leave nothing behind.</summary>
        public void Dispose() => MarkerOverlay.Clear();

        private static SceneMarker At(double x, double y, double z, double radius = 0.15) =>
            new SceneMarker(x, y, z, radius, Red);

        [Fact]
        public void NothingIsUpToStartWith()
        {
            Assert.Equal(0, MarkerOverlay.Count);
            Assert.False(MarkerOverlay.Any);
            Assert.Empty(MarkerOverlay.Shown);
        }

        [Fact]
        public void ShowPutsThemUp()
        {
            Assert.Equal(2, MarkerOverlay.Show(new[] { At(1, 2, 3), At(4, 5, 6) }));

            Assert.Equal(2, MarkerOverlay.Count);
            Assert.True(MarkerOverlay.Any);
            Assert.Equal(4, MarkerOverlay.Shown[1].X);
        }

        [Fact]
        public void ShowReplacesRatherThanAdds()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1), At(2, 2, 2) });
            MarkerOverlay.Show(new[] { At(9, 9, 9) });

            Assert.Equal(1, MarkerOverlay.Count);
            Assert.Equal(9, MarkerOverlay.Shown[0].X);
        }

        [Fact]
        public void ClearTakesThemDown()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1) });
            MarkerOverlay.Clear();

            Assert.False(MarkerOverlay.Any);
            Assert.Empty(MarkerOverlay.Shown);
        }

        [Fact]
        public void NullIsAClearRatherThanACrash()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1) });

            Assert.Equal(0, MarkerOverlay.Show(null));
            Assert.Empty(MarkerOverlay.Shown);
        }

        /// <summary>
        /// The render thread iterates whatever it read, so a null inside the set would be a crash
        /// in the middle of a frame rather than an empty ball.
        /// </summary>
        [Fact]
        public void NullsInsideTheSetAreDropped()
        {
            Assert.Equal(1, MarkerOverlay.Show(new List<SceneMarker> { null!, At(1, 1, 1), null! }));
            Assert.Single(MarkerOverlay.Shown);
        }

        /// <summary>
        /// The set is swapped whole, never mutated: a reader that took the list before the swap
        /// keeps seeing what it took, rather than a list half-way through being refilled.
        /// </summary>
        [Fact]
        public void AReaderKeepsWhatItTook()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1) });

            var taken = MarkerOverlay.Shown;

            MarkerOverlay.Show(new[] { At(2, 2, 2), At(3, 3, 3) });

            Assert.Single(taken);
            Assert.Equal(1, taken[0].X);
        }

        [Fact]
        public void EveryChangeMovesTheVersionOn()
        {
            var before = MarkerOverlay.Version;

            MarkerOverlay.Show(new[] { At(1, 1, 1) });
            MarkerOverlay.Clear();

            Assert.Equal(before + 2, MarkerOverlay.Version);
        }

        [Fact]
        public void AMarkerKeepsWhatItWasGiven()
        {
            var marker = new SceneMarker(1.5, -2.5, 3.5, 0.075, new Colour(1, 2, 3));

            Assert.Equal(1.5, marker.X);
            Assert.Equal(-2.5, marker.Y);
            Assert.Equal(3.5, marker.Z);
            Assert.Equal(0.075, marker.Radius);
            Assert.Equal(2, marker.Colour.G);
        }

        /// <summary>
        /// Hiding used to be a one-way door: the only way back was to pick the same rows and drop
        /// them again, which on a row somebody has scrolled past is no way back at all.
        /// </summary>
        [Fact]
        public void HidingRemembersSoTheMarkersCanComeBack()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1), At(2, 2, 2) });

            Assert.Equal(2, MarkerOverlay.Hide());
            Assert.False(MarkerOverlay.Any);
            Assert.True(MarkerOverlay.CanUnhide);

            Assert.Equal(2, MarkerOverlay.Unhide());
            Assert.Equal(2, MarkerOverlay.Count);
        }

        [Fact]
        public void ThereIsNothingToPutBackBeforeAnythingIsHidden()
        {
            Assert.False(MarkerOverlay.CanUnhide);
            Assert.Equal(0, MarkerOverlay.Unhide());
        }

        [Fact]
        public void HidingNothingRemembersNothing()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1) });
            MarkerOverlay.Hide();

            // A second press with nothing up must not wipe what the first one is holding, or the
            // toggle loses the set between two presses of the same button.
            Assert.Equal(0, MarkerOverlay.Hide());
            Assert.Equal(1, MarkerOverlay.Unhide());
        }

        [Fact]
        public void ClearingForgetsThemForGood()
        {
            MarkerOverlay.Show(new[] { At(1, 1, 1) });
            MarkerOverlay.Hide();
            MarkerOverlay.Clear();

            Assert.False(MarkerOverlay.CanUnhide);
            Assert.Equal(0, MarkerOverlay.Unhide());
        }

        /// <summary>Half by default: readable from across the model, still see-through.</summary>
        [Fact]
        public void MarkersAreDrawnHalfTransparent() =>
            Assert.Equal(0.5, MarkerOverlay.Transparency);
    }
}
