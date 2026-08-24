using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class LabelSheetTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Mech.nwc", name);

        private static FieldElement Element(string name, string? mark = null) =>
            new FieldElement(Key(name), name, new BoundingBox(0, 0, 0, 1, 1, 1), true, mark);

        // ---- what it makes with nothing set up ------------------------------------------------

        [Fact]
        public void With_nothing_set_up_every_element_gets_a_label()
        {
            var run = LabelSheet.Make(
                new[] { Element("Pipe 1", "PIPE-0221"), Element("Pipe 2", "PIPE-0222") },
                new LabelRecipe());

            Assert.True(run.IsUsable);
            Assert.Equal(2, run.Labels.Count);
            Assert.Equal("PIPE-0221", run.Labels[0].Heading);
            Assert.Equal("PIPE-0221", run.Labels[0].Encoded);
            Assert.NotNull(run.Labels[0].Symbol);
        }

        [Fact]
        public void An_element_with_no_mark_is_labelled_by_its_name()
        {
            var run = LabelSheet.Make(new[] { Element("AHU-03") }, new LabelRecipe());

            Assert.Equal("AHU-03", Assert.Single(run.Labels).Heading);
        }

        [Fact]
        public void A_prefix_goes_in_the_code_and_the_bare_identity_stays_on_the_label()
        {
            var recipe = new LabelRecipe { Prefix = "https://bimcamel.com/e/" };
            var label = Assert.Single(LabelSheet.Make(new[] { Element("P", "PIPE-0221") }, recipe).Labels);

            Assert.Equal("PIPE-0221", label.Identity);
            Assert.Equal("https://bimcamel.com/e/PIPE-0221", label.Encoded);
            Assert.Equal("https://bimcamel.com/e/PIPE-0221", label.Symbol!.Text);
            Assert.Equal("PIPE-0221", label.Heading);
        }

        [Fact]
        public void Without_a_prefix_it_says_the_code_is_only_the_mark()
        {
            var run = LabelSheet.Make(new[] { Element("P", "PIPE-0221") }, new LabelRecipe());

            Assert.Contains("link into a document system", run.Explain(), StringComparison.Ordinal);
        }

        // ---- where the identity comes from ----------------------------------------------------

        [Fact]
        public void A_property_source_with_no_properties_read_refuses_rather_than_coming_back_empty()
        {
            var recipe = new LabelRecipe { Source = LabelSource.Property, PropertyName = "Asset" };
            var run = LabelSheet.Make(new[] { Element("P", "PIPE-0221") }, recipe);

            Assert.False(run.IsUsable);
            Assert.Contains("no properties have been read", run.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_property_source_reads_the_property_it_was_given()
        {
            var recipe = new LabelRecipe { Source = LabelSource.Property, PropertyName = "Asset" };
            var run = LabelSheet.Make(new[] { Element("P", "PIPE-0221") }, recipe, e => "ASSET-99");

            Assert.Equal("ASSET-99", Assert.Single(run.Labels).Identity);
        }

        [Fact]
        public void The_key_source_gives_every_element_something_even_when_it_is_nameless()
        {
            var recipe = new LabelRecipe { Source = LabelSource.Key };
            var run = LabelSheet.Make(new[] { Element("(unnamed)") }, recipe);

            Assert.True(run.IsUsable);
            Assert.NotEmpty(Assert.Single(run.Labels).Identity);
        }

        // ---- what it refuses to pretend --------------------------------------------------------

        [Fact]
        public void Two_elements_with_the_same_mark_are_reported_not_quietly_printed_twice()
        {
            var run = LabelSheet.Make(new[]
            {
                Element("Pipe 1", "PIPE-0221"),
                Element("Pipe 2", "PIPE-0221"),
                Element("Pipe 3", "PIPE-0222"),
            }, new LabelRecipe());

            Assert.Equal(3, run.Labels.Count);
            Assert.Equal(new[] { "PIPE-0221" }, run.Repeated);
            Assert.Contains("cannot say which", run.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_identity_too_long_for_any_symbol_is_left_off_and_counted()
        {
            var recipe = new LabelRecipe { Level = QrLevel.Highest };
            var run = LabelSheet.Make(new[]
            {
                Element(new string('X', 2000)),
                Element("Fine", "OK-1"),
            }, recipe);

            Assert.Equal(1, run.Oversize);
            Assert.Single(run.Labels);
            Assert.Contains("would not fit in any symbol", run.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_selected_is_refused_with_a_reason()
        {
            var run = LabelSheet.Make(Array.Empty<FieldElement>(), new LabelRecipe());

            Assert.False(run.IsUsable);
            Assert.Contains("nothing selected", run.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_ceiling_stops_the_run_and_reports_what_it_dropped()
        {
            var many = Enumerable.Range(0, 30).Select(i => Element("E" + i, "M-" + i)).ToList();
            var run = LabelSheet.Make(many, new LabelRecipe { Ceiling = 10 });

            Assert.Equal(10, run.Labels.Count);
            Assert.Equal(20, run.Dropped);
            Assert.Contains("ceiling", run.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        // ---- the sheet -------------------------------------------------------------------------

        [Fact]
        public void The_sheet_holds_one_label_and_one_symbol_for_each_element()
        {
            var run = LabelSheet.Make(
                new[] { Element("A", "M-1"), Element("B", "M-2"), Element("C", "M-3") },
                new LabelRecipe());

            var html = LabelSheet.Html(run, "Riverside");

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.EndsWith("</html>", html);
            Assert.Equal(3, Count(html, "class=\"label\""));
            Assert.Equal(3, Count(html, "<svg "));
            Assert.Contains("Riverside", html, StringComparison.Ordinal);
        }

        [Fact]
        public void The_sheet_is_measured_in_the_millimetres_it_was_asked_for()
        {
            var recipe = new LabelRecipe { LabelWidth = 70, LabelHeight = 37, SymbolWidth = 30 };
            var html = LabelSheet.Html(LabelSheet.Make(new[] { Element("A", "M-1") }, recipe));

            Assert.Contains("width:70mm", html, StringComparison.Ordinal);
            Assert.Contains("height:37mm", html, StringComparison.Ordinal);
            Assert.Contains("width:30mm", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_mark_with_markup_in_it_cannot_break_the_page()
        {
            var run = LabelSheet.Make(new[] { Element("P", "<b>M&1</b>") }, new LabelRecipe());
            var html = LabelSheet.Html(run);

            Assert.Contains("&lt;b&gt;M&amp;1&lt;/b&gt;", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<b>M&1</b>", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_sheet_of_a_refusal_is_the_refusal_and_no_labels()
        {
            var html = LabelSheet.Html(LabelSheet.Make(Array.Empty<FieldElement>(), new LabelRecipe()));

            Assert.Equal(0, Count(html, "class=\"label\""));
            Assert.Contains("nothing selected", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_sheet_of_nothing_at_all_is_still_a_page()
        {
            var html = LabelSheet.Html(null);

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.EndsWith("</html>", html);
        }

        // ---- the index -------------------------------------------------------------------------

        [Fact]
        public void The_index_has_a_header_and_a_row_for_every_label()
        {
            var run = LabelSheet.Make(
                new[] { Element("A", "M-1"), Element("B", "M-2") },
                new LabelRecipe { Prefix = "https://bimcamel.com/e/" });

            var lines = LabelSheet.Csv(run)
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("Identity,Encoded,Element,Model,Version,Level", lines[0]);
            Assert.Equal(3, lines.Length);
            Assert.Contains("https://bimcamel.com/e/M-1", lines[1], StringComparison.Ordinal);
        }

        [Fact]
        public void A_mark_with_a_comma_in_it_does_not_shift_every_column_after_it()
        {
            var run = LabelSheet.Make(new[] { Element("P", "M-1, spare") }, new LabelRecipe());
            var line = LabelSheet.Csv(run)
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1];

            Assert.StartsWith("\"M-1, spare\",", line);
        }

        // ---- the recipe -------------------------------------------------------------------------

        [Fact]
        public void A_recipe_survives_being_written_down_and_read_back()
        {
            var recipe = new LabelRecipe("Riser labels")
            {
                Source = LabelSource.Property,
                PropertyName = "Asset",
                PropertyCategory = "Element",
                Prefix = "https://bimcamel.com/e/",
                Level = QrLevel.Highest,
                LabelWidth = 70,
                LabelHeight = 37,
                SymbolWidth = 28,
                ShowName = false,
                Ceiling = 500,
            };

            var back = LabelRecipe.FromJson(recipe.ToJson());

            Assert.Equal("Riser labels", back.Name);
            Assert.Equal(LabelSource.Property, back.Source);
            Assert.Equal("Asset", back.PropertyName);
            Assert.Equal("https://bimcamel.com/e/", back.Prefix);
            Assert.Equal(QrLevel.Highest, back.Level);
            Assert.Equal(70, back.LabelWidth);
            Assert.Equal(28, back.SymbolWidth);
            Assert.False(back.ShowName);
            Assert.Equal(500, back.Ceiling);
        }

        [Fact]
        public void A_symbol_wider_than_the_label_is_brought_back_inside_it()
        {
            var recipe = new LabelRecipe { LabelWidth = 40, LabelHeight = 20, SymbolWidth = 90 };
            var back = LabelRecipe.FromJson(recipe.ToJson());

            Assert.True(back.SymbolWidth <= back.LabelHeight);
        }

        [Fact]
        public void An_unreadable_recipe_comes_back_working()
        {
            var back = LabelRecipe.FromJson(null);

            Assert.Equal(LabelSource.Mark, back.Source);
            Assert.True(back.LabelWidth > 0);
            Assert.True(back.SymbolWidth > 0);
            Assert.Equal(LabelRecipe.DefaultCeiling, back.Ceiling);
        }

        [Fact]
        public void A_copy_is_its_own()
        {
            var recipe = new LabelRecipe("Risers") { Prefix = "a" };
            var copy = recipe.Copy();

            copy.Prefix = "b";
            copy.Name = "Something else";

            Assert.Equal("a", recipe.Prefix);
            Assert.Equal("Risers", recipe.Name);
        }

        private static int Count(string text, string needle) =>
            text.Split(new[] { needle }, StringSplitOptions.None).Length - 1;
    }
}
