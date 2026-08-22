using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The models this federation can no longer find, and what to do about them.
    ///
    /// A model whose source has moved still displays: Navisworks is showing its own cached copy,
    /// happily, with no warning anywhere. Everybody coordinates against it for a fortnight and then
    /// discovers the geometry is from November.
    ///
    /// <b>What Navisworks will not let a plug-in do.</b> There is no API to re-point an existing
    /// model at a new path. The document exposes append, merge and open; nothing replaces one
    /// model's source in place, and nothing removes a single model. So this window does the part
    /// that can be done properly — find where the files went, and bring the right one in — and says
    /// plainly what has to be finished by hand, rather than pretending to a repoint it cannot make.
    /// </summary>
    public static class FixLinksWindow
    {
        private static Window? _open;

        /// <summary>
        /// Show the window.
        /// </summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            if (_open != null)
            {
                _open.Activate();
                return null;
            }

            var window = new Window
            {
                Title = "CamelWorks - fix broken links",
                Width = 640,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = Body(session),
            };

            window.Closed += (_, __) => _open = null;

            _open = window;
            window.Show();

            return null;
        }

        private static UIElement Body(Session session)
        {
            var results = Ui.Stack();
            var folder = Ui.Text(string.Empty, null, 380);

            var broken = Missing(session);

            var look = Ui.Runner("Look for them", results, job => Search(session, folder, broken, results, job));

            var head = broken.Count == 0
                ? Ui.Line("Every loaded model's source file is where the federation expects it.", 1, true)
                : Ui.Problem(Ui.Count(broken.Count, "model")
                    + " cannot find its source file. Navisworks is showing you its own cached copy of "
                    + "each of them, with no warning anywhere, and it may be months old.");

            var list = Ui.Stack();

            foreach (var model in broken)
                list.Children.Add(Ui.Line(model.DisplayName + " - expected at " + model.SourcePath, 0.8));

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Fix broken links"),
                head,
                list,
                Ui.Field("Look in", folder),
                Ui.Across(look),
                results,
                Ui.Note("Navisworks offers no way for a plug-in to re-point an existing model at a new "
                        + "path: the document can append, merge and open, and nothing replaces one "
                        + "model's source in place or removes a single model. So bringing the right "
                        + "file in adds it alongside the stale one. Remove the stale one in the "
                        + "Selection Tree and save the federation, and the link is fixed properly."),
                Ui.Note("Everything else in CamelWorks reads what is loaded. Until the stale model is "
                        + "out of the federation, the board and every report include it.")));
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

        /// <summary>Look for each missing source under a folder, and offer to bring it in.</summary>
        private static void Search(Session session, TextBox folder, IReadOnlyList<IModelSource> broken,
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

            foreach (var model in broken)
            {
                if (!job.Step(looked++, "models checked")) return;

                var name = System.IO.Path.GetFileName(model.SourcePath);

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
                    results.Children.Add(Ui.Line(model.DisplayName + " - nothing called " + name
                        + " under that folder.", 0.75));

                    continue;
                }

                var found = candidates[0];

                results.Children.Add(Ui.Line(model.DisplayName + " - found at " + found, 1, true));

                if (candidates.Length > 1)
                    results.Children.Add(Ui.Line("and " + (candidates.Length - 1)
                        + " more with the same name; the first is offered.", 0.55));

                results.Children.Add(Ui.Button("Bring in " + name, () =>
                {
                    try
                    {
                        session.Model.Document.AppendFile(found);
                        session.Model.Invalidate();

                        results.Children.Add(Ui.Line(name + " is in the federation. Remove the stale "
                            + model.DisplayName + " in the Selection Tree and save.", 1, true));

                        session.Record(ActivityKind.Reconcile, "brought in " + name + " from " + found);
                    }
                    catch (Exception error)
                    {
                        results.Children.Add(Ui.Problem("The host refused to append it: " + error.Message));
                    }
                }));
            }
        }
    }
}
