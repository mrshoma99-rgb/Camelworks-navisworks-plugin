using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The board is two lists, not one square one, and its cells are keyed on the lines
    /// themselves - so dragging a row somewhere else takes its symbols with it rather than
    /// leaving every symbol below the change attached to the wrong pair.
    /// </summary>
    public class MatrixBoardTests
    {
        [Fact]
        public void RowsAndColumnsAreSeparateLists()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(Line("Packages 1-3"));
            var column = board.AddColumn(Line("Ductwork"));

            board.Set(row, column, "A");

            Assert.Single(board.Rows);
            Assert.Single(board.Columns);
            Assert.Equal("A", board.At(row, column));
        }

        [Fact]
        public void NothingIsMirrored()
        {
            // A row and a column are side A and side B. Where both axes hold the same thing the
            // planner is what notices; the board itself stores exactly what was put in it.
            var board = new MatrixBoard();

            var steel = board.AddRow(Line("Steel"));
            var ducts = board.AddColumn(Line("Ducts"));

            board.Set(steel, ducts, "A");

            Assert.Null(board.At(board.AddRow(Line("Ducts")), board.AddColumn(Line("Steel"))));
        }

        [Fact]
        public void MovingARowTakesItsCellsWithIt()
        {
            var board = new MatrixBoard();

            var one = board.AddRow(Line("One"));
            var two = board.AddRow(Line("Two"));
            var three = board.AddRow(Line("Three"));
            var column = board.AddColumn(Line("Ducts"));

            board.Set(three, column, "C");

            board.MoveRow(2, 0);

            Assert.Equal("Three", board.Rows[0].Name);
            Assert.Equal("C", board.At(three, column));
            Assert.Null(board.At(one, column));
            Assert.Null(board.At(two, column));
        }

        [Fact]
        public void RemovingARowTakesOnlyItsOwnCells()
        {
            var board = new MatrixBoard();

            var one = board.AddRow(Line("One"));
            var two = board.AddRow(Line("Two"));
            var column = board.AddColumn(Line("Ducts"));

            board.Set(one, column, "A");
            board.Set(two, column, "B");

            board.RemoveRow(one);

            Assert.Single(board.Rows);
            Assert.Equal(1, board.Filled);
            Assert.Equal("B", board.At(two, column));
        }

        [Fact]
        public void SquaringOffCopiesTheRowsAndKeepsWhatWasFilled()
        {
            var board = new MatrixBoard();

            var steel = board.AddRow(Line("Steel"));
            board.AddRow(Line("Ducts"));

            board.SquareOffRows();

            Assert.Equal(2, board.Columns.Count);
            Assert.Equal(new[] { "Steel", "Ducts" }, board.Columns.Select(c => c.Name));

            // Copies, not the same objects: the two axes have to be able to diverge afterwards.
            Assert.DoesNotContain(board.Columns, c => ReferenceEquals(c, steel));
        }

        [Fact]
        public void FillPutsTheSameSymbolEverywhere()
        {
            var board = new MatrixBoard();

            board.AddRow(Line("One"));
            board.AddRow(Line("Two"));
            board.AddColumn(Line("A"));
            board.AddColumn(Line("B"));

            board.Fill("X");

            Assert.Equal(4, board.Filled);

            board.Fill(null);

            Assert.Equal(0, board.Filled);
        }

        [Fact]
        public void ABoardSurvivesBeingWrittenAndReadBack()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", "01", MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts", "02", MatrixSource.SearchSet("MEP")));

            board.Set(row, column, "A");

            var back = MatrixBoard.FromJson(board.ToJson());

            Assert.Equal("01", back.Rows[0].Number);
            Assert.Equal("STR", back.Rows[0].Source.Name);
            Assert.Equal(MatrixSourceKind.SearchSet, back.Columns[0].Source.Kind);
            Assert.Equal("A", back.At(back.Rows[0], back.Columns[0]));
        }

        private static MatrixLine Line(string name) =>
            new MatrixLine(name, null, MatrixSource.SelectionSet(name));
    }

    /// <summary>What a grid and its legend ask the host for.</summary>
    /// <summary>What a bulk fill writes, and what it deliberately leaves alone.</summary>
    public class MatrixFillTests
    {
        [Fact]
        public void FillingTheGridLeavesTheDiagonalAlone()
        {
            // Twenty-two disciplines filled in one press used to give two hundred and fifty-three
            // tests, twenty-two of them a discipline against itself. Nobody filling a grid meant
            // those, and they are the ones that come back as thousands of results.
            var board = Square("STR", "MEP", "ARC");

            board.Fill("A");

            Assert.Equal(6, board.Filled);

            for (var at = 0; at < 3; at++)
                Assert.Null(board.At(board.Rows[at], board.Columns[at]));
        }

        [Fact]
        public void ADisciplineAgainstItselfCanStillBeAskedForOnPurpose()
        {
            var board = Square("STR", "MEP");

            board.Set(board.Rows[0], board.Columns[0], "A");

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[0]));
        }

        [Fact]
        public void FillingTheGridWritesTheDiagonalWhenItIsAskedTo()
        {
            var board = Square("STR", "MEP");

            board.Fill("A", selfPairs: true);

            Assert.Equal(4, board.Filled);
        }

        [Fact]
        public void TwoLinesPointedAtTheSameSetAreTheSamePairWhereverTheySit()
        {
            // Not the same object - the same thing. A square matrix copies its rows into its
            // columns, so the two ends of the diagonal are two objects pointing at one set.
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var last = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));
            var same = board.AddColumn(new MatrixLine("Steel again", null, MatrixSource.SelectionSet("STR")));

            Assert.True(board.IsSelfPair(row, same));
            Assert.False(board.IsSelfPair(row, last));
        }

        [Fact]
        public void ALinePointedAtNothingIsNotItsOwnPair()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("New row"));
            var column = board.AddColumn(new MatrixLine("New column"));

            Assert.False(board.IsSelfPair(row, column));
        }

        private static MatrixBoard Square(params string[] sets)
        {
            var board = new MatrixBoard();

            foreach (var set in sets)
                board.AddRow(new MatrixLine(set, null, MatrixSource.SelectionSet(set)));

            board.SquareOffRows();

            return board;
        }
    }

    public class MatrixPlanTests
    {
        [Fact]
        public void EveryFilledCellBecomesATest()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "A");

            var plan = MatrixPlanner.Plan(board, Legend());

            Assert.Single(plan.Tests);
            Assert.Equal("Steel vs Ducts", plan.Tests[0].Name);
        }

        [Fact]
        public void TheMirroredCellIsTheSameTestAndIsNotASecondOne()
        {
            // A clash matrix is mirrored about its diagonal: A against B is the same test as B
            // against A. Writing one half writes the other, and the pair is planned once - with
            // nothing reported as skipped, because nothing was left out.
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));

            var plan = MatrixPlanner.Plan(board, Legend());

            Assert.Single(plan.Tests);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void ClearingOneHalfClearsTheOther()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[1], board.Columns[0], null);

            Assert.Null(board.At(board.Rows[0], board.Columns[1]));
            Assert.Empty(MatrixPlanner.Plan(board, Legend()).Tests);
        }

        [Fact]
        public void ACellAgainstItselfIsARealTest()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[0], "A");

            Assert.Single(MatrixPlanner.Plan(board, Legend()).Tests);
        }

        [Fact]
        public void ASymbolTheLegendDoesNotExplainIsSkippedWithAReason()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "Z");

            var plan = MatrixPlanner.Plan(board, Legend());

            Assert.Empty(plan.Tests);
            Assert.Contains("legend does not say", plan.Skipped[0].Why);
        }

        [Fact]
        public void ALineThatIsNotPointedAtAnythingIsSkippedWithAReason()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts"));

            board.Set(row, column, "A");

            var plan = MatrixPlanner.Plan(board, Legend());

            Assert.Empty(plan.Tests);
            Assert.Contains("not pointed at anything", plan.Skipped[0].Why);
        }

        [Fact]
        public void ASymbolTurnedOffMakesNoTest()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "B");

            var legend = new MatrixLegend(new[]
            {
                new MatrixSymbol("A"),
                new MatrixSymbol("B", MatrixTest.Hard, 0.01, enabled: false),
            });

            var plan = MatrixPlanner.Plan(board, legend);

            Assert.Empty(plan.Tests);
            Assert.Contains("turned off", plan.Skipped[0].Why);
        }

        [Fact]
        public void TheCellsSymbolDecidesTheKindAndTheTolerance()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "C");

            var legend = new MatrixLegend(new[]
            {
                new MatrixSymbol("C", MatrixTest.Clearance, 0.05),
            });

            var test = MatrixPlanner.Plan(board, legend).Tests[0];

            Assert.Equal(MatrixTest.Clearance, test.Symbol.Test);
            Assert.Equal(0.05, test.Symbol.Tolerance);
        }

        [Fact]
        public void TwoCellsNamedTheSameByThePatternAreNumberedApart()
        {
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[0], "A");
            board.Set(board.Rows[1], board.Columns[1], "A");

            var names = MatrixPlanner.Plan(board, Legend(), "{symbol}").Tests
                                     .Select(t => t.Name).ToList();

            Assert.Equal(new[] { "A", "A (2)" }, names);
        }

        private static MatrixBoard Square(params string[] names)
        {
            var board = new MatrixBoard();

            foreach (var name in names)
                board.AddRow(new MatrixLine(name, null, MatrixSource.SelectionSet(name)));

            board.SquareOffRows();

            return board;
        }

        private static MatrixLegend Legend() =>
            new MatrixLegend(new[] { new MatrixSymbol("A") });
    }

    /// <summary>How a test name is built out of what a cell knows.</summary>
    /// <summary>What makes two sources the same source, and what does not.</summary>
    public class MatrixSourceTests
    {
        [Fact]
        public void TwoCapturesOfTheSameCountAreNotTheSameSource()
        {
            // The one that was wrong. Two tree picks holding the same number of elements
            // fingerprinted identically, so the planner dropped the second pair as a duplicate,
            // the difference matched it against the first one's test, and the mirror wrote one
            // row's cells into another's - none of it said out loud.
            var one = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("a", "b", "c"));
            var two = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("x", "y", "z"));

            Assert.NotEqual(one.Fingerprint(), two.Fingerprint());
        }

        [Fact]
        public void TheSameElementsInAnotherOrderAreTheSameSource()
        {
            var one = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("a", "b", "c"));
            var two = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("c", "a", "b"));

            Assert.Equal(one.Fingerprint(), two.Fingerprint());
        }

        [Fact]
        public void TwoKeysRunTogetherDoNotLookLikeOneLongerKey()
        {
            var one = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("ab", "c"));
            var two = MatrixSource.Captured(MatrixSourceKind.TreePick, Keys("a", "bc"));

            Assert.NotEqual(one.Fingerprint(), two.Fingerprint());
        }

        [Fact]
        public void ACaptureSurvivesBeingWrittenDownAndReadBack()
        {
            // The fingerprint has to be the same tomorrow, in another process, or a matrix saved
            // today matches nothing in the document it built.
            var source = MatrixSource.Captured(MatrixSourceKind.CurrentSelection, Keys("a", "b"));

            Assert.Equal(source.Fingerprint(),
                         MatrixSource.FromJson(source.ToJson()).Fingerprint());
        }

        [Fact]
        public void TheSameKindWithNoElementsIsStillTheSameSource()
        {
            Assert.Equal(
                MatrixSource.Captured(MatrixSourceKind.TreePick, Keys()).Fingerprint(),
                MatrixSource.Captured(MatrixSourceKind.TreePick, Keys()).Fingerprint());
        }

        // A real model scope, which is an eight-character hash. A shorter one makes a key that
        // will not parse back out of the project file, which is a fault in the test rather than
        // in the key.
        [Fact]
        public void TwoSetsOfTheSameNameInTwoFoldersAreTwoSources()
        {
            // A real document has "MEP" inside Level 01 and another inside Level 02. Told apart
            // only by name, a matrix pointed at twenty of them is pointed at one of them twenty
            // times, and the planner drops nineteen pairs as duplicates.
            Assert.NotEqual(
                MatrixSource.SelectionSet("MEP", "Level 01").Fingerprint(),
                MatrixSource.SelectionSet("MEP", "Level 02").Fingerprint());
        }

        [Fact]
        public void ASetInAFolderStillMatchesWhatTheDocumentReports()
        {
            // The document says what a test's set is called and not where it lives, so what a
            // plan is matched against has to be the name on its own - or every test built from a
            // matrix that knows its folders looks new the next time anybody presses Build.
            Assert.Equal(
                MatrixSource.SelectionSet("MEP").Matched(),
                MatrixSource.SelectionSet("MEP", "Level 01").Matched());
        }

        [Fact]
        public void AFolderSurvivesBeingWrittenDownAndReadBack()
        {
            var source = MatrixSource.SearchSet("MEP", "Level 01/Package B");
            var back = MatrixSource.FromJson(source.ToJson());

            Assert.Equal("Level 01/Package B", back.Folder);
            Assert.Equal(source.Fingerprint(), back.Fingerprint());
        }

        [Fact]
        public void AMatrixDrawnBeforeFoldersWereKeptStillNamesItsSet()
        {
            Assert.Equal(string.Empty, MatrixSource.SelectionSet("MEP").Folder);
            Assert.Equal("set:mep", MatrixSource.SelectionSet("MEP").Fingerprint());
        }

        private static List<Identity.ElementKey> Keys(params string[] names) =>
            names.Select(n => Identity.ElementKey.FromTreePath("a1b2c3d4", null, n)).ToList();
    }

    /// <summary>
    /// Making a twenty-discipline matrix by hand, in the order somebody actually makes one:
    /// type the names, square the columns off, then go round saying what each row is.
    /// </summary>
    public class MatrixByHandTests
    {
        private static MatrixBoard Named(params string[] names)
        {
            var board = new MatrixBoard();

            foreach (var name in names) board.AddRow(new MatrixLine(name));

            board.SquareOffRows();

            return board;
        }

        [Fact]
        public void PointingARowAfterSquaringOffKeepsThePairMirrored()
        {
            // The whole afternoon this cost. Twenty names typed, squared off, and then pointed
            // one row at a time: every row pointed stopped matching its own column, the pair
            // stopped being mirrored, and the triangle quietly became a square.
            var board = Named("Steel", "Ducts", "Pipes");

            Assert.False(board.IsDrawnHalf(board.Rows[1], board.Columns[0]));

            board.Point(board.Rows[0], MatrixSource.SelectionSet("STR"));

            Assert.NotNull(board.MirrorOf(board.Rows[0], board.Columns[1]));
            Assert.True(board.IsDrawnHalf(board.Rows[0], board.Columns[1]));
            Assert.False(board.IsDrawnHalf(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void PointingARowPointsTheColumnThatStandsForTheSameThing()
        {
            // Saying what a row is is one decision. The column standing for that discipline is
            // the same decision written twice, and a column pointing at nothing builds nothing.
            var board = Named("Steel", "Ducts");

            var twin = board.Point(board.Rows[0], MatrixSource.SelectionSet("STR"));

            Assert.Same(board.Columns[0], twin);
            Assert.Equal("STR", board.Columns[0].Source.Name);

            // A twin somebody has already pointed somewhere else is left where it is: two axes
            // are allowed to diverge on purpose.
            board.Columns[1].Source = MatrixSource.SelectionSet("MEP-columns");

            Assert.Null(board.Point(board.Rows[1], MatrixSource.SelectionSet("MEP")));
            Assert.Equal("MEP-columns", board.Columns[1].Source.Name);
        }

        [Fact]
        public void SquaringOffAgainAfterARenameKeepsTheDecisions()
        {
            // Rename a row, press "make the columns match the rows" to tidy up, and every
            // decision in that column used to be dropped: no count, no message, and the only way
            // back was noticing in time to press undo.
            var board = Named("Steel", "Ducts", "Pipes");

            board.Set(board.Rows[1], board.Columns[0], "A");
            board.Set(board.Rows[2], board.Columns[0], "B");

            board.Rows[0].Name = "Steel_L01";
            board.SquareOffRows();

            Assert.Equal("Steel_L01", board.Columns[0].Name);
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));
            Assert.Equal("B", board.At(board.Rows[2], board.Columns[0]));
        }

        [Fact]
        public void OnePressFillsEveryPairNobodyHasRuledOn()
        {
            var board = Named("Steel", "Ducts", "Pipes");

            board.Set(board.Rows[0], board.Columns[1], "B");

            // Three pairs in the live half; one is already decided, so two are filled - and the
            // diagonal is never one of them.
            Assert.Equal(2, board.FillEmpty("A"));

            Assert.Equal("B", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("A", board.At(board.Rows[0], board.Columns[2]));
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[2]));

            Assert.Null(board.At(board.Rows[0], board.Columns[0]));
            Assert.Equal(0, board.FillEmpty("A"));
        }

        [Fact]
        public void MovingALineMovesTheOneStandingForItOnTheOtherAxis()
        {
            // The block gesture is a rectangle over the order the two axes are in, so grouping a
            // matrix has to be one job. It was two, and getting it wrong once was enough.
            var board = Named("Steel", "Ducts", "Pipes");

            Assert.True(board.MoveLine(board.Rows[2], 0));

            Assert.Equal(new[] { "Pipes", "Steel", "Ducts" },
                         board.Rows.Select(r => r.Name).ToArray());

            Assert.Equal(new[] { "Pipes", "Steel", "Ducts" },
                         board.Columns.Select(c => c.Name).ToArray());
        }

        [Fact]
        public void TheLegendKeepsTheOrderItsSymbolsWereMadeIn()
        {
            // The order is the number key. Sorted by text, adding a symbol that sorts early
            // silently renumbered every key under it, across every cell typed afterwards.
            var legend = new MatrixLegend(new[]
            {
                new MatrixSymbol("H"), new MatrixSymbol("C"), new MatrixSymbol("B"),
            });

            Assert.Equal(new[] { "H", "C", "B" }, legend.Symbols.Select(s => s.Symbol).ToArray());

            var grown = new MatrixLegend(legend.Symbols.Concat(new[] { new MatrixSymbol("AA") }));

            Assert.Equal(new[] { "H", "C", "B", "AA" },
                         grown.Symbols.Select(s => s.Symbol).ToArray());

            // And an edit is a new legend built out of the old one, so an edited symbol must not
            // jump to the end and renumber everything under it either.
            var edited = new MatrixLegend(grown.Symbols
                .Select(s => s.Symbol == "C" ? new MatrixSymbol("C", MatrixTest.Clearance, 0.05) : s));

            Assert.Equal(new[] { "H", "C", "B", "AA" },
                         edited.Symbols.Select(s => s.Symbol).ToArray());

            Assert.Equal(MatrixTest.Clearance, edited.Symbols[1].Test);
        }
    }

    /// <summary>
    /// How a matrix is drawn, which the matrix carries rather than the seat it is opened on.
    /// </summary>
    public class MatrixStyleTests
    {
        private static MatrixBoard Square(params string[] names)
        {
            var board = new MatrixBoard();

            foreach (var name in names)
                board.AddRow(new MatrixLine(name, null, MatrixSource.SelectionSet(name)));

            board.SquareOffRows();

            return board;
        }

        [Fact]
        public void EitherHalfCanBeTheOneThatIsWorkedIn()
        {
            var board = Square("Steel", "Ducts", "Pipes");

            // Put a symbol in from one side; both cells that name the pair carry it, which is
            // what makes flipping the drawing safe in the first place.
            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));

            Assert.True(board.IsDrawnHalf(board.Rows[0], board.Columns[1]));
            Assert.False(board.IsDrawnHalf(board.Rows[1], board.Columns[0]));

            board.UpperIsLive = false;

            Assert.False(board.IsDrawnHalf(board.Rows[0], board.Columns[1]));
            Assert.True(board.IsDrawnHalf(board.Rows[1], board.Columns[0]));

            // The diagonal is its own mirror and is drawn in either half.
            Assert.True(board.IsDrawnHalf(board.Rows[2], board.Columns[2]));

            // And what the matrix keeps does not move with the drawing: the cell the planner
            // walks and names the test from is the earlier one, always.
            Assert.True(board.IsKeptHalf(board.Rows[0], board.Columns[1]));
            Assert.False(board.IsKeptHalf(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void FlippingTheHalfChangesNothingAboutTheTests()
        {
            var board = Square("Steel", "Ducts", "Pipes");

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], "B");

            var legend = new MatrixLegend(new[] { new MatrixSymbol("A"), new MatrixSymbol("B") });

            var upper = MatrixPlanner.Plan(board, legend);

            board.UpperIsLive = false;

            var lower = MatrixPlanner.Plan(board, legend);

            Assert.Equal(upper.Tests.Count, lower.Tests.Count);
            Assert.Equal(upper.Tests.Select(t => t.Name).OrderBy(n => n),
                         lower.Tests.Select(t => t.Name).OrderBy(n => n));
        }

        [Fact]
        public void WhichHalfIsLiveIsSavedWithTheMatrix()
        {
            var board = Square("Steel", "Ducts");

            board.UpperIsLive = false;

            var read = MatrixBoard.FromJson(board.ToJson());

            Assert.False(read.UpperIsLive);
            Assert.True(MatrixBoard.FromJson(Square("Steel", "Ducts").ToJson()).UpperIsLive);
        }

        [Fact]
        public void RenamingASymbolTakesItsCellsWithIt()
        {
            // What it replaced: taking the symbol out of the legend, which orphaned every cell
            // using it, and then painting them all again.
            var board = Square("Steel", "Ducts", "Pipes");

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], "A");
            board.Set(board.Rows[1], board.Columns[2], "B");

            Assert.Equal(4, board.Recast("a", "H"));

            Assert.Equal("H", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("H", board.At(board.Rows[1], board.Columns[0]));
            Assert.Equal("H", board.At(board.Rows[0], board.Columns[2]));
            Assert.Equal("B", board.At(board.Rows[1], board.Columns[2]));

            Assert.Equal(0, board.Recast("Z", "H"));
            Assert.Equal(0, board.Recast(null, "H"));
        }

        [Fact]
        public void ASymbolKeepsAColourOfItsOwnAndRefusesAnythingThatIsNotOne()
        {
            Assert.Equal("#DDF2E4", new MatrixSymbol("B", colour: "#ddf2e4").Colour);
            Assert.Equal("#ABC", new MatrixSymbol("B", colour: " #abc ").Colour);

            // Not a colour, and Core has no idea what one is - so anything that is not plainly
            // one is dropped here rather than guarded against by every screen that reads it.
            Assert.Equal(string.Empty, new MatrixSymbol("B", colour: "green").Colour);
            Assert.Equal(string.Empty, new MatrixSymbol("B", colour: "#12345").Colour);
            Assert.Equal(string.Empty, new MatrixSymbol("B", colour: "#GGGGGG").Colour);
            Assert.Equal(string.Empty, new MatrixSymbol("B").Colour);
        }

        [Fact]
        public void AFieldIsDrawnWhenItSaysSomething_UnlessSomebodySaidOtherwise()
        {
            Assert.True(MatrixStyle.Drawn(MatrixField.Auto, worthIt: true));
            Assert.False(MatrixStyle.Drawn(MatrixField.Auto, worthIt: false));

            Assert.True(MatrixStyle.Drawn(MatrixField.Shown, worthIt: false));
            Assert.False(MatrixStyle.Drawn(MatrixField.Hidden, worthIt: true));
        }

        [Fact]
        public void TheStyleIsWrittenDownAndReadBack()
        {
            var style = new MatrixStyle
            {
                UpperIsLive = false,
                Echo = false,
                Spine = false,
                SpineColour = "#CBD5E3",
                Numbers = MatrixField.Shown,
                Codes = MatrixField.Hidden,
                Warnings = MatrixField.Shown,
            };

            var read = MatrixStyle.FromJson(style.ToJson());

            Assert.False(read.UpperIsLive);
            Assert.False(read.Echo);
            Assert.False(read.Spine);
            Assert.Equal("#CBD5E3", read.SpineColour);
            Assert.Equal(MatrixField.Shown, read.Numbers);
            Assert.Equal(MatrixField.Hidden, read.Codes);
            Assert.Equal(MatrixField.Shown, read.Warnings);

            // Nothing stored is the ordinary one, not a blank one: a matrix saved before any of
            // this existed opens drawn the way it was drawn then.
            var fresh = MatrixStyle.FromJson(null);

            Assert.True(fresh.UpperIsLive);
            Assert.True(fresh.Echo);
            Assert.True(fresh.Spine);
            Assert.Equal(MatrixField.Auto, fresh.Numbers);
        }
    }

    /// <summary>The mark for a pair somebody ruled out on purpose.</summary>
    public class MatrixNotRequiredTests
    {
        [Fact]
        public void ARuledOutPairMakesNoTestAndIsNotAFault()
        {
            var plan = MatrixPlanner.Plan(Ruled(), new MatrixLegend(new[] { new MatrixSymbol("A") }));

            Assert.Empty(plan.Tests);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void TheReservedMarkIsNeverUnexplained()
        {
            // Offering to give it a tolerance is offering to turn every pair somebody ruled out
            // into a test, which is exactly what a reserved mark exists to prevent.
            var legend = new MatrixLegend(new[] { new MatrixSymbol("A") });

            Assert.Empty(legend.Unexplained(Ruled()));
            Assert.Single(legend.Covering(Ruled()).Symbols);
        }

        [Fact]
        public void ARuledOutPairIsNotTheSameAsAnEmptyOne()
        {
            var board = Ruled();

            // The pair that was ruled out carries the mark on both sides of the diagonal,
            // because a pair is only ever run once whichever half somebody was filling in.
            Assert.Equal(MatrixBoard.NotRequired, board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal(MatrixBoard.NotRequired, board.At(board.Rows[1], board.Columns[0]));

            // And a pair nobody has ruled on is still empty, which is the whole distinction.
            Assert.Null(board.At(board.Rows[0], board.Columns[0]));
        }

        [Fact]
        public void TheSheetSaysWhatTheMarkMeansInWords()
        {
            // Where it matters most: a client reading a printed matrix cannot hover a cell.
            var sheet = MatrixSanity.AsSheet(Ruled(), new MatrixLegend());

            Assert.Contains(sheet.Rows,
                row => row.Any(c => c != null && c.Contains("Not required")));
        }

        private static MatrixBoard Ruled()
        {
            var board = new MatrixBoard();

            board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            board.AddRow(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));
            board.SquareOffRows();

            board.Set(board.Rows[0], board.Columns[1], MatrixBoard.NotRequired);

            return board;
        }
    }

    public class MatrixNamingTests
    {
        [Fact]
        public void EveryTokenIsFilledIn()
        {
            var row = new MatrixLine("Steel", "01", MatrixSource.SelectionSet("STR"));
            var column = new MatrixLine("Ducts", "02", MatrixSource.SelectionSet("MEP"));
            var symbol = new MatrixSymbol("A", MatrixTest.Clearance, 0.05);

            var named = MatrixNaming.Fill(
                "{rowNumber}-{row} / {columnNumber}-{column} [{symbol} {test} {tolerance}] #{n}",
                row, column, symbol, 7);

            Assert.Equal("01-Steel / 02-Ducts [A Clearance 50] #7", named);
        }

        [Fact]
        public void AnEmptyTokenDoesNotLeaveADoubleSpace()
        {
            var row = new MatrixLine("Steel");
            var column = new MatrixLine("Ducts");

            Assert.Equal("Steel vs Ducts",
                         MatrixNaming.Fill("{rowNumber} {row} vs {columnNumber} {column}",
                                           row, column, null, 1));
        }

        [Fact]
        public void SomethingThatIsNotATokenIsLeftExactlyAsTyped()
        {
            // Somebody halfway through typing "{ro" should see it, not a name that has quietly
            // lost a word.
            var named = MatrixNaming.Fill("{ro} {row}", new MatrixLine("Steel"),
                                          new MatrixLine("Ducts"), null, 1);

            Assert.Equal("{ro} Steel", named);
        }

        [Fact]
        public void NoPatternMeansTheDefaultOne()
        {
            Assert.Equal("Steel vs Ducts",
                         MatrixNaming.Fill(null, new MatrixLine("Steel"),
                                           new MatrixLine("Ducts"), null, 1));
        }

        [Fact]
        public void EveryTokenTheBuilderOffersIsOneFillKnows()
        {
            var row = new MatrixLine("Steel", "01", MatrixSource.SelectionSet("STR"));
            var column = new MatrixLine("Ducts", "02", MatrixSource.SelectionSet("MEP"));
            var symbol = new MatrixSymbol("A");

            foreach (var token in MatrixNaming.Tokens)
                Assert.DoesNotContain(token.Token,
                    MatrixNaming.Fill(token.Token + " x", row, column, symbol, 1));
        }
    }

    /// <summary>What has to change for the document to match the matrix.</summary>
    public class MatrixDiffTests
    {
        [Fact]
        public void ATestTheDocumentDoesNotHaveIsAnAddition()
        {
            var lines = MatrixDiff.Between(Plan("A"), Array.Empty<ClashTestRead>());

            Assert.Equal(MatrixChange.Added, Assert.Single(lines).Change);
        }

        [Fact]
        public void ATestTheDocumentAlreadyHasIsLeftAlone()
        {
            var lines = MatrixDiff.Between(Plan("A"), new[] { Read("Steel vs Ducts", 0.01) });

            Assert.Equal(MatrixChange.Unchanged, Assert.Single(lines).Change);
        }

        [Fact]
        public void ARenameIsNotANewTest()
        {
            // The whole reason a test is matched on what it is between rather than on its name:
            // changing the naming pattern must not delete and remake every test on the job.
            var lines = MatrixDiff.Between(Plan("A"), new[] { Read("STR-01 vs MEP-02", 0.01) });

            var line = Assert.Single(lines);

            Assert.Equal(MatrixChange.Altered, line.Change);
            Assert.Contains("named", line.What);
        }

        [Fact]
        public void ADifferentToleranceIsAnAlteration()
        {
            var lines = MatrixDiff.Between(Plan("A"), new[] { Read("Steel vs Ducts", 0.05) });

            var line = Assert.Single(lines);

            Assert.Equal(MatrixChange.Altered, line.Change);
            Assert.Contains("tolerance", line.What);
        }

        [Fact]
        public void ATestNoCellAsksForIsExtraRatherThanDeleted()
        {
            var lines = MatrixDiff.Between(Plan("A"),
                new[]
                {
                    Read("Steel vs Ducts", 0.01),

                    // Its own id. A document never gives two tests the same one, and the
                    // difference claims a test by its id so that one test can only ever answer
                    // for one cell.
                    Read("Something else", 0.01, "OTH", "XYZ", id: "t2"),
                });

            Assert.Equal(1, MatrixDiff.Count(lines, MatrixChange.Unchanged));
            Assert.Equal(1, MatrixDiff.Count(lines, MatrixChange.Extra));
        }

        [Fact]
        public void ATestTheHostWillNotDescribeIsNeitherClaimedNorDeleted()
        {
            var blind = new ClashTestRead(
                new ClashTestInfo("t9", "Hand-picked", null, 0, 0), null,
                Array.Empty<ClashResultInfo>());

            var lines = MatrixDiff.Between(Plan("A"), new[] { blind });

            var extra = lines.Single(l => l.Change == MatrixChange.Extra);

            Assert.Contains("will not say", extra.What);
        }

        [Fact]
        public void ChangingTheKindAltersTheTestRatherThanStrandingIt()
        {
            // The one that cost the most. A symbol moved from a hard clash to a clearance used
            // to match nothing: the test carrying a fortnight of statuses fell into Extra and
            // was never touched again, and a second test was made beside it.
            var lines = MatrixDiff.Between(
                Plan("A", MatrixTest.Clearance, 0.05),
                new[] { Read("Steel vs Ducts", 0.01) });

            var line = Assert.Single(lines);

            Assert.Equal(MatrixChange.Altered, line.Change);
            Assert.Equal("t1", line.TestId);
            Assert.True(line.Reshapes);
            Assert.Contains("clearance", line.What);
            Assert.Equal(0, MatrixDiff.Count(lines, MatrixChange.Extra));
        }

        [Fact]
        public void ARenameSaysItIsOnlyARename()
        {
            var line = Assert.Single(
                MatrixDiff.Between(Plan("A"), new[] { Read("STR-01 vs MEP-02", 0.01) }));

            Assert.True(line.Renames);
            Assert.False(line.Reshapes);
        }

        [Fact]
        public void ARetuneSaysItIsNotARename()
        {
            var line = Assert.Single(
                MatrixDiff.Between(Plan("A"), new[] { Read("Steel vs Ducts", 0.05) }));

            Assert.False(line.Renames);
            Assert.True(line.Reshapes);
        }

        [Fact]
        public void TwoKindsBetweenTheSameSetsAreStillTwoTests()
        {
            // The reason the exact match runs first. A hard clash and a clearance between the
            // same two disciplines are two tests somebody meant to have, and the looser match
            // must not collapse them into one.
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var one = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));
            var two = board.AddColumn(new MatrixLine("Ducts again", null, MatrixSource.SelectionSet("MEP")));

            board.Set(row, one, "A");
            board.Set(row, two, "B");

            var plan = MatrixPlanner.Plan(board, new MatrixLegend(new[]
            {
                new MatrixSymbol("A", MatrixTest.Hard, 0.01, true),
                new MatrixSymbol("B", MatrixTest.Clearance, 0.05, true),
            }));

            var lines = MatrixDiff.Between(plan, new[]
            {
                Read("Steel vs Ducts", 0.01),
                Read("Steel vs Ducts again", 0.05, kind: "Clearance", id: "t2"),
            });

            Assert.Equal(2, MatrixDiff.Count(lines, MatrixChange.Unchanged));
            Assert.Equal(0, MatrixDiff.Count(lines, MatrixChange.Extra));
        }

        [Fact]
        public void OneDocumentTestIsOnlyEverClaimedOnce()
        {
            // Two cells asking for the same pair with different kinds, and one test in the
            // document. The looser match may take it for one of them, never for both.
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var one = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));
            var two = board.AddColumn(new MatrixLine("Ducts again", null, MatrixSource.SelectionSet("MEP")));

            board.Set(row, one, "A");
            board.Set(row, two, "B");

            var plan = MatrixPlanner.Plan(board, new MatrixLegend(new[]
            {
                new MatrixSymbol("A", MatrixTest.Hard, 0.01, true),
                new MatrixSymbol("B", MatrixTest.Clearance, 0.05, true),
            }));

            var lines = MatrixDiff.Between(plan, new[] { Read("Steel vs Ducts", 0.01) });

            Assert.Equal(1, MatrixDiff.Count(lines, MatrixChange.Unchanged));
            Assert.Equal(1, MatrixDiff.Count(lines, MatrixChange.Added));
            Assert.Equal(0, MatrixDiff.Count(lines, MatrixChange.Extra));
        }

        private static MatrixPlan Plan(string symbol, MatrixTest kind = MatrixTest.Hard,
                                       double tolerance = 0.01)
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));

            board.Set(row, column, symbol);

            return MatrixPlanner.Plan(board, new MatrixLegend(new[]
            {
                new MatrixSymbol(symbol, kind, tolerance, true),
            }));
        }

        private static ClashTestRead Read(string name, double tolerance,
                                          string setA = "STR", string setB = "MEP",
                                          string kind = "Hard", string id = "t1") =>
            new ClashTestRead(
                new ClashTestInfo(id, name, null, 0, 0),
                new ClashTestSides(setA, setB, Array.Empty<string>(), Array.Empty<string>(),
                                   kind, tolerance),
                Array.Empty<ClashResultInfo>());
    }

    /// <summary>A matrix read back out of the tests the document already holds.</summary>
    public class MatrixFromModelTests
    {
        [Fact]
        public void EverySourceBecomesARowAndAColumn()
        {
            var reading = MatrixFromModel.Read(new[]
            {
                Read("one", "STR", "MEP", "Hard", 0.01),
                Read("two", "STR", "ARC", "Hard", 0.01),
            });

            Assert.Equal(3, reading.Board.Rows.Count);
            Assert.Equal(3, reading.Board.Columns.Count);
            Assert.Equal(2, reading.Tests);
        }

        [Fact]
        public void TheLegendIsBuiltFromWhatTheTestsActuallyUse()
        {
            var reading = MatrixFromModel.Read(new[]
            {
                Read("one", "STR", "MEP", "Hard", 0.01),
                Read("two", "STR", "ARC", "Clearance", 0.05),
                Read("three", "MEP", "ARC", "Hard", 0.01),
            });

            var symbols = reading.Legend.Symbols;

            Assert.Equal(2, symbols.Count);
            Assert.Equal(new[] { "A", "B" }, symbols.Select(s => s.Symbol));
            Assert.Equal(MatrixTest.Clearance, symbols[1].Test);
            Assert.Equal(0.05, symbols[1].Tolerance);
        }

        [Fact]
        public void BothHalvesAreFilledSoItLooksLikeAMatrix()
        {
            var reading = MatrixFromModel.Read(new[] { Read("one", "STR", "MEP", "Hard", 0.01) });

            var board = reading.Board;

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void ATestTheHostWillNotDescribeIsSaidRatherThanDropped()
        {
            var blind = new ClashTestRead(
                new ClashTestInfo("t9", "Hand-picked", null, 0, 0), null,
                Array.Empty<ClashResultInfo>());

            var reading = MatrixFromModel.Read(new[] { blind });

            Assert.Equal(0, reading.Tests);
            Assert.Contains("does not say what it is between", Assert.Single(reading.Warnings));
        }

        [Fact]
        public void WhatComesBackPlansTheSameTestsItCameFrom()
        {
            // The round trip that matters: read the model, plan it again, and the diff says
            // nothing has to change.
            var already = new[]
            {
                Read("one", "STR", "MEP", "Hard", 0.01),
                Read("two", "STR", "ARC", "Clearance", 0.05),
            };

            var reading = MatrixFromModel.Read(already);
            var plan = MatrixPlanner.Plan(reading.Board, reading.Legend);

            Assert.Equal(2, plan.Tests.Count);
            Assert.Equal(0, MatrixDiff.Count(MatrixDiff.Between(plan, already), MatrixChange.Added));
        }

        private static ClashTestRead Read(string id, string a, string b, string kind, double tolerance) =>
            new ClashTestRead(
                new ClashTestInfo(id, id, null, 0, 0),
                new ClashTestSides(a, b, Array.Empty<string>(), Array.Empty<string>(),
                                   kind, tolerance),
                Array.Empty<ClashResultInfo>());
    }

    /// <summary>What is wrong with a matrix, said before anything is built.</summary>
    public class MatrixSanityTests
    {
        [Fact]
        public void ASetTheModelNoLongerHasStopsTheBuild()
        {
            var board = new MatrixBoard();

            board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            board.SquareOffRows();

            var faults = MatrixSanity.Check(board, new MatrixLegend(),
                                            new[] { "MEP", "ARC" }, null);

            Assert.True(MatrixSanity.Stopping(faults) > 0);
            Assert.Contains(faults, f => f.What.Contains("no set by that name"));
        }

        [Fact]
        public void ASetTheModelHasIsFine()
        {
            var board = new MatrixBoard();

            board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            board.SquareOffRows();

            Assert.Equal(0, MatrixSanity.Stopping(
                MatrixSanity.Check(board, new MatrixLegend(), new[] { "STR" }, null)));
        }

        [Fact]
        public void ACaptureIsWorthKnowingAboutButDoesNotStopAnything()
        {
            var board = new MatrixBoard();

            board.AddRow(new MatrixLine("Picked", null,
                MatrixSource.Captured(MatrixSourceKind.CurrentSelection,
                                      Array.Empty<Core.Identity.ElementKey>())));

            board.SquareOffRows();

            var faults = MatrixSanity.Check(board, new MatrixLegend(), new[] { "STR" }, null);

            // Empty, so it does stop - but for having nothing in it, not for being a capture.
            Assert.Contains(faults, f => f.What.Contains("not pointed at anything"));
        }

        [Fact]
        public void ASymbolNotInTheLegendIsAFault()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));

            board.Set(row, column, "Z");

            var faults = MatrixSanity.Check(board, new MatrixLegend(),
                                            new[] { "STR", "MEP" }, null);

            Assert.Contains(faults, f => f.What.Contains("not in the legend"));
        }

        [Fact]
        public void TheSheetCarriesTheGridAndTheLegendUnderIt()
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", "01", MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts", "02", MatrixSource.SelectionSet("MEP")));

            board.Set(row, column, "A");

            var sheet = MatrixSanity.AsSheet(board, new MatrixLegend(new[]
            {
                new MatrixSymbol("A", MatrixTest.Clearance, 0.05),
            }));

            Assert.Equal(new[] { "#", "Row", "Points at", "02 Ducts" }, sheet.Rows[0]);

            // What the row is checking, beside its name: two rows called "Ductwork" on two
            // different sets used to export identically.
            Assert.Equal(new[] { "01", "Steel", "selection set: STR", "A" }, sheet.Rows[1]);

            Assert.Contains(sheet.Rows, r => r.Count > 0 && r[0] == "Legend");
            Assert.Contains(sheet.Rows, r => r.Count > 1 && r[1] != null
                                             && r[1]!.Contains("Clearance at 50 mm"));
        }

        [Fact]
        public void TheSheetSaysWhatABlankCellMeans()
        {
            // The dash exists because blank meant two opposite things. A sheet that spells the
            // dash out and leaves the blank to be guessed at has put the ambiguity straight back,
            // at the moment - an issue to a client - when it matters most.
            var board = new MatrixBoard();

            foreach (var name in new[] { "Steel", "Ducts", "Pipes" })
                board.AddRow(new MatrixLine(name, null, MatrixSource.SelectionSet(name)));

            board.SquareOffRows();
            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], MatrixBoard.NotRequired);

            var sheet = MatrixSanity.AsSheet(board, new MatrixLegend(new[] { new MatrixSymbol("A") }));

            Assert.Contains(sheet.Rows, r => r.Count > 1 && r[0] == "(blank)"
                                             && r[1]!.Contains("Nobody has ruled on this pair"));

            Assert.Contains(sheet.Rows, r => r.Count > 1 && r[0] == MatrixBoard.NotRequired
                                             && r[1]!.Contains("agreed"));

            // And a symbol nobody explained gets a line saying so, rather than sitting in the
            // grid as a letter with no key.
            board.Set(board.Rows[1], board.Columns[2], "Q");

            var again = MatrixSanity.AsSheet(board, new MatrixLegend(new[] { new MatrixSymbol("A") }));

            Assert.Contains(again.Rows, r => r.Count > 1 && r[0] == "Q"
                                             && r[1]!.Contains("Not in the legend"));
        }
    }

    /// <summary>
    /// Both axes are one list of disciplines, which is what makes it a clash matrix.
    /// </summary>
    public class MatrixPairedTests
    {
        [Fact]
        public void AddingACategoryPutsItOnBothAxes()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure", "10"));
            board.AddPair(new MatrixLine("Services", "20"));

            Assert.Equal(2, board.Rows.Count);
            Assert.Equal(2, board.Columns.Count);
            Assert.Equal(new[] { "Structure", "Services" }, board.Rows.Select(r => r.Name));
            Assert.Equal(new[] { "Structure", "Services" }, board.Columns.Select(c => c.Name));
            Assert.Equal(new[] { "10", "20" }, board.Columns.Select(c => c.Number));
        }

        [Fact]
        public void TheColumnItAddsIsTheRowsTwin()
        {
            var board = new MatrixBoard();
            var row = board.AddPair(new MatrixLine("Structure"));

            var twin = board.TwinOf(row);

            Assert.NotNull(twin);
            Assert.Same(board.Columns[0], twin);
            Assert.NotSame(row, twin);
        }

        [Fact]
        public void APairAddedIsAPairOnTheDiagonal()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            Assert.True(board.IsSelfPair(board.Rows[0], board.Columns[0]));
            Assert.False(board.IsSelfPair(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void WritingOneCellWritesItsMirror()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void RemovingACategoryTakesBothAxes()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            board.RemovePair(board.Rows[0]);

            Assert.Single(board.Rows);
            Assert.Single(board.Columns);
            Assert.Equal("Services", board.Rows[0].Name);
            Assert.Equal("Services", board.Columns[0].Name);
        }

        [Fact]
        public void RemovingByTheColumnTakesTheRowToo()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            board.RemovePair(board.Columns[1]);

            Assert.Single(board.Rows);
            Assert.Single(board.Columns);
            Assert.Equal("Structure", board.Rows[0].Name);
        }

        [Fact]
        public void KeptApartTheAxesDoNotFollowEachOther()
        {
            var board = new MatrixBoard { Paired = false };

            board.AddPair(new MatrixLine("Structure"));

            Assert.Single(board.Rows);
            Assert.Empty(board.Columns);
        }

        [Fact]
        public void KeptApartRemovingOneLeavesTheOther()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            board.Paired = false;
            board.RemovePair(board.Rows[0]);

            Assert.Single(board.Rows);
            Assert.Equal(2, board.Columns.Count);
        }

        [Fact]
        public void RenamingACategoryRenamesBothAxes()
        {
            var board = new MatrixBoard();
            var row = board.AddPair(new MatrixLine("Structure"));

            board.Rename(row, "Structural");

            Assert.Equal("Structural", board.Rows[0].Name);
            Assert.Equal("Structural", board.Columns[0].Name);
        }

        [Fact]
        public void PointingACategoryPointsBothAxes()
        {
            var board = new MatrixBoard();
            var row = board.AddPair(new MatrixLine("Structure"));

            board.Point(row, MatrixSource.SelectionSet("STR"));

            Assert.Equal("STR", board.Columns[0].Source.Name);
        }

        [Fact]
        public void WhetherTheAxesArePairedSurvivesASaveAndRead()
        {
            var board = new MatrixBoard { Paired = false };

            board.AddRow(new MatrixLine("Structure"));

            var read = MatrixBoard.FromJson(board.ToJson());

            Assert.False(read.Paired);
        }

        [Fact]
        public void APairedBoardIsPairedWhenItComesBack()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure"));

            var read = MatrixBoard.FromJson(board.ToJson());

            Assert.True(read.Paired);
            Assert.Single(read.Rows);
            Assert.Single(read.Columns);
        }
    }

    /// <summary>
    /// The exported workbook has to be a workbook Excel will open.
    ///
    /// Every one of these writes the real file and parses the real part out of the real zip,
    /// because "it produced bytes" is not the claim - the claim is that a spreadsheet opens it,
    /// and the only cheap stand-in for that is an XML parser refusing what Excel would refuse.
    /// </summary>
    public class MatrixSheetTests
    {
        private static string Part(byte[] book, string path)
        {
            using var stream = new MemoryStream(book);
            using var zip = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Read);

            var entry = zip.GetEntry(path);

            Assert.NotNull(entry);

            using var held = entry!.Open();
            using var text = new StreamReader(held);

            return text.ReadToEnd();
        }

        private static MatrixBoard Peopled()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Structure", "10", MatrixSource.SelectionSet("STR")));
            board.AddPair(new MatrixLine("Services", "20", MatrixSource.SelectionSet("MEP")));
            board.AddPair(new MatrixLine("Architecture", "30"));

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], MatrixBoard.NotRequired);

            return board;
        }

        [Fact]
        public void TheSheetIsWellFormedXml()
        {
            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(Peopled(), new MatrixLegend()) });

            XDocument.Parse(Part(book, "xl/worksheets/sheet1.xml"));
        }

        [Fact]
        public void EveryPartOfTheWorkbookIsWellFormedXml()
        {
            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(Peopled(), new MatrixLegend()) });

            using var stream = new MemoryStream(book);
            using var zip = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Read);

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal)
                    && !entry.FullName.EndsWith(".rels", StringComparison.Ordinal)) continue;

                using var held = entry.Open();
                using var text = new StreamReader(held);

                XDocument.Parse(text.ReadToEnd());
            }
        }

        [Fact]
        public void AnEmptyMatrixStillWritesASheetThatOpens()
        {
            var book = XlsxWriter.Write(
                new[] { MatrixSanity.AsSheet(new MatrixBoard(), new MatrixLegend()) });

            XDocument.Parse(Part(book, "xl/worksheets/sheet1.xml"));
        }

        [Fact]
        public void ANameWithMarkupInItDoesNotBreakTheSheet()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Pipes <2\" & fittings>", "1"));
            board.AddPair(new MatrixLine("Ducts", "2"));
            board.Set(board.Rows[0], board.Columns[1], "A");

            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(board, new MatrixLegend()) });

            var sheet = XDocument.Parse(Part(book, "xl/worksheets/sheet1.xml"));

            var text = sheet.Descendants()
                            .Where(e => e.Name.LocalName == "t")
                            .Select(e => e.Value);

            Assert.Contains("Pipes <2\" & fittings>", text);
        }

        /// <summary>
        /// <b>A worksheet's children are a sequence, not a set.</b> sheetViews, then cols, then
        /// sheetData. Out of order the file parses perfectly and Excel throws the part away,
        /// which is a corruption no XML check can see - so it is checked here by name.
        /// </summary>
        [Fact]
        public void TheWorksheetsChildrenAreInTheOrderTheSchemaWants()
        {
            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(Peopled(), new MatrixLegend()) });

            var sheet = XDocument.Parse(Part(book, "xl/worksheets/sheet1.xml"));

            var order = sheet.Root!.Elements().Select(e => e.Name.LocalName).ToList();

            Assert.Equal(new[] { "sheetViews", "cols", "sheetData" }, order);
        }

        /// <summary>
        /// <b>Three things in the stylesheet that look like padding and are not.</b> Excel treats
        /// a stylesheet without them as a damaged workbook and offers to repair it - which is
        /// what it did - and no XML check can see any of it, so they are named here.
        /// </summary>
        [Fact]
        public void TheStylesheetHasWhatExcelRefusesToOpenWithout()
        {
            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(Peopled(), new MatrixLegend()) });

            var styles = XDocument.Parse(Part(book, "xl/styles.xml"));

            string Local(string name) => name;

            var fills = styles.Descendants().First(e => e.Name.LocalName == "fills");

            // The reserved second fill, whether or not anything uses it.
            Assert.Equal("2", fills.Attribute("count")!.Value);
            Assert.Contains(fills.Descendants(),
                e => e.Name.LocalName == "patternFill"
                     && e.Attribute("patternType")?.Value == "gray125");

            // A default style for everything else to inherit from.
            Assert.Contains(styles.Descendants(),
                e => e.Name.LocalName == "cellStyle" && e.Attribute("name")?.Value == "Normal");

            // Every format saying what it is outright: left off, they are absent, not defaulted.
            foreach (var xf in styles.Descendants().Where(e => e.Name.LocalName == Local("xf")))
            {
                Assert.NotNull(xf.Attribute("numFmtId"));
                Assert.NotNull(xf.Attribute("fontId"));
                Assert.NotNull(xf.Attribute("fillId"));
                Assert.NotNull(xf.Attribute("borderId"));
            }
        }

        /// <summary>Nothing may name the theme part, because no theme part is shipped.</summary>
        [Fact]
        public void NothingPointsAtAThemeThatIsNotInThePackage()
        {
            var book = XlsxWriter.Write(new[] { MatrixSanity.AsSheet(Peopled(), new MatrixLegend()) });

            using var stream = new MemoryStream(book);
            using var zip = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Read);

            Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("theme"));

            var styles = Part(book, "xl/styles.xml");

            Assert.DoesNotContain("theme=", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("scheme", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void EverySheetThisProductWritesPutsThemInThatOrder()
        {
            // The frozen header is on by default and a one-row sheet was the only shape that
            // escaped the fault, so both shapes are checked.
            var one = new XlsxSheet("One");

            one.Row("Only");

            var many = new XlsxSheet("Many");

            many.Row("Head");
            many.Row("Body");

            foreach (var made in new[] { one, many })
            {
                var book = XlsxWriter.Write(new[] { made });
                var sheet = XDocument.Parse(Part(book, "xl/worksheets/sheet1.xml"));

                var order = sheet.Root!.Elements().Select(e => e.Name.LocalName).ToList();
                var wanted = order.Where(n => n == "sheetViews" || n == "cols" || n == "sheetData");

                Assert.Equal(wanted, order);
            }
        }
    }

    /// <summary>
    /// Folders: a name several lines share, kept together, and mirrored like everything else.
    /// </summary>
    public class MatrixFolderTests
    {
        private static MatrixBoard Peopled(params string[] names)
        {
            var board = new MatrixBoard();

            foreach (var name in names) board.AddPair(new MatrixLine(name));

            return board;
        }

        [Fact]
        public void ALineFiledInAFolderTakesItsTwinWithIt()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");

            Assert.Equal("Services", board.Rows[0].Folder);
            Assert.Equal("Services", board.Columns.Single(c => c.Name == "Pipes").Folder);
        }

        [Fact]
        public void KeptApartOnlyTheLineFiledIsFiled()
        {
            var board = Peopled("Pipes", "Ducts");

            board.Paired = false;
            board.File(board.Rows[0], "Services");

            Assert.Equal("Services", board.Rows[0].Folder);
            Assert.Equal(string.Empty, board.Columns[0].Folder);
        }

        [Fact]
        public void FilingMovesTheLineNextToTheRestOfItsFolder()
        {
            var board = Peopled("Pipes", "Walls", "Ducts");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows[2], "Services");

            Assert.Equal(new[] { "Pipes", "Ducts", "Walls" }, board.Rows.Select(r => r.Name));
            Assert.Equal(new[] { "Pipes", "Ducts", "Walls" }, board.Columns.Select(c => c.Name));
        }

        [Fact]
        public void FoldersComeBackInTheOrderTheirFirstMemberAppears()
        {
            var board = Peopled("Pipes", "Walls", "Ducts", "Doors");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows.Single(r => r.Name == "Walls"), "Fabric");
            board.File(board.Rows.Single(r => r.Name == "Ducts"), "Services");

            Assert.Equal(new[] { "Services", "Fabric" }, board.Folders(rows: true));
        }

        [Fact]
        public void AFolderKnowsWhatIsInIt()
        {
            var board = Peopled("Pipes", "Walls", "Ducts");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows.Single(r => r.Name == "Ducts"), "Services");

            Assert.Equal(new[] { "Pipes", "Ducts" },
                         board.InFolder(rows: true, "Services").Select(l => l.Name));
        }

        [Fact]
        public void TakingALineOutOfAFolderClearsItOnBothAxes()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows[0], null);

            Assert.Equal(string.Empty, board.Rows[0].Folder);
            Assert.All(board.Columns, c => Assert.Equal(string.Empty, c.Folder));
        }

        [Fact]
        public void RenamingAFolderRenamesItOnBothAxes()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows[1], "Services");

            Assert.Equal(4, board.RenameFolder("Services", "MEP"));
            Assert.All(board.Rows, r => Assert.Equal("MEP", r.Folder));
            Assert.All(board.Columns, c => Assert.Equal("MEP", c.Folder));
        }

        [Fact]
        public void DissolvingAFolderKeepsItsLines()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows[1], "Services");

            Assert.Equal(4, board.DissolveFolder("Services"));
            Assert.Equal(2, board.Rows.Count);
            Assert.Equal(2, board.Columns.Count);
            Assert.All(board.Rows, r => Assert.Equal(string.Empty, r.Folder));
            Assert.Empty(board.Folders(rows: true));
        }

        [Fact]
        public void RemovingAFolderTakesItsLinesToo()
        {
            var board = Peopled("Pipes", "Ducts", "Walls");

            board.File(board.Rows[0], "Services");
            board.File(board.Rows.Single(r => r.Name == "Ducts"), "Services");

            Assert.Equal(2, board.RemoveFolder("Services"));
            Assert.Single(board.Rows);
            Assert.Single(board.Columns);
            Assert.Equal("Walls", board.Rows[0].Name);
        }

        [Fact]
        public void MergingFoldersPutsThemUnderOneName()
        {
            var board = Peopled("Pipes", "Ducts", "Cables");

            board.File(board.Rows[0], "Wet");
            board.File(board.Rows.Single(r => r.Name == "Ducts"), "Air");
            board.File(board.Rows.Single(r => r.Name == "Cables"), "Power");

            Assert.Equal(6, board.MergeFolders(new[] { "Wet", "Air", "Power" }, "Services"));
            Assert.Equal(new[] { "Services" }, board.Folders(rows: true));
            Assert.All(board.Rows, r => Assert.Equal("Services", r.Folder));
        }

        [Fact]
        public void MergingIntoOneOfThemIsAllowed()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Wet");
            board.File(board.Rows[1], "Air");

            board.MergeFolders(new[] { "Wet", "Air" }, "Wet");

            Assert.Equal(new[] { "Wet" }, board.Folders(rows: true));
        }

        [Fact]
        public void TidyingPutsEachFoldersMembersTogetherAndLeavesLooseLinesLast()
        {
            var board = Peopled("Pipes", "Walls", "Ducts", "Doors");

            // Filed without moving, so the axes are deliberately interleaved.
            board.Rows[0].Folder = "Services";
            board.Rows[2].Folder = "Services";
            board.Rows[1].Folder = "Fabric";

            board.Tidy();

            Assert.Equal(new[] { "Pipes", "Ducts", "Walls", "Doors" },
                         board.Rows.Select(r => r.Name));
        }

        [Fact]
        public void AFolderSurvivesASaveAndRead()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");

            var read = MatrixBoard.FromJson(board.ToJson());

            Assert.Equal("Services", read.Rows[0].Folder);
            Assert.Equal(new[] { "Services" }, read.Folders(rows: true));
        }

        [Fact]
        public void AddingADisciplineToAFolderGivesTheColumnTheSameFolder()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Pipes") { Folder = "Services" });

            Assert.Equal("Services", board.Columns[0].Folder);
        }

        [Fact]
        public void TheFolderIsATokenATestNameCanUse()
        {
            var row = new MatrixLine("Pipes") { Folder = "Services" };
            var column = new MatrixLine("Walls") { Folder = "Fabric" };

            Assert.Equal("Services/Pipes vs Fabric/Walls",
                         MatrixNaming.Fill("{rowFolder}/{row} vs {columnFolder}/{column}",
                                           row, column, null, 1));
        }

        [Fact]
        public void ALineInNoFolderLeavesTheTokenEmptyRatherThanTheWordEmpty()
        {
            var row = new MatrixLine("Pipes");
            var column = new MatrixLine("Walls");

            Assert.Equal("Pipes vs Walls",
                         MatrixNaming.Fill("{rowFolder} {row} vs {columnFolder} {column}",
                                           row, column, null, 1));
        }

        /// <summary>
        /// Removing a folder removes what is in it, and on axes kept apart only one axis was
        /// ever filed - so only that one loses anything.
        /// </summary>
        [Fact]
        public void RemovingAFolderOnAnUnpairedBoardTakesOnlyWhatWasFiled()
        {
            var board = Peopled("Pipes", "Ducts");

            board.Paired = false;
            board.File(board.Rows[0], "Services");

            Assert.Equal(string.Empty, board.Columns[0].Folder);

            board.RemoveFolder("Services");

            Assert.Single(board.Rows);
            Assert.Equal(2, board.Columns.Count);
        }

        /// <summary>And on a paired board it takes the discipline off both, which is the point.</summary>
        [Fact]
        public void RemovingAFolderTakesTheDisciplineOffBothAxes()
        {
            var board = Peopled("Pipes", "Ducts");

            board.File(board.Rows[0], "Services");
            board.RemoveFolder("Services");

            Assert.Single(board.Rows);
            Assert.Single(board.Columns);
            Assert.Equal("Ducts", board.Rows[0].Name);
        }
    }

    /// <summary>
    /// A discipline is what it is, not what it is called. Six unnamed ones are six.
    /// </summary>
    public class MatrixIdentityTests
    {
        [Fact]
        public void SixDisciplinesNobodyHasNamedYetAreSixDisciplines()
        {
            var board = new MatrixBoard();

            for (var i = 0; i < 6; i++) board.AddPair(new MatrixLine("New category"));

            // By name every one of these was every other, so every pair among them was a pair
            // with itself: the whole block drew as diagonal and would hold nothing.
            for (var r = 0; r < 6; r++)
            for (var c = 0; c < 6; c++)
                Assert.Equal(r == c, board.IsSelfPair(board.Rows[r], board.Columns[c]));
        }

        [Fact]
        public void AndTheyCanBeFilledIn()
        {
            var board = new MatrixBoard();

            for (var i = 0; i < 6; i++) board.AddPair(new MatrixLine("New category"));

            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[0]));
            Assert.Null(board.At(board.Rows[2], board.Columns[3]));
        }

        [Fact]
        public void RenamingOneDoesNotMakeItADifferentDiscipline()
        {
            var board = new MatrixBoard();
            var row = board.AddPair(new MatrixLine("Structure"));

            board.Rename(row, "Structural frame");

            Assert.True(board.IsSelfPair(row, board.Columns[0]));
            Assert.Same(board.Columns[0], board.TwinOf(row));
        }

        [Fact]
        public void TwoUnnamedDisciplinesKeepTheirOwnCells()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("New category"));
            board.AddPair(new MatrixLine("New category"));
            board.AddPair(new MatrixLine("New category"));

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], "B");

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[1]));
            Assert.Equal("B", board.At(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void WhatADisciplineIsSurvivesASaveAndRead()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("New category"));
            board.AddPair(new MatrixLine("New category"));

            var read = MatrixBoard.FromJson(board.ToJson());

            Assert.True(read.IsSelfPair(read.Rows[0], read.Columns[0]));
            Assert.False(read.IsSelfPair(read.Rows[0], read.Columns[1]));
        }

        [Fact]
        public void ABoardSavedBeforeALineKnewWhatItWasStillReadsByName()
        {
            // No key on either, which is every board written before this existed.
            var one = new MatrixLine("Steel") { Key = string.Empty };
            var other = new MatrixLine("Steel") { Key = string.Empty };

            var board = new MatrixBoard();

            board.AddRow(one);
            board.AddColumn(other);

            Assert.True(board.IsSelfPair(one, other));
        }

        [Fact]
        public void SquaringOffCarriesWhatEachLineIs()
        {
            var board = new MatrixBoard();

            board.AddRow(new MatrixLine("New category"));
            board.AddRow(new MatrixLine("New category"));

            board.SquareOffRows();

            Assert.True(board.IsSelfPair(board.Rows[0], board.Columns[0]));
            Assert.False(board.IsSelfPair(board.Rows[0], board.Columns[1]));
        }
    }

    /// <summary>
    /// What a folder's colour is, and what happens to it when the folder changes.
    ///
    /// A folder is a name its members share and nothing more, so its colour is kept beside the
    /// board rather than on an object - which makes every act that changes a folder's name a
    /// chance to lose the colour or to leave one behind for a later folder to inherit.
    /// </summary>
    public class MatrixFolderColourTests
    {
        private static MatrixBoard Filed()
        {
            var board = new MatrixBoard();

            var one = board.AddPair(new MatrixLine("ARC_Walls"));
            var two = board.AddPair(new MatrixLine("ARC_Ceilings"));
            var three = board.AddPair(new MatrixLine("STR_Frame"));

            board.File(one, "Architecture");
            board.File(two, "Architecture");
            board.File(three, "Structure");

            return board;
        }

        [Fact]
        public void AFolderNobodyHasColouredHasNoColour()
        {
            Assert.Equal(string.Empty, Filed().ColourOf("Architecture"));
        }

        [Fact]
        public void AColourIsKeptByName()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");

            Assert.Equal("#2E7D32", board.ColourOf("Architecture"));
            Assert.Equal(string.Empty, board.ColourOf("Structure"));
        }

        [Fact]
        public void TheNameIsMatchedHoweverItIsCased()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");

            Assert.Equal("#2E7D32", board.ColourOf("ARCHITECTURE"));
        }

        [Fact]
        public void PaintingWithNothingTakesTheColourAway()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.Paint("Architecture", null);

            Assert.Equal(string.Empty, board.ColourOf("Architecture"));
        }

        [Fact]
        public void RenamingAFolderCarriesItsColour()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.RenameFolder("Architecture", "Arch");

            Assert.Equal("#2E7D32", board.ColourOf("Arch"));
            Assert.Equal(string.Empty, board.ColourOf("Architecture"));
        }

        [Fact]
        public void DissolvingAFolderDropsItsColour()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.DissolveFolder("Architecture");

            Assert.Empty(board.FolderColours);
        }

        [Fact]
        public void AColourIsNotInheritedByTheNextFolderOfTheSameName()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.DissolveFolder("Architecture");

            board.File(board.Rows[0], "Architecture");

            Assert.Equal(string.Empty, board.ColourOf("Architecture"));
        }

        [Fact]
        public void RemovingAFolderTakesItsColourWithIt()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.RemoveFolder("Architecture");

            Assert.Empty(board.FolderColours);
        }

        [Fact]
        public void MergingKeepsTheColourOfTheNameThatSurvives()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");
            board.Paint("Structure", "#1565C0");

            board.MergeFolders(new[] { "Architecture", "Structure" }, "Structure");

            Assert.Equal("#1565C0", board.ColourOf("Structure"));
            Assert.Single(board.FolderColours);
        }

        [Fact]
        public void MergingIntoANewNameCarriesTheFirstColourThere()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");

            board.MergeFolders(new[] { "Architecture", "Structure" }, "Fabric");

            Assert.Equal("#2E7D32", board.ColourOf("Fabric"));
            Assert.Single(board.FolderColours);
        }

        [Fact]
        public void AColourSurvivesBeingWrittenDownAndReadBack()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");

            var back = MatrixBoard.FromJson(board.ToJson());

            Assert.Equal("#2E7D32", back.ColourOf("Architecture"));
        }

        [Fact]
        public void ABoardWithNoColoursWritesNoneDown()
        {
            var json = Filed().ToJson();

            Assert.Equal(0, json["folders"].Items.Count());
        }

        [Fact]
        public void AColourForAFolderNobodyIsInIsNotReadBack()
        {
            var board = Filed();

            board.Paint("Architecture", "#2E7D32");

            var json = board.ToJson();

            board.DissolveFolder("Architecture");

            // The stored board still names it; the one read back has nobody filed under it.
            var stale = MatrixBoard.FromJson(json);

            Assert.Equal("#2E7D32", stale.ColourOf("Architecture"));

            stale.DissolveFolder("Architecture");

            Assert.Empty(stale.FolderColours);
        }
    }

    /// <summary>
    /// What the matrix says came back: open clashes per pair, keyed by what the tests point at.
    ///
    /// The join between a board and the document's results has to key sides exactly the way the
    /// board does, or the counts land on cells that do not exist and the grid draws nothing where
    /// the work is - silently, which is the worst way for this to be wrong.
    /// </summary>
    public class MatrixTallyTests
    {
        private static ClashTestRead Run(string setA, string setB, params string?[] statuses)
        {
            var results = statuses
                .Select((status, at) => new ClashResultInfo(
                    "r" + at,
                    default, default, 0, 0, 0, status, null))
                .ToList();

            return new ClashTestRead(
                new ClashTestInfo(setA + " vs " + setB, setA + " vs " + setB, null, results.Count, 0),
                new ClashTestSides(setA, setB, Array.Empty<string>(), Array.Empty<string>(),
                                   "Hard", 0.01),
                results);
        }

        private static MatrixBoard Board(params string[] sets)
        {
            var board = new MatrixBoard();

            foreach (var set in sets)
                board.AddPair(new MatrixLine(set, null, MatrixSource.SelectionSet(set)));

            return board;
        }

        [Fact]
        public void NothingCountedIsAnEmptyTally()
        {
            Assert.True(MatrixTally.Of(null).IsEmpty);
            Assert.True(MatrixCounts.None.IsEmpty);
        }

        [Fact]
        public void OnlyTheOpenOnesAreCounted()
        {
            var counts = MatrixTally.Of(new[]
            {
                Run("ARC", "STR", "New", "Active", "Approved", "Reviewed", "Resolved"),
            });

            var board = Board("ARC", "STR");

            Assert.Equal(2, counts.OpenBetween(board.Rows[0], board.Columns[1]));
            Assert.Equal(5, counts.EverBetween(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void AResultNobodyHasRuledOnIsOpen()
        {
            Assert.True(MatrixTally.IsOpen(null));
            Assert.True(MatrixTally.IsOpen(" "));
            Assert.True(MatrixTally.IsOpen("New"));
            Assert.False(MatrixTally.IsOpen("approved"));
        }

        [Fact]
        public void ThePairIsTheSameWhicheverWayRoundTheTestWasBuilt()
        {
            var counts = MatrixTally.Of(new[] { Run("STR", "ARC", "New", "New") });
            var board = Board("ARC", "STR");

            Assert.Equal(2, counts.OpenBetween(board.Rows[0], board.Columns[1]));
            Assert.Equal(2, counts.OpenBetween(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void TwoTestsOnOnePairAreAddedTogether()
        {
            var counts = MatrixTally.Of(new[]
            {
                Run("ARC", "STR", "New"),
                Run("ARC", "STR", "Active", "Active"),
            });

            var board = Board("ARC", "STR");

            Assert.Equal(3, counts.OpenBetween(board.Rows[0], board.Columns[1]));
            Assert.Equal(2, counts.Tests);
        }

        [Fact]
        public void APairNoTestCoversIsNotNought()
        {
            var counts = MatrixTally.Of(new[] { Run("ARC", "STR", "New") });
            var board = Board("ARC", "STR", "MEP");

            // Tested and clean would be nought; never tested is a hole in the coordination, and
            // the grid has to be able to draw them differently.
            Assert.Equal(-1, counts.OpenBetween(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void APairTestedAndCleanIsNought()
        {
            var counts = MatrixTally.Of(new[] { Run("ARC", "STR") });
            var board = Board("ARC", "STR");

            Assert.Equal(0, counts.OpenBetween(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void ATestThatDoesNotSayWhatItIsBetweenIsSkipped()
        {
            var counts = MatrixTally.Of(new[]
            {
                new ClashTestRead(new ClashTestInfo("odd", "odd", null, 0, 0), null,
                                  Array.Empty<ClashResultInfo>()),
            });

            Assert.True(counts.IsEmpty);
        }

        [Fact]
        public void TheWorstPairIsWhatAHeatScaleRunsTo()
        {
            var counts = MatrixTally.Of(new[]
            {
                Run("ARC", "STR", "New"),
                Run("ARC", "MEP", "New", "New", "New"),
            });

            Assert.Equal(3, counts.Worst);
            Assert.Equal(4, counts.Open);
        }
    }

    /// <summary>
    /// A pair carries how urgent it is as well as what is checked on it, in the host's own four
    /// ranks - and the two travel together: mirrored the same way, cleared when the cell is
    /// cleared, dropped when the line goes, and written down and read back only when somebody
    /// has actually ranked something.
    /// </summary>
    public class MatrixRankTests
    {
        private static MatrixBoard Square()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Architecture"));
            board.AddPair(new MatrixLine("Structure"));
            board.AddPair(new MatrixLine("Services"));

            return board;
        }

        [Fact]
        public void Nothing_is_ranked_to_begin_with()
        {
            var board = Square();

            Assert.Equal(MatrixBoard.NotSet, board.RankAt(board.Rows[0], board.Columns[1]));
            Assert.Empty(board.Ranks());
        }

        [Fact]
        public void A_rank_lands_on_both_cells_of_a_mirrored_pair()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);

            Assert.Equal(MatrixBoard.High, board.RankAt(board.Rows[0], board.Columns[1]));
            Assert.Equal(MatrixBoard.High, board.RankAt(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void Ranks_names_the_pair_once()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.Medium);

            var ranked = board.Ranks().ToList();

            Assert.Single(ranked);
            Assert.Equal(MatrixBoard.Medium, ranked[0].Rank);
        }

        [Fact]
        public void A_rank_outside_the_four_is_no_rank()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);
            board.Rank(board.Rows[0], board.Columns[1], 9);

            Assert.Equal(MatrixBoard.NotSet, board.RankAt(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void Clearing_the_cell_clears_its_rank()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.Low);

            board.Set(board.Rows[0], board.Columns[1], null);

            Assert.Equal(MatrixBoard.NotSet, board.RankAt(board.Rows[0], board.Columns[1]));
            Assert.Equal(MatrixBoard.NotSet, board.RankAt(board.Rows[1], board.Columns[0]));
        }

        [Fact]
        public void Removing_a_line_takes_its_ranks_with_it()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);

            board.RemovePair(board.Rows[0]);

            Assert.Empty(board.Ranks());
        }

        [Fact]
        public void The_ranks_are_written_down_and_read_back()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[1], board.Columns[2], "B");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);
            board.Rank(board.Rows[1], board.Columns[2], MatrixBoard.Low);

            var back = MatrixBoard.FromJson(board.ToJson());

            Assert.Equal(MatrixBoard.High, back.RankAt(back.Rows[0], back.Columns[1]));
            Assert.Equal(MatrixBoard.Low, back.RankAt(back.Rows[1], back.Columns[2]));
        }

        [Fact]
        public void A_board_nobody_ranked_writes_no_ranks_at_all()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Empty(board.ToJson()["ranks"].Items);
        }

        [Fact]
        public void Squaring_off_keeps_the_ranks_of_the_pairs_that_survive()
        {
            var board = Square();

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.Medium);

            board.SquareOffRows();

            Assert.Equal(MatrixBoard.Medium, board.RankAt(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void Every_rank_has_a_name()
        {
            Assert.Equal("High", MatrixBoard.RankNamed(MatrixBoard.High));
            Assert.Equal("Medium", MatrixBoard.RankNamed(MatrixBoard.Medium));
            Assert.Equal("Low", MatrixBoard.RankNamed(MatrixBoard.Low));
            Assert.Equal("Not set", MatrixBoard.RankNamed(MatrixBoard.NotSet));
            Assert.Equal("Not set", MatrixBoard.RankNamed(42));
        }
    }

    /// <summary>
    /// A rule is a sentence about two folders that keeps holding: it fills the block it covers,
    /// re-applies itself as the board grows, loses to a hand that writes over it, and takes its
    /// own cells back when it is dropped.
    /// </summary>
    public class MatrixRuleTests
    {
        private static MatrixBoard Filed()
        {
            var board = new MatrixBoard();

            board.File(board.AddPair(new MatrixLine("Walls")), "ARC");
            board.File(board.AddPair(new MatrixLine("Slabs")), "STR");
            board.File(board.AddPair(new MatrixLine("Pipes")), "MEP");

            return board;
        }

        [Fact]
        public void A_rule_covers_its_pair_either_way_round()
        {
            var rule = new MatrixRule("ARC", "MEP", "B");

            Assert.True(rule.Covers("ARC", "MEP"));
            Assert.True(rule.Covers("MEP", "ARC"));
            Assert.False(rule.Covers("ARC", "STR"));
        }

        [Fact]
        public void An_empty_side_covers_any_folder()
        {
            var rule = new MatrixRule("MEP", null, "A");

            Assert.True(rule.Covers("MEP", "ARC"));
            Assert.True(rule.Covers("STR", "MEP"));
            Assert.False(rule.Covers("ARC", "STR"));
        }

        [Fact]
        public void Applying_writes_the_block_the_rule_covers()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));

            Assert.Equal(1, board.ApplyRules());
            Assert.Equal("B", board.At(board.Rows[0], board.Columns[2]));
            Assert.Null(board.At(board.Rows[0], board.Columns[1]));
        }

        [Fact]
        public void A_rule_written_cell_says_it_is_the_rules()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            Assert.True(board.IsByRule(board.Rows[0], board.Columns[2]));
            Assert.Equal("B", board.RuleOn(board.Rows[0], board.Columns[2])?.Symbol);
        }

        [Fact]
        public void The_last_rule_that_covers_a_pair_wins()
        {
            var board = Filed();

            board.AddRule(new MatrixRule(null, null, "A"));
            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            Assert.Equal("B", board.At(board.Rows[0], board.Columns[2]));
            Assert.Equal("A", board.At(board.Rows[1], board.Columns[2]));
        }

        [Fact]
        public void A_hand_written_cell_is_left_alone()
        {
            var board = Filed();

            board.Set(board.Rows[0], board.Columns[2], "D");
            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            Assert.Equal("D", board.At(board.Rows[0], board.Columns[2]));
            Assert.False(board.IsByRule(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void Writing_over_a_rule_cell_makes_it_a_decision()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            board.Set(board.Rows[0], board.Columns[2], "D");

            Assert.False(board.IsByRule(board.Rows[0], board.Columns[2]));

            board.ApplyRules();

            Assert.Equal("D", board.At(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void Dropping_a_rule_takes_its_cells_with_it()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            board.RemoveRule(0);
            board.ApplyRules();

            Assert.Null(board.At(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void A_line_added_later_arrives_already_filled()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            board.File(board.AddPair(new MatrixLine("Ducts")), "MEP");
            board.ApplyRules();

            var added = board.Columns.First(c => c.Name == "Ducts");

            Assert.Equal("B", board.At(board.Rows[0], added));
        }

        [Fact]
        public void Refiling_a_line_moves_what_the_rules_wrote_on_it()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();

            board.File(board.Rows[1], "MEP");
            board.ApplyRules();

            Assert.Equal("B", board.At(board.Rows.First(r => r.Name == "Walls"),
                                       board.Columns.First(c => c.Name == "Slabs")));
        }

        [Fact]
        public void The_rules_and_whose_cell_it_is_are_written_down()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "B"));
            board.ApplyRules();
            board.Set(board.Rows[0], board.Columns[1], "D");

            var back = MatrixBoard.FromJson(board.ToJson());

            Assert.Single(back.Rules);
            Assert.Equal("B", back.Rules[0].Symbol);
            Assert.True(back.IsByRule(back.Rows[0], back.Columns[2]));
            Assert.False(back.IsByRule(back.Rows[0], back.Columns[1]));
        }

        [Fact]
        public void A_board_with_no_rules_writes_none()
        {
            var board = Filed();

            board.Set(board.Rows[0], board.Columns[1], "A");

            Assert.Empty(board.ToJson()["rules"].Items);
        }

        [Fact]
        public void An_empty_rule_is_not_kept()
        {
            var board = Filed();

            board.AddRule(new MatrixRule("ARC", "MEP", "   "));

            Assert.Empty(board.Rules);
        }

        [Fact]
        public void Rules_can_be_reordered()
        {
            var board = Filed();

            board.AddRule(new MatrixRule(null, null, "A"));
            board.AddRule(new MatrixRule("ARC", "MEP", "B"));

            board.MoveRule(1, 0);
            board.ApplyRules();

            Assert.Equal("A", board.At(board.Rows[0], board.Columns[2]));
        }

        [Fact]
        public void A_rule_never_writes_the_diagonal()
        {
            var board = Filed();

            board.AddRule(new MatrixRule(null, null, "A"));
            board.ApplyRules();

            Assert.Null(board.At(board.Rows[0], board.Columns[0]));
        }
    }

    /// <summary>
    /// The workbook a matrix goes out as: one tab per matrix somebody has filled in, drawn the
    /// way the screen draws it - the folder bands in their colours, the headings turned, the
    /// spine, the dead half - and a legend whose keys carry the ground their cells carry.
    /// </summary>
    public class MatrixWorkbookTests
    {
        private static MatrixBoard Coloured()
        {
            var board = new MatrixBoard();

            // Two lines in one folder, so the band has a run to merge across - which is the
            // shape every real board has and the shape a one-line folder never tests.
            board.File(board.AddPair(new MatrixLine("Walls", "1", MatrixSource.SelectionSet("ARC_Walls"))), "ARC");
            board.File(board.AddPair(new MatrixLine("Ceilings", "2", MatrixSource.SelectionSet("ARC_Ceilings"))), "ARC");
            board.File(board.AddPair(new MatrixLine("Slabs", "3", MatrixSource.SelectionSet("STR_Slabs"))), "STR");
            board.File(board.AddPair(new MatrixLine("Pipes", "4", MatrixSource.SelectionSet("MEP_Pipes"))), "MEP");

            board.Paint("ARC", "#B3E5FC");

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[0], board.Columns[2], "B");
            board.Set(board.Rows[1], board.Columns[3], MatrixBoard.NotRequired);

            return board;
        }

        private static MatrixLegend Key() =>
            new MatrixLegend(new[]
            {
                new MatrixSymbol("A", colour: "#FFAA00"),
                new MatrixSymbol("B", MatrixTest.Clearance, 0.025),
            });

        [Fact]
        public void A_plain_board_writes_the_grid_and_the_legend()
        {
            var book = MatrixSheets.Book(Coloured(), Key());

            Assert.Equal(new[] { "Clash matrix", "Legend" }, book.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void A_ranked_board_writes_a_priority_tab()
        {
            var board = Coloured();

            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);

            Assert.Contains("Priority", MatrixSheets.Book(board, Key()).Select(s => s.Name));
        }

        [Fact]
        public void Counts_write_a_results_tab()
        {
            var board = Coloured();

            var counts = MatrixTally.Of(new[]
            {
                new ClashTestRead(
                    new ClashTestInfo("1", "Walls vs Slabs", null, 1, 0),
                    new ClashTestSides("ARC_Walls", "STR_Slabs", Array.Empty<string>(),
                                       Array.Empty<string>(), "Hard", 0.01),
                    new[] { new ClashResultInfo("1", default, default, 0, 0, 0, "New", null) }),
            });

            var book = MatrixSheets.Book(board, Key(), counts);

            Assert.Contains("Results", book.Select(s => s.Name));
        }

        [Fact]
        public void The_cells_are_painted()
        {
            var book = MatrixSheets.Book(Coloured(), Key());
            var grid = book[0];

            // Row nought is the folder band, row one the headings, so the first pair sits at
            // row two, column four.
            Assert.True(grid.Styles.ContainsKey((2, 5)));
            Assert.Equal("FFAA00", grid.Styles[(2, 5)].Fill);
        }

        [Fact]
        public void A_symbol_with_no_colour_takes_the_one_it_is_handed()
        {
            var book = MatrixSheets.Book(Coloured(), Key(), null,
                                         new Dictionary<string, string> { ["B"] = "#123456" });

            Assert.Equal("123456", book[0].Styles[(2, 6)].Fill);
        }

        [Fact]
        public void The_diagonal_carries_the_spine()
        {
            var grid = MatrixSheets.Book(Coloured(), Key())[0];

            Assert.Equal("D7DBE3", grid.Styles[(2, 4)].Fill);
        }

        [Fact]
        public void The_folder_band_is_merged_and_coloured()
        {
            var grid = MatrixSheets.Book(Coloured(), Key())[0];

            Assert.Contains(grid.Merges, m => m.Row == 0 && m.Column == 4 && m.ToColumn == 5);
            Assert.Equal("B3E5FC", grid.Styles[(0, 4)].Fill);
        }

        [Fact]
        public void A_headings_row_is_turned()
        {
            var grid = MatrixSheets.Book(Coloured(), Key())[0];

            Assert.True(grid.Styles[(1, 4)].Turned);
        }

        [Fact]
        public void A_row_header_is_washed_with_its_folder()
        {
            var grid = MatrixSheets.Book(Coloured(), Key())[0];

            // A long way to white, so a name is still readable on it.
            Assert.NotNull(grid.Styles[(2, 2)].Fill);
            Assert.NotEqual("B3E5FC", grid.Styles[(2, 2)].Fill);
        }

        [Fact]
        public void The_workbook_opens()
        {
            var board = Coloured();

            board.Rank(board.Rows[0], board.Columns[1], MatrixBoard.High);

            var bytes = XlsxWriter.Write(MatrixSheets.Book(board, Key()).ToList());

            Assert.True(bytes.Length > 0);
            Assert.Equal(0x50, bytes[0]);
            Assert.Equal(0x4B, bytes[1]);
        }

        [Fact]
        public void Every_style_used_gets_a_format()
        {
            var bytes = XlsxWriter.Write(MatrixSheets.Book(Coloured(), Key()).ToList());
            var text = Unzipped(bytes, "xl/styles.xml");

            Assert.Contains("patternType=\"solid\"", text);
            Assert.Contains("FFB3E5FC", text);
            Assert.Contains("textRotation=\"90\"", text);
        }

        [Fact]
        public void The_merges_are_written()
        {
            var bytes = XlsxWriter.Write(MatrixSheets.Book(Coloured(), Key()).ToList());

            Assert.Contains("<mergeCells", Unzipped(bytes, "xl/worksheets/sheet1.xml"));
        }

        [Fact]
        public void A_report_that_says_nothing_about_colour_is_unchanged()
        {
            var sheet = new XlsxSheet("Plain");

            sheet.Row("One", "Two");
            sheet.Row("a", "b");

            var text = Unzipped(XlsxWriter.Write(new[] { sheet }), "xl/worksheets/sheet1.xml");

            // The header still points at the bold format and nothing else carries one.
            Assert.Contains("s=\"1\"", text);
        }

        private static string Unzipped(byte[] bytes, string path)
        {
            using var buffer = new MemoryStream(bytes);
            using var zip = new System.IO.Compression.ZipArchive(buffer);

            var entry = zip.GetEntry(path);

            Assert.NotNull(entry);

            using var stream = entry!.Open();
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// The matrix as a file the Clash Detective makes its tests from: names, sides, kinds and
    /// tolerances, and nothing it cannot name written as though it could be.
    /// </summary>
    public class MatrixTestsXmlTests
    {
        private static MatrixPlan Planned(out MatrixBoard board)
        {
            board = new MatrixBoard();

            board.AddPair(new MatrixLine("Walls", "1", MatrixSource.SelectionSet("ARC_Walls")));
            board.AddPair(new MatrixLine("Slabs", "2", MatrixSource.SelectionSet("STR_Slabs", "Structure")));

            board.Set(board.Rows[0], board.Columns[1], "B");

            return MatrixPlanner.Plan(board,
                new MatrixLegend(new[]
                {
                    new MatrixSymbol("B", MatrixTest.Clearance, 0.025),
                }));
        }

        [Fact]
        public void One_cell_becomes_one_test()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.Equal(1, file.Written);
            Assert.Contains("<clashtest ", file.Text);
        }

        [Fact]
        public void The_kind_and_the_tolerance_are_the_symbols()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.Contains("test_type=\"clearance\"", file.Text);
            Assert.Contains("tolerance=\"0.025000\"", file.Text);
        }

        [Fact]
        public void A_side_is_its_saved_set_as_a_locator()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.Contains("<locator>lcop_selection_set_tree/ARC_Walls</locator>", file.Text);
        }

        [Fact]
        public void A_set_in_a_folder_carries_the_folder()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.Contains("<locator>lcop_selection_set_tree/Structure/STR_Slabs</locator>",
                            file.Text);
        }

        [Fact]
        public void The_prefix_can_be_changed_without_a_new_build()
        {
            var file = MatrixTestsXml.Write(Planned(out _),
                new MatrixTestsXmlOptions { SetPrefix = "sets/" });

            Assert.Contains("<locator>sets/ARC_Walls</locator>", file.Text);
        }

        [Fact]
        public void It_carries_no_results()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.DoesNotContain("<clashresults", file.Text);
        }

        [Fact]
        public void A_side_the_file_cannot_name_is_left_out_and_said()
        {
            var board = new MatrixBoard();

            board.AddPair(new MatrixLine("Walls", "1", MatrixSource.SelectionSet("ARC_Walls")));
            board.AddPair(new MatrixLine("Everything", "2",
                                         MatrixSource.Models(new[] { "one.nwc" })));

            board.Set(board.Rows[0], board.Columns[1], "A");

            var file = MatrixTestsXml.Write(MatrixPlanner.Plan(board,
                new MatrixLegend(new[] { new MatrixSymbol("A") })));

            Assert.Equal(0, file.Written);
            Assert.True(file.IsEmpty);
            Assert.Contains(file.Warnings, w => w.Contains("cannot name"));
        }

        [Fact]
        public void A_matrix_that_plans_nothing_writes_an_empty_batch()
        {
            var file = MatrixTestsXml.Write(MatrixPlanner.Plan(new MatrixBoard(),
                                                               new MatrixLegend()));

            Assert.True(file.IsEmpty);
            Assert.Contains("<clashtests>", file.Text);
        }

        [Fact]
        public void The_header_declares_its_units()
        {
            var file = MatrixTestsXml.Write(Planned(out _),
                new MatrixTestsXmlOptions { Units = "mm" });

            Assert.Contains("<exchange units=\"mm\"", file.Text);
        }

        [Fact]
        public void It_is_well_formed()
        {
            var file = MatrixTestsXml.Write(Planned(out _));

            Assert.NotNull(XDocument.Parse(file.Text));
        }
    }

    /// <summary>
    /// A naming pattern read backwards: what a name has to look like, what each part of it means,
    /// and what it refuses rather than guessing at.
    /// </summary>
    public class MatrixNamePatternTests
    {
        [Fact]
        public void It_pulls_the_two_sides_out_of_a_name()
        {
            var found = MatrixNamePattern.Parse("{row} vs {column}").Match("Steel vs Ductwork");

            Assert.NotNull(found);
            Assert.Equal("Steel", found!.Value("row"));
            Assert.Equal("Ductwork", found.Value("column"));
        }

        [Fact]
        public void Braces_are_optional_when_asking_for_a_value()
        {
            var found = MatrixNamePattern.Parse("{row} vs {column}").Match("Steel vs Ductwork");

            Assert.Equal("Steel", found!.Value("{row}"));
        }

        [Fact]
        public void A_name_that_does_not_fit_is_not_forced()
        {
            Assert.Null(MatrixNamePattern.Parse("{row} vs {column}").Match("Steel | Ductwork"));
        }

        [Fact]
        public void Numbers_split_a_name_at_the_right_place()
        {
            var found = MatrixNamePattern.Parse("{rowNumber} {row} vs {columnNumber} {column}")
                                         .Match("01 Steel vs 04 Ductwork");

            Assert.Equal("01", found!.Value("rowNumber"));
            Assert.Equal("Steel", found.Value("row"));
            Assert.Equal("04", found.Value("columnNumber"));
        }

        [Fact]
        public void A_real_convention_reads_end_to_end()
        {
            var found = MatrixNamePattern
                .Parse("{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column} ({symbol})")
                .Match("ST-01 Steel vs ME-04 Ductwork (B)");

            Assert.Equal("ST", found!.Value("rowCode"));
            Assert.Equal("01", found.Value("rowNumber"));
            Assert.Equal("Steel", found.Value("row"));
            Assert.Equal("ME", found.Value("columnCode"));
            Assert.Equal("Ductwork", found.Value("column"));
            Assert.Equal("B", found.Value("symbol"));
        }

        [Fact]
        public void It_does_not_care_about_case_in_the_literals()
        {
            Assert.NotNull(MatrixNamePattern.Parse("{row} vs {column}").Match("Steel VS Ductwork"));
        }

        [Fact]
        public void Two_tokens_with_nothing_between_them_are_refused()
        {
            var pattern = MatrixNamePattern.Parse("{row}{column}");

            Assert.False(pattern.IsUsable);
            Assert.Contains("directly", pattern.Trouble);
        }

        [Fact]
        public void The_same_token_twice_is_refused()
        {
            var pattern = MatrixNamePattern.Parse("{row} vs {row}");

            Assert.False(pattern.IsUsable);
            Assert.Contains("appears twice", pattern.Trouble);
        }

        [Fact]
        public void A_token_nobody_has_is_refused()
        {
            var pattern = MatrixNamePattern.Parse("{discipline} vs {column}");

            Assert.False(pattern.IsUsable);
            Assert.Contains("not a known token", pattern.Trouble);
        }

        [Fact]
        public void An_unclosed_brace_is_refused()
        {
            Assert.False(MatrixNamePattern.Parse("{row vs {column}").IsUsable);
        }

        [Fact]
        public void A_pattern_with_no_tokens_is_refused()
        {
            var pattern = MatrixNamePattern.Parse("Steel vs Ductwork");

            Assert.False(pattern.IsUsable);
            Assert.Contains("No tokens", pattern.Trouble);
        }

        [Fact]
        public void An_empty_pattern_says_so_rather_than_throwing()
        {
            Assert.False(MatrixNamePattern.Parse(null).IsUsable);
            Assert.False(MatrixNamePattern.Parse("   ").IsUsable);
        }

        [Fact]
        public void It_counts_how_many_names_it_fits()
        {
            var pattern = MatrixNamePattern.Parse("{row} vs {column}");

            Assert.Equal(2, pattern.Fits(new[] { "A vs B", "C vs D", "E | F" }));
        }

        [Fact]
        public void A_pattern_reads_back_what_the_matrix_writes()
        {
            var row = new MatrixLine("Steel", "01") { Code = "ST" };
            var column = new MatrixLine("Ductwork", "04") { Code = "ME" };
            var symbol = new MatrixSymbol("B", MatrixTest.Clearance, 0.025);

            var pattern = MatrixNamePattern.Parse(
                "{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column} ({symbol})");

            var written = MatrixNaming.Fill(pattern.Pattern, row, column, symbol, 1);
            var found = pattern.Match(written);

            Assert.NotNull(found);
            Assert.Equal("Steel", found!.Value("row"));
            Assert.Equal("B", found.Value("symbol"));
        }
    }

    /// <summary>
    /// The matrix read out of the document, using the job's own naming rather than whatever the
    /// saved sets happen to be called.
    /// </summary>
    public class MatrixFromNamesTests
    {
        private static ClashTestRead Test(string name, string a, string b,
                                          string kind = "Hard", double tolerance = 0.01) =>
            new ClashTestRead(
                new ClashTestInfo(name, name, null, 0, 0),
                new ClashTestSides(a, b, Array.Empty<string>(), Array.Empty<string>(),
                                   kind, tolerance),
                Array.Empty<ClashResultInfo>());

        private static IReadOnlyList<MatrixNamePattern> One(string pattern) =>
            new[] { MatrixNamePattern.Parse(pattern) };

        [Fact]
        public void The_rows_are_named_the_way_the_tests_are()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork", "ST_SET_R21", "ME_SET_R21") },
                One("{row} vs {column}"));

            Assert.Contains(read.Board.Rows, r => r.Name == "Steel");
            Assert.Contains(read.Board.Rows, r => r.Name == "Ductwork");
        }

        [Fact]
        public void The_row_still_points_at_the_set()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork", "ST_SET_R21", "ME_SET_R21") },
                One("{row} vs {column}"));

            var steel = read.Board.Rows.First(r => r.Name == "Steel");

            Assert.Equal("ST_SET_R21", steel.Source.Name);
        }

        [Fact]
        public void The_code_and_the_folder_come_out_of_the_name()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("ARC/ST Steel vs MEP/ME Ductwork", "a", "b") },
                One("{rowFolder}/{rowCode} {row} vs {columnFolder}/{columnCode} {column}"));

            var steel = read.Board.Rows.First(r => r.Name == "Steel");

            Assert.Equal("ST", steel.Code);
            Assert.Equal("ARC", steel.Folder);
        }

        [Fact]
        public void One_discipline_named_by_five_tests_is_one_row()
        {
            var read = MatrixFromNames.Read(
                new[]
                {
                    Test("Steel vs Ductwork", "a", "b"),
                    Test("Steel vs Pipes", "a", "c"),
                    Test("Steel vs Trays", "a", "d"),
                },
                One("{row} vs {column}"));

            Assert.Single(read.Board.Rows.Where(r => r.Name == "Steel"));
        }

        [Fact]
        public void The_symbol_in_the_name_becomes_the_symbol_in_the_cell()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork (B)", "a", "b") },
                One("{row} vs {column} ({symbol})"));

            var steel = read.Board.Rows.First(r => r.Name == "Steel");
            var duct = read.Board.Columns.First(c => c.Name == "Ductwork");

            Assert.Equal("B", read.Board.At(steel, duct));
            Assert.Contains(read.Legend.Symbols, s => s.Symbol == "B");
        }

        [Fact]
        public void What_the_symbol_means_still_comes_from_the_test()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork (B)", "a", "b", "Clearance", 0.025) },
                One("{row} vs {column} ({symbol})"));

            var symbol = read.Legend.Symbols.First(s => s.Symbol == "B");

            Assert.Equal(MatrixTest.Clearance, symbol.Test);
            Assert.Equal(0.025, symbol.Tolerance, 6);
        }

        [Fact]
        public void The_first_pattern_that_fits_claims_a_name()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel | Ductwork", "a", "b") },
                new[]
                {
                    MatrixNamePattern.Parse("{row} vs {column}"),
                    MatrixNamePattern.Parse("{row} | {column}"),
                });

            Assert.Contains(read.Board.Rows, r => r.Name == "Steel");
        }

        [Fact]
        public void A_name_no_pattern_fits_falls_back_to_its_sets()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("something else entirely", "ST_SET", "ME_SET") },
                One("{row} vs {column}"));

            Assert.Equal(1, read.Tests);
            Assert.Contains(read.Warnings, w => w.Contains("fits"));
        }

        [Fact]
        public void It_can_be_told_to_leave_what_does_not_fit_out()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("something else entirely", "ST_SET", "ME_SET") },
                One("{row} vs {column}"),
                new MatrixReadOptions { TakeUnmatched = false });

            Assert.Equal(0, read.Tests);
        }

        [Fact]
        public void One_name_over_two_different_sets_is_said_out_loud()
        {
            var read = MatrixFromNames.Read(
                new[]
                {
                    Test("Steel vs Ductwork", "ST_R21", "ME_R21"),
                    Test("Steel vs Pipes", "ST_R22", "PIPE_R21"),
                },
                One("{row} vs {column}"));

            Assert.Contains(read.Warnings, w => w.Contains("more than one saved set"));
        }

        [Fact]
        public void No_patterns_at_all_is_the_read_it_always_was()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork", "ST_SET", "ME_SET") },
                Array.Empty<MatrixNamePattern>());

            Assert.Contains(read.Board.Rows, r => r.Source.Name == "ST_SET");
        }

        [Fact]
        public void The_names_can_be_told_not_to_win()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork", "ST_SET", "ME_SET") },
                One("{row} vs {column}"),
                new MatrixReadOptions { NamesWin = false });

            Assert.Contains(read.Board.Rows, r => r.Source.Name == "ST_SET");
            Assert.DoesNotContain(read.Board.Rows, r => r.Name == "Steel");
        }

        [Fact]
        public void A_test_that_says_nothing_about_its_sides_is_left_out()
        {
            var read = MatrixFromNames.Read(
                new[]
                {
                    new ClashTestRead(new ClashTestInfo("1", "Steel vs Ductwork", null, 0, 0),
                                      null, Array.Empty<ClashResultInfo>()),
                },
                One("{row} vs {column}"));

            Assert.Equal(0, read.Tests);
            Assert.Contains(read.Warnings, w => w.Contains("does not say"));
        }

        [Fact]
        public void The_matrix_it_makes_is_mirrored()
        {
            var read = MatrixFromNames.Read(
                new[] { Test("Steel vs Ductwork (B)", "a", "b") },
                One("{row} vs {column} ({symbol})"));

            var duct = read.Board.Rows.First(r => r.Name == "Ductwork");
            var steel = read.Board.Columns.First(c => c.Name == "Steel");

            Assert.Equal("B", read.Board.At(duct, steel));
        }
    }
}
