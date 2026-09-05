using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace CamelWorks.Core.Data
{
    /// <summary>One worksheet, as a rectangle of text.</summary>
    public sealed class SheetTable
    {
        internal SheetTable(string name, IReadOnlyList<IReadOnlyList<string?>> rows)
        {
            Name = name;
            Rows = rows;
        }

        /// <summary>The sheet's name, as the tab shows it.</summary>
        public string Name { get; }

        /// <summary>Every row, top to bottom. Ragged: a row stops at its last filled cell.</summary>
        public IReadOnlyList<IReadOnlyList<string?>> Rows { get; }

        /// <summary>How many columns the widest row has.</summary>
        public int Columns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);

        /// <summary>True when nothing was in it.</summary>
        public bool IsEmpty => Rows.Count == 0;

        /// <summary>The first row, as headings. Empty when the sheet is.</summary>
        public IReadOnlyList<string?> Headers =>
            Rows.Count == 0 ? Array.Empty<string?>() : Rows[0];

        /// <summary>
        /// One cell, or null when the row is short or the sheet is smaller than that.
        ///
        /// Out-of-range is null rather than an exception. Every spreadsheet anybody has ever been
        /// sent is ragged somewhere, and a reader that threw on the first short row would refuse
        /// most real files.
        /// </summary>
        /// <param name="row">Zero-based row.</param>
        /// <param name="column">Zero-based column.</param>
        public string? Cell(int row, int column) =>
            row < 0 || row >= Rows.Count || column < 0 || column >= Rows[row].Count
                ? null
                : Rows[row][column];

        /// <summary>
        /// Which column a heading is in, or -1.
        ///
        /// Case- and whitespace-insensitive, because a sheet that has been round-tripped through
        /// three people has a trailing space in a heading somewhere.
        /// </summary>
        /// <param name="heading">The heading to look for.</param>
        public int ColumnOf(string? heading)
        {
            if (string.IsNullOrWhiteSpace(heading)) return -1;

            var wanted = heading!.Trim();

            for (var c = 0; c < Headers.Count; c++)
                if (string.Equals((Headers[c] ?? string.Empty).Trim(), wanted,
                                  StringComparison.OrdinalIgnoreCase))
                    return c;

            return -1;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " (" + Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows x "
            + Columns.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>What was in a workbook, and what could not be read.</summary>
    public sealed class WorkbookTables
    {
        internal WorkbookTables(IReadOnlyList<SheetTable> sheets, string? problem = null)
        {
            Sheets = sheets;
            Problem = problem;
        }

        /// <summary>Every worksheet, in workbook order.</summary>
        public IReadOnlyList<SheetTable> Sheets { get; }

        /// <summary>Why nothing could be read, or null when it could.</summary>
        public string? Problem { get; }

        /// <summary>True when the file could be opened and had at least one sheet.</summary>
        public bool IsUsable => Problem == null && Sheets.Count > 0;

        /// <summary>One sheet by name, or null.</summary>
        /// <param name="name">The tab's name.</param>
        public SheetTable? Sheet(string? name) =>
            name == null ? null
                : Sheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The first sheet, or null when there is none.</summary>
        public SheetTable? First => Sheets.Count > 0 ? Sheets[0] : null;

        /// <inheritdoc />
        public override string ToString() =>
            Problem ?? Sheets.Count.ToString(CultureInfo.InvariantCulture) + " sheets";
    }

    /// <summary>
    /// Reads what Excel writes, well enough to take a schedule back.
    ///
    /// The other half of the round trip. Writing a spreadsheet is a courtesy; reading one back is the
    /// feature, because the reason a project sends properties to a subcontractor is that somebody
    /// there will fill in a column, and a tool that cannot take it back has moved the retyping rather
    /// than removed it.
    ///
    /// A text reader, deliberately. Every cell comes back as the string it appears as, and numbers
    /// keep their stored form rather than being parsed and reformatted. A property write is a
    /// string, and a round trip through <c>double</c> is how "0450" becomes "450" and a door code
    /// stops matching.
    ///
    /// It handles what Excel emits: a shared-string table, inline strings, formula results, booleans,
    /// errors, sparse rows and out-of-order cells. It does not evaluate formulas; it takes the cached
    /// result Excel stored, which is what the person saw.
    /// </summary>
    public static class XlsxReader
    {
        private static readonly XNamespace Main =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private static readonly XNamespace Rel =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static readonly XNamespace PackageRel =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>How many rows one sheet is read before the rest are left.</summary>
        public const int RowCap = 200_000;

        /// <summary>Read a workbook.</summary>
        /// <param name="bytes">The file. Null or empty is a readable answer, not an error.</param>
        public static WorkbookTables Read(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return new WorkbookTables(Array.Empty<SheetTable>(), "the file is empty");

            using (var stream = new MemoryStream(bytes, writable: false))
                return Read(stream);
        }

        /// <summary>Read a workbook.</summary>
        /// <param name="input">The file. Left open.</param>
        public static WorkbookTables Read(Stream? input)
        {
            if (input == null) return new WorkbookTables(Array.Empty<SheetTable>(), "there is no file");

            try
            {
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
                {
                    var workbook = Find(zip, "xl/workbook.xml");

                    if (workbook == null)
                        return new WorkbookTables(Array.Empty<SheetTable>(),
                            "this is not a spreadsheet - it has no xl/workbook.xml inside it");

                    var strings = SharedStrings(zip);
                    var targets = Rels(zip);

                    var sheets = new List<SheetTable>();

                    var declared = Parse(workbook)?.Element(Main + "sheets")?.Elements(Main + "sheet")
                                   ?? Enumerable.Empty<XElement>();

                    foreach (var sheet in declared)
                    {
                        var name = (string?)sheet.Attribute("name") ?? "Sheet";
                        var id = (string?)sheet.Attribute(Rel + "id");

                        var path = id != null && targets.TryGetValue(id, out var target)
                            ? target
                            : null;

                        var entry = path == null ? null : Find(zip, path);

                        // A workbook can name a sheet whose part is missing. An empty sheet under
                        // the right name is a better answer than dropping the tab silently.
                        sheets.Add(entry == null
                            ? new SheetTable(name, Array.Empty<IReadOnlyList<string?>>())
                            : new SheetTable(name, Cells(entry, strings)));
                    }

                    return new WorkbookTables(sheets);
                }
            }
            catch (InvalidDataException)
            {
                return new WorkbookTables(Array.Empty<SheetTable>(),
                    "this file is not a readable .xlsx - it may be an older .xls, or a CSV renamed");
            }
            catch (Exception error)
            {
                return new WorkbookTables(Array.Empty<SheetTable>(), "could not read it: " + error.Message);
            }
        }

        /// <summary>
        /// Turn a column reference into a zero-based index: A is 0, Z is 25, AA is 26.
        ///
        /// Public because the reader is not the only thing that needs it — anything reporting
        /// "column D of the sheet you sent back" has to speak the same alphabet.
        /// </summary>
        /// <param name="reference">A cell reference such as <c>AB12</c>, or a bare column such as <c>AB</c>.</param>
        public static int ColumnIndex(string? reference)
        {
            if (string.IsNullOrEmpty(reference)) return -1;

            var index = 0;
            var any = false;

            foreach (var c in reference!)
            {
                if (c >= 'A' && c <= 'Z') index = (index * 26) + (c - 'A' + 1);
                else if (c >= 'a' && c <= 'z') index = (index * 26) + (c - 'a' + 1);
                else break;

                any = true;
            }

            return any ? index - 1 : -1;
        }

        /// <summary>The column letters for a zero-based index. The inverse of <see cref="ColumnIndex"/>.</summary>
        /// <param name="index">Zero-based column.</param>
        public static string ColumnName(int index)
        {
            if (index < 0) return string.Empty;

            var name = string.Empty;
            var n = index + 1;

            while (n > 0)
            {
                var rest = (n - 1) % 26;
                name = (char)('A' + rest) + name;
                n = (n - 1) / 26;
            }

            return name;
        }

        private static ZipArchiveEntry? Find(ZipArchive zip, string path) =>
            zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.TrimStart('/'), path, StringComparison.OrdinalIgnoreCase));

        private static XElement? Parse(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            {
                try
                {
                    return XElement.Load(stream, LoadOptions.None);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Relationship id to part path, so a sheet can be found where the workbook says it is.</summary>
        private static Dictionary<string, string> Rels(ZipArchive zip)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            var entry = Find(zip, "xl/_rels/workbook.xml.rels");
            if (entry == null) return map;

            var root = Parse(entry);
            if (root == null) return map;

            foreach (var rel in root.Elements(PackageRel + "Relationship"))
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");

                if (id == null || target == null) continue;

                // Targets are relative to xl/ unless they are absolute in the package.
                map[id] = target.StartsWith("/", StringComparison.Ordinal)
                    ? target.TrimStart('/')
                    : "xl/" + target.Replace("../", string.Empty);
            }

            return map;
        }

        /// <summary>
        /// The shared-string table.
        ///
        /// Excel puts nearly every string in here rather than in the cell, which is why a reader
        /// that only understood inline strings would read a real workbook as a grid of numbers and
        /// blanks. Rich text is flattened: a cell with three differently-formatted runs is one
        /// value to everything downstream.
        /// </summary>
        private static List<string> SharedStrings(ZipArchive zip)
        {
            var strings = new List<string>();

            var entry = Find(zip, "xl/sharedStrings.xml");
            if (entry == null) return strings;

            var root = Parse(entry);
            if (root == null) return strings;

            foreach (var si in root.Elements(Main + "si"))
                strings.Add(Text(si));

            return strings;
        }

        /// <summary>Every run of text under an element, joined. Ignores phonetic guides.</summary>
        private static string Text(XElement element)
        {
            var direct = element.Element(Main + "t");
            if (direct != null) return direct.Value;

            var runs = element.Elements(Main + "r").Select(r => r.Element(Main + "t")?.Value ?? string.Empty);

            return string.Concat(runs);
        }

        private static List<IReadOnlyList<string?>> Cells(ZipArchiveEntry entry, List<string> strings)
        {
            var rows = new List<IReadOnlyList<string?>>();

            var root = Parse(entry);
            if (root == null) return rows;

            var data = root.Element(Main + "sheetData");
            if (data == null) return rows;

            var at = 0;

            foreach (var row in data.Elements(Main + "row"))
            {
                if (rows.Count >= RowCap) break;

                // A row can declare its own number, and rows can be skipped entirely. Filling the
                // gap keeps row 40 of the file at index 39 here, which is what anybody comparing
                // against Excel's own row numbers expects.
                var number = int.TryParse((string?)row.Attribute("r"), NumberStyles.Integer,
                                          CultureInfo.InvariantCulture, out var declared)
                    ? declared
                    : at + 1;

                while (at + 1 < number && rows.Count < RowCap)
                {
                    rows.Add(Array.Empty<string?>());
                    at++;
                }

                var cells = new List<string?>();

                foreach (var cell in row.Elements(Main + "c"))
                {
                    var column = ColumnIndex((string?)cell.Attribute("r"));
                    if (column < 0) column = cells.Count;

                    while (cells.Count < column) cells.Add(null);

                    var value = Value(cell, strings);

                    if (cells.Count == column) cells.Add(value);
                    else cells[column] = value;
                }

                // Trailing empties carry no information and make every row a different length.
                while (cells.Count > 0 && cells[cells.Count - 1] == null) cells.RemoveAt(cells.Count - 1);

                rows.Add(cells);
                at++;
            }

            return rows;
        }

        private static string? Value(XElement cell, List<string> strings)
        {
            var type = (string?)cell.Attribute("t") ?? "n";

            switch (type)
            {
                case "s":
                {
                    var raw = cell.Element(Main + "v")?.Value;

                    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                           && index >= 0 && index < strings.Count
                        ? strings[index]
                        : null;
                }

                case "inlineStr":
                {
                    var inline = cell.Element(Main + "is");
                    return inline == null ? null : Text(inline);
                }

                case "b":
                    return cell.Element(Main + "v")?.Value == "1" ? "TRUE" : "FALSE";

                default:
                {
                    // "str" is a formula's cached text and "e" is an error such as #REF!; both are
                    // what the person saw, so both come back as they read.
                    var value = cell.Element(Main + "v")?.Value;

                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
    }
}
