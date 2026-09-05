using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// Where to stand to see something, in metres.
    ///
    /// Held as eye, target and up rather than as a rotation, because that is what both consumers
    /// want: BCF writes a direction vector, and the host is told to point at a place. Neither has
    /// to know what the other's rotation convention is, which is the part that goes quietly wrong.
    /// </summary>
    public sealed class ViewFraming
    {
        internal ViewFraming(double eyeX, double eyeY, double eyeZ,
                             double targetX, double targetY, double targetZ,
                             double upX, double upY, double upZ, double fieldOfViewDegrees)
        {
            EyeX = eyeX; EyeY = eyeY; EyeZ = eyeZ;
            TargetX = targetX; TargetY = targetY; TargetZ = targetZ;
            UpX = upX; UpY = upY; UpZ = upZ;
            FieldOfViewDegrees = fieldOfViewDegrees;
        }

        /// <summary>Where the camera is, in metres.</summary>
        public double EyeX { get; }

        /// <summary>Where the camera is, in metres.</summary>
        public double EyeY { get; }

        /// <summary>Where the camera is, in metres.</summary>
        public double EyeZ { get; }

        /// <summary>What it looks at, in metres.</summary>
        public double TargetX { get; }

        /// <summary>What it looks at, in metres.</summary>
        public double TargetY { get; }

        /// <summary>What it looks at, in metres.</summary>
        public double TargetZ { get; }

        /// <summary>Which way is up.</summary>
        public double UpX { get; }

        /// <summary>Which way is up.</summary>
        public double UpY { get; }

        /// <summary>Which way is up.</summary>
        public double UpZ { get; }

        /// <summary>Vertical field of view, in degrees.</summary>
        public double FieldOfViewDegrees { get; }

        /// <summary>The view direction, normalised.</summary>
        public (double X, double Y, double Z) Direction
        {
            get
            {
                var (x, y, z) = (TargetX - EyeX, TargetY - EyeY, TargetZ - EyeZ);
                var length = Math.Sqrt(x * x + y * y + z * z);
                return length > 0 ? (x / length, y / length, z / length) : (0, 1, 0);
            }
        }

        /// <summary>How far the eye is from what it is looking at, in metres.</summary>
        public double Distance =>
            Math.Sqrt((TargetX - EyeX) * (TargetX - EyeX)
                      + (TargetY - EyeY) * (TargetY - EyeY)
                      + (TargetZ - EyeZ) * (TargetZ - EyeZ));

        /// <summary>The same view, as a BCF perspective camera.</summary>
        /// <param name="aspectRatio">The viewport's width over its height.</param>
        public BcfCamera ToCamera(double aspectRatio = BcfCamera.DefaultAspectRatio)
        {
            var (dx, dy, dz) = Direction;
            return BcfCamera.Perspective(EyeX, EyeY, EyeZ, dx, dy, dz, UpX, UpY, UpZ,
                                         FieldOfViewDegrees, aspectRatio);
        }

        /// <inheritdoc />
        public override string ToString() =>
            "from " + Point(EyeX, EyeY, EyeZ) + " at " + Point(TargetX, TargetY, TargetZ)
            + ", " + Distance.ToString("0.0", CultureInfo.InvariantCulture) + " m away";

        private static string Point(double x, double y, double z) =>
            "(" + x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + y.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + z.ToString("0.0", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Works out where to stand to see a set of points.
    ///
    /// Not a plan view and not a straight-on elevation. A clash between a duct and a beam is
    /// invisible from directly above and ambiguous from directly in front, and the reason a
    /// viewpoint is worth attaching is that it shows the two things missing each other. So the eye
    /// goes off to one side and slightly above.
    /// </summary>
    public static class BcfFraming
    {
        /// <summary>How far back to stand, as a multiple of what has to fit on screen.</summary>
        public const double Pullback = 2.2;

        /// <summary>The closest the camera will ever be, in metres, however small the thing is.</summary>
        public const double MinimumDistance = 3.0;

        /// <summary>The default vertical field of view. Inside BCF 2.1's 45-60 range, so it survives both schemas.</summary>
        public const double DefaultFieldOfView = 55;

        /// <summary>
        /// Frame a set of points.
        /// </summary>
        /// <param name="points">What has to be visible, in metres. An empty set frames the origin.</param>
        /// <param name="fieldOfViewDegrees">Vertical field of view.</param>
        public static ViewFraming Around(IReadOnlyList<(double X, double Y, double Z)> points,
                                         double fieldOfViewDegrees = DefaultFieldOfView)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
            var any = false;

            foreach (var (x, y, z) in points)
            {
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) continue;

                if (!any)
                {
                    minX = maxX = x; minY = maxY = y; minZ = maxZ = z;
                    any = true;
                    continue;
                }

                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }

            return Box(minX, minY, minZ, maxX, maxY, maxZ, fieldOfViewDegrees);
        }

        /// <summary>Frame a box.</summary>
        /// <param name="box">What has to be visible, in metres.</param>
        /// <param name="fieldOfViewDegrees">Vertical field of view.</param>
        public static ViewFraming Around(BoundingBox box, double fieldOfViewDegrees = DefaultFieldOfView) =>
            Box(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ, fieldOfViewDegrees);

        private static ViewFraming Box(double minX, double minY, double minZ,
                                       double maxX, double maxY, double maxZ, double fov)
        {
            var cx = (minX + maxX) / 2;
            var cy = (minY + maxY) / 2;
            var cz = (minZ + maxZ) / 2;

            var span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            var distance = Math.Max(MinimumDistance, span * Pullback);

            // Down and across, at roughly the angle somebody looking at a ceiling void stands at.
            // Normalised so the distance means what it says.
            const double dx = -0.62, dy = -0.62, dz = -0.48;
            var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            var eyeX = cx - dx / length * distance;
            var eyeY = cy - dy / length * distance;
            var eyeZ = cz - dz / length * distance;

            // Up is world Z, made perpendicular to the view direction. Handing a viewer an up
            // vector that is not perpendicular is how a viewpoint arrives rolled by a few degrees.
            var (ux, uy, uz) = Perpendicular(dx / length, dy / length, dz / length);

            return new ViewFraming(eyeX, eyeY, eyeZ, cx, cy, cz, ux, uy, uz,
                                   Math.Max(1, Math.Min(179, fov)));
        }

        private static (double X, double Y, double Z) Perpendicular(double dx, double dy, double dz)
        {
            // up = normalise(worldZ - direction * (worldZ . direction))
            var dot = dz;
            var ux = -dx * dot;
            var uy = -dy * dot;
            var uz = 1 - dz * dot;

            var length = Math.Sqrt(ux * ux + uy * uy + uz * uz);

            // Looking straight up or straight down leaves no world-Z component to work from.
            return length > 1e-9 ? (ux / length, uy / length, uz / length) : (0, 1, 0);
        }
    }
}
