using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class AttachmentsTests
    {
        private const string Bin = @"C:\Jobs\Riverside\O&M\AHU-01";

        private static ReportElement Element(string name,
                                             params (string Category, string Name, string? Value)[] properties)
        {
            var key = ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Mech.nwc", name);

            return new ReportElement(key, name, "Mechanical", "AHU", "Mech", new[] { "Mech", name },
                                     properties.Select(p => new PropertyValue(p.Category, p.Name, p.Value)));
        }

        private static AttachmentRule Rule(AttachmentKind kind = AttachmentKind.Document,
                                           string property = "Documents", string? root = null) =>
            new AttachmentRule(kind, new PropertyRef("Asset", property)) { Root = root };

        private static InMemoryFileSystem Disk()
        {
            var files = new InMemoryFileSystem();

            files.With(Bin + @"\Data sheet.pdf", "x");
            files.With(Bin + @"\Warranty.docx", "x");
            files.With(Bin + @"\Thumbs.db", "x");

            return files;
        }

        // ---- Finding what is there -------------------------------------------------------

        [Fact]
        public void A_folder_named_by_a_property_becomes_a_list_somebody_can_open()
        {
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin)) },
                new[] { Rule() }, Disk());

            Assert.Null(report.Refusal);
            Assert.True(report.IsUsable);
            Assert.Equal(1, report.Carrying);

            // The database the operating system left in the folder is not a document.
            Assert.Equal(new[] { "Data sheet.pdf", "Warranty.docx" },
                         report.Found.Select(a => a.Name));
        }

        [Fact]
        public void What_comes_back_is_a_path_that_opens_and_not_the_folder_nine_times()
        {
            // The seam gives file names, not paths. Taking them at face value would produce a list
            // of links that all point at the folder.
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin)) },
                new[] { Rule() }, Disk());

            Assert.Equal(Bin + @"\Data sheet.pdf", report.Found[0].Where);
            Assert.Equal(".pdf", report.Found[0].Extension);
        }

        [Fact]
        public void A_property_naming_one_file_rather_than_a_folder_is_taken_as_that_file()
        {
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin + @"\Data sheet.pdf")) },
                new[] { Rule() }, Disk());

            Assert.Single(report.Found);
            Assert.Equal("Data sheet.pdf", report.Found[0].Name);
        }

        [Fact]
        public void Photos_and_documents_are_the_same_mechanism_pointed_at_different_things()
        {
            var files = Disk();

            files.With(@"C:\Jobs\Riverside\Photos\AHU-01\2026-08-01.jpg", "x");
            files.With(@"C:\Jobs\Riverside\Photos\AHU-01\notes.txt", "x");

            var report = Attachments.Find(
                new[]
                {
                    Element("AHU-01",
                            ("Asset", "Documents", Bin),
                            ("Asset", "Photos", @"C:\Jobs\Riverside\Photos\AHU-01")),
                },
                new[] { Rule(), Rule(AttachmentKind.Photo, "Photos") }, files);

            Assert.Equal(3, report.Found.Count);
            Assert.Single(report.Found.Where(a => a.Kind == AttachmentKind.Photo));
            Assert.Equal("2026-08-01.jpg", report.Found.Single(a => a.Kind == AttachmentKind.Photo).Name);
        }

        [Fact]
        public void A_rule_that_names_the_file_types_it_wants_takes_only_those()
        {
            var rule = Rule();

            rule.Extensions.Add(".pdf");

            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin)) }, new[] { rule }, Disk());

            Assert.Single(report.Found);
            Assert.Equal("Data sheet.pdf", report.Found[0].Name);
        }

        [Fact]
        public void A_relative_path_is_taken_from_the_root_so_the_job_can_move_between_machines()
        {
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", @"O&M\AHU-01")) },
                new[] { Rule(root: @"C:\Jobs\Riverside") }, Disk());

            Assert.Equal(2, report.Found.Count);
            Assert.StartsWith(Bin, report.Found[0].Where, StringComparison.Ordinal);
        }

        [Fact]
        public void A_path_the_model_states_in_full_is_left_alone_whatever_the_root_says()
        {
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin)) },
                new[] { Rule(root: @"D:\Somewhere else") }, Disk());

            Assert.Equal(2, report.Found.Count);
            Assert.StartsWith(Bin, report.Found[0].Where, StringComparison.Ordinal);
        }

        [Fact]
        public void An_address_into_a_document_system_is_taken_at_its_word()
        {
            // Nothing here can reach a document system to ask, and a red cross beside a perfectly
            // good link because the plug-in has no network is worse than no answer.
            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Docs", "https://docs.example/projects/riverside/AHU-01")) },
                new[] { new AttachmentRule(AttachmentKind.Link, new PropertyRef("Asset", "Docs")) },
                Disk());

            Assert.Single(report.Found);
            Assert.True(report.Found[0].Exists);
            Assert.Equal("AHU-01", report.Found[0].Name);
        }

        // ---- Telling the three failures apart --------------------------------------------

        [Fact]
        public void A_folder_that_is_not_there_is_counted_as_that_rather_than_as_empty()
        {
            var report = Attachments.Find(
                new[] { Element("AHU-02", ("Asset", "Documents", @"C:\Jobs\Riverside\O&M\AHU-02")) },
                new[] { Rule() }, Disk());

            Assert.Equal(1, report.Missing);
            Assert.Empty(report.EmptyFolders);
            Assert.Contains("points somewhere that is not there", report.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_folder_that_is_there_and_empty_is_counted_as_that_rather_than_as_missing()
        {
            var files = Disk();

            files.CreateDirectory(@"C:\Jobs\Riverside\O&M\AHU-03");

            var report = Attachments.Find(
                new[] { Element("AHU-03", ("Asset", "Documents", @"C:\Jobs\Riverside\O&M\AHU-03")) },
                new[] { Rule() }, files);

            Assert.Equal(0, report.Missing);
            Assert.Single(report.EmptyFolders);
            Assert.Contains("there and empty", report.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_folder_holding_nothing_the_rule_wants_is_empty_as_far_as_the_rule_goes()
        {
            var files = new InMemoryFileSystem();

            files.With(Bin + @"\Thumbs.db", "x");

            var report = Attachments.Find(
                new[] { Element("AHU-01", ("Asset", "Documents", Bin)) }, new[] { Rule() }, files);

            Assert.Empty(report.Found);
            Assert.Single(report.EmptyFolders);
        }

        [Fact]
        public void An_element_that_says_nothing_is_not_looked_for_and_says_so()
        {
            var report = Attachments.Find(new[] { Element("AHU-09") }, new[] { Rule() }, Disk());

            Assert.Equal(0, report.Carrying);
            Assert.Contains("nothing was even looked for", report.Explain(), StringComparison.Ordinal);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void Nothing_in_scope_and_no_rule_each_say_which_it_is()
        {
            Assert.Contains("Select what you want the paperwork for",
                Attachments.Find(null, new[] { Rule() }, Disk()).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("No rule says which property",
                Attachments.Find(new[] { Element("AHU-01") }, null, Disk()).Refusal ?? string.Empty,
                StringComparison.Ordinal);
        }

        // ---- Working out which property it is --------------------------------------------

        [Fact]
        public void The_property_a_folder_usually_hides_under_is_recognised_by_name()
        {
            var found = Attachments.Likely(new[]
            {
                Element("AHU-01", ("Asset", "PhotoBin Dir", @"C:\Photos\AHU-01"), ("Asset", "Mark", "A1")),
            }, AttachmentKind.Photo);

            Assert.Single(found);
            Assert.Equal("PhotoBin Dir", found[0].Name);
        }

        [Fact]
        public void A_property_called_something_nobody_expected_is_found_by_what_is_in_it()
        {
            var found = Attachments.Likely(new[]
            {
                Element("AHU-01",
                        ("Asset", "Where the paperwork lives", @"C:\Jobs\Riverside\O&M\AHU-01"),
                        ("Asset", "Mark", "AHU-01")),
            }, AttachmentKind.Document);

            Assert.Single(found);
            Assert.Equal("Where the paperwork lives", found[0].Name);
        }

        [Fact]
        public void Nothing_that_looks_like_a_path_comes_back_as_nothing()
        {
            Assert.Empty(Attachments.Likely(
                new[] { Element("AHU-01", ("Asset", "Mark", "AHU-01")) }, AttachmentKind.Document));

            Assert.Empty(Attachments.Likely(null, AttachmentKind.Photo));
        }

        // ---- The half that reaches back to the authoring tool -----------------------------

        [Fact]
        public void The_hand_back_sheet_is_keyed_on_the_id_the_authoring_tool_uses()
        {
            var sheet = Attachments.HandBack(
                new[]
                {
                    Element("AHU-01", ("Element", "Id", "418922"), ("CamelWorks", "Status", "Installed")),
                    Element("AHU-02", ("Element", "Id", "418923"), ("CamelWorks", "Status", "Ordered")),
                },
                new PropertyRef("Element", "Id"),
                new[] { new PropertyRef("CamelWorks", "Status") });

            Assert.Equal(3, sheet.Rows.Count);
            Assert.Equal(new[] { "Id", "Status" }, sheet.Headers.Select(h => h ?? string.Empty));
            Assert.Equal("418922", sheet.Cell(1, 0));
            Assert.Equal("Installed", sheet.Cell(1, 1));
            Assert.Equal("Ordered", sheet.Cell(2, 1));
        }

        [Fact]
        public void An_element_with_no_id_is_left_out_because_the_other_end_could_not_place_it()
        {
            var sheet = Attachments.HandBack(
                new[]
                {
                    Element("AHU-01", ("Element", "Id", "418922")),
                    Element("Imported thing"),
                },
                new PropertyRef("Element", "Id"),
                Array.Empty<PropertyRef>());

            Assert.Equal(2, sheet.Rows.Count);
            Assert.Equal("418922", sheet.Cell(1, 0));
        }

        [Fact]
        public void A_hand_back_of_nothing_is_still_a_sheet_with_its_heading()
        {
            var sheet = Attachments.HandBack(null, default, null);

            Assert.Single(sheet.Rows);
            Assert.Equal(new[] { "Id" }, sheet.Headers.Select(h => h ?? string.Empty));
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void A_rule_survives_being_written_down_and_read_back()
        {
            var rule = Rule(AttachmentKind.Photo, "PhotoBin Dir", @"C:\Jobs\Riverside");

            rule.Extensions.Add(".jpg");
            rule.Extensions.Add(".png");

            var back = AttachmentRule.FromJson(rule.ToJson());

            Assert.NotNull(back);
            Assert.Equal(AttachmentKind.Photo, back!.Kind);
            Assert.Equal("PhotoBin Dir", back.Property.Name);
            Assert.Equal("Asset", back.Property.Category);
            Assert.Equal(@"C:\Jobs\Riverside", back.Root);
            Assert.Equal(new[] { ".jpg", ".png" }, back.Extensions);
        }

        [Fact]
        public void A_rule_naming_no_property_is_not_a_rule()
        {
            Assert.Null(AttachmentRule.FromJson(null));
            Assert.Null(AttachmentRule.FromJson(JsonValue.Object().Set("kind", "Photo")));
            Assert.Empty(Attachments.Saved(Attachments.Keep(new[]
            {
                new AttachmentRule(AttachmentKind.Document, default),
            })));
        }

        [Fact]
        public void The_saved_rules_come_back_in_the_order_they_went_in()
        {
            var written = Attachments.Keep(new[] { Rule(), Rule(AttachmentKind.Photo, "Photos") });
            var back = Attachments.Saved(written);

            Assert.Equal(2, back.Count);
            Assert.Equal(AttachmentKind.Document, back[0].Kind);
            Assert.Equal(AttachmentKind.Photo, back[1].Kind);
            Assert.Empty(Attachments.Saved(null));
        }

        // ---- Reading a path apart --------------------------------------------------------

        [Theory]
        [InlineData(@"C:\Sheets\A-101.pdf", "A-101.pdf", ".pdf")]
        [InlineData("https://docs.example/riverside/AHU-01", "AHU-01", "")]
        [InlineData(@"C:\Sheets\", "Sheets", "")]
        [InlineData("", "", "")]
        public void The_last_part_of_a_path_and_its_type_are_read_whichever_slash_it_uses(
            string path, string leaf, string extension)
        {
            Assert.Equal(leaf, Attachments.Leaf(path));
            Assert.Equal(extension, Attachments.ExtensionOf(path));
        }
    }
}
