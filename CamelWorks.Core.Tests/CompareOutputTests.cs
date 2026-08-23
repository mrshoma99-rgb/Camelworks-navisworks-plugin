using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Report;
using CamelWorks.Core.Testing;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

namespace CamelWorks.Core.Tests
{
    public class CompareLayersTests
    {
        private static CompareResult Mixed()
        {
            var before = new[]
            {
                Print("Kept"),
                Print("Gone", x: 1),
                Print("Shifted", x: 2),
            };

            var after = new[]
            {
                Print("Kept"),
                Print("Shifted", x: 4),
                Print("New", x: 6),
            };

            return ModelCompare.Of(Snapshot("A", before), Snapshot("B", after));
        }

        [Fact]
        public void The_view_paints_a_layer_for_each_kind_of_change()
        {
            var view = CompareLayers.Build(Mixed());

            Assert.Contains(view.Layers, l => l.Id == CompareLayers.IdOf(ChangeBucket.Added));
            Assert.Contains(view.Layers, l => l.Id == CompareLayers.IdOf(ChangeBucket.Moved));
            Assert.Contains(view.Layers, l => l.Id == CompareLayers.IdOf(ChangeBucket.Removed));
        }

        [Fact]
        public void Everything_else_is_faded_from_the_bottom_of_the_stack()
        {
            var view = CompareLayers.Build(Mixed());

            Assert.Equal(CompareLayers.IdPrefix + "ghost", view.Layers[0].Id);
            Assert.Equal(ComparePalette.UnchangedTransparency, view.Layers[0].Transparency);
            Assert.Null(view.Layers[0].Colour);
        }

        [Fact]
        public void Fading_can_be_switched_off()
        {
            var view = CompareLayers.Build(Mixed(), ghostUnchanged: false);

            Assert.DoesNotContain(view.Layers, l => l.Id == CompareLayers.IdPrefix + "ghost");
        }

        [Fact]
        public void Removed_elements_are_counted_as_unpaintable_and_explained()
        {
            var view = CompareLayers.Build(Mixed());

            Assert.Equal(1, view.Uncolourable);
            Assert.Contains(view.Notes, n => n.Contains("Append the baseline model"));
        }

        [Fact]
        public void Each_bucket_gets_its_own_colour()
        {
            Assert.Equal(ComparePalette.Added, ComparePalette.Of(ChangeBucket.Added));
            Assert.Equal(ComparePalette.Removed, ComparePalette.Of(ChangeBucket.Removed));
            Assert.Null(ComparePalette.Of(ChangeBucket.Unchanged));
        }

        [Fact]
        public void A_compare_layer_can_be_told_apart_from_somebody_elses_work()
        {
            var view = CompareLayers.Build(Mixed());

            Assert.All(view.Layers, l => Assert.True(CompareLayers.IsCompareLayer(l)));
            Assert.False(CompareLayers.IsCompareLayer(
                new CamelWorks.Core.Appearance.AppearanceLayer("mine", "Mine",
                    CamelWorks.Core.Appearance.LayerTarget.Everything())));
        }

        [Fact]
        public void The_legend_carries_the_counts_a_screenshot_needs()
        {
            var legend = CompareLayers.Legend(Mixed());

            Assert.Contains(legend, r => r.Label == "Added" && r.Count == 1);
            Assert.Contains(legend, r => r.Label == "Removed" && r.Count == 1);
            Assert.Contains(legend, r => r.Label == "Moved" && r.Count == 1);
        }

        [Fact]
        public void Nothing_compared_is_an_answer_rather_than_an_empty_screen()
        {
            var view = CompareLayers.Build(null);

            Assert.True(view.IsEmpty);
            Assert.NotEmpty(view.Notes);
        }
    }

    public class CompareStampTests
    {
        private static CompareResult Mixed()
        {
            var before = new[] { Print("Kept"), Print("Gone", x: 1), Print("Shifted", x: 2) };
            var after = new[] { Print("Kept"), Print("Shifted", x: 4), Print("New", x: 6) };

            return ModelCompare.Of(Snapshot("Rev C", before), Snapshot("Rev D", after));
        }

        [Fact]
        public void The_plan_covers_what_changed_and_leaves_the_rest_alone()
        {
            var plan = CompareStamp.Plan(Mixed());

            Assert.Equal(2, plan.Count);
            Assert.Contains(plan.Rows, r => r.Bucket == ChangeBucket.Added);
            Assert.Contains(plan.Rows, r => r.Bucket == ChangeBucket.Moved);
            Assert.DoesNotContain(plan.Rows, r => r.Bucket == ChangeBucket.Unchanged);
        }

