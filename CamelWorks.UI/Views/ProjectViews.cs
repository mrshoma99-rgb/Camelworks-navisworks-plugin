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
        /// Is this federation fit to work on: CamelWorks' own checks, and the client's.
        ///
        /// Both run on demand and neither needs anything set up first. The built-in rules ship
        /// with the product; the IDS card takes the file your client sent and answers it against
        /// the model, which is the same question asked by somebody with a contract behind them.
        /// </summary>
        public static UIElement Health() =>
            Ui.Scroll(Ui.Stack(Intake(), BuiltIn(), Federation(), Coded(), Ids()));

        /// <summary>
        /// Whether the coded properties hold values anybody agreed to.
        ///
        /// The other half of a code table. Assigning from one guarantees what CamelWorks wrote;
        /// this is the check over what is already there — values that arrived with the model, were
        /// written by another tool, or were typed before the table existed.
        /// </summary>
        private static UIElement Coded()
        {
            var session = Host.Current;

            if (session == null) return Ui.Card("Codes", Host.NoModel);

            var tables = CodeTables.FromJson(
                session.Store.Section(ProjectStore.ProfileSection)["codeTables"]);

            if (tables.IsEmpty)
                return Ui.Marked(Glyphs.Table, "Codes",
                    "Needs a code table",
                    Ui.Empty("No code tables on this project.",
                        "Project ▸ Profile takes one pasted straight out of the spreadsheet your "
                        + "client sent. Every other check here runs without one."));

            var results = Ui.Stack();
            var table = tables.Find(tables.Names[0])!;
            var property = Ui.Text(tables.Names[0], null, 240);

            var picker = Ui.Choice(tables.Names, tables.Names[0], pick =>
            {
                table = tables.Find(pick)!;
                property.Text = pick;
            });

            var run = Ui.Runner("Check the codes", results, primary: false, work: job =>
            {
                var (category, name) = SplitProperty(property.Text);

                if (name.Length == 0)
                {
                    results.Children.Add(Ui.Problem(
                        "Say which property carries the code, as \"Category / Name\" or just the name."));

                    return;
                }

                var values = new List<string?>();
                var read = 0;

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (read % 2000 == 0 && !job.Step(read, "elements read")) return;
                    read++;

                    values.Add(category.Length == 0
                        ? Anywhere(item, name)
                        : item.Property(category, name));
                }

                var coverage = table.Check(values);

                results.Children.Add(Ui.Line(coverage.Explain(), 1, true));

                results.Children.Add(Ui.Across(
                    Ui.Figure(Measure.Count(coverage.Coded), "in " + table.Name,
                              coverage.Coded > 0 ? Tone.Good : Tone.Plain),
                    Ui.Figure(Measure.Count(coverage.Uncoded), "carry nothing"),
                    Ui.Figure(Measure.Count(coverage.Invalid), "out of the table",
                              coverage.Invalid > 0 ? Tone.Bad : Tone.Good)));

                if (coverage.IsClean)
                {
                    results.Children.Add(Ui.Note("Everything that carries a value carries one "
                        + table.Name + " lists. Uncoded elements are a different question - a table "
                        + "says which values are allowed, not which elements need one."));

                    return;
                }

                results.Children.Add(Ui.Problem(Ui.Count(coverage.Invalid, "element")
                    + " carry a value " + table.Name + " does not list. Either the table is missing "
                    + "a row or the model is carrying a value nobody agreed to, and the difference "
                    + "matters: one is a five-second edit and the other is a conversation."));

                results.Children.Add(Ui.Table(
                    coverage.InvalidValues.Select(v => new TableRow(v, v) { Tone = Tone.Bad }).ToList(),
                    ("Not in " + table.Name, 0)));
            });

            return Ui.Card("Codes",
                "Whether a coded property holds values anybody agreed to",
                Ui.Field("Table", picker),
                Ui.Field("Property", property),
                run,
                results);
        }

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

        /// <summary>Where CamelWorks keeps the manifest of what the last check saw.</summary>
        private const string IntakeKey = "intake";

        /// <summary>
        /// What arrived this week, and whether anything moved.
        ///
        /// The question that opens every coordination meeting — who published and who did not —
        /// answered today by opening properties on eight files. And the one nothing else catches: a
        /// model the federation is now holding somewhere else, because somebody dragged it in the
        /// units-and-transform dialog and nobody noticed. Every clash from that moment on is in the
        /// wrong place.
        /// </summary>
        private static UIElement Intake()
        {
            var results = Ui.Stack();
            var actions = Ui.Stack();

            var run = Ui.Runner("Check what arrived", results, primary: false, work: job =>
            {
                actions.Children.Clear();

                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                job.Say("Reading the models...");

                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var read = 0;

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (read % 2000 == 0 && !job.Step(read, "elements read")) return;
                    read++;

                    if (!item.HasGeometry) continue;

                    var scope = item.Model.Scope;
                    counts[scope] = counts.TryGetValue(scope, out var seen) ? seen + 1 : 1;
                }

                var manifest = ModelManifest.Of(session.Model.Models, session.Profile,
                                                itemCounts: counts, version: Ui.Release,
                                                takenUtc: DateTimeOffset.UtcNow);

                var section = session.Store.Section(ProjectStore.ModelsSection);
                var before = ModelManifest.FromJson(section[IntakeKey]);

                var intake = manifest.Since(before.Models.Count > 0 ? before : null);

                results.Children.Add(Ui.Line(intake.Explain(), 1, true));
                results.Children.Add(Ui.Line(manifest.Explain(), 0.8));

                if (intake.NothingNew && intake.Missing.Count == 0)
                    results.Children.Add(Ui.Note(
                        "A run against exactly last week's files produces exactly last week's board. "
                        + "Worth knowing before the meeting rather than during it."));

                var units = session.Profile.Units;
                var table = manifest.ModelTable(units);

                var rows = table.Rows.Select(r => new TableRow(r,
                    r[0] ?? string.Empty, r[2] ?? string.Empty, r[3] ?? string.Empty,
                    r[5] ?? string.Empty, r[6] ?? string.Empty)).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Model", 200), ("Source last written", 150), ("Size", 80),
                    ("Placement", 170), ("Elements", 0)));

                if (intake.HasPrevious)
                {
                    var register = intake.Table();

                    results.Children.Add(Ui.Line("Since the last check", 1, true));

                    results.Children.Add(Ui.Table(register.Rows.Select(r => new TableRow(r,
                        r[0] ?? string.Empty, r[1] ?? string.Empty, r[2] ?? string.Empty,
                        r[3] ?? string.Empty)).ToList(),
                        ("Model", 200), ("Discipline", 120), ("State", 200), ("Source last written", 0)));
                }

                Placement(session, intake, actions);

                section.Set(IntakeKey, manifest.ToJson());
                session.Store.Save();
            });

            return Ui.Marked(Glyphs.Model, "Models",
                "What arrived, what is byte-identical, and whether the federation has moved anything",
                run,
                results,
                actions);
        }

        /// <summary>
        /// The models the federation has moved, and the offer to put them back.
        ///
        /// Previewed before it writes and reversible after: the previous placement is in the record
        /// this check just replaced, so putting a model back is the same operation as moving it and
        /// leaves the same trail.
        /// </summary>
        private static void Placement(Session session, ModelIntake intake, Panel actions)
        {
            var carried = session.Model.Models.Where(m => m.HasTransform).ToList();

            if (intake.Moved.Count == 0 && carried.Count == 0) return;

            if (intake.Moved.Count > 0)
            {
                actions.Children.Add(Ui.Problem(
                    Ui.Count(intake.Moved.Count, "model") + " has been moved by the federation since "
                    + "the last check: " + string.Join(", ", intake.Moved.Select(m => m.DisplayName))
                    + ". Nobody is told when this happens, and every clash from that moment on is in "
                    + "the wrong place."));

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
                            ? Ui.Line(record.DisplayName + " is back where the last check found it. "
                                      + "Run the clash tests again - everything on the board was "
                                      + "computed against the other placement.", 1, true)
                            : Ui.Problem(problem));

                        if (problem == null)
                            session.Record(ActivityKind.Reconcile,
                                "put " + record.DisplayName + " back to its previous placement");
                    }));
                }
            }

            if (carried.Count == 0) return;

            actions.Children.Add(Ui.Note(
                Ui.Count(carried.Count, "model") + " carries a transform: "
                + string.Join(", ", carried.Select(m => m.DisplayName))
                + ". That is legitimate - it is how a survey-grid model is brought onto a project "
                + "grid - and it is worth knowing which ones, because a transform is also the thing "
                + "nobody remembers applying."));
        }

        /// <summary>The rules that ship with the product.</summary>
        private static UIElement BuiltIn()
        {
            var results = Ui.Stack();

            var run = Ui.Runner("Check this model", results, primary: false, work: job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var elements = new List<HealthElement>();
                var scale = session.MetresPerUnit;

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (elements.Count % 2000 == 0 && !job.Step(elements.Count, "elements read")) return;

                    var bounds = item.Bounds;

                    elements.Add(new HealthElement(item.Key.ToString(), item.Model.DisplayName)
                    {
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
                }

                job.Say("Checking...");
                var report = ModelHealth.Check(elements);

                results.Children.Add(Ui.Line(report.ToString(), 1, true));

                if (report.IsClean)
                {
                    results.Children.Add(Ui.Empty("Nothing found.",
                        "That is a real result, not an empty screen — every rule ran and none matched."));
                    return;
                }

                foreach (var finding in report.Findings)
                {
                    results.Children.Add(Ui.Line(finding.Summary, 1, true));
                    results.Children.Add(Ui.Line(finding.Fix, 0.75));
                    results.Children.Add(Ui.Line("e.g. " + string.Join(", ", finding.Examples), 0.55));
                }

                session.Record(ActivityKind.Reconcile, "checked model health: " + report);
            });

            return Ui.Marked(Glyphs.Tick, "Built-in checks",
                "Six checks over geometry, placement and data. Nothing to configure — the rules "
                + "are built in and the thresholds scale to the model rather than assuming metres.",
                run,
                results);
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
            var path = Ui.Text(string.Empty, null, 340);
            var classSource = Ui.Text(string.Empty, null, 200);

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
                    if (projection.Read % 2000 == 0 && !job.Step(projection.Read, "elements read")) return;

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

                foreach (var detail in Details(report)) results.Children.Add(detail);

                session.Record(ActivityKind.Exchange, "checked against " + document.Title + ": " + report, file);
            });

            return Ui.Marked(Glyphs.Report, "Client requirements",
                "Answer an IDS — the openBIM way of writing information requirements down — against "
                + "this federation. Point it at the file and press the button; there is nothing else "
                + "to fill in.",
                Ui.Field("IDS file", path),
                Ui.Field("IFC class from", classSource),
                Ui.Note("Leave the second box empty unless a run tells you to fill it in. IDS is "
                        + "written about IFC classes, and a federation only carries those where a "
                        + "model came from IFC — so CamelWorks looks in the places the readers put "
                        + "them and believes a value only if it reads like a class name. A Revit "
                        + "export saying \"Basic Wall\" is a family, not an IfcWall, and treating it "
                        + "as one would make every row below meaningless."),
                run,
                results);
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

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Profile"), Ui.Sub(Host.NoModel)));

            return Ui.Scroll(Ui.Stack(
                Ui.Line("Kept " + session.Store.Where, 0.75),
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

            return Ui.Card("Responsible parties",
                "Comma separated. Derived from the disciplines in the loaded models until you say otherwise",
                Ui.Field("Parties", parties),
                Ui.Field("Priorities", priorities),
                Ui.Across(save),
                Ui.Note("Leave these alone and the board still works: the assignee is free text and "
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

            return Ui.Card("Code tables",
                "The values a coded property is allowed to hold, and the ones it is not",
                Ui.Field("Name", name),
                Ui.Field("Rows", body),
                Ui.Across(add),
                Ui.Note("One row per line: code, description, parent - commas or tabs between them, "
                        + "and a line starting with # is a comment. Paste the column straight out of "
                        + "the spreadsheet your client sent."),
                list,
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

            return Ui.Card("Model writes",
                "The three operations that touch the model itself",
                count,
                Ui.Note("Ctrl+Z does not undo a property write - Navisworks does not put custom "
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
                if (read % 2000 == 0 && !job.Step(read, "elements read")) { read = -1; return carrying; }

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

            if (session == null)
                return Ui.Scroll(Ui.Stack(Ui.Heading("Setup"), Ui.Sub(Host.NoModel)));

            var profile = session.Profile;
            var status = Ui.Stack();
            var rows = Ui.Stack();

            foreach (var setting in profile.Settings) rows.Children.Add(Row(setting));

            var save = Ui.Button("Save overrides", () =>
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

            return Ui.Scroll(Ui.Stack(
                Ui.Card("Derived values", null, rows),
                Ui.Across(save),
                status));
        }

        private static UIElement Row(ProfileSetting setting)
        {
            var value = Ui.Stack();

            UIElement editor = setting.Choices.Count > 0
                ? (UIElement)Ui.Choice(setting.Choices, setting.Value, pick => setting.Override = pick, 200)
                : Ui.Text(setting.Value, typed => setting.Override = typed, 200);

            var source = Ui.Line(setting.IsOverridden ? "you" : "derived: " + (setting.Derived ?? "nothing"), 0.55);

            value.Children.Add(editor);
            value.Children.Add(source);
            value.Children.Add(Ui.Line(setting.Because, 0.55));

            return Ui.Field(setting.Title, value);
        }

        // -----------------------------------------------------------------------------------
        // Models
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The federation: what is loaded, from where, and whether the source has moved on.
        ///
        /// The last column is the one that matters on a Monday. Navisworks will happily open a
        /// federation whose sources changed a fortnight ago and show it without comment, and the
        /// coordination that follows is against a model nobody is building.
        /// </summary>
        private static UIElement Federation()
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Marked(Glyphs.Clash, "The federation", Host.NoModel);

            var savedAt = FileTime(session.SavedPath);
            var rows = new List<TableRow>();
            var missing = 0;
            var stale = 0;

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
                    missing++;
                }
                else if (savedAt != null && when > savedAt)
                {
                    state = "changed since this file was saved";
                    tone = Tone.Warn;
                    stale++;
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

            var summary = Ui.Across(
                Ui.Pill(Ui.Count(rows.Count, "model")),
                missing > 0 ? Ui.Pill(missing.ToString(CultureInfo.InvariantCulture) + " not found", Tone.Bad) : null,
                stale > 0 ? Ui.Pill(stale.ToString(CultureInfo.InvariantCulture) + " changed since save", Tone.Warn) : null);

            var body = Ui.Stack(summary);

            if (missing > 0)
                body.Children.Add(Ui.Note("A model whose source cannot be found still displays: Navisworks is "
                    + "showing you its own cached copy. Repoint it, or refresh from the folder it moved to, "
                    + "before coordinating against it."));

            if (rows.Count == 0)
                body.Children.Add(Ui.Empty("Nothing is loaded.", "Append or merge a model to start."));
            else
                body.Children.Add(Ui.Table(rows,
                    ("Model", 150), ("Discipline", 100), ("Source", 190), ("Path", 0)));

            body.Children.Add(Repoint(session));

            return Ui.Marked(Glyphs.Clash, "The federation",
                "What is loaded, from where, and whether the file on disk has moved on since this "
                + "federation was saved",
                body);
        }

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

            return Ui.Card("Find moved files",
                "Searches a folder for anything a loaded model can no longer find.",
                Ui.Field("Search folder", folder),
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

            var open = Ui.Button("Open the IFC exporter", () =>
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
