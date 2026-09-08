using System;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Search
{
    /// <summary>
    /// What can honestly be measured about two elements from what the seam carries.
    ///
    /// The seam gives an axis-aligned box per element and nothing else, so everything here is
    /// derived from boxes. That is enough for the questions a coordinator asks about a pair —
    /// how far apart, at what angle, which one is the riser — and it is not enough for all of
    /// them, so each answer is nullable and the ones that cannot be trusted are not given.
    ///
    /// A direction is only claimed for an element whose box is clearly longer one way than the
    /// other two. A duct running at thirty degrees has a box as wide as it is long, and the
    /// longest side of that box says nothing about which way the duct runs: answering "along X"
    /// there would be a wrong answer rather than a missing one, and a search that reports the
    /// wrong result is worse than one that reports fewer.
    /// </summary>
    public static class ElementGeometry
    {
        /// <summary>
        /// How much longer the longest side must be than the next before it names a direction.
        ///
        /// Three to one. A duct, a pipe, a beam or a column clears it comfortably; a box at any
        /// angle off the axes does not, which is exactly the case that must not be answered.
        /// </summary>
        public const double Slenderness = 3.0;

        /// <summary>An element runs up and down.</summary>
        public const string Vertical = "Vertical";

        /// <summary>An element runs along the plan.</summary>
        public const string Horizontal = "Horizontal";

        /// <summary>
        /// Which way an element runs, as a unit vector, or false when its box will not say.
        /// </summary>
        /// <param name="box">The element's box.</param>
        /// <param name="x">The direction's X.</param>
        /// <param name="y">Its Y.</param>
        /// <param name="z">Its Z.</param>
        /// <returns>True when a direction was found.</returns>
        public static bool Direction(BoundingBox box, out double x, out double y, out double z)
        {
            x = 0; y = 0; z = 0;

            var sx = Math.Abs(box.SizeX);
            var sy = Math.Abs(box.SizeY);
            var sz = Math.Abs(box.SizeZ);

            var longest = Math.Max(sx, Math.Max(sy, sz));

            if (longest <= 0) return false;

            // The next longest side, whichever two are left.
            var next = sx + sy + sz - longest - Math.Min(sx, Math.Min(sy, sz));

            if (longest < next * Slenderness) return false;

            if (longest == sx) x = 1;
            else if (longest == sy) y = 1;
            else z = 1;

            return true;
        }

        /// <summary>
        /// Whether an element runs up and down or along the plan, or null when its box will not
        /// say.
        /// </summary>
        /// <param name="box">The element's box.</param>
        public static string? Orientation(BoundingBox box) =>
            !Direction(box, out _, out _, out var z) ? null
            : z > 0 ? Vertical
            : Horizontal;

        /// <summary>
        /// How far from horizontal an element runs, in degrees, or null when its box will not say.
        ///
        /// Zero for a duct along the plan, ninety for a riser. From a box the answer is only ever
        /// one of the two, which is what a box can honestly report.
        /// </summary>
        /// <param name="box">The element's box.</param>
        public static double? Tilt(BoundingBox box) =>
            !Direction(box, out _, out _, out var z) ? (double?)null : z > 0 ? 90 : 0;

        /// <summary>
        /// The angle between the ways two elements run, in degrees, or null when either box will
        /// not say which way it runs.
        ///
        /// Undirected, so it is always between zero and ninety: a pipe crossing a beam is at
        /// ninety degrees to it whichever end of either you measure from.
        /// </summary>
        /// <param name="a">One element's box.</param>
        /// <param name="b">The other's.</param>
        public static double? Angle(BoundingBox a, BoundingBox b)
        {
            if (!Direction(a, out var ax, out var ay, out var az)) return null;
            if (!Direction(b, out var bx, out var by, out var bz)) return null;

            var dot = Math.Abs(ax * bx + ay * by + az * bz);

            if (dot > 1) dot = 1;

            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        /// <summary>
        /// The clear distance between two boxes: zero when they touch or overlap.
        ///
        /// Between the boxes and not between the solids inside them, which makes it a floor on
        /// the real distance rather than the real distance. A check that measures the solids
        /// declares a column for it and that column is the one to search on; this is what every
        /// other kind of result has.
        /// </summary>
        /// <param name="a">One box.</param>
        /// <param name="b">The other.</param>
        public static double Gap(BoundingBox a, BoundingBox b)
        {
            var x = Apart(a.MinX, a.MaxX, b.MinX, b.MaxX);
            var y = Apart(a.MinY, a.MaxY, b.MinY, b.MaxY);
            var z = Apart(a.MinZ, a.MaxZ, b.MinZ, b.MaxZ);

            return Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>The distance between two boxes' centres.</summary>
        /// <param name="a">One box.</param>
        /// <param name="b">The other.</param>
        public static double Centres(BoundingBox a, BoundingBox b)
        {
            var x = a.CentreX - b.CentreX;
            var y = a.CentreY - b.CentreY;
            var z = a.CentreZ - b.CentreZ;

            return Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>How far apart two spans are on one axis, zero when they overlap.</summary>
        /// <param name="lowA">One span's low end.</param>
        /// <param name="highA">Its high end.</param>
        /// <param name="lowB">The other span's low end.</param>
        /// <param name="highB">Its high end.</param>
        private static double Apart(double lowA, double highA, double lowB, double highB)
        {
            if (lowB > highA) return lowB - highA;
            if (lowA > highB) return lowA - highB;

            return 0;
        }
    }
}
