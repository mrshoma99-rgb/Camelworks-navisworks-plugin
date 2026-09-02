using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
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
        public void TheMirroredCellIsTheSameTestAndIsSkipped()
        {
            // The whole reason the planner exists: a square grid filled on both sides of the
            // diagonal must not give the job two identical tests with two sets of results.
            var board = Square("Steel", "Ducts");

            board.Set(board.Rows[0], board.Columns[1], "A");
            board.Set(board.Rows[1], board.Columns[0], "A");

            var plan = MatrixPlanner.Plan(board, Legend());

            Assert.Single(plan.Tests);
            Assert.Single(plan.Skipped);
            Assert.Contains("already tested", plan.Skipped[0].Why);
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
                new[] { Read("Steel vs Ducts", 0.01), Read("Something else", 0.01, "OTH", "XYZ") });

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

        private static MatrixPlan Plan(string symbol)
        {
            var board = new MatrixBoard();

            var row = board.AddRow(new MatrixLine("Steel", null, MatrixSource.SelectionSet("STR")));
            var column = board.AddColumn(new MatrixLine("Ducts", null, MatrixSource.SelectionSet("MEP")));

            board.Set(row, column, symbol);

            return MatrixPlanner.Plan(board, new MatrixLegend(new[] { new MatrixSymbol(symbol) }));
        }

        private static ClashTestRead Read(string name, double tolerance,
                                          string setA = "STR", string setB = "MEP") =>
            new ClashTestRead(
                new ClashTestInfo("t1", name, null, 0, 0),
                new ClashTestSides(setA, setB, Array.Empty<string>(), Array.Empty<string>(),
                                   "Hard", tolerance),
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

            Assert.Equal(new[] { "#", "Row", "02 Ducts" }, sheet.Rows[0]);
            Assert.Equal(new[] { "01", "Steel", "A" }, sheet.Rows[1]);
            Assert.Contains(sheet.Rows, r => r.Count > 0 && r[0] == "Legend");
            Assert.Contains(sheet.Rows, r => r.Count > 1 && r[1] != null
                                             && r[1]!.Contains("Clearance at 50 mm"));
        }
    }
}
