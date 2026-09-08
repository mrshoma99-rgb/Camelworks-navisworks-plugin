using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;
using CamelWorks.Core.Publish;
using CamelWorks.Core.Store;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A folder of supplier drops, turned into the file the project ships.
    ///
    /// The hard part is not the writing but knowing what goes in: the delivery folder holds three
    /// revisions of half the models, two of them superseded and none of them saying so except in the
    /// file name. So the list is read, the revisions are worked out, older ones lose, and the plan
    /// states what is going in and what was left out before anything runs.
    ///
    /// It takes the session over. Navisworks has one document and no call that builds a file on the
    /// side, so compiling clears what is open, appends the sources and saves. That is stated in
    /// front of the button every time.
    /// </summary>
    public static class CompileWindow
    {
        /// <summary>Where saved compilations live in the project file.</summary>
        public const string Key = Compilation.StoreKey;

        private const string Combined = "one file holding all of them";
        private const string Separate = "one file each";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Compiler(session).View;

        private sealed class Compiler
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            private readonly List<CompileRecipe> _saved = new List<CompileRecipe>();
            private readonly List<string> _paths = new List<string>();

            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _shape = Ui.Stack();
            private readonly StackPanel _verdict = Ui.Stack();
            private readonly StackPanel _run = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            private readonly TextBox _folder;
            private readonly TextBox _sheet;
            private readonly TextBox _name;

            private CompileRecipe _recipe = new CompileRecipe();
            private ComboBox? _picker;
            private bool _understood;

            internal Compiler(Session session)
            {
                _session = session;
                _kept = ToolMemory.Of(session.Store, "publish.compile");

                _folder = Ui.Text(_kept.Text("out"), "where the files go",
                                  text => _kept.Set("out", text), Ui.Wide.Phrase);

                _sheet = Ui.Text(_kept.Text("sheet"), "a spreadsheet of paths",
                                 text => _kept.Set("sheet", text), Ui.Wide.Phrase);

                _name = Ui.Text(_kept.Text("name", "Weekly federation"), null, Ui.Wide.Phrase);

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Three decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns, which meant the button that closes whatever is open
            /// and overwrites a file on the network sat beside an empty file list. The dependency
            /// is real: there is nothing to name until there are files, and nothing worth warning
            /// about until there is somewhere for the result to land.
            ///
            /// The warning about what compiling costs lives on the last step, next to the button
            /// it is about, and the button does not appear until the box beside it is ticked.
            ///
            /// Each step's surface is built once and handed back on every visit, so going back to
            /// add one more file does not clear the folder already chosen.
            /// </summary>
            private UIElement Build()
            {
                var input = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Actions(
                            Ui.Button("Add files...", AddFiles),
                            Ui.Quiet("Add a folder...", AddFolder),
                            Ui.Quiet("Clear the list", Clear)),
                        Ui.PathField("Spreadsheet", _sheet, PathKind.Open,
                                     "Spreadsheet|*.xlsx;*.xlsm|All files|*.*",
                                     "a register the project already keeps, if there is one"),
                        Ui.Actions(Ui.Quiet("Read the paths", FromSheet))),
                    _list,
                    Bench.Group("This compilation", Saved()));

                var output = Ui.Stack(
                    _shape,
                    Bench.Group("What would happen", _verdict));

                var run = Ui.Stack(Warning(), _run, _note);

                Draw();

                var steps = new[]
                {
                    new Ui.Step("What goes in", () => input,
                                () => _paths.Count == 0
                                      ? "Put at least one model on the list." : null),
                    new Ui.Step("What comes out", () => output,
                                () => _folder.Text.Trim().Length == 0
                                      ? "Say which folder the compiled file goes in." : null),
                    new Ui.Step("Run it", () => run),
                };

                return Ui.Steps(steps, null, null);
            }

            /// <summary>
            /// The thing that must be read before the button works.
            ///
            /// Not a folded disclosure and not a footnote. Compiling closes whatever is open, and a
            /// person who finds that out afterwards has lost an afternoon of viewpoints and
            /// overrides that were never saved.
            /// </summary>
            private UIElement Warning()
            {
                var tick = Ui.Check("I have saved what is open. Go ahead and close it.", false, on =>
                {
                    _understood = on;
                    DrawRun();
                });

                return Ui.Stack(
                    Ui.Note("Navisworks has one document open at a time, and no way to build a file "
                            + "on the side. Compiling clears what is open, appends the models on "
                            + "the list and saves the result - so whatever is open now, including "
                            + "anything unsaved, is gone."),
                    tick);
            }

            private UIElement Saved()
            {
                var names = _saved.Select(c => c.Name).ToList();

                if (names.Count == 0) names.Add(_recipe.Name);

                _picker = Ui.Choice(names, _recipe.Name, Switch, Ui.Wide.Phrase);

                return Ui.Stack(
                    Ui.Field("Saved", _picker, "Re-runs next week against whatever the folder holds."),
                    Ui.Field("Name", _name, "what a combined file is named after"),
                    Ui.Actions(
                        Ui.Quiet("Save this one", Keep),
                        Ui.Quiet("Forget it", Forget)));
            }

            // -----------------------------------------------------------------------------------
            // The list
            // -----------------------------------------------------------------------------------

            private void AddFiles()
            {
                _note.Children.Clear();

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Models to compile",
                    Multiselect = true,
                    Filter = "Models Navisworks reads|"
                             + string.Join(";", Compilation.Readable.Select(e => "*" + e))
                             + "|All files|*.*",
                    CheckFileExists = true,
                };

                if (!Ui.Asked(dialog)) return;

                Took(dialog.FileNames);
            }

            private void AddFolder()
            {
                _note.Children.Clear();

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick any file in the delivery folder",
                    CheckFileExists = true,
                };

                if (!Ui.Asked(dialog)) return;

                var folder = Path.GetDirectoryName(dialog.FileName);

                if (folder == null) return;

                try
                {
                    Took(Directory.GetFiles(folder)
                                  .Where(f => Compilation.Readable.Contains(
                                      Compilation.ExtensionOf(Compilation.NameOf(f)),
                                      StringComparer.OrdinalIgnoreCase))
                                  .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                  .ToArray());
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not read that folder: " + Ui.Describe(e)));
                }
            }

            private void FromSheet()
            {
                _note.Children.Clear();

                var file = _sheet.Text.Trim();

                if (file.Length == 0)
                {
                    _note.Children.Add(Ui.Problem("Say which spreadsheet first."));
                    return;
                }

                WorkbookTables book;

                try
                {
                    book = XlsxReader.Read(File.ReadAllBytes(file));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                if (book.Problem != null)
                {
                    _note.Children.Add(Ui.Problem(book.Problem));
                    return;
                }

                var found = book.Sheets.SelectMany(s => Compilation.Paths(s)).ToList();

                if (found.Count == 0)
                {
                    _note.Children.Add(Ui.Note("Nothing in that workbook looks like a path to a "
                        + "model. CamelWorks looks for the column with the most file names in it, "
                        + "whatever the heading says."));

                    return;
                }

                Took(found);

                _note.Children.Add(Ui.Done("Read " + Ui.Count(found.Count, "path") + " out of "
                    + Compilation.NameOf(file) + "."));
            }

            private void Took(IReadOnlyList<string> paths)
            {
                var added = 0;

                foreach (var path in paths)
                {
                    if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;

                    _paths.Add(path);
                    added++;
                }

                if (added == 0 && paths.Count > 0)
                    _note.Children.Add(Ui.Note("They are all on the list already."));

                Draw();
            }

            private void Clear()
            {
                _paths.Clear();
                _note.Children.Clear();

                Draw();
            }

            private IReadOnlyList<SourceFile> Files()
            {
                var files = new List<SourceFile>(_paths.Count);

                foreach (var path in _paths)
                {
                    var exists = false;
                    var bytes = 0L;
                    var ticks = 0L;

                    try
                    {
                        var info = new FileInfo(path);

                        exists = info.Exists;

                        if (exists)
                        {
                            bytes = info.Length;
                            ticks = info.LastWriteTimeUtc.Ticks;
                        }
                    }
                    catch (Exception)
                    {
                        // A path with a character the file system refuses, or one on a share that
                        // is not there. It is on the list and it is not readable, which is exactly
                        // what "missing" means to the plan.
                        exists = false;
                    }

                    files.Add(new SourceFile(path, exists, bytes, ticks));
                }

                return files;
            }

            // -----------------------------------------------------------------------------------
            // Drawing
            // -----------------------------------------------------------------------------------

            private void Draw()
            {
                DrawList();
                DrawShape();
                DrawVerdict();
                DrawRun();
            }

            private void DrawList()
            {
                _list.Children.Clear();

                if (_paths.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("Nothing on the list yet.",
                        "Add the models to compile, point at the delivery folder, or read the paths "
                        + "out of the register."));

                    return;
                }

                var files = Files();

                var rows = files.Select(f => new TableRow(f,
                    Compilation.NameOf(f.Path),
                    f.Revision.IsNone ? "—" : f.Revision.Text,
                    !f.Exists ? "not there" : !f.IsReadable ? "not a model" : Size(f.Bytes))
                {
                    Tone = !f.Exists || !f.IsReadable ? Tone.Warn : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows, ("File", 230), ("Revision", 80), ("", 0));

                // Taking one file off had no way to happen. The list is built by pointing at a
                // delivery folder, so it arrives with last month's revision in it as often as not,
                // and the only control was "Clear the list" - start again, re-pick the folder,
                // re-read the register.
                _list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Take {n} off the list", Drop, danger: true),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "file"))));

                _list.Children.Add(Ui.Line(Ui.Count(_paths.Count, "file") + " on the list", 0.7));
            }

            /// <summary>Take the picked files off the list, and redraw everything that counted them.</summary>
            private void Drop(IReadOnlyList<object> picked, Panel say)
            {
                var gone = 0;

                foreach (var item in picked)
                {
                    if (!(item is SourceFile file)) continue;

                    var at = _paths.FindIndex(
                        path => string.Equals(path, file.Path, StringComparison.OrdinalIgnoreCase));

                    if (at < 0) continue;

                    _paths.RemoveAt(at);
                    gone++;
                }

                if (gone == 0)
                {
                    say.Children.Add(Ui.Note("Nothing was taken off."));
                    return;
                }

                // The shrunken list is the confirmation, and Draw rebuilds the panel this button
                // is in - so nothing is said here that would be thrown away a line later.
                Draw();
            }

            private void DrawShape()
            {
                _shape.Children.Clear();

                _shape.Children.Add(Ui.Stack(
                    Ui.Field("Produce",
                        Ui.Choice(new[] { Combined, Separate },
                                  _recipe.Mode == CompileMode.Individual ? Separate : Combined,
                                  pick =>
                                  {
                                      _recipe.Mode = pick == Separate
                                          ? CompileMode.Individual
                                          : CompileMode.Combine;
                                      DrawVerdict();
                                  }, Ui.Wide.Phrase)),
                    Ui.Field("Format",
                        Ui.Choice(Compilation.Writable, Chosen(), pick =>
                        {
                            _recipe.Format = pick;
                            DrawVerdict();
                        }, Ui.Wide.Word),
                        ".nwd holds the geometry; .nwf points at the sources and needs them to stay put"),
                    Ui.Field("Only the newest revision",
                        Ui.Toggle(string.Empty, _recipe.NewestOnly, on =>
                        {
                            _recipe.NewestOnly = on;
                            DrawVerdict();
                        }),
                        "Rev03 pushes Rev02 out. Off, and every revision on the list goes in."),
                    Ui.Field("Put the date in the name",
                        Ui.Toggle(string.Empty, _recipe.Stamped, on =>
                        {
                            _recipe.Stamped = on;
                            DrawVerdict();
                        }),
                        "Keeps the previous issue for comparison")));

                _shape.Children.Add(Ui.PathField("Folder", _folder, PathKind.Folder));
            }

            private void DrawVerdict()
            {
                _verdict.Children.Clear();

                _recipe.Name = Named();

                var plan = Compilation.Plan(Files(), _recipe, Today());

                if (plan.Refusal != null)
                {
                    _verdict.Children.Add(Ui.Note(plan.Refusal));
                    DrawRun();
                    return;
                }

                _verdict.Children.Add(Ui.Line(plan.Explain(), 1, true));

                _verdict.Children.Add(Ui.Across(
                    Ui.Figure(plan.Jobs.Count.ToString("N0", CultureInfo.InvariantCulture),
                              "files to write", Tone.Good),
                    Ui.Figure(plan.Sources.ToString("N0", CultureInfo.InvariantCulture), "models in"),
                    Ui.Figure(plan.Superseded.Count.ToString("N0", CultureInfo.InvariantCulture),
                              "older revisions out"),
                    Ui.Figure((plan.Missing.Count + plan.Unreadable.Count)
                                  .ToString("N0", CultureInfo.InvariantCulture),
                              "left out",
                              plan.Missing.Count + plan.Unreadable.Count > 0 ? Tone.Warn : Tone.Plain)));

                foreach (var note in plan.Notes) _verdict.Children.Add(Ui.Note(note));

                var jobs = Ui.Table(
                    plan.Jobs.Take(40).Select(j => new TableRow(j, j.Output,
                        j.Sources.Count.ToString("N0", CultureInfo.InvariantCulture),
                        Size(j.Bytes))).ToList(),
                    ("Comes out as", 300), ("Models", 80), ("", 0));

                _verdict.Children.Add(Ui.Picking(jobs,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(jobs, picked), say, "job"))));

                if (plan.Superseded.Count > 0)
                {
                    var older = Ui.Table(plan.Superseded.Take(60).Select(s => new TableRow(s,
                        Compilation.NameOf(s.Older.Path),
                        Compilation.NameOf(s.Newer.Path))).ToList(),
                        ("Left out", 240), ("Superseded by", 0));

                    _verdict.Children.Add(Ui.Eyebrow("The revisions being left out — "
                        + Ui.Count(plan.Superseded.Count, "file")));
                    _verdict.Children.Add(Ui.Picking(older,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(older, picked), say, "file"))));
                }

                if (plan.Missing.Count + plan.Unreadable.Count > 0)
                {
                    // This is the one table in the window whose contents get pasted into an email:
                    // it is the list the supplier has to be sent.
                    var refused = Ui.Table(
                        plan.Missing.Select(m => new TableRow(m, m.Path, "not there"))
                            .Concat(plan.Unreadable.Select(u => new TableRow(u, u.Path, "not a model")))
                            .Take(60).ToList(),
                        ("File", 320), ("", 0));

                    _verdict.Children.Add(Ui.Eyebrow("The files that cannot go in — "
                        + Ui.Count(plan.Missing.Count + plan.Unreadable.Count, "file")));
                    _verdict.Children.Add(Ui.Picking(refused,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(refused, picked), say, "file"))));
                }

                DrawRun();
            }

            private void DrawRun()
            {
                _run.Children.Clear();

                var plan = Compilation.Plan(Files(), _recipe, Today());

                if (!plan.IsUsable)
                {
                    _run.Children.Add(Ui.Small("There is nothing to compile yet."));
                    return;
                }

                if (_folder.Text.Trim().Length == 0)
                {
                    _run.Children.Add(Ui.Small("Say which folder the files go in, on the step before."));
                    return;
                }

                if (!_understood)
                {
                    _run.Children.Add(Ui.Small("Tick the box above once what is open has been saved."));
                    return;
                }

                _run.Children.Add(Ui.Runner(
                    "Compile " + Ui.Count(plan.Jobs.Count, "file"), _note, Run));
            }

            // -----------------------------------------------------------------------------------
            // Running it
            // -----------------------------------------------------------------------------------

            private void Run(Job job)
            {
                var folder = _folder.Text.Trim();
                var plan = Compilation.Plan(Files(), _recipe, Today());

                if (plan.Refusal != null)
                {
                    _note.Children.Add(Ui.Problem(plan.Refusal));
                    return;
                }

                var publish = new NavPublish(_session.Model);
                var results = new List<CompileResult>();
                var started = DateTime.UtcNow;

                foreach (var piece in plan.Jobs)
                {
                    var output = Path.Combine(folder, piece.Output);
                    var began = DateTime.UtcNow;

                    job.Say("Building " + piece.Output + "...");

                    var problem = publish.Compile(
                        piece.Sources.Select(s => s.Path).ToList(), output,
                        (done, path) => job.Step(done, piece.Sources.Count,
                                                 "models appended to " + piece.Output));

                    results.Add(new CompileResult(output, piece.Sources.Count,
                                                  (DateTime.UtcNow - began).TotalSeconds, problem));
                }

                // What is open now is whatever the last job left behind, which is not the model the
                // rest of CamelWorks was told about. Forgetting the session makes every screen
                // resolve it again rather than answering from a federation that is no longer there.
                Host.Forget();

                var log = Compilation.Log(plan, results,
                    started.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

                var written = Path.Combine(folder,
                    Compilation.FileName(_recipe.Name + " log", true, Today(), ".txt"));

                var wrote = true;

                try
                {
                    File.WriteAllText(written, log, new UTF8Encoding(true));
                }
                catch (Exception e)
                {
                    wrote = false;
                    _note.Children.Add(Ui.Problem("Could not write the log: " + Ui.Describe(e)));
                }

                var good = results.Count(r => r.Worked);

                _note.Children.Add(Ui.Line(
                    good == results.Count
                        ? "Wrote " + Ui.Count(good, "file") + "."
                        : good.ToString("N0", CultureInfo.InvariantCulture) + " of "
                          + Ui.Count(results.Count, "file") + " written.",
                    1, true));

                foreach (var result in results.Where(r => !r.Worked))
                    _note.Children.Add(Ui.Problem(Compilation.NameOf(result.Output) + " — "
                                                  + (result.Problem ?? "it did not say why")));

                if (wrote) _note.Children.Add(Ui.Small("The run is written down in "
                    + Compilation.NameOf(written) + ", beside what it produced."));

                _note.Children.Add(Ui.Note("What was open before this has been replaced by the last "
                    + "file compiled. Open the federation you work in again before carrying on."));
            }

            // -----------------------------------------------------------------------------------
            // Saving
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                _saved.Clear();
                _saved.AddRange(Compilation.Saved(_session.Store.Section(ProjectStore.DataSection)[Key]));

                var last = _kept.Text("compilation");

                var wanted = last == null
                    ? _saved.FirstOrDefault()
                    : _saved.FirstOrDefault(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _recipe = wanted.Copy();

                if (_recipe.Folder != null) _folder.Text = _recipe.Folder;
            }

            private void Switch(string name)
            {
                var wanted = _saved.FirstOrDefault(
                    c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _recipe = wanted.Copy();
                _name.Text = _recipe.Name;

                if (_recipe.Folder != null) _folder.Text = _recipe.Folder;

                _kept.Set("compilation", _recipe.Name);
                _kept.Keep();

                _note.Children.Clear();

                Draw();
            }

            private void Keep()
            {
                _note.Children.Clear();

                _recipe.Name = Named();
                _recipe.Folder = _folder.Text.Trim().Length == 0 ? null : _folder.Text.Trim();

                var written = Compilation.Keep(_saved, _recipe);

                _session.Store.Section(ProjectStore.DataSection).Set(Key, written);

                _saved.Clear();
                _saved.AddRange(Compilation.Saved(written));

                _kept.Set("compilation", _recipe.Name);
                _kept.Keep();

                _session.Record(ActivityKind.Setup, "Saved the compilation \"" + _recipe.Name + "\"",
                                _recipe.ToString());

                _note.Children.Add(Ui.Done("Saved \"" + _recipe.Name + "\". The list of files is not "
                    + "saved with it - the point is that next week's folder holds different ones."));

                Repick();
            }

            private void Forget()
            {
                _note.Children.Clear();

                var name = _recipe.Name;

                var left = _saved
                    .Where(c => !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (left.Count == _saved.Count)
                {
                    _note.Children.Add(Ui.Note("\"" + name + "\" was never saved, so there is "
                                               + "nothing to forget."));
                    return;
                }

                _session.Store.Section(ProjectStore.DataSection)
                        .Set(Key, JsonValue.Array(left.Select(c => c.ToJson())));

                _saved.Clear();
                _saved.AddRange(left);

                _note.Children.Add(Ui.Done("Forgot \"" + name + "\". It is still open here."));

                Repick();
            }

            private void Repick()
            {
                var picker = _picker;
                if (picker == null) return;

                picker.Items.Clear();

                foreach (var recipe in _saved) picker.Items.Add(recipe.Name);

                if (_saved.Count == 0) picker.Items.Add(_recipe.Name);

                picker.SelectedItem = _recipe.Name;
            }

            // -----------------------------------------------------------------------------------
            // Words
            // -----------------------------------------------------------------------------------

            private string Named()
            {
                var text = _name.Text.Trim();

                return text.Length == 0 ? "Weekly federation" : text;
            }

            private string Chosen() =>
                Compilation.Writable.Contains(_recipe.Format, StringComparer.OrdinalIgnoreCase)
                    ? _recipe.Format
                    : Compilation.Nwd;

            private static string Today() =>
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            private static string Size(long bytes)
            {
                if (bytes <= 0) return "—";

                var mb = bytes / 1024.0 / 1024.0;

                return mb < 1
                    ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
                    : mb.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            }
        }
    }
}
