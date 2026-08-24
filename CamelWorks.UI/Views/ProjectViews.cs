using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api.Plugins;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Project workspace: is this federation fit to work on, what did CamelWorks work out about
    /// it, what is loaded, and the IFC exporter.
    /// </summary>
    public static class ProjectViews
    {
        /// <summary>The dock pane key of the BIMCamel IFC exporter, which ships alongside.</summary>
        public const string ExporterPaneKey = "BIMCamel.ExportDockPane.BIMCamel";

        // -----------------------------------------------------------------------------------
        // Health
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Is this federation fit to work on: one button, one answer, and the reasons under it.
        ///
        /// Every check here runs on demand and none of them needs anything set up first. They used
        /// to be five cards, each drawing its own conclusion in its own words, which left the
        /// reader to work out which of five conclusions was the one that mattered. They are one
        /// sweep now, over one pass of the model, ranked worst first — because the question anybody
        /// opens this tab with has a single answer and it is not five paragraphs long.
        ///
        /// The IDS card below is a genuinely different act: those are the client's rules, in a file
        /// somebody with a contract wrote, and nothing on it is CamelWorks' opinion about the data.
        /// </summary>
        public static UIElement Health() => Ui.Scroll(Ui.Stack(Sweep(), Ids()));

        /// <summary>Where CamelWorks keeps the manifest of what the last check saw.</summary>
        private const string IntakeKey = "intake";

        /// <summary>The check whose findings the put-it-back buttons answer.</summary>
        private const string Placed = "Placement";

        /// <summary>The check whose findings the folder search answers.</summary>
        private const string Federated = "The federation";

        /// <summary>
        /// The sweep: every built-in check, one traversal, one verdict.
        ///
        /// One traversal because the four checks between them used to walk the whole federation
        /// four times over, and on a real project each pass is minutes. They want different things
        /// from each element and there is no reason to ask twice.
        /// </summary>
        private static UIElement Sweep()
        {
            var results = Ui.Stack();
            var codes = CodeChoice();

            var run = Ui.Runner("Check this federation", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var review = new FederationReview();
                var actions = Ui.Stack();

                var loaded = Loaded(session, review);
                var reading = Walk(session, job, codes);

                if (reading == null) return;   // stopped

                BuiltIn(reading, review);
                var intake = Arrivals(session, reading, review, actions);
                CodeCheck(session, reading, codes, review);

                session.Record(ActivityKind.Reconcile, "swept the federation: " + review);

                results.Children.Add(Answer(review));
                results.Children.Add(Ui.Small(reading.Explain(session)));

                // The remedy goes under the finding that asks for it. A fix appended after five
                // findings is a fix nobody connects to the one that said "put it back below".
                foreach (var finding in review.Findings)
                {
                    results.Children.Add(Found(session, finding));

                    if (finding.Check == Placed && actions.Children.Count > 0)
                    {
                        results.Children.Add(actions);
                        actions = Ui.Stack();
                    }

                    if (finding.Check == Federated && loaded.AnyMissing)
                    {
                        results.Children.Add(Repoint(session));
                        loaded = (loaded.Table, false);
                    }
                }

                foreach (var skipped in review.Missing) results.Children.Add(Ui.Small("Not checked: " + skipped));

                if (actions.Children.Count > 0) results.Children.Add(actions);

                results.Children.Add(Ui.Section("What is loaded",
                    Ui.Count(session.Model.Models.Count, "model") + " in this federation", false,
                    loaded.Table,
                    intake));
            });

            return Ui.Marked(Glyphs.Tick, "Is this fit to coordinate against?",
                "Placement, geometry, what has arrived and what it is coded as, in one pass over "
                + "the models.",
                run,
                codes.Fold,
                results);
        }

        /// <summary>The verdict, said once and in one line.</summary>
        private static UIElement Answer(FederationReview review)
        {
            var headline = Ui.Heading(review.Verdict());

            headline.Tint(TextBlock.ForegroundProperty,
                review.Grade == ReviewGrade.Fault ? "RedTx"
                : review.Grade == ReviewGrade.Watch ? "AmberTx"
                : "Ok");

            return Ui.Stack(
                headline,
                Ui.Across(
                    Ui.Figure(Measure.Count(review.Faults), "to settle",
                              review.Faults > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(Measure.Count(review.Watches), "worth a look",
                              review.Watches > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(Measure.Count(review.Clean), "clean")));
        }

        /// <summary>One finding, in the vocabulary's own shape.</summary>
        private static UIElement Found(Session session, ReviewFinding finding) =>
            Ui.Finding(finding.Grade == ReviewGrade.Fault, finding.Check, finding.Headline,
                       finding.Detail, finding.Fix,
                       finding.Examples.Count == 0 ? null : string.Join(", ", finding.Examples),
                       Reach(session, finding));

        /// <summary>
        /// The way to the elements a finding is about, or nothing when it is not about elements.
        ///
        /// A federation check answers a question about the model, and until now that answer was a
        /// sentence: "37 elements sit on top of each other at the origin" and no way to look at one.
        /// The reader's next move is always the same - see them - and it was the one thing the tab
        /// would not do.
        ///
        /// A finding about models rather than geometry (a missing file, a moved source) has nothing
        /// to select, so it gets no row at all rather than a button that would do nothing.
        /// </summary>
        private static UIElement? Reach(Session session, ReviewFinding finding)
        {
            if (finding.Affected.Count == 0) return null;

            var keys = finding.Affected;

            var show = Ui.Quiet("Show me " + (keys.Count == 1 ? "it" : "all " + Ui.Count(keys.Count, "of them")),
                () =>
                {
                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);
                });

            var only = Ui.Quiet("Leave only these on screen", () =>
            {
                session.Model.Select(keys);
                session.View.ZoomTo(keys, 2.0);
                Isolate(session, keys);
            });

            var row = Ui.Across(show, only);
            row.Margin = new Thickness(0, 6, 0, 0);
            return row;
        }

        /// <summary>Hide everything the finding is not about, so what is left is what it found.</summary>
        private static void Isolate(Session session, IReadOnlyList<ElementKey> keep)
        {
            var wanted = new HashSet<ElementKey>(keep);
            var hide = new List<ElementKey>();

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                if (item.HasGeometry && !wanted.Contains(item.Key)) hide.Add(item.Key);

            session.View.SetVisible(hide, false);
            session.View.SetVisible(keep, true);
        }

        /// <summary>
        /// The federation itself: what is loaded, from where, and whether the source has moved on.
        ///
        /// The last column is the one that matters on a Monday. Navisworks will happily open a
        /// federation whose sources changed a fortnight ago and show it without comment, and the
        /// coordination that follows is against a model nobody is building.
        /// </summary>
        private static (UIElement Table, bool AnyMissing) Loaded(Session session, FederationReview review)
        {
            const string Check = Federated;

            var savedAt = FileTime(session.SavedPath);
            var rows = new List<TableRow>();
            var missing = new List<string>();
            var stale = new List<string>();

            foreach (var model in session.Model.Models)
            {
                var when = FileTime(model.SourcePath);
                string state;
                Tone tone;

                if (string.IsNullOrWhiteSpace(model.SourcePath))
                {
                    state = "no source path";
                    tone = Tone.Plain;
                }
                else if (when == null)
                {
                    state = "not found on disk";
                    tone = Tone.Bad;
                    missing.Add(model.DisplayName);
                }
                else if (savedAt != null && when > savedAt)
                {
                    state = "changed since this file was saved";
                    tone = Tone.Warn;
                    stale.Add(model.DisplayName);
                }
                else
                {
                    state = when.Value.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
                    tone = Tone.Plain;
                }

                rows.Add(new TableRow(model,
                    model.DisplayName,
                    session.Profile.DisciplineOf(model.Scope) ?? "-",
                    state,
                    model.SourcePath)
                {
                    Tone = tone,
                });
            }

            review.Ran(Check);

            if (missing.Count > 0)
                review.Fault(Check, Ui.Count(missing.Count, "model") + " has no file on disk",
                    "A model whose source cannot be found still displays: Navisworks is showing its "
                    + "own cached copy, which is as old as the last time anybody opened this file.",
                    "Repoint it, or refresh from the folder it moved to, before coordinating.",
                    missing);

            if (stale.Count > 0)
                review.Watch(Check, Ui.Count(stale.Count, "model") + " has changed since this file was saved",
                    "The federation is showing what was published when it was saved, not what is on "
                    + "disk now.",
                    "Refresh, then run this again.",
                    stale);

            if (rows.Count == 0)
                return (Ui.Empty("Nothing is loaded.", "Append or merge a model to start."), false);

            return (Ui.Table(rows, ("Model", 150), ("Discipline", 100), ("Source", 190), ("Path", 0)),
                    missing.Count > 0);
        }

        /// <summary>
        /// Everything the checks want from the model, gathered in one pass.
        ///
        /// Deliberately dumb: it reads, it does not judge. Each check gets the same reading and
        /// draws its own conclusions, which is what makes one traversal enough for all of them.
        /// </summary>
        private sealed class Reading
        {
            /// <summary>Every element, as the geometry rules see it.</summary>
            public List<HealthElement> Elements { get; } = new List<HealthElement>();

            /// <summary>How many elements with geometry each loaded model contributed.</summary>
            public Dictionary<string, int> Geometry { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>The coded value on each element, when a code table asked for one.</summary>
            public List<string?> Codes { get; } = new List<string?>();

            /// <summary>What was read, in one line.</summary>
            public string Explain(Session session) =>
                Ui.Count(Elements.Count, "element") + " across "
                + Ui.Count(session.Model.Models.Count, "model") + ", read once.";
        }

        /// <summary>One pass over the federation, filling a <see cref="Reading"/>. Null when stopped.</summary>
        private static Reading? Walk(Session session, Job job, CodeSource codes)
        {
            var reading = new Reading();
            var scale = session.MetresPerUnit;
            var (category, name) = SplitProperty(codes.Property);
            var wanted = codes.Table != null && name.Length > 0;

            job.Say("Reading the models...");

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!job.Step(reading.Elements.Count, "elements read")) return null;

                var bounds = item.Bounds;

                reading.Elements.Add(new HealthElement(item.Key.ToString(), item.Model.DisplayName)
                {
                    Key = item.Key,
                    Name = item.DisplayName,
                    Category = item.Category,
                    X = bounds.CentreX * scale,
                    Y = bounds.CentreY * scale,
                    Z = bounds.CentreZ * scale,
                    SizeX = bounds.SizeX * scale,
                    SizeY = bounds.SizeY * scale,
                    SizeZ = bounds.SizeZ * scale,

                    // Only the zero case is ever asked about, and counting every property on
                    // every element of a federation costs more than the whole rest of the check.
                    PropertyCount = item.Properties().Any() ? 1 : 0,
                });

                if (item.HasGeometry)
                {
                    var scope = item.Model.Scope;
                    reading.Geometry[scope] = reading.Geometry.TryGetValue(scope, out var seen) ? seen + 1 : 1;
                }

                if (wanted)
                    reading.Codes.Add(category.Length == 0
                        ? Anywhere(item, name)
                        : item.Property(category, name));
            }

            return reading;
        }

        /// <summary>The six geometry and data rules that ship with the product.</summary>
        private static void BuiltIn(Reading reading, FederationReview review)
        {
            var report = ModelHealth.Check(reading.Elements);

            review.Ran("Geometry");

            foreach (var finding in report.Findings)
                review.Add(new ReviewFinding(ModelHealth.GradeOf(finding), "Geometry",
                                             finding.Summary, null, finding.Fix, finding.Examples,
                                             finding.Affected));
        }

        /// <summary>
        /// What arrived since the last sweep, and whether anything moved.
        ///
        /// The question that opens every coordination meeting — who published and who did not —
        /// answered today by opening properties on eight files. And the one nothing else catches: a
        /// model the federation is now holding somewhere else, because somebody dragged it in the
        /// units-and-transform dialog and nobody noticed. Every clash from that moment on is in the
        /// wrong place.
        /// </summary>
        private static UIElement Arrivals(Session session, Reading reading,
                                          FederationReview review, Panel actions)
        {
            const string Check = "Arrivals";

            var manifest = ModelManifest.Of(session.Model.Models, session.Profile,
                                            itemCounts: reading.Geometry, version: Ui.Release,
                                            takenUtc: DateTimeOffset.UtcNow);

            var section = session.Store.Section(ProjectStore.ModelsSection);
            var before = ModelManifest.FromJson(section[IntakeKey]);
            var intake = manifest.Since(before.Models.Count > 0 ? before : null);

            review.Ran(Check);

            Placement(session, intake, review, actions);

            if (intake.Missing.Count > 0)
                review.Watch(Check, Ui.Count(intake.Missing.Count, "model")
                    + " the last sweep saw is no longer in the federation",
                    "Either it was deliberately removed, or an append was lost when somebody saved.",
                    null,
                    intake.Missing.Select(m => m.DisplayName).ToList());

            var body = Ui.Stack(Ui.Line(intake.Explain(), 1, true), Ui.Line(manifest.Explain(), 0.8));

            if (intake.NothingNew && intake.Missing.Count == 0)
                body.Children.Add(Ui.Info("Nothing has been republished since the last sweep. A run "
                    + "against exactly last week's files produces exactly last week's board - worth "
                    + "knowing before the meeting rather than during it."));

            var units = session.Profile.Units;
            var table = manifest.ModelTable(units);

            body.Children.Add(Ui.Table(table.Rows.Select(r => new TableRow(r,
                r[0] ?? string.Empty, r[2] ?? string.Empty, r[3] ?? string.Empty,
                r[5] ?? string.Empty, r[6] ?? string.Empty)).ToList(),
                ("Model", 200), ("Source last written", 150), ("Size", 80),
                ("Placement", 170), ("Elements", 0)));

            if (intake.HasPrevious)
            {
                var register = intake.Table();

                body.Children.Add(Ui.Eyebrow("Since the last sweep"));

                body.Children.Add(Ui.Table(register.Rows.Select(r => new TableRow(r,
                    r[0] ?? string.Empty, r[1] ?? string.Empty, r[2] ?? string.Empty,
                    r[3] ?? string.Empty)).ToList(),
                    ("Model", 200), ("Discipline", 120), ("State", 200), ("Source last written", 0)));
            }

            section.Set(IntakeKey, manifest.ToJson());
            session.Store.Save();

            return body;
        }

        /// <summary>
        /// The models the federation has moved, and the offer to put them back.
        ///
        /// Previewed before it writes and reversible after: the previous placement is in the record
        /// this sweep just replaced, so putting a model back is the same operation as moving it and
        /// leaves the same trail.
        /// </summary>
        private static void Placement(Session session, ModelIntake intake,
                                      FederationReview review, Panel actions)
        {
            const string Check = Placed;

            var carried = session.Model.Models.Where(m => m.HasTransform).ToList();

            review.Ran(Check);

            if (intake.Moved.Count > 0)
            {
                review.Fault(Check, Ui.Count(intake.Moved.Count, "model")
                    + " has been moved by the federation since the last sweep",
                    "Nobody is told when this happens, and every clash from that moment on is in "
                    + "the wrong place.",
                    "Run the clash tests again once it is back.",
                    intake.Moved.Select(m => m.DisplayName).ToList());

                foreach (var model in intake.Moved)
                {
                    var record = model;

                    if (!intake.Before.TryGetValue(record.Scope, out var was) || was.Placement == null)
                        continue;

                    var source = session.Model.Models.FirstOrDefault(
                        m => string.Equals(m.Scope, record.Scope, StringComparison.Ordinal));

                    if (source == null) continue;

                    actions.Children.Add(Ui.Button("Put " + record.DisplayName + " back where it was", () =>
                    {
                        var problem = session.Model.Restore(source, was.Placement);

                        actions.Children.Add(problem == null
                            ? Ui.Done(record.DisplayName + " is back where the last sweep found it. "
                                      + "Run the clash tests again - everything on the board was "
                                      + "computed against the other placement.")
                            : Ui.Problem(problem));

                        if (problem == null)
                            session.Record(ActivityKind.Reconcile,
                                "put " + record.DisplayName + " back to its previous placement");
                    }));
                }
            }

            if (carried.Count > 0)
                review.Watch(Check, Ui.Count(carried.Count, "model") + " carries a transform",
                    "That is legitimate - it is how a survey-grid model is brought onto a project "
                    + "grid - and it is worth knowing which ones, because a transform is also the "
                    + "thing nobody remembers applying.",
                    null,
                    carried.Select(m => m.DisplayName).ToList());
        }

        /// <summary>
        /// Whether a coded property holds values anybody agreed to.
        ///
        /// The other half of a code table. Assigning from one guarantees what CamelWorks wrote;
        /// this is the check over what is already there — values that arrived with the model, were
        /// written by another tool, or were typed before the table existed.
        /// </summary>
        private static void CodeCheck(Session session, Reading reading, CodeSource codes,
                                  FederationReview review)
        {
            const string Check = "Codes";

            if (codes.Table == null)
            {
                review.Skipped(Check, "no code table on this project. Project > Profile takes one "
                                      + "pasted straight out of the spreadsheet your client sent");
                return;
            }

            var table = Tables(session).Find(codes.Table);

            if (table == null)
            {
                review.Skipped(Check, "the table " + codes.Table + " is no longer on this project");
                return;
            }

            var (_, name) = SplitProperty(codes.Property);

            if (name.Length == 0)
            {
                review.Skipped(Check, "say which property carries the code, under the button");
                return;
            }

            var coverage = table.Check(reading.Codes);

            review.Ran(Check);

            if (coverage.Coded == 0 && coverage.Invalid == 0)
            {
                review.Watch(Check, "Nothing carries " + name,
                    "Not one element in the federation has that property, so " + table.Name
                    + " has had nothing to check.",
                    "Check the property name under the button - it is written \"Category / Name\", "
                    + "or a bare name to look in any category.");

                return;
            }

            if (coverage.Invalid > 0)
                review.Fault(Check, Ui.Count(coverage.Invalid, "element")
                    + " carries a value " + table.Name + " does not list",
                    "Either the table is missing a row or the model is carrying a value nobody "
                    + "agreed to, and the difference matters: one is a five-second edit and the "
                    + "other is a conversation.",
                    null,
                    coverage.InvalidValues.Take(6).ToList());
        }

        /// <summary>Which table and property the code check should use.</summary>
        private sealed class CodeSource
        {
            /// <summary>The chosen table, or null when the project has none.</summary>
            public string? Table { get; set; }

            /// <summary>The property carrying the code, as "Category / Name" or a bare name.</summary>
            public string Property { get; set; } = string.Empty;

            /// <summary>The disclosure holding the two, or null when there is nothing to choose.</summary>
            public UIElement? Fold { get; set; }
        }

        /// <summary>
        /// The one thing on this panel anybody can be asked to choose, folded away below the button.
        ///
        /// It defaults to the first table and to a property named after it, which is right often
        /// enough that most people never open it. A project with no code tables gets no fold at
        /// all rather than an empty one: the sweep says the check was skipped and why.
        /// </summary>
        private static CodeSource CodeChoice()
        {
            var source = new CodeSource();
            var session = Host.Current;

            if (session == null) return source;

            var tables = Tables(session);

            if (tables.IsEmpty) return source;

            source.Table = tables.Names[0];
            source.Property = tables.Names[0];

            var property = Ui.Text(source.Property, typed => source.Property = typed, 240);

            var picker = Ui.Choice(tables.Names, source.Table, pick =>
            {
                source.Table = pick;
                property.Text = pick;
            });

            source.Fold = Ui.Section("Which property carries the code",
                () => source.Table + " · " + source.Property, false,
                Ui.Form(
                    Ui.Field("Table", picker),
                    Ui.Field("Property", property, "\"Category / Name\", or a bare name for any category")));

            return source;
        }

        /// <summary>The project's code tables, or an empty set. Never null: no table is not an error.</summary>
        private static CodeTables Tables(Session session) =>
            CodeTables.FromJson(session.Store.Section(ProjectStore.ProfileSection)["codeTables"]);

        /// <summary>"Category / Name" into its two halves. A bare name means any category.</summary>
        private static (string Category, string Name) SplitProperty(string? text)
        {
            var whole = (text ?? string.Empty).Trim();
            var slash = whole.IndexOf('/');

            return slash < 0
                ? (string.Empty, whole)
                : (whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
        }

        /// <summary>The first property with this name in any category, or null.</summary>
        private static string? Anywhere(IModelItem item, string name)
        {
            foreach (var property in item.Properties())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(property.Value))
                    return property.Value;

            return null;
        }

        /// <summary>
        /// The client's own rules, run against the federation.
        ///
        /// An IDS is an employer's information requirements written so a machine can read them —
        /// "every wall carries a fire rating from this list", in a file, from whoever is paying for
        /// the model. Nothing on this card is CamelWorks' opinion about your data. The file is the
        /// rules, and the only thing to set up is the file, which somebody else already wrote.
        ///
        /// The hard part is that IDS speaks IFC and a federation does not. See
        /// <see cref="IdsProjection"/> for how the two are bridged, and note that every guess it
        /// makes is reported on screen rather than folded silently into the result.
        /// </summary>
        private static UIElement Ids()
        {
            var results = Ui.Stack();

            // The file a client sent is the file they will send a revision of, and the property
            // their exporter puts the IFC class in does not change between Tuesdays.
            var kept = ToolMemory.Of(Host.Current?.Store, "project.ids");

            var path = Ui.Text(kept.Text("file"), text => kept.Set("file", text), 340);
            var classSource = Ui.Text(kept.Text("classFrom"), text => kept.Set("classFrom", text), 200);

            var run = Ui.Runner("Check against an IDS", results, primary: false, work: job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    results.Children.Add(Ui.Problem("Type the path of the .ids file your client sent."));
                    return;
                }

                if (!File.Exists(file))
                {
                    results.Children.Add(Ui.Problem("There is no file at " + file + "."));
                    return;
                }

                IdsReadResult read;
                using (var stream = File.OpenRead(file)) read = IdsReader.Read(stream);

                foreach (var warning in read.Warnings) results.Children.Add(Ui.Note(warning));

                if (!read.IsUsable || read.Document == null)
                {
                    results.Children.Add(Ui.Empty("Nothing to check.",
                        "That file read as IDS but carried no specifications CamelWorks could use."));
                    return;
                }

                var document = read.Document;
                var projection = IdsProjection.For(document, PropertyAddress.Parse(classSource.Text));
                var elements = new List<IdsElement>();

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(projection.Read, "elements read")) return;

                    var element = projection.From(item);
                    if (element != null) elements.Add(element);
                }

                job.Say("Checking...");
                var report = IdsChecker.Check(document, elements);

                results.Children.Add(Ui.Line(document.ToString(), 1, true));
                results.Children.Add(Ui.Line(report.ToString(), 0.85));

                results.Children.Add(Ui.Across(
                    Ui.Figure(report.Satisfied.ToString(CultureInfo.InvariantCulture), "met",
                              report.Satisfied > 0 ? Tone.Good : Tone.Plain),
                    Ui.Figure(report.Unsatisfied.ToString(CultureInfo.InvariantCulture), "failed",
                              report.Unsatisfied > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(report.NotApplicable.ToString(CultureInfo.InvariantCulture), "matched nothing",
                              report.NotApplicable > 0 ? Tone.Warn : Tone.Plain)));

                results.Children.Add(Reach(projection));

                foreach (var note in Caveats(document, projection)) results.Children.Add(note);

                var rows = report.Results.Select(r => new TableRow(r,
                    r.Specification.Name,
                    r.NothingApplied && !r.CountOutOfRange ? "-" : r.Applicable.ToString("N0", CultureInfo.InvariantCulture),
                    r.NothingApplied ? "-" : r.Passed.ToString("N0", CultureInfo.InvariantCulture),
                    r.Failed > 0 ? r.Failed.ToString("N0", CultureInfo.InvariantCulture) : "-",
                    Verdict(r))
                {
                    Tone = ToneOf(r),
                }).ToList();

                var table = Ui.Table(rows,
                    ("Specification", 250), ("Applies to", 80), ("Pass", 60), ("Fail", 60), ("", 0));

                table.OnPick(picked =>
                {
                    if (!(picked is IdsSpecificationResult result)) return;

                    var live = Host.Current;
                    if (live == null) return;

                    var keys = new List<ElementKey>();

                    foreach (var violation in result.Violations)
                    {
                        if (ElementKey.TryParse(violation.ElementId, out var key)) keys.Add(key);
                    }

                    if (keys.Count > 0) live.Model.Select(keys);
                });

                results.Children.Add(table);
                results.Children.Add(Ui.Line("Pick a row to select that specification's failing elements.", 0.6));

                // The panel answers "are we ready to issue". The workbook answers "what do I fix",
                // and it is asked by somebody who is not sitting in Navisworks.
                results.Children.Add(Ui.Actions(Ui.Primary("Save it as a workbook...",
                    () => Workbook(results, document, report, session.NowTicks))));

                foreach (var detail in Details(report)) results.Children.Add(detail);

                // At the press, which is the moment somebody committed to the path and the
                // property name. Next Tuesday's revision opens on both.
                kept.Keep();

                session.Record(ActivityKind.Exchange, "checked against " + document.Title + ": " + report, file);
            });

            return Ui.Marked(Glyphs.Report, "The client's own rules",
                "Answer an IDS — the openBIM way of writing information requirements down — against "
                + "this federation. Point it at the file and press the button; there is nothing else "
                + "to fill in.",
                Ui.Form(Ui.PathField("IDS file", path, PathKind.Open, "IDS|*.ids|All files|*.*")),
                run,
                Ui.Section("Where the IFC class is read from", "wherever the readers put it", false,
                    Ui.Form(Ui.Field("IFC class from", classSource,
                                     "leave it empty unless a run tells you to fill it in")),
                    Ui.Small("IDS is written about IFC classes, and a federation only carries those "
                             + "where a model came from IFC — so CamelWorks looks in the places the "
                             + "readers put them and believes a value only if it reads like a class "
                             + "name. A Revit export saying \"Basic Wall\" is a family, not an "
                             + "IfcWall, and treating it as one would make every row meaningless.")),
                Ui.Rule(),
                Author(results),
                results);
        }

        /// <summary>
        /// The other direction: the requirements this project sets, rather than the ones it is set.
        ///
        /// A client's IDS answers "have we given them what they asked for". It says nothing about
        /// what the job has decided among itself, which is argued about all week and then enforced
        /// by eye. Written as an IDS, the same file checks the model here, travels to the architect
        /// and comes back with the next revision.
        /// </summary>
        private static UIElement Author(Panel results)
        {
            var open = Ui.Button("Write an IDS of our own...", () =>
            {
                var problem = IdsWindow.Show();
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Ui.Eyebrow("Our own rules"),
                Ui.Small("The properties this job has decided have to be filled in, and on what. "
                         + "It opens on the specification everybody writes first, keeps the draft "
                         + "in the project as IDS itself, and saves a .ids anybody's tool reads."),
                Ui.Actions(open));
        }

        /// <summary>Write the check out as the two sheets somebody works through.</summary>
        private static void Workbook(Panel results, IdsDocument document, IdsReport report, long ticks)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the IDS results",
                Filter = "Excel workbook|*.xlsx",
                FileName = document.Title + " - IDS.xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx",
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllBytes(dialog.FileName, IdsSheet.Write(document, report, ticks));
            }
            catch (Exception e)
            {
                results.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                return;
            }

            results.Children.Add(Ui.Done("Wrote " + dialog.FileName
                + " - one sheet per job: the verdicts, and the failures element by element."));
        }

        /// <summary>How much of the federation the check could actually see.</summary>
        private static UIElement Reach(IdsProjection projection) => Ui.Line(
            Ui.Count(projection.Read, "element") + " read · "
            + Ui.Count(projection.Kept, "element") + " carried something this IDS asks about · "
            + Ui.Count(projection.Classed, "element") + " carried an IFC class", 0.7);

        /// <summary>The things a reader has to know before believing the rows.</summary>
        private static IEnumerable<UIElement> Caveats(IdsDocument document, IdsProjection projection)
        {
            if (projection.Classed == 0 && projection.Read > 0)
            {
                yield return Ui.Problem(
                    "Nothing in this federation carried an IFC class, so every specification written "
                    + "about an IFC entity matched nothing. That is a property-naming difference, not "
                    + "an empty model.");

                var candidates = projection.Candidates;

                yield return candidates.Count > 0
                    ? Ui.Note("These properties hold values that read like IFC classes. Put one in the "
                              + "\"IFC class from\" box and run it again: "
                              + string.Join(", ", candidates.Select(c => c.ToString())))
                    : Ui.Note("No property anywhere in the federation held a value beginning with "
                              + "\"Ifc\", so there is nothing to point the box at. The models were "
                              + "almost certainly not exported through IFC.");
            }

            var unanswerable = IdsProjection.Unanswerable(document);

            if (unanswerable.Count > 0)
                yield return Ui.Note(
                    Ui.Count(unanswerable.Count, "specification") + " below ask what an element is "
                    + "aggregated into, which import does not carry across — the tree above an element "
                    + "is a model and a layer, not an IfcBuildingStorey. They will read as failures "
                    + "here and should be answered against the IFC itself: "
                    + string.Join(", ", unanswerable));
        }

        /// <summary>Why each unsatisfied specification failed, split by requirement.</summary>
        private static IEnumerable<UIElement> Details(IdsReport report)
        {
            foreach (var result in report.Results.Where(r => !r.IsSatisfied && r.Violations.Count > 0))
            {
                yield return Ui.Line(result.Specification.Name, 1, true);

                var byRequirement = result.Violations
                    .GroupBy(v => v.Requirement)
                    .OrderByDescending(g => g.Count());

                foreach (var group in byRequirement)
                {
                    var missing = group.Count(v => v.Failure == IdsFailure.Missing);
                    var wrong = group.Count(v => v.Failure == IdsFailure.WrongValue);
                    var prohibited = group.Count(v => v.Failure == IdsFailure.Prohibited);

                    var parts = new List<string>();
                    if (missing > 0) parts.Add(missing.ToString("N0", CultureInfo.InvariantCulture) + " missing it");
                    if (wrong > 0) parts.Add(wrong.ToString("N0", CultureInfo.InvariantCulture) + " with a value it does not accept");
                    if (prohibited > 0) parts.Add(prohibited.ToString("N0", CultureInfo.InvariantCulture) + " carrying what it forbids");

                    yield return Ui.Line(group.Key.Describe() + " — " + string.Join(", ", parts), 0.75);

                    if (group.Key.Instructions != null)
                        yield return Ui.Line(group.Key.Instructions, 0.6);
                }

                if (result.ViolationsTruncated)
                    yield return Ui.Line("Only the first "
                        + IdsChecker.DefaultViolationCap.ToString("N0", CultureInfo.InvariantCulture)
                        + " failures were kept, so the split above counts those and the totals count all of them.", 0.6);
            }
        }

        /// <summary>The last column: what happened, in a word or two.</summary>
        private static string Verdict(IdsSpecificationResult result)
        {
            if (result.CountOutOfRange) return "wrong number of elements";
            if (result.NothingApplied) return "matched nothing";
            return result.Failed == 0 ? "met" : "not met";
        }

        private static Tone ToneOf(IdsSpecificationResult result)
        {
            if (result.IsSatisfied) return Tone.Good;
            return result.NothingApplied && !result.CountOutOfRange ? Tone.Warn : Tone.Bad;
        }

        // -----------------------------------------------------------------------------------
        // Setup
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// What CamelWorks already derived, with an override on each line.
        ///
        /// <b>This screen is skippable and says so.</b> Every value on it already has an answer, so
        /// nothing here has to be visited before any other feature works — which is the difference
        /// between a product you can try in a minute and one that starts with an afternoon of forms.
        /// </summary>
        /// <summary>
        /// The project file, as a screen.
        ///
        /// <b>It is created lazily and nothing blocks on it.</b> Every screen in the product works
        /// against a raw model with none of this filled in; what is here makes the second project
        /// faster, not the first one possible. A coordinator who never opens this tab gets the
        /// whole product.
        ///
        /// Four things live here because all four are answers about the project rather than about
        /// the model: who the parties are, what the priorities are called, which values a coded
        /// property is allowed to hold, and the three operations that write into the model itself.
        /// </summary>
        public static UIElement Profile()
        {
            var session = Host.Current;

            if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

            // Three cards, and each earns one: they are three different acts with three different
            // consequences, and the glyphs are there so that is legible before the titles are read.
            return Ui.Scroll(Ui.Stack(
                Ui.Small("Kept " + session.Store.Where),
                Parties(session),
                Codes(session),
                ModelWrites(session)));
        }

        /// <summary>Who a clash can be assigned to, and what the priorities are called.</summary>
        private static UIElement Parties(Session session)
        {
            var status = Ui.Stack();

            var parties = Ui.Text(string.Join(", ", session.Profile.Parties), null, 380);
            var priorities = Ui.Text(string.Join(", ", session.Profile.Priorities), null, 380);

            var save = Ui.Button("Save", () =>
            {
                status.Children.Clear();

                Override(session, ProfileKeys.Parties, parties.Text);
                Override(session, ProfileKeys.Priorities, priorities.Text);

                status.Children.Add(session.Store.Save()
                    ? Ui.Done("Saved. The board's assign picker and the report's per-party split "
                              + "both read this.")
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
            });

            return Ui.Marked(Glyphs.Onward, "Who a clash can be given to",
                "Comma separated. Derived from the disciplines in the loaded models until you say "
                + "otherwise.",
                Ui.Form(
                    Ui.Field("Parties", parties),
                    Ui.Field("Priorities", priorities)),
                Ui.Actions(save),
                Ui.Small("Leave these alone and the board still works: the assignee is free text and "
                         + "the priorities are the built-in three. Filling them in turns two free-text "
                         + "boxes into pickers, which is worth about a minute a week."),
                status);
        }

        /// <summary>
        /// The values a coded property is allowed to hold.
        ///
        /// The point is the refusal. A lookup that silently passes an unlisted value does nothing:
        /// the whole reason a project keeps a code list is that "L3-MECH-01" and "L03 MECH 1" are
        /// the same thing to a person and two different things to every downstream tool, and the
        /// only moment either can be caught is the moment somebody writes one.
        /// </summary>
        private static UIElement Codes(Session session)
        {
            var status = Ui.Stack();
            var list = Ui.Stack();

            var name = Ui.Text("Uniclass", null, 180);
            var body = Ui.Text(string.Empty, null, 420);

            var tables = CodeTables.FromJson(
                session.Store.Section(ProjectStore.ProfileSection)["codeTables"]);

            void Redraw()
            {
                list.Children.Clear();

                if (tables.IsEmpty)
                {
                    list.Children.Add(Ui.Empty("No code tables.",
                        "Paste one in above. Everything works without one; a table turns a free-text "
                        + "property into a checked one."));

                    return;
                }

                foreach (var tableName in tables.Names)
                {
                    var table = tables.Find(tableName)!;
                    var mine = tableName;

                    list.Children.Add(Ui.Across(
                        Ui.Pill(table.ToString()),
                        Ui.Button("Load", () =>
                        {
                            name.Text = mine;
                            body.Text = table.ToText();
                        }),
                        Ui.Danger("Remove", () =>
                        {
                            tables.Remove(mine);
                            Store(session, tables, status);
                            Redraw();
                        })));
                }
            }

            var add = Ui.Button("Save this table", () =>
            {
                status.Children.Clear();

                var parsed = CodeTable.Parse(name.Text, body.Text);

                if (parsed.IsEmpty && tables.Find(name.Text) == null)
                {
                    status.Children.Add(Ui.Problem("Nothing in that box reads as a code. One row per "
                        + "line: the code, then optionally a description and a parent, separated by "
                        + "commas or tabs."));

                    return;
                }

                tables.Put(parsed);
                Store(session, tables, status);

                status.Children.Add(Ui.Line(parsed.IsEmpty
                    ? "Removed - an empty table means you do not want it, not that you want an empty one."
                    : parsed.ToString() + " saved. Data can now assign from it, and Health reports "
                      + "anything out of it.", 0.8));

                Redraw();
            });

            Redraw();

            return Ui.Marked(Glyphs.Table, "Code tables",
                "The values a coded property is allowed to hold, and the ones it is not.",
                list,
                Ui.Section("Paste one in", "code, description, parent - one per line", false,
                    Ui.Form(
                        Ui.Field("Name", name),
                        Ui.Field("Rows", body, "commas or tabs between them, and a line starting "
                                               + "with # is a comment")),
                    add,
                    Ui.Small("Paste the column straight out of the spreadsheet your client sent.")),
                status);
        }

        private static void Store(Session session, CodeTables tables, Panel status)
        {
            session.Store.Section(ProjectStore.ProfileSection).Set("codeTables", tables.ToJson());

            if (!session.Store.Save())
                status.Children.Add(Ui.Problem(session.Store.LastSaveProblem
                                               ?? "Could not write the project file."));
        }

        /// <summary>
        /// The three things that write into the model, each behind a count of what it would touch.
        ///
        /// These are off the ribbon on purpose. Writing into a federation is the one destructive
        /// thing this product does, and it is destructive in a way Ctrl+Z does not reach — so the
        /// buttons live where somebody has to go looking for them, and each one says how many
        /// elements it would affect before it does anything.
        /// </summary>
        private static UIElement ModelWrites(Session session)
        {
            var status = Ui.Stack();

            var count = Ui.Runner("Count what CamelWorks has written", status, job =>
            {
                var carrying = Carrying(session, job, out var read);

                if (read < 0) return;

                status.Children.Add(Ui.Line(Ui.Count(carrying.Count, "element")
                    + " of " + Ui.Count(read, "element") + " carry a CamelWorks property.", 1, true));

                if (carrying.Count == 0)
                {
                    status.Children.Add(Ui.Note("Nothing to remove. CamelWorks keeps its work in the "
                        + "project file beside the model; properties are only written when you ask "
                        + "for them on the Data tab."));

                    return;
                }

                status.Children.Add(Ui.Button("Remove them from " + Ui.Count(carrying.Count, "element"),
                    () => Strip(session, carrying, status)));
            });

            return Ui.Marked(Glyphs.Bin, "Take back what CamelWorks wrote",
                "Everything the product has put into the model itself, and the way to remove it.",
                count,
                Ui.Small("Ctrl+Z does not undo a property write - Navisworks does not put custom "
                         + "property writes on its undo stack. What CamelWorks wrote is recorded in "
                         + "the project journal, and removing it here is the way back."),
                status);
        }

        /// <summary>Every element carrying anything in the CamelWorks tab.</summary>
        private static List<ElementKey> Carrying(Session session, Job job, out int read)
        {
            var carrying = new List<ElementKey>();
            read = 0;

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!job.Step(read, "elements read")) { read = -1; return carrying; }

                read++;

                foreach (var property in item.Properties())
                {
                    if (!string.Equals(property.Category, NavWriteTransaction.TabName,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrEmpty(property.Value)) continue;

                    carrying.Add(item.Key);
                    break;
                }
            }

            return carrying;
        }

        /// <summary>Take every CamelWorks property back off the elements that carry one.</summary>
        private static void Strip(Session session, IReadOnlyList<ElementKey> keys, Panel status)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                var item = session.Model.Resolve(key);
                if (item == null) continue;

                foreach (var property in item.Properties())
                    if (string.Equals(property.Category, NavWriteTransaction.TabName,
                                      StringComparison.OrdinalIgnoreCase))
                        names.Add(property.Name);
            }

            int written;

            using (var write = new NavWriteTransaction(session.Model, "CamelWorks: remove properties"))
            {
                foreach (var key in keys)
                    foreach (var property in names)
                        write.RemoveProperty(key, NavWriteTransaction.TabName, property);

                written = write.Commit();
            }

            status.Children.Add(Ui.Done("Removed " + Ui.Count(names.Count, "property") + " from "
                + Ui.Count(written, "element") + "."));

            session.Record(ActivityKind.Write,
                "removed CamelWorks properties from " + Ui.Count(written, "element"));
        }

        /// <summary>Write one profile override and leave the rest alone.</summary>
        private static void Override(Session session, string key, string? value)
        {
            var setting = session.Profile.Find(key);
            if (setting != null) setting.Override = value;

            session.Store.Section(ProjectStore.ProfileSection)
                   .Set(key, JsonValue.String(string.IsNullOrWhiteSpace(value) ? null : value!.Trim()));
        }

        public static UIElement Setup()
        {
            var session = Host.Current;

            if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

            var profile = session.Profile;
            var status = Ui.Stack();
            var rows = Ui.Stack();

            void Fill()
            {
                rows.Children.Clear();
                foreach (var setting in profile.Settings) rows.Children.Add(Row(setting));
            }

            Fill();

            // The way out of a decision somebody regrets. Every row on this tab is an override of
            // something CamelWorks worked out for itself, and a tab that can only be overridden is
            // one people are afraid to touch.
            var reset = Ui.Quiet("Reset them all to derived", () =>
            {
                status.Children.Clear();

                foreach (var setting in profile.Settings) setting.Override = null;

                var section = session.Store.Section(ProjectStore.ProfileSection);

                foreach (var key in section.Keys.ToList())
                    if (profile.Find(key) != null) section.Remove(key);

                Fill();

                status.Children.Add(session.Store.Save()
                    ? Ui.Done("Every row is back to what CamelWorks worked out from the models.")
                    : Ui.Problem(session.Store.LastSaveProblem ?? "The project file could not be written."));
            });

            var save = Ui.Primary("Save overrides", () =>
            {
                status.Children.Clear();

                // Written whole rather than merged: the profile object already carries every
                // override in force, including the ones loaded from the file.
                var saved = profile.OverridesToJson();
                var section = session.Store.Section(ProjectStore.ProfileSection);

                foreach (var key in section.Keys.ToList()) section.Remove(key);
                foreach (var key in saved.Keys) section.Set(key, saved[key]);

                status.Children.Add(session.Store.Save()
                    ? Ui.Done("Saved " + Ui.Count(saved.Keys.Count, "override") + " to " + session.Store.Where)
                    : Ui.Problem(session.Store.LastSaveProblem ?? "The project file could not be written."));
            });

            // The project file's own problems are reported here, where the file is, rather than
            // on the front door where they were three banners between the user and their week.
            var file = Ui.Stack(Ui.Split("Project file", session.Store.Where));
            var broken = Ui.Stack();

            if (session.Store.UsedBackup)
                broken.Children.Add(Ui.Note("The project file could not be read and its backup was used "
                                            + "instead. Nothing is lost that was saved before the last write."));

            if (session.Store.ConcurrencyNote.Length > 0)
                broken.Children.Add(Ui.Note(session.Store.ConcurrencyNote));

            // The failures first when there are any. A tab reporting that the project file could
            // not be read, and putting that below thirty rows of settings, is a tab that has
            // filed its own alarm at the bottom of the scroll.
            return Ui.Scroll(Ui.Stack(
                broken,
                Ui.Marked(Glyphs.Tune, "What CamelWorks worked out", null,
                          Ui.Form(rows), Ui.Actions(save, reset), status),
                People(session),
                file));
        }

        /// <summary>
        /// Who is on the job.
        ///
        /// A party typed into a box on the board is a party typed differently next week, and a
        /// board filtered by it quietly splits in two. And an assignment is really to a person: a
        /// person has an address, which is the difference between a BCF file somebody receives and
        /// one that sits in a folder waiting to be noticed.
        ///
        /// Optional, like everything else here. With nobody on it the board behaves exactly as it
        /// did - a party is whatever somebody types.
        /// </summary>
        private static UIElement People(Session session)
        {
            var roster = TeamRoster.FromJson(
                session.Store.Section(ProjectStore.ProfileSection)["roster"]);

            var list = Ui.Stack();
            var status = Ui.Stack();

            var name = Ui.Text(string.Empty, "who", null, 160);
            var party = Ui.Text(string.Empty, "which organisation", null, 160);
            var email = Ui.Text(string.Empty, "where a BCF file reaches them", null, 200);

            void Keep()
            {
                session.Store.Section(ProjectStore.ProfileSection).Set("roster", roster.ToJson());

                status.Children.Clear();

                status.Children.Add(session.Store.Save()
                    ? Ui.Done(roster.Explain())
                    : Ui.Problem(session.Store.LastSaveProblem ?? "The project file could not be written."));
            }

            void Fill()
            {
                list.Children.Clear();

                foreach (var member in roster.Members)
                {
                    var here = member;
                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                    row.Children.Add(Ui.Check(string.Empty, here.IsActive, on =>
                    {
                        here.IsActive = on;
                        Keep();
                    }));

                    row.Children.Add(Ui.Line(here.Name, here.IsActive ? 1 : 0.5, true));

                    if (here.Party != null) row.Children.Add(Ui.Pill(here.Party, Tone.Plain));

                    row.Children.Add(here.Email == null
                        ? Ui.Pill("no address", Tone.Warn)
                        : Ui.Line(here.Email, 0.65));

                    row.Children.Add(Ui.Icon(Glyphs.Bin, "Take them off the roster", () =>
                    {
                        roster.Remove(here.Id);
                        Fill();
                        Keep();
                    }));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 8, 0);

                    list.Children.Add(row);
                }

                if (roster.Members.Count == 0)
                    list.Children.Add(Ui.Small("Nobody yet. A party is whatever somebody types on "
                        + "the board until there is."));
            }

            Fill();

            var add = Ui.Button("Add", () =>
            {
                status.Children.Clear();

                var who = name.Text.Trim();

                if (who.Length == 0)
                {
                    status.Children.Add(Ui.Problem("Type a name."));
                    return;
                }

                // Through Add, so typing a name that is already there edits that row rather than
                // making a second one - two rows with one name would make every assignment to that
                // name ambiguous, since the name is the identity.
                roster.Add(who, party.Text, email.Text);

                name.Text = string.Empty;
                email.Text = string.Empty;

                Fill();
                Keep();
            });

            return Ui.Marked(Glyphs.Tune, "Who is on the job",
                "The organisations and the people in them. The board offers these when a group is "
                + "assigned, and more than one person can carry a group.",
                list,
                Ui.Form(
                    Ui.Pair(Ui.Field("Name", name), Ui.Field("Organisation", party)),
                    Ui.Field("Address", email,
                             "Optional, and the difference between a BCF file somebody receives "
                             + "and one that waits in a folder to be noticed.")),
                Ui.Actions(add),
                Ui.Small("Unticking somebody keeps them on the roster and out of every list. Their "
                         + "work does not leave when they do, and the board says so."),
                status);
        }

        private static UIElement Row(ProfileSetting setting)
        {
            var value = Ui.Stack();

            FrameworkElement editor = setting.Choices.Count > 0
                ? (FrameworkElement)Ui.Choice(setting.Choices, setting.Value, pick => setting.Override = pick, 200)
                : Ui.Text(setting.Value, typed => setting.Override = typed, 200);

            value.Children.Add(editor);

            // "derived" or "yours", not a sentence each. Why it was derived that way is what the
            // tooltip is for; eight controls with sixteen lines of grey prose under them is the
            // panel reading as a form.
            editor.ToolTip = setting.Because;

            return Ui.Field(setting.Title, value,
                            setting.IsOverridden ? "yours" : "derived from " + (setting.Derived ?? "nothing"));
        }

        // -----------------------------------------------------------------------------------
        // Repointing a model whose file has gone
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Where a model whose file has gone might have gone to.
        ///
        /// Offered only when the sweep found a model with no file on disk, and framed as the fix
        /// for that finding rather than as a tool of its own — nobody goes looking for moved files
        /// on a federation where nothing has moved.
        /// </summary>
        private static UIElement Repoint(Session session)
        {
            var results = Ui.Stack();
            var folder = Ui.Text(string.Empty, null, 300);

            var run = Ui.Runner("Look for missing sources", results, job =>
            {
                var where = folder.Text.Trim();

                if (where.Length == 0)
                {
                    results.Children.Add(Ui.Line("Type a folder to search first.", 0.8));
                    return;
                }

                if (!Directory.Exists(where))
                {
                    results.Children.Add(Ui.Problem("There is no folder at " + where + "."));
                    return;
                }

                var found = 0;
                var looked = 0;

                foreach (var model in session.Model.Models)
                {
                    if (!job.Step(looked++, session.Model.Models.Count, "models checked")) return;

                    if (string.IsNullOrWhiteSpace(model.SourcePath) || File.Exists(model.SourcePath)) continue;

                    var name = System.IO.Path.GetFileName(model.SourcePath);

                    var candidates = Directory.GetFiles(where, name, SearchOption.AllDirectories);

                    if (candidates.Length == 0)
                    {
                        results.Children.Add(Ui.Line(model.DisplayName + " — no file called " + name
                                                     + " under that folder.", 0.75));
                        continue;
                    }

                    found++;
                    results.Children.Add(Ui.Line(model.DisplayName + " — found at " + candidates[0], 1, true));

                    if (candidates.Length > 1)
                        results.Children.Add(Ui.Line("and " + (candidates.Length - 1) + " more with the same name; "
                            + "the first is shown.", 0.55));
                }

                if (found == 0 && looked > 0)
                    results.Children.Add(Ui.Empty("Nothing to repoint.",
                        "Every loaded model's source file is where the federation expects it."));
                else
                    results.Children.Add(Ui.Note("CamelWorks reports where the files are; it does not rewrite "
                        + "the federation. Repointing is done in Navisworks with Project > Refresh or by "
                        + "re-appending the file, so that what changes your project file is always your own "
                        + "deliberate action."));
            });

            return Ui.Stack(
                Ui.PathField("Search a folder for them", folder, PathKind.Folder),
                run,
                results);
        }

        private static DateTime? FileTime(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                      || e is ArgumentException || e is NotSupportedException)
            {
                // An unreachable share or a path the OS will not even parse. Unknown, not missing:
                // telling somebody their model is gone because a VPN dropped would be worse.
                return null;
            }
        }

        // -----------------------------------------------------------------------------------
        // Export IFC
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The IFC exporter, which is its own pane.
        ///
        /// Opened rather than re-hosted. The exporter is a finished tool with its own screen, and
        /// duplicating it here to make the tab look full would mean two copies of a settings form
        /// that must not disagree.
        /// </summary>
        public static UIElement ExportIfc()
        {
            var status = Ui.Stack();

            var open = Ui.Primary("Open the IFC exporter", () =>
            {
                status.Children.Clear();

                var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(ExporterPaneKey);

                if (!(record is DockPanePluginRecord pane))
                {
                    status.Children.Add(Ui.Problem(
                        "The BIMCamel IFC exporter is not installed in this Navisworks. It is a separate "
                        + "free plug-in from bimcamel.com with its own installer; CamelWorks opens it when "
                        + "it is there and says this when it is not. Check too that its build matches this "
                        + "Navisworks release."));
                    return;
                }

                if (!pane.IsLoaded) pane.LoadPlugin();

                if (pane.LoadedPlugin is DockPanePlugin dock)
                {
                    dock.Visible = true;
                    status.Children.Add(Ui.Line("Opened. It docks as its own panel.", 0.75));
                }
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Across(open),
                status));
        }
    }
}
