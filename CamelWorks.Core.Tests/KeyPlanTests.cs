using System;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class KeyPlanTests
    {
        private static BoundingBox Site(double minX, double minY, double maxX, double maxY) =>
            new BoundingBox(minX, minY, 0, maxX, maxY, 10);

        [Fact]
        public void The_framed_box_has_the_image_aspect_so_nothing_is_letterboxed()
        {
            // The host fits a box into the frame and leaves bars on the other axis. A projection
            // that ignored them would put every marker out by exactly the width of the bars.
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1600, 1200);

            var width = framed.MaxX - framed.MinX;
            var height = framed.MaxY - framed.MinY;

            Assert.Equal(1600d / 1200, width / height, 6);
        }

        [Fact]
        public void The_framed_box_is_centred_on_the_site_and_never_smaller_than_it()
        {
            var framed = KeyPlan.Frame(Site(0, 0, 100, 40), 1000, 1000);

            Assert.Equal(50, (framed.MinX + framed.MaxX) / 2, 6);
            Assert.Equal(20, (framed.MinY + framed.MaxY) / 2, 6);

            Assert.True(framed.MinX <= 0);
            Assert.True(framed.MaxX >= 100);
            Assert.True(framed.MinY <= 0);
            Assert.True(framed.MaxY >= 40);
        }

        [Fact]
        public void A_margin_is_left_around_the_site_so_an_edge_marker_is_not_on_the_border()
        {
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1000, 1000);

            Assert.Equal(-4, framed.MinX, 6);
            Assert.Equal(104, framed.MaxX, 6);
        }

        [Fact]
        public void A_site_with_no_extent_still_frames_rather_than_dividing_by_zero()
        {
            // One model, one column, one clash. A metre of nothing is better than an infinity.
            var framed = KeyPlan.Frame(Site(10, 10, 10, 10), 800, 600);

            Assert.True(framed.MaxX - framed.MinX > 0);
            Assert.True(framed.MaxY - framed.MinY > 0);

            var at = KeyPlan.At(framed, 10, 10);

            Assert.Equal(0.5, at.X, 6);
            Assert.Equal(0.5, at.Y, 6);
        }

        [Fact]
        public void The_middle_of_the_frame_is_the_middle_of_the_image()
        {
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1000, 1000);

            var at = KeyPlan.At(framed, 50, 50);

            Assert.Equal(0.5, at.X, 6);
            Assert.Equal(0.5, at.Y, 6);
        }

        [Fact]
        public void North_is_up_which_means_the_Y_axis_flips()
        {
            // The one sign flip in the whole calculation, and the one that is silently wrong if
            // forgotten: the plan comes out mirrored top to bottom and looks entirely plausible.
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1000, 1000);

            var north = KeyPlan.At(framed, 50, 104);
            var south = KeyPlan.At(framed, 50, -4);

            Assert.Equal(0, north.Y, 6);
            Assert.Equal(1, south.Y, 6);
            Assert.True(north.Y < south.Y);
        }

        [Fact]
        public void East_is_right()
        {
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1000, 1000);

            Assert.Equal(0, KeyPlan.At(framed, -4, 50).X, 6);
            Assert.Equal(1, KeyPlan.At(framed, 104, 50).X, 6);
        }

        [Fact]
        public void A_callout_takes_the_projected_position_unchanged()
        {
            var framed = KeyPlan.Frame(Site(0, 0, 100, 100), 1000, 1000);
            var at = KeyPlan.At(framed, 27, 63);

            var callout = new Callout(1, at.X, at.Y);

            Assert.Equal(at.X, callout.X, 6);
            Assert.Equal(at.Y, callout.Y, 6);
        }

        [Fact]
        public void The_extent_comes_from_the_results_rather_than_from_the_models()
        {
            // A federation that includes a two-kilometre survey strip would shrink every real issue
            // into one pixel in the middle. The plan is of where the work is.
            var extent = KeyPlan.Extent(new[] { (1d, 2d, 3d), (11d, 22d, 33d), (-5d, 0d, 1d) })!.Value;

            Assert.Equal(-5, extent.MinX, 6);
            Assert.Equal(11, extent.MaxX, 6);
            Assert.Equal(0, extent.MinY, 6);
            Assert.Equal(22, extent.MaxY, 6);
            Assert.Equal(1, extent.MinZ, 6);
            Assert.Equal(33, extent.MaxZ, 6);
        }

        [Fact]
        public void A_point_that_is_not_a_number_does_not_drag_the_extent_to_infinity()
        {
            var extent = KeyPlan.Extent(new[] { (1d, 2d, 3d), (double.NaN, 5d, 5d) })!.Value;

            Assert.Equal(1, extent.MinX, 6);
            Assert.Equal(1, extent.MaxX, 6);
        }

        [Fact]
        public void Nothing_to_mark_is_no_plan_rather_than_an_empty_one()
        {
            Assert.Null(KeyPlan.Extent(null));
            Assert.Null(KeyPlan.Extent(Array.Empty<(double, double, double)>()));
        }

        [Fact]
        public void It_says_what_it_cannot_resolve_rather_than_implying_a_precision()
        {
            // A marker on a 400 m plan is a claim about a point the page cannot resolve, and a
            // report that let a reader scale off it would be worse than one with no plan at all.
            var framed = KeyPlan.Frame(Site(0, 0, 400, 300), 1600, 1200);

            var said = KeyPlan.Resolution(framed, 1600, Measure.Millimetres);

            Assert.Contains("1,600 pixels", said);
            Assert.Contains("270 mm", said);
            Assert.Contains("setting-out coordinates", said);
        }

        [Fact]
        public void A_plan_of_nothing_says_it_is_not_to_scale()
        {
            Assert.Contains("not to scale", KeyPlan.Resolution(Site(0, 0, 0, 0), 0));
        }

        [Fact]
        public void The_legend_says_which_number_is_which_issue()
        {
            var legend = KeyPlan.Legend(new[]
            {
                (2, "Riser 3", (string?)null, (string?)"L03"),
                (1, "Level 2 MEP v STR", (string?)"Mechanical", (string?)"L02"),
            });

            Assert.Equal("1", legend.Rows[0][0]);
            Assert.Equal("Level 2 MEP v STR", legend.Rows[0][1]);
            Assert.Equal("Mechanical", legend.Rows[0][2]);
            Assert.Equal("2", legend.Rows[1][0]);
            Assert.Equal("Unassigned", legend.Rows[1][2]);
        }
    }
}
