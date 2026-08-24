using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class DataLinkTests
    {
        private static SheetTable Sheet(params string?[][] rows) =>
            new SheetTable("Register", rows.Select(r => (IReadOnlyList<string?>)r).ToList());

        private static ReportElement Element(string name,
                                             params (string Category, string Name, string? Value)[] properties)
        {
            var key = ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Mech.nwc", name);

            return new ReportElement(key, name, "Mechanical", "Pump", "Mech", new[] { "Mech", name },
                                     properties.Select(p => new PropertyValue(p.Category, p.Name, p.Value)));
        }

        private static IReadOnlyList<ReportElement> Plant()
        {
            return new[]
            {
                Element("Pump 01", ("Identity Data", "Asset Tag", "P-101"),
                                   ("Identity Data", "Duty", "Duty")),
                Element("Pump 02", ("Identity Data", "Asset Tag", "P-102"),
                                   ("Identity Data", "Duty", "Standby")),
                Element("Pump 03", ("Identity Data", "Asset Tag", "P-103")),
            };
        }

        private static SheetTable Register() =>
            Sheet(
                new string?[] { "Asset No.", "Supplier", "Order", "Duty" },
                new string?[] { "P-101", "Grundfos", "PO-8841", "Duty" },
                new string?[] { "P-102", "Grundfos", "PO-8841", "Standby" },
                new string?[] { "P-103", "Wilo", "PO-9002", "Standby" });

        // ---- Reading the sheet -----------------------------------------------------------

        [Fact]
        public void The_headings_come_back_trimmed_and_without_the_empty_ones()
        {
            var sheet = Sheet(new string?[] { " Tag ", null, "Supplier", "   " });

            Assert.Equal(new[] { "Tag", "Supplier" }, DataLink.Headings(sheet));
            Assert.Empty(DataLink.Headings(null));
        }

        // ---- Working the join out --------------------------------------------------------

        [Fact]
        public void A_sheet_CamelWorks_exported_is_joined_on_the_identity_it_put_there()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { SheetImport.KeyHeading, "Supplier" },
                new string?[] { scope[0].Key.ToString(), "Grundfos" });

            var link = DataLink.Propose(sheet, scope);

            Assert.Equal(LinkMatch.Identity, link.Match);
            Assert.Equal(SheetImport.KeyHeading, link.KeyHeading);
            Assert.DoesNotContain(link.Columns, c => c.Heading == SheetImport.KeyHeading);
        }

        [Fact]
        public void Somebody_elses_register_is_joined_on_whichever_column_turns_up_in_the_model()
        {
            // The point of the whole tool: nothing in this file knows CamelWorks exists, and the
            // key column is not called what the property is called.
            var link = DataLink.Propose(Register(), Plant());

            Assert.Equal(LinkMatch.Property, link.Match);
            Assert.Equal("Asset No.", link.KeyHeading);
            Assert.Equal("Asset Tag", link.KeyProperty.Name);
            Assert.Equal("Identity Data", link.KeyProperty.Category);
        }

        [Fact]
        public void A_column_the_model_recognises_every_value_of_is_still_not_the_key_if_it_repeats()
        {
            // "Duty" reads Duty/Standby/Standby, and every one of those values is in the model - so
            // on match rate alone it scores exactly as highly as the asset number. It is not a key:
            // it does not identify a row. Put it first so leftmost-wins cannot rescue the answer.
            var sheet = Sheet(
                new string?[] { "Duty", "Asset No." },
                new string?[] { "Duty", "P-101" },
                new string?[] { "Standby", "P-102" },
                new string?[] { "Standby", "P-103" });

            var link = DataLink.Propose(sheet, Plant());

            Assert.Equal("Asset No.", link.KeyHeading);
            Assert.Equal("Asset Tag", link.KeyProperty.Name);
        }

        [Fact]
        public void A_sheet_keyed_on_the_element_name_is_joined_on_that()
        {
            var sheet = Sheet(
                new string?[] { "Item", "Supplier" },
                new string?[] { "Pump 01", "Grundfos" },
                new string?[] { "Pump 02", "Grundfos" });

            var link = DataLink.Propose(sheet, Plant());

            Assert.Equal(LinkMatch.Name, link.Match);
            Assert.Equal("Item", link.KeyHeading);
        }

        [Fact]
        public void A_sheet_with_nothing_in_common_with_the_model_is_not_guessed_at()
        {
            var stranger = Sheet(
                new string?[] { "Invoice", "Amount" },
                new string?[] { "INV-1", "100" },
                new string?[] { "INV-2", "200" });

            var link = DataLink.Propose(stranger, Plant());

            Assert.Empty(link.Columns);

            var plan = DataLink.Plan(stranger, Plant(), link);

            Assert.NotNull(plan.Refusal);
            Assert.Contains("Work it out", plan.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_column_named_like_a_property_the_model_already_has_lands_on_that_property()
        {
            var link = DataLink.Propose(Register(), Plant());

            var duty = link.Columns.Single(c => c.Heading == "Duty");

            Assert.Equal("Identity Data", duty.Category);
            Assert.Equal("Duty", duty.Property);
        }

        [Fact]
        public void A_column_the_model_has_never_heard_of_lands_on_a_tab_of_our_own()
        {
            var supplier = DataLink.Propose(Register(), Plant()).Columns.Single(c => c.Heading == "Supplier");

            Assert.Equal(DataLink.DefaultTab, supplier.Category);
            Assert.Equal("Supplier", supplier.Property);
        }

        [Fact]
        public void A_mapped_column_defaults_its_property_to_its_heading_and_its_tab_to_ours()
        {
            var column = new LinkColumn("  Order  ");

            Assert.Equal("Order", column.Heading);
            Assert.Equal("Order", column.Property);
            Assert.Equal(DataLink.DefaultTab, column.Category);
            Assert.Equal(LinkAction.Write, column.Action);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void An_empty_sheet_and_an_empty_scope_each_say_which_it_is()
        {
            Assert.Contains("nothing in that sheet",
                DataLink.Plan(null, Plant(), DataLink.Propose(Register(), Plant())).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("no elements in scope",
                DataLink.Plan(Register(), Array.Empty<ReportElement>(),
                              DataLink.Propose(Register(), Plant())).Refusal ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_key_column_that_is_not_in_this_sheet_is_named_rather_than_silently_missed()
        {
            var link = DataLink.Propose(Register(), Plant());

            var renamed = Sheet(
                new string?[] { "Tag", "Supplier" },
                new string?[] { "P-101", "Grundfos" });

            var plan = DataLink.Plan(renamed, Plant(), link);

            Assert.NotNull(plan.Refusal);
            Assert.Contains("\"Asset No.\"", plan.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void Matching_on_a_property_without_saying_which_property_is_refused()
        {
            var link = DataLink.Propose(Register(), Plant());
            link.KeyProperty = default;

            Assert.Contains("no property has been named",
                DataLink.Plan(Register(), Plant(), link).Refusal ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public void A_link_where_every_column_is_set_to_be_read_writes_nothing_and_says_so()
        {
            var link = DataLink.Propose(Register(), Plant());

            foreach (var column in link.Columns) column.Action = LinkAction.Read;

            Assert.Contains("Send back",
                DataLink.Plan(Register(), Plant(), link).Refusal ?? string.Empty, StringComparison.Ordinal);
        }

        // ---- What it would write ---------------------------------------------------------

        [Fact]
        public void Every_mapped_column_becomes_one_property_with_a_value_per_element()
        {
            var scope = Plant();
            var plan = DataLink.Plan(Register(), scope, DataLink.Propose(Register(), scope));

            Assert.Null(plan.Refusal);
            Assert.True(plan.IsUsable);
            Assert.Equal(3, plan.Rows);
            Assert.Equal(3, plan.Matched);
            Assert.Equal(3, plan.Elements);

            var supplier = plan.Writes.Single(w => w.Property == "Supplier");

            Assert.Equal(DataLink.DefaultTab, supplier.Category);
            Assert.Equal(3, supplier.Count);
            Assert.Equal("Grundfos", supplier.For(scope[0].Key));
            Assert.Equal("Wilo", supplier.For(scope[2].Key));
            Assert.True(supplier.Mentions(scope[1].Key));
        }

        [Fact]
        public void Filling_a_gap_leaves_an_element_that_already_holds_something_alone()
        {
            var scope = Plant();
            var link = DataLink.Propose(Register(), scope);

            foreach (var column in link.Columns) column.Action = LinkAction.Fill;

            var duty = DataLink.Plan(Register(), scope, link).Writes.Single(w => w.Property == "Duty");

            // Pumps 1 and 2 already carry a duty; only pump 3's gap gets filled.
            Assert.Equal(1, duty.Count);
            Assert.False(duty.Mentions(scope[0].Key));
            Assert.Equal("Standby", duty.For(scope[2].Key));
        }

        [Fact]
        public void Writing_over_replaces_what_is_there_where_filling_would_not()
        {
            var scope = Plant();
            var sheet = Sheet(
                new string?[] { "Asset No.", "Duty" },
                new string?[] { "P-101", "Standby" });

            var link = DataLink.Propose(Register(), scope);

            var duty = DataLink.Plan(sheet, scope, link).Writes.Single(w => w.Property == "Duty");

            Assert.Equal("Standby", duty.For(scope[0].Key));
        }

        [Fact]
        public void One_row_covering_several_elements_writes_to_all_of_them_and_is_counted()
        {
            // Ordinary in a register keyed by tag: one line item, a run of elements.
            var scope = new[]
            {
                Element("Pump 01", ("Identity Data", "Asset Tag", "P-101")),
                Element("Pump 01 motor", ("Identity Data", "Asset Tag", "P-101")),
            };

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" });

            var link = new DataLinkRecipe("Tags")
            {
                Match = LinkMatch.Property,
                KeyHeading = "Asset No.",
                KeyProperty = new PropertyRef("Identity Data", "Asset Tag"),
            };

            link.Add(new LinkColumn("Supplier"));

            var plan = DataLink.Plan(sheet, scope, link);

            Assert.Equal(1, plan.Matched);
            Assert.Equal(1, plan.Spread);
            Assert.Equal(2, plan.Elements);
            Assert.Contains("more than one element", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_row_about_something_not_in_the_model_is_kept_and_reported()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" },
                new string?[] { "P-999", "Somebody else" });

            var plan = DataLink.Plan(sheet, scope, DataLink.Propose(Register(), scope));

            Assert.Single(plan.Orphans);
            Assert.Equal("P-999", plan.Orphans[0].Key);
            Assert.Equal(3, plan.Orphans[0].Number);
            Assert.Contains("not in the model", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Elements_the_sheet_never_mentions_are_listed_rather_than_left_unsaid()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" });

            var plan = DataLink.Plan(sheet, scope, DataLink.Propose(Register(), scope));

            Assert.Equal(2, plan.Untouched.Count);
            Assert.Contains("left alone", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_row_with_nothing_in_the_key_column_is_passed_over_and_counted()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" },
                new string?[] { "   ", "Nobody" });

            var plan = DataLink.Plan(sheet, scope, DataLink.Propose(Register(), scope));

            Assert.Equal(1, plan.Rows);
            Assert.Contains(plan.Notes, n => n.Contains("nothing in the key column", StringComparison.Ordinal));
        }

        [Fact]
        public void Two_rows_claiming_the_same_element_are_reported_as_the_guess_they_are()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" },
                new string?[] { "P-101", "Wilo" });

            var plan = DataLink.Plan(sheet, scope, DataLink.Propose(Register(), scope));

            Assert.Contains(plan.Notes, n => n.Contains("more than one row", StringComparison.Ordinal));
            Assert.Equal("Wilo", plan.Writes.Single(w => w.Property == "Supplier").For(scope[0].Key));
        }

        [Fact]
        public void A_mapped_column_that_is_no_longer_in_the_sheet_is_named()
        {
            var scope = Plant();
            var link = DataLink.Propose(Register(), scope);

            var thinner = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "P-101", "Grundfos" });

            var plan = DataLink.Plan(thinner, scope, link);

            Assert.Contains(plan.Notes, n => n.Contains("\"Order\"", StringComparison.Ordinal));
            Assert.Contains(plan.Notes, n => n.Contains("not in this sheet any more", StringComparison.Ordinal));
        }

        [Fact]
        public void A_sheet_that_matches_nothing_says_where_to_look_rather_than_reporting_success()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Supplier" },
                new string?[] { "Q-1", "Somebody" });

            var plan = DataLink.Plan(sheet, scope, DataLink.Propose(Register(), scope));

            Assert.Equal(0, plan.Matched);
            Assert.False(plan.IsUsable);
            Assert.Contains("None of the", plan.Explain(), StringComparison.Ordinal);
        }

        // ---- The other direction ---------------------------------------------------------

        [Fact]
        public void Nothing_set_to_be_sent_back_is_refused_rather_than_writing_an_identical_file()
        {
            var scope = Plant();

            Assert.Contains("No column is set to be sent back",
                DataLink.Back(Register(), scope, DataLink.Propose(Register(), scope)).Refusal ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_models_value_goes_into_the_sheet_where_the_sheet_is_empty_or_disagrees()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Duty" },
                new string?[] { "P-101", null },        // empty  -> filled
                new string?[] { "P-102", "Duty" },      // wrong  -> changed
                new string?[] { "P-103", "Standby" });  // model holds nothing -> left alone

            var link = new DataLinkRecipe("Back")
            {
                Match = LinkMatch.Property,
                KeyHeading = "Asset No.",
                KeyProperty = new PropertyRef("Identity Data", "Asset Tag"),
            };

            link.Add(new LinkColumn("Duty", "Identity Data", "Duty", LinkAction.Read));

            var returned = DataLink.Back(sheet, scope, link);

            Assert.Null(returned.Refusal);
            Assert.True(returned.IsUsable);
            Assert.Equal(1, returned.Filled);
            Assert.Equal(1, returned.Changed);
            Assert.Equal(0, returned.Same);
            Assert.Equal(1, returned.Missing);
            Assert.Equal(new[] { "Duty" }, returned.Columns);

            Assert.Equal("Duty", returned.Sheet.Rows[1][1]);
            Assert.Equal("Standby", returned.Sheet.Rows[2][1]);
            Assert.Equal("Standby", returned.Sheet.Rows[3][1]);
        }

        [Fact]
        public void A_sheet_that_already_agrees_comes_back_saying_there_is_nothing_to_change()
        {
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Duty" },
                new string?[] { "P-101", "Duty" },
                new string?[] { "P-102", "Standby" });

            var link = new DataLinkRecipe("Back")
            {
                Match = LinkMatch.Property,
                KeyHeading = "Asset No.",
                KeyProperty = new PropertyRef("Identity Data", "Asset Tag"),
            };

            link.Add(new LinkColumn("Duty", "Identity Data", "Duty", LinkAction.Read));

            var returned = DataLink.Back(sheet, scope, link);

            Assert.Equal(2, returned.Same);
            Assert.False(returned.IsUsable);
            Assert.Contains("already agrees", returned.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Every_row_survives_the_trip_back_whether_it_matched_or_not()
        {
            // What comes out goes back over the file it came from. A returned register holding only
            // the rows that matched is not the register any more.
            var scope = Plant();

            var sheet = Sheet(
                new string?[] { "Asset No.", "Duty", "Notes" },
                new string?[] { "P-101", null, "keep me" },
                new string?[] { "Q-999", null, "not ours" });

            var link = new DataLinkRecipe("Back")
            {
                Match = LinkMatch.Property,
                KeyHeading = "Asset No.",
                KeyProperty = new PropertyRef("Identity Data", "Asset Tag"),
            };

            link.Add(new LinkColumn("Duty", "Identity Data", "Duty", LinkAction.Read));

            var returned = DataLink.Back(sheet, scope, link);

            Assert.Equal(3, returned.Sheet.Rows.Count);
            Assert.Equal("Asset No.", returned.Sheet.Rows[0][0]);
            Assert.Equal("keep me", returned.Sheet.Rows[1][2]);
            Assert.Equal("Q-999", returned.Sheet.Rows[2][0]);
            Assert.Equal("not ours", returned.Sheet.Rows[2][2]);
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void A_link_survives_being_written_down_and_read_back()
        {
            var link = DataLink.Propose(Register(), Plant());

            link.Name = "Asset register";
            link.File = @"C:\Jobs\Riverside\register.xlsx";
            link.Sheet = "Plant";
            link.Columns[0].Action = LinkAction.Read;

            var back = DataLinkRecipe.FromJson(link.ToJson());

            Assert.Equal("Asset register", back.Name);
            Assert.Equal(@"C:\Jobs\Riverside\register.xlsx", back.File);
            Assert.Equal("Plant", back.Sheet);
            Assert.Equal(LinkMatch.Property, back.Match);
            Assert.Equal("Asset No.", back.KeyHeading);
            Assert.Equal("Asset Tag", back.KeyProperty.Name);
            Assert.Equal("Identity Data", back.KeyProperty.Category);
            Assert.Equal(LinkAction.Read, back.Columns[0].Action);

            Assert.Equal(link.Columns.Select(c => c.Heading), back.Columns.Select(c => c.Heading));
            Assert.Equal(link.Columns.Select(c => c.Category), back.Columns.Select(c => c.Category));
        }

        [Fact]
        public void An_unreadable_link_comes_back_empty_rather_than_throwing()
        {
            var empty = DataLinkRecipe.FromJson(null);

            Assert.Empty(empty.Columns);
            Assert.Equal(SheetImport.KeyHeading, empty.KeyHeading);
            Assert.Equal(LinkMatch.Identity, empty.Match);
        }

        [Fact]
        public void A_copy_can_be_edited_without_disturbing_what_was_saved()
        {
            var saved = DataLink.Propose(Register(), Plant());
            var working = saved.Copy();

            working.Name = "Something else";
            working.Columns[0].Action = LinkAction.Ignore;

            Assert.NotEqual("Something else", saved.Name);
            Assert.Equal(LinkAction.Write, saved.Columns[0].Action);
        }

        [Fact]
        public void Saving_a_link_under_a_name_already_used_replaces_it_rather_than_doubling_it()
        {
            var first = new DataLinkRecipe("Asset register");
            first.Add(new LinkColumn("Supplier"));

            var kept = DataLink.Keep(DataLink.Saved(null), first);

            Assert.Single(DataLink.Saved(kept));

            var second = new DataLinkRecipe("ASSET REGISTER");
            second.Add(new LinkColumn("Order"));

            var again = DataLink.Saved(DataLink.Keep(DataLink.Saved(kept), second));

            Assert.Single(again);
            Assert.Equal("Order", again[0].Columns[0].Heading);
        }

        [Fact]
        public void A_link_with_nothing_mapped_is_not_worth_saving()
        {
            Assert.Empty(DataLink.Saved(DataLink.Keep(null, new DataLinkRecipe("Nothing"))));
        }
    }
}
