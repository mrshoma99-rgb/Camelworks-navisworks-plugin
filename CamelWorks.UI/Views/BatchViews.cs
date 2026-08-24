using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CamelWorks.Core.Automation;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.UI.Views
{
    /// <summary>The Batch workspace: saved jobs, run without opening the canvas, and what they did.</summary>
    public static class BatchViews
    {
        /// <summary>Jobs and their run history.</summary>
        public static UIElement Jobs()
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Jobs"), Ui.Sub(Host.NoModel)));

            var log = Ui.Stack();
            var graphs = Saved(session);
            Graph? picked = graphs.FirstOrDefault();

            var body = Ui.Stack();

            if (graphs.Count == 0)
            {
                body.Children.Add(Ui.Empty("No jobs saved on this project.",
                    "A job is a graph and the settings it runs with. Build one on the Automate canvas and "
                    + "press Save job; it goes in the project file, so whoever opens the model next has it too."));
            }
            else
            {
                var rows = graphs.Select(g => new TableRow(g,
                    g.Name,
                    Ui.Count(g.Nodes.Count, "node"),
                    string.Join(", ", g.Nodes.Select(n => n.Definition?.Title ?? n.Kind).Distinct().Take(4)))).ToList();

                body.Children.Add(Ui.Table(rows, ("Job", 170), ("Size", 90), ("What it does", 0))
                    .OnPick(item => picked = item as Graph));

                body.Children.Add(Ui.Runner("Run the selected job", log, job =>
                {
                    if (picked == null)
                    {
                        log.Children.Add(Ui.Line("Pick a job first.", 0.8));
                        return;
                    }

                    var result = GraphRunner.Run(picked, new NavGraphHost(session), job.Say);

                    log.Children.Add(Ui.Line(picked.Name + " — " + result, 1, true));

                    foreach (var problem in result.Problems) log.Children.Add(Ui.Problem(problem));

                    foreach (var step in result.Steps)
                        log.Children.Add(Ui.Line((step.Ran ? "ran  " : "skip ") + step.Title + " — " + step.Outcome,
                            step.Ran ? 0.85 : 0.6));

                    session.Record(ActivityKind.Job, "ran \"" + picked.Name + "\": " + result);
                }));

                body.Children.Add(Ui.Note("A job runs on the model that is open now, using whatever is selected "
                    + "now. Nothing in it is scheduled or unattended: Navisworks has to be running, and so do you."));
            }

            var history = session.Store.Activity.Entries
                .Where(e => e.Kind == ActivityKind.Job)
                .Take(20)
                .ToList();

            var past = history.Count == 0
                ? (UIElement)Ui.Empty("Nothing has run yet.", "The result of every run is kept here.")
                : Ui.Table(history.Select(e => new TableRow(e,
                        e.Summary,
                        ActivityLog.Ago(e.WhenTicks, session.NowTicks),
                        e.Detail ?? string.Empty)).ToList(),
                    ("Run", 300), ("When", 120), ("", 0));

            return Ui.Scroll(Ui.Stack(
                FromThisDocument(session),
                Ui.Card("Jobs", null, body),
                log,
                Ui.Card("History", null, past)));
        }

        /// <summary>
        /// Turn the open federation into a runnable job, in one press.
        ///
        /// The blank-canvas problem, answered by not starting from a blank canvas. What somebody
        /// actually wants the first time is the job they are already doing by hand every Monday:
        /// append these models, reload them, run the tests, write the report. All four facts are
        /// on screen already - the models are loaded, their paths are known - so asking somebody
        /// to type them into a graph is asking them to copy out something the tool can see.
        ///
        /// It is a starting point and says so. The canvas is where it gets changed.
        /// </summary>
        private static UIElement FromThisDocument(Session session)
        {
            var status = Ui.Stack();

            var sources = session.Model.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.SourcePath))
                .Select(m => m.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var name = Ui.Text("Weekly coordination", null, 220);

            var make = Ui.Button("Make a job from what is open", () =>
            {
                status.Children.Clear();

                if (sources.Count == 0)
                {
                    status.Children.Add(Ui.Empty("Nothing to build a job from.",
                        "No loaded model has a source file on disk - a document assembled and never "
                        + "saved has nothing for a job to re-append. Save it first."));
                    return;
                }

                var graph = Build(session, name.Text.Trim(), sources);

                var section = session.Store.Section(ProjectStore.JobsSection);

                var items = section["items"].Kind == JsonKind.Array
                    ? section["items"].Items.ToList()
                    : new List<JsonValue>();

                items.Add(graph.ToJson());
                section.Set("items", JsonValue.Array(items));

                if (!session.Store.Save())
                {
                    status.Children.Add(Ui.Problem(session.Store.LastSaveProblem
                                                   ?? "Could not write the project file."));
                    return;
                }

                status.Children.Add(Ui.Done("Saved \"" + graph.Name + "\" - "
                    + Ui.Count(graph.Nodes.Count, "step") + ", starting with "
                    + Ui.Count(sources.Count, "model") + " to append."));

                status.Children.Add(Ui.Note("As it stands it appends those models, reloads anything that "
                    + "changed on disk, runs the clash tests that have never run, and writes the board as "
                    + "a PDF beside the project file. Open it on the Automate canvas to change that."));

                status.Children.Add(Ui.Note("Run it on an empty document. Append adds to what is already "
                    + "loaded rather than replacing it, so running this in the session that made it gives "
                    + "you every model twice - and a clash test on a model against its own copy. "
                    + "File > New, then run it."));

                session.Record(ActivityKind.Job, "made \"" + graph.Name + "\" from the open document");
            });

            var body = Ui.Stack(
                Ui.Line(sources.Count == 0
                    ? "No loaded model has a source file on disk."
                    : Ui.Count(sources.Count, "model") + " loaded, all with a file to re-append:", 0.8));

            foreach (var path in sources.Take(6))
                body.Children.Add(Ui.Line(System.IO.Path.GetFileName(path), 0.6));

            if (sources.Count > 6)
                body.Children.Add(Ui.Line("and " + Ui.Count(sources.Count - 6, "more") + ".", 0.6));

            return Ui.Card("Job from this document",
                "The Monday morning job, written out from what is already open.",
                body,
                Ui.Field("Call it", name),
                Ui.Across(make),
                status);
        }

        /// <summary>The four steps, laid out left to right so the canvas reads in run order.</summary>
        private static Graph Build(Session session, string name, IReadOnlyList<string> sources)
        {
            var graph = new Graph(string.IsNullOrWhiteSpace(name) ? "Job from this document" : name);

            var y = 40.0;

            foreach (var path in sources)
            {
                var node = new GraphNode(graph.NextId("model.append"), "model.append") { X = 40, Y = y };
                node.Settings["path"] = path;
                graph.Nodes.Add(node);

                y += 70;
            }

            graph.Nodes.Add(new GraphNode(graph.NextId("model.refresh"), "model.refresh") { X = 300, Y = 40 });
            graph.Nodes.Add(new GraphNode(graph.NextId("clash.run"), "clash.run") { X = 540, Y = 40 });

            var board = new GraphNode(graph.NextId("clash.board"), "clash.board") { X = 780, Y = 40 };
            graph.Nodes.Add(board);

            var report = new GraphNode(graph.NextId("report.write"), "report.write") { X = 1020, Y = 40 };

            // Beside the project file, because that is the one folder the job already knows exists
            // and can write to. A path in somebody's Documents folder works on one machine.
            report.Settings["path"] = Beside(session, graph.Name + ".pdf");
            report.Settings["format"] = "PDF";
            report.Settings["title"] = graph.Name;
            graph.Nodes.Add(report);

            graph.Connect(new GraphWire(board.Id, "out", report.Id, "in"));

            return graph;
        }

        /// <summary>A path next to the project file, or just the name when there is no project file.</summary>
        private static string Beside(Session session, string file)
        {
            var project = session.Store.Path;

            if (string.IsNullOrWhiteSpace(project)) return file;

            var folder = System.IO.Path.GetDirectoryName(project);

            return string.IsNullOrEmpty(folder) ? file : System.IO.Path.Combine(folder!, file);
        }

        private static IReadOnlyList<Graph> Saved(Session session)
        {
            var section = session.Store.Section(ProjectStore.JobsSection);
            if (section["items"].Kind != JsonKind.Array) return Array.Empty<Graph>();

            return section["items"].Items.Select(Graph.FromJson).ToList();
        }
    }
}
