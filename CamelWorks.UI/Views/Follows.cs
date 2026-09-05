using System.Windows;
using System.Windows.Controls;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A panel that asks for nothing and gives its children whatever it is handed.
    ///
    /// <b>Why anything would want this.</b> In a frozen-pane grid the corner, the band across
    /// the top and the band down the side are one frame, and they have to agree to the pixel or
    /// the headers stop sitting over the cells they name. Exactly one of the three should decide
    /// how big that frame is - the headers, because they are the thing with contents that have
    /// to fit - and the other two should follow.
    ///
    /// A Grid with an auto row and an auto column will do that arithmetic itself, for free and
    /// without a number written down anywhere, on one condition: that the followers do not ask
    /// for room of their own. An auto track is as big as the largest thing in it, so a corner
    /// holding a search box and a legend would quietly become the thing that decides the frame's
    /// height - and the headers, whose size actually matters, would be the ones following.
    ///
    /// Wrapping the corner's contents in this settles who decides. The contents are measured
    /// against all the room in the world, so nothing inside them is squeezed into a smaller
    /// shape than it wants; the panel then reports that it needs none of it, and lays them out
    /// at whatever size the frame turned out to be. The headers decide, the corner follows, and
    /// nothing has to be told a number.
    /// </summary>
    public sealed class Follows : Panel
    {
        /// <summary>Measure the children freely, and ask for nothing.</summary>
        /// <param name="given">What the parent is offering, which is not what this reports back.</param>
        protected override Size MeasureOverride(Size given)
        {
            var free = new Size(double.PositiveInfinity, double.PositiveInfinity);

            foreach (UIElement child in InternalChildren) child.Measure(free);

            return new Size(0, 0);
        }

        /// <summary>Lay every child out over the whole of what was given.</summary>
        /// <param name="given">The room the parent settled on.</param>
        protected override Size ArrangeOverride(Size given)
        {
            foreach (UIElement child in InternalChildren)
                child.Arrange(new Rect(0, 0, given.Width, given.Height));

            return given;
        }
    }
}
