using System.Linq;
using CamelWorks.Core.Clash;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Turning a filled-in matrix into tests, and — the part that matters more — saying why each
    /// cell that made none made none.
    /// </summary>
    public class MatrixPlannerTests
    {
        private static MatrixGrid Grid(params MatrixCategory[] categories) => new MatrixGrid(categories);

        private static MatrixCategory Cat(string name, string? set = null) =>
            new MatrixCategory(name, set ?? name.ToUpperInvariant());

        private static MatrixLegend Key(params MatrixSymbol[] symbols) => new MatrixLegend(symbols);

        [Fact]
        public void Every_cell_brings_its_own_kind_and_tolerance()
        {
            var grid = Grid(Cat("Frame"), Cat("Ductwork"), Cat("Pipework"));
            grid.Set(0, 1, "A");
            grid.Set(0, 2, "B");

            var build = MatrixPlanner.Plan(grid, Key(
                new MatrixSymbol("A", MatrixTest.Hard, 0.01),
                new MatrixSymbol("B", MatrixTest.Clearance, 0.05)));

            Assert.Equal(2, build.Tests.Count);

            var hard = build.Tests.Single(t => t.Symbol == "A");
            var clear = build.Tests.Single(t => t.Symbol == "B");

            Assert.Equal(MatrixTest.Hard, hard.Test);
            Assert.Equal(0.01, hard.Tolerance);
            Assert.Equal(MatrixTest.Clearance, clear.Test);
            Assert.Equal(0.05, clear.Tolerance);
        }

        [Fact]
        public void A_category_with_no_saved_set_is_named_in_the_reason()
        {
            var grid = Grid(Cat("Frame"), new MatrixCategory("Ductwork"));
            grid.Set(0, 1, "A");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A")));

            Assert.Empty(build.Tests);
            Assert.Single(build.Skipped);
            Assert.Contains("Ductwork names no saved set", build.Skipped[0]);
        }

        [Fact]
        public void A_symbol_switched_off_in_the_key_makes_nothing_and_says_so()
        {
            var grid = Grid(Cat("Frame"), Cat("Ductwork"));
            grid.Set(0, 1, "X");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("X", MatrixTest.Hard, 0.01, enabled: false)));

            Assert.Empty(build.Tests);
            Assert.Contains("switched off", build.Skipped[0]);
        }

        [Fact]
        public void A_symbol_nothing_explains_makes_nothing_and_says_so()
        {
            var grid = Grid(Cat("Frame"), Cat("Ductwork"));
            grid.Set(0, 1, "?");

            var build = MatrixPlanner.Plan(grid, Key());

            Assert.Empty(build.Tests);
            Assert.Contains("nothing says what", build.Skipped[0]);
        }

        [Fact]
        public void A_test_the_document_already_has_is_counted_not_remade()
        {
            var grid = Grid(Cat("Frame"), Cat("Ductwork"));
            grid.Set(0, 1, "A");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A")),
                                           existing: new[] { "Frame vs Ductwork" });

            Assert.Empty(build.Tests);
            Assert.Equal(1, build.Already);
            Assert.Empty(build.Skipped);
        }

        [Fact]
        public void The_diagonal_makes_a_test_against_the_same_set_twice()
        {
            var grid = Grid(Cat("Ductwork"));
            grid.Set(0, 0, "A");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A")));

            var test = Assert.Single(build.Tests);
            Assert.True(test.IsSelf);

            var spec = test.Spec();
            Assert.Equal("DUCTWORK", spec.A.Name);
            Assert.Equal("DUCTWORK", spec.B.Name);
        }

        [Fact]
        public void A_side_is_a_saved_set_by_name_so_it_re_resolves()
        {
            var grid = Grid(Cat("Frame", "ST_Frame"), Cat("Ductwork", "ME_Duct"));
            grid.Set(0, 1, "A");

            var spec = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A"))).Tests.Single().Spec();

            Assert.Equal(ClashSideKind.Set, spec.A.Kind);
            Assert.Equal("ST_Frame", spec.A.Name);
            Assert.Equal("ME_Duct", spec.B.Name);
        }

        [Fact]
        public void The_name_pattern_can_use_the_number()
        {
            var grid = Grid(Cat("Frame"), Cat("Ductwork"));
            grid.Set(0, 1, "A");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A")), namePattern: "{n} {a}/{b}");

            Assert.Equal("01 Frame/Ductwork", build.Tests.Single().Name);
        }

        [Fact]
        public void The_cap_stops_the_run_and_says_it_stopped()
        {
            var grid = Grid(Cat("A1"), Cat("B1"), Cat("C1"));
            grid.Set(0, 1, "A");
            grid.Set(0, 2, "A");
            grid.Set(1, 2, "A");

            var build = MatrixPlanner.Plan(grid, Key(new MatrixSymbol("A")), cap: 2);

            Assert.Equal(2, build.Tests.Count);
            Assert.Contains("more than 2 tests", build.Skipped[0]);
        }

        [Fact]
        public void An_empty_grid_plans_nothing_without_complaining()
        {
            var build = MatrixPlanner.Plan(new MatrixGrid(), Key());

            Assert.True(build.IsEmpty);
            Assert.Empty(build.Skipped);
            Assert.Contains("nothing to make", build.Explain());
        }
    }
}
