using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The models this federation can no longer find, and what to do about them.
    ///
    /// A model whose source has moved still displays: Navisworks shows its own cached copy with no
    /// warning. Coordination then continues against geometry that is weeks out of date.
    ///
    /// There is no API to re-point an existing model at a new path. The document exposes append,
    /// merge and open; nothing replaces one model's source in place, and nothing removes a single
    /// model. So this window does the part that can be done properly — find where the files went and
    /// bring the right one in — and states what has to be finished by hand rather than implying a
    /// repoint it cannot perform.
    /// </summary>
    public static class FixLinksWindow
    {

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            Body(session);

        private static UIElement Body(Session session)
        {
            var results = Ui.Stack();
            var folder = Ui.Text(string.Empty, null, Ui.Wide.Phrase);

            var broken = Missing(session).Select(m => new Broken(m)).ToList();

            // The folder the healthy models live in is where a moved file most often went - a
            // republish landed beside the others while the federation still points at the old
            // path. Offered as a prefill, not a decision: the box stays the user's.
            if (broken.Count > 0) folder.Text = Suggest(session) ?? string.Empty;

            var look = Ui.Runner("Look for them", results,
                                 job => Search(session, folder, broken, results, job));

            var head = broken.Count == 0
                ? Ui.Line("Every loaded model's source file is where the federation expects it.", 1, true)
                : Ui.Problem(Ui.Count(broken.Count, "model")
                    + " cannot find its source file. Navisworks is showing you its own cached copy of "
                    + "each of them, with no warning anywhere, and it may be months old.");

            // The broken models are a table rather than a stack of sentences, because every one of
            // them is something to act on: a path to paste into an email, a file to bring in once
            // the search has found it. A line of text is a dead end; a row carries verbs.
            var list = Ui.Stack();

            if (broken.Count > 0) Draw(session, broken, list);

            var setup = Ui.Stack(
                Bench.Group(string.Empty,
                    Ui.PathField("Look in", folder, PathKind.Folder,
                                 hint: broken.Count > 0 && folder.Text.Length > 0
                                     ? "prefilled with where the healthy models live"
                                     : null)));

            var stage = Ui.Stack(
                head,
                list,
                results,
                Ui.Note("Navisworks offers no way for a plug-in to re-point an existing model at a new "
                        + "path: the document can append, merge and open, and nothing replaces one "
                        + "model's source in place or removes a single model. So bringing the right "
                        + "file in adds it alongside the stale one. Remove the stale one in the "
                        + "Selection Tree and save the federation, and the link is fixed properly - "
                        + "until you do, the board and every report still include the stale copy."));

            return Bench.Surface(setup, look, stage);
        }

        /// <summary>One model whose source has gone, and what the search made of it.</summary>
        private sealed class Broken
        {
            public Broken(IModelSource model)
            {
                Model = model;
            }

            /// <summary>The loaded model.</summary>
            public IModelSource Model { get; }

            /// <summary>Where the federation still expects the file.</summary>
            public string Expected => Model.SourcePath ?? string.Empty;

            /// <summary>Where the search found a file of the same name, if it did.</summary>
            public string? Match { get; set; }

            /// <summary>How many others of the same name were under the folder.</summary>
            public int Others { get; set; }

            /// <summary>Whether it has already been brought into the federation.</summary>
            public bool BroughtIn { get; set; }

            /// <summary>What the row says has become of it.</summary>
            public string Say =>
                BroughtIn ? "brought in"
                : Match != null ? "found" + (Others > 0 ? " (" + Others + " more of that name)" : string.Empty)
                : "not looked for yet";
        }

        /// <summary>Draw the broken models as a table with the verbs that apply to them.</summary>
        private static void Draw(Session session, IReadOnlyList<Broken> broken, Panel into)
        {
            into.Children.Clear();

            var table = Ui.Table(broken.Select(b => new TableRow(b,
                                     b.Model.DisplayName,
                                     b.Say,
                                     b.Match ?? b.Expected)
                                 {
                                     Tone = b.BroughtIn ? Tone.Good
                                          : b.Match != null ? Tone.Warn
                                          : Tone.Plain
                                 }),
                                 ("Model", 220), ("State", 200), ("Path", 420));

            into.Children.Add(table);

            into.Children.Add(Ui.Picking(table,
                new Ui.OnPicked("Bring in {n}", (picked, say) =>
                {
                    var brought = 0;

                    foreach (var row in picked.OfType<Broken>())
                    {
                        if (row.Match == null || row.BroughtIn) continue;

                        try
                        {
                            session.Model.Document.AppendFile(row.Match);
                            session.Model.Invalidate();

                            row.BroughtIn = true;
                            brought++;

                            session.Record(ActivityKind.Reconcile,
                                           "brought in " + row.Model.DisplayName + " from " + row.Match);
                        }
                        catch (Exception error)
                        {
                            say.Children.Add(Ui.Problem("The host refused to append "
                                + System.IO.Path.GetFileName(row.Match) + ": " + error.Message));
                        }
                    }

                    if (brought == 0)
                    {
                        say.Children.Add(Ui.Line("Nothing to bring in: look for the files first.", 0.8));
                        return;
                    }

                    Draw(session, broken, into);

                    say.Children.Add(Ui.Line(Ui.Count(brought, "model")
                        + " is in the federation. Remove the stale copies in the Selection Tree and "
                        + "save.", 1, true));

                    // The natural next question is "is it fit to coordinate against now" - which is
                    // the Health tab's sweep, one press away rather than a memory.
                    say.Children.Add(Ui.Actions(Ui.Quiet("Check the federation",
                        () => Ui.Told(AppWindow.Show("audit", "health")))));
                }, primary: true),
                new Ui.OnPicked("Copy {n} paths", (picked, say) =>
                    Ui.Copy(picked.OfType<Broken>().Select(b => b.Model.DisplayName + "\t"
                                                              + b.Expected + "\t"
                                                              + (b.Match ?? string.Empty)),
                            say, "model"))));
        }

        /// <summary>The folder a healthy model's source lives in, as the first place to look.</summary>
        private static string? Suggest(Session session)
        {
            foreach (var model in session.Model.Models)
            {
                if (string.IsNullOrWhiteSpace(model.SourcePath)) continue;

                try
                {
                    if (File.Exists(model.SourcePath))
                        return System.IO.Path.GetDirectoryName(model.SourcePath);
                }
                catch (Exception)
                {
                    // A path the OS will not parse suggests nothing.
                }
            }

            return null;
        }

        /// <summary>The loaded models whose source file is not where the federation says it is.</summary>
        private static IReadOnlyList<IModelSource> Missing(Session session)
        {
            var broken = new List<IModelSource>();

            foreach (var model in session.Model.Models)
            {
                if (string.IsNullOrWhiteSpace(model.SourcePath)) continue;

                try
                {
                    if (!File.Exists(model.SourcePath)) broken.Add(model);
                }
                catch (Exception)
                {
                    // An unreachable share or a path the OS will not parse. Unknown, not missing:
                    // telling somebody their model is gone because a VPN dropped would be worse.
                }
            }

            return broken;
        }

        /// <summary>Look for each missing source under a folder, and fill the table with what turned up.</summary>
        private static void Search(Session session, TextBox folder, IReadOnlyList<Broken> broken,
                                   Panel results, Job job)
        {
            var where = folder.Text.Trim();

            if (where.Length == 0)
            {
                results.Children.Add(Ui.Problem("Type a folder to search first."));
                return;
            }

            if (!Directory.Exists(where))
            {
                results.Children.Add(Ui.Problem("There is no folder at " + where + "."));
                return;
            }

            if (broken.Count == 0)
            {
                results.Children.Add(Ui.Empty("Nothing to look for.",
                    "Every loaded model's source file is where the federation expects it."));

                return;
            }

            var looked = 0;
            var found = 0;

            foreach (var row in broken)
            {
                if (!job.Step(looked++, broken.Count, "models checked")) return;

                if (row.BroughtIn) continue;

                var name = System.IO.Path.GetFileName(row.Expected);

                string[] candidates;

                try
                {
                    candidates = Directory.GetFiles(where, name, SearchOption.AllDirectories);
                }
                catch (Exception error)
                {
                    results.Children.Add(Ui.Problem("Could not search for " + name + ": " + error.Message));
                    continue;
                }

                if (candidates.Length == 0)
                {
                    row.Match = null;
                    row.Others = 0;

                    continue;
                }

                row.Match = candidates[0];
                row.Others = candidates.Length - 1;

                found++;
            }

            results.Children.Add(found == 0
                ? Ui.Problem("Nothing of those names is under that folder. Try the folder the "
                             + "publisher writes to.")
                : Ui.Line(Ui.Count(found, "model") + " has a file of the same name under that folder. "
                          + "Pick the rows above and bring them in.", 1, true));
        }
    }
}
