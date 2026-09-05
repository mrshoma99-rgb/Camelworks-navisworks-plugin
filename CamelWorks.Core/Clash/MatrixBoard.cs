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

        /// <summary>
        /// What this line is, as against what it is called.
        ///
        /// <b>Two lines were judged the same thing by their names.</b> A row and the column
        /// standing for it had to be recognised as one discipline - that is what mirrors the
        /// grid, what makes the diagonal the diagonal, and what stops a pair being asked twice -
        /// and with nothing else to go on the test was the name. Which is fine until somebody
        /// adds six disciplines and names none of them yet: six rows called "New category" are
        /// then all the same line as each other, every pair among them is a pair with itself,
        /// the whole block is diagonal, and the grid quietly refuses to hold anything.
        ///
        /// A discipline knows what it is. The pair is made together and says so here, so identity
        /// survives being unnamed, being renamed, and being named the same as its neighbour.
        ///
        /// Empty on a line read from a board saved before this existed, and the older test by
        /// source and name still answers for those.
        /// </summary>
        public string Key { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>How it reads.</summary>
        public string Name { get; set; }

        /// <summary>Its number, or empty.</summary>
        public string Number { get; set; }

        /// <summary>Its discipline code — "ST", "ME" — or empty. A sheet often carries one.</summary>
        public string Code { get; set; }

        /// <summary>
        /// The folder it is filed in, or empty for one that is loose.
        ///
        /// <b>A folder is a name several lines share, not an object they hang off.</b> Twenty
        /// disciplines is a readable matrix; sixty is a wall, and the way anybody makes sense of
        /// sixty is "these eight are services". Held as a name on the line, a folder cannot go
        /// stale against the lines in it, cannot be emptied without emptying itself, and
        /// survives every reorder, save and undo that the lines survive - because it is the
        /// lines. The board keeps the members of one folder next to each other; what a folder
        /// <i>is</i> is the set of lines that answer to its name.
        /// </summary>
        public string Folder { get; set; } = string.Empty;

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
            if (Folder.Length > 0) json.Set("folder", Folder);
            if (Key.Length > 0) json.Set("key", Key);

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
                                  json["code"].AsString())
            {
                Folder = (json["folder"].AsString() ?? string.Empty).Trim(),
                Key = (json["key"].AsString() ?? string.Empty).Trim(),
            };
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

        /// <summary>
        /// The mark for a pair somebody has ruled out on purpose.
        ///
        /// <b>Blank used to mean two things.</b> "Nobody has got to this yet" and "we agreed at
        /// the kick-off that these two are never checked" were the same empty cell, and forty
        /// per cent of a real grid is empty - so a coordinator inheriting one in March cannot
        /// tell an agreement from an abandonment, and the count of what is left to decide can
        /// never reach the end.
        ///
        /// An en dash, which is what every paper matrix has used for this for as long as they
        /// have been drawn. It is reserved rather than a legend entry: a symbol somebody can
        /// give a tolerance to and switch on is a symbol that turns ninety agreed-not-required
        /// pairs into ninety tests the first time anybody ticks it.
        /// </summary>
        public const string NotRequired = "\u2013";

        /// <summary>Whether a cell's mark is the reserved one for a pair ruled out on purpose.</summary>
        /// <param name="symbol">What is in the cell, or null.</param>
        public static bool IsNotRequired(string? symbol) =>
            string.Equals(symbol, NotRequired, StringComparison.Ordinal);

        /// <summary>How many cells carry a symbol.</summary>
        public int Filled => _cells.Count;

        /// <summary>Nothing to build from.</summary>
        public bool IsEmpty => _rows.Count == 0 || _columns.Count == 0 || _cells.Count == 0;

        /// <summary>Every distinct symbol in the grid, ordered.</summary>
        public IReadOnlyList<string> SymbolsUsed =>
            _cells.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Whether the two axes are one list of categories, kept in step.
        ///
        /// <b>A clash matrix is a list of disciplines against itself.</b> Structure against
        /// services is the same agreement as services against structure, which is the whole
        /// reason the grid mirrors about its diagonal and half of it is dead. Two axes that can
        /// be added to separately are not that: they are two lists that happen to overlap, and
        /// the mirroring, the dead half and the diagonal all become coincidences of what somebody
        /// typed twice.
        ///
        /// So one act adds a category and it lands on both axes, one act takes it away and it
        /// leaves both, and naming, numbering, coding and pointing already carry across.
        ///
        /// It can be turned off, because a matrix of one trade's sets against another's is a
        /// real thing somebody builds and the grid is allowed to be it. Off is the exception a
        /// person chooses, not the shape everybody starts in.
        /// </summary>
        public bool Paired { get; set; } = true;

        /// <summary>
        /// Add a category: a row, and the column standing for the same thing.
        ///
        /// The column is its own line rather than the same object on both axes - everything here
        /// asks which list a line is in to know which axis it is - but it is made from the row,
        /// so the two agree from the moment they exist and every edit that follows takes its twin
        /// with it.
        /// </summary>
        /// <param name="line">The category, which becomes the row.</param>
        /// <returns>The row, so the caller can pick it.</returns>
        public MatrixLine AddPair(MatrixLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            _rows.Add(line);

            if (Paired)
                _columns.Add(new MatrixLine(line.Name, line.Number, line.Source, line.Code)
                {
                    Folder = line.Folder,
                    Key = line.Key,
                });

            return line;
        }

        /// <summary>
        /// Take a category out, both axes at once.
        ///
        /// Half a category left behind is a line whose every cell is dead - it names a discipline
        /// the matrix no longer has - and it is the shape that made "remove this row" quietly
        /// leave a column nobody could account for.
        /// </summary>
        /// <param name="line">A row or a column of this board.</param>
        public void RemovePair(MatrixLine? line)
        {
            if (line == null) return;

            // Found first, because dropping the line is what stops them being twins.
            var twin = Paired ? TwinOf(line) : null;

            Drop(_rows.Contains(line) ? _rows : _columns, line);

            if (twin != null) Drop(_rows.Contains(twin) ? _rows : _columns, twin);
        }

        // ---------------------------------------------------------------------------
        // Folders
        // ---------------------------------------------------------------------------

        /// <summary>The folders on one axis, in the order their first member appears.</summary>
        /// <param name="rows">True for the rows, false for the columns.</param>
        public IReadOnlyList<string> Folders(bool rows)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in rows ? _rows : _columns)
                if (line.Folder.Length > 0 && seen.Add(line.Folder))
                    found.Add(line.Folder);

            return found;
        }

        /// <summary>
        /// What colour a folder is drawn in, by name, or empty for the skin's own.
        ///
        /// <b>A folder is a name its members share, and nothing else - which is why this is
        /// kept here.</b> There is no folder object to hang a colour on: filing a line is
        /// writing a string on it, and a folder exists exactly as long as some line is still
        /// carrying its name. So the colour lives with the board, keyed by that name, and the
        /// acts that change a name have to bring it along.
        /// </summary>
        private readonly Dictionary<string, string> _folderColours =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The colour a folder is drawn in, or empty when nobody has chosen one.
        ///
        /// <b>Colour is how a band of eight rows becomes one thing at a glance.</b> Twenty-two
        /// disciplines in five packages read as twenty-two disciplines until the packages are
        /// told apart, and the band down the side is the only place on the screen with room to
        /// say which is which without writing it out again on every row.
        /// </summary>
        /// <param name="folder">The folder's name.</param>
        public string ColourOf(string? folder)
        {
            var name = (folder ?? string.Empty).Trim();

            return name.Length > 0 && _folderColours.TryGetValue(name, out var colour)
                ? colour
                : string.Empty;
        }

        /// <summary>Give a folder a colour, or take the one it has away.</summary>
        /// <param name="folder">The folder's name.</param>
        /// <param name="colour">The colour, or null or empty for the skin's own.</param>
        public void Paint(string? folder, string? colour)
        {
            var name = (folder ?? string.Empty).Trim();

            if (name.Length == 0) return;

            var wanted = (colour ?? string.Empty).Trim();

            if (wanted.Length == 0) _folderColours.Remove(name);
            else _folderColours[name] = wanted;
        }

        /// <summary>Every folder that has been given a colour, by name.</summary>
        public IReadOnlyDictionary<string, string> FolderColours => _folderColours;

        /// <summary>
        /// Carry a folder's colour to a new name, and drop it when the folder is gone.
        ///
        /// Renaming a folder that somebody coloured must not lose the colour, and dissolving one
        /// must not leave its colour behind to be inherited by the next folder that happens to be
        /// given the same name.
        /// </summary>
        /// <param name="was">The name it had.</param>
        /// <param name="wanted">The name it has now, or empty when it is being dissolved.</param>
        private void Recoloured(string was, string wanted)
        {
            if (!_folderColours.TryGetValue(was, out var colour)) return;

            _folderColours.Remove(was);

            // Merging into a folder that already has a colour keeps the one it has: the name
            // being merged into is the one that survives, so its colour is the one that survives.
            if (wanted.Length > 0 && !_folderColours.ContainsKey(wanted))
                _folderColours[wanted] = colour;
        }

        /// <summary>Forget the colours of folders no line answers to any more.</summary>
        private void Swept()
        {
            var live = new HashSet<string>(Folders(rows: true).Concat(Folders(rows: false)),
                                           StringComparer.OrdinalIgnoreCase);

            foreach (var gone in _folderColours.Keys.Where(k => !live.Contains(k)).ToList())
                _folderColours.Remove(gone);
        }

        /// <summary>Every line filed in one folder, in the order they sit on their axis.</summary>
        /// <param name="rows">True for the rows.</param>
        /// <param name="folder">The folder's name.</param>
        public IReadOnlyList<MatrixLine> InFolder(bool rows, string? folder) =>
            (rows ? _rows : _columns)
                .Where(l => string.Equals(l.Folder, (folder ?? string.Empty).Trim(),
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

        /// <summary>
        /// File a line in a folder, and file its twin with it.
        ///
        /// <b>Filing is also a move.</b> A folder is only legible while its members are next to
        /// each other - a band down the side with two of its eight rows somewhere else is not a
        /// folder, it is a label that lies - so a line dropped into a folder goes to the end of
        /// that folder's block rather than staying where it was. A line taken out of one goes
        /// after the block it just left, which is where a hand expects to find it.
        /// </summary>
        /// <param name="line">The row or column.</param>
        /// <param name="folder">The folder, or null or empty to take it out of one.</param>
        public void File(MatrixLine? line, string? folder)
        {
            if (line == null) return;

            var wanted = (folder ?? string.Empty).Trim();

            Filed(_rows.Contains(line) ? _rows : _columns, line, wanted);

            if (!Paired) return;

            var twin = TwinOf(line);

            if (twin != null) Filed(_rows.Contains(twin) ? _rows : _columns, twin, wanted);
        }

        private static void Filed(List<MatrixLine> lines, MatrixLine line, string folder)
        {
            var at = lines.IndexOf(line);

            if (at < 0) return;

            var was = line.Folder;

            line.Folder = folder;

            // The end of the block it now belongs to, or the end of the one it just left.
            var last = -1;

            for (var i = 0; i < lines.Count; i++)
            {
                if (i == at) continue;

                if (string.Equals(lines[i].Folder,
                                  folder.Length > 0 ? folder : was,
                                  StringComparison.OrdinalIgnoreCase))
                    last = i;
            }

            if (last < 0) return;

            var to = last < at ? last + 1 : last;

            if (to == at) return;

            lines.RemoveAt(at);
            lines.Insert(to, line);
        }

        /// <summary>
        /// Call a folder something else, on both axes.
        ///
        /// Every line answering to the old name answers to the new one, which is the whole of
        /// what renaming a folder is when a folder is a name its members share.
        /// </summary>
        /// <param name="from">What it is called.</param>
        /// <param name="to">What it should be called. Empty dissolves it.</param>
        /// <returns>How many lines were refiled.</returns>
        public int RenameFolder(string? from, string? to)
        {
            var was = (from ?? string.Empty).Trim();
            var wanted = (to ?? string.Empty).Trim();

            if (was.Length == 0) return 0;

            var moved = 0;

            foreach (var line in _rows.Concat(_columns))
            {
                if (!string.Equals(line.Folder, was, StringComparison.OrdinalIgnoreCase)) continue;

                line.Folder = wanted;
                moved++;
            }

            Recoloured(was, wanted);

            return moved;
        }

        /// <summary>
        /// Take a folder away and leave its lines where they are.
        ///
        /// <b>Dissolving is not deleting.</b> A folder holds nothing of its own - no source, no
        /// symbol, no decision - so taking it away can never cost anybody a decision they made,
        /// and the two acts have to be told apart on the menu that offers them.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>How many lines came loose.</returns>
        public int DissolveFolder(string? folder) => RenameFolder(folder, null);

        /// <summary>
        /// Take a folder away and every line in it, on both axes.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>How many disciplines went.</returns>
        public int RemoveFolder(string? folder)
        {
            var wanted = (folder ?? string.Empty).Trim();

            if (wanted.Length == 0) return 0;

            var going = _rows.Concat(_columns)
                             .Where(l => string.Equals(l.Folder, wanted,
                                                       StringComparison.OrdinalIgnoreCase))
                             .ToList();

            var went = 0;

            foreach (var line in going)
            {
                // RemovePair takes the twin, so a line already gone this way is skipped.
                if (!_rows.Contains(line) && !_columns.Contains(line)) continue;

                RemovePair(line);
                went++;
            }

            _folderColours.Remove(wanted);

            return went;
        }

        /// <summary>
        /// Put several folders together under one name.
        ///
        /// The name they are merged into may be one of them or a new one; either way every line
        /// in any of them ends up answering to it, on both axes, and next to each other.
        /// </summary>
        /// <param name="folders">The folders being merged.</param>
        /// <param name="into">What the one folder is called.</param>
        /// <returns>How many lines ended up in it.</returns>
        public int MergeFolders(IEnumerable<string>? folders, string? into)
        {
            var wanted = (into ?? string.Empty).Trim();

            if (wanted.Length == 0 || folders == null) return 0;

            var names = new HashSet<string>(
                folders.Select(f => (f ?? string.Empty).Trim()).Where(f => f.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            if (names.Count == 0) return 0;

            var moved = 0;

            foreach (var line in _rows.Concat(_columns))
            {
                if (!names.Contains(line.Folder)) continue;

                line.Folder = wanted;
                moved++;
            }

            foreach (var name in names) Recoloured(name, wanted);

            Tidy();
            Swept();

            return moved;
        }

        /// <summary>
        /// Put both axes in folder order, so every folder's members sit together.
        ///
        /// The folders keep the order their first member is already in, and the loose lines stay
        /// at the end - so tidying a grid somebody has arranged does not rearrange it, it only
        /// closes the gaps that filing has opened.
        /// </summary>
        public void Tidy()
        {
            Tidied(_rows);
            Tidied(_columns);
        }

        private static void Tidied(List<MatrixLine> lines)
        {
            var order = new List<string>();

            foreach (var line in lines)
                if (line.Folder.Length > 0
                    && !order.Any(f => string.Equals(f, line.Folder, StringComparison.OrdinalIgnoreCase)))
                    order.Add(line.Folder);

            var sorted = lines
                .OrderBy(l => l.Folder.Length == 0
                    ? order.Count
                    : order.FindIndex(f => string.Equals(f, l.Folder, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (sorted.SequenceEqual(lines)) return;

            lines.Clear();
            lines.AddRange(sorted);
        }

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
        /// Which triangle of a mirrored matrix is the one that is worked in.
        ///
        /// <b>Nothing about a matrix says which one it should be.</b> One of the two has to be,
        /// and every drawing of a clash matrix keeps one - but which one is a house habit, and
        /// half the practices that draw these fill in the lower triangle because that is how the
        /// sheet they inherited was drawn. Being handed the other one and told it is the dead
        /// half is being told your own matrix is upside down.
        ///
        /// It changes what is drawn and nothing else: a pair is written to both cells whichever
        /// half it is put in, so the tests that come out are the same either way.
        /// </summary>
        public bool UpperIsLive { get; set; } = true;

        /// <summary>
        /// Whether this cell is the one of a mirrored pair that the matrix keeps.
        ///
        /// <b>Always the earlier of the two, whichever half is drawn.</b> This is what walks the
        /// cells, what the planner counts and what the test names are built from, so it cannot
        /// follow a drawing preference: a pair kept the other way round would name every test
        /// "Ducts vs Steel" where it said "Steel vs Ducts", and the difference against the
        /// document would read as two hundred and thirty-one renames because somebody preferred
        /// the lower triangle.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public bool IsKeptHalf(MatrixLine? row, MatrixLine? column) => Earlier(row, column) == true;

        /// <summary>
        /// Whether this cell is in the half somebody is working in, which is the half drawn live.
        ///
        /// The only thing <see cref="UpperIsLive"/> changes. A pair is written to both cells
        /// whichever one it is put in, so this decides what is painted and nothing else.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public bool IsDrawnHalf(MatrixLine? row, MatrixLine? column)
        {
            var earlier = Earlier(row, column);

            if (earlier == null) return false;

            // A cell with no mirror - a grid whose axes hold different things - is always drawn.
            if (MirrorOf(row, column) == null) return true;

            return UpperIsLive ? earlier.Value : !earlier.Value;
        }

        /// <summary>
        /// Whether a cell comes before its mirror in reading order, or null when there is no cell.
        ///
        /// A cell with no mirror, and a cell on the diagonal, are both their own earlier half.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        private bool? Earlier(MatrixLine? row, MatrixLine? column)
        {
            if (row == null || column == null) return null;

            var mirror = MirrorOf(row, column);

            if (mirror == null) return true;

            var here = (_rows.IndexOf(row), _columns.IndexOf(column));
            var there = (_rows.IndexOf(mirror.Value.Row), _columns.IndexOf(mirror.Value.Column));

            return here.Item1 < there.Item1
                   || (here.Item1 == there.Item1 && here.Item2 <= there.Item2);
        }

        /// <summary>
        /// Give every cell carrying one symbol a different one, and say how many changed.
        ///
        /// A symbol in a legend is a name somebody chose - "A" because the client's sheet said A -
        /// and renaming it afterwards used to mean taking it out of the legend, which orphaned
        /// every cell using it, and then painting them all again. The cells follow the name.
        /// </summary>
        /// <param name="from">The symbol as it is now.</param>
        /// <param name="to">What it becomes. Empty or null empties the cells.</param>
        public int Recast(string? from, string? to)
        {
            if (string.IsNullOrWhiteSpace(from)) return 0;

            var wanted = string.IsNullOrWhiteSpace(to) ? null : to!.Trim();
            var changed = 0;

            foreach (var row in _rows)
            foreach (var column in _columns)
            {
                var held = At(row, column);

                if (held == null
                    || !string.Equals(held, from!.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                Write(row, column, wanted);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// Whether a row and a column stand for the same thing.
        ///
        /// <b>A line nobody has pointed yet is still the discipline it is named after.</b> This
        /// used to say: if either side has a source, the two are the same only when their sources
        /// are. Which is right for two pointed lines and catastrophic halfway through making a
        /// matrix - twenty names typed, squared off, and then pointed one row at a time, every row
        /// pointed stopped matching its own column. The pair stopped being mirrored, so both cells
        /// of it became live and the grid quietly grew from a triangle of two hundred and ten to
        /// a square of four hundred, with the dead half drawn as decisions nobody had made. And
        /// nothing built, because the planner drops a pair whose column points at nothing.
        ///
        /// So an empty source is not a different source; it is no answer yet, and the name is
        /// what is left to go on. Two lines that both point somewhere are the same when they
        /// point at the same thing, whatever they are called - which is what lets two axes
        /// deliberately diverge.
        /// </summary>
        /// <param name="one">One line.</param>
        /// <param name="other">The other.</param>
        private static bool Same(MatrixLine one, MatrixLine other)
        {
            var keyed = one.Key.Length > 0 && other.Key.Length > 0;

            // Made together, so known to be one discipline however either has been renamed since.
            if (keyed && string.Equals(one.Key, other.Key, StringComparison.Ordinal)) return true;

            // Pointed at the same elements, so one discipline however they were made - which is
            // what a matrix read out of a sheet or the model relies on, and what lets a stray
            // column be joined to a row by pointing it at that row's set.
            if (!one.Source.IsEmpty && !other.Source.IsEmpty)
                return string.Equals(one.Source.Fingerprint(), other.Source.Fingerprint(),
                                     StringComparison.Ordinal);

            // Both know what they are and they are not each other. This is the case a name could
            // never settle: six disciplines nobody has named yet are all called the same thing,
            // and by name every one of them was every other - so every pair among them was a pair
            // with itself, the whole block was diagonal, and the grid would hold nothing.
            if (keyed) return false;

            // A board saved before a line knew what it was. Its name is all there is to go on.
            return string.Equals(one.Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The line on the other axis that stands for the same thing, or null.
        /// </summary>
        /// <param name="line">A row or a column of this board.</param>
        public MatrixLine? TwinOf(MatrixLine? line)
        {
            if (line == null) return null;

            return _rows.Contains(line)
                ? _columns.FirstOrDefault(c => Same(c, line))
                : _columns.Contains(line)
                    ? _rows.FirstOrDefault(r => Same(r, line))
                    : null;
        }

        /// <summary>
        /// Point a line at something, and take its twin on the other axis with it.
        ///
        /// <b>Half of a pair pointed at nothing is a pair that builds nothing.</b> Saying what a
        /// row is is one decision, and the column standing for the same discipline is the same
        /// decision written twice - so the second one is not asked for. A twin that has already
        /// been pointed somewhere is left alone, because two axes that have deliberately diverged
        /// are a thing this grid is allowed to be.
        /// </summary>
        /// <param name="line">The row or column being pointed.</param>
        /// <param name="source">Where its elements come from.</param>
        /// <returns>The twin that was pointed with it, or null when there was none to point.</returns>
        public MatrixLine? Point(MatrixLine? line, MatrixSource? source)
        {
            if (line == null) return null;

            // Found before the change, because what makes them twins is about to move.
            var twin = TwinOf(line);

            line.Source = source ?? MatrixSource.Nothing;

            if (twin == null || !twin.Source.IsEmpty) return null;

            twin.Source = line.Source;

            return twin;
        }

        /// <summary>
        /// Rename a line, and rename the one standing for it on the other axis with it.
        ///
        /// <b>What makes two lines twins is what they are called, until somebody says what they
        /// are.</b> So on a matrix typed out by hand - where nothing points anywhere yet - fixing
        /// a typo in a row broke it away from its own column: the pair stopped being mirrored,
        /// both its cells went live, and the triangle became a square for that discipline. Which
        /// is the same fault pointing a row had, and the most likely act anybody performs.
        /// </summary>
        /// <param name="line">The row or column being renamed.</param>
        /// <param name="name">What it is called now.</param>
        /// <returns>The twin that was renamed with it, or null when there was none.</returns>
        public MatrixLine? Rename(MatrixLine? line, string? name)
        {
            if (line == null) return null;

            var wanted = (name ?? string.Empty).Trim();

            if (wanted.Length == 0 || string.Equals(wanted, line.Name, StringComparison.Ordinal))
            {
                if (line != null && wanted.Length > 0) line.Name = wanted;

                return null;
            }

            // Found before the change, because a name is what makes them twins.
            var twin = TwinOf(line);

            line.Name = wanted;

            if (twin == null) return null;

            twin.Name = wanted;

            return twin;
        }

        /// <summary>
        /// Put a symbol in every empty cell of the half that is worked in.
        ///
        /// <b>The press that was missing.</b> A matrix is nearly always "this one thing, except
        /// for the twenty pairs we talked about" - so the way it is filled is exceptions first,
        /// blanket last, and the blanket had only two sizes: one line, or the whole grid over
        /// everything already decided. Twenty gutter clicks, one per row, was the answer, and it
        /// is not an answer. Empty cells only, so it cannot destroy a decision, and never the
        /// diagonal, which no bulk fill writes.
        /// </summary>
        /// <param name="symbol">The symbol to put in them.</param>
        /// <returns>How many pairs were filled.</returns>
        public int FillEmpty(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return 0;

            var laid = 0;

            foreach (var row in _rows)
            foreach (var column in _columns)
            {
                if (!IsKeptHalf(row, column) || IsSelfPair(row, column)) continue;
                if (At(row, column) != null) continue;

                Set(row, column, symbol);
                laid++;
            }

            return laid;
        }

        /// <summary>
        /// Move a row or a column, and move its twin on the other axis to match.
        ///
        /// <b>Grouping a matrix is one job, not two.</b> The block gesture is a rectangle over the
        /// order the two axes are in, so "every MEP row against every structural column" is only
        /// one rectangle when the disciplines sit in the same order down the side and across the
        /// top. Moving them separately means doing the same reorder twice and getting it wrong
        /// once; a square matrix has one order, and this keeps it.
        /// </summary>
        /// <param name="line">The row or column to move.</param>
        /// <param name="to">Where it should sit, from zero.</param>
        /// <returns>True when anything moved.</returns>
        public bool MoveLine(MatrixLine? line, int to)
        {
            if (line == null) return false;

            var twin = TwinOf(line);
            var rows = _rows.Contains(line);

            var at = rows ? _rows.IndexOf(line) : _columns.IndexOf(line);

            if (at < 0 || to < 0) return false;

            if (rows) MoveRow(at, to); else MoveColumn(at, to);

            if (twin == null) return true;

            var other = rows ? _columns.IndexOf(twin) : _rows.IndexOf(twin);

            if (other < 0) return true;

            var landed = rows ? _rows.IndexOf(line) : _columns.IndexOf(line);

            if (rows) MoveColumn(other, landed); else MoveRow(other, landed);

            return true;
        }

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
        /// <param name="selfPairs">
        /// True to write the diagonal as well. Off by default: a discipline against itself is a
        /// legitimate test - it finds the pipe running through its own hanger - but it is never
        /// what somebody filling a whole grid in one press meant, and twenty-two of those on a
        /// twenty-two by twenty-two matrix is twenty-two tests nobody asked for.
        /// </param>
        public void Fill(string? symbol, bool selfPairs = false)
        {
            foreach (var row in _rows)
                foreach (var column in _columns)
                    if (selfPairs || !IsSelfPair(row, column))
                        Set(row, column, symbol);
        }

        /// <summary>
        /// Whether a cell is a discipline against itself.
        ///
        /// <b>Not the same object - the same thing.</b> A square matrix is built by copying the
        /// rows into the columns, so the row and the column on the diagonal are two objects
        /// pointing at one saved set. What makes them the same pair is what they point at, and -
        /// until somebody has said what that is - what they are called.
        ///
        /// This used to answer "no" whenever either side had no source, which is every cell of
        /// a matrix somebody is still typing the names of. So on a grid that had been squared
        /// off but not yet pointed, the diagonal was not a diagonal: bulk fills wrote it, and
        /// the spine was drawn as twenty ordinary cells.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public bool IsSelfPair(MatrixLine? row, MatrixLine? column) =>
            row != null && column != null && (ReferenceEquals(row, column) || Same(row, column));

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
            var wasColumns = new List<MatrixLine>(_columns);

            _columns.Clear();

            foreach (var row in _rows)
                _columns.Add(new MatrixLine(row.Name, row.Number, row.Source, row.Code)
                {
                    Folder = row.Folder,
                    Key = row.Key,
                });

            _cells.Clear();

            // <b>What was in a cell survives when both its ends are still there.</b> Matched by
            // name first, so squaring off a grid that was already square keeps every symbol in
            // it - and then, when the name has moved, by where the old column sat.
            //
            // The fallback used to look the old column up in a table keyed on rows, so it could
            // never resolve: rename one row, press this to tidy the columns up, and every
            // decision in that column was dropped. No count, no message, and the only way back
            // was noticing in time to press undo.
            var was = new Dictionary<MatrixLine, MatrixLine>();

            for (var at = 0; at < wasColumns.Count && at < _columns.Count; at++)
                was[wasColumns[at]] = _columns[at];

            foreach (var cell in kept)
            {
                var column = _columns.FirstOrDefault(
                    c => string.Equals(c.Name, cell.Key.Item2.Name, StringComparison.OrdinalIgnoreCase))
                    ?? (was.TryGetValue(cell.Key.Item2, out var moved) ? moved : null);

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

            if (!UpperIsLive) json.Set("lower", true);
            if (!Paired) json.Set("apart", true);

            // Only the folders somebody has actually coloured, so a board that has never used
            // one writes nothing and reads back the same as it always did.
            if (_folderColours.Count > 0)
                json.Set("folders", JsonValue.Array(_folderColours.Select(one =>
                    JsonValue.Object()
                        .Set("name", one.Key)
                        .Set("colour", one.Value))));

            return json;
        }

        /// <summary>Read one back, or an empty board.</summary>
        /// <param name="json">The stored board.</param>
        public static MatrixBoard FromJson(JsonValue? json)
        {
            var board = new MatrixBoard();

            if (json == null || json.Kind != JsonKind.Object) return board;

            board.UpperIsLive = !json["lower"].AsBool(false);
            board.Paired = !json["apart"].AsBool(false);

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

            foreach (var held in json["folders"].Items)
                board.Paint(held["name"].AsString(), held["colour"].AsString());

            // A board saved with a folder that has since lost every member would otherwise carry
            // its colour for ever, and hand it to the next folder given that name.
            board.Swept();

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
