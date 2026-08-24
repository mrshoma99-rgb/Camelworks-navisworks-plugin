using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class IdsSheetTests
    {
        /// <summary>Every wall must carry a fire rating, and one of three does not.</summary>
        private static (IdsDocument Ids, IdsReport Report) Checked()
        {
            var document = new IdsDocument("Riverside") { Version = "1.2", Author = "jo@site.com" };

            var spec = new IdsSpecification("Walls carry a fire rating")
            {
                Instructions = "Fill it in on the type, not the instance.",
            };

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_WallCommon"), IdsValue.Simple("FireRating")));

            document.Specifications.Add(spec);

            var elements = new List<IdsElement>();

            for (var i = 1; i <= 3; i++)
            {
                var element = new IdsElement("wall-" + i, "IFCWALL");

                if (i < 3) element.Properties.Add(new IdsProperty("Pset_WallCommon", "FireRating", "60"));

                elements.Add(element);
            }

            // And one the specification does not apply to at all.
            elements.Add(new IdsElement("slab-1", "IFCSLAB"));

            return (document, IdsChecker.Check(document, elements));
        }

        private static SheetTable Sheet(byte[] bytes, string name)
        {
            var book = XlsxReader.Read(bytes);

            Assert.True(book.IsUsable, book.Problem ?? "the workbook did not read back");

            var sheet = book.Sheet(name);

            Assert.NotNull(sheet);

            return sheet!;
        }

        [Fact]
        public void The_summary_says_where_it_came_from_before_it_says_anything_else()
        {
            var (document, report) = Checked();

            var sheet = Sheet(IdsSheet.Write(document, report, 637_000_000_000_000_000L),
                              IdsSheet.SummarySheet);

            Assert.Equal(new string?[] { "IDS", "Riverside" }, sheet.Rows[0]);
            Assert.Equal(new string?[] { "Version", "1.2" }, sheet.Rows[1]);
            Assert.Equal(new string?[] { "Author", "jo@site.com" }, sheet.Rows[2]);
            Assert.StartsWith("2019-", sheet.Cell(3, 1)!, StringComparison.Ordinal);
            Assert.Equal("Elements read", sheet.Cell(4, 0));
            Assert.Equal("4", sheet.Cell(4, 1));
        }

        [Fact]
        public void One_row_per_specification_for_the_person_deciding()
        {
            var (document, report) = Checked();

            var sheet = Sheet(IdsSheet.Write(document, report), IdsSheet.SummarySheet);

            // Four provenance rows and a blank one, then the header.
            var header = sheet.Rows.Select((r, i) => (r, i))
                              .First(x => string.Equals(x.r.FirstOrDefault(), "Specification",
                                                        StringComparison.Ordinal)).i;

            Assert.Equal(new string?[]
            {
                "Specification", "Verdict", "Applies to", "Pass", "Fail", "Instructions",
            }, sheet.Rows[header]);

            Assert.Equal("Walls carry a fire rating", sheet.Cell(header + 1, 0));
            Assert.Equal("fail", sheet.Cell(header + 1, 1));
            Assert.Equal("3", sheet.Cell(header + 1, 2));
            Assert.Equal("2", sheet.Cell(header + 1, 3));
            Assert.Equal("1", sheet.Cell(header + 1, 4));
            Assert.Equal("Fill it in on the type, not the instance.", sheet.Cell(header + 1, 5));
        }

        [Fact]
        public void One_row_per_element_per_requirement_for_the_person_fixing()
        {
            var (document, report) = Checked();

            var sheet = Sheet(IdsSheet.Write(document, report), IdsSheet.FailureSheet);

            Assert.Equal(new string?[]
            {
                "Element", "Specification", "Requirement", "What is wrong", "Instructions",
            }, sheet.Headers);

            // The element first, so it sorts into a work list.
            Assert.Equal("wall-3", sheet.Cell(1, 0));
            Assert.Equal("Walls carry a fire rating", sheet.Cell(1, 1));
            Assert.Contains("FireRating", sheet.Cell(1, 2)!, StringComparison.Ordinal);
            Assert.Equal("missing", sheet.Cell(1, 3));
            Assert.Equal("Fill it in on the type, not the instance.", sheet.Cell(1, 4));

            Assert.Equal(2, sheet.Rows.Count);
        }

        [Fact]
        public void A_specification_that_matched_nothing_never_reads_as_a_pass()
        {
            // The single most useful thing an IDS report says, and the one a green row would hide.
            var document = new IdsDocument("Unmatched");
            var spec = new IdsSpecification("Ducts carry a system name");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCDUCTSEGMENT")));
            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));

            document.Specifications.Add(spec);

            var report = IdsChecker.Check(document, new[] { new IdsElement("wall-1", "IFCWALL") });

            var sheet = Sheet(IdsSheet.Write(document, report), IdsSheet.SummarySheet);
            var row = sheet.Rows.Count - 1;

            Assert.Equal("Ducts carry a system name", sheet.Cell(row, 0));
            Assert.Equal("nothing matched", sheet.Cell(row, 1));
            Assert.Equal("nothing", sheet.Cell(row, 2));
        }

        [Fact]
        public void Too_few_matching_elements_is_its_own_verdict()
        {
            var document = new IdsDocument("Occurs");
            var spec = new IdsSpecification("There is at least one core wall") { MinOccurs = 1 };

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));

            document.Specifications.Add(spec);

            var report = IdsChecker.Check(document, new[] { new IdsElement("slab-1", "IFCSLAB") });

            Assert.Equal("too few matched", IdsSheet.Verdict(Assert.Single(report.Results)));
        }

        [Fact]
        public void A_verdict_of_nothing_at_all_is_not_a_crash()
        {
            Assert.Equal("unknown", IdsSheet.Verdict(null));

            var sheets = IdsSheet.Sheets(null, null);

            Assert.Equal(2, sheets.Count);
            Assert.Equal(IdsSheet.SummarySheet, sheets[0].Name);
            Assert.Equal(IdsSheet.FailureSheet, sheets[1].Name);

            var summary = Sheet(IdsSheet.Write(null, null), IdsSheet.SummarySheet);

            Assert.Equal(new string?[] { "IDS", "(none)" }, summary.Rows[0]);
            Assert.Equal("0", summary.Cell(1, 1));
        }

        [Fact]
        public void A_capped_list_of_failures_says_it_is_capped_in_the_sheet()
        {
            // A workbook that quietly holds the first hundred of four hundred is one somebody works
            // through and believes they have finished.
            var document = new IdsDocument("Many");
            var spec = new IdsSpecification("Everything carries a name");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));

            document.Specifications.Add(spec);

            var elements = Enumerable.Range(1, 5000)
                .Select(n => new IdsElement("wall-" + n, "IFCWALL"))
                .ToList();

            var report = IdsChecker.Check(document, elements);
            var result = Assert.Single(report.Results);

            Assert.True(result.ViolationsTruncated,
                        "5,000 failures should be more than the checker's cap");

            var sheet = Sheet(IdsSheet.Write(document, report), IdsSheet.FailureSheet);
            var last = sheet.Rows[sheet.Rows.Count - 1];

            Assert.Contains("run it again", last[3]!, StringComparison.Ordinal);
        }

        [Fact]
        public void The_summary_does_not_freeze_a_row_that_is_not_the_header()
        {
            // Frozen at row one, the provenance above the header would stay and the column names
            // would scroll away with the rows.
            var sheets = IdsSheet.Sheets(new IdsDocument("T"), null);

            Assert.False(sheets[0].FreezeHeader);
            Assert.True(sheets[1].FreezeHeader);
        }
    }
}
