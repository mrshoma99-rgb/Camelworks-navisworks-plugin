using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Report
{
    /// <summary>
    /// One plan of the whole site with every group marked on it.
    ///
    /// A report full of close-up renders answers "what does this look like" and never answers
    /// "where is it". A single top-down plan with numbered markers answers the second question for
    /// forty issues at once, and it is the page a reader actually turns back to.
    ///
    /// The maths is here rather than in the host adapter because it has to be testable: a marker
    /// half a metre out on a page is invisible in review and wrong on site, and no amount of
    /// looking at a rendered PNG proves the projection is right.
    ///
    /// <b>The honest limitation.</b> On a 400 m site rendered 1,600 pixels wide, one pixel is a
    /// quarter of a metre — so a 300 mm clash is a marker sitting on a point the page cannot
    /// resolve. The plan says so in words rather than implying a precision it does not have, and
    /// the setting-out coordinates beside each group are the number to work from.
    /// </summary>
    public static class KeyPlan
    {
        /// <summary>How much of the image is left as margin around the site, each side.</summary>
        public const double Margin = 0.04;

        /// <summary>
        /// The box to frame so that a top-down render of it fills the image exactly.
        ///
        /// Padded to the image's aspect ratio on whichever axis is short, because the host fits a
        /// box into the frame and leaves letterboxing on the other axis — and a projection that
        /// ignored the letterboxing would put every marker in the wrong place by exactly the width
        /// of the bars.
        /// </summary>
        /// <param name="site">What has to be visible, in metres.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        public static BoundingBox Frame(BoundingBox site, int width, int height)
        {
            if (width <= 0 || height <= 0) return site;

            var aspect = (double)width / height;

            var midX = (site.MinX + site.MaxX) / 2;
            var midY = (site.MinY + site.MaxY) / 2;

            // A site with no extent on an axis - one model, one column - would divide by zero on
            // projection. A metre of nothing is better than an infinity.
            var spanX = Math.Max(site.MaxX - site.MinX, 1);
            var spanY = Math.Max(site.MaxY - site.MinY, 1);

            spanX *= 1 + (2 * Margin);
            spanY *= 1 + (2 * Margin);

            if (spanX / spanY < aspect) spanX = spanY * aspect;
            else spanY = spanX / aspect;

            return new BoundingBox(midX - (spanX / 2), midY - (spanY / 2), site.MinZ,
                                   midX + (spanX / 2), midY + (spanY / 2), site.MaxZ);
        }

        /// <summary>
        /// Where a point lands in the image, 0 to 1 on each axis.
        ///
        /// North is up and Y grows upward on the ground but downward in an image, which is the one
        /// sign flip in the whole calculation and the one that is silently wrong if forgotten: the
        /// plan comes out mirrored top to bottom and looks entirely plausible.
        /// </summary>
        /// <param name="framed">The box the render was fitted to, from <see cref="Frame"/>.</param>
        /// <param name="x">The point's easting, in metres.</param>
        /// <param name="y">The point's northing, in metres.</param>
        public static (double X, double Y) At(BoundingBox framed, double x, double y)
        {
            var spanX = framed.MaxX - framed.MinX;
            var spanY = framed.MaxY - framed.MinY;

            if (spanX <= 0 || spanY <= 0) return (0.5, 0.5);

            return ((x - framed.MinX) / spanX, (framed.MaxY - y) / spanY);
        }

        /// <summary>
        /// The site's extent, from everything on the board.
        ///
        /// From the results rather than from the models, because a federation that includes a
        /// two-kilometre survey strip would shrink every real issue into one pixel at the middle.
        /// The plan is of where the work is.
        /// </summary>
        /// <param name="points">Every result's position, in metres.</param>
        public static BoundingBox? Extent(IEnumerable<(double X, double Y, double Z)>? points)
        {
            if (points == null) return null;

            var any = false;
            double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;

            foreach (var p in points)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)) continue;

                if (!any)
                {
                    minX = maxX = p.X;
                    minY = maxY = p.Y;
                    minZ = maxZ = p.Z;
                    any = true;
                    continue;
                }

                if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; else if (p.Z > maxZ) maxZ = p.Z;
            }

            return any ? new BoundingBox(minX, minY, minZ, maxX, maxY, maxZ) : (BoundingBox?)null;
        }

        /// <summary>
        /// What the plan can and cannot resolve, in words.
        ///
        /// Printed under the image. A marker on a 400 m plan is a claim about a point the page
        /// cannot resolve, and a report that let a reader scale off it would be worse than one with
        /// no plan at all.
        /// </summary>
        /// <param name="framed">The box the render was fitted to.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="units">How this project writes a length.</param>
        public static string Resolution(BoundingBox framed, int width, Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            var span = framed.MaxX - framed.MinX;

            if (width <= 0 || span <= 0) return "The plan is not to scale.";

            var perPixel = span / width;

            return "The plan spans " + measure.Length(span) + " across "
                 + Measure.Count(width) + " pixels, so one pixel is about "
                 + measure.Length(perPixel)
                 + ". Markers show which part of the site an issue is in, not where it is: "
                 + "the setting-out coordinates beside each group are the numbers to work from.";
        }

        /// <summary>
        /// The marker table: which number is which group.
        ///
        /// Without it the plan is a page of numbered dots. It is deliberately short — number,
        /// group, party, level — because the detail is on the group's own page.
        /// </summary>
        /// <param name="rows">Number, name, party and level for each marked group.</param>
        public static TableBlock Legend(IEnumerable<(int Number, string Name, string? Party, string? Level)> rows)
        {
            var table = new TableBlock("#", "Issue", "Responsible", "Level");

            foreach (var row in rows.OrderBy(r => r.Number))
                table.Row(row.Number.ToString(CultureInfo.InvariantCulture), row.Name,
                          row.Party ?? "Unassigned", row.Level ?? string.Empty);

            return table;
        }
    }
}
