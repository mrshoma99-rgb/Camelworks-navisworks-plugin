using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashMatrixTests
    {
        private static MatrixSide Side(string name, int elements = -1) =>
            new MatrixSide(name, ClashSide.Set(name), elements);

        private static MatrixSide[] Disciplines(params string[] names) =>
            names.Select(n => Side(n)).ToArray();

        [Fact]
        public void A_grid_of_two_lists_is_every_pairing()
        {
            var plan = ClashMatrix.Build(Disciplines("ARC", "STR"), Disciplines("MEP", "FP"));

            Assert.Equal(4, plan.Tests.Count);
            Assert.Equal(4, plan.Selected);
            Assert.Equal(new[] { "ARC vs MEP", "ARC vs FP", "STR vs MEP", "STR vs FP" },
                         plan.Tests.Select(t => t.Name));
        }

        [Fact]
        public void A_square_grid_is_half_redundant_and_says_so()
        {
            // ARC vs STR and STR vs ARC find the same conflicts. One of them is the test.
            var axis = Disciplines("ARC", "STR", "MEP");
            var plan = ClashMatrix.Build(axis, axis);

            Assert.Equal(3, plan.Tests.Count);
            Assert.Equal(3, plan.Mirrored);
            Assert.Equal(new[] { "ARC vs STR", "ARC vs MEP", "STR vs MEP" },
                         plan.Tests.Select(t => t.Name));
            Assert.Contains("the same pair the other way round", plan.Explain());
        }

        [Fact]
        public void The_diagonal_is_off_unless_it_is_asked_for()
        {
            var axis = Disciplines("ARC", "STR");

            Assert.DoesNotContain(ClashMatrix.Build(axis, axis).Tests, t => t.Name == "ARC vs ARC");

            var withDiagonal = ClashMatrix.Build(axis, axis, new MatrixOptions { SelfPairs = true });
            var self = withDiagonal.Tests.Single(t => t.Name == "ARC vs ARC");

            Assert.False(self.IsSelected);
            Assert.Contains("against itself", self.Note);
        }

        [Fact]
        public void A_test_the_document_already_has_is_shown_but_not_ticked()
        {
            // The second run of the builder is the common one: a model was added and the matrix
            // needs its row. It must not produce a duplicate of everything else.
            var plan = ClashMatrix.Build(Disciplines("ARC"), Disciplines("STR", "MEP"),
                new MatrixOptions { Existing = new[] { "ARC vs STR" } });

            var known = plan.Tests.Single(t => t.Name == "ARC vs STR");
            var fresh = plan.Tests.Single(t => t.Name == "ARC vs MEP");

            Assert.False(known.IsSelected);
            Assert.Contains("already in the document", known.Note);
            Assert.True(fresh.IsSelected);
            Assert.Equal(1, plan.Selected);
        }

        [Fact]
        public void An_empty_side_is_shown_with_the_reason_not_hidden()
        {
            // A discipline with nothing in it usually means a model failed to load, which is worth
            // seeing on the screen where somebody is about to build tests against it.
            var plan = ClashMatrix.Build(
                new[] { Side("ARC", 1200), Side("FP", 0) },
                new[] { Side("STR", 800) });

            Assert.Equal(2, plan.Tests.Count);

            var empty = plan.Tests.Single(t => t.Name == "FP vs STR");

            Assert.False(empty.IsSelected);
            Assert.Contains("\"FP\" has nothing in it", empty.Note);
        }

        [Fact]
        public void An_unknown_size_is_not_an_empty_one()
        {
            var plan = ClashMatrix.Build(new[] { Side("ARC") }, new[] { Side("STR") });

            Assert.True(plan.Tests.Single().IsSelected);
        }

        [Fact]
        public void The_name_template_is_filled_in()
        {
            var plan = ClashMatrix.Build(Disciplines("ARC"), Disciplines("STR", "MEP"),
                new MatrixOptions { NamePattern = "{n}: L03 {a}-{b} hard" });

            Assert.Equal(new[] { "1: L03 ARC-STR hard", "2: L03 ARC-MEP hard" },
                         plan.Tests.Select(t => t.Name));
        }

        [Fact]
        public void Two_pairings_that_would_share_a_name_are_told_apart()
        {
            // A constant template is a reasonable thing to type by accident. Two tests the host
            // cannot tell apart is not a reasonable thing to leave behind.
            var plan = ClashMatrix.Build(Disciplines("ARC"), Disciplines("STR", "MEP"),
                new MatrixOptions { NamePattern = "Coordination" });

            Assert.Equal(2, plan.Tests.Select(t => t.Name).Distinct().Count());
        }

        [Fact]
        public void Names_that_run_together_are_still_different_pairs()
        {
            // The pair key is length-prefixed, so ("AB", "C") does not collide with ("A", "BC").
            var plan = ClashMatrix.Build(
                new[] { Side("AB"), Side("A") },
                new[] { Side("C"), Side("BC") });

            Assert.Equal(4, plan.Tests.Count);
            Assert.Equal(0, plan.Mirrored);
        }

        [Fact]
        public void Past_the_cap_the_rest_are_counted_rather_than_produced()
        {
            var rows = Enumerable.Range(0, 10).Select(n => Side("R" + n)).ToArray();
            var columns = Enumerable.Range(0, 10).Select(n => Side("C" + n)).ToArray();

            var plan = ClashMatrix.Build(rows, columns, new MatrixOptions { Cap = 12 });

            Assert.Equal(12, plan.Tests.Count);
            Assert.Equal(88, plan.Dropped);
            Assert.Contains("beyond the cap", plan.Explain());
        }

        [Fact]
        public void Tolerance_travels_to_the_spec_in_metres()
        {
            var plan = ClashMatrix.Build(Disciplines("ARC"), Disciplines("STR"),
                new MatrixOptions { Tolerance = 0.025 });

            var spec = plan.Tests.Single().ToSpec("Coordination");

            Assert.Equal(0.025, spec.Tolerance);
            Assert.Equal("Coordination", spec.Folder);
            Assert.Equal("ARC vs STR", spec.Name);
            Assert.Equal(ClashSideKind.Set, spec.A.Kind);
            Assert.Equal("ARC", spec.A.Name);
        }

        [Fact]
        public void A_side_can_be_a_whole_model_a_set_or_a_list()
        {
            Assert.Equal(ClashSideKind.Models, ClashSide.Model("scope").Kind);
            Assert.Equal(2, ClashSide.Models(new[] { "a", "b" }).Scopes.Count);
            Assert.Equal(ClashSideKind.Set, ClashSide.Set("Level 3").Kind);
            Assert.Equal(ClashSideKind.Elements, ClashSide.Elements(new ElementKey[0]).Kind);
            Assert.Empty(ClashSide.Model("scope").Keys);
            Assert.Equal("scope", ClashSide.Model("scope").Scopes.Single());
        }
    }
}
