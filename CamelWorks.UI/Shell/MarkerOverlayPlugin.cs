using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// The markers dropped on a clash, drawn straight into the view.
    ///
    /// A render plug-in rather than geometry. A marker answers "where is this one" for the few
    /// seconds it is being looked at. Building a mesh and appending it as a file makes a model,
    /// which joins the selection tree, has to be unloaded by hand, costs a re-index and persists.
    /// The host calls this once a frame with a <see cref="Graphics"/> to draw into, and what it
    /// draws is gone as soon as the set is cleared.
    ///
    /// It holds no state of its own. Everything it draws comes from <see cref="MarkerOverlay"/>,
    /// which lives in Core, because the screens that drop markers must not know this assembly exists
    /// and this class must not know theirs. The store is the only thing both ends touch.
    ///
    /// Called on the render thread, so it reads one array reference and iterates it, and the store
    /// swaps the array whole rather than mutating it. Nothing here allocates, throws or takes a
    /// lock: a frame is sixteen milliseconds.
    /// </summary>
    [Plugin("CamelWorks.Markers", "CamelWorks",
        DisplayName = "CamelWorks markers",
        ToolTip = "Draws the marker spheres CamelWorks drops at clash points")]
    public class CamelWorksMarkers : RenderPlugin
    {
        /// <summary>Draw whatever is up, in the model's own space.</summary>
        /// <param name="view">The view being drawn. Not needed: the markers are in model space.</param>
        /// <param name="graphics">What to draw into.</param>
        public override void Render(View view, Graphics graphics)
        {
            _ = view;

            var markers = MarkerOverlay.Shown;
            if (markers.Count == 0 || graphics == null) return;

            var transparency = MarkerOverlay.Transparency;

            for (var i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                if (marker == null || marker.Radius <= 0) continue;

                graphics.Color(
                    Color.FromByteRGB(marker.Colour.R, marker.Colour.G, marker.Colour.B),
                    transparency);

                graphics.Sphere(new Point3D(marker.X, marker.Y, marker.Z), marker.Radius);
            }
        }
    }
}
