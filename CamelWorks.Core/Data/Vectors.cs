using System;
using System.Globalization;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// A point or direction in metres.
    ///
    /// Hand-rolled because Core depends on the framework and nothing else — a rule a test asserts —
    /// so the geometry library the clearance engine was written against cannot come with it. The
    /// name is short and unusual on purpose: <c>Point</c>, <c>Vector3D</c> and every other obvious
    /// spelling is a WPF or Navisworks type, and CamelWorks.UI imports both.
    /// </summary>
    public readonly struct Vec3
    {
        /// <summary>Create a vector.</summary>
        /// <param name="x">X, in metres.</param>
        /// <param name="y">Y, in metres.</param>
        /// <param name="z">Z, in metres.</param>
        public Vec3(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }

        /// <summary>X, in metres.</summary>
        public double X { get; }

        /// <summary>Y, in metres.</summary>
        public double Y { get; }

        /// <summary>Z, in metres.</summary>
        public double Z { get; }

        /// <summary>One component by axis: 0 is X, 1 is Y, 2 is Z.</summary>
        /// <param name="axis">Which axis.</param>
        public double Axis(int axis) => axis == 0 ? X : axis == 1 ? Y : Z;

        /// <summary>This vector less <paramref name="other"/>.</summary>
        /// <param name="other">What to subtract.</param>
        public Vec3 Less(Vec3 other) => new Vec3(X - other.X, Y - other.Y, Z - other.Z);

        /// <summary>The cross product of this and <paramref name="other"/>.</summary>
        /// <param name="other">The second vector.</param>
        public Vec3 Cross(Vec3 other) => new Vec3(
            (Y * other.Z) - (Z * other.Y),
            (Z * other.X) - (X * other.Z),
            (X * other.Y) - (Y * other.X));

        /// <summary>The dot product of this and <paramref name="other"/>.</summary>
        /// <param name="other">The second vector.</param>
        public double Dot(Vec3 other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        /// <summary>How long this vector is.</summary>
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        /// <inheritdoc />
        public override string ToString() =>
            "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + ", "
                + Y.ToString("0.###", CultureInfo.InvariantCulture) + ", "
                + Z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// A triangle in plan — three corners in the two axes that are not up.
    ///
    /// What a walkable patch's outline is made of, and what the clearance prism is extruded from.
    /// A patch's bounding rectangle is not its shape: an L-shaped floor plate, an atrium ring and a
    /// corridor around a stair void all have a rectangle covering ground nobody can stand on, and
    /// every column and railing inside it would otherwise be reported as low headroom.
    /// </summary>
    public readonly struct Tri2
    {
        /// <summary>Create a triangle.</summary>
        /// <param name="ax">First corner, across.</param>
        /// <param name="ay">First corner, along.</param>
        /// <param name="bx">Second corner, across.</param>
        /// <param name="by">Second corner, along.</param>
        /// <param name="cx">Third corner, across.</param>
        /// <param name="cy">Third corner, along.</param>
        public Tri2(double ax, double ay, double bx, double by, double cx, double cy)
        {
            Ax = ax; Ay = ay; Bx = bx; By = by; Cx = cx; Cy = cy;
        }

        /// <summary>First corner, across.</summary>
        public double Ax { get; }

        /// <summary>First corner, along.</summary>
        public double Ay { get; }

        /// <summary>Second corner, across.</summary>
        public double Bx { get; }

        /// <summary>Second corner, along.</summary>
        public double By { get; }

        /// <summary>Third corner, across.</summary>
        public double Cx { get; }

        /// <summary>Third corner, along.</summary>
        public double Cy { get; }

        /// <summary>The area it covers, always positive.</summary>
        public double Area =>
            Math.Abs(((Bx - Ax) * (Cy - Ay)) - ((Cx - Ax) * (By - Ay))) / 2.0;

        /// <summary>Whether a point is inside, edges included.</summary>
        /// <param name="x">Across.</param>
        /// <param name="y">Along.</param>
        public bool Holds(double x, double y)
        {
            var d1 = Side(x, y, Ax, Ay, Bx, By);
            var d2 = Side(x, y, Bx, By, Cx, Cy);
            var d3 = Side(x, y, Cx, Cy, Ax, Ay);

            var negative = d1 < 0 || d2 < 0 || d3 < 0;
            var positive = d1 > 0 || d2 > 0 || d3 > 0;

            return !(negative && positive);
        }

        /// <summary>
        /// Whether this triangle shares any area with an axis-aligned rectangle.
        ///
        /// The separating-axis test: two convex shapes miss each other exactly when some axis
        /// separates their projections. Four axes are the rectangle's own two and the triangle's
        /// three edge normals, which is the whole test in plan.
        /// </summary>
        /// <param name="minX">Rectangle, low across.</param>
        /// <param name="minY">Rectangle, low along.</param>
        /// <param name="maxX">Rectangle, high across.</param>
        /// <param name="maxY">Rectangle, high along.</param>
        public bool Overlaps(double minX, double minY, double maxX, double maxY)
        {
            // The rectangle's own axes first: the cheap rejection, and the one that fires most.
            if (Math.Min(Ax, Math.Min(Bx, Cx)) > maxX) return false;
            if (Math.Max(Ax, Math.Max(Bx, Cx)) < minX) return false;
            if (Math.Min(Ay, Math.Min(By, Cy)) > maxY) return false;
            if (Math.Max(Ay, Math.Max(By, Cy)) < minY) return false;

            return !Separates(Ax, Ay, Bx, By, minX, minY, maxX, maxY)
                && !Separates(Bx, By, Cx, Cy, minX, minY, maxX, maxY)
                && !Separates(Cx, Cy, Ax, Ay, minX, minY, maxX, maxY);
        }

        /// <summary>Whether the normal of one triangle edge separates it from the rectangle.</summary>
        private bool Separates(double x0, double y0, double x1, double y1,
                               double minX, double minY, double maxX, double maxY)
        {
            var nx = y1 - y0;
            var ny = x0 - x1;

            if (Math.Abs(nx) < 1e-12 && Math.Abs(ny) < 1e-12) return false;

            var triLow = Math.Min((nx * Ax) + (ny * Ay),
                         Math.Min((nx * Bx) + (ny * By), (nx * Cx) + (ny * Cy)));
            var triHigh = Math.Max((nx * Ax) + (ny * Ay),
                          Math.Max((nx * Bx) + (ny * By), (nx * Cx) + (ny * Cy)));

            var boxLow = double.MaxValue;
            var boxHigh = double.MinValue;

            for (var corner = 0; corner < 4; corner++)
            {
                var value = (nx * (corner < 2 ? minX : maxX))
                          + (ny * (corner % 2 == 0 ? minY : maxY));

                if (value < boxLow) boxLow = value;
                if (value > boxHigh) boxHigh = value;
            }

            return triHigh < boxLow || boxHigh < triLow;
        }

        private static double Side(double x, double y, double x0, double y0, double x1, double y1) =>
            ((x - x1) * (y0 - y1)) - ((x0 - x1) * (y - y1));

        /// <inheritdoc />
        public override string ToString() =>
            "(" + Ax.ToString("0.###", CultureInfo.InvariantCulture) + ","
                + Ay.ToString("0.###", CultureInfo.InvariantCulture) + ") ("
                + Bx.ToString("0.###", CultureInfo.InvariantCulture) + ","
                + By.ToString("0.###", CultureInfo.InvariantCulture) + ") ("
                + Cx.ToString("0.###", CultureInfo.InvariantCulture) + ","
                + Cy.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// A range along one axis, in metres — the vertical band a clearance prism occupies, and the
    /// elevation band a patch's triangles fall in.
    /// </summary>
    public readonly struct Span1
    {
        /// <summary>Create a range. The two ends may be given either way round.</summary>
        /// <param name="from">One end.</param>
        /// <param name="to">The other end.</param>
        public Span1(double from, double to)
        {
            Low = from < to ? from : to;
            High = from < to ? to : from;
        }

        /// <summary>The low end.</summary>
        public double Low { get; }

        /// <summary>The high end.</summary>
        public double High { get; }

        /// <summary>How long the range is.</summary>
        public double Length => High - Low;

        /// <summary>Whether a value is in the range, ends included.</summary>
        /// <param name="value">The value.</param>
        public bool Holds(double value) => value >= Low && value <= High;

        /// <summary>Whether two ranges share any length, touching included.</summary>
        /// <param name="other">The other range.</param>
        public bool Overlaps(Span1 other) => Low <= other.High && High >= other.Low;

        /// <inheritdoc />
        public override string ToString() =>
            Low.ToString("0.###", CultureInfo.InvariantCulture) + ".."
            + High.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A box being built from points, seeded from the first one.
    ///
    /// There is no empty constructor, and that is the whole point of the type. The engine this was
    /// ported from builds two of its boxes by growing an empty one, and an empty box is a point at
    /// the world origin: on a site-coordinate model every such box reaches from the origin to the
    /// geometry, the containment test then answers yes for everything in the quadrant between, and
    /// the check reports elements kilometres from the surface they are supposedly over. Requiring a
    /// seed point makes that mistake impossible to write.
    /// </summary>
    public sealed class BoxFit
    {
        private double _minX, _minY, _minZ, _maxX, _maxY, _maxZ;

        /// <summary>Start a box at one point.</summary>
        /// <param name="x">X, in metres.</param>
        /// <param name="y">Y, in metres.</param>
        /// <param name="z">Z, in metres.</param>
        public BoxFit(double x, double y, double z)
        {
            _minX = _maxX = x;
            _minY = _maxY = y;
            _minZ = _maxZ = z;
        }

        /// <summary>Start a box at one point.</summary>
        /// <param name="point">The seed point.</param>
        public BoxFit(Vec3 point)
            : this(point.X, point.Y, point.Z)
        {
        }

        /// <summary>Grow the box to hold a point.</summary>
        /// <param name="x">X, in metres.</param>
        /// <param name="y">Y, in metres.</param>
        /// <param name="z">Z, in metres.</param>
        public void Add(double x, double y, double z)
        {
            if (x < _minX) _minX = x;
            if (y < _minY) _minY = y;
            if (z < _minZ) _minZ = z;
            if (x > _maxX) _maxX = x;
            if (y > _maxY) _maxY = y;
            if (z > _maxZ) _maxZ = z;
        }

        /// <summary>Grow the box to hold a point.</summary>
        /// <param name="point">The point.</param>
        public void Add(Vec3 point) => Add(point.X, point.Y, point.Z);

        /// <summary>The box as it stands.</summary>
        public BoundingBox Box => new BoundingBox(_minX, _minY, _minZ, _maxX, _maxY, _maxZ);

        /// <inheritdoc />
        public override string ToString() => Box.ToString();
    }
}
