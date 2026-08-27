using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The federation's register: what is loaded, at which revision, and what moved since.
    ///
    /// The manifest has been taken since the first release, as one check inside the health sweep,
    /// which is the wrong place for it twice over. It is not a health question - a federation can
    /// be perfectly healthy and still be built on last month's architecture - and burying it in a
    /// sweep meant the one fact a coordinator is asked for most on a Monday ("which revision is
    /// this against?") took a full model read and a scroll to answer.
    ///
    /// So it is its own window, and it keeps the register: take a manifest now, and it says what
    /// has been republished, what has gone, and what has been moved, measured against the last
    /// time anybody looked.
    /// </summary>
    public static class ModelsWindow
    {
        private const string IntakeKey = "intake";

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            Body(session);

        private static UIElement Body(Session session)
        {
            var results = Ui.Stack();

            var loaded = session.Model.Models;

            var stage = Ui.Stack(
                loaded.Count == 0
                    ? (UIElement)Ui.Empty("Nothing is loaded.",
                          "Append a model and the register can say what it is and where it came from.")
                    : Bench.Ready(Ui.Count(loaded.Count, "model") + " loaded. Take a manifest to "
                          + "record what they are right now, and to see what has changed since "
                          + "the last one.",
                          "The manifest is kept with the project, so next week's answer to \"which "
                          + "revision was that against?\" is a record rather than a memory."),
                results);

            return Bench.Surface(null,
                Ui.Runner("Take a manifest", results, job => Take(session, results, job)),
                stage);
        }

        /// <summary>The picked register rows, as columns somebody can paste.</summary>
        private static void Rows(IReadOnlyList<object> picked, Panel results) =>
            Ui.Copy(picked.OfType<IReadOnlyList<string?>>()
                        .Select(r => string.Join("\t", r.Select(cell => cell ?? string.Empty))),
                    results);

        /// <summary>
        /// Read the federation, record what it is, and say what moved.
        ///
        /// Element counts come from one pass rather than from the model headers, because a model
        /// appended twice reports its own count twice and the register's whole job is to be the
        /// thing you can trust when the geometry disagrees with the file name.
        /// </summary>
        private static void Take(Session session, Panel results, Job job)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var read = 0;

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!job.Step(read++, "elements read")) return;
                if (!item.HasGeometry) continue;

                var scope = item.Model.Scope;
                counts[scope] = counts.TryGetValue(scope, out var seen) ? seen + 1 : 1;
            }

            job.Say("Writing the register...");

            var manifest = ModelManifest.Of(session.Model.Models, session.Profile,
                                            itemCounts: counts, version: Ui.Release,
                                            takenUtc: DateTimeOffset.UtcNow);

            var section = session.Store.Section(ProjectStore.ModelsSection);
            var before = ModelManifest.FromJson(section[IntakeKey]);
            var intake = manifest.Since(before.Models.Count > 0 ? before : null);

            results.Children.Add(Ui.Line(intake.Explain(), 1, true));
            results.Children.Add(Ui.Line(manifest.Explain(), 0.8));

            if (intake.HasPrevious && intake.NothingNew && intake.Missing.Count == 0)
                results.Children.Add(Ui.Info("Nothing has been republished since the last manifest. "
                    + "A clash run against exactly these files produces exactly last week's board - "
                    + "worth knowing before the meeting rather than during it."));

            if (intake.Missing.Count > 0)
                results.Children.Add(Ui.Note(Ui.Count(intake.Missing.Count, "model")
                    + " the last manifest saw is no longer loaded: "
                    + string.Join(", ", intake.Missing.Select(m => m.DisplayName))
                    + ". Either it was removed on purpose, or an append was lost when somebody saved."));

            var units = session.Profile.Units;
            var table = manifest.ModelTable(units);

            var models = Ui.Table(table.Rows.Select(r => new TableRow(r,
                r[0] ?? string.Empty, r[2] ?? string.Empty, r[3] ?? string.Empty,
                r[5] ?? string.Empty, r[6] ?? string.Empty)).ToList(),
                ("Model", 200), ("Source last written", 150), ("Size", 80),
                ("Placement", 170), ("Elements", 0));

            // The register's most common destination is somebody else's inbox - "which revisions
            // is this week's board against?" - so picking rows and copying them is the action,
            // rather than making somebody retype five columns out of a window.
            results.Children.Add(Ui.Picking(models,
                new Ui.OnPicked("Copy {n}", Rows, primary: true)));

            if (intake.HasPrevious)
            {
                var register = intake.Table();

                results.Children.Add(Ui.Eyebrow("Since the last manifest"));

                var since = Ui.Table(register.Rows.Select(r => new TableRow(r,
                    r[0] ?? string.Empty, r[1] ?? string.Empty, r[2] ?? string.Empty,
                    r[3] ?? string.Empty)).ToList(),
                    ("Model", 200), ("Discipline", 120), ("State", 200), ("Source last written", 0));

                results.Children.Add(Ui.Picking(since,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(since, picked), say, "model"))));
            }

            section.Set(IntakeKey, manifest.ToJson());

            results.Children.Add(session.Store.Save()
                ? Ui.Done("Recorded. The next manifest is measured against this one.")
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            session.Record(ActivityKind.Reconcile,
                           "took a manifest of " + Ui.Count(manifest.Models.Count, "model"));
        }
    }
}
