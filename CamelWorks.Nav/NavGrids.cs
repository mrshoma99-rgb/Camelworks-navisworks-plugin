using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;

namespace CamelWorks.Nav
{
    /// <summary>
    /// The coordination grid, when the federation has one.
    ///
    /// A grid system is the strongest answer to "where is this" that a model can give. Its level
    /// names are the ones printed on the drawings, its line labels are what a site engineer says
    /// out loud, and nobody has to be persuaded to adopt it because every discipline already set
    /// out against it. "Grid C-4, Level 02" locates a clash; "element 41823 at Z=7.412" does not.
    ///
    /// Navisworks has carried grids since it learnt to read Revit files and has never surfaced them
    /// as anything a coordinator can group, filter or report by. They sit in the API, one call
    /// away, doing nothing.
    ///
    /// Everything here tolerates a document with no grid, because most do.
    /// </summary>
    public sealed class NavGrids
    {
        private readonly NavDocument _document;
        private readonly double _scale;

        /// <summary>Wrap a document's grids.</summary>
        /// <param name="document">The open document.</param>
        public NavGrids(NavDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _scale = NavUnits.MetresPerUnit(document.Document.Units);
        }

        /// <summary>
        /// The system to work against: the active one, else the first with levels, else the first.
        ///
        /// A federation with two grid systems is one where somebody linked a second building, and
        /// the host's own active system is the one the user is looking at. Picking a different one
        /// silently would put a grid reference on a report that disagrees with the screen.
        /// </summary>
        public GridSystem? System
        {
            get
            {
                try
                {
                    var grids = _document.Document.Grids;

                    if (grids.ActiveSystem != null) return grids.ActiveSystem;

                    var systems = grids.Systems;
                    if (systems == null || systems.Count == 0) return null;

                    return systems.FirstOrDefault(s => s.Levels != null && s.Levels.Count > 0)
                           ?? systems[0];
                }
                catch (Exception)
                {
                    // A document with no grids at all, or one whose grid the host cannot open.
                    // Neither is an error and neither is worth a message: the caller falls through
                    // to the next source in the chain.
                    return null;
                }
            }
        }

        /// <summary>True when there is a grid with levels on it.</summary>
        public bool HasLevels => Levels().Count > 0;

        /// <summary>What the grid system is called, or null when there is not one.</summary>
        public string? Name
        {
            get
            {
                try { return System?.DisplayName; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>
        /// The grid's levels as name and elevation pairs, in metres.
        /// </summary>
        /// <returns>Empty when the document has no grid, which is the common case.</returns>
        public IReadOnlyList<KeyValuePair<string, double>> Levels()
        {
            var levels = new List<KeyValuePair<string, double>>();

            try
            {
                var system = System;
                if (system?.Levels == null) return levels;

                foreach (var level in system.Levels)
                {
                    var name = level.DisplayName;

                    // A level with no name is one nothing can be filed under, and calling it
                    // "Level 3" because it is third in the list is the kind of invented fact this
                    // product refuses to produce.
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    levels.Add(new KeyValuePair<string, double>(name, level.Elevation * _scale));
                }
            }
            catch (Exception)
            {
                // Half a list is worse than none: a set of levels missing its top two would put
                // every element up there on the highest level that did survive.
                return Array.Empty<KeyValuePair<string, double>>();
            }

            return levels;
        }

        /// <summary>
        /// The levels as CamelWorks understands them, or <see cref="LevelSet.Empty"/>.
        ///
        /// Named Derive and not LevelSet: a member named after a type this file imports makes the
        /// type unreachable in the whole class, and the compile error lands a long way from the
        /// line that caused it.
        /// </summary>
        public LevelSet Derive()
        {
            var levels = Levels();

            return levels.Count == 0 ? LevelSet.Empty : LevelSet.FromGrid(levels);
        }

        /// <summary>
        /// Where a point is, in grid terms: two line labels and a level.
        /// </summary>
        /// <param name="x">Easting, in model units.</param>
        /// <param name="y">Northing, in model units.</param>
        /// <param name="z">Elevation, in model units.</param>
        /// <returns>Something like "C-4 : Level 02", or null when there is no grid.</returns>
        public string? Reference(double x, double y, double z)
        {
            try
            {
                var system = System;
                if (system == null) return null;

                var found = system.ClosestIntersection(new Point3D(x, y, z));
                var name = found?.DisplayName;

                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch (Exception)
            {
                // Asked about a point outside anything the grid covers, or a grid the host will not
                // resolve. One element without a reference is not a reason to abandon the run.
                return null;
            }
        }

        /// <summary>
        /// Where an element is, in grid terms, taken from the centre of its bounding box.
        ///
        /// The centre rather than a corner: a duct running through grid C is a duct at C, and the
        /// corner of its box is wherever the run happens to start.
        /// </summary>
        /// <param name="item">The element.</param>
        public string? Reference(IModelItem item)
        {
            if (item == null || !item.HasGeometry) return null;

            var box = item.Bounds;

            return Reference(box.CentreX, box.CentreY, box.CentreZ);
        }
    }
}
