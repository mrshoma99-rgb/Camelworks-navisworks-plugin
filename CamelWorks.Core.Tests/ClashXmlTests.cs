using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashXmlTests
    {
        private static ClashItem Item(string name, string test = "L03 MEP v STR",
                                      FindingStatus status = FindingStatus.New,
                                      double x = 0, double? distance = null)
        {
            var item = ClashFixture.Item(name, x: x, test: test);
            item.Status = status;
            item.Distance = distance;
            item.Grid = "C4";
            item.Zone = "Plant";
            return item;
        }

        private static ClashGroup Group(string name, params ClashItem[] members)
        {
            var group = new ClashGroup(name.ToLowerInvariant(), name);
            foreach (var member in members) group.Items.Add(member);
            return group;
        }

        // -----------------------------------------------------------------------------------
        // Out and back
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_file_we_wrote_comes_back_pointing_at_the_same_elements()
        {
            var items = new[]
            {
                Item("a", status: FindingStatus.Reviewed, distance: -0.05),
                Item("b", x: 4, status: FindingStatus.Active),
            };

            var written = ClashXml.Write(items);
            var read = ClashXml.Read(written.Text);

            Assert.Null(read.Problem);
            Assert.True(read.IsUsable);
            Assert.Equal(0, read.Unidentified);

            Assert.Equal(items.Select(i => i.Key), read.Items.Select(i => i.Key));
            Assert.Equal(new[] { FindingStatus.Reviewed, FindingStatus.Active },
                         read.Items.Select(i => i.Status));

            var first = read.Items[0];

            Assert.Equal("L03 MEP v STR", first.TestName);
            Assert.Equal("L03", first.Level);
            Assert.Equal("C4", first.Grid);
            Assert.Equal("Plant", first.Zone);
            Assert.Equal("ARCH", first.ModelA);
            Assert.Equal("MEP", first.ModelB);
            Assert.Equal(-0.05, first.Distance!.Value, 6);
        }

        [Fact]
        public void The_position_survives_the_round_trip()
        {
            var item = Item("a", x: 12.5);
            item.Y = -3.25;
            item.Z = 7;

            var read = ClashXml.Read(ClashXml.Write(new[] { item }).Text);
            var back = Assert.Single(read.Items);

            Assert.Equal(12.5, back.X, 6);
            Assert.Equal(-3.25, back.Y, 6);
            Assert.Equal(7, back.Z, 6);
        }

        [Fact]
        public void An_overlap_and_a_crossing_angle_survive_as_smart_tags()
        {
            var item = Item("a");
            item.OverlapVolume = 0.0125;
            item.CrossingAngleDegrees = 62.5;

            var back = Assert.Single(ClashXml.Read(ClashXml.Write(new[] { item }).Text).Items);

            Assert.Equal(0.0125, back.OverlapVolume!.Value, 6);
            Assert.Equal(62.5, back.CrossingAngleDegrees!.Value, 3);
        }

        [Fact]
        public void The_test_folder_travels_with_the_test()
        {
            var item = Item("a");
            item.TestFolder = "Level 03";

            var written = ClashXml.Write(new[] { item });

            Assert.Equal("Level 03", Assert.Single(written.Tests).Folder);
            Assert.Equal("Level 03", Assert.Single(ClashXml.Read(written.Text).Items).TestFolder);
        }

        // -----------------------------------------------------------------------------------
        // Folders
        // -----------------------------------------------------------------------------------

        [Fact]
        public void The_folders_somebody_spent_an_afternoon_on_are_in_the_file()
        {
            var a = Item("a");
            var b = Item("b", x: 4);
            var loose = Item("c", x: 8);

            var written = ClashXml.Write(new[] { a, b, loose }, new[] { Group("Ducts v beams", a, b) });

            Assert.Contains("<clashgroup name=\"Ducts v beams\">", written.Text, StringComparison.Ordinal);
            Assert.Equal(1, Assert.Single(written.Tests).Groups);

            var read = ClashXml.Read(written.Text);

            Assert.Equal(new string?[] { "Ducts v beams", "Ducts v beams", null },
                         read.Results.Select(r => r.Group));

            // A folder a reviewer worked in is a folder that must come back, or the afternoon is
            // spent again.
            Assert.Equal("Ducts v beams", read.Items[0].CarriedGroup);
            Assert.Null(read.Items[2].CarriedGroup);
        }

        [Fact]
        public void Asking_for_it_flat_gets_it_flat()
        {
            var a = Item("a");
            var b = Item("b", x: 4);

            var written = ClashXml.Write(new[] { a, b }, new[] { Group("Ducts v beams", a, b) },
                                         new ClashXmlOptions { KeepGroups = false });

            Assert.DoesNotContain("<clashgroup", written.Text, StringComparison.Ordinal);
            Assert.Equal(0, Assert.Single(written.Tests).Groups);
            Assert.All(ClashXml.Read(written.Text).Results, r => Assert.Null(r.Group));
        }

        [Fact]
        public void A_folder_reaching_two_tests_is_written_under_both_and_said_out_loud()
        {
            var a = Item("a", test: "Hard");
            var b = Item("b", test: "Clearance", x: 4);

            var written = ClashXml.Write(new[] { a, b }, new[] { Group("Level 03", a, b) });

            Assert.Equal(2, written.Tests.Count);
            Assert.All(written.Tests, t => Assert.Equal(1, t.Groups));

            var warning = Assert.Single(written.Warnings);
            Assert.Contains("more than one test", warning, StringComparison.Ordinal);
            Assert.Contains("Level 03", warning, StringComparison.Ordinal);
        }

        [Fact]
        public void A_flat_export_says_nothing_about_folders_it_was_not_asked_to_keep()
        {
            var a = Item("a", test: "Hard");
            var b = Item("b", test: "Clearance", x: 4);

            var written = ClashXml.Write(new[] { a, b }, new[] { Group("Level 03", a, b) },
                                         new ClashXmlOptions { KeepGroups = false });

            Assert.Empty(written.Warnings);
        }

        // -----------------------------------------------------------------------------------
        // What the file says about itself
        // -----------------------------------------------------------------------------------

        [Fact]
        public void The_summary_counts_every_status_the_format_knows()
        {
            var items = new[]
            {
                Item("a", status: FindingStatus.New),
                Item("b", x: 4, status: FindingStatus.New),
                Item("c", x: 8, status: FindingStatus.Resolved),
            };

            var written = ClashXml.Write(items);

            Assert.Contains("new=\"2\"", written.Text, StringComparison.Ordinal);
            Assert.Contains("resolved=\"1\"", written.Text, StringComparison.Ordinal);
            Assert.Contains("total=\"3\"", written.Text, StringComparison.Ordinal);

            var test = Assert.Single(written.Tests);

            Assert.Equal(3, test.Total);
            Assert.Equal(2, test.Counts[FindingStatus.New]);
        }

        [Fact]
        public void A_test_where_nothing_touched_is_a_clearance_test()
        {
            var clear = ClashXml.Write(new[]
            {
                Item("a", distance: 0.04), Item("b", x: 4, distance: 0.02),
            });

            Assert.Equal(MatrixTest.Clearance, Assert.Single(clear.Tests).Kind);
            Assert.Contains("test_type=\"clearance\"", clear.Text, StringComparison.Ordinal);

            var hard = ClashXml.Write(new[]
            {
                Item("a", distance: -0.04), Item("b", x: 4, distance: 0.02),
            });

            Assert.Equal(MatrixTest.Hard, Assert.Single(hard.Tests).Kind);
        }

        [Fact]
        public void What_the_caller_knows_beats_what_the_numbers_suggest()
        {
            var options = new ClashXmlOptions();
            options.Kinds["L03 MEP v STR"] = MatrixTest.Duplicate;

            var written = ClashXml.Write(new[] { Item("a", distance: 0.04) }, null, options);

            Assert.Equal(MatrixTest.Duplicate, Assert.Single(written.Tests).Kind);
            Assert.Contains("test_type=\"duplicates\"", written.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_clash_keeps_the_same_id_every_export()
        {
            // A reviewer's tool tracks results by id. A fresh one each export turns last week's
            // reviewed clash into this week's new one.
            var first = ClashXml.Write(new[] { Item("a") }).Text;
            var second = ClashXml.Write(new[] { Item("a") }).Text;

            Assert.Equal(Guid(first), Guid(second));
            Assert.NotEqual(Guid(first), Guid(ClashXml.Write(new[] { Item("b", x: 4) }).Text));
        }

        private static string Guid(string xml)
        {
            var at = xml.IndexOf("guid=\"", StringComparison.Ordinal) + 6;
            return xml.Substring(at, xml.IndexOf('"', at) - at);
        }

        // -----------------------------------------------------------------------------------
        // Files somebody else wrote
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_file_from_another_tool_reads_as_a_record_rather_than_being_refused()
        {
            const string foreign = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<exchange units=""m"">
  <batchtest name=""Clash Detective"" internal_name=""clashtests"" units=""m"">
    <clashtests>
      <clashtest name=""MEP vs STR"" test_type=""hard"" status=""ok"" tolerance=""0.010000"">
        <clashresults>
          <clashresult name=""Clash1"" status=""active"" distance=""-0.032000"">
            <clashpoint><pos3f x=""1.5"" y=""2.5"" z=""3.5"" /></clashpoint>
            <gridlocation>C-4 : Level 2</gridlocation>
          </clashresult>
          <clashresult name=""Clash2"" status=""approved"">
            <clashpoint><pos3f x=""0"" y=""0"" z=""0"" /></clashpoint>
          </clashresult>
        </clashresults>
      </clashtest>
    </clashtests>
  </batchtest>
</exchange>";

            var read = ClashXml.Read(foreign);

            Assert.Null(read.Problem);
            Assert.True(read.IsUsable);

            Assert.Equal(new[] { "Clash1", "Clash2" }, read.Results.Select(r => r.Name));
            Assert.Equal(new[] { FindingStatus.Active, FindingStatus.Approved },
                         read.Results.Select(r => r.Status));
            Assert.Equal(1.5, read.Results[0].X, 6);

            // Nothing in it names elements we have, so nothing carries over - but the file is read.
            Assert.Empty(read.Items);
            Assert.Equal(2, read.Unidentified);
            Assert.Contains("No result in that file carries a CamelWorks key",
                            Assert.Single(read.Warnings), StringComparison.Ordinal);

            var test = Assert.Single(read.Tests);

            Assert.Equal("MEP vs STR", test.Name);
            Assert.Equal(MatrixTest.Hard, test.Kind);
            Assert.Equal(0.01, test.Tolerance, 6);
        }

        [Fact]
        public void A_file_half_ours_says_how_much_of_it_landed()
        {
            var ours = ClashXml.Write(new[] { Item("a") }).Text;

            var mixed = ours.Replace("</clashresults>",
                "<clashresult name=\"Stranger\" status=\"new\">"
                + "<clashpoint><pos3f x=\"0\" y=\"0\" z=\"0\" /></clashpoint>"
                + "</clashresult></clashresults>");

            var read = ClashXml.Read(mixed);

            Assert.Equal(2, read.Results.Count);
            Assert.Single(read.Items);
            Assert.Equal(1, read.Unidentified);
            Assert.Contains("1 of 2 results", Assert.Single(read.Warnings), StringComparison.Ordinal);
        }

        [Fact]
        public void Junk_is_refused_in_a_sentence_that_says_what_is_wrong()
        {
            Assert.Contains("nothing in that file", ClashXml.Read(null).Problem!,
                            StringComparison.OrdinalIgnoreCase);
            Assert.Contains("nothing in that file", ClashXml.Read("   ").Problem!,
                            StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not valid XML", ClashXml.Read("<exchange><oops").Problem!,
                            StringComparison.Ordinal);
            Assert.Contains("no <clashtest>", ClashXml.Read("<report><row /></report>").Problem!,
                            StringComparison.Ordinal);
            Assert.Contains("holds no clash tests",
                            ClashXml.Read("<exchange units=\"m\" />").Problem!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_refused_file_is_not_usable_and_says_only_why()
        {
            var read = ClashXml.Read("<report />");

            Assert.False(read.IsUsable);
            Assert.Empty(read.Results);
            Assert.Empty(read.Items);
            Assert.Equal(read.Problem, read.Explain());
        }

        // -----------------------------------------------------------------------------------
        // The edges
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Nothing_to_write_is_still_a_file()
        {
            var written = ClashXml.Write(null);

            Assert.Equal(0, written.Results);
            Assert.Empty(written.Tests);
            Assert.Equal("Nothing to write.", written.Explain());

            Assert.Contains("<clashtests>", written.Text, StringComparison.Ordinal);
            Assert.Contains("holds no clash tests", ClashXml.Read(written.Text).Problem!,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void A_test_name_with_XML_in_it_does_not_break_the_file()
        {
            var written = ClashXml.Write(new[] { Item("a", test: "M&E <v> STR \"1\"") });

            Assert.Contains("M&amp;E &lt;v&gt; STR", written.Text, StringComparison.Ordinal);

            var read = ClashXml.Read(written.Text);

            Assert.Equal("M&E <v> STR \"1\"", Assert.Single(read.Tests).Name);
            Assert.Equal("M&E <v> STR \"1\"", Assert.Single(read.Items).TestName);
        }

        [Fact]
        public void Leaving_the_elements_out_makes_a_smaller_file_that_still_round_trips()
        {
            var lean = ClashXml.Write(new[] { Item("a") },
                                      null, new ClashXmlOptions { IncludeObjects = false });

            Assert.DoesNotContain("<clashobjects>", lean.Text, StringComparison.Ordinal);

            var back = Assert.Single(ClashXml.Read(lean.Text).Items);

            // The key is what identity rests on, and it is in the smart tag, not the objects.
            Assert.Equal(Item("a").Key, back.Key);
            Assert.Null(back.ModelA);
        }

        [Fact]
        public void A_date_is_written_when_one_is_given_and_left_out_when_it_is_not()
        {
            var stamped = ClashXml.Write(new[] { Item("a") }, null,
                                         new ClashXmlOptions { WhenTicks = 637_000_000_000_000_000L });

            Assert.Contains("<createddate>", stamped.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("<createddate>", ClashXml.Write(new[] { Item("a") }).Text,
                                  StringComparison.Ordinal);
        }

        [Fact]
        public void Tests_come_out_in_a_stable_order_whatever_order_the_results_arrived_in()
        {
            var forwards = ClashXml.Write(new[]
            {
                Item("a", test: "Zone A"), Item("b", x: 4, test: "Alpha"),
            });

            var backwards = ClashXml.Write(new[]
            {
                Item("b", x: 4, test: "Alpha"), Item("a", test: "Zone A"),
            });

            Assert.Equal(new[] { "Alpha", "Zone A" }, forwards.Tests.Select(t => t.Name));
            Assert.Equal(forwards.Text, backwards.Text);
        }

        [Fact]
        public void A_single_clashtest_element_reads_on_its_own()
        {
            // What somebody sends when they have pulled one test out of a bigger file.
            const string alone = @"<clashtest name=""One"" test_type=""clearance"" tolerance=""0.025"">
  <clashresults>
    <clashresult name=""Clash1"" status=""resolved"">
      <clashpoint><pos3f x=""1"" y=""1"" z=""1"" /></clashpoint>
    </clashresult>
  </clashresults>
</clashtest>";

            var read = ClashXml.Read(alone);

            Assert.Null(read.Problem);
            Assert.Equal(MatrixTest.Clearance, Assert.Single(read.Tests).Kind);
            Assert.Equal(FindingStatus.Resolved, Assert.Single(read.Results).Status);
        }
    }
}
