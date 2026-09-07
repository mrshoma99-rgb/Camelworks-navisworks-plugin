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
        /// The line's identity, as distinct from its name.
        ///
        /// A row and the column standing for it must be recognised as one discipline: that is what
        /// mirrors the grid, defines the diagonal and stops a pair being asked twice. Identity was
        /// previously taken from the name, which fails as soon as several lines share one. Six rows
        /// added and not yet named are all called "New category", so each is treated as the same
        /// line as the others, every pair among them reads as a pair with itself, the whole block
        /// counts as diagonal, and the grid holds nothing.
        ///
        /// The row and column of a discipline are created together and carry this identity, so it
        /// survives being unnamed, renamed, or named the same as a neighbour.
        ///
        /// Empty on a line read from a board saved before this field existed; those fall back to
        /// the older test by source and name.
        /// </summary>
        public string Key { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>How it reads.</summary>
        public string Name { get; set; }

        /// <summary>Its number, or empty.</summary>
        public string Number { get; set; }

        /// <summary>Its discipline code — "ST", "ME" — or empty. A sheet often carries one.</summary>
        public string Code { get; set; }

        /// <summary>
        /// The folder it is filed in, or empty for a loose line.
        ///
        /// A folder is a name several lines share rather than an object they hang off. Twenty
        /// disciplines make a readable matrix; sixty need grouping into packages. Held as a name on
        /// the line, a folder cannot go stale against its members, cannot be emptied independently
        /// of them, and survives every reorder, save and undo the lines survive. The board keeps
        /// the members of one folder adjacent; the folder itself is the set of lines answering to
        /// its name.
        /// </summary>
        public string Folder { get; set; } = string.Empty;

        /// <summary>Where its elements come from.</summary>
        public MatrixSource Source { get; set; }

        /// <summary>
        /// What this line holds under the scheme's custom fields, by field key.
        ///
        /// The four built-in fields are not in here: they keep their own properties, because the
        /// headers, the diff, the naming and the export all read <see cref="Name"/> and
        /// <see cref="Number"/> directly and a lookup in their place would be a change to every
        /// one of them. <see cref="Value"/> hides the difference, so a caller working from a field
        /// key never has to know which of the two a key lands in.
        /// </summary>
        public IDictionary<string, string> Values { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The fields this line holds by hand, by key, and so keeps against the scheme.
        ///
        /// Per field rather than per line. A discipline whose number was set deliberately still
        /// wants its code counted with the rest, and a line pinned whole would quietly fall out of
        /// every series it was in.
        ///
        /// <see cref="IsCustom"/> is what a header draws a mark from, since a line that has left
        /// the scheme in any respect is one somebody should be able to see without opening it.
        /// </summary>
        public ISet<string> Pinned { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether any field on this line is held by hand.</summary>
        public bool IsCustom => Pinned.Count > 0;

        /// <summary>What this line holds under one field, whichever kind of field it is.</summary>
        /// <param name="key">The field's key.</param>
        public string Value(string? key)
        {
            var said = (key ?? string.Empty).Trim();

            if (said.Length == 0) return string.Empty;

            if (string.Equals(said, MatrixField.NameKey, StringComparison.OrdinalIgnoreCase))
                return Name ?? string.Empty;

            if (string.Equals(said, MatrixField.NumberKey, StringComparison.OrdinalIgnoreCase))
                return Number ?? string.Empty;

            if (string.Equals(said, MatrixField.CodeKey, StringComparison.OrdinalIgnoreCase))
                return Code ?? string.Empty;

            if (string.Equals(said, MatrixField.FolderKey, StringComparison.OrdinalIgnoreCase))
                return Folder ?? string.Empty;

            return Values.TryGetValue(said, out var held) ? held : string.Empty;
        }

        /// <summary>Put a value under one field, whichever kind of field it is.</summary>
        /// <param name="key">The field's key.</param>
        /// <param name="value">What it should hold. Empty removes a custom field's entry.</param>
        public void Set(string? key, string? value)
        {
            var said = (key ?? string.Empty).Trim();

            if (said.Length == 0) return;

            var held = (value ?? string.Empty).Trim();

            if (string.Equals(said, MatrixField.NameKey, StringComparison.OrdinalIgnoreCase))
            {
                // A line with no name at all cannot be drawn or matched, so an empty name is
                // refused here rather than producing a header nobody can read.
                if (held.Length > 0) Name = held;

                return;
            }

            if (string.Equals(said, MatrixField.NumberKey, StringComparison.OrdinalIgnoreCase))
            {
                Number = held;
                return;
            }

            if (string.Equals(said, MatrixField.CodeKey, StringComparison.OrdinalIgnoreCase))
            {
                Code = held;
                return;
            }

            if (string.Equals(said, MatrixField.FolderKey, StringComparison.OrdinalIgnoreCase))
            {
                Folder = held;
                return;
            }

            if (held.Length == 0) Values.Remove(said);
            else Values[said] = held;
        }

        /// <summary>Hold a field by hand, taking it out of the scheme for this line.</summary>
        /// <param name="key">The field's key.</param>
        /// <param name="value">What it should hold.</param>
        public void Pin(string? key, string? value)
        {
            var said = (key ?? string.Empty).Trim();

            if (said.Length == 0) return;

            Set(said, value);
            Pinned.Add(said);
        }

        /// <summary>Give a field back to the scheme. Its value is rewritten on the next apply.</summary>
        /// <param name="key">The field's key, or null for every field this line pinned.</param>
        public void Unpin(string? key = null)
        {
            if (key == null) Pinned.Clear();
            else Pinned.Remove(key.Trim());
        }

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

            // Written only when there is something to write, so a board using none of this saves
            // the bytes it always did.
            if (Values.Count > 0)
            {
                var held = JsonValue.Object();

                foreach (var one in Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
                    held.Set(one.Key, one.Value);

                json.Set("values", held);
            }

            if (Pinned.Count > 0)
                json.Set("pinned", JsonValue.Array(
                    Pinned.OrderBy(one => one, StringComparer.OrdinalIgnoreCase)
                          .Select(JsonValue.String)));

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

            var line = new MatrixLine(name, json["number"].AsString(),
                                      MatrixSource.FromJson(json["source"]),
                                      json["code"].AsString())
            {
                Folder = (json["folder"].AsString() ?? string.Empty).Trim(),
                Key = (json["key"].AsString() ?? string.Empty).Trim(),
            };

            var values = json["values"];

            if (values.Kind == JsonKind.Object)
                foreach (var one in values.Keys)
                    line.Set(one, values[one].AsString());

            foreach (var one in json["pinned"].Items)
            {
                var said = (one.AsString() ?? string.Empty).Trim();

                if (said.Length > 0) line.Pinned.Add(said);
            }

            return line;
        }

        /// <inheritdoc />
        public override string ToString() => Titled;
    }

    /// <summary>
    /// The clash matrix: rows down one side, columns across the top, a symbol where they meet.
    ///
    /// Rows and columns are two separate lists. The square, mirrored grid drawn at a coordination
    /// kick-off is the case where both lists hold the same items, and it is the common case, but it
    /// is not the only useful shape. A row is side A of a test and a column is side B, which allows
    /// four packages down the side and every discipline across the top without inventing categories
    /// that mean nothing on the other axis.
    ///
    /// Consequently nothing is mirrored: cell (r,c) is stored where it is put. Where both lists do
    /// hold the same item, the pair (Ducts, Steel) and the pair (Steel, Ducts) are two cells naming
    /// one test, and <see cref="MatrixPlan"/> is what detects that. Storing one half and reflecting
    /// it would make the grid assume a shape only the author knows.
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

        // Kept apart from the symbol rather than folded into it. A cell says two independent
        // things - whether these two are checked and how, and how much anybody cares about the
        // answer - and a grid that encoded both in one mark would need a legend entry per
        // combination: twelve entries to say four things about three tolerances.
        private readonly Dictionary<(MatrixLine Row, MatrixLine Column), int> _ranks =
            new Dictionary<(MatrixLine, MatrixLine), int>();

        // The standing instructions, and which cells they wrote. Kept apart from the cells
        // themselves rather than inferred by re-running the rules, because what a rule would
        // write now and what actually wrote a cell are different questions: a rule that has
        // since been edited must take back the cells it wrote, not the cells it would write.
        private readonly List<MatrixRule> _rules = new List<MatrixRule>();

        private readonly HashSet<(MatrixLine Row, MatrixLine Column)> _byRule =
            new HashSet<(MatrixLine, MatrixLine)>();

        /// <summary>The rows, which are side A.</summary>
        public IReadOnlyList<MatrixLine> Rows => _rows;

        /// <summary>The columns, which are side B.</summary>
        public IReadOnlyList<MatrixLine> Columns => _columns;

        /// <summary>
        /// The mark for a pair deliberately ruled out.
        ///
        /// Blank previously carried two meanings: "not yet decided" and "agreed at the kick-off as
        /// never checked". Around forty per cent of a real grid is empty, so a coordinator
        /// inheriting one cannot tell an agreement from an omission, and the count of undecided
        /// pairs never reaches zero.
        ///
        /// Drawn as an en dash, the conventional mark on a paper matrix. It is reserved rather than
        /// a legend entry: a legend symbol can be given a tolerance and switched on, which would
        /// turn every agreed-not-required pair into a test.
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
        /// A clash matrix is normally a list of disciplines against itself. Structure against
        /// services is the same agreement as services against structure, which is why the grid
        /// mirrors about its diagonal and half of it is unused. Two axes that can be edited
        /// separately are a different thing: two lists that happen to overlap, where the mirroring,
        /// the unused half and the diagonal are coincidences of what was typed twice.
        ///
        /// So with this on, adding a category adds it to both axes, removing one removes it from
        /// both, and naming, numbering, coding and colour carry across.
        ///
        /// It can be turned off, because a matrix of one trade's sets against another's is a real
        /// case. Off is a deliberate exception rather than the default.
        /// </summary>
        public bool Paired { get; set; } = true;

        /// <summary>
        /// The fields every line carries, and how they are filled in.
        ///
        /// One scheme for both axes, because a square matrix is one list mirrored and two schemes
        /// would have to be kept in step by hand.
        /// </summary>
        public MatrixScheme Scheme { get; private set; } = new MatrixScheme();

        /// <summary>
        /// Write the scheme's filled fields across both axes.
        ///
        /// Called after anything that changes what a series would produce: adding a line, moving
        /// one, filing it in a folder, or editing a field. The two axes are walked separately, so
        /// the rows number from the top and the columns from the left rather than continuing one
        /// count through both.
        /// </summary>
        /// <returns>How many values were written.</returns>
        public int ApplyScheme() => Scheme.Apply(_rows) + Scheme.Apply(_columns);

        /// <summary>
        /// Put a value under one field, and put it on the line's twin as well.
        ///
        /// Setting a field is the same shape of act as renaming or pointing a line, and mirrors
        /// for the same reason: on a paired board a row and its column are one discipline said
        /// twice, so a phase typed against the row and not the column would leave the two halves
        /// of the same thing disagreeing, and a pattern reading <c>{rowPhase}</c> against
        /// <c>{columnPhase}</c> would produce a name half filled in.
        ///
        /// A twin that has pinned the field is left alone: it has been taken out of the scheme on
        /// purpose, and mirroring over it would undo a decision.
        /// </summary>
        /// <param name="line">The row or column being written to.</param>
        /// <param name="key">The field's key.</param>
        /// <param name="value">What it should hold.</param>
        /// <param name="byHand">True to pin it, holding it against the scheme.</param>
        /// <returns>The twin that took it too, or null when there was none.</returns>
        public MatrixLine? Write(MatrixLine? line, string? key, string? value, bool byHand = false)
        {
            if (line == null) return null;

            var said = (key ?? string.Empty).Trim();

            if (said.Length == 0) return null;

            // Found before the change: a name is one of the things that makes two lines twins,
            // so writing the name first would lose the twin it was meant to carry to.
            var twin = TwinOf(line);

            if (byHand) line.Pin(said, value);
            else line.Set(said, value);

            if (twin == null || twin.Pinned.Contains(said)) return null;

            if (byHand) twin.Pin(said, value);
            else twin.Set(said, value);

            return twin;
        }

        /// <summary>
        /// Give a field back to the scheme on a line and its twin, and rewrite what it holds.
        /// </summary>
        /// <param name="line">The row or column.</param>
        /// <param name="key">The field's key, or null for every field the line pinned.</param>
        public void Release(MatrixLine? line, string? key = null)
        {
            if (line == null) return;

            var twin = TwinOf(line);

            line.Unpin(key);
            twin?.Unpin(key);

            ApplyScheme();
        }

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
        /// Folder colours, by folder name; empty means the theme's own colour.
        ///
        /// A folder is a name its members share and nothing more, so there is no folder object to
        /// hold a colour. Filing a line writes a string on it, and a folder exists for as long as
        /// some line carries its name. The colour therefore lives on the board, keyed by that name,
        /// and any operation that changes a folder name must carry the colour with it.
        /// </summary>
        private readonly Dictionary<string, string> _folderColours =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The colour a folder is drawn in, or empty when none has been chosen.
        ///
        /// Colour is what makes a band of eight rows read as one package. Twenty-two disciplines in
        /// five packages read as twenty-two disciplines unless the packages are distinguished, and
        /// the band down the side is the only place with room to show that without repeating the
        /// package name on every row.
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
        /// Filing also moves the line. A folder is only legible while its members are adjacent, so a
        /// line dropped into a folder goes to the end of that folder's block rather than staying
        /// where it was. A line taken out of a folder is placed after the block it left.
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
        /// Remove a folder and leave its lines in place.
        ///
        /// Dissolving is not deleting. A folder holds nothing of its own — no source, no symbol, no
        /// decision — so removing it cannot lose a decision, and the menu keeps the two commands
        /// distinct.
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
        /// A clash matrix is mirrored about its diagonal, because A against B is the same test as B
        /// against A. Where both axes carry the same two items, writing one cell writes the other:
        /// the grid cannot hold two different answers for one pair, and filling in one half fills
        /// the other.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <param name="symbol">The symbol, or null to clear.</param>
        public void Set(MatrixLine? row, MatrixLine? column, string? symbol)
        {
            if (row == null || column == null) return;

            Write(row, column, symbol);

            // A hand edit takes the cell from the rule. Overriding a rule-filled cell is a
            // decision, and a later re-application must not put the rule's answer back over it, so
            // the cell stops being the rule's as soon as it is written by hand.
            _byRule.Remove((row, column));

            var mirror = MirrorOf(row, column);

            if (mirror != null)
            {
                Write(mirror.Value.Row, mirror.Value.Column, symbol);
                _byRule.Remove((mirror.Value.Row, mirror.Value.Column));
            }
        }

        /// <summary>The standing instructions, in the order they are read.</summary>
        public IReadOnlyList<MatrixRule> Rules => _rules;

        /// <summary>Add one, at the end, where it wins over the ones above it.</summary>
        /// <param name="rule">The rule.</param>
        public void AddRule(MatrixRule? rule)
        {
            if (rule == null || rule.IsEmpty) return;

            _rules.Add(rule);
        }

        /// <summary>Take one away.</summary>
        /// <param name="at">Where it sits in the list.</param>
        public void RemoveRule(int at)
        {
            if (at < 0 || at >= _rules.Count) return;

            _rules.RemoveAt(at);
        }

        /// <summary>Move one up or down the list, which is what changes who wins.</summary>
        /// <param name="from">Where it is.</param>
        /// <param name="to">Where it goes.</param>
        public void MoveRule(int from, int to)
        {
            if (from < 0 || from >= _rules.Count) return;

            var wanted = to < 0 ? 0 : to >= _rules.Count ? _rules.Count - 1 : to;

            if (wanted == from) return;

            var rule = _rules[from];

            _rules.RemoveAt(from);
            _rules.Insert(wanted, rule);
        }

        /// <summary>Whether a cell was written by a rule rather than by hand.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public bool IsByRule(MatrixLine? row, MatrixLine? column) =>
            row != null && column != null && _byRule.Contains((row, column));

        /// <summary>
        /// Which rule owns a cell, or null when no rule wrote it.
        ///
        /// Answered by reading the rules rather than by remembering which one it was, so a cell
        /// always names the rule that would write it now - which is the one a reader asking
        /// "why is this here" needs to be sent to.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public MatrixRule? RuleOn(MatrixLine? row, MatrixLine? column)
        {
            if (!IsByRule(row, column)) return null;

            for (var at = _rules.Count - 1; at >= 0; at--)
                if (_rules[at].Covers(row!.Folder, column!.Folder))
                    return _rules[at];

            return null;
        }

        /// <summary>
        /// Apply every rule over the board again, and release the cells no rule now covers.
        ///
        /// The last matching rule wins. Rules are written general first and exception after —
        /// "everything against everything is a hard clash, except services against architecture,
        /// which is a clearance" — so the later line is the more specific one and reading from the
        /// bottom matches the intent.
        ///
        /// Cells written by hand are untouched in either direction: a rule does not overwrite one,
        /// and dropping a rule does not clear one.
        /// </summary>
        /// <returns>How many cells the rules changed.</returns>
        public int ApplyRules()
        {
            var changed = 0;

            foreach (var row in _rows)
            foreach (var column in _columns)
            {
                if (IsSelfPair(row, column)) continue;
                if (!IsKeptHalf(row, column)) continue;

                var hand = At(row, column) != null && !IsByRule(row, column);

                if (hand) continue;

                var wanted = Wanted(row, column);

                if (string.Equals(At(row, column), wanted, StringComparison.Ordinal)) continue;

                Ruled(row, column, wanted);
                changed++;
            }

            return changed;
        }

        /// <summary>What the rules say about a pair, or null when none covers it.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public string? Wanted(MatrixLine? row, MatrixLine? column)
        {
            if (row == null || column == null) return null;

            for (var at = _rules.Count - 1; at >= 0; at--)
                if (_rules[at].Covers(row.Folder, column.Folder))
                    return _rules[at].Symbol;

            return null;
        }

        private void Ruled(MatrixLine row, MatrixLine column, string? symbol)
        {
            Write(row, column, symbol);
            Marked(row, column, symbol != null);

            var mirror = MirrorOf(row, column);

            if (mirror == null) return;

            Write(mirror.Value.Row, mirror.Value.Column, symbol);
            Marked(mirror.Value.Row, mirror.Value.Column, symbol != null);
        }

        private void Marked(MatrixLine row, MatrixLine column, bool owned)
        {
            if (owned) _byRule.Add((row, column));
            else _byRule.Remove((row, column));
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
        /// Which triangle of a mirrored matrix is the one worked in.
        ///
        /// Every drawing of a clash matrix keeps one triangle, but which one is a house convention;
        /// many practices fill in the lower triangle. Forcing the other one makes the user's own
        /// matrix read as inverted.
        ///
        /// This affects what is drawn and nothing else: a pair is written to both cells whichever
        /// half it is entered in, so the tests produced are the same either way.
        /// </summary>
        public bool UpperIsLive { get; set; } = true;

        /// <summary>
        /// Which storey this board's tests are about, for the <c>{level}</c> naming token.
        ///
        /// One value for the whole board rather than one per line, because a matrix is a grid of
        /// disciplines and a level cuts across all of them: a coordinator builds "L03 - Mech v
        /// Struct" for level 3 and then the same grid again for level 4. Empty when the board is
        /// not per-storey, and the token then fills as nothing.
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// Whether this cell is the one of a mirrored pair that the matrix keeps.
        ///
        /// Always the earlier of the two, whichever half is drawn. This is what walks the cells,
        /// what the planner counts and what the test names are built from, so it cannot follow a
        /// drawing preference: keeping the pair the other way round would name every test "Ducts vs
        /// Steel" where it previously said "Steel vs Ducts", and the difference against the document
        /// would read as a rename of every test.
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
        /// A line with no source yet is still the discipline it is named after. This previously
        /// held that if either side had a source, the two matched only when their sources matched.
        /// That is correct for two pointed lines and wrong midway through building a matrix: with
        /// twenty names typed, squared off and then pointed one row at a time, each row pointed
        /// stopped matching its own column. The pair stopped being mirrored, so both of its cells
        /// became live and the grid grew from a triangle of 210 pairs to a square of 400, with the
        /// unused half drawn as decisions nobody had made — and nothing built, because the planner
        /// drops a pair whose column points at nothing.
        ///
        /// So an empty source is not a different source; it means no answer yet, and the name is
        /// what remains to compare. Two lines that both point somewhere match when they point at the
        /// same thing, whatever they are called, which is what lets the two axes diverge on purpose.
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
        /// Point a line at a source, and point its twin on the other axis with it.
        ///
        /// A pair with one side pointed at nothing builds no test. Saying what a row is and saying
        /// what its column is are the same decision, so the second is not asked for separately. A
        /// twin that already points somewhere is left alone, because the two axes are allowed to
        /// diverge deliberately.
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
        /// Rename a line, and rename the line standing for it on the other axis with it.
        ///
        /// Until a line has a source, its name is what makes it the twin of the line on the other
        /// axis. On a matrix typed out by hand, correcting a typo in a row therefore separated it
        /// from its own column: the pair stopped being mirrored, both cells went live, and the
        /// triangle became a square for that discipline. Same failure as pointing a row, and
        /// renaming is the more common operation.
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
        /// A matrix is usually filled exceptions first and blanket last. The blanket fill
        /// previously had only two sizes — one line, or the whole grid over everything already
        /// decided — so the blanket step meant one gutter click per row.
        ///
        /// Empty cells only, so it cannot overwrite a decision, and never the diagonal, which no
        /// bulk fill writes.
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
        /// The block gesture is a rectangle over the order the two axes are in, so "every MEP row
        /// against every structural column" is a single rectangle only while the disciplines sit in
        /// the same order down the side and across the top. Moving the axes separately means
        /// repeating the same reorder twice. A square matrix has one order, and this keeps it.
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

        /// <summary>
        /// What the host itself calls a clash test's priority: nought is not set, then high,
        /// medium, low.
        ///
        /// The numbers are the host's, not ours. Navisworks writes a test's priority as one of
        /// these four and shows it as a column in its own results, so a matrix that invented a
        /// five-point scale would be a scale that could never be pushed into the document or
        /// read back out of it.
        /// </summary>
        public const int NotSet = 0;

        /// <summary>Highest of the three the host has.</summary>
        public const int High = 1;

        /// <summary>The middle one.</summary>
        public const int Medium = 2;

        /// <summary>The lowest.</summary>
        public const int Low = 3;

        /// <summary>What a rank is called, for a tip or a legend.</summary>
        /// <param name="rank">The rank.</param>
        public static string RankNamed(int rank) =>
            rank == High ? "High" : rank == Medium ? "Medium" : rank == Low ? "Low" : "Not set";

        /// <summary>How urgent this pair is, or <see cref="NotSet"/>.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public int RankAt(MatrixLine? row, MatrixLine? column) =>
            row != null && column != null && _ranks.TryGetValue((row, column), out var rank)
                ? rank
                : NotSet;

        /// <summary>
        /// Say how urgent a pair is, on both cells of a mirrored pair.
        ///
        /// Mirrored the same way a symbol is, and for the same reason: one pair cannot be two
        /// different amounts of urgent depending on which triangle somebody was handed.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <param name="rank">One of the four, anything else being taken as not set.</param>
        public void Rank(MatrixLine? row, MatrixLine? column, int rank)
        {
            if (row == null || column == null) return;

            Ranked(row, column, rank);

            var mirror = MirrorOf(row, column);

            if (mirror != null) Ranked(mirror.Value.Row, mirror.Value.Column, rank);
        }

        /// <summary>How many cells anybody has ranked.</summary>
        public int RankedCells => _ranks.Count;

        /// <summary>Every ranked pair, each once, in reading order.</summary>
        public IEnumerable<(MatrixLine Row, MatrixLine Column, int Rank)> Ranks()
        {
            foreach (var row in _rows)
                foreach (var column in _columns)
                {
                    if (!IsKeptHalf(row, column)) continue;

                    var rank = RankAt(row, column);

                    if (rank != NotSet) yield return (row, column, rank);
                }
        }

        private void Ranked(MatrixLine row, MatrixLine column, int rank)
        {
            if (rank != High && rank != Medium && rank != Low) _ranks.Remove((row, column));
            else _ranks[(row, column)] = rank;
        }

        private void Write(MatrixLine row, MatrixLine column, string? symbol)
        {
            var text = symbol == null ? string.Empty : symbol.Trim();

            if (text.Length == 0)
            {
                _cells.Remove((row, column));

                // A rank on an empty cell is a priority for a test that will never be built,
                // and it would come back the moment somebody put a symbol there again - saying
                // something about the new pair that nobody said.
                _ranks.Remove((row, column));
            }
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
        /// The row and the column on the diagonal are two objects standing for one discipline: a
        /// square matrix is built by copying the rows into the columns. What makes them the same
        /// pair is what they point at, or, until that is set, what they are called.
        ///
        /// This previously answered "no" whenever either side had no source, which is every cell of
        /// a matrix whose names are still being typed. On a grid squared off but not yet pointed,
        /// the diagonal was therefore not treated as one: bulk fills wrote it and the spine was
        /// drawn as ordinary cells.
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
            var keptRanks = new Dictionary<(MatrixLine, MatrixLine), int>(_ranks);
            var keptRuled = new HashSet<(MatrixLine, MatrixLine)>(_byRule);
            var wasColumns = new List<MatrixLine>(_columns);

            _columns.Clear();

            foreach (var row in _rows)
                _columns.Add(new MatrixLine(row.Name, row.Number, row.Source, row.Code)
                {
                    Folder = row.Folder,
                    Key = row.Key,
                });

            _cells.Clear();

            // A cell's contents survive when both its ends still exist. Matched by name first, so
            // squaring off a grid that was already square keeps every symbol; then, where the name
            // has changed, by the old column's position.
            //
            // The fallback previously looked the old column up in a table keyed on rows, so it
            // never resolved: renaming one row and then squaring off dropped every decision in that
            // column, with no count and no message.
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

            // Whose cell it is follows the cell. A pair that survived squaring off is still the
            // rule's if it was the rule's, and still somebody's decision if it was that.
            _byRule.Clear();

            foreach (var ruled in keptRuled)
            {
                var column = _columns.FirstOrDefault(
                    c => string.Equals(c.Name, ruled.Item2.Name, StringComparison.OrdinalIgnoreCase))
                    ?? (was.TryGetValue(ruled.Item2, out var moved) ? moved : null);

                if (_rows.Contains(ruled.Item1) && column != null)
                    _byRule.Add((ruled.Item1, column));
            }

            // The ranks follow their cells. A priority is about a pair, so a pair that survived
            // squaring off keeps how urgent somebody said it was; one whose column went takes
            // its rank with it rather than leaving it keyed to a line that is no longer here.
            _ranks.Clear();

            foreach (var rank in keptRanks)
            {
                var column = _columns.FirstOrDefault(
                    c => string.Equals(c.Name, rank.Key.Item2.Name, StringComparison.OrdinalIgnoreCase))
                    ?? (was.TryGetValue(rank.Key.Item2, out var moved) ? moved : null);

                if (_rows.Contains(rank.Key.Item1) && column != null)
                    _ranks[(rank.Key.Item1, column)] = rank.Value;
            }
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            if (Level.Length > 0) json.Set("level", Level);

            json.Set("rows", JsonValue.Array(_rows.Select(r => r.ToJson())));
            json.Set("columns", JsonValue.Array(_columns.Select(c => c.ToJson())));

            var fields = Scheme.ToJson();

            if (fields != null) json.Set("fields", fields);

            // The rule flag rides on the cell rather than in a list of its own, because it is a
            // fact about that cell - and a board reloaded without it would turn every
            // rule-filled cell into a decision somebody made by hand, which no later edit to the
            // rule could ever take back.
            var cells = JsonValue.Array(Cells().Select(cell =>
            {
                var held = JsonValue.Object()
                    .Set("row", (double)_rows.IndexOf(cell.Row))
                    .Set("column", (double)_columns.IndexOf(cell.Column))
                    .Set("symbol", cell.Symbol);

                if (IsByRule(cell.Row, cell.Column)) held.Set("rule", true);

                return held;
            }));

            json.Set("cells", cells);

            // Only written when somebody has ranked something, so every board saved before the
            // priority view existed reads back byte for byte as it did.
            // Only when there are any, so a board that has never used one reads back as it did.
            if (_rules.Count > 0)
                json.Set("rules", JsonValue.Array(_rules.Select(one => one.ToJson())));

            if (_ranks.Count > 0)
                json.Set("ranks", JsonValue.Array(Ranks().Select(one =>
                    JsonValue.Object()
                        .Set("row", (double)_rows.IndexOf(one.Row))
                        .Set("column", (double)_columns.IndexOf(one.Column))
                        .Set("rank", (double)one.Rank))));

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
            board.Scheme = MatrixScheme.FromJson(json["fields"]);
            board.Level = json["level"].AsString() ?? string.Empty;

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

                if (held["rule"].AsBool(false))
                    board.Marked(board._rows[row], board._columns[column], true);
            }

            foreach (var held in json["rules"].Items)
                board.AddRule(MatrixRule.FromJson(held));

            foreach (var held in json["ranks"].Items)
            {
                var row = (int)held["row"].AsDouble();
                var column = (int)held["column"].AsDouble();

                if (row < 0 || row >= board._rows.Count) continue;
                if (column < 0 || column >= board._columns.Count) continue;

                board.Rank(board._rows[row], board._columns[column], (int)held["rank"].AsDouble());
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

            foreach (var key in _ranks.Keys.Where(k => k.Row == line || k.Column == line).ToList())
                _ranks.Remove(key);

            foreach (var key in _byRule.Where(k => k.Row == line || k.Column == line).ToList())
                _byRule.Remove(key);
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
