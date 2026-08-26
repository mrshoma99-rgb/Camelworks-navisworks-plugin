using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Compare: what changed between two revisions of the model.
    ///
    /// <b>Three screens, one idea.</b> Run picks the two sides and does the work. Changes is the
    /// list, and everything you can do with it — paint it, stamp it, raise it, send it. Baselines
    /// is the small amount of housekeeping the other two need.
    ///
    /// <b>The zero-setup story is one button.</b> With no baseline on a raw model, Run says so and
    /// offers to take one. Nothing else has to be configured, ever: the tolerance, the property
    /// list and the matching strength all have defaults that work, and every one of them is on the
    /// screen so it can be argued with rather than guessed at.
    /// </summary>
    public static class CompareViews
    {
        /// <summary>The last comparison, so the Changes tab has something to show.</summary>
        internal static CompareResult? Last { get; private set; }

        /// <summary>
        /// The baseline that comparison ran against.
        ///
        /// Kept because the baseline is the only thing that knows which files the old revision was
        /// loaded from — and those files are what "show me what was removed" actually needs.
        /// </summary>
        internal static ModelSnapshot? LastBaseline { get; private set; }

        private static readonly CompareSettings Settings = new CompareSettings();
        private static string? _pickedBaseline;

        /// <summary>
        /// Whether to read the actual triangles, and how many.
        ///
        /// Held on the class rather than the screen so that the choice made when a baseline was
        /// taken is still the choice when the comparison runs a week later — a baseline measured
        /// with bounding boxes and a run measured with triangles answers nothing, and the two
        /// screens agreeing by default is the cheapest way to stop that happening.
        /// </summary>
        private static bool _readTriangles;

        private static int _triangleCap = NavCompare.DefaultCap;

        // -------------------------------------------------------------------------------------
        // Run
        // -------------------------------------------------------------------------------------

        /// <summary>The Run screen.</summary>
        public static UIElement Run()
        {
            var session = Host.Current;

            if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

            var store = StoreFor(session);
            var baselines = store.Baselines();
            var results = new StackPanel();
            var listed = new StackPanel();

            Redraw(store, listed, results);

            var holding = BaselineModels.Of(LastBaseline, session.Model.Models, PhysicalFileSystem.Instance);

            // Said before the button rather than discovered in the results: with the old revision
            // appended, a fresh comparison sees both revisions at once, pairs the baseline model to
            // itself, and reports that nothing changed.
            var warning = BaselineModels.IsHoldingBoth(holding)
                ? Ui.Note("An older revision is open in this document — it was brought in to show "
                          + "what was removed. Comparing again now would compare the baseline "
                          + "against itself and report no changes. Take it back out in "
                          + "Changes first.")
                : null;

            if (baselines.Count == 0)
                return Ui.Scroll(Ui.Stack(warning, First(session, store, results), Terms(), results));

            var labels = baselines.Select(b => b.Describe()).ToList();

            if (_pickedBaseline == null || !labels.Contains(_pickedBaseline))
                _pickedBaseline = labels[0];

            return Ui.Scroll(Ui.Stack(
                warning,
                Ui.Form(Ui.Field("Compare against",
                    Ui.Choice(labels, _pickedBaseline, pick => _pickedBaseline = pick, 320),
                    "the most recent is picked for you")),
                Ui.Runner("Run the comparison", results, job => Go(session, store, results, job)),
                Terms(),
                Ui.Section("Record a new baseline", "the model as it stands right now", false,
                    Ui.Small("Give it a name you will recognise in three months - an issue number, "
                             + "a milestone, a date."),
                    Take(session, store, results, () => Redraw(store, listed, results),
                         primary: false)),
                Ui.Section("Baselines kept", Ui.Count(baselines.Count, "baseline"), false, listed),
                results));
        }

        /// <summary>
        /// The whole of the first-run experience.
        ///
        /// No form, no template, no settings — one button, and from next week the tab is useful.
        /// This one keeps its card, because here the empty state is the answer rather than a
        /// preamble to one.
        /// </summary>
        private static UIElement First(Session session, SnapshotStore store, StackPanel results) =>
            Ui.Marked(Glyphs.Compare, "No baseline yet",
                "Nothing has been recorded for this document, so there is nothing to compare "
                + "against. Take one now and every comparison from here on has something to work from.",
                Ui.Line(SnapshotBuilder.Estimate(Guess(session), Settings.Watch), 0.75),
                Take(session, store, results),
                store.IsMemoryOnly
                    ? Ui.Note("This document has never been saved, so there is nowhere beside it "
                              + "to keep a baseline. Save the document first.")
                    : Ui.Line("It will be kept in " + store.Folder + ".", 0.7));

        private static UIElement Terms()
        {
            var watch = new[] { "Standard list", "Every property", "No properties" };

            // Folded, with what it is currently set to on the header line. The defaults work on a
            // raw model, so this is a card almost nobody has to open - but a row somebody disagrees
            // with still needs an answer better than "the tool said so", and folding a thing away
            // only costs nothing while the fold itself says what it will do.
            // Short enough to survive the header at 470 pixels. CompareSettings.Describe() is the
            // long form and stays the long form; a summary that ellipsises says nothing, which is
            // the one thing a folded header must not do.
            return Ui.Section("What counts as a change",
                () => Settings.Tolerance.ToString("0.####", CultureInfo.InvariantCulture)
                      + " units · " + Show(Settings.Watch),
                false,
                Ui.Field("Move over", Ui.Text(
                    Settings.Tolerance.ToString("0.####", CultureInfo.InvariantCulture),
                    text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                            && value >= 0)
                            Settings.Tolerance = value;
                    }, 90),
                    "model units. Below this an element has not moved, which is what stops a "
                    + "re-export's floating-point noise reading as a moved federation"),

                Ui.Field("Properties", Ui.Choice(watch, Show(Settings.Watch), pick =>
                    Settings.Watch = pick == "Every property" ? PropertyWatch.All
                                   : pick == "No properties" ? PropertyWatch.None
                                   : PropertyWatch.Standard)),

                Ui.Field("Match on", Ui.Choice(
                    new[] { "GUID, tree path or shape", "GUID or tree path", "GUID only" },
                    Show(Settings.WeakestRung),
                    pick => Settings.WeakestRung = pick == "GUID only" ? KeyRung.InstanceGuid
                                                 : pick == "GUID or tree path" ? KeyRung.TreePath
                                                 : KeyRung.Geometry)),

                Ui.Check("Include grouping nodes — levels, categories, families", Settings.IncludeGroupingNodes,
                         value => Settings.IncludeGroupingNodes = value),

                Ui.Toggle("Read the actual triangles", _readTriangles, value => _readTriangles = value),

                Ui.Field("At most", Ui.Text(
                    _triangleCap.ToString(CultureInfo.InvariantCulture),
                    text =>
                    {
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                            && value >= 0)
                            _triangleCap = value;
                    }, 90),
                    "elements. Zero means no limit"),

                Ui.Small("The triangles are the only way to see an element remodelled inside its own "
                         + "bounding box — a duct rerouted through the same opening, a stair rebuilt "
                         + "between the same two levels. Everything else on this screen is free; "
                         + "this one is a geometry pass per element."),

                Triangles());
        }

        /// <summary>
        /// What a triangle read can and cannot answer against the baseline that is picked.
        ///
        /// <b>A signature on one side compares against nothing.</b> Reading the current model's
        /// triangles against a baseline that has none costs the minutes and reports nothing at
        /// all — which reads as "nothing was remodelled" and is the most expensive way this
        /// feature could lie. So the screen says it before the button is pressed.
        /// </summary>
        private static UIElement? Triangles()
        {
            if (!_readTriangles) return null;

            var baseline = LastBaseline;

            if (baseline == null)
                return Ui.Line("Whether this is worth doing depends on the baseline. Run once and "
                               + "this line will say.", 0.7);

            if (baseline.Shapes > 0)
                return Ui.Line("The baseline measured " + ComparePolicy.Count(baseline.Shapes)
                               + " elements, so only the current model's matching ones are read — "
                               + "the rest would compare against nothing.", 0.7);

            return Ui.Note("The baseline was taken with bounding boxes only, so reading triangles "
                           + "now has nothing to compare against and no element can be reported as "
                           + "remodelled. Take a fresh baseline with this ticked and next week's "
                           + "comparison can answer it.");
        }

        private static void Go(Session session, SnapshotStore store, StackPanel results, Job job)
        {
            var baselines = store.Baselines();

            if (baselines.Count == 0)
            {
                results.Children.Add(Ui.Empty("No baseline to compare against.",
                    "Take one with the button above. It is the whole of the setup."));
                return;
            }

            var entry = baselines.FirstOrDefault(b => b.Describe() == _pickedBaseline) ?? baselines[0];

            job.Say("Reading the baseline...");

            var before = store.Load(entry);

            if (before == null)
            {
                results.Children.Add(Ui.Problem("That baseline could not be read. It may have been "
                                                + "moved or truncated; take a fresh one."));
                return;
            }

            job.Say("Walking the model...");

            var after = SnapshotBuilder.Take(
                session.Model, DateTimeOffset.UtcNow, "Now", Settings,
                documentPath: session.SavedPath, units: session.Document.Units.ToString(),
                keepGoing: () => !job.IsCancelled,
                onProgress: p => job.Step(p.Kept, "elements recorded"));

            if (job.IsCancelled)
            {
                results.Children.Add(Ui.Note("Stopped. Nothing was changed."));
                return;
            }

            if (_readTriangles)
            {
                if (before.Shapes == 0)
                {
                    results.Children.Add(Ui.Note("Triangles were not read: the baseline was taken "
                        + "with bounding boxes only, so there is nothing on the other side to "
                        + "compare a signature against. Nothing was skipped that could have been "
                        + "answered."));
                }
                else
                {
                    var wanted = ModelCompare.WorthDeepening(before, after, Settings);

                    job.Say("Reading triangles for " + ComparePolicy.Count(wanted.Count) + " elements...");

                    var read = new NavCompare(session.Model).Deepen(
                        after, wanted, _triangleCap,
                        () => !job.IsCancelled,
                        done => job.Step(done, "elements measured"));

                    if (_triangleCap > 0 && read >= _triangleCap && wanted.Count > read)
                        results.Children.Add(Ui.Note("Stopped at the limit of "
                            + ComparePolicy.Count(_triangleCap) + ". "
                            + ComparePolicy.Count(wanted.Count - read)
                            + " more elements would have been measured — raise the limit, or accept "
                            + "that those are reported on their bounding box alone."));
                }
            }

            job.Say("Comparing...");

            var result = ModelCompare.Of(before, after, Settings);

            Last = result;
            LastBaseline = before;

            Scoreboard(results, result);

            session.Record(ActivityKind.Reconcile, "compared against " + entry.Describe(), result.Explain());
        }

        private static void Scoreboard(StackPanel results, CompareResult result)
        {
            results.Children.Add(Ui.Line(result.Between(), 1, true));

            // UIElement? rather than UIElement: Ui.Across takes params UIElement?[], and
            // handing it a UIElement[] is a nullability conversion the compiler refuses.
            var figures = new List<UIElement?>();

            foreach (var row in CompareLayers.Legend(result))
            {
                figures.Add(Ui.Figure(
                    ComparePolicy.Count(row.Count), row.Label,
                    row.Label == "Removed" ? Tone.Bad
                    : row.Label == "Added" ? Tone.Good
                    : row.Label == "Unchanged" ? Tone.Plain
                    : Tone.Warn));
            }

            results.Children.Add(Ui.Across(figures.ToArray()));

            if (result.IsQuiet)
                results.Children.Add(Ui.Line("Nothing differs. Every element that could be paired "
                                             + "was paired, and matched exactly.", 0.85));

            var paired = Ui.Table(result.Models.Select(m => new TableRow(m,
                    m.Title,
                    ComparePolicy.Count(m.Added),
                    ComparePolicy.Count(m.Removed),
                    ComparePolicy.Count(m.Moved),
                    ComparePolicy.Count(m.Changed),
                    ModelPairing.Show(m.Strength) + " — " + m.Reason)),
                ("Model", 150), ("Added", 60), ("Removed", 70), ("Moved", 60),
                ("Changed", 70), ("Paired on", 0));

            // Added, removed, moved and changed per model is the summary somebody is asked for by
            // name after every revision, and it went out as a screenshot.
            results.Children.Add(Ui.Card("Which model was held to be which",
                result.Pairing.Explain(),
                Ui.Picking(paired,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(paired, picked), say, "model")))));

            foreach (var note in result.Notes) results.Children.Add(Ui.Note(note));

            results.Children.Add(Ui.Line("The change list is in Changes, on the ribbon.", 0.75));
        }

        // -------------------------------------------------------------------------------------
        // Changes
        // -------------------------------------------------------------------------------------

        /// <summary>The Changes screen.</summary>
        public static UIElement Changes()
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Changes"), Ui.Problem(Host.NoModel)));

            var result = Last;

            if (result == null || result.IsEmpty)
                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Changes"),
                    Ui.Empty("Nothing has been compared yet.",
                             "Run one in Compare and the list appears here.")));

            var screen = new ChangeScreen(session, result);
            return Ui.Scroll(screen.Build());
        }

        private sealed class ChangeScreen
        {
            private readonly Session _session;
            private readonly CompareResult _result;
            private readonly StackPanel _list = new StackPanel();
            private readonly StackPanel _output = new StackPanel();
            private string _filter = "Everything";
            private string _search = string.Empty;

            internal ChangeScreen(Session session, CompareResult result)
            {
                _session = session;
                _result = result;
            }

            internal UIElement Build()
            {
                Redraw();

                return Ui.Stack(
                    Ui.Eyebrow(_result.Between()),
                    Counts(),
                    Paint(),
                    Filters(),
                    _list,
                    Ui.Rule(),
                    Keep(),
                    _output);
            }

            /// <summary>
            /// The answer, as four numbers rather than as a sentence.
            ///
            /// It was <c>Ui.Sub(result.Explain())</c> — the counts, in prose, in the subtitle. The
            /// same four facts as figures are both shorter and the legend for the colours the
            /// paint puts on the model, which is a second sentence that then does not need
            /// writing.
            /// </summary>
            private UIElement Counts() => Ui.Across(
                Ui.Figure(Many(ChangeBucket.Added), "added", Tone.Good),
                Ui.Figure(Many(ChangeBucket.Removed), "removed", Tone.Bad),
                Ui.Figure(Many(ChangeBucket.Moved), "moved", Tone.Warn),
                Ui.Figure(Many(ChangeBucket.Changed), "changed"));

            private string Many(ChangeBucket bucket) =>
                _result.CountIn(bucket).ToString("N0", CultureInfo.InvariantCulture);


            private UIElement Filters()
            {
                var buckets = new List<string> { "Everything" };

                foreach (var bucket in new[]
                         {
                             ChangeBucket.Added, ChangeBucket.Removed,
                             ChangeBucket.Moved, ChangeBucket.Changed,
                         })
                {
                    if (_result.CountIn(bucket) > 0)
                        buckets.Add(ElementChange.Show(bucket));
                }

                return Ui.Across(
                    Ui.Field("Show", Ui.Choice(buckets, _filter, pick => { _filter = pick; Redraw(); })),
                    Ui.Field("Find", Ui.Text(_search, text => { _search = text ?? string.Empty; Redraw(); }, 200)));
            }

            private void Redraw()
            {
                _list.Children.Clear();

                var rows = _result.Changes
                    .Where(c => _filter == "Everything" || ElementChange.Show(c.Bucket) == _filter)
                    .Where(c => _search.Length == 0
                                || c.Print.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                                || c.Summary().IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(c => new TableRow(c,
                        ElementChange.Show(c.Bucket),
                        c.Print.Name.Length > 0 ? c.Print.Name : "(unnamed)",
                        c.Print.Type,
                        c.Model,
                        c.Summary(),
                        c.MatchedOn.HasValue ? CompareReport.Rung(c.MatchedOn.Value) : "not matched")
                    {
                        Tone = c.Bucket == ChangeBucket.Removed ? Tone.Bad
                             : c.Bucket == ChangeBucket.Added ? Tone.Good
                             : Tone.Warn,
                    })
                    .ToList();

                if (rows.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("Nothing matches.",
                        "Widen the filter, or clear the search box."));
                    return;
                }

                var table = Ui.Table(rows,
                        ("Change", 80), ("Element", 170), ("Type", 130), ("Model", 110),
                        ("What changed", 260), ("Matched on", 0))
                    .OnPick(item => Look(item as ElementChange));

                // Walking a revision one row at a time is the slow way. Ticking the eight that
                // matter and selecting them together is what somebody does before they screenshot
                // it for the architect.
                _list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Select {n} in the model", Show, primary: true)));

                if (_result.TotalDropped > 0)
                    _list.Children.Add(Ui.Note(Ui.Count(_result.TotalDropped, "row")
                        + " are not listed — the list is capped so it stays usable. The counts above "
                        + "are complete, and the report has every row."));
            }

            /// <summary>
            /// Select every picked change that is still in this document.
            ///
            /// Removed elements are not, by definition. They are counted out loud rather than
            /// silently skipped, because "I ticked twelve and it selected nine" is otherwise a
            /// bug somebody reports.
            /// </summary>
            private void Show(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var keys = new List<ElementKey>();
                var gone = 0;

                foreach (var change in picked.OfType<ElementChange>())
                {
                    if (change.Bucket == ChangeBucket.Removed) { gone++; continue; }
                    if (change.Print.TryKey(out var key)) keys.Add(key);
                }

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Note(gone > 0
                        ? Ui.Count(gone, "removed element") + " cannot be selected: they are not in "
                          + "this model any more. That is what removed means."
                        : "Nothing there to select."));
                    return;
                }

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.5);

                say.Children.Add(Ui.Done("Selected " + Ui.Count(keys.Count, "element") + "."
                    + (gone > 0
                        ? " " + Ui.Count(gone, "removed one") + " left out - they are not in this "
                          + "model any more."
                        : string.Empty)));
            }

            private void Look(ElementChange? change)
            {
                if (change == null) return;
                if (!change.Print.TryKey(out var key)) return;

                // A removed element is not in this document, so selecting it would do nothing at
                // all — which reads as a broken row rather than as the fact it is.
                if (change.Bucket == ChangeBucket.Removed) return;

                var keys = new[] { key };

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.5);
            }

            private UIElement Paint()
            {
                var view = CompareLayers.Build(_result);

                var paint = Ui.Runner("Paint it onto the model", _output, job => ApplyView(job, view));

                var clear = Ui.Button("Take the colours off", () =>
                {
                    _output.Children.Clear();

                    // Every element the comparison touched, including the unchanged ones that
                    // carry the fade. This is the exact inverse of what Paint wrote, which is why
                    // the fade names its elements rather than covering the whole model: nothing
                    // outside the comparison has been written to, so nothing outside it is undone.
                    var keys = new List<ElementKey>();

                    foreach (var bucket in new[]
                             {
                                 ChangeBucket.Added, ChangeBucket.Removed, ChangeBucket.Moved,
                                 ChangeBucket.Changed, ChangeBucket.Unchanged,
                             })
                        keys.AddRange(_result.KeysIn(bucket));

                    _session.View.ClearOverrides(keys);

                    _output.Children.Add(Ui.Done("Cleared the comparison's colour and fade on "
                        + Ui.Count(keys.Count, "element") + ". Nothing else was touched."));
                });

                // No card. The figures above are already the legend - added green, removed red,
                // moved amber - and bringing the old revision in belongs beside the red figure it
                // exists to serve rather than folded away at the foot of the panel.
                return Ui.Stack(
                    Ui.Actions(paint, clear),
                    Removed(view));
            }

            private void ApplyView(Job job, CompareView view)
            {
                job.Say("Resolving...");

                var resolver = new SetsViews.HostResolver(_session);
                var fold = AppearanceStack.Fold(view.Layers, resolver);
                var keys = fold.Elements.Select(e => e.Key).ToList();

                job.Say("Reading what the model looks like now...");

                var plan = AppearancePlanner.Plan(fold, _session.View.ReadAppearance(keys));

                job.Say("Painting...");

                if (plan.Clear.Count > 0) _session.View.ClearOverrides(plan.Clear);
                if (plan.Hide.Count > 0) _session.View.SetVisible(plan.Hide, false);
                if (plan.Show.Count > 0) _session.View.SetVisible(plan.Show, true);

                foreach (var batch in plan.Colours) _session.View.SetColour(batch.Keys, batch.Colour);
                foreach (var batch in plan.Transparencies)
                    _session.View.SetTransparency(batch.Keys, batch.Transparency);

                _output.Children.Add(Ui.Line(view.ToString(), 1, true));

                _session.Record(ActivityKind.Appearance, "painted the comparison onto the model",
                                _result.Explain());
            }

            /// <summary>
            /// The removed elements, and the old revision they are still in.
            ///
            /// <b>"A removed element cannot be shown" is only true until somebody opens the old
            /// file.</b> The element is not gone — it is in last month's model, in last month's
            /// issue folder, and the baseline recorded exactly which file that was. Appending it
            /// beside the current revision makes the red layer start working, and nothing else in
            /// the view moves: the host's element index is first-wins in document order, so every
            /// key the current model already holds still resolves to the current element and the
            /// old revision claims only the keys nothing else has.
            /// </summary>
            private UIElement? Removed(CompareView view)
            {
                if (_result.CountIn(ChangeBucket.Removed) == 0) return null;

                var models = BaselineModels.Of(CompareViews.LastBaseline, _session.Model.Models,
                                               PhysicalFileSystem.Instance);

                var bring = BaselineModels.ToBring(models);
                var back = BaselineModels.ToSendBack(models);

                var body = new List<UIElement?>
                {
                    Ui.Line(Ui.Count(_result.CountIn(ChangeBucket.Removed), "removed element")
                            + (view.Uncolourable > 0 && back.Count == 0
                                ? " — not in this document, which is what removed means."
                                : " — the old revision is open, so they are painted red."), 0.85),
                    Ui.Line(BaselineModels.Explain(models), 0.7),
                };

                if (bring.Count > 0)
                {
                    body.Add(Ui.Runner("Bring the old revision in", output: _output, primary: false, work: job =>
                    {
                        job.Say("Opening " + Ui.Count(bring.Count, "file") + "...");

                        var gained = new NavCompare(_session.Model).Bring(bring, out var problem);

                        if (problem != null) { _output.Children.Add(Ui.Problem(problem)); return; }

                        _output.Children.Add(Ui.Line("Brought in " + Ui.Count(gained, "model") + ".", 1, true));

                        // Repainted straight away, because bringing the file in and then asking
                        // somebody to press Paint is two steps for one intention.
                        ApplyView(job, CompareLayers.Build(_result));

                        _output.Children.Add(Ui.Note("The older revision is now part of this "
                            + "document. Take it back out before comparing again, or the next "
                            + "comparison will see both revisions and report no changes."));

                        _session.Record(ActivityKind.Reconcile,
                            "brought the baseline revision in to show " + Ui.Count(
                                _result.CountIn(ChangeBucket.Removed), "removed element"));
                    }));
                }

                if (back.Count > 0)
                {
                    body.Add(Ui.Runner("Take the old revision back out", output: _output, primary: false, work: job =>
                    {
                        job.Say("Removing " + Ui.Count(back.Count, "model") + "...");

                        var lost = new NavCompare(_session.Model).SendBack(back, out var problem);

                        _output.Children.Add(Ui.Line("Took out " + Ui.Count(lost, "model")
                                                     + ". The document is as it was.", 1, true));

                        if (problem != null) _output.Children.Add(Ui.Note(problem));

                        _session.Record(ActivityKind.Reconcile, "took the baseline revision back out");
                    }));
                }

                foreach (var model in models)
                {
                    if (model.Standing == BaselineStanding.CanBring
                        || model.Standing == BaselineStanding.AlreadyIn) continue;

                    body.Add(Ui.Note(model.Title + " — " + model.Reason));
                }

                if (bring.Count == 0 && back.Count == 0)
                    body.Add(Ui.Line("Nothing can be brought in, so the removed elements are on the "
                                     + "list and in the report but not in the view.", 0.7));

                return Ui.Card("The removed ones", null, body.ToArray());
            }

            /// <summary>
            /// A viewpoint per place something happened.
            ///
            /// <b>A change list is a list; a building is a place.</b> Four hundred rows tell
            /// nobody where to stand. A dozen viewpoints — one per area where something actually
            /// moved — is a walk somebody can do in ten minutes and hand to whoever has to fix it,
            /// and it survives being sent to a machine that has never had CamelWorks on it.
            /// </summary>
            private UIElement Walk()
            {
                var cell = CompareClusters.DefaultCell;

                var make = Ui.Runner("Make a viewpoint for each place", output: _output, primary: false, work: job =>
                {
                    var clusters = CompareClusters.Of(_result, cell);

                    if (clusters.Count == 0)
                    {
                        _output.Children.Add(Ui.Empty("Nowhere to look.",
                            "Nothing was added, removed, moved or changed."));
                        return;
                    }

                    var folder = CompareClusters.Folder(_result);
                    var made = 0;
                    var framed = 0;

                    for (var i = 0; i < clusters.Count; i++)
                    {
                        if (!job.Step(made, clusters.Count, "viewpoints")) break;

                        var cluster = clusters[i];

                        // A cluster of removed elements has nothing in this document to zoom to,
                        // so the camera is framed on the extent the baseline recorded. That is the
                        // only way to look at a deletion, and it is the one people ask for most.
                        if (cluster.Keys.Count > 0) _session.View.ZoomTo(cluster.Keys, 1.5);
                        else { _session.View.FrameBox(cluster.Bounds); framed++; }

                        _session.Viewpoints.SaveCurrent(
                            CompareClusters.Name(cluster, i + 1, clusters.Count), folder);

                        made++;
                    }

                    _output.Children.Add(Ui.Line("Made " + Ui.Count(made, "viewpoint")
                        + " in \"" + folder + "\".", 1, true));

                    if (framed > 0)
                        _output.Children.Add(Ui.Note(Ui.Count(framed, "viewpoint")
                            + " look at where removed elements used to be. There is nothing in "
                            + "this document at those coordinates — that is what removed means — "
                            + "so the view is framed on the extent the baseline recorded."));

                    _session.Record(ActivityKind.Reconcile,
                        "made " + Ui.Count(made, "viewpoint") + " from the comparison");
                });

                var count = CompareClusters.Of(_result, cell).Count;

                return count == 0 ? (UIElement)Ui.Stack() : make;
            }

            private UIElement Keep()
            {
                StampPlan? planned = null;

                var stamp = Ui.Commit("Show me what it would stamp", "Stamp", "element",
                    plan: (job, into) =>
                {
                    planned = null;

                    // The adapter writes every custom property into one tab of its own, whatever
                    // category it was handed. Core names that tab by a literal it cannot check
                    // against the adapter's own constant, so this is where the two are held to
                    // agree — a mismatch would write the stamp somewhere the search sets below
                    // will never find it.
                    if (!string.Equals(CompareStamp.Category, NavWriteTransaction.TabName,
                                       StringComparison.Ordinal))
                    {
                        into.Children.Add(Ui.Problem(
                            "This build writes custom properties to the \"" + NavWriteTransaction.TabName
                            + "\" tab, but the stamp names \"" + CompareStamp.Category
                            + "\". The search sets would not find it, so nothing was written. "
                            + "This is a bug in the build rather than anything you did."));
                        return 0;
                    }

                    var plan = CompareStamp.Plan(_result);

                    if (plan.IsEmpty)
                    {
                        into.Children.Add(Ui.Empty("Nothing to stamp.", "Nothing changed."));
                        return 0;
                    }

                    planned = plan;

                    into.Children.Add(Ui.Line(plan.Explain(), 0.85));

                    if (plan.Unreachable > 0)
                        into.Children.Add(Ui.Note(Ui.Count(plan.Unreachable, "element")
                            + " in the comparison no longer resolves in the open model and would be "
                            + "skipped."));

                    foreach (var set in CompareStamp.Sets(_result))
                        into.Children.Add(Ui.Line("Search set it would make: \"" + set.Name + "\" — "
                            + Ui.Count(set.Count, "element"), 0.75));

                    into.Children.Add(Ui.Note("Ctrl+Z does not reverse a custom property write — "
                        + "the host's undo does not reach them. \"Take the stamp off\" is the way back."));

                    return plan.Count;
                },
                    apply: job =>
                {
                    var plan = planned;
                    if (plan == null) return;

                    job.Say("Writing " + ComparePolicy.Count(plan.Count) + " elements...");

                    using (var transaction = new NavWriteTransaction(_session.Model, "CamelWorks: stamp the comparison"))
                    {
                        CompareStamp.Apply(transaction, plan);

                        var preview = transaction.Preview();
                        var written = transaction.Commit();

                        _output.Children.Add(Ui.Line(
                            "Stamped " + Ui.Count(written, "element") + " with "
                            + CompareStamp.Category + " / " + CompareStamp.ChangeName + ".", 1, true));

                        if (preview.Unresolved.Count > 0)
                            _output.Children.Add(Ui.Note(Ui.Count(preview.Unresolved.Count, "element")
                                + " stopped resolving between the comparison and the write, so they "
                                + "were skipped."));
                    }

                    _session.Record(ActivityKind.Write, "stamped the comparison onto the model", plan.Explain());
                });

                var unstamp = Ui.Button("Take the stamp off", () =>
                {
                    _output.Children.Clear();

                    var keys = new List<ElementKey>();

                    foreach (var bucket in new[]
                             {
                                 ChangeBucket.Added, ChangeBucket.Moved,
                                 ChangeBucket.Changed, ChangeBucket.Unchanged,
                             })
                        keys.AddRange(_result.KeysIn(bucket));

                    using var transaction = new NavWriteTransaction(_session.Model, "CamelWorks: take the comparison stamp off");

                    CompareStamp.Clear(transaction, keys);

                    _output.Children.Add(Ui.Done("Cleared on " + Ui.Count(transaction.Commit(), "element")
                                                 + "."));
                });

                var raise = Ui.Button("Raise them on the board", () =>
                {
                    _output.Children.Clear();

                    var findings = CompareIssues.Raise(_result);

                    if (findings.Count == 0)
                    {
                        _output.Children.Add(Ui.Empty("Nothing to raise.",
                            "Nothing was added, removed or changed."));
                        return;
                    }

                    foreach (var finding in findings)
                        _output.Children.Add(Ui.Line(finding.Title + " — "
                            + Ui.Count(finding.Children.Count, "element"), 0.85));

                    _output.Children.Add(Ui.Note("Moves are left off by default: on most revisions "
                        + "they are hundreds of elements settling by a few millimetres, and a board "
                        + "full of them is a board nobody opens."));

                    _output.Children.Add(Ui.Line("Raised " + Ui.Count(findings.Count, "issue")
                        + " on the board. Coordinate → Triage to work through them, or "
                        + "Coordinate → BCF to send them out.", 0.85));

                    _session.Record(ActivityKind.Reconcile,
                        "raised " + Ui.Count(findings.Count, "issue") + " from the comparison");
                });

                var report = Ui.Button("Save the report", () =>
                {
                    _output.Children.Clear();
                    Report();
                });

                // Four zero-argument commands, in two groups that are not the same kind of act.
                // Saving a report and raising issues produce something new; stamping writes into
                // the model itself and Ctrl+Z does not reach it. One row of four identical
                // outlines put those on the same footing, which is exactly the footing a write
                // must not share with an export.
                return Ui.Stack(
                    Ui.Eyebrow("Take it away"),
                    Ui.Across(report, raise),
                    Walk(),
                    Ui.Rule(),
                    Ui.Eyebrow("Write it into the model"),
                    Ui.Across(stamp, unstamp),
                    Ui.Small("Ctrl+Z does not undo a property write. \"Take the stamp off\" is the "
                             + "way back, and it clears only what the stamp wrote."));
            }

            private void Report()
            {
                var folder = Folder(_session);

                var stem = Deliverable.Safe(_session.Profile.ProjectName + " - model compare");

                try
                {
                    var document = CompareReport.Build(_result, _session.Profile.ProjectName);

                    var html = Path.Combine(folder, stem + ".html");
                    var book = Path.Combine(folder, stem + ".xlsx");

                    File.WriteAllText(html, HtmlReportWriter.Write(document));
                    File.WriteAllBytes(book, XlsxWriter.Write(CompareReport.Workbook(_result)));

                    _output.Children.Add(Ui.Line("Written:", 1, true));
                    _output.Children.Add(Ui.Line(html, 0.8));
                    _output.Children.Add(Ui.Line(book, 0.8));
                    _output.Children.Add(Ui.Line("The spreadsheet has every row; the page stops at "
                        + ComparePolicy.Count(CompareReport.PrintCap) + " so it stays readable.", 0.7));

                    _session.Record(ActivityKind.Report, "wrote the compare report", _result.Explain());
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // Baselines
        // -------------------------------------------------------------------------------------

        // The list of what has been recorded, and the way to delete one, used to be a screen of
        // its own - a tab called Baselines. It is a fold on Compare now: the act it exists around,
        // "Take a baseline", was already on Compare, so the two of them were one tool the whole
        // time and the tab was where its history went to be looked at separately.

        private static void Redraw(SnapshotStore store, StackPanel listed, StackPanel results)
        {
            listed.Children.Clear();

            var baselines = store.Baselines();

            if (baselines.Count == 0)
            {
                listed.Children.Add(Ui.Empty("Nothing recorded yet.",
                    store.IsMemoryOnly
                        ? "This document has never been saved, so there is nowhere beside it to keep one."
                        : "Take one with the button above. It is the whole of the setup."));
                return;
            }

            BaselineEntry? picked = null;

            var table = Ui.Table(baselines.Select(b => new TableRow(b,
                    b.Label.Length > 0 ? b.Label : "(unlabelled)",
                    b.TakenUtc.HasValue
                        ? b.TakenUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm",
                            CultureInfo.InvariantCulture) + "Z"
                        : "unknown",
                    ComparePolicy.Count(b.Elements),
                    ComparePolicy.Count(b.Models),
                    Show(b.Watch))),
                ("Baseline", 200), ("Taken", 150), ("Elements", 80), ("Models", 70), ("Properties", 0))
                .OnPick(item => picked = item as BaselineEntry);

            listed.Children.Add(Ui.Eyebrow("Recorded"));
            listed.Children.Add(Ui.Small(store.Folder ?? "nowhere — this document is unsaved"));

            listed.Children.Add(Ui.Stack(
                table,
                // Named and asked twice. A red button whose only description of its target is
                // "the selected one" is a button that deletes something you cannot check first,
                // and a baseline is a week of history nothing else in the product can rebuild.
                Ui.Danger("Delete the selected one", () =>
                {
                    results.Children.Clear();

                    if (picked == null)
                    {
                        results.Children.Add(Ui.Note("Pick one in the list first."));
                        return;
                    }

                    var gone = picked;

                    results.Children.Add(Ui.Danger("Yes, delete " + gone.Describe(), () =>
                    {
                        results.Children.Clear();

                        results.Children.Add(store.Delete(gone)
                            ? Ui.Done("Deleted " + gone.Describe() + ".")
                            : Ui.Problem(store.LastProblem ?? "It could not be deleted."));

                        Redraw(store, listed, results);
                    }));
                })));
        }

        // -------------------------------------------------------------------------------------
        // Taking one
        // -------------------------------------------------------------------------------------

        private static UIElement Take(Session session, SnapshotStore store, StackPanel results,
                                      Action? afterwards = null, bool primary = true)
        {
            var label = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var field = Ui.Text(label, text => label = text ?? string.Empty, 220);

            var button = Ui.Runner("Take a baseline", results, primary: primary, work: job =>
            {
                job.Say("Walking the model...");

                var snapshot = SnapshotBuilder.Take(
                    session.Model, DateTimeOffset.UtcNow, label, Settings,
                    documentPath: session.SavedPath, units: session.Document.Units.ToString(),
                    keepGoing: () => !job.IsCancelled,
                    onProgress: p => job.Step(p.Kept, "elements recorded"));

                if (job.IsCancelled)
                {
                    results.Children.Add(Ui.Note("Stopped. Nothing was saved."));
                    return;
                }

                // Everything with geometry, not a subset: a baseline is what a comparison a week
                // from now will be held to, and an element it did not measure is one that
                // comparison can never report as remodelled.
                if (_readTriangles)
                {
                    job.Say("Reading triangles...");

                    var measured = new NavCompare(session.Model).Deepen(
                        snapshot, null, _triangleCap,
                        () => !job.IsCancelled,
                        done => job.Step(done, "elements measured"));

                    results.Children.Add(Ui.Line("Measured " + Ui.Count(measured, "element")
                        + ". Next week's comparison can tell a remodelling from a move.", 0.75));
                }

                job.Say("Saving...");

                var entry = store.Save(snapshot);

                if (entry == null)
                {
                    results.Children.Add(Ui.Problem(store.LastProblem ?? "It could not be saved."));
                    return;
                }

                _pickedBaseline = entry.Describe();

                results.Children.Add(Ui.Line("Recorded " + entry.Describe() + ".", 1, true));
                results.Children.Add(Ui.Line("Kept in " + store.Folder + ".", 0.75));

                session.Record(ActivityKind.Reconcile, "recorded a baseline: " + entry.Describe());

                afterwards?.Invoke();
            });

            return Ui.Stack(Ui.Field("Call it", field), button);
        }

        /// <summary>
        /// The ribbon's Baseline button: record the model as it stands, with no screen at all.
        ///
        /// Named for the day it is used on. Somebody pressing this on a Friday is marking "what we
        /// issued", and asking them to name it first would put a dialog in front of a one-click
        /// habit — the Baselines tab is where a name gets chosen when the name matters.
        /// </summary>
        public static string? BaselineNow()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            var store = StoreFor(session);

            if (store.IsMemoryOnly)
                return "This document has never been saved, so there is nowhere beside it to keep a "
                       + "baseline. Save the document first, then press this again.";

            var snapshot = SnapshotBuilder.Take(
                session.Model, DateTimeOffset.UtcNow,
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z",
                Settings,
                documentPath: session.SavedPath,
                units: session.Document.Units.ToString());

            var entry = store.Save(snapshot);

            if (entry == null) return store.LastProblem ?? "The baseline could not be saved.";

            _pickedBaseline = entry.Describe();
            session.Record(ActivityKind.Reconcile, "recorded a baseline: " + entry.Describe());

            return "Recorded " + entry.Describe() + ".\n\nKept in " + store.Folder
                   + ".\n\nCompare against it from the CamelWorks pane, Compare → Run.";
        }

        // -------------------------------------------------------------------------------------
        // Odds and ends
        // -------------------------------------------------------------------------------------

        private static SnapshotStore StoreFor(Session session) =>
            new SnapshotStore(PhysicalFileSystem.Instance,
                              SnapshotStore.FolderFor(session.SavedPath, UserFolder()));

        private static string? UserFolder()
        {
            try
            {
                var data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrEmpty(data) ? null : Path.Combine(data, "CamelWorks");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Folder(Session session)
        {
            try
            {
                var saved = session.SavedPath;

                if (!string.IsNullOrWhiteSpace(saved))
                {
                    var beside = Path.GetDirectoryName(saved);
                    if (!string.IsNullOrEmpty(beside)) return beside!;
                }

                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return string.IsNullOrEmpty(documents) ? "." : documents;
            }
            catch (Exception)
            {
                return ".";
            }
        }

        /// <summary>
        /// Roughly how many elements this document has, for the size estimate.
        ///
        /// From the intake register rather than a walk: the estimate exists to be shown BEFORE
        /// anybody waits for anything, and walking a federation to work out how long walking a
        /// federation takes would be its own joke.
        /// </summary>
        private static int Guess(Session session)
        {
            var total = 0;

            foreach (var record in ModelManifest.Of(session.Model.Models).Models)
                total += record.Items ?? 0;

            return total > 0 ? total : session.Model.Models.Count * 20000;
        }

        private static string Show(PropertyWatch watch) => watch switch
        {
            PropertyWatch.All => "Every property",
            PropertyWatch.None => "No properties",
            _ => "Standard list",
        };

        private static string Show(KeyRung rung) => rung switch
        {
            KeyRung.InstanceGuid => "GUID only",
            KeyRung.TreePath => "GUID or tree path",
            _ => "GUID, tree path or shape",
        };
    }
}
