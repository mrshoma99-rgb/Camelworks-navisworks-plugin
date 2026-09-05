using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// The colours a matrix is read by, in one place.
    ///
    /// The screen and the spreadsheet have to agree. A coordinator marks a printed matrix up and
    /// hands it back, and if the colours on the sheet are not the colours on the screen the two are
    /// pictures of different boards. So the ranks and the heat scale live here and both the screen
    /// and the export read them.
    /// </summary>
    public static class MatrixInk
    {
        /// <summary>High, in the red every schedule uses for it.</summary>
        public const string High = "#E53935";

        /// <summary>Medium.</summary>
        public const string Medium = "#FB8C00";

        /// <summary>Low.</summary>
        public const string Low = "#7CB342";

        /// <summary>The palest end of the heat scale, which is one open clash.</summary>
        public const string Mildest = "#FFF3C4";

        /// <summary>The other end, which is the worst pair on the board.</summary>
        public const string Worst = "#E53935";

        /// <summary>A pair that was tested and came back with nothing open.</summary>
        public const string Clean = "#E6F4EA";

        /// <summary>A discipline against itself, and the spine the grid is mirrored about.</summary>
        public const string Spine = "#D7DBE3";

        /// <summary>The half of a mirrored grid that is not worked in.</summary>
        public const string Echo = "#F5F7F9";

        /// <summary>A pair somebody has agreed not to check.</summary>
        public const string Ruled = "#E4E7EB";

        /// <summary>What a rank is drawn in, or empty when it is not set.</summary>
        /// <param name="rank">One of <see cref="MatrixBoard.High"/> and its neighbours.</param>
        public static string OfRank(int rank) =>
            rank == MatrixBoard.High ? High
            : rank == MatrixBoard.Medium ? Medium
            : rank == MatrixBoard.Low ? Low
            : string.Empty;

        /// <summary>
        /// The wash for a count, from the mildest thing on a board to the worst on it.
        ///
        /// Scaled to the board rather than to a fixed number of clashes: a job with nine thousand
        /// on its heaviest pair and one with ninety both want the same picture, which is where to
        /// go first.
        /// </summary>
        /// <param name="open">How many are still open on this pair.</param>
        /// <param name="worst">The most open on any pair of this board.</param>
        public static string Heat(int open, int worst)
        {
            var how = Math.Min(1.0, Math.Max(0.0, (double)open / Math.Max(1, worst)));

            return Between(Mildest, Worst, how);
        }

        /// <summary>
        /// One colour blended towards another.
        /// </summary>
        /// <param name="from">The colour at nought, as <c>#RRGGBB</c>.</param>
        /// <param name="to">The colour at one.</param>
        /// <param name="how">How far along, nought to one.</param>
        public static string Between(string from, string to, double how)
        {
            if (!Rgb(from, out var a) || !Rgb(to, out var b)) return from;

            var far = Math.Min(1.0, Math.Max(0.0, how));

            return "#"
                + ((int)(a.R + ((b.R - a.R) * far))).ToString("X2", CultureInfo.InvariantCulture)
                + ((int)(a.G + ((b.G - a.G) * far))).ToString("X2", CultureInfo.InvariantCulture)
                + ((int)(a.B + ((b.B - a.B) * far))).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A colour most of the way to white, for a ground something has to be read on.
        /// </summary>
        /// <param name="colour">The colour, as <c>#RRGGBB</c>.</param>
        /// <param name="toward">How far to white, nought being the colour itself.</param>
        public static string Washed(string? colour, double toward) =>
            string.IsNullOrWhiteSpace(colour)
                ? string.Empty
                : Between(colour!, "#FFFFFF", toward);

        /// <summary>
        /// Whether ink on this ground should be white rather than black.
        ///
        /// The same rule the screen uses, so a cell that reads in white on the screen reads in
        /// white on the page: perceived luminance, not the average of three channels.
        /// </summary>
        /// <param name="colour">The ground, as <c>#RRGGBB</c>.</param>
        public static bool IsDark(string? colour) =>
            Rgb(colour, out var c)
            && (((c.R * 0.299) + (c.G * 0.587) + (c.B * 0.114)) / 255.0) < 0.55;

        private static bool Rgb(string? colour, out (int R, int G, int B) parts)
        {
            parts = (0, 0, 0);

            var said = (colour ?? string.Empty).Trim().TrimStart('#');

            if (said.Length == 8) said = said.Substring(2);
            if (said.Length != 6) return false;

            try
            {
                parts = (Convert.ToInt32(said.Substring(0, 2), 16),
                         Convert.ToInt32(said.Substring(2, 2), 16),
                         Convert.ToInt32(said.Substring(4, 2), 16));

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The matrix as a workbook that looks like the matrix.
    ///
    /// The export previously carried the letters and nothing else. The folders that group twenty
    /// disciplines into five packages, the colour that says which kind of test each block is, the
    /// unused triangle and the spine down the diagonal all arrived at the client as characters on
    /// white, so the sheet had to be re-read from scratch by whoever opened it.
    ///
    /// It was also one matrix of three. The priorities and the counts are the same grid saying two
    /// other things about the same pairs, and the meeting that asks for the sheet usually wants to
    /// know where the work is, which is the results rather than the tolerances.
    ///
    /// One tab per matrix that has been filled in, then the legend. A board with no ranks writes no
    /// priority tab; a board with no counts writes no results tab.
    /// </summary>
    public static class MatrixSheets
    {
        /// <summary>The band column, the number, the name and what it points at.</summary>
        private const int Band = 0;
        private const int Number = 1;
        private const int Name = 2;
        private const int Points = 3;
        private const int First = 4;

        /// <summary>Where the column headings sit, under the folder band above them.</summary>
        private const int Heading = 1;
        private const int Body = 2;

        /// <summary>
        /// The whole workbook.
        /// </summary>
        /// <param name="board">The matrix.</param>
        /// <param name="legend">What its symbols mean, or null.</param>
        /// <param name="counts">What came back, or null when nothing has been counted.</param>
        /// <param name="washes">
        /// The colour each symbol's cells carry, by symbol.
        ///
        /// Handed in rather than worked out here, because a symbol with no colour of its own
        /// takes one from where it sits in the legend - and which colour that is belongs to the
        /// screen's palette, not to the board. Passing it keeps one answer rather than two that
        /// drift.
        /// </param>
        public static IReadOnlyList<XlsxSheet> Book(MatrixBoard board, MatrixLegend? legend,
                                                    MatrixCounts? counts = null,
                                                    IReadOnlyDictionary<string, string>? washes = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            var sheets = new List<XlsxSheet> { Grid(board, legend, washes, null) };

            if (board.Ranks().Any()) sheets.Add(Ranked(board));

            if (counts != null && !counts.IsEmpty) sheets.Add(Counted(board, counts));

            sheets.Add(Legend(board, legend, washes));

            return sheets;
        }

        /// <summary>The symbols matrix, drawn the way the screen draws it.</summary>
        /// <param name="board">The matrix.</param>
        /// <param name="legend">What its symbols mean, or null.</param>
        /// <param name="washes">The colour each symbol's cells carry.</param>
        /// <param name="unused">Reserved.</param>
        private static XlsxSheet Grid(MatrixBoard board, MatrixLegend? legend,
                                      IReadOnlyDictionary<string, string>? washes, object? unused)
        {
            var sheet = Framed(board, "Clash matrix");

            for (var down = 0; down < board.Rows.Count; down++)
            {
                var row = board.Rows[down];

                for (var across = 0; across < board.Columns.Count; across++)
                {
                    var column = board.Columns[across];
                    var at = (Body + down, First + across);
                    var symbol = board.At(row, column);

                    if (board.IsSelfPair(row, column))
                    {
                        sheet.Paint(at.Item1, at.Item2,
                                    new XlsxStyle(MatrixInk.Spine, centre: true, boxed: true));
                        continue;
                    }

                    // The half that is not worked in, quiet the way it is quiet on the screen:
                    // a row can still be read across, and the live half is what the eye goes to.
                    if (!board.IsDrawnHalf(row, column))
                    {
                        Write(sheet, at.Item1, at.Item2, symbol,
                              new XlsxStyle(MatrixInk.Echo, "9AA3AD", centre: true, boxed: true));
                        continue;
                    }

                    if (symbol == null)
                    {
                        sheet.Paint(at.Item1, at.Item2, new XlsxStyle(boxed: true));
                        continue;
                    }

                    if (MatrixBoard.IsNotRequired(symbol))
                    {
                        Write(sheet, at.Item1, at.Item2, MatrixBoard.NotRequired,
                              new XlsxStyle(MatrixInk.Ruled, "6B7280", centre: true, boxed: true));
                        continue;
                    }

                    var wash = Wash(symbol, legend, washes);

                    Write(sheet, at.Item1, at.Item2, symbol,
                          new XlsxStyle(wash.Length == 0 ? null : wash,
                                        MatrixInk.IsDark(wash) ? "FFFFFF" : "000000",
                                        bold: true, centre: true, boxed: true));
                }
            }

            return sheet;
        }

        /// <summary>The priority matrix: the host's own four ranks, in their own colours.</summary>
        /// <param name="board">The matrix.</param>
        private static XlsxSheet Ranked(MatrixBoard board)
        {
            var sheet = Framed(board, "Priority");

            for (var down = 0; down < board.Rows.Count; down++)
            {
                var row = board.Rows[down];

                for (var across = 0; across < board.Columns.Count; across++)
                {
                    var column = board.Columns[across];
                    var at = (Body + down, First + across);

                    if (board.IsSelfPair(row, column))
                    {
                        sheet.Paint(at.Item1, at.Item2,
                                    new XlsxStyle(MatrixInk.Spine, centre: true, boxed: true));
                        continue;
                    }

                    var symbol = board.At(row, column);
                    var rank = board.RankAt(row, column);
                    var mark = rank == MatrixBoard.High ? "H"
                        : rank == MatrixBoard.Medium ? "M"
                        : rank == MatrixBoard.Low ? "L"
                        : string.Empty;

                    // A rank is a thing said about a test, so a pair nobody has agreed to check
                    // has nothing to be urgent about and is left as it is on the screen: blank.
                    if (symbol == null || MatrixBoard.IsNotRequired(symbol))
                    {
                        sheet.Paint(at.Item1, at.Item2, new XlsxStyle(boxed: true));
                        continue;
                    }

                    if (!board.IsDrawnHalf(row, column))
                    {
                        Write(sheet, at.Item1, at.Item2, mark,
                              new XlsxStyle(MatrixInk.Echo, "9AA3AD", centre: true, boxed: true));
                        continue;
                    }

                    var ground = MatrixInk.OfRank(rank);

                    Write(sheet, at.Item1, at.Item2, mark,
                          new XlsxStyle(ground.Length == 0 ? null : ground,
                                        MatrixInk.IsDark(ground) ? "FFFFFF" : "000000",
                                        bold: true, centre: true, boxed: true));
                }
            }

            return sheet;
        }

        /// <summary>The results matrix: open clashes per pair, on the same heat scale.</summary>
        /// <param name="board">The matrix.</param>
        /// <param name="counts">What came back.</param>
        private static XlsxSheet Counted(MatrixBoard board, MatrixCounts counts)
        {
            var sheet = Framed(board, "Results");
            var worst = Math.Max(1, counts.Worst);

            for (var down = 0; down < board.Rows.Count; down++)
            {
                var row = board.Rows[down];

                for (var across = 0; across < board.Columns.Count; across++)
                {
                    var column = board.Columns[across];
                    var at = (Body + down, First + across);

                    if (board.IsSelfPair(row, column))
                    {
                        sheet.Paint(at.Item1, at.Item2,
                                    new XlsxStyle(MatrixInk.Spine, centre: true, boxed: true));
                        continue;
                    }

                    var open = counts.OpenBetween(row, column);

                    // Nought and "no test covers this pair" are different answers and the sheet
                    // has to draw them differently, the same way the screen does: a pair that
                    // came back clean is one somebody can stop thinking about, and a pair no
                    // test covers is a hole in the coordination nobody has noticed.
                    if (open < 0)
                    {
                        sheet.Paint(at.Item1, at.Item2, new XlsxStyle(boxed: true));
                        continue;
                    }

                    var ground = open == 0 ? MatrixInk.Clean : MatrixInk.Heat(open, worst);

                    Write(sheet, at.Item1, at.Item2,
                          open.ToString(CultureInfo.InvariantCulture),
                          new XlsxStyle(ground, MatrixInk.IsDark(ground) ? "FFFFFF" : "000000",
                                        bold: open > 0, centre: true, boxed: true));
                }
            }

            return sheet;
        }

        /// <summary>
        /// The frame every matrix tab shares: the folder bands, the headings, and the rows down the
        /// side.
        ///
        /// Written once for all three tabs. Three tabs whose rows were in different orders, or whose
        /// folders were drawn differently, would be three matrices rather than one matrix seen three
        /// ways.
        /// </summary>
        /// <param name="board">The matrix.</param>
        /// <param name="named">The tab's name.</param>
        private static XlsxSheet Framed(MatrixBoard board, string named)
        {
            var sheet = new XlsxSheet(named) { FreezeHeader = false };

            // The heading row is turned, so it is as tall as the longest name is long; the
            // matrix columns are as wide as one letter, which is all any of them holds.
            sheet.Heights[Heading] = Math.Min(220, Math.Max(60,
                (board.Columns.Count == 0
                    ? 0
                    : board.Columns.Max(c => c.Titled.Length)) * 5.6));

            sheet.Widths[Band] = 3;
            sheet.Widths[Number] = 5;
            sheet.Widths[Name] = 34;
            sheet.Widths[Points] = 30;

            for (var at = 0; at < board.Columns.Count; at++) sheet.Widths[First + at] = 4.5;

            var heading = new List<string?> { null, "#", "Row", "Points at" };

            foreach (var column in board.Columns) heading.Add(column.Titled);

            sheet.Row();
            sheet.Row(heading.ToArray());

            foreach (var row in board.Rows)
            {
                var cells = new List<string?>
                {
                    null,
                    row.Number,
                    row.Name,
                    row.Source.IsEmpty
                        ? "nothing"
                        : row.Source.Kinded() + ": " + row.Source.Describe(),
                };

                foreach (var unused in board.Columns) cells.Add(null);

                sheet.Row(cells.ToArray());
            }

            var head = new XlsxStyle(bold: true, boxed: true);

            sheet.Paint(Heading, Number, head);
            sheet.Paint(Heading, Name, head);
            sheet.Paint(Heading, Points, head);

            // Each heading tinted the way its own header is tinted on the screen: a wash of its
            // folder's colour, far enough towards white to read a name on.
            for (var at = 0; at < board.Columns.Count; at++)
            {
                var pale = MatrixInk.Washed(board.ColourOf(board.Columns[at].Folder), 0.62);

                sheet.Paint(Heading, First + at,
                            new XlsxStyle(pale.Length == 0 ? null : pale,
                                          bold: true, centre: true, turned: true, boxed: true));
            }

            for (var at = 0; at < board.Rows.Count; at++)
            {
                var pale = MatrixInk.Washed(board.ColourOf(board.Rows[at].Folder), 0.62);
                var tint = new XlsxStyle(pale.Length == 0 ? null : pale, boxed: true);

                sheet.Paint(Body + at, Number, tint);
                sheet.Paint(Body + at, Name, tint);
                sheet.Paint(Body + at, Points, tint);
            }

            Bands(sheet, board, board.Columns, across: true);
            Bands(sheet, board, board.Rows, across: false);

            return sheet;
        }

        /// <summary>
        /// The folder bands, in the folder's own colour, merged across the run each covers.
        ///
        /// A run rather than a folder: the bands say where a block of the grid begins and ends,
        /// so a folder whose members have been split by a drag is drawn as the two blocks it
        /// actually is rather than as one merge across a gap.
        /// </summary>
        /// <param name="sheet">The tab.</param>
        /// <param name="board">The matrix, which is what knows a folder's colour.</param>
        /// <param name="lines">The rows or the columns, in order.</param>
        /// <param name="across">True for the band above the columns.</param>
        private static void Bands(XlsxSheet sheet, MatrixBoard board,
                                  IReadOnlyList<MatrixLine> lines, bool across)
        {
            var at = 0;

            while (at < lines.Count)
            {
                var folder = lines[at].Folder;
                var run = at;

                while (run + 1 < lines.Count
                       && string.Equals(lines[run + 1].Folder, folder,
                                        StringComparison.OrdinalIgnoreCase))
                    run++;

                if (folder.Length > 0)
                {
                    var colour = board.ColourOf(folder);

                    var style = new XlsxStyle(colour.Length == 0 ? "EDF0F3" : colour,
                                              MatrixInk.IsDark(colour) ? "FFFFFF" : "000000",
                                              bold: true, centre: true, turned: !across,
                                              boxed: true);

                    if (across)
                    {
                        sheet.Rows[0] = Widened(sheet.Rows[0], First + run);
                        Set(sheet, 0, First + at, folder);
                        sheet.Paint(0, First + at, style);
                        sheet.Merge(0, First + at, 0, First + run);
                    }
                    else
                    {
                        Set(sheet, Body + at, Band, folder);
                        sheet.Paint(Body + at, Band, style);
                        sheet.Merge(Body + at, Band, Body + run, Band);
                    }
                }

                at = run + 1;
            }
        }

        /// <summary>Put a value in a cell, growing the row to reach it.</summary>
        private static void Set(XlsxSheet sheet, int row, int column, string? value)
        {
            while (sheet.Rows.Count <= row) sheet.Row();

            var cells = sheet.Rows[row].ToList();

            while (cells.Count <= column) cells.Add(null);

            cells[column] = value;
            sheet.Rows[row] = cells;
        }

        private static IReadOnlyList<string?> Widened(IReadOnlyList<string?> row, int to)
        {
            var cells = row.ToList();

            while (cells.Count <= to) cells.Add(null);

            return cells;
        }

        /// <summary>Put a value in a cell and paint it.</summary>
        private static void Write(XlsxSheet sheet, int row, int column, string? value,
                                  XlsxStyle style)
        {
            Set(sheet, row, column, value);
            sheet.Paint(row, column, style);
        }

        /// <summary>What wash a symbol's cells carry: the one handed in, or the legend's own.</summary>
        private static string Wash(string symbol, MatrixLegend? legend,
                                   IReadOnlyDictionary<string, string>? washes)
        {
            if (washes != null && washes.TryGetValue(symbol, out var given)
                && !string.IsNullOrWhiteSpace(given))
                return given;

            var meaning = legend?.Meaning(symbol);

            return meaning == null ? string.Empty : meaning.Colour;
        }

        /// <summary>
        /// The legend, and the two marks that are not symbols, each on the ground its cells carry.
        ///
        /// A key that is not drawn the way the thing it explains is drawn is not a key - which is
        /// exactly what a column of bare letters beside a coloured grid was.
        /// </summary>
        /// <param name="board">The matrix.</param>
        /// <param name="legend">What its symbols mean, or null.</param>
        /// <param name="washes">The colour each symbol's cells carry.</param>
        private static XlsxSheet Legend(MatrixBoard board, MatrixLegend? legend,
                                        IReadOnlyDictionary<string, string>? washes)
        {
            var sheet = new XlsxSheet("Legend");

            sheet.Row("Mark", "What it means");
            sheet.Widths[0] = 8;
            sheet.Widths[1] = 64;

            var at = 1;

            void Line(string mark, string says, string ground)
            {
                sheet.Row(mark, says);
                sheet.Paint(at, 0, new XlsxStyle(ground.Length == 0 ? null : ground,
                                                 MatrixInk.IsDark(ground) ? "FFFFFF" : "000000",
                                                 bold: true, centre: true, boxed: true));
                at++;
            }

            foreach (var symbol in legend?.Symbols ?? Array.Empty<MatrixSymbol>())
                Line(symbol.Symbol,
                     MatrixNaming.Worded(symbol.Test) + " at "
                     + (symbol.Tolerance * 1000).ToString("0.###", CultureInfo.InvariantCulture)
                     + " mm" + (symbol.Enabled ? string.Empty : ", off - no test is made"),
                     Wash(symbol.Symbol, legend, washes));

            Line(MatrixBoard.NotRequired, "Not required, agreed - no test is made",
                 MatrixInk.Ruled);

            Line(string.Empty, "Undecided - no test is made",
                 string.Empty);

            sheet.Row();
            at++;

            sheet.Row("The matrix is mirrored: a pair reads the same either way round, and is "
                      + "tested once. The quiet half is the same pairs read the other way.");
            at++;

            if (board.Ranks().Any())
            {
                sheet.Row();
                at++;

                sheet.Row("Priority");
                sheet.Paint(at, 0, new XlsxStyle(bold: true));
                at++;

                Line("H", "High", MatrixInk.High);
                Line("M", "Medium", MatrixInk.Medium);
                Line("L", "Low", MatrixInk.Low);
            }

            return sheet;
        }
    }
}
