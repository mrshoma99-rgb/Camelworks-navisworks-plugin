using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class MassPropertiesTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Mech.nwc", name);

        /// <summary>A closed axis-aligned box, so the answer is known before the test runs.</summary>
        private static MassPart Box(string name, double x, double y, double z,
                                    double sx = 1, double sy = 1, double sz = 1, double? mass = null)
        {
            var positions = new List<float>();

            foreach (var corner in new[]
                     {
                         (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0),
                         (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1),
                     })
            {
                positions.Add((float)(x + corner.Item1 * sx));
                positions.Add((float)(y + corner.Item2 * sy));
                positions.Add((float)(z + corner.Item3 * sz));
            }

            // Wound so every face looks outward, which is what makes the signed volume positive.
            var indices = new[]
            {
                0, 2, 1,  0, 3, 2,
                4, 5, 6,  4, 6, 7,
                0, 1, 5,  0, 5, 4,
                1, 2, 6,  1, 6, 5,
                2, 3, 7,  2, 7, 6,
                3, 0, 4,  3, 4, 7,
            };

            return new MassPart(Key(name), name, positions, indices, mass);
        }

        /// <summary>One triangle: an area and no enclosed volume, which is most of a federation.</summary>
        private static MassPart Sheet(string name, double z) =>
            new MassPart(Key(name), name,
                new[] { 0f, 0f, (float)z, 2f, 0f, (float)z, 0f, 2f, (float)z },
                new[] { 0, 1, 2 });

        // ---- The arithmetic --------------------------------------------------------------

        [Fact]
        public void A_closed_box_gives_its_own_volume_and_its_own_middle()
        {
            var body = MassProperties.Body(Box("Block", 0, 0, 0, 2, 2, 2));

            Assert.True(body.Closed);
            Assert.Equal(8, body.Volume, 6);
            Assert.Equal(24, body.Surface, 6);
            Assert.Equal(1, body.X, 6);
            Assert.Equal(1, body.Y, 6);
            Assert.Equal(1, body.Z, 6);
        }

        [Fact]
        public void A_box_away_from_the_origin_still_gives_its_own_middle()
        {
            // The volume sum is taken from the origin, so a shape a long way from it is where the
            // arithmetic would go wrong if it were going to.
            var body = MassProperties.Body(Box("Block", 100, 200, 300, 2, 4, 6));

            Assert.Equal(48, body.Volume, 5);
            Assert.Equal(101, body.X, 5);
            Assert.Equal(202, body.Y, 5);
            Assert.Equal(303, body.Z, 5);
        }

        [Fact]
        public void A_single_triangle_encloses_nothing_however_much_volume_it_seems_to_have()
        {
            // Three metres above the origin it makes a two-cubic-metre tetrahedron with it, so
            // "the volume came out near zero" would have called this a solid. Closure is whether
            // every edge is shared, and this one shares none.
            var body = MassProperties.Body(Sheet("Panel", 3));

            Assert.False(body.Closed);
            Assert.Equal(2, body.Volume, 6);
            Assert.Equal(2, body.Surface, 6);

            // Falls back to the centroid of the face, which for this triangle is a third along.
            Assert.Equal(2.0 / 3.0, body.X, 6);
            Assert.Equal(3, body.Z, 6);
        }

        [Fact]
        public void Geometry_that_cannot_be_read_measures_nothing_rather_than_throwing()
        {
            Assert.Equal((false, 0.0, 0.0, 0.0, 0.0, 0.0), MassProperties.Body(null));

            var ragged = new MassPart(Key("Bad"), "Bad", new[] { 0f, 0f, 0f }, new[] { 0, 1, 2 });

            Assert.False(ragged.IsUsable);
            Assert.False(MassProperties.Body(ragged).Closed);
            Assert.Equal(0, MassProperties.Body(ragged).Volume);
        }

        [Fact]
        public void A_box_whose_corners_are_not_shared_by_index_is_still_closed()
        {
            // What the host actually hands over: one set of vertices per triangle, sharing nothing.
            // Matching edges by index would call every real solid an open surface.
            var shared = Box("Shared", 0, 0, 0, 2, 2, 2);

            var positions = new List<float>();
            var indices = new List<int>();

            for (var i = 0; i < shared.Indices.Count; i++)
            {
                var at = shared.Indices[i] * 3;

                positions.Add(shared.Positions[at]);
                positions.Add(shared.Positions[at + 1]);
                positions.Add(shared.Positions[at + 2]);

                indices.Add(i);
            }

            var loose = new MassPart(Key("Loose"), "Loose", positions, indices);

            Assert.Equal(8, shared.Positions.Count / 3);
            Assert.Equal(36, loose.Positions.Count / 3);

            var body = MassProperties.Body(loose);

            Assert.True(body.Closed);
            Assert.Equal(8, body.Volume, 6);
            Assert.Equal(1, body.X, 6);
        }

        [Fact]
        public void A_box_with_a_face_missing_is_open_however_much_of_it_is_there()
        {
            var whole = Box("Whole", 0, 0, 0, 2, 2, 2);

            // Drop the two triangles of the first face, leaving five sides and a hole.
            var indices = whole.Indices.Skip(6).ToList();

            var body = MassProperties.Body(
                new MassPart(Key("Open"), "Open", whole.Positions, indices));

            Assert.False(body.Closed);
            Assert.Equal(20, body.Surface, 6);
        }

        // ---- What it weighs by -----------------------------------------------------------

        [Fact]
        public void With_a_mass_on_every_element_it_is_really_a_centre_of_gravity()
        {
            var result = MassProperties.Of(new[]
            {
                Box("Light", 0, 0, 0, 1, 1, 1, 100),
                Box("Heavy", 10, 0, 0, 1, 1, 1, 900),
            });

            Assert.Null(result.Refusal);
            Assert.Equal(MassBasis.Weight, result.Basis);
            Assert.True(result.IsTrueCentreOfGravity);
            Assert.Equal(1000, result.Weight, 6);

            // Nine tenths of the weight is at 10.5, one tenth at 0.5.
            Assert.Equal(0.1 * 0.5 + 0.9 * 10.5, result.X, 6);
            Assert.Equal(0.5, result.Y, 6);
        }

        [Fact]
        public void Without_a_mass_it_is_the_centre_of_the_volume_and_does_not_pretend_otherwise()
        {
            var result = MassProperties.Of(new[]
            {
                Box("Small", 0, 0, 0, 1, 1, 1),
                Box("Large", 10, 0, 0, 3, 1, 1),
            });

            Assert.Equal(MassBasis.Volume, result.Basis);
            Assert.False(result.IsTrueCentreOfGravity);
            Assert.Equal(4, result.Volume, 6);

            // One cubic metre at 0.5 and three at 11.5.
            Assert.Equal((0.5 + 3 * 11.5) / 4, result.X, 6);
            Assert.Contains("centre of volume", result.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_element_with_no_mass_among_ones_that_have_it_makes_the_answer_an_estimate()
        {
            var result = MassProperties.Of(new[]
            {
                Box("Weighed", 0, 0, 0, 1, 1, 1, 500),
                Box("Not weighed", 10, 0, 0, 1, 1, 1),
            });

            Assert.Equal(MassBasis.Weight, result.Basis);
            Assert.False(result.IsTrueCentreOfGravity);
            Assert.Equal(1, result.Unweighted);
            Assert.Contains("this is an estimate", result.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Asking_for_the_volume_ignores_a_mass_that_is_there()
        {
            var parts = new[]
            {
                Box("Light", 0, 0, 0, 1, 1, 1, 1),
                Box("Heavy", 10, 0, 0, 1, 1, 1, 999),
            };

            var byWeight = MassProperties.Of(parts);
            var byVolume = MassProperties.Of(parts, byMass: false);

            Assert.NotEqual(byWeight.X, byVolume.X, 6);
            Assert.Equal(MassBasis.Volume, byVolume.Basis);
            Assert.Equal(0, byVolume.Unweighted);
            Assert.Equal(5.5, byVolume.X, 6);
        }

        [Fact]
        public void Geometry_that_encloses_nothing_at_all_is_a_centre_of_surface_and_says_it_is_not_a_lift_plan()
        {
            var result = MassProperties.Of(new[] { Sheet("Panel A", 0), Sheet("Panel B", 4) });

            Assert.Equal(MassBasis.Surface, result.Basis);
            Assert.Equal(2, result.Z, 6);
            Assert.Contains(result.Notes, n => n.Contains("not a lift plan", StringComparison.Ordinal));
        }

        [Fact]
        public void Some_of_it_being_a_surface_is_called_out_because_that_is_where_it_goes_wrong()
        {
            var result = MassProperties.Of(new[]
            {
                Box("Solid", 0, 0, 0, 1, 1, 1),
                Sheet("Panel", 4),
            }, byMass: false);

            Assert.Equal(MassBasis.Volume, result.Basis);
            Assert.Contains(result.Notes, n => n.Contains("exported as surfaces", StringComparison.Ordinal));
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void Nothing_selected_says_what_to_select()
        {
            var result = MassProperties.Of(null);

            Assert.NotNull(result.Refusal);
            Assert.False(result.IsUsable);
            Assert.Equal(MassBasis.None, result.Basis);
            Assert.Contains("Select the assembly", result.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void Elements_that_are_all_grouping_nodes_say_that_rather_than_giving_the_origin()
        {
            var result = MassProperties.Of(new[]
            {
                new MassPart(Key("Group"), "Group", null, null),
                new MassPart(Key("Folder"), "Folder", null, null),
            });

            Assert.NotNull(result.Refusal);
            Assert.Contains("Grouping nodes", result.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void One_unreadable_element_among_good_ones_is_counted_and_left_out()
        {
            var result = MassProperties.Of(new[]
            {
                Box("Good", 0, 0, 0, 2, 2, 2),
                new MassPart(Key("Group"), "Group", null, null),
            });

            Assert.Null(result.Refusal);
            Assert.Equal(1, result.Parts);
            Assert.Equal(1, result.Unusable);
            Assert.Equal(1, result.X, 6);
            Assert.Contains("could not be read", result.Explain(), StringComparison.Ordinal);
        }

        // ---- Reading a mass off the model ------------------------------------------------

        [Theory]
        [InlineData("1250 kg", 1250)]
        [InlineData("1.25 t", 1250)]
        [InlineData("1250", 1250)]
        [InlineData("2500 g", 2.5)]
        public void A_mass_is_read_however_the_model_wrote_it(string text, double expected)
        {
            Assert.True(MassProperties.TryMass(text, out var kilograms));
            Assert.Equal(expected, kilograms, 6);
        }

        [Fact]
        public void A_bare_number_is_taken_as_kilograms_because_that_is_what_a_mass_property_means()
        {
            Assert.True(MassProperties.TryMass("980", out var kilograms));
            Assert.Equal(980, kilograms, 6);
        }

        [Fact]
        public void Something_that_is_not_a_mass_at_all_is_refused_rather_than_guessed()
        {
            Assert.False(MassProperties.TryMass("2 m", out _));
            Assert.False(MassProperties.TryMass("heavy", out _));
            Assert.False(MassProperties.TryMass(null, out _));
            Assert.False(MassProperties.TryMass("   ", out _));
        }

        [Fact]
        public void The_names_a_mass_hides_under_are_offered_rather_than_left_to_be_guessed()
        {
            Assert.Contains("Mass", MassProperties.MassNames, StringComparer.Ordinal);
            Assert.Contains("Weight", MassProperties.MassNames, StringComparer.Ordinal);
        }
    }
}