        [Fact]
        public void Removed_elements_cannot_be_stamped_and_the_plan_says_how_many()
        {
            var plan = CompareStamp.Plan(Mixed());

            Assert.Equal(1, plan.Unreachable);
            Assert.Contains("cannot", plan.Explain());
        }

        [Fact]
        public void Unchanged_elements_can_be_stamped_when_somebody_asks()
        {
            var plan = CompareStamp.Plan(Mixed(), includeUnchanged: true);

            Assert.Equal(3, plan.Count);
        }

        [Fact]
        public void Applying_queues_three_properties_an_element()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");
            var item = document.AddItem(model, "Wall");

            var print = ElementFingerprint.Of(item);
            var moved = ElementFingerprint.Of(item);
            moved.CentreX += 5;

            var result = ModelCompare.Of(Snapshot("Rev C", new[] { print }, Model(model.Scope, "Structure", model.SourcePath)),
                                         Snapshot("Rev D", new[] { moved }, Model(model.Scope, "Structure", model.SourcePath)));

            using var transaction = document.BeginWrite("stamp");

            var stamped = CompareStamp.Apply(transaction, CompareStamp.Plan(result));

            Assert.Equal(1, stamped);
            Assert.Equal(3, transaction.Preview().PropertyWrites);
        }

        [Fact]
        public void Clearing_queues_a_removal_for_every_property_the_stamp_wrote()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");
            var item = document.AddItem(model, "Wall");

            using var transaction = document.BeginWrite("clear");

