using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Dyncamelo graphs, run from here.
    ///
    /// <b>Why this replaced a canvas.</b> This app used to carry a node editor of its own — a
    /// smaller, weaker one than the editor Dyncamelo already is, maintained twice, teaching two
    /// vocabularies for one idea. Dyncamelo ships in the same install now, so the editing belongs
    /// there and this tab does the half the editor is a bad place for: pick a graph off the shelf
    /// and run it against the document that is open, without a canvas in the way.
    ///
    /// <b>What it does not do.</b> It does not edit. A graph that needs changing opens in
    /// Dyncamelo, which is one press away and is the tool for it. Nothing here re-implements a
    /// node, a port or a wire — the whole surface is a list of files, a button, and what the run
    /// had to say.
    /// </summary>
    public static class GraphViews
    {
        /// <summary>The tab.</summary>
        public static UIElement Build() => new Screen().Build();

        /// <summary>Where the folder a project keeps its graphs in is remembered.</summary>
        private const string FolderKey = "folder";

        private sealed class Screen
        {
            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();

            private ToolMemory? _kept;
            private TextBox? _folder;
            private string? _picked;

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

                _kept = ToolMemory.Of(session.Store, "automate.graphs");
                _folder = Ui.Text(_kept.Text(FolderKey) ?? Suggested(session), null, 240);

                var run = Ui.Runner("Run the graph", _results, job => Go(session, job));

                var foot = new WrapPanel();

                foot.Children.Add(run);
                foot.Children.Add(new Border { Width = 14 });

                foot.Children.Add(Ui.Icon(Glyphs.Folder, "Where the graphs are", () => Where()));

                foot.Children.Add(Ui.Icon(Glyphs.Pencil, "Open Dyncamelo to edit", OpenEditor));

                Draw();

                return Bench.Surface(null, foot, Ui.Stack(Head(), _list, _results));
            }

            /// <summary>The one thing set up per project: where the graphs are kept.</summary>
            private void Where()
            {
                var hint = "every " + NavDyncamelo.Extension + " file in it is listed";

                Ui.Configure("Graphs", Draw, new Ui.Page("Where the graphs are", () => Ui.Stack(
                    Ui.PathField("Folder", _folder!, PathKind.Folder, hint: hint),
                    Ui.Small("Kept with the project, so whoever opens the job next has the same "
                             + "shelf of graphs."))));
            }

            /// <summary>Where this tab stands before anything is pressed.</summary>
            private UIElement Head()
            {
                var why = NavDyncamelo.Unavailable;

                if (why != null)
                {
                    // Named rather than hidden: a tab that silently lists nothing is a tab
                    // somebody reports as broken.
                    return Ui.Problem(why);
                }

                return Bench.Ready(
                    "Runs a Dyncamelo graph against the document that is open, without opening "
                    + "the editor.",
                    "Every node reports below — what ran, what warned and what failed. Editing a "
                    + "graph is Dyncamelo's job, one press away.");
            }

            /// <summary>The graphs on the shelf, as a table with the verbs on the rows.</summary>
            private void Draw()
            {
                _list.Children.Clear();

                var where = (_folder?.Text ?? string.Empty).Trim();

                if (where.Length == 0 || !Directory.Exists(where))
                {
                    _list.Children.Add(Ui.Empty("No folder to read.",
                        "Say where this project keeps its graphs and every " + NavDyncamelo.Extension
                        + " file in it is listed here."));

                    return;
                }

                List<FileInfo> files;

                try
                {
                    files = new DirectoryInfo(where)
                        .GetFiles("*" + NavDyncamelo.Extension, SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception error)
                {
                    _list.Children.Add(Ui.Problem("That folder could not be read: " + Ui.Describe(error)));
                    return;
                }

                if (files.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("No graphs in that folder.",
                        "A graph is a " + NavDyncamelo.Extension + " file, saved out of Dyncamelo."));

                    return;
                }

                var table = Ui.Table(files.Select(f => new TableRow(f,
                        Path.GetFileNameWithoutExtension(f.Name),
                        f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        Bytes(f.Length))),
                    ("Graph", 260), ("Last saved", 140), ("Size", 0))
                    .OnPick(item => _picked = (item as FileInfo)?.FullName);

                _list.Children.Add(Ui.Eyebrow("On the shelf"));
                _list.Children.Add(table);

                _list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(picked.OfType<FileInfo>().Select(f => f.FullName),
                                                 say, "graph")),
                    new Ui.OnPicked("Open {n} in Dyncamelo", (picked, say) => OpenEditor())));

                _list.Children.Add(Ui.Small("Pick one and press Run the graph. The run works on "
                                            + "whatever is open and whatever is selected now."));

                _picked = files.Count == 1 ? files[0].FullName : _picked;
            }

            /// <summary>Run the picked graph and report every node.</summary>
            private void Go(Session session, Job job)
            {
                if (_picked == null || !File.Exists(_picked))
                {
                    _results.Children.Add(Ui.Note("Pick a graph in the list first."));
                    return;
                }

                var name = Path.GetFileNameWithoutExtension(_picked);

                job.Say("Running " + name + "...");

                // No progress steps: the engine runs the whole graph in one call and does not
                // report through. Saying which graph is running beats a bar that cannot move.
                var run = NavDyncamelo.Run(_picked);

                if (run.Problem != null)
                {
                    _results.Children.Add(Ui.Problem(run.Problem));
                    return;
                }

                _results.Children.Add(run.Errors > 0
                    ? Ui.Problem(run.Describe())
                    : Ui.Line(run.Describe(), 1, true));

                if (run.Nodes.Count > 0)
                {
                    var table = Ui.Table(run.Nodes.Select(n => new TableRow(n,
                            n.Name, n.State, n.Message)
                        {
                            Tone = n.Failed ? Tone.Bad : n.Warned ? Tone.Warn : Tone.Plain,
                        }),
                        ("Node", 240), ("State", 100), ("What it said", 0));

                    _results.Children.Add(Ui.Eyebrow("Every node"));
                    _results.Children.Add(table);

                    _results.Children.Add(Ui.Picking(table,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "node"))));
                }

                // A graph that writes into the model is the ordinary case, so the log records it
                // the way every other write in the product is recorded.
                session.Record(ActivityKind.Job, "ran the graph \"" + name + "\"", run.Describe());

                if (run.Errors > 0)
                {
                    _results.Children.Add(Ui.Actions(Ui.Quiet("Open it in Dyncamelo", OpenEditor)));
                }
            }

            /// <summary>Open the Dyncamelo editor, or say why it will not open.</summary>
            private void OpenEditor()
            {
                _results.Children.Clear();

                var problem = NavDyncamelo.Unavailable;

                if (problem != null)
                {
                    _results.Children.Add(Ui.Problem(problem));
                    return;
                }

                var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(EditorPaneKey);

                if (!(record is Autodesk.Navisworks.Api.Plugins.DockPanePluginRecord pane))
                {
                    _results.Children.Add(Ui.Problem(
                        "Dyncamelo's editor panel is not registered with this Navisworks, although "
                        + "its assemblies are installed. That is usually a build made for another "
                        + "release: a plug-in compiled for the wrong year loads and then fails to "
                        + "register its panel."));

                    return;
                }

                if (!pane.IsLoaded) pane.LoadPlugin();

                if (pane.LoadedPlugin is Autodesk.Navisworks.Api.Plugins.DockPanePlugin dock)
                {
                    dock.Visible = true;
                    _results.Children.Add(Ui.Line("Opened. It docks as its own panel.", 0.75));
                }
            }

            /// <summary>The dock pane Dyncamelo registers its editor as.</summary>
            private const string EditorPaneKey = "Dyncamelo.DockPane.DYNC";

            /// <summary>A file size somebody can read at a glance.</summary>
            private static string Bytes(long length) =>
                length >= 1024 * 1024
                    ? (length / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                    : (length / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";

            /// <summary>A folder beside the document, which is where a project's graphs belong.</summary>
            private static string Suggested(Session session)
            {
                try
                {
                    var where = session.Store.Where;

                    var folder = string.IsNullOrWhiteSpace(where)
                        ? null
                        : Path.GetDirectoryName(where);

                    return folder == null ? string.Empty : Path.Combine(folder, "graphs");
                }
                catch (Exception)
                {
                    // An unsaved document has nowhere beside it. Not an error - the box is the
                    // user's to fill in.
                    return string.Empty;
                }
            }
        }
    }
}
