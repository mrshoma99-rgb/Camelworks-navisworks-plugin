using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CamelWorks.Core.Report
{
    /// <summary>
    /// How one cell is drawn: its ground, its ink, and how the text sits in it.
    ///
    /// A matrix is a picture as much as a table. Exported as bare letters on white, the grid loses
    /// the folders that group it, the colour that says which kind of test each symbol is, the
    /// unused triangle and the diagonal — everything the screen draws. What comes back from the
    /// client is then marked up in a different vocabulary from the one it was sent in.
    ///
    /// Deliberately small: a ground, an ink, bold, centred, rotated and a hairline box. Colours are
    /// <c>RRGGBB</c> without the hash, because that is what the format takes and a converter
    /// between two spellings of the same value is somewhere for a bug to live.
    /// </summary>
    public readonly struct XlsxStyle : IEquatable<XlsxStyle>
    {
        /// <summary>Describe one.</summary>
        /// <param name="fill">The ground as <c>RRGGBB</c>, or null for none.</param>
        /// <param name="ink">The text colour as <c>RRGGBB</c>, or null for black.</param>
        /// <param name="bold">True for bold.</param>
        /// <param name="centre">True to centre the text both ways.</param>
        /// <param name="turned">True to write it bottom-to-top, as a narrow column header.</param>
        /// <param name="boxed">True for a hairline box, which is what makes a grid a grid.</param>
        public XlsxStyle(string? fill = null, string? ink = null, bool bold = false,
                         bool centre = false, bool turned = false, bool boxed = false)
        {
            Fill = Six(fill);
            Ink = Six(ink);
            Bold = bold;
            Centre = centre;
            Turned = turned;
            Boxed = boxed;
        }

        /// <summary>The ground as <c>RRGGBB</c>, or null.</summary>
        public string? Fill { get; }

        /// <summary>The ink as <c>RRGGBB</c>, or null.</summary>
        public string? Ink { get; }

        /// <summary>Bold.</summary>
        public bool Bold { get; }

        /// <summary>Centred both ways.</summary>
        public bool Centre { get; }

        /// <summary>Written bottom-to-top.</summary>
        public bool Turned { get; }

        /// <summary>Boxed with a hairline.</summary>
        public bool Boxed { get; }

        /// <summary>Whether this asks for nothing at all, and so needs no format of its own.</summary>
        public bool IsPlain =>
            Fill == null && Ink == null && !Bold && !Centre && !Turned && !Boxed;

        /// <summary>The same style with a different ground.</summary>
        /// <param name="fill">The ground as <c>RRGGBB</c>, or null for none.</param>
        public XlsxStyle On(string? fill) =>
            new XlsxStyle(fill, Ink, Bold, Centre, Turned, Boxed);

        /// <summary>
        /// A colour as six hex characters, however it arrived.
        ///
        /// The screen writes them with a hash and the format wants them without; a colour that
        /// is not one at all becomes nothing, because a cell with no ground is a cell somebody
        /// can still read.
        /// </summary>
        /// <param name="colour">The colour.</param>
        private static string? Six(string? colour)
        {
            var said = (colour ?? string.Empty).Trim().TrimStart('#');

            if (said.Length == 8) said = said.Substring(2);
            if (said.Length != 6) return null;

            foreach (var c in said)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return null;

            return said.ToUpperInvariant();
        }

        /// <inheritdoc />
        public bool Equals(XlsxStyle other) =>
            string.Equals(Fill, other.Fill, StringComparison.Ordinal)
            && string.Equals(Ink, other.Ink, StringComparison.Ordinal)
            && Bold == other.Bold && Centre == other.Centre
            && Turned == other.Turned && Boxed == other.Boxed;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is XlsxStyle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = 17;

            hash = (hash * 31) + (Fill?.GetHashCode() ?? 0);
            hash = (hash * 31) + (Ink?.GetHashCode() ?? 0);
            hash = (hash * 31) + (Bold ? 1 : 0);
            hash = (hash * 31) + (Centre ? 2 : 0);
            hash = (hash * 31) + (Turned ? 4 : 0);
            hash = (hash * 31) + (Boxed ? 8 : 0);

            return hash;
        }
    }

    /// <summary>One tab of a workbook: its rows, and how any of them are painted.</summary>
    public sealed class XlsxSheet
    {
        /// <summary>Create a sheet.</summary>
        public XlsxSheet(string name)
        {
            Name = Sanitise(name);
            if (Name.Length == 0) throw new ArgumentException("sheet name is required", nameof(name));
        }

        /// <summary>The tab name, already sanitised.</summary>
        public string Name { get; }

        /// <summary>Rows of cells. A null cell is written as empty.</summary>
        public IList<IReadOnlyList<string?>> Rows { get; } = new List<IReadOnlyList<string?>>();

        /// <summary>True to freeze the first row, which every report sheet wants.</summary>
        public bool FreezeHeader { get; set; } = true;

        /// <summary>
        /// How particular cells are painted, by their row and column, both counted from zero.
        ///
        /// Kept beside the text rather than in it. Every caller in this codebase builds a sheet by
        /// handing <see cref="Row(string[])"/> a line of strings, and a report with nothing to say
        /// about colour continues to do that. A grid that is read by its blocks of colour, such as a
        /// clash matrix, states its painting here cell by cell, and nothing else changes.
        /// </summary>
        public IDictionary<(int Row, int Column), XlsxStyle> Styles { get; } =
            new Dictionary<(int, int), XlsxStyle>();

        /// <summary>Blocks of cells written as one, by their corners, counted from nought.</summary>
        public IList<(int Row, int Column, int ToRow, int ToColumn)> Merges { get; } =
            new List<(int, int, int, int)>();

        /// <summary>Columns whose width is stated rather than measured, in characters.</summary>
        public IDictionary<int, double> Widths { get; } = new Dictionary<int, double>();

        /// <summary>Rows whose height is stated rather than left to the reader, in points.</summary>
        public IDictionary<int, double> Heights { get; } = new Dictionary<int, double>();

        /// <summary>Paint one cell.</summary>
        /// <param name="row">Its row, from nought.</param>
        /// <param name="column">Its column, from nought.</param>
        /// <param name="style">How it is drawn.</param>
        public XlsxSheet Paint(int row, int column, XlsxStyle style)
        {
            if (row >= 0 && column >= 0) Styles[(row, column)] = style;

            return this;
        }

        /// <summary>Write a block of cells as one.</summary>
        /// <param name="row">The top row, from nought.</param>
        /// <param name="column">The left column, from nought.</param>
        /// <param name="toRow">The bottom row.</param>
        /// <param name="toColumn">The right column.</param>
        public XlsxSheet Merge(int row, int column, int toRow, int toColumn)
        {
            if (row < 0 || column < 0 || toRow < row || toColumn < column) return this;
            if (toRow == row && toColumn == column) return this;

            Merges.Add((row, column, toRow, toColumn));

            return this;
        }

        /// <summary>Append a row.</summary>
        public XlsxSheet Row(params string?[] cells)
        {
            Rows.Add(cells ?? Array.Empty<string?>());
            return this;
        }

        /// <summary>
        /// Excel refuses a sheet name over 31 characters or containing <c>: \ / ? * [ ]</c>, and
        /// refuses the whole file rather than the sheet. Sanitising here means a group name from a
        /// model can never produce a workbook that will not open.
        /// </summary>
        public static string Sanitise(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in name!.Trim())
            {
                if (c == ':' || c == '\\' || c == '/' || c == '?' || c == '*' || c == '[' || c == ']') sb.Append('-');
                else if (c < 0x20) continue;
                else sb.Append(c);
            }

            var s = sb.ToString().Trim('\'');
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }
    }

    /// <summary>
    /// A minimal XLSX writer.
    ///
    /// Hand-rolled for the same reason as the JSON writer: the architecture test forbids a
    /// third-party runtime, and a free plug-in loading into four host years alongside whatever else
    /// the user has installed cannot risk an assembly-binding conflict with another copy of a
    /// spreadsheet library. It also keeps EPPlus — Polyform Noncommercial, and so unusable in a
    /// product advertised as free for commercial use — out of the bundle.
    ///
    /// Deliberately small: strings and numbers, one header row, a frozen pane, auto-width. No
    /// formulas, no charts, and no styling beyond what <see cref="XlsxStyle"/> carries.
    ///
    /// Every value is written as an inline string except recognised numbers. Left to guess, Excel
    /// turns "L03-1" into a date and drops a leading zero, and a takeoff quantity must survive the
    /// round trip exactly.
    /// </summary>
    public static class XlsxWriter
    {
        /// <summary>Write a workbook to bytes.</summary>
        public static byte[] Write(IReadOnlyList<XlsxSheet> sheets)
        {
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (sheets.Count == 0) throw new ArgumentException("a workbook needs at least one sheet", nameof(sheets));

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sheets)
            {
                if (!names.Add(s.Name))
                    throw new ArgumentException("two sheets are named '" + s.Name + "'; Excel refuses the file", nameof(sheets));
            }

            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
                Add(zip, "_rels/.rels", RootRels);
                Add(zip, "xl/workbook.xml", Workbook(sheets));
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
                // The stylesheet is built from what the sheets ask for. A fixed one could only
                // offer what was anticipated, and a matrix wants a ground per symbol, per folder,
                // per rank and per step of a heat scale, which is not a number that can be named up
                // front. Every distinct style used across the workbook becomes one format, once,
                // and the cells point at it.
                var wanted = Wanted(sheets);

                Add(zip, "xl/styles.xml", Styles(wanted));

                for (var i = 0; i < sheets.Count; i++)
                    Add(zip, "xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml", Sheet(sheets[i], wanted));
            }

            return buffer.ToArray();
        }

        /// <summary>Write a single-sheet workbook from a report table.</summary>
        public static byte[] Write(string sheetName, TableBlock table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var sheet = new XlsxSheet(sheetName);
            sheet.Row(table.Headers.Select(h => (string?)h).ToArray());
            foreach (var row in table.Rows) sheet.Row(row.ToArray());

            return Write(new[] { sheet });
        }

        private static void Add(ZipArchive zip, string path, string content)
        {
            // No compression level games: these files are small, and Store vs Optimal has produced
            // reader quirks in the wild often enough not to be worth the bytes.
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ContentTypes(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");

            for (var i = 1; i <= sheetCount; i++)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");

            sb.Append("</Types>");
            return sb.ToString();
        }

        private const string RootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string Workbook(IReadOnlyList<XlsxSheet> sheets)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");

            for (var i = 0; i < sheets.Count; i++)
            {
                sb.Append("<sheet name=\"");
                EscapeAttr(sb, sheets[i].Name);
                sb.Append("\" sheetId=\"").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" r:id=\"rId").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            }

            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string WorkbookRels(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

            for (var i = 1; i <= sheetCount; i++)
                sb.Append("<Relationship Id=\"rId").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                  .Append(i.ToString(CultureInfo.InvariantCulture)).Append(".xml\"/>");

            // The styles part is always the last relationship id.
            sb.Append("<Relationship Id=\"rId").Append((sheetCount + 1).ToString(CultureInfo.InvariantCulture))
              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>
        /// Every distinct style the workbook uses, in the order their formats are written.
        ///
        /// Index zero is the plain cell and index one is the bold header, because the sheet writer
        /// hard-codes those two for every report that says nothing about colour, so a sheet built
        /// the old way comes out byte for byte as it did.
        /// </summary>
        /// <param name="sheets">The workbook.</param>
        private static List<XlsxStyle> Wanted(IReadOnlyList<XlsxSheet> sheets)
        {
            var wanted = new List<XlsxStyle>
            {
                new XlsxStyle(),
                new XlsxStyle(bold: true),
            };

            foreach (var sheet in sheets)
            foreach (var style in sheet.Styles.Values)
            {
                if (style.IsPlain) continue;
                if (wanted.Contains(style)) continue;

                wanted.Add(style);
            }

            return wanted;
        }

        /// <summary>
        /// The stylesheet: a font, a fill and a border per distinct thing asked for, and one format
        /// per style pointing at them.
        ///
        /// Three of the entries here look redundant and are not, because Excel treats a stylesheet
        /// missing them as a damaged workbook and offers to repair it rather than open it:
        ///
        /// <list type="bullet">
        /// <item>Two fills before any of ours. Index one must be <c>gray125</c> whether or not
        /// anything uses it; it is the reserved second entry, and a single fill is rejected.</item>
        /// <item>A <c>cellStyles</c> entry naming the Normal style. Without it the workbook has no
        /// default style to inherit from.</item>
        /// <item>Every <c>xf</c> stating its numFmtId, fontId, fillId and borderId explicitly. Left
        /// off they are absent rather than defaulted.</item>
        /// </list>
        ///
        /// No theme part is shipped, so nothing here may name one: <c>color theme="1"</c> and
        /// <c>scheme val="minor"</c> both point at a part that is not in the package.
        /// </summary>
        /// <param name="wanted">The styles used, index zero plain and index one bold.</param>
        private static string Styles(List<XlsxStyle> wanted)
        {
            // Fonts and fills are shared between styles that ask for the same one, because two
            // hundred cells of the same colour is one fill and two hundred references to it.
            var fonts = new List<string>();
            var fills = new List<string?> { null, null };
            var borders = new List<bool> { false };

            int Font(XlsxStyle style)
            {
                var one = "<font>" + (style.Bold ? "<b/>" : string.Empty)
                          + "<sz val=\"11\"/><color rgb=\"FF"
                          + (style.Ink ?? "000000") + "\"/>"
                          + "<name val=\"Calibri\"/><family val=\"2\"/></font>";

                var at = fonts.IndexOf(one);

                if (at >= 0) return at;

                fonts.Add(one);

                return fonts.Count - 1;
            }

            int Fill(XlsxStyle style)
            {
                if (style.Fill == null) return 0;

                var at = fills.IndexOf(style.Fill);

                if (at >= 0) return at;

                fills.Add(style.Fill);

                return fills.Count - 1;
            }

            int Border(XlsxStyle style)
            {
                if (!style.Boxed) return 0;

                var at = borders.IndexOf(true);

                if (at >= 0) return at;

                borders.Add(true);

                return borders.Count - 1;
            }

            var formats = new StringBuilder();

            foreach (var style in wanted)
            {
                formats.Append("<xf numFmtId=\"0\" fontId=\"")
                       .Append(Font(style).ToString(CultureInfo.InvariantCulture))
                       .Append("\" fillId=\"").Append(Fill(style).ToString(CultureInfo.InvariantCulture))
                       .Append("\" borderId=\"").Append(Border(style).ToString(CultureInfo.InvariantCulture))
                       .Append("\" xfId=\"0\" applyFont=\"1\"");

                if (style.Fill != null) formats.Append(" applyFill=\"1\"");
                if (style.Boxed) formats.Append(" applyBorder=\"1\"");

                if (style.Centre || style.Turned)
                {
                    formats.Append(" applyAlignment=\"1\"><alignment");

                    if (style.Centre)
                        formats.Append(" horizontal=\"center\" vertical=\"center\"");

                    // Ninety degrees anticlockwise, which is how every clash matrix ever drawn
                    // fits twenty discipline names across the top of a page.
                    if (style.Turned) formats.Append(" textRotation=\"90\"");

                    formats.Append("/></xf>");
                }
                else formats.Append("/>");
            }

            var sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            sb.Append("<fonts count=\"").Append(fonts.Count.ToString(CultureInfo.InvariantCulture))
              .Append("\">");

            foreach (var one in fonts) sb.Append(one);

            sb.Append("</fonts>");

            sb.Append("<fills count=\"").Append(fills.Count.ToString(CultureInfo.InvariantCulture))
              .Append("\">");
            sb.Append("<fill><patternFill patternType=\"none\"/></fill>");
            sb.Append("<fill><patternFill patternType=\"gray125\"/></fill>");

            for (var at = 2; at < fills.Count; at++)
                sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF")
                  .Append(fills[at]).Append("\"/><bgColor rgb=\"FF").Append(fills[at])
                  .Append("\"/></patternFill></fill>");

            sb.Append("</fills>");

            sb.Append("<borders count=\"").Append(borders.Count.ToString(CultureInfo.InvariantCulture))
              .Append("\">");
            sb.Append("<border><left/><right/><top/><bottom/><diagonal/></border>");

            for (var at = 1; at < borders.Count; at++)
                sb.Append("<border>")
                  .Append("<left style=\"thin\"><color rgb=\"FFB0B0B0\"/></left>")
                  .Append("<right style=\"thin\"><color rgb=\"FFB0B0B0\"/></right>")
                  .Append("<top style=\"thin\"><color rgb=\"FFB0B0B0\"/></top>")
                  .Append("<bottom style=\"thin\"><color rgb=\"FFB0B0B0\"/></bottom>")
                  .Append("<diagonal/></border>");

            sb.Append("</borders>");

            sb.Append("<cellStyleXfs count=\"1\">");
            sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>");
            sb.Append("</cellStyleXfs>");

            sb.Append("<cellXfs count=\"").Append(wanted.Count.ToString(CultureInfo.InvariantCulture))
              .Append("\">").Append(formats).Append("</cellXfs>");

            sb.Append("<cellStyles count=\"1\">");
            sb.Append("<cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>");
            sb.Append("</cellStyles>");
            sb.Append("</styleSheet>");

            return sb.ToString();
        }

        private static string Sheet(XlsxSheet sheet, List<XlsxStyle> wanted)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // Order is part of the format. A worksheet's children are a sequence, not a set:
            // sheetViews, then cols, then sheetData. Written in another order the file is still
            // well-formed XML and every parser reads it, but Excel discards the whole part and
            // opens a repaired workbook with the sheet replaced. That happened on every export of
            // more than one row, because the frozen header is on by default and a one-row sheet was
            // the only shape that avoided it.
            if (sheet.FreezeHeader && sheet.Rows.Count > 1)
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\">")
                  .Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
                  .Append("</sheetView></sheetViews>");

            AppendColumnWidths(sb, sheet);

            sb.Append("<sheetData>");

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];

                sb.Append("<row r=\"").Append((r + 1).ToString(CultureInfo.InvariantCulture)).Append('"');

                if (sheet.Heights.TryGetValue(r, out var tall))
                    sb.Append(" ht=\"").Append(tall.ToString("0.##", CultureInfo.InvariantCulture))
                      .Append("\" customHeight=\"1\"");

                sb.Append('>');

                // A merged block is written as its top-left cell and as empty cells for the rest
                // of it - so a row shorter than the block still has to reach the far side of it,
                // or Excel reads the merge as covering cells that are not there.
                var wide = Math.Max(row.Count, Reaches(sheet, r));

                for (var c = 0; c < wide; c++)
                    AppendCell(sb, Reference(c, r + 1), c < row.Count ? row[c] : null,
                               Format(sheet, wanted, r, c));

                sb.Append("</row>");
            }

            sb.Append("</sheetData>");

            if (sheet.Merges.Count > 0)
            {
                sb.Append("<mergeCells count=\"")
                  .Append(sheet.Merges.Count.ToString(CultureInfo.InvariantCulture)).Append("\">");

                foreach (var block in sheet.Merges)
                    sb.Append("<mergeCell ref=\"")
                      .Append(Reference(block.Column, block.Row + 1)).Append(':')
                      .Append(Reference(block.ToColumn, block.ToRow + 1)).Append("\"/>");

                sb.Append("</mergeCells>");
            }

            sb.Append("</worksheet>");
            return sb.ToString();
        }

        /// <summary>How far right a row has to be written, counting the merges that cross it.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="row">The row, from nought.</param>
        private static int Reaches(XlsxSheet sheet, int row)
        {
            var wide = 0;

            foreach (var block in sheet.Merges)
                if (row >= block.Row && row <= block.ToRow && block.ToColumn + 1 > wide)
                    wide = block.ToColumn + 1;

            foreach (var at in sheet.Styles.Keys)
                if (at.Row == row && at.Column + 1 > wide) wide = at.Column + 1;

            return wide;
        }

        /// <summary>Which cell format one cell uses: its own, the header's, or the plain one.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="wanted">The styles, in the order they were written.</param>
        /// <param name="row">The row, from nought.</param>
        /// <param name="column">The column, from nought.</param>
        private static int Format(XlsxSheet sheet, List<XlsxStyle> wanted,
                                  int row, int column)
        {
            if (sheet.Styles.TryGetValue((row, column), out var style) && !style.IsPlain)
            {
                var at = wanted.IndexOf(style);

                if (at >= 0) return at;
            }

            // The first row is the header of a report that said nothing about colour, and is
            // bold for the same reason it always was. A sheet that paints its own cells has
            // said what it wants there.
            return row == 0 && sheet.Styles.Count == 0 ? 1 : 0;
        }

        private static void AppendColumnWidths(StringBuilder sb, XlsxSheet sheet)
        {
            var columns = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Count);

            foreach (var at in sheet.Widths.Keys) columns = Math.Max(columns, at + 1);

            if (columns == 0) return;

            sb.Append("<cols>");
            for (var c = 0; c < columns; c++)
            {
                var longest = sheet.Rows
                    .Where(r => c < r.Count)
                    .Select(r => r[c]?.Length ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();

                // Clamped: one pathological cell should not produce a column nobody can scroll
                // past - unless the sheet has said outright how wide it wants a column, which a
                // matrix does: its columns hold one letter and measuring them by their turned
                // headings would give twenty columns forty characters wide.
                var width = sheet.Widths.TryGetValue(c, out var said)
                    ? said
                    : Math.Min(60, Math.Max(9, longest + 2));
                sb.Append("<col min=\"").Append((c + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" max=\"").Append((c + 1).ToString(CultureInfo.InvariantCulture))
                  .Append("\" width=\"").Append(width.ToString(CultureInfo.InvariantCulture))
                  .Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }

        private static void AppendCell(StringBuilder sb, string reference, string? value, int format)
        {
            sb.Append("<c r=\"").Append(reference).Append('"');

            if (format > 0)
                sb.Append(" s=\"").Append(format.ToString(CultureInfo.InvariantCulture)).Append('"');

            if (string.IsNullOrEmpty(value))
            {
                sb.Append("/>");
                return;
            }

            if (IsPlainNumber(value!))
            {
                sb.Append("><v>").Append(value).Append("</v></c>");
                return;
            }

            // Inline string, not a shared-string table: it costs a few bytes and removes an entire
            // class of index-mismatch corruption, in a writer nobody is going to profile.
            sb.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            EscapeText(sb, value!);
            sb.Append("</t></is></c>");
        }

        /// <summary>
        /// Whether a value should be written as a number.
        ///
        /// Strict on purpose. A leading zero, a leading plus, whitespace, an exponent or anything
        /// Excel might reinterpret stays a string — because "007" becoming 7, or "L03-1" becoming a
        /// date, is a takeoff quantity or a group name silently changing on the way to the client.
        /// </summary>
        private static bool IsPlainNumber(string value)
        {
            if (value.Length == 0 || value.Length > 15) return false;
            if (value != value.Trim()) return false;

            var i = 0;
            if (value[0] == '-') i = 1;
            if (i >= value.Length) return false;

            // A leading zero is significant to a human — a drawing number, a level code — so it is
            // never a number to us. "0" alone is fine.
            if (value[i] == '0' && value.Length > i + 1 && value[i + 1] != '.') return false;

            var seenDot = false;
            for (; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '.')
                {
                    if (seenDot) return false;
                    seenDot = true;
                    continue;
                }
                if (c < '0' || c > '9') return false;
            }

            return true;
        }

        private static string Reference(int columnIndex, int rowNumber)
        {
            var sb = new StringBuilder();
            var c = columnIndex;
            do
            {
                sb.Insert(0, (char)('A' + (c % 26)));
                c = (c / 26) - 1;
            } while (c >= 0);

            sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static void EscapeAttr(StringBuilder sb, string text)
        {
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(c); break;
                }
            }
        }

        private static void EscapeText(StringBuilder sb, string text)
        {
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default:
                        // XML 1.0 cannot carry most control characters at all, escaped or not.
                        // Dropping them beats writing a file Excel refuses to open.
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') break;
                        sb.Append(c);
                        break;
                }
            }
        }
    }
}
