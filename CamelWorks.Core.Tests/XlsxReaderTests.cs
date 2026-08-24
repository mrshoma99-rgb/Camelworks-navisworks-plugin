using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Builds workbooks the way Excel does, not the way our writer does.
    ///
    /// The reader's whole job is taking back a file somebody edited in Excel, and Excel puts nearly
    /// every string in a shared table rather than in the cell. A fixture that only exercised our
    /// own output would prove the round trip and miss the feature.
    /// </summary>
    internal static class XlsxFixture
    {
        private const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
        private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PkgNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        internal static byte[] Book(string sheetName, string sheetXml, string? sharedStrings = null,
                                    string? sheetPath = "worksheets/sheet1.xml")
        {
            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "xl/workbook.xml",
                    Head + "<workbook xmlns=\"" + Ns + "\" xmlns:r=\"" + RelNs + "\"><sheets>"
                    + "<sheet name=\"" + sheetName + "\" sheetId=\"1\" r:id=\"rId1\"/>"
                    + "</sheets></workbook>");

                Add(zip, "xl/_rels/workbook.xml.rels",
                    Head + "<Relationships xmlns=\"" + PkgNs + "\">"
                    + "<Relationship Id=\"rId1\" Type=\"" + RelNs + "/worksheet\" Target=\""
                    + sheetPath + "\"/></Relationships>");

                if (sheetPath != null) Add(zip, "xl/" + sheetPath, Head + sheetXml);

                if (sharedStrings != null) Add(zip, "xl/sharedStrings.xml", Head + sharedStrings);
            }

            return buffer.ToArray();
        }

        internal static string Shared(params string[] values) =>
            "<sst xmlns=\"" + Ns + "\" count=\"" + values.Length + "\" uniqueCount=\"" + values.Length + "\">"
            + string.Concat(values.Select(v => "<si><t>" + v + "</t></si>")) + "</sst>";

        internal static string Sheet(string rows) =>
            "<worksheet xmlns=\"" + Ns + "\"><sheetData>" + rows + "</sheetData></worksheet>";

        private static void Add(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var bytes = new UTF8Encoding(false).GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    public class XlsxReaderTests
    {
        [Fact]
        public void A_shared_string_is_resolved_because_that_is_what_Excel_writes()
        {
            // A reader that only understood inline strings would read a real workbook as a grid of
            // numbers and blanks.
            var book = XlsxReader.Read(XlsxFixture.Book("Schedule",
                XlsxFixture.Sheet(
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>"),
                XlsxFixture.Shared("Mark", "Level")));

            var sheet = Assert.Single(book.Sheets);

            Assert.Equal("Schedule", sheet.Name);
            Assert.Equal(new[] { "Mark", "Level" }, sheet.Rows[0]);
        }

        [Fact]
        public void Rich_text_runs_are_flattened_into_one_value()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("S",
                XlsxFixture.Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"),
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<si><r><t>FD</t></r><r><t>60</t></r></si></sst>"));

            Assert.Equal("FD60", book.First!.Cell(0, 0));
        }

        [Fact]
        public void Every_cell_type_Excel_emits_comes_back_as_the_person_saw_it()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\">"
                + "<c r=\"A1\"><v>42</v></c>"
                + "<c r=\"B1\" t=\"inlineStr\"><is><t>inline</t></is></c>"
                + "<c r=\"C1\" t=\"str\"><f>A1*2</f><v>84</v></c>"
                + "<c r=\"D1\" t=\"b\"><v>1</v></c>"
                + "<c r=\"E1\" t=\"b\"><v>0</v></c>"
                + "<c r=\"F1\" t=\"e\"><v>#REF!</v></c>"
                + "</row>")));

            var row = book.First!.Rows[0];

            Assert.Equal("42", row[0]);
            Assert.Equal("inline", row[1]);
            Assert.Equal("84", row[2]);
            Assert.Equal("TRUE", row[3]);
            Assert.Equal("FALSE", row[4]);
            Assert.Equal("#REF!", row[5]);
        }

        [Fact]
        public void A_number_keeps_its_stored_form_rather_than_being_reformatted()
        {
            // Putting a round trip through double is how "0450" becomes "450" and a door code stops
            // matching the model.
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"),
                XlsxFixture.Shared("0450")));

            Assert.Equal("0450", book.First!.Cell(0, 0));
        }

        [Fact]
        public void A_gap_in_the_columns_keeps_everything_after_it_in_the_right_place()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"D1\"><v>4</v></c></row>")));

            var row = book.First!.Rows[0];

            Assert.Equal(4, row.Count);
            Assert.Equal("1", row[0]);
            Assert.Null(row[1]);
            Assert.Null(row[2]);
            Assert.Equal("4", row[3]);
        }

        [Fact]
        public void A_gap_in_the_rows_keeps_row_numbers_matching_Excel()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>"
                + "<row r=\"4\"><c r=\"A4\"><v>4</v></c></row>")));

            var sheet = book.First!;

            Assert.Equal(4, sheet.Rows.Count);
            Assert.Equal("1", sheet.Cell(0, 0));
            Assert.Empty(sheet.Rows[1]);
            Assert.Empty(sheet.Rows[2]);
            Assert.Equal("4", sheet.Cell(3, 0));
        }

        [Fact]
        public void Cells_out_of_order_land_where_their_reference_says()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\"><c r=\"C1\"><v>3</v></c><c r=\"A1\"><v>1</v></c></row>")));

            Assert.Equal("1", book.First!.Cell(0, 0));
            Assert.Equal("3", book.First!.Cell(0, 2));
        }

        [Fact]
        public void A_cell_that_is_not_there_is_null_rather_than_an_exception()
        {
            // Every spreadsheet anybody has been sent is ragged somewhere, and a reader that threw
            // on the first short row would refuse most real files.
            var book = XlsxReader.Read(XlsxFixture.Book("S", XlsxFixture.Sheet(
                "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>")));

            var sheet = book.First!;

            Assert.Null(sheet.Cell(0, 9));
            Assert.Null(sheet.Cell(9, 0));
            Assert.Null(sheet.Cell(-1, -1));
        }

        [Theory]
        [InlineData("A1", 0)]
        [InlineData("B2", 1)]
        [InlineData("Z9", 25)]
        [InlineData("AA1", 26)]
        [InlineData("AB12", 27)]
        [InlineData("BA1", 52)]
        [InlineData("aa1", 26)]
        [InlineData("", -1)]
        [InlineData(null, -1)]
        [InlineData("12", -1)]
        public void A_column_reference_reads_as_its_index(string? reference, int expected)
        {
            Assert.Equal(expected, XlsxReader.ColumnIndex(reference));
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(27, "AB")]
        [InlineData(51, "AZ")]
        [InlineData(52, "BA")]
        [InlineData(-1, "")]
        public void An_index_reads_back_as_its_column(int index, string expected)
        {
            Assert.Equal(expected, XlsxReader.ColumnName(index));

            if (index >= 0) Assert.Equal(index, XlsxReader.ColumnIndex(expected));
        }

        [Fact]
        public void A_heading_is_found_whatever_case_or_padding_it_arrives_in()
        {
            // A sheet round-tripped through three people has a trailing space in a heading
            // somewhere.
            var book = XlsxReader.Read(XlsxFixture.Book("S",
                XlsxFixture.Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c>"
                    + "<c r=\"B1\" t=\"s\"><v>1</v></c></row>"),
                XlsxFixture.Shared("  Element Key ", "MARK")));

            var sheet = book.First!;

            Assert.Equal(0, sheet.ColumnOf("Element key"));
            Assert.Equal(1, sheet.ColumnOf("mark"));
            Assert.Equal(-1, sheet.ColumnOf("Level"));
            Assert.Equal(-1, sheet.ColumnOf(null));
        }

        [Fact]
        public void What_the_writer_writes_the_reader_reads()
        {
            var table = new TableBlock("Element", "Mark", "Level");
            table.Row("Door 1", "D-12", "L03");
            table.Row("Door 2", null, "L03");

            var book = XlsxReader.Read(XlsxWriter.Write("Doors", table));

            var sheet = Assert.Single(book.Sheets);

            Assert.Equal("Doors", sheet.Name);
            Assert.Equal(3, sheet.Rows.Count);
            Assert.Equal(new[] { "Element", "Mark", "Level" }, sheet.Headers);
            Assert.Equal("D-12", sheet.Cell(1, 1));
            Assert.Null(sheet.Cell(2, 1));
            Assert.Equal("L03", sheet.Cell(2, 2));
        }

        [Fact]
        public void Two_sheets_both_come_back_under_their_own_names()
        {
            var first = new XlsxSheet("First");
            first.Row("A");
            first.Row("1");

            var second = new XlsxSheet("Second");
            second.Row("B");
            second.Row("2");

            var book = XlsxReader.Read(XlsxWriter.Write(new[] { first, second }));

            Assert.Equal(2, book.Sheets.Count);
            Assert.Equal("First", book.Sheets[0].Name);
            Assert.Equal("2", book.Sheet("second")!.Cell(1, 0));
            Assert.Null(book.Sheet("Third"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(new byte[0])]
        public void Nothing_to_read_says_so_rather_than_throwing(byte[]? bytes)
        {
            var book = XlsxReader.Read(bytes);

            Assert.False(book.IsUsable);
            Assert.NotNull(book.Problem);
            Assert.Empty(book.Sheets);
            Assert.Null(book.First);
        }

        [Fact]
        public void A_file_that_is_not_a_spreadsheet_says_which_way_it_is_wrong()
        {
            Assert.Contains("not a readable .xlsx",
                XlsxReader.Read(Encoding.UTF8.GetBytes("Mark,Level\nD-12,L03\n")).Problem);
        }

        [Fact]
        public void A_zip_that_is_not_a_workbook_says_so()
        {
            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                zip.CreateEntry("readme.txt");

            Assert.Contains("no xl/workbook.xml", XlsxReader.Read(buffer.ToArray()).Problem);
        }

        [Fact]
        public void A_sheet_whose_part_is_missing_keeps_its_tab_rather_than_disappearing()
        {
            var book = XlsxReader.Read(XlsxFixture.Book("Gone", XlsxFixture.Sheet(string.Empty),
                                                        sheetPath: null));

            var sheet = Assert.Single(book.Sheets);

            Assert.Equal("Gone", sheet.Name);
            Assert.True(sheet.IsEmpty);
            Assert.Equal(0, sheet.Columns);
        }
    }
}
