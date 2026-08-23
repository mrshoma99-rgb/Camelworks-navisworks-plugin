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

        private static readonly CompareSettings Settings = new CompareSettings();
        private static string? _pickedBaseline;
        private static bool _deepGeometry;

        // -------------------------------------------------------------------------------------
        // Run
        // -------------------------------------------------------------------------------------

        /// <summary>The Run screen.</summary>
        public static UIElement Run()
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Compare"), Ui.Problem(Host.NoModel)));

            var store = StoreFor(session);
            var baselines = store.Baselines();
            var results = new StackPanel();

            var body = Ui.Stack(
                Ui.Heading("Compare"),
                Ui.Sub("What changed since a baseline. Added, removed, moved and changed — with the "
                       + "evidence for each match, and a plain list of what this baseline cannot answer."),
                Against(session, store, baselines, results),
                Terms(),
                Ui.Runner("Compare", results, job => Go(session, store, results, job)),
                results);

            return Ui.Scroll(body);
        }

        private static UIElement Against(Session session, SnapshotStore store,
                                         IReadOnlyList<BaselineEntry> baselines, StackPanel results)
        {
            if (baselines.Count == 0)
            {
                // The whole of the first-run experience. No form, no template, no settings — one
                // button, and from next week the tab is useful.
                return Ui.Card("No baseline yet",
                    "Nothing has been recorded for this document, so there is nothing to compare "
                    + "against. Take one now and every comparison from here on has something to work from.",
                    Ui.Line(SnapshotBuilder.Estimate(Guess(session), Settings.Watch), 0.75),
                    Take(session, store, results),
                    store.IsMemoryOnly
                        ? Ui.Note("This document has never been saved, so there is nowhere beside it "
                                  + "to keep a baseline. Save the document first.")
                        : Ui.Line("It will be kept in " + store.Folder + ".", 0.7));
            }

            var labels = baselines.Select(b => b.Describe()).ToList();

            if (_pickedBaseline == null || !labels.Contains(_pickedBaseline))
                _pickedBaseline = labels[0];

            return Ui.Card("Compare against", "The most recent is picked for you.",
                Ui.Field("Baseline", Ui.Choice(labels, _pickedBaseline, pick => _pickedBaseline = pick, 320)),
                Ui.Across(Take(session, store, results)));
        }

        private static UIElement Terms()
        {
            var watch = new[] { "Standard list", "Every property", "No properties" };

            return Ui.Card("What counts as a change",
                "Every default here works on a raw model. They are on the screen so a row you "
                + "disagree with has an answer better than \"the tool said so\".",

                Ui.Field("Move over", Ui.Text(
                    Settings.Tolerance.ToString("0.####", CultureInfo.InvariantCulture),
                    text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                            && value >= 0)
                            Settings.Tolerance = value;
                    }, 90)),

                Ui.Line("Model units. Below this an element has not moved — which is what stops a "
                        + "re-export's floating-point noise reading as a moved federation.", 0.7),

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

                Ui.Check("Read the actual triangles (slow — minutes on a federation)", _deepGeometry,
                         value => _deepGeometry = value),

                Ui.Line("The triangles are the only way to see an element remodelled inside its own "
                        + "bounding box. Everything else here is free; that one is not, which is why "
                        + "it is a choice.", 0.7));
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

            if (_deepGeometry)
            {
                job.Say("Reading triangles...");
                new NavCompare(session.Model).Deepen(after, NavCompare.DefaultCap,
                                                     () => !job.IsCancelled,
                                                     read => job.Step(read, "elements measured"));
            }

            job.Say("Comparing...");

            var result = ModelCompare.Of(before, after, Settings);
            Last = result;

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

            results.Children.Add(Ui.Card("Which model was held to be which",
                result.Pairing.Explain(),
                Ui.Table(result.Models.Select(m => new TableRow(m,
                        m.Title,
                        ComparePolicy.Count(m.Added),
                        ComparePolicy.Count(m.Removed),
                        ComparePolicy.Count(m.Moved),
                        ComparePolicy.Count(m.Changed),
                        ModelPairing.Show(m.Strength) + " — " + m.Reason)),
                    ("Model", 150), ("Added", 60), ("Removed", 70), ("Moved", 60),
                    ("Changed", 70), ("Paired on", 0))));

            foreach (var note in result.Notes) results.Children.Add(Ui.Note(note));

            results.Children.Add(Ui.Line("The change list is on the Changes tab.", 0.75));
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
                             "Run a comparison on the Run tab and the list appears here.")));

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
                    Ui.Heading("Changes"),
                    Ui.Sub(_result.Between() + " — " + _result.Explain()),
                    Filters(),
                    _list,
                    Paint(),
                    Walk(),
                    Keep(),
                    _output);
            }

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

                _list.Children.Add(Ui.Table(rows,
                        ("Change", 80), ("Element", 170), ("Type", 130), ("Model", 110),
                        ("What changed", 260), ("Matched on", 0))
                    .OnPick(item => Look(item as ElementChange)));

                if (_result.TotalDropped > 0)
                    _list.Children.Add(Ui.Note(Ui.Count(_result.TotalDropped, "row")
                        + " are not listed — the list is capped so it stays usable. The counts above "
                        + "are complete, and the report has every row."));
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

                var paint = Ui.Runner("Paint it onto the model", _output, job =>
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

                    foreach (var note in view.Notes) _output.Children.Add(Ui.Note(note));

                    _session.Record(ActivityKind.Appearance, "painted the comparison onto the model",
                                    _result.Explain());
                });

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

                    _output.Children.Add(Ui.Line("Cleared the comparison's colour and fade on "
                        + Ui.Count(keys.Count, "element") + ". Nothing else was touched.", 0.8));
                });

                return Ui.Card("Paint",
                    "Added green, removed red, moved amber, changed blue, everything else faded. "
                    + "Through the appearance stack, so it is one row you can switch off rather "
                    + "than an override somebody has to undo by hand.",
                    Ui.Across(paint, clear),
                    view.Uncolourable > 0
                        ? Ui.Note(Ui.Count(view.Uncolourable, "removed element")
                                  + " cannot be painted: they are not in this document, which is "
                                  + "what removed means. Append the baseline model beside the "
                                  + "current one and they appear in red.")
                        : null);
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

                var make = Ui.Runner("Make a viewpoint for each place", _output, job =>
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
                        if (!job.Step(made, "viewpoints")) break;

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

                return Ui.Card("Walk it",
                    "The changes grouped by where they are, one viewpoint each, biggest first.",
                    Ui.Line(count == 0
                        ? "Nothing to walk."
                        : Ui.Count(count, "place") + " where something happened, at "
                          + cell.ToString("0.#", CultureInfo.InvariantCulture)
                          + " model units apart or closer.", 0.8),
                    make);
            }

            private UIElement Keep()
            {
                var stamp = Ui.Runner("Stamp it into the properties", _output, job =>
                {
                    // The adapter writes every custom property into one tab of its own, whatever
                    // category it was handed. Core names that tab by a literal it cannot check
                    // against the adapter's own constant, so this is where the two are held to
                    // agree — a mismatch would write the stamp somewhere the search sets below
                    // will never find it.
                    if (!string.Equals(CompareStamp.Category, NavWriteTransaction.TabName,
                                       StringComparison.Ordinal))
                    {
                        _output.Children.Add(Ui.Problem(
                            "This build writes custom properties to the \"" + NavWriteTransaction.TabName
                            + "\" tab, but the stamp names \"" + CompareStamp.Category
                            + "\". The search sets would not find it, so nothing was written. "
                            + "This is a bug in the build rather than anything you did."));
                        return;
                    }

                    var plan = CompareStamp.Plan(_result);

                    if (plan.IsEmpty)
                    {
                        _output.Children.Add(Ui.Empty("Nothing to stamp.", "Nothing changed."));
                        return;
                    }

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

                    _output.Children.Add(Ui.Note("Ctrl+Z does not reverse a custom property write — "
                        + "the host's undo does not reach them. \"Take the stamp off\" is the way back."));

                    foreach (var set in CompareStamp.Sets(_result))
                        _output.Children.Add(Ui.Line("Search set: \"" + set.Name + "\" — "
                            + Ui.Count(set.Count, "element"), 0.75));

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

                    _output.Children.Add(Ui.Line("Cleared on " + Ui.Count(transaction.Commit(), "element")
                                                 + ".", 0.8));
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

                return Ui.Card("Keep it",
                    "A comparison that lives only on this screen is one nobody else can act on.",
                    Ui.Across(stamp, unstamp),
                    Ui.Line("The stamp writes " + CompareStamp.Category + " / " + CompareStamp.ChangeName
                            + " onto each element, so the host's own search finds them, a selection "
                            + "set re-resolves, and a clash test can be pointed at only what changed.", 0.7),
                    Ui.Across(raise, report));
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

        /// <summary>The Baselines screen.</summary>
        public static UIElement Baselines()
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Baselines"), Ui.Problem(Host.NoModel)));

            var store = StoreFor(session);
            var results = new StackPanel();
            var listed = new StackPanel();

            Redraw(store, listed, results);

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Baselines"),
                Ui.Sub("What has been recorded, and when. Kept beside the document rather than in "
                       + "your own folder, so a job handed over arrives with its history."),
                Ui.Card("Record one now",
                    "Give it a name you will recognise in three months — an issue number, a "
                    + "milestone, a date.",
                    Take(session, store, results, () => Redraw(store, listed, results))),
                listed,
                results));
        }

        private static void Redraw(SnapshotStore store, StackPanel listed, StackPanel results)
        {
            listed.Children.Clear();

            var baselines = store.Baselines();

            if (baselines.Count == 0)
            {
                listed.Children.Add(Ui.Empty("Nothing recorded yet.",
                    store.IsMemoryOnly
                        ? "This document has never been saved, so there is nowhere beside it to keep one."
                        : "Take one above. It is the whole of the setup."));
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

            listed.Children.Add(Ui.Card("Recorded", store.Folder ?? "nowhere — this document is unsaved",
                table,
                Ui.Button("Delete the selected one", () =>
                {
                    results.Children.Clear();

                    if (picked == null)
                    {
                        results.Children.Add(Ui.Note("Pick one in the list first."));
                        return;
                    }

                    var gone = picked;

                    results.Children.Add(store.Delete(gone)
                        ? Ui.Line("Deleted " + gone.Describe() + ".", 0.8)
                        : Ui.Problem(store.LastProblem ?? "It could not be deleted."));

                    Redraw(store, listed, results);
                })));
        }

        // -------------------------------------------------------------------------------------
        // Taking one
        // -------------------------------------------------------------------------------------

        private static UIElement Take(Session session, SnapshotStore store, StackPanel results,
                                      Action? afterwards = null)
        {
            var label = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var field = Ui.Text(label, text => label = text ?? string.Empty, 220);

            var button = Ui.Runner("Take a baseline", results, job =>
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