            Assert.Equal(1, CompareStamp.Clear(transaction, new[] { item.Key }));
            Assert.Equal(3, transaction.Preview().PropertyRemovals);
        }

        [Fact]
        public void The_sets_a_stamp_makes_possible_are_named_for_the_comparison()
        {
            var sets = CompareStamp.Sets(Mixed());

            Assert.Contains(sets, s => s.Name.Contains("added") && s.Name.Contains("Rev C"));
            Assert.Contains(sets, s => s.Name.Contains("everything"));
        }

        [Fact]
        public void Carrying_the_property_at_all_means_something_changed()
        {
            // Which is why leaving the unchanged elements unstamped is the right default and not a
            // shortcut: absence is the answer.
            Assert.Contains("is present", CompareStamp.AnyChange.Describe());
            Assert.NotEqual(CompareStamp.AnyChange.Describe(), CompareStamp.For(ChangeBucket.Added).Describe());
        }
    }

    public class CompareIssuesTests
    {
        private static CompareResult Mixed()
        {
            var before = new[] { Print("Kept"), Print("Gone A", x: 1), Print("Gone B", x: 2) };
            var after = new[] { Print("Kept"), Print("New A", x: 6) };

            return ModelCompare.Of(Snapshot("Rev C", before), Snapshot("Rev D", after));
        }

        [Fact]
        public void One_parent_a_model_and_a_kind_of_change_with_the_places_underneath()
        {
            var findings = CompareIssues.Raise(Mixed());

            var removed = findings.Single(f => f.Title.Contains("removed"));

            Assert.True(removed.IsParent);
            Assert.Equal(2, removed.Children.Count);
            Assert.Equal(FindingSource.Compare, removed.Source);
        }

        [Fact]
        public void Moves_are_left_off_the_board_unless_they_are_asked_for()
        {
            var before = new[] { Print("Duct", x: 0) };
            var after = new[] { Print("Duct", x: 5) };

            var result = ModelCompare.Of(Snapshot("A", before), Snapshot("B", after));

            Assert.Empty(CompareIssues.Raise(result));
            Assert.Single(CompareIssues.Raise(result, new[] { ChangeBucket.Moved }));
        }

        [Fact]
        public void A_finding_keeps_its_identity_when_next_weeks_count_differs()
        {
            // The whole point of putting the model and the kind of change in the identity and
            // leaving the count out: 301 moved elements instead of 300 is the same thing to decide
            // about, and re-deriving the id would throw last week's sign-off away.
            var week1 = ModelCompare.Of(
                Snapshot("A", new[] { Print("A1"), Print("A2", x: 1) }),
                Snapshot("B", new List<ElementFingerprint>()));

            var week2 = ModelCompare.Of(
                Snapshot("A", new[] { Print("A1"), Print("A2", x: 1), Print("A3", x: 2) }),
                Snapshot("B", new List<ElementFingerprint>()));

            Assert.Equal(CompareIssues.Raise(week1)[0].Id, CompareIssues.Raise(week2)[0].Id);
        }

        [Fact]
        public void The_parent_title_carries_the_true_count_even_when_the_children_are_capped()
        {
            var before = Enumerable.Range(0, 20).Select(i => Print("Wall " + i, x: i)).ToList();

            var result = ModelCompare.Of(Snapshot("A", before), Snapshot("B", new List<ElementFingerprint>()),
                                         new CompareOptions { ExampleCap = 3 });

            var parent = CompareIssues.Raise(result).Single();

            Assert.Equal(3, parent.Children.Count);
            Assert.Contains("20 removed", parent.Title);
        }

        [Fact]
        public void One_change_can_be_raised_on_its_own()
        {
            var result = Mixed();
            var change = result.Changes.First(c => c.Bucket == ChangeBucket.Added);

            var finding = CompareIssues.One(change, "Rev C");

            Assert.NotNull(finding);
            Assert.Contains("since Rev C", finding!.Title);
            Assert.Single(finding.Elements);
        }
    }

    public class CompareReportTests
    {
        private static CompareResult Mixed()
        {
            var before = new[]
            {
                Print("Kept"),
                Print("Gone", x: 1),
                Print("Shifted", x: 2).With("Element", "Level", "L02"),
            };

            var after = new[]
            {
                Print("Kept"),
                Print("Shifted", x: 4).With("Element", "Level", "L03"),
                Print("New", x: 6),
            };

            return ModelCompare.Of(Snapshot("Rev C", before), Snapshot("Rev D", after));
        }

        [Fact]
        public void The_report_names_both_sides_and_the_settings_it_ran_under()
        {
            var document = CompareReport.Build(Mixed(), "Riverside");

            Assert.Equal("Model compare", document.Title);
            Assert.Contains("Rev C", document.Subtitle);
            Assert.Contains(document.CoverFacts, f => f.Key == "Project" && f.Value == "Riverside");
            Assert.Contains(document.CoverFacts, f => f.Key == "Settings" && f.Value!.Contains("model units"));
        }

        [Fact]
        public void The_legend_is_in_the_report_with_its_colours()
        {
            var legend = CompareReport.Build(Mixed()).Blocks.OfType<LegendBlock>().Single();

            Assert.Contains(legend.Entries, e => e.Label == "Added" && e.ColourHex == ComparePalette.Added.ToString());
        }

        [Fact]
        public void What_the_comparison_cannot_tell_you_is_in_the_report_and_not_only_on_screen()
        {
            var disclosure = CompareReport.Build(Mixed()).Blocks.OfType<DisclosureBlock>().SingleOrDefault();

            Assert.NotNull(disclosure);
            Assert.Contains("cannot tell you", disclosure!.Heading);
            Assert.NotEmpty(disclosure.Lines);
        }

        [Fact]
        public void The_evidence_for_the_matching_is_printed()
        {
            var tables = CompareReport.Build(Mixed()).Blocks.OfType<TableBlock>().ToList();

            Assert.Contains(tables, t => t.Headers.Contains("Matched on"));
        }

        [Fact]
        public void The_workbook_has_a_sheet_for_the_scoreboard_the_rows_the_models_and_the_notes()
        {
            var sheets = CompareReport.Workbook(Mixed());

            Assert.Equal(new[] { "Summary", "Changes", "By model", "Notes" }, sheets.Select(s => s.Name).ToArray());
            Assert.True(XlsxWriter.Write(sheets).Length > 0);
        }

        [Fact]
        public void A_property_change_gets_its_own_row_so_a_spreadsheet_can_filter_on_it()
        {
            var changes = CompareReport.Workbook(Mixed()).Single(s => s.Name == "Changes");

            Assert.Contains(changes.Rows, r => r.Count > 13 && r[13] == "Element / Level"
                                               && r[14] == "L02" && r[15] == "L03");
        }

        [Fact]
        public void The_csv_leads_with_a_header_and_quotes_what_needs_it()
        {
            var csv = CompareReport.Csv(Mixed());

            Assert.StartsWith("Change,Element,Type,Category,Model,What changed,", csv);
            Assert.Contains("Added,New,", csv);
        }

        [Fact]
        public void Nothing_compared_still_produces_a_document_that_says_so()
        {
            var document = CompareReport.Build(null);

            Assert.Contains("Nothing was compared", document.Subtitle);
        }
    }
}
