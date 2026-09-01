using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// The balls the coordinator drops on a clash, drawn straight into the view.
    ///
    /// <b>Why a render plug-in and not geometry.</b> A marker answers "where is this one?" for the
    /// ten seconds somebody is looking at it. Building a mesh and appending it as a file answers a
    /// different question: it makes a model, which joins the selection tree, has to be unloaded by
    /// hand, costs a re-index, and is still there next week. The host will call this once a frame
    /// with a <see cref="Graphics"/> to draw into, for nothing, and what it draws is gone the
    /// moment the set is cleared. That is what a marker wants to be.
    ///
    /// <b>It holds no state of its own.</b> Everything it draws comes from
    /// <see cref="MarkerOverlay"/>, which lives in Core - because the screens that drop markers
    /// must not know this assembly exists, and this class must not know theirs. The store is the
    /// only thing both ends touch.
    ///
    /// <b>Called on the render thread.</b> So it reads one array reference and iterates it, and
    /// the store swaps the array whole rather than mutating it. Nothing here allocates, throws or
    /// takes a lock: a frame is sixteen milliseconds and this is not the thing that should spend
    /// them.
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
