using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>One row or column of the matrix: a number, a name, and where its elements are.</summary>
    public sealed class MatrixLine
    {
        /// <summary>Draw a line.</summary>
        /// <param name="name">How it reads — "Ductwork", "Package 3".</param>
        /// <param name="number">Its number in the sheet, used in test names. Optional.</param>
        /// <param name="source">Where its elements come from.</param>
        /// <param name="code">A discipline code such as "ST". Optional.</param>
        public MatrixLine(string name, string? number = null, MatrixSource? source = null,
                          string? code = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim();
            Number = (number ?? string.Empty).Trim();
            Source = source ?? MatrixSource.Nothing;
            Code = (code ?? string.Empty).Trim();
        }

        /// <summary>How it reads.</summary>
        public string Name { get; set; }

        /// <summary>Its number, or empty.</summary>
        public string Number { get; set; }

        /// <summary>Its discipline code — "ST", "ME" — or empty. A sheet often carries one.</summary>
        public string Code { get; set; }

        /// <summary>Where its elements come from.</summary>
        public MatrixSource Source { get; set; }

        /// <summary>Nothing is behind it, so no test on it could run.</summary>
        public bool IsUnbacked => Source.IsEmpty;

        /// <summary>Number and name, as a header reads.</summary>
        public string Titled => Number.Length == 0 ? Name : Number + " " + Name;

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("name", Name);

            if (Number.Length > 0) json.Set("number", Number);
            if (Code.Length > 0) json.Set("code", Code);

            json.Set("source", Source.ToJson());

            return json;
        }

        /// <summary>Read one back, or null when there is no name to read.</summary>
        /// <param name="json">The stored line.</param>
        public static MatrixLine? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var name = (json["name"].AsString() ?? string.Empty).Trim();

            if (name.Length == 0) return null;

            return new MatrixLine(name, json["number"].AsString(),
                                  MatrixSource.FromJson(json["source"]),
                                  json["code"].AsString());
        }

        /// <inheritdoc />
        public override string ToString() => Titled;
    }

    /// <summary>
    /// The clash matrix: rows down one side, columns across the top, a symbol where they meet.
    ///
    /// <b>Rows and columns are two lists, not one.</b> The square, mirrored grid a coordination
    /// kick-off draws is the case where they happen to hold the same things — and it is the
    /// common case — but it is not the only shape anybody wants. A row is side A of a test and a
    /// column is side B; that is the whole rule, and it lets somebody put four packages down the
    /// side and every discipline across the top without inventing categories that mean nothing on
    /// the other axis.
    ///
    /// <b>What follows from that.</b> Nothing is mirrored: cell (r,c) is stored where it is put.
    /// Where the two lists <em>do</em> hold the same thing, the pair (Ducts, Steel) and the pair
    /// (Steel, Ducts) are two cells naming one test, and the planner is what notices — see
    /// <see cref="MatrixPlan"/>. Storing one half and reflecting it would be the grid deciding
    /// something about the matrix's shape that only the person drawing it knows.
    /// </summary>
    public sealed class MatrixBoard
    {
        private readonly List<MatrixLine> _rows = new List<MatrixLine>();
        private readonly List<MatrixLine> _columns = new List<MatrixLine>();

        // Keyed on the lines themselves rather than on their indexes, so inserting, removing and
        // dragging a row into a new place moves its cells with it instead of leaving every
        // symbol below the change attached to the wrong pair. An index-keyed grid renumbers
        // correctly right up until the first bug in the renumbering, and then it still reads as
        // a grid while saying something nobody meant.
        private readonly Dictionary<(MatrixLine Row, MatrixLine Column), string> _cells =
            new Dictionary<(MatrixLine, MatrixLine), string>();

        /// <summary>The rows, which are side A.</summary>
        public IReadOnlyList<MatrixLine> Rows => _rows;

        /// <summary>The columns, which are side B.</summary>
        public IReadOnlyList<MatrixLine> Columns => _columns;

        /// <summary>How many cells carry a symbol.</summary>
        public int Filled => _cells.Count;

        /// <summary>Nothing to build from.</summary>
        public bool IsEmpty => _rows.Count == 0 || _columns.Count == 0 || _cells.Count == 0;

        /// <summary>Every distinct symbol in the grid, ordered.</summary>
        public IReadOnlyList<string> SymbolsUsed =>
            _cells.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Add a row, and hand it back.</summary>
        /// <param name="line">The row.</param>
        public MatrixLine AddRow(MatrixLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            _rows.Add(line);
            return line;
        }

        /// <summary>Add a column, and hand it back.</summary>
        /// <param name="line">The column.</param>
        public MatrixLine AddColumn(MatrixLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            _columns.Add(line);
            return line;
        }

        /// <summary>Take a row out, and its cells with it.</summary>
        /// <param name="line">The row.</param>
        public void RemoveRow(MatrixLine line) => Drop(_rows, line);

        /// <summary>Take a column out, and its cells with it.</summary>
        /// <param name="line">The column.</param>
        public void RemoveColumn(MatrixLine line) => Drop(_columns, line);

        /// <summary>Move a row to a new place in the order, keeping its cells.</summary>
        /// <param name="from">Where it is.</param>
        /// <param name="to">Where it should go.</param>
        public void MoveRow(int from, int to) => Move(_rows, from, to);

        /// <summary>Move a column to a new place in the order, keeping its cells.</summary>
        /// <param name="from">Where it is.</param>
        /// <param name="to">Where it should go.</param>
        public void MoveColumn(int from, int to) => Move(_columns, from, to);

        /// <summary>
        /// Put a symbol where a row meets a column, or clear it with null. The mirror follows.
        ///
        /// <b>A clash matrix is mirrored about its diagonal</b>, because A against B is the same
        /// test as B against A. So where both axes carry the same two things, writing one cell
        /// writes the other: the grid cannot hold two different answers about one pair, and a
        /// person filling in the half they were given never has to fill the other.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <param name="symbol">The symbol, or null to clear.</param>
        public void Set(MatrixLine? row, MatrixLine? column, string? symbol)
        {
            if (row == null || column == null) return;

            Write(row, column, symbol);

            var mirror = MirrorOf(row, column);

            if (mirror != null) Write(mirror.Value.Row, mirror.Value.Column, symbol);
        }

        /// <summary>
        /// The cell that names the same pair the other way round, or null when there is none.
        ///
        /// There is one only where both axes carry both things - the square matrix everybody
        /// draws. A grid of packages down the side against disciplines across the top has no
        /// diagonal and no mirror, and every cell in it is its own pair.
        ///
        /// A cell on the diagonal is its own mirror, and is reported as such rather than as
        /// nothing: a source against itself is a real test, and it is written once.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public (MatrixLine Row, MatrixLine Column)? MirrorOf(MatrixLine? row, MatrixLine? column)
        {
            if (row == null || column == null) return null;

            var mirrorRow = _rows.FirstOrDefault(r => Same(r, column));
            var mirrorColumn = _columns.FirstOrDefault(c => Same(c, row));

            if (mirrorRow == null || mirrorColumn == null) return null;

            if (ReferenceEquals(mirrorRow, row) && ReferenceEquals(mirrorColumn, column))
                return null;

            return (mirrorRow, mirrorColumn);
        }

        /// <summary>
        /// Whether this cell is the half of a mirrored pair that the grid draws.
        ///
        /// The earlier of the two in reading order. Nothing about a matrix says which triangle
        /// somebody fills in, so the one above the diagonal is kept for no reason but that one of
        /// them has to be, and every drawing of a clash matrix keeps one.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public bool IsKeptHalf(MatrixLine? row, MatrixLine? column)
        {
            if (row == null || column == null) return false;

            var mirror = MirrorOf(row, column);

            if (mirror == null) return true;

            var here = (_rows.IndexOf(row), _columns.IndexOf(column));
            var there = (_rows.IndexOf(mirror.Value.Row), _columns.IndexOf(mirror.Value.Column));

            return here.Item1 < there.Item1
                   || (here.Item1 == there.Item1 && here.Item2 <= there.Item2);
        }

        /// <summary>Whether a row and a column stand for the same thing.</summary>
        private static bool Same(MatrixLine one, MatrixLine other) =>
            !one.Source.IsEmpty
                ? string.Equals(one.Source.Fingerprint(), other.Source.Fingerprint(),
                                StringComparison.Ordinal)
                : other.Source.IsEmpty
                  && string.Equals(one.Name, other.Name, StringComparison.OrdinalIgnoreCase);

        private void Write(MatrixLine row, MatrixLine column, string? symbol)
        {
            var text = symbol == null ? string.Empty : symbol.Trim();

            if (text.Length == 0) _cells.Remove((row, column));
            else _cells[(row, column)] = text;
        }

        /// <summary>What is where a row meets a column, or null.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public string? At(MatrixLine? row, MatrixLine? column) =>
            row != null && column != null && _cells.TryGetValue((row, column), out var symbol)
                ? symbol
                : null;

        /// <summary>
        /// Every filled cell that names a pair, in reading order - each pair once.
        ///
        /// The mirror half is not yielded. It holds the same symbol about the same pair, so a
        /// planner reading both would ask the host for two identical tests, and a planner that
        /// noticed and skipped one would report a skip on every square matrix ever drawn.
        /// </summary>
        public IEnumerable<(MatrixLine Row, MatrixLine Column, string Symbol)> Cells()
        {
            foreach (var row in _rows)
                foreach (var column in _columns)
                {
                    if (!IsKeptHalf(row, column)) continue;

                    var symbol = At(row, column);

                    if (symbol != null) yield return (row, column, symbol);
                }
        }

        /// <summary>Put the same symbol in every cell of the grid.</summary>
        /// <param name="symbol">The symbol, or null to clear the grid.</param>
        public void Fill(string? symbol)
        {
            foreach (var row in _rows)
                foreach (var column in _columns)
                    Set(row, column, symbol);
        }

        /// <summary>
        /// Make the columns a copy of the rows, which is the square matrix everybody draws.
        ///
        /// Copies rather than shares: a column that was the same object as its row would rename
        /// itself when the row was renamed, which is right until somebody wants the two axes to
        /// diverge — and then it is a bug nobody can see the cause of.
        /// </summary>
        public void SquareOffRows()
        {
            var kept = new Dictionary<(MatrixLine, MatrixLine), string>(_cells);

            _columns.Clear();

            var pairing = new Dictionary<MatrixLine, MatrixLine>();

            foreach (var row in _rows)
            {
                var column = new MatrixLine(row.Name, row.Number, row.Source, row.Code);

                pairing[row] = column;
                _columns.Add(column);
            }

            _cells.Clear();

            // What was in a cell survives when both its ends are still there, matched by name so
            // squaring off a grid that was already square keeps every symbol in it.
            foreach (var cell in kept)
            {
                var column = _columns.FirstOrDefault(
                    c => string.Equals(c.Name, cell.Key.Item2.Name, StringComparison.OrdinalIgnoreCase))
                    ?? (pairing.TryGetValue(cell.Key.Item2, out var mapped) ? mapped : null);

                if (_rows.Contains(cell.Key.Item1) && column != null)
                    _cells[(cell.Key.Item1, column)] = cell.Value;
            }
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            json.Set("rows", JsonValue.Array(_rows.Select(r => r.ToJson())));
            json.Set("columns", JsonValue.Array(_columns.Select(c => c.ToJson())));

            var cells = JsonValue.Array(Cells().Select(cell =>
                JsonValue.Object()
                    .Set("row", (double)_rows.IndexOf(cell.Row))
                    .Set("column", (double)_columns.IndexOf(cell.Column))
                    .Set("symbol", cell.Symbol)));

            json.Set("cells", cells);

            return json;
        }

        /// <summary>Read one back, or an empty board.</summary>
        /// <param name="json">The stored board.</param>
        public static MatrixBoard FromJson(JsonValue? json)
        {
            var board = new MatrixBoard();

            if (json == null || json.Kind != JsonKind.Object) return board;

            foreach (var held in json["rows"].Items)
            {
                var line = MatrixLine.FromJson(held);

                if (line != null) board._rows.Add(line);
            }

            foreach (var held in json["columns"].Items)
            {
                var line = MatrixLine.FromJson(held);

                if (line != null) board._columns.Add(line);
            }

            foreach (var held in json["cells"].Items)
            {
                var row = (int)held["row"].AsDouble();
                var column = (int)held["column"].AsDouble();

                if (row < 0 || row >= board._rows.Count) continue;
                if (column < 0 || column >= board._columns.Count) continue;

                board.Set(board._rows[row], board._columns[column], held["symbol"].AsString());
            }

            return board;
        }

        private void Drop(List<MatrixLine> from, MatrixLine? line)
        {
            if (line == null || !from.Remove(line)) return;

            foreach (var key in _cells.Keys.Where(k => k.Row == line || k.Column == line).ToList())
                _cells.Remove(key);
        }

        private static void Move(List<MatrixLine> lines, int from, int to)
        {
            if (from < 0 || from >= lines.Count) return;

            var wanted = to < 0 ? 0 : to >= lines.Count ? lines.Count - 1 : to;

            if (wanted == from) return;

            var line = lines[from];

            lines.RemoveAt(from);
            lines.Insert(wanted, line);
        }

        /// <inheritdoc />
        public override string ToString() =>
            _rows.Count.ToString(CultureInfo.InvariantCulture) + " rows, "
            + _columns.Count.ToString(CultureInfo.InvariantCulture) + " columns, "
            + _cells.Count.ToString(CultureInfo.InvariantCulture) + " filled";
    }
}
