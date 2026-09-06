using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav
{
    /// <summary>
    /// The triangle seam's host side.
    ///
    /// Deliberately thin, and held to it: nothing in this assembly can be run or tested without a
    /// licensed seat, so the weld, the widening, the fallback and the detection all live in Core
    /// where they are asserted against synthetic meshes. What is left here is the COM call, the
    /// handover, and the one judgement that can only be made here — whether this seat can read
    /// primitives at all.
    ///
    /// A per-element failure is a refusal and the caller degrades that element to its bounding box.
    /// An element that carries no triangles is not a refusal: it answers
    /// <see cref="TriangleMesh.Empty"/>, so a grouping node, a line and an annotation do not each
    /// become a box-top walkable surface.
    ///
    /// Main thread only, like everything that touches the document.
    /// </summary>
    public sealed class NavTriangles : ITriangleSource
    {
        /// <summary>
        /// How many refusals in a row, with nothing ever read, before the seat itself is called
        /// unable.
        ///
        /// A seat whose primitive reader is missing refuses every element, and answering "n
        /// elements refused" for a whole federation says the model is the problem when the seat is.
        /// The count is what tells the two apart, and it is reset by the first element that reads:
        /// a model with a run of awkward objects in it is not an unable seat.
        ///
        /// Twenty is well past any run of genuinely awkward elements and still early enough that
        /// the whole-seat answer arrives before a federation sweep has spent real time on it.
        /// </summary>
        public const int RefusalsBeforeTheSeatIsUnable = 20;

        private readonly NavDocument _document;
        private readonly NavGeometry _geometry;

        private int _refusalsInARow;
        private bool _somethingRead;
        private string? _unavailable;

        /// <summary>Wrap a document.</summary>
        /// <param name="document">The open document.</param>
        public NavTriangles(NavDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _geometry = new NavGeometry(document);
        }

        /// <inheritdoc />
        public bool CanReadTriangles => _unavailable == null;

        /// <inheritdoc />
        public string? Unavailable => _unavailable;

        /// <summary>How many elements the host refused, over the life of this source.</summary>
        public int Refused { get; private set; }

        /// <summary>How many elements were read, whether or not they carried triangles.</summary>
        public int ElementsRead { get; private set; }

        /// <inheritdoc />
        public bool TryRead(ElementKey key, out TriangleMesh? mesh) =>
            TryRead(_document.Resolve(key), out mesh);

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> Read(TraversalScope scope,
                                                                       Func<int, bool>? onProgress)
        {
            var walked = 0;

            foreach (var item in _document.Traverse(scope))
            {
                walked++;

                // After the increment, so the count is elements walked and not elements walked
                // before this one: reporting before it starts at nought and never reports the last.
                if (onProgress != null && !onProgress(walked)) yield break;

                // The item the traversal is already holding, rather than its key resolved again
                // through the document's index: that is a dictionary hit per element for something
                // in hand, and it is what NavGeometry.Read takes an item to avoid.
                if (TryRead(item, out var mesh) && mesh != null && mesh.Triangles > 0)
                    yield return new KeyValuePair<ElementKey, TriangleMesh>(item.Key, mesh);
            }
        }

        /// <summary>
        /// One element, counted.
        /// </summary>
        /// <param name="item">The element, or null when it could not be resolved.</param>
        /// <param name="mesh">The triangles, or null when the host refused.</param>
        private bool TryRead(IModelItem? item, out TriangleMesh? mesh)
        {
            if (item == null)
            {
                // A key that resolves to nothing is a refusal like any other: the caller cannot
                // read this element and needs to know rather than to find one silently missing.
                mesh = null;
                Refuse();

                return false;
            }

            var outcome = _geometry.ReadTriangles(item, out mesh);

            if (outcome == GeometryRead.Refused)
            {
                Refuse();

                return false;
            }

            ElementsRead++;
            _somethingRead = true;
            _refusalsInARow = 0;

            return true;
        }

        /// <summary>Count a refusal, and call the seat unable when refusals are all there have been.</summary>
        private void Refuse()
        {
            Refused++;
            _refusalsInARow++;

            if (_somethingRead || _refusalsInARow < RefusalsBeforeTheSeatIsUnable) return;

            _unavailable = "the primitive reader on this seat refused the first "
                + _refusalsInARow.ToString(CultureInfo.InvariantCulture)
                + " elements it was asked for and has read none.";
        }
    }
}
