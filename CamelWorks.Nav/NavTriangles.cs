using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav
{
    /// <summary>
    /// The triangle seam's host side.
    ///
    /// Deliberately thin, and held to it: nothing in this assembly can be run or tested without a
    /// licensed seat, so the weld, the widening, the fallback and the counting all live in Core
    /// where they are asserted against synthetic meshes. What is left here is the COM call and the
    /// handover, under fifty lines of code.
    ///
    /// A per-element failure is a refusal, not an exception: <see cref="NavGeometry"/> swallows the
    /// COM fault and answers null, and the caller degrades that element to its bounding box. An
    /// element that simply carries no triangles answers the same way, which overstates refusals on
    /// a model full of grouping nodes and understates nothing.
    ///
    /// Main thread only, like everything that touches the document.
    /// </summary>
    public sealed class NavTriangles : ITriangleSource
    {
        private readonly NavDocument _document;
        private readonly NavGeometry _geometry;

        /// <summary>Wrap a document.</summary>
        /// <param name="document">The open document.</param>
        public NavTriangles(NavDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _geometry = new NavGeometry(document);
        }

        /// <inheritdoc />
        public bool CanReadTriangles => true;

        /// <inheritdoc />
        public string? Unavailable => null;

        /// <inheritdoc />
        public bool TryRead(ElementKey key, out TriangleMesh? mesh)
        {
            var item = _document.Resolve(key);
            var read = item == null ? null : _geometry.Read(item, item.DisplayName);

            mesh = read == null ? null : TriangleMesh.FromSingles(read.Positions, read.Indices).Weld();

            return mesh != null;
        }

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> Read(TraversalScope scope,
                                                                       Func<int, bool>? onProgress)
        {
            var read = 0;

            foreach (var item in _document.Traverse(scope))
            {
                if (onProgress != null && !onProgress(read)) yield break;

                read++;

                if (TryRead(item.Key, out var mesh) && mesh != null)
                    yield return new KeyValuePair<ElementKey, TriangleMesh>(item.Key, mesh);
            }
        }
    }
}
