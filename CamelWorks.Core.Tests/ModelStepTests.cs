using System.Linq;
using CamelWorks.Core.Automation;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ModelStepTests
    {
        private static GraphNode Node(string id, string kind, string? path = null)
        {
            var node = new GraphNode(id, kind);
            if (path != null) node.Settings["path"] = path;
            return node;
        }

        [Fact]
        public void The_three_model_steps_are_in_the_catalogue_under_one_group()
        {
            foreach (var id in new[] { "model.append", "model.refresh", "model.save" })
            {
                var kind = NodeCatalogue.Find(id);

                Assert.NotNull(kind);
                Assert.Equal("Model", kind!.Group);
                Assert.False(string.IsNullOrWhiteSpace(kind.Title));
                Assert.False(string.IsNullOrWhiteSpace(kind.Summary));
            }

            Assert.Contains("Model", NodeCatalogue.Groups);
        }

        [Fact]
        public void Append_asks_the_host_for_the_file_it_was_given()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("a", "model.append", @"C:\jobs\structure.nwc"));

            var host = new FakeGraphHost();
            var run = GraphRunner.Run(graph, host);

            Assert.Equal(1, run.Ran);
            Assert.Contains(@"append C:\jobs\structure.nwc", host.Did);
            Assert.Contains("structure.nwc", run.Steps[0].Outcome);
        }

        [Fact]
        public void An_append_the_host_refuses_stops_the_run_and_names_the_file()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("a", "model.append", "missing.nwc"));
            graph.Nodes.Add(Node("r", "model.refresh"));

            var host = new FakeGraphHost();
            host.Unappendable.Add("missing.nwc");

            var run = GraphRunner.Run(graph, host);

            // Everything after it is about a federation that was meant to include this file, and a
            // clash report over three models where four were meant is worse than no report. These
            // steps carry no wires, so the skip-what-depends-on-it rule cannot reach them - the
            // run has to stop.
            Assert.Equal(0, run.Ran);
            Assert.Single(run.Steps);
            Assert.Contains("missing.nwc", run.Steps[0].Outcome);
            Assert.Contains("missing.nwc", string.Join(" ", run.Problems));
            Assert.Contains("Nothing after this step was run", string.Join(" ", run.Problems));
            Assert.DoesNotContain("refresh", host.Did);
        }

        [Fact]
        public void An_append_with_no_file_named_is_a_problem_rather_than_a_no_op()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("a", "model.append", "   "));

            var run = GraphRunner.Run(graph, new FakeGraphHost());

            Assert.Equal(0, run.Ran);
            Assert.Contains("no file named", run.Steps[0].Outcome);
            Assert.Contains("no file named", string.Join(" ", run.Problems));
        }

        [Fact]
        public void A_refresh_that_found_nothing_new_is_an_answer_and_not_a_failure()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("r", "model.refresh"));

            var host = new FakeGraphHost { AnythingChanged = false };
            var run = GraphRunner.Run(graph, host);

            // The answer an overnight refresh gives on the morning after nobody published. A run
            // that failed there would page somebody for a quiet week.
            Assert.Equal(1, run.Ran);
            Assert.Empty(run.Problems);
            Assert.Contains("nothing on disk had changed", run.Steps[0].Outcome);
        }

        [Fact]
        public void A_refresh_that_reloaded_something_says_so()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("r", "model.refresh"));

            var run = GraphRunner.Run(graph, new FakeGraphHost { AnythingChanged = true });

            Assert.Contains("reloaded", run.Steps[0].Outcome);
        }

        [Fact]
        public void Save_hands_the_path_straight_through_so_the_extension_decides()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("s", "model.save", @"C:\jobs\federation.nwd"));

            var host = new FakeGraphHost();
            var run = GraphRunner.Run(graph, host);

            Assert.Equal(1, run.Ran);
            Assert.Contains(@"save C:\jobs\federation.nwd", host.Did);
        }

        [Fact]
        public void A_save_with_no_file_named_is_a_problem()
        {
            var graph = new Graph();
            graph.Nodes.Add(Node("s", "model.save"));

            var run = GraphRunner.Run(graph, new FakeGraphHost());

            Assert.Equal(0, run.Ran);
            Assert.Contains("no file named", run.Steps[0].Outcome);
        }

        [Fact]
        public void A_step_that_is_not_foundational_does_not_stop_the_run()
        {
            var graph = new Graph();

            // A colour node with nothing wired in is skipped, not failed - and the refresh after it
            // still runs. Only the federation-assembly steps stop everything.
            graph.Nodes.Add(new GraphNode("c", "view.colour"));
            graph.Nodes.Add(Node("r", "model.refresh"));

            var host = new FakeGraphHost();
            var run = GraphRunner.Run(graph, host);

            Assert.Equal(2, run.Steps.Count);
            Assert.Equal(1, run.Ran);
            Assert.Contains("refresh", host.Did);
        }

        [Fact]
        public void Only_the_model_steps_are_foundational()
        {
            var foundational = NodeCatalogue.All.Where(k => k.Foundational).Select(k => k.Id).ToList();

            Assert.Equal(new[] { "model.append", "model.refresh", "model.save" },
                         foundational.OrderBy(id => id, System.StringComparer.Ordinal));
        }

        [Fact]
        public void The_monday_morning_job_runs_end_to_end()
        {
            var graph = new Graph("Weekly coordination");

            graph.Nodes.Add(Node("a1", "model.append", "arch.nwc"));
            graph.Nodes.Add(Node("a2", "model.append", "struct.nwc"));
            graph.Nodes.Add(Node("r", "model.refresh"));
            graph.Nodes.Add(Node("run", "clash.run"));

            var board = new GraphNode("board", "clash.board");
            var report = new GraphNode("report", "report.write");
            report.Settings["path"] = "out.pdf";
            report.Settings["format"] = "PDF";
            report.Settings["title"] = "Weekly coordination";

            graph.Nodes.Add(board);
            graph.Nodes.Add(report);
            graph.Connect(new GraphWire("board", "out", "report", "in"));

            var host = new FakeGraphHost(20);
            var run = GraphRunner.Run(graph, host);

            Assert.Empty(run.Problems);
            Assert.Equal(6, run.Ran);

            Assert.Equal(new[] { "append arch.nwc", "append struct.nwc", "refresh" },
                         host.Did.Take(3).ToArray());

            Assert.Contains(host.Did, d => d.StartsWith("report", System.StringComparison.Ordinal));
        }

        [Fact]
        public void A_saved_job_survives_the_round_trip_with_its_paths()
        {
            var graph = new Graph("Weekly coordination");
            graph.Nodes.Add(Node("a1", "model.append", @"\\server\job\arch.nwc"));
            graph.Nodes.Add(Node("s", "model.save", @"\\server\job\federation.nwd"));

            var read = Graph.FromJson(graph.ToJson());

            Assert.NotNull(read);
            Assert.Equal("Weekly coordination", read!.Name);
            Assert.Equal(@"\\server\job\arch.nwc", read.Find("a1")!.Setting("path"));
            Assert.Equal(@"\\server\job\federation.nwd", read.Find("s")!.Setting("path"));
        }
    }
}
