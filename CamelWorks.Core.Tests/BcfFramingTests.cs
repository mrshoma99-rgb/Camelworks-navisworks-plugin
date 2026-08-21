using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class BcfFramingTests
    {
        private static IReadOnlyList<(double, double, double)> Points(params (double, double, double)[] p) => p;

        [Fact]
        public void The_camera_looks_at_the_middle_of_what_it_frames()
        {
            var framing = BcfFraming.Around(Points((0, 0, 0), (10, 4, 2)));

            Assert.Equal(5, framing.TargetX, 6);
            Assert.Equal(2, framing.TargetY, 6);
            Assert.Equal(1, framing.TargetZ, 6);
        }

        [Fact]
        public void It_stands_off_to_one_side_and_above_rather_than_in_plan()
        {
            // A clash between a duct and a beam is invisible from directly above and ambiguous from
            // directly in front. The whole value of the viewpoint is that it shows the two missing
            // each other.
            var framing = BcfFraming.Around(Points((0, 0, 0)));

            Assert.True(framing.EyeZ > framing.TargetZ, "the eye should be above what it looks at");
            Assert.NotEqual(framing.EyeX, framing.TargetX, 3);
            Assert.NotEqual(framing.EyeY, framing.TargetY, 3);
        }

        [Fact]
        public void A_single_point_is_still_framed_from_a_sensible_distance()
        {
            // Nothing to fit on screen means no span to scale by, and a camera at the clash point
            // shows the inside of a duct.
            var framing = BcfFraming.Around(Points((3, 3, 3)));

            Assert.Equal(BcfFraming.MinimumDistance, framing.Distance, 6);
        }

        [Fact]
        public void A_bigger_group_is_framed_from_further_back()
        {
            var small = BcfFraming.Around(Points((0, 0, 0), (1, 1, 1)));
            var large = BcfFraming.Around(Points((0, 0, 0), (40, 40, 6)));

            Assert.True(large.Distance > small.Distance);
            Assert.Equal(40 * BcfFraming.Pullback, large.Distance, 6);
        }

        [Fact]
        public void The_up_vector_is_perpendicular_to_the_view_direction()
        {
            // A viewer handed an up vector that is not perpendicular renders the view rolled by a
            // few degrees, which reads as the viewpoint being subtly wrong rather than as a bug.
            var framing = BcfFraming.Around(Points((0, 0, 0), (5, 5, 5)));

            var (dx, dy, dz) = framing.Direction;
            var dot = dx * framing.UpX + dy * framing.UpY + dz * framing.UpZ;

            Assert.Equal(0, dot, 9);
            Assert.Equal(1, Math.Sqrt(framing.UpX * framing.UpX + framing.UpY * framing.UpY
                                      + framing.UpZ * framing.UpZ), 9);
        }

        [Fact]
        public void The_up_vector_still_points_broadly_upwards()
        {
            var framing = BcfFraming.Around(Points((0, 0, 0), (5, 5, 5)));
            Assert.True(framing.UpZ > 0.5, "up should be recognisably up");
        }

        [Fact]
        public void The_direction_is_a_unit_vector()
        {
            var framing = BcfFraming.Around(Points((1, 2, 3), (9, 4, 7)));
            var (dx, dy, dz) = framing.Direction;

            Assert.Equal(1, Math.Sqrt(dx * dx + dy * dy + dz * dz), 9);
        }

        [Fact]
        public void Nothing_to_frame_is_the_origin_rather_than_an_exception()
        {
            var framing = BcfFraming.Around(Array.Empty<(double, double, double)>());

            Assert.Equal(0, framing.TargetX, 6);
            Assert.Equal(BcfFraming.MinimumDistance, framing.Distance, 6);
        }

        [Fact]
        public void A_corrupt_coordinate_does_not_move_the_camera_to_nowhere()
        {
            var framing = BcfFraming.Around(Points((0, 0, 0), (double.NaN, 0, 0), (4, 0, 0)));

            Assert.Equal(2, framing.TargetX, 6);
            Assert.False(double.IsNaN(framing.EyeX));
        }

        [Fact]
        public void A_box_frames_the_same_way_a_pair_of_corners_does()
        {
            var box = BcfFraming.Around(new BoundingBox(0, 0, 0, 10, 4, 2));
            var points = BcfFraming.Around(Points((0, 0, 0), (10, 4, 2)));

            Assert.Equal(points.EyeX, box.EyeX, 9);
            Assert.Equal(points.Distance, box.Distance, 9);
        }

        [Fact]
        public void The_default_field_of_view_survives_both_schemas()
        {
            // BCF 2.1 clamps anything outside 45-60 and the writer reports it. The default sits
            // inside that range so an export never has to explain itself.
            var camera = BcfFraming.Around(Points((0, 0, 0))).ToCamera();

            Assert.InRange(camera.FieldOfViewDegrees, BcfWriter.MinFieldOfView21, BcfWriter.MaxFieldOfView21);
            Assert.True(camera.IsPerspective);
        }

        [Fact]
        public void The_camera_carries_the_framing_it_was_made_from()
        {
            var framing = BcfFraming.Around(Points((0, 0, 0), (6, 6, 2)));
            var camera = framing.ToCamera(21.0 / 9.0);

            var (dx, dy, dz) = framing.Direction;

            Assert.Equal(framing.EyeX, camera.X, 9);
            Assert.Equal(dx, camera.DirectionX, 9);
            Assert.Equal(dy, camera.DirectionY, 9);
            Assert.Equal(dz, camera.DirectionZ, 9);
            Assert.Equal(21.0 / 9.0, camera.AspectRatio, 9);
        }
    }

    public class IfcGuidTests
    {
        [Theory]
        [InlineData("0YvctVUKr0kugbFTf53O9L")]
        [InlineData("3DqaUydM1QMgUIXlXc$bpx")]
        [InlineData("1hqA$Kcgn2gfPqcnMBFRSZ")]
        public void A_real_compressed_guid_is_accepted(string guid) =>
            Assert.True(IfcGuid.IsCompressed(guid));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("123456")]
        [InlineData("d8a9f6c2-1e34-4b5a-9c7d-0e1f2a3b4c5d")]   // a UUID, not a compressed one
        [InlineData("0YvctVUKr0kugbFTf53O9")]                   // one short
        [InlineData("0YvctVUKr0kugbFTf53O9LL")]                 // one long
        [InlineData("0YvctVUKr0kugbFTf53O9!")]                  // not in the alphabet
        public void Anything_else_is_refused(string? text) =>
            Assert.False(IfcGuid.IsCompressed(text));

        [Fact]
        public void A_first_character_that_cannot_start_one_is_refused()
        {
            // The leading character carries only two of the 128 bits, so it can only be 0-3.
            // Checking it throws out most look-alikes - including a plain 22-character name -
            // before the alphabet check has to.
            Assert.False(IfcGuid.IsCompressed("ZYvctVUKr0kugbFTf53O9L"));
            Assert.True(IfcGuid.IsCompressed("3YvctVUKr0kugbFTf53O9L"));
        }

        [Fact]
        public void OrNull_hands_back_only_what_can_be_written_as_one()
        {
            // The reason this exists: a value out of a property called "GUID" is not necessarily an
            // IFC GlobalId, and a viewer handed a Revit element id finds nothing and says nothing.
            Assert.Equal("0YvctVUKr0kugbFTf53O9L", IfcGuid.OrNull("0YvctVUKr0kugbFTf53O9L"));
            Assert.Null(IfcGuid.OrNull("349871"));
        }
    }
}
