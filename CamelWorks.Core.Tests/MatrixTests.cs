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
}
