using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class DataReportTests
    {
        private static ReportElement Element(string name, string? category = null, string? type = null,
                                             string? model = null, string[]? path = null,
                                             params (string Category, string Name, string? Value)[] properties)
        {
            var key = ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Arch.nwc", name);

            return new ReportElement(key, name, category, type, model,
                                     path ?? new[] { "Arch", name },
                                     properties.Select(p => new PropertyValue(p.Category, p.Name, p.Value)));
        }

        private static IReadOnlyList<ReportElement> Doors()
        {
            return new[]
            {
                Element("Door 01", "Doors", "Single 900", "Arch",
                        properties: new (string, string, string?)[]
                        {
                            ("Identity Data", "Mark", "D-01"),
                            ("Identity Data", "Fire rating", "FD60"),
                            ("Dimensions", "Width", "900 mm"),
                            ("Other", "Phase", "New"),
                        }),
                Element("Door 02", "Doors", "Single 900", "Arch",
                        properties: new (string, string, string?)[]
                        {
                            ("Identity Data", "Mark", "D-02"),
                            ("Identity Data", "Fire rating", "FD60"),
                            ("Dimensions", "Width", "900 mm"),
                            ("Other", "Phase", "New"),
                        }),
                Element("Door 03", "Doors", "Double 1800", "Arch",
                        properties: new (string, string, string?)[]
                        {
                            ("Identity Data", "Mark", "D-03"),
                            ("Identity Data", "Fire rating", "FD30"),
                            ("Dimensions", "Width", "1800 mm"),
                            ("Other", "Phase", "New"),
                        }),
            };
        }

        // ---- Reading what is there -------------------------------------------------------

        [Fact]
        public void Field_discovery_says_how_far_a_property_reaches_and_what_it_looks_like()
        {
            var fields = DataReport.Fields(Doors());

            var rating = fields.Single(f => f.Name == "Fire rating");

            Assert.Equal("Identity Data", rating.Category);
            Assert.Equal(3, rating.Carried);
            Assert.Equal(3, rating.Filled);
            Assert.Equal(2, rating.Distinct);
            Assert.NotNull(rating.Example);

            Assert.Contains("on 3 of 3", rating.Describe(3), StringComparison.Ordinal);
            Assert.Contains("2 values", rating.Describe(3), StringComparison.Ordinal);
        }

        [Fact]
        public void A_property_with_the_same_value_everywhere_is_called_out_as_telling_nobody_anything()
        {
            var phase = DataReport.Fields(Doors()).Single(f => f.Name == "Phase");

            Assert.True(phase.IsConstant);
            Assert.False(phase.IsIdentifier);
            Assert.Contains("the same one everywhere", phase.Describe(3), StringComparison.Ordinal);
        }

        [Fact]
        public void A_property_with_a_different_value_on_every_element_reads_as_an_identifier()
        {
            // Three is too few to be sure, so the rule needs more than four before it will say so.
            var wide = Enumerable.Range(1, 9)
                .Select(i => Element("Door " + i, "Doors",
                                     properties: new (string, string, string?)[]
                                     {
                                         ("Identity Data", "Mark", "D-" + i),
                                         ("Identity Data", "Fire rating", "FD60"),
                                     }))
                .ToList();

            var mark = DataReport.Fields(wide).Single(f => f.Name == "Mark");

            Assert.True(mark.IsIdentifier);
            Assert.Contains("one each", mark.Describe(9), StringComparison.Ordinal);
        }

        [Fact]
        public void Blank_values_count_as_carried_but_not_as_filled()
        {
            var mixed = new[]
            {
                Element("A", properties: new (string, string, string?)[] { ("Set", "Code", "C1") }),
                Element("B", properties: new (string, string, string?)[] { ("Set", "Code", "   ") }),
                Element("C", properties: new (string, string, string?)[] { ("Set", "Code", null) }),
            };

            var code = DataReport.Fields(mixed).Single(f => f.Name == "Code");

            Assert.Equal(3, code.Carried);
            Assert.Equal(1, code.Filled);
            Assert.Equal(1, code.Distinct);
        }

        // ---- Working with nothing set up -------------------------------------------------

        [Fact]
        public void A_report_with_no_template_at_all_still_comes_out_of_the_model()
        {
            // The whole point: press the button on a model nobody has configured anything for.
            var table = DataReport.Build(Doors(), null);

            Assert.Null(table.Refusal);
            Assert.True(table.IsUsable);
            Assert.Equal(3, table.Rows.Count);

            Assert.Contains("Name", table.Headings, StringComparer.Ordinal);
            Assert.Contains("Category", table.Headings, StringComparer.Ordinal);
            Assert.Contains("Fire rating", table.Headings, StringComparer.Ordinal);
        }

        [Fact]
        public void The_proposal_leaves_out_the_property_that_never_varies_and_the_one_that_always_does()
        {
            var wide = Enumerable.Range(1, 9)
                .Select(i => Element("Door " + i, "Doors",
                                     properties: new (string, string, string?)[]
                                     {
                                         ("Identity Data", "Mark", "D-" + i),
                                         ("Other", "Phase", "New"),
                                         ("Identity Data", "Fire rating", i % 2 == 0 ? "FD60" : "FD30"),
                                     }))
                .ToList();

            var headings = DataReport.Propose(wide).Columns.Select(c => c.Heading).ToList();

            Assert.Contains("Fire rating", headings, StringComparer.Ordinal);
            Assert.DoesNotContain("Phase", headings, StringComparer.Ordinal);
            Assert.DoesNotContain("Mark", headings, StringComparer.Ordinal);
        }

        [Fact]
        public void The_model_column_only_appears_when_there_is_more_than_one_model()
        {
            Assert.DoesNotContain("Model",
                DataReport.Propose(Doors()).Columns.Select(c => c.Heading), StringComparer.Ordinal);

            var federation = Doors()
                .Concat(new[] { Element("Duct 01", "Ducts", model: "Mech") })
                .ToList();

            Assert.Contains("Model",
                DataReport.Propose(federation).Columns.Select(c => c.Heading), StringComparer.Ordinal);
        }

        [Fact]
        public void Two_properties_of_the_same_name_on_different_tabs_are_told_apart_by_their_tab()
        {
            var clashing = Enumerable.Range(1, 9)
                .Select(i => Element("Part " + i, "Parts",
                                     properties: new (string, string, string?)[]
                                     {
                                         ("Identity Data", "Code", "A" + (i % 3)),
                                         ("Asset", "Code", "B" + (i % 4)),
                                     }))
                .ToList();

            var headings = DataReport.Propose(clashing).Columns.Select(c => c.Heading).ToList();

            Assert.Contains("Identity Data / Code", headings, StringComparer.Ordinal);
            Assert.Contains("Asset / Code", headings, StringComparer.Ordinal);
        }

        [Fact]
        public void A_property_column_the_proposal_added_is_one_a_person_may_fill_in_and_send_back()
        {
            var template = DataReport.Propose(Doors());

            Assert.True(template.Columns.Single(c => c.Heading == "Fire rating").Editable);
            Assert.False(template.Columns.Single(c => c.Heading == "Name").Editable);
        }

        [Fact]
        public void A_column_that_is_not_a_property_cannot_be_made_editable_because_there_is_nowhere_to_put_it()
        {
            var column = ReportColumn.Built(ReportField.Path);
            column.Editable = true;

            Assert.False(column.Editable);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void A_template_with_no_columns_says_what_to_do_about_it()
        {
            var table = DataReport.Build(Doors(), new ReportTemplate("Empty"));

            Assert.NotNull(table.Refusal);
            Assert.False(table.IsUsable);
            Assert.Contains("Suggest", table.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_scope_says_so_rather_than_producing_a_sheet_of_headings()
        {
            var table = DataReport.Build(Array.Empty<ReportElement>(), DataReport.Propose(Doors()));

            Assert.NotNull(table.Refusal);
            Assert.Contains("no elements in scope", table.Refusal!, StringComparison.Ordinal);
        }

        // ---- The sheet comes back --------------------------------------------------------

        [Fact]
        public void The_identity_column_leads_the_sheet_and_is_named_what_the_reader_looks_for()
        {
            var table = DataReport.Build(Doors(), null);

            Assert.True(table.WithIdentity);
            Assert.Equal(SheetImport.KeyHeading, table.Headings[0]);
        }

        [Fact]
        public void What_goes_out_is_what_comes_back_in()
        {
            var scope = Doors();
            var table = DataReport.Build(scope, null);
            var sheet = DataReport.Sheet(table);

            var returned = new SheetTable("Model data", sheet.Rows.ToList());

            var report = SheetImport.Of(returned, scope.Select(e => e.Key.ToString()).ToList());

            Assert.Null(report.Problem);
            Assert.True(report.IsUsable);
            Assert.Equal(3, report.Matched.Count);
            Assert.Empty(report.Unmatched);
            Assert.True(report.ColumnOf("Fire rating") > 0);
        }

        [Fact]
        public void A_report_that_can_be_sent_back_says_how_on_the_sheet_that_explains_it()
        {
            var template = DataReport.Propose(Doors());
            var sheets = DataReport.Workbook(DataReport.Build(Doors(), template), template);

            Assert.Equal(2, sheets.Count);
            Assert.Equal("How this was made", sheets[1].Name);

            var text = string.Join(" ", sheets[1].Rows.SelectMany(r => r).Select(c => c ?? string.Empty));

            Assert.Contains(SheetImport.KeyHeading, text, StringComparison.Ordinal);
            Assert.Contains("Bring a sheet back in", text, StringComparison.Ordinal);
            Assert.Contains("the property \"Fire rating\"", text, StringComparison.Ordinal);
        }

        // ---- Folding ---------------------------------------------------------------------

        [Fact]
        public void Grouping_folds_the_rows_and_keeps_the_value_it_folded_on()
        {
            var template = new ReportTemplate("By rating");
            template.Add(ReportColumn.Of("Identity Data", "Fire rating"));
            template.Add(ReportColumn.Built(ReportField.Type));
            template.GroupBy = "Fire rating";

            var table = DataReport.Build(Doors(), template);

            Assert.True(table.Grouped);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(3, table.Elements);

            var sixty = table.Rows.Single(r => r.Identity == "FD60");

            Assert.Equal(2, sixty.Members);
            Assert.Equal("FD60", sixty.Cell(table.ColumnOf("Fire rating")));
        }

        [Fact]
        public void A_grouped_report_drops_the_identity_column_and_says_why()
        {
            var template = new ReportTemplate("By rating");
            template.Add(ReportColumn.Of("Identity Data", "Fire rating"));
            template.GroupBy = "Fire rating";
            template.WithIdentity = true;

            var table = DataReport.Build(Doors(), template);

            Assert.False(table.WithIdentity);
            Assert.DoesNotContain(SheetImport.KeyHeading, table.Headings, StringComparer.Ordinal);
            Assert.Contains(table.Notes, n => n.Contains("cannot be sent back in", StringComparison.Ordinal));
        }

        [Fact]
        public void Grouping_on_a_column_that_is_no_longer_there_reports_it_rather_than_silently_flattening()
        {
            var template = new ReportTemplate("Stale");
            template.Add(ReportColumn.Built(ReportField.Name));
            template.GroupBy = "Fire rating";

            var table = DataReport.Build(Doors(), template);

            Assert.False(table.Grouped);
            Assert.Contains(table.Notes, n => n.Contains("no \"Fire rating\" column", StringComparison.Ordinal));
        }

        [Fact]
        public void A_folded_column_shows_the_shared_value_or_says_that_there_is_not_one()
        {
            var template = new ReportTemplate("By rating");
            template.Add(ReportColumn.Of("Identity Data", "Fire rating"));

            var type = ReportColumn.Built(ReportField.Type);
            template.Add(type);

            template.GroupBy = "Fire rating";

            var table = DataReport.Build(Doors(), template);
            var column = table.ColumnOf("Type");

            Assert.Equal("Single 900", table.Rows.Single(r => r.Identity == "FD60").Cell(column));
            Assert.Equal("Double 1800", table.Rows.Single(r => r.Identity == "FD30").Cell(column));

            template.GroupBy = "Type";
            template.Columns[0].Roll = ReportRoll.Same;

            var byType = DataReport.Build(Doors(), template);

            Assert.Equal("FD60", byType.Rows.Single(r => r.Identity == "Single 900")
                                            .Cell(byType.ColumnOf("Fire rating")));
        }

        [Fact]
        public void Elements_that_disagree_under_a_folded_column_say_so_instead_of_picking_one()
        {
            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));
            template.Add(ReportColumn.Of("Identity Data", "Fire rating"));
            template.GroupBy = "Category";

            var table = DataReport.Build(Doors(), template);

            Assert.Single(table.Rows);
            Assert.Equal(DataReport.Varies, table.Rows[0].Cell(table.ColumnOf("Fire rating")));
        }

        [Fact]
        public void A_counting_column_counts_the_elements_and_a_listing_column_lists_them()
        {
            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            var counted = ReportColumn.Built(ReportField.Name, "How many");
            counted.Roll = ReportRoll.Count;
            template.Add(counted);

            var listed = ReportColumn.Of("Identity Data", "Fire rating", "Ratings");
            listed.Roll = ReportRoll.List;
            template.Add(listed);

            var distinct = ReportColumn.Built(ReportField.Type, "Types");
            distinct.Roll = ReportRoll.Distinct;
            template.Add(distinct);

            template.GroupBy = "Category";

            var table = DataReport.Build(Doors(), template);
            var row = table.Rows.Single();

            Assert.Equal("3", row.Cell(table.ColumnOf("How many")));
            Assert.Equal("FD30, FD60", row.Cell(table.ColumnOf("Ratings")));
            Assert.Equal("2", row.Cell(table.ColumnOf("Types")));
        }

        [Fact]
        public void A_listing_column_stops_counting_out_values_once_there_are_too_many()
        {
            var many = Enumerable.Range(1, DataReport.ListCap + 4)
                .Select(i => Element("Part " + i, "Parts",
                                     properties: new (string, string, string?)[] { ("Set", "Code", "C" + i) }))
                .ToList();

            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            var listed = ReportColumn.Of("Set", "Code", "Codes");
            listed.Roll = ReportRoll.List;
            template.Add(listed);

            template.GroupBy = "Category";

            var cell = DataReport.Build(many, template).Rows.Single().Cell(1);

            Assert.NotNull(cell);
            Assert.Contains("and 4 more", cell!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_column_that_adds_up_adds_up_in_the_units_the_project_works_in()
        {
            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            var width = ReportColumn.Of("Dimensions", "Width", "Total width");
            width.Roll = ReportRoll.Sum;
            template.Add(width);

            template.GroupBy = "Category";

            var metres = DataReport.Build(Doors(), template, Measure.Metric).Rows.Single().Cell(1);
            var millimetres = DataReport.Build(Doors(), template, Measure.Millimetres).Rows.Single().Cell(1);

            Assert.NotNull(metres);
            Assert.NotNull(millimetres);
            Assert.Contains("3.6", metres!, StringComparison.Ordinal);
            Assert.Contains("3,600", millimetres!, StringComparison.Ordinal);
        }

        [Fact]
        public void The_smallest_the_largest_and_the_average_are_each_available_on_a_folded_column()
        {
            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            foreach (var pair in new[]
                     {
                         (Roll: ReportRoll.Least, Heading: "Narrowest"),
                         (Roll: ReportRoll.Most, Heading: "Widest"),
                         (Roll: ReportRoll.Mean, Heading: "Average"),
                     })
            {
                var column = ReportColumn.Of("Dimensions", "Width", pair.Heading);
                column.Roll = pair.Roll;
                template.Add(column);
            }

            template.GroupBy = "Category";

            var table = DataReport.Build(Doors(), template, Measure.Millimetres);
            var row = table.Rows.Single();

            Assert.Equal("900 mm", row.Cell(table.ColumnOf("Narrowest")));
            Assert.Equal("1,800 mm", row.Cell(table.ColumnOf("Widest")));
            Assert.Equal("1,200 mm", row.Cell(table.ColumnOf("Average")));
        }

        [Fact]
        public void Adding_up_values_that_are_not_all_numbers_refuses_the_sum_and_names_the_column()
        {
            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            var rating = ReportColumn.Of("Identity Data", "Fire rating", "Total rating");
            rating.Roll = ReportRoll.Sum;
            template.Add(rating);

            template.GroupBy = "Category";

            var table = DataReport.Build(Doors(), template);

            Assert.Equal(DataReport.Mixed, table.Rows.Single().Cell(1));
            Assert.Contains(table.Notes, n => n.Contains("\"Total rating\"", StringComparison.Ordinal));
            Assert.Contains(table.Notes, n => n.Contains("not all numbers", StringComparison.Ordinal));
        }

        [Fact]
        public void Adding_a_length_to_an_area_is_refused_rather_than_producing_a_plausible_total()
        {
            var muddled = new[]
            {
                Element("A", "Parts", properties: new (string, string, string?)[] { ("Q", "Size", "2 m") }),
                Element("B", "Parts", properties: new (string, string, string?)[] { ("Q", "Size", "3 m2") }),
            };

            var template = new ReportTemplate("By category");
            template.Add(ReportColumn.Built(ReportField.Category));

            var size = ReportColumn.Of("Q", "Size", "Total size");
            size.Roll = ReportRoll.Sum;
            template.Add(size);

            template.GroupBy = "Category";

            Assert.Equal(DataReport.Mixed, DataReport.Build(muddled, template).Rows.Single().Cell(1));
        }

        // ---- Shape -----------------------------------------------------------------------

        [Fact]
        public void Sorting_a_column_of_numbers_orders_it_as_numbers_not_as_text()
        {
            var numbered = new[] { 2, 10, 1 }
                .Select(i => Element("Part " + i, "Parts",
                                     properties: new (string, string, string?)[]
                                     {
                                         ("Set", "Order", i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                     }))
                .ToList();

            var template = new ReportTemplate("Ordered");
            template.Add(ReportColumn.Of("Set", "Order"));
            template.SortBy = "Order";
            template.WithIdentity = false;

            var up = DataReport.Build(numbered, template);

            Assert.Equal(new[] { "1", "2", "10" }, up.Rows.Select(r => r.Cell(0) ?? string.Empty));

            template.Descending = true;

            Assert.Equal(new[] { "10", "2", "1" },
                DataReport.Build(numbered, template).Rows.Select(r => r.Cell(0) ?? string.Empty));
        }

        [Fact]
        public void Sorting_a_column_of_words_orders_it_without_caring_about_case()
        {
            var words = new[] { "beta", "Alpha", "gamma" }
                .Select(w => Element(w, "Parts"))
                .ToList();

            var template = new ReportTemplate("Ordered");
            template.Add(ReportColumn.Built(ReportField.Name));
            template.SortBy = "Name";
            template.WithIdentity = false;

            Assert.Equal(new[] { "Alpha", "beta", "gamma" },
                DataReport.Build(words, template).Rows.Select(r => r.Cell(0) ?? string.Empty));
        }

        [Fact]
        public void A_report_stops_at_its_ceiling_and_says_how_many_it_left_out()
        {
            var many = Enumerable.Range(1, 40).Select(i => Element("Part " + i, "Parts")).ToList();

            var template = new ReportTemplate("Capped");
            template.Add(ReportColumn.Built(ReportField.Name));
            template.Ceiling = 25;

            var table = DataReport.Build(many, template);

            Assert.Equal(25, table.Rows.Count);
            Assert.Equal(15, table.Dropped);
            Assert.Contains("15 more did not fit", table.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_element_that_carries_nothing_there_says_so_unless_it_is_told_not_to()
        {
            var bare = new[] { Element("Part 1", "Parts") };

            var template = new ReportTemplate("Bare");
            template.Add(ReportColumn.Of("Set", "Code"));
            template.WithIdentity = false;

            Assert.Equal(DataReport.Blank, DataReport.Build(bare, template).Rows.Single().Cell(0));

            template.BlanksShown = false;

            Assert.Null(DataReport.Build(bare, template).Rows.Single().Cell(0));
        }

        [Fact]
        public void Two_columns_wanting_the_same_heading_are_told_apart_on_the_sheet()
        {
            var template = new ReportTemplate("Doubled");
            template.Add(ReportColumn.Of("A", "Code", "Code"));
            template.Add(ReportColumn.Of("B", "Code", "Code"));
            template.WithIdentity = false;

            Assert.Equal(new[] { "Code", "Code (2)" }, DataReport.Build(Doors(), template).Headings);
        }

        [Fact]
        public void A_column_can_read_one_level_of_the_selection_tree()
        {
            var deep = new[]
            {
                Element("Door 01", "Doors", path: new[] { "Riverside", "Level 02", "Doors", "Door 01" }),
            };

            var template = new ReportTemplate("Levels");
            template.Add(ReportColumn.Built(ReportField.PathLevel, level: 2));
            template.Add(ReportColumn.Built(ReportField.Path));
            template.WithIdentity = false;

            var row = DataReport.Build(deep, template).Rows.Single();

            Assert.Equal("Level 02", row.Cell(0));
            Assert.Equal("Riverside > Level 02 > Doors > Door 01", row.Cell(1));
        }

        [Fact]
        public void A_level_deeper_than_the_tree_goes_reads_as_nothing_rather_than_throwing()
        {
            var shallow = new[] { Element("Door 01", "Doors", path: new[] { "Arch" }) };

            var template = new ReportTemplate("Levels");
            template.Add(ReportColumn.Built(ReportField.PathLevel, level: 6));
            template.WithIdentity = false;

            Assert.Equal(DataReport.Blank, DataReport.Build(shallow, template).Rows.Single().Cell(0));
        }

        [Fact]
        public void A_property_asked_for_without_a_tab_is_found_on_whatever_tab_it_sits_on()
        {
            var template = new ReportTemplate("Loose");
            template.Add(ReportColumn.Of(string.Empty, "Fire rating"));
            template.WithIdentity = false;

            Assert.Equal("FD60", DataReport.Build(Doors(), template).Rows[0].Cell(0));
        }

        // ---- Grouping suggestion ---------------------------------------------------------

        [Fact]
        public void The_suggested_grouping_is_a_column_with_enough_values_to_separate_but_few_enough_to_read()
        {
            var template = DataReport.Propose(Doors());

            Assert.Equal("Fire rating", DataReport.SuggestGroup(Doors(), template));
        }

        [Fact]
        public void Nothing_is_suggested_to_group_on_when_there_is_no_template_to_group()
        {
            Assert.Null(DataReport.SuggestGroup(Doors(), null));
            Assert.Null(DataReport.SuggestGroup(Doors(), new ReportTemplate("Empty")));
            Assert.Null(DataReport.SuggestGroup(Array.Empty<ReportElement>(), DataReport.Propose(Doors())));
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void A_template_survives_being_written_down_and_read_back()
        {
            var template = DataReport.Propose(Doors());
            template.Name = "Door schedule";
            template.GroupBy = "Fire rating";
            template.SortBy = "Name";
            template.Descending = true;
            template.BlanksShown = false;
            template.Ceiling = 500;
            template.Columns[0].Roll = ReportRoll.Count;

            var back = ReportTemplate.FromJson(template.ToJson());

            Assert.Equal("Door schedule", back.Name);
            Assert.Equal("Fire rating", back.GroupBy);
            Assert.Equal("Name", back.SortBy);
            Assert.True(back.Descending);
            Assert.False(back.BlanksShown);
            Assert.Equal(500, back.Ceiling);
            Assert.Equal(ReportRoll.Count, back.Columns[0].Roll);

            Assert.Equal(template.Columns.Select(c => c.Heading), back.Columns.Select(c => c.Heading));
            Assert.Equal(template.Columns.Select(c => c.Editable), back.Columns.Select(c => c.Editable));

            var rating = back.Columns.Single(c => c.Heading == "Fire rating");

            Assert.Equal("Identity Data", rating.Source.Category);
        }

        [Fact]
        public void An_unreadable_template_comes_back_empty_rather_than_throwing()
        {
            var empty = ReportTemplate.FromJson(null);

            Assert.Empty(empty.Columns);
            Assert.Equal(ReportTemplate.DefaultCeiling, empty.Ceiling);
            Assert.NotNull(DataReport.Build(Doors(), empty).Refusal);
        }

        [Fact]
        public void A_copy_can_be_edited_without_disturbing_what_was_saved()
        {
            var saved = DataReport.Propose(Doors());
            var working = saved.Copy();

            working.Name = "Something else";
            working.Columns[0].Heading = "Renamed";
            working.Columns.RemoveAt(1);

            Assert.Equal("Model data", saved.Name);
            Assert.Equal("Name", saved.Columns[0].Heading);
            Assert.True(saved.Columns.Count > working.Columns.Count);
        }

        [Fact]
        public void Saving_a_template_under_a_name_already_used_replaces_it_rather_than_doubling_it()
        {
            var first = new ReportTemplate("Door schedule");
            first.Add(ReportColumn.Built(ReportField.Name));

            var kept = DataReport.Keep(DataReport.Saved(null), first);

            Assert.Single(DataReport.Saved(kept));

            var second = new ReportTemplate("door SCHEDULE");
            second.Add(ReportColumn.Built(ReportField.Category));

            var again = DataReport.Saved(DataReport.Keep(DataReport.Saved(kept), second));

            Assert.Single(again);
            Assert.Equal("Category", again[0].Columns[0].Heading);
        }

        [Fact]
        public void A_template_with_no_columns_is_not_worth_saving()
        {
            Assert.Empty(DataReport.Saved(DataReport.Keep(null, new ReportTemplate("Nothing"))));
        }

        // ---- Getting it out --------------------------------------------------------------

        [Fact]
        public void The_comma_separated_form_quotes_what_would_otherwise_break_it()
        {
            var awkward = new[]
            {
                Element("Door, wide", "Doors",
                        properties: new (string, string, string?)[] { ("Set", "Note", "he said \"no\"") }),
            };

            var template = new ReportTemplate("Awkward");
            template.Add(ReportColumn.Built(ReportField.Name));
            template.Add(ReportColumn.Of("Set", "Note"));
            template.WithIdentity = false;

            var csv = DataReport.Csv(DataReport.Build(awkward, template));

            Assert.Contains("\"Door, wide\"", csv, StringComparison.Ordinal);
            Assert.Contains("\"he said \"\"no\"\"\"", csv, StringComparison.Ordinal);
            Assert.Equal(2, csv.Split(new[] { "\r\n" }, StringSplitOptions.None).Length);
        }

        [Fact]
        public void There_is_no_report_to_write_when_there_was_no_report()
        {
            Assert.Equal(string.Empty, DataReport.Csv(null));
            Assert.Empty(DataReport.Sheet(null).Rows);
        }

        [Fact]
        public void The_explanation_counts_the_elements_behind_a_folded_report()
        {
            var template = new ReportTemplate("By rating");
            template.Add(ReportColumn.Of("Identity Data", "Fire rating"));
            template.GroupBy = "Fire rating";

            var text = DataReport.Build(Doors(), template).Explain();

            Assert.Contains("2 rows over 3 elements", text, StringComparison.Ordinal);
        }

        // ---- Through the seam ------------------------------------------------------------

        [Fact]
        public void An_element_read_through_the_seam_carries_everything_a_report_asks_of_it()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\Jobs\Riverside\Arch.nwc");

            document.AddItem(model, "Door 01", "Doors", "Single 900", default, true, "Riverside", "Level 02")
                    .WithProperty("Identity Data", "Mark", "D-01");

            var item = document.Traverse(TraversalScope.WholeDocument).Single();
            var element = ReportElement.Of(item);

            Assert.NotNull(element);
            Assert.Equal("Door 01", element!.Name);
            Assert.Equal("Doors", element.Category);
            Assert.Equal("Single 900", element.Type);
            Assert.Equal("Arch", element.Model);
            Assert.Equal("D-01", element.Value(new PropertyRef("Identity Data", "Mark")));
            Assert.True(element.Carries(new PropertyRef("Identity Data", "Mark")));
            Assert.False(element.Carries(new PropertyRef("Identity Data", "Missing")));

            Assert.Equal("Riverside > Level 02 > Door 01",
                ReportColumn.Built(ReportField.Path).Read(element));
        }

        [Fact]
        public void Nothing_read_through_the_seam_is_nothing_rather_than_a_crash()
        {
            Assert.Null(ReportElement.Of(null));
            Assert.Null(ReportColumn.Built(ReportField.Name).Read(null));
        }
    }
}
