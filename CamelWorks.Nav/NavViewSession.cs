using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav
{
    /// <summary>
    /// Colour, transparency, visibility, camera and sectioning.
    ///
    /// The host's two override layers are not symmetric, and the asymmetry drives the whole design
    /// above this seam. The permanent layer can be reset for a given collection and read back; the
    /// temporary layer can only be reset globally and cannot be read at all. That is why the
    /// Appearance Manager's stack lives on the permanent layer, and why this class refuses a
    /// per-element clear on the temporary one rather than pretending it worked.
    ///
    /// <b>Main thread only.</b>
    /// </summary>
    public sealed class NavViewSession : IViewSession
    {
        private readonly NavDocument _document;

        /// <summary>Wrap a document.</summary>
        public NavViewSession(NavDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <inheritdoc />
        public OverrideLayer Layer { get; set; } = OverrideLayer.Permanent;

        private Document Host => _document.Document;

        /// <inheritdoc />
        public void SetColour(IReadOnlyList<ElementKey> keys, Colour colour)
        {
            var items = _document.ItemsFor(keys);
            if (items.Count == 0) return;

            var value = NavValues.FromColour(colour);

            if (Layer == OverrideLayer.Permanent) Host.Models.OverridePermanentColor(items, value);
            else Host.Models.OverrideTemporaryColor(items, value);
        }

        /// <inheritdoc />
        public void SetTransparency(IReadOnlyList<ElementKey> keys, double transparency)
        {
            var items = _document.ItemsFor(keys);
            if (items.Count == 0) return;

            var value = Math.Max(0, Math.Min(1, transparency));

            if (Layer == OverrideLayer.Permanent) Host.Models.OverridePermanentTransparency(items, value);
            else Host.Models.OverrideTemporaryTransparency(items, value);
        }

        /// <inheritdoc />
        public void SetVisible(IReadOnlyList<ElementKey> keys, bool visible)
        {
            var items = _document.ItemsFor(keys);
            if (items.Count == 0) return;

            Host.Models.SetHidden(items, !visible);
        }

        /// <summary>
        /// Show these elements and nothing else.
        ///
        /// <b>The host does the walking.</b> This used to recurse the whole federation in managed
        /// code, deciding branch by branch what to hide - which is a walk of every element in the
        /// document on the UI thread, paid every time somebody pressed Isolate, and a recursion
        /// deep enough to be a risk of its own on a tall assembly tree.
        ///
        /// The host can do the whole thing in three calls: hide everything, then bring back what
        /// was asked for. <c>RootItemDescendantsAndSelf</c> is a lazy native collection and
        /// <c>SetHidden</c> takes it whole, so nothing is enumerated into managed memory and the
        /// cost stops depending on the size of the federation.
        ///
        /// Ancestors and descendants come back with the elements themselves, because hidden state
        /// is per item: an element inside a hidden parent is not drawn, and an assembly whose
        /// children are all hidden is an empty box.
        /// </summary>
        /// <param name="keys">What to leave visible.</param>
        public void Isolate(IReadOnlyList<ElementKey> keys) => Keep(_document.ItemsFor(keys));

        /// <inheritdoc />
        public void IsolateSelection()
        {
            var items = new ModelItemCollection();

            foreach (ModelItem item in Host.CurrentSelection.SelectedItems) items.Add(item);

            Keep(items);
        }

        /// <summary>
        /// Hide everything but these and the branch they hang from.
        ///
        /// <b>The host works out what "everything else" is.</b> <c>ModelItemCollection.Invert</c>
        /// is the complement, computed natively in one call - the same thing the ribbon's own Hide
        /// Unselected does, and the reason that command is instant on a federation this would
        /// otherwise take minutes to walk.
        ///
        /// Two versions of this were wrong before it, and both are worth naming because the
        /// mistake is easy to make twice:
        ///
        /// <list type="bullet">
        /// <item><description>Hiding <c>RootItemDescendantsAndSelf</c> is correct and hands the
        /// host every item in the document one at a time, on the UI thread, with nothing on the
        /// screen to say so. That was Isolate freezing.</description></item>
        /// <item><description>Hiding only the model roots is fast and does nothing. Hidden is
        /// inherited, so the roots have to be un-hidden again to show the branch - and un-hiding a
        /// root puts its whole model back, because nothing under it was ever marked.</description>
        /// </item>
        /// </list>
        ///
        /// The ancestors are not decoration. An element inside a hidden parent is not drawn, so
        /// the chain from the root down to the item has to stay out of what gets hidden or the
        /// thing being isolated goes with it. Descendants for the same reason in reverse: an
        /// assembly is isolated with what it is made of.
        /// </summary>
        /// <param name="items">What to keep visible.</param>
        private void Keep(ModelItemCollection items)
        {
            if (items == null || items.Count == 0) return;

            var keep = new ModelItemCollection();

            foreach (ModelItem item in items)
            {
                keep.Add(item);

                foreach (var ancestor in item.Ancestors) keep.Add(ancestor);
                foreach (var descendant in item.Descendants) keep.Add(descendant);
            }

            var hide = new ModelItemCollection();

            hide.CopyFrom(keep);
            hide.Invert(Host);

            Host.Models.ResetAllHidden();
            Host.Models.SetHidden(hide, true);
        }

        /// <inheritdoc />
        public void ShowAll() => Host.Models.ResetAllHidden();

        /// <inheritdoc />
        public void ClearOverrides(IReadOnlyList<ElementKey> keys)
        {
            if (Layer == OverrideLayer.Temporary)
                throw new NotSupportedException(
                    "the host's temporary override layer can only be reset globally, so a per-element "
                    + "clear on it is not possible; use the permanent layer, or ClearAllOverrides");

            var items = _document.ItemsFor(keys);
            if (items.Count == 0) return;

            Host.Models.ResetPermanentMaterials(items);
            Host.Models.SetHidden(items, false);
        }

        /// <inheritdoc />
        public void ClearAllOverrides()
        {
            if (Layer == OverrideLayer.Temporary)
            {
                Host.Models.ResetAllTemporaryMaterials();
                return;
            }

            Host.Models.ResetAllPermanentMaterials();
            Host.Models.ResetAllHidden();
        }

        /// <summary>
        /// What the document is actually in, for the elements given.
        /// </summary>
        /// <param name="keys">The elements to read.</param>
        /// <exception cref="NotSupportedException">
        /// The temporary layer is selected. The host cannot read it back, and returning the
        /// permanent state while claiming to describe the temporary one would make the Appearance
        /// Manager quietly wrong about everything it showed.
        /// </exception>
        public IReadOnlyList<AppearanceState> ReadAppearance(IReadOnlyList<ElementKey> keys)
        {
            if (Layer == OverrideLayer.Temporary)
                throw new NotSupportedException(
                    "the host cannot report its temporary override layer; read the permanent one instead");

            var states = new List<AppearanceState>();
            if (keys == null) return states;

            foreach (var key in keys)
            {
                var item = _document.Resolve(key) as NavModelItem;
                if (item == null) continue;

                states.Add(Read(key, item.Item));
            }

            return states;
        }

        /// <inheritdoc />
        public void ZoomToSelection(double marginMetres)
        {
            _ = marginMetres;

            try
            {
                if (Host.CurrentSelection.SelectedItems.Count == 0) return;

                // Nothing to borrow and nothing to put back: the selection the host focuses on is
                // already the one the caller wanted framed.
                Host.ActiveView.FocusOnCurrentSelection();
            }
            catch (Exception)
            {
                // A view that will not focus leaves the camera where it is, which is what it did
                // before anybody pressed anything.
            }
        }

        /// <inheritdoc />
        ///
        /// <remarks>
        /// The markers themselves are drawn by the render plug-in, which the host calls once a
        /// frame with a <c>Graphics</c> to draw into. All that happens here is the swap of the set
        /// it reads and a nudge to redraw - so dropping a marker costs nothing beyond the frame it
        /// appears on, and there is no geometry, no file and no re-index behind it.
        /// </remarks>
        public void ShowMarkers(IReadOnlyList<SceneMarker> markers)
        {
            MarkerOverlay.Show(markers);
            Redraw();
        }

        /// <inheritdoc />
        public int HideMarkers()
        {
            var down = MarkerOverlay.Hide();
            Redraw();
            return down;
        }

        /// <inheritdoc />
        public int UnhideMarkers()
        {
            var back = MarkerOverlay.Unhide();
            Redraw();
            return back;
        }

        /// <inheritdoc />
        public bool CanUnhideMarkers => MarkerOverlay.CanUnhide;

        /// <inheritdoc />
        public void ClearMarkers()
        {
            MarkerOverlay.Clear();
            Redraw();
        }

        /// <inheritdoc />
        public bool CanMark => true;

        /// <summary>
        /// Ask for a frame.
        ///
        /// <c>All</c> rather than one or the other because the markers are drawn from
        /// <c>Render</c> and a host that has only been asked for an overlay pass may leave the
        /// model pass alone - which reads as a marker that never appeared.
        /// </summary>
        private void Redraw()
        {
            try
            {
                Host.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch (Exception)
            {
                // No active view, or one mid-teardown. The set is already swapped, so the next
                // frame the host draws for its own reasons shows it.
            }
        }

        /// <inheritdoc />
        public void ZoomTo(IReadOnlyList<ElementKey> keys, double marginMetres)
        {
            var items = _document.ItemsFor(keys);
            if (items.Count == 0) return;

            var restore = new ModelItemCollection();
            foreach (ModelItem item in Host.CurrentSelection.SelectedItems) restore.Add(item);

            try
            {
                // The host focuses on the current selection, so this borrows it — and puts it back.
                // A tool that quietly changed what was selected would throw away whatever the
                // coordinator had picked, every single time they clicked a row.
                Host.CurrentSelection.CopyFrom(items);
                Host.ActiveView.FocusOnCurrentSelection();
            }
            finally
            {
                Host.CurrentSelection.CopyFrom(restore);
            }
        }

        /// <inheritdoc />
        public void LookAt(double x, double y, double z, double backMetres)
        {
            try
            {
                var scale = NavUnits.UnitsPerMetre(_document.Document.Units);
                var back = backMetres * scale;

                var target = new Point3D(x, y, z);
                var viewpoint = Host.CurrentViewpoint.CreateCopy();

                // Stood back along -Y and up in Z, which is the three-quarter view a coordinator
                // draws on a whiteboard when they say "look at this junction". Keeping the current
                // viewpoint's other settings - the render mode, the lighting, whether it is
                // orthographic - because this is a move of the camera, not a new view.
                viewpoint.Position = new Point3D(target.X, target.Y - back, target.Z + back);
                viewpoint.Rotation = new Rotation3D();
                viewpoint.PointAt(target);

                Host.CurrentViewpoint.CopyFrom(viewpoint);
            }
            catch (Exception)
            {
                // A host that will not take the viewpoint leaves the camera where it was. The
                // caller has already selected the elements, so the press was not wasted.
            }
        }

        /// <inheritdoc />
        public void FrameBox(CamelWorks.Core.Abstractions.BoundingBox box)
        {
            var scale = NavUnits.UnitsPerMetre(_document.Document.Units);

            // The host's own ZoomBox rather than a computed camera. It knows about the current
            // aspect ratio, the projection and the near plane, and getting any of those wrong
            // yields a view that is technically pointed the right way and shows nothing.
            var viewpoint = Host.CurrentViewpoint.CreateCopy();

            viewpoint.ZoomBox(new BoundingBox3D(
                new Point3D(box.MinX * scale, box.MinY * scale, box.MinZ * scale),
                new Point3D(box.MaxX * scale, box.MaxY * scale, box.MaxZ * scale)));

            Host.CurrentViewpoint.CopyFrom(viewpoint);
        }

        /// <inheritdoc />
        public bool LookDown(CamelWorks.Core.Abstractions.BoundingBox box)
        {
            try
            {
                var scale = NavUnits.UnitsPerMetre(_document.Document.Units);
                var viewpoint = Host.CurrentViewpoint.CreateCopy();

                // Projection and direction before the fit, in that order: ZoomBox works out the
                // extents the camera needs, and it can only do that once it knows which way the
                // camera is facing and whether the frustum converges.
                viewpoint.Projection = ViewpointProjection.Orthographic;
                viewpoint.AlignDirection(new Vector3D(0, 0, -1));

                viewpoint.ZoomBox(new BoundingBox3D(
                    new Point3D(box.MinX * scale, box.MinY * scale, box.MinZ * scale),
                    new Point3D(box.MaxX * scale, box.MaxY * scale, box.MaxZ * scale)));

                Host.CurrentViewpoint.CopyFrom(viewpoint);

                return true;
            }
            catch (Exception)
            {
                // A 2D sheet view has no camera to rotate. The report goes out without the plan.
                return false;
            }
        }

        /// <inheritdoc />
        public IDisposable BorrowCamera() => new BorrowedCamera(Host);

        private sealed class BorrowedCamera : IDisposable
        {
            private readonly Document _document;
            private readonly Viewpoint _saved;

            internal BorrowedCamera(Document document)
            {
                _document = document;
                _saved = document.CurrentViewpoint.CreateCopy();
            }

            public void Dispose()
            {
                try
                {
                    _document.CurrentViewpoint.CopyFrom(_saved);
                }
                catch (Exception)
                {
                    // Disposed inside a finally after something else already failed. Throwing here
                    // would replace the real error with one about a camera.
                }
            }
        }

        /// <inheritdoc />
        public byte[]? Capture(int width, int height)
        {
            if (width < 1 || height < 1) return null;

            try
            {
                using (var image = Host.GenerateImage(ImageGenerationStyle.Scene, width, height, false))
                using (var buffer = new System.IO.MemoryStream())
                {
                    if (image == null) return null;

                    image.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
                    return buffer.ToArray();
                }
            }
            catch (Exception)
            {
                // Rendering off-screen is not available in every configuration - a machine with no
                // GPU context, a document mid-load. The contract is null, and the caller writes the
                // file without images rather than not at all.
                return null;
            }
        }

        /// <inheritdoc />
        public void SetSectionBox(CamelWorks.Core.Abstractions.BoundingBox box, double marginMetres)
        {
            // The seam states margins in metres; the box arrives in model units. Without the
            // conversion a half-metre margin around a millimetre model is half a millimetre.
            var margin = Math.Max(0, marginMetres) * NavUnits.UnitsPerMetre(_document.Document.Units);

            var extents = new BoundingBox3D(
                new Point3D(box.MinX - margin, box.MinY - margin, box.MinZ - margin),
                new Point3D(box.MaxX + margin, box.MaxY + margin, box.MaxZ + margin));

            var viewer = Viewer();
            if (viewer == null) return;

            var planes = viewer.EditClipPlane();

#if NAV2024
            planes.SetBox(extents);
            planes.SetEnabled(true);
#else
            planes.Box = extents;
            planes.Enabled = true;
#endif
        }

        /// <inheritdoc />
        public void SetSectionBoxEnabled(bool enabled)
        {
            // Nothing is remembered here. The set keeps its own box when it is switched off, so
            // asking the host to switch it back on restores what was there - which is the whole
            // contract, and the JSON version had to hoard a string to fake it.
            var viewer = Viewer();
            if (viewer == null) return;

            var planes = viewer.EditClipPlane();

#if NAV2024
            planes.SetEnabled(enabled);
#else
            planes.Enabled = enabled;
#endif
        }

        /// <inheritdoc />
        public bool IsSectionBoxEnabled
        {
            get
            {
                // Read from the host rather than remembered here: the user can switch sectioning
                // off with the host's own button, and a toggle that trusted its own memory would
                // then do the opposite of what the button says.
                var viewer = Viewer();
                if (viewer == null) return false;

                var planes = viewer.GetClipPlaneSet();

#if NAV2024
                return planes != null && planes.IsEnabled();
#else
                return planes != null && planes.Enabled;
#endif
            }
        }

        /// <summary>
        /// The viewer that owns the document's clip planes, or null where there is not one.
        ///
        /// <b>Why not the JSON.</b> <c>View.SetClippingPlanes</c> takes a string it calls
        /// <c>jsonClipPlaneSet</c>, and a payload it dislikes throws
        /// <c>Failed to set clipping planes</c> out of the button - which is what shipped, because
        /// the schema was written from the outside and the two keys it leaned on, "OrientedBox"
        /// and "Box", do not occur anywhere in the assembly. The format is produced by native code
        /// (<c>GetClippingPlanesAsJsonClipPlaneSet</c>), is undocumented, and there is no reason to
        /// believe it is stable across the four supported years.
        ///
        /// The clip-plane set is the same state as a typed object, on the same assembly. A wrong
        /// call there does not compile, which is the only kind of check worth having for something
        /// no test on a build machine can exercise.
        ///
        /// <b>Why the one conditional.</b> This path is identical in all four years - and it is
        /// the only one that is. The set itself was <c>Interop.LcOaClipPlaneSet</c> with
        /// <c>SetBox</c> and <c>SetEnabled</c> in 2024, and became <c>Api.ClipPlaneSet</c> with
        /// <c>Box</c> and <c>Enabled</c> properties in 2025. The type is never named here, only
        /// inferred, so the difference is three lines of member style rather than a second copy of
        /// the method. <c>Viewpoint</c> offers the same set - as <c>InternalClipPlanes</c> in 2024
        /// and <c>ClipPlanes</c> after - which would have been two differences instead of one.
        /// </summary>
        private static Autodesk.Navisworks.Api.Interop.LcOaViewer? Viewer()
        {
            var state = Autodesk.Navisworks.Api.Interop.LcOpState.GetActiveInstance();

            return state == null ? null : state.GetViewer();
        }



        private static AppearanceState Read(ElementKey key, ModelItem item)
        {
            Colour? colour = null;
            double? transparency = null;
            var foreign = false;

            var geometry = item.Geometry;
            if (geometry != null)
            {
                var permanent = geometry.PermanentColor;
                var original = geometry.OriginalColor;

                // The host has no flag for "this element carries an override". The only signal is
                // that the permanent colour differs from the original one, so that is what is used
                // — and whether CamelWorks authored it is decided above this seam, by comparing
                // against the layer stack. Here it is reported as foreign, and the Appearance
                // Manager corrects it for the elements its own layers claim.
                if (!Same(permanent, original))
                {
                    colour = NavValues.ToColour(permanent);
                    foreign = true;
                }

                if (geometry.PermanentTransparency > 0)
                {
                    transparency = geometry.PermanentTransparency;
                    foreign = true;
                }
            }

            return new AppearanceState(key, colour, transparency, item.IsHidden, foreign);
        }

        private static bool Same(Autodesk.Navisworks.Api.Color a, Autodesk.Navisworks.Api.Color b)
        {
            const double tolerance = 1.0 / 512.0;

            return Math.Abs(a.R - b.R) < tolerance
                && Math.Abs(a.G - b.G) < tolerance
                && Math.Abs(a.B - b.B) < tolerance;
        }
    }
}
