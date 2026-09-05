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
using CamelWorks.Core.Findings;
using CamelWorks.Core.Issues;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
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
        /// The IDS check is a genuinely different act: those are the client's rules, in a file
        /// somebody with a contract wrote, and nothing about it is CamelWorks' opinion about the
        /// data — so its file lives in its own setup group and its answer lands on the same stage.
        /// </summary>
        public static UIElement Health()
        {
            var live = Host.Current;

            if (live == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

            var results = Ui.Stack();
            var idsResults = Ui.Stack();
            var codes = CodeChoice();

            results.Children.Add(Bench.Ready(
                "Sweeps " + Ui.Count(live.Model.Models.Count, "model")
                + " in one pass: placement, geometry, what has arrived, and what it is coded as.",
                "The verdict lands here, worst first, with the fix under the finding that asks for it."));

            // The rail carries the run somebody came here for; which property holds the code is
            // set once on the first Tuesday of a project and read never after, so it sits behind a
            // symbol rather than above the button it has nothing to do with.
            var foot = new WrapPanel();

            foot.Children.Add(Ui.Runner("Check this federation", results,
                                        job => Sweep(results, codes, job)));

            foot.Children.Add(new Border { Width = 14 });

            foot.Children.Add(Ui.Icon(Glyphs.Tune, "Which property carries the code", () =>
                Ui.Configure("How the codes are read", null,
                    new Ui.Page("Which property carries the code", () => codes.Fields ?? Ui.Stack()))));

            return Bench.Surface(
                Ui.Stack(IdsGroup(live, idsResults)),
                foot,
                Ui.Stack(results, idsResults));
        }

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
        private static void Sweep(Panel results, CodeSource codes, Job job)
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

            var kept = Keep(session, review);

            results.Children.Add(Answer(review));
            results.Children.Add(Ui.Small(reading.Explain(session)));

            // The remedy goes under the finding that asks for it. A fix appended after five
            // findings is a fix nobody connects to the one that said "put it back below".
            foreach (var finding in review.Findings)
            {
                results.Children.Add(Found(session, finding,
                                           kept.TryGetValue(finding.Named, out var held)
                                               ? held
                                               : null));

                if (finding.Check == Placed && actions.Children.Count > 0)
                {
                    results.Children.Add(actions);
                    actions = Ui.Stack();
                }

                if (finding.Check == Federated && loaded.AnyMissing)
                {
                    results.Children.Add(Repoint(session));

                    // The search above reports; Broken Links is the tab that can also bring the
                    // found file in. The sweep should not dead-end one press short of the fix.
                    results.Children.Add(Ui.Actions(Ui.Quiet(
                        "Open Broken Links — it can bring the found files in",
                        () => Ui.Told(Shell.AppWindow.Show("audit", "links")))));

                    loaded = (loaded.Table, false);
                }
            }

            Drifted(session, results, job);

            foreach (var skipped in review.Missing) results.Children.Add(Ui.Small("Not checked: " + skipped));

            if (actions.Children.Count > 0) results.Children.Add(actions);

            results.Children.Add(Ui.Eyebrow("What is loaded"));
            results.Children.Add(Ui.Small(Ui.Count(session.Model.Models.Count, "model")
                                          + " in this federation"));
            results.Children.Add(loaded.Table);
            results.Children.Add(intake);
        }

        /// <summary>
        /// What every saved set used to return, and what it returns now.
        ///
        /// Carried on the federation sweep because the sweep answers "is this fit to coordinate
        /// against", and a set that has silently emptied is one of the sharpest ways it can fail to
        /// be: nothing errors, nothing turns red, and every check pointed at that set now runs over
        /// what is left and passes. A green board over a building nobody checked is the outcome this
        /// product exists to prevent.
        ///
        /// Its findings are its own rather than the review's: drift has its own source, identity
        /// and history, and folding it into the review's cards would put the same problem in the
        /// register twice.
        /// </summary>
        private static void Drifted(Session session, Panel results, Job job)
        {
            job.Say("Reading the sets...");

            var now = new List<SetReading>();
            var names = SetNames(session);

            foreach (var name in names)
            {
                var members = new List<ElementKey>();
                var held = 0;
                var whole = true;

                try
                {
                    foreach (var item in session.Model.Traverse(TraversalScope.Set(name)))
                    {
                        held++;

                        if (members.Count < SetDrift.DefaultCap) members.Add(item.Key);
                        else whole = false;
                    }
                }
                catch (Exception)
                {
                    // The host refused to resolve it. Skipped rather than recorded as empty: an
                    // unreadable set and an emptied one look identical from here, and reporting a
                    // read failure as a disarmed check would cry wolf on a set that is fine.
                    continue;
                }

                now.Add(new SetReading(name, members, session.NowTicks, whole, held));
            }

            var baseline = session.Baselines;
            var drifted = SetDrift.Across(baseline.All.ToList(), now);

            // Which sets this document no longer has. Only the ones actually missing, not the ones
            // that merely would not read a moment ago.
            var known = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            var uses = Uses(session);

            var found = SetDriftFindings.For(
                drifted.Where(d => d.Kind != SetDriftKind.Gone || !known.Contains(d.Name)).ToList(),
                name => uses.TryGetValue(name, out var on) ? on : Array.Empty<SetUse>());

            var register = session.Findings.Findings;

            register.Observe(FindingSource.SetDrift, found, DateTime.UtcNow.Ticks,
                             origin: session.Store.Path);

            session.Findings.Save();

            // This run becomes the baseline, so a drift is said once rather than every sweep from
            // now on. The finding keeps the record of it having happened.
            baseline.Keep(now);

            foreach (var gone in drifted.Where(d => d.Kind == SetDriftKind.Gone))
                baseline.Forget(gone.Name);

            if (!baseline.Save() && baseline.LastSaveProblem != null)
                results.Children.Add(Ui.Note("What the sets hold now could not be kept, so the "
                    + "next sweep will compare against the same reading as this one: "
                    + baseline.LastSaveProblem));

            results.Children.Add(Ui.Eyebrow("Sets"));

            if (baseline.Count == 0 && found.Count == 0)
            {
                results.Children.Add(Ui.Small(
                    "No set has been read before, so there is nothing to compare against yet. "
                    + "What they hold now has been kept; the next sweep says what moved."));

                return;
            }

            if (found.Count == 0)
            {
                results.Children.Add(Ui.Small(
                    Ui.Count(now.Count, "set") + " read, and every one still returns about what "
                    + "it did."));

                return;
            }

            foreach (var finding in found)
                results.Children.Add(Ui.Finding(
                    Fault(finding), "Sets", finding.Title, finding.Detail,
                    "Open the set and see what it matches now. A set is a saved query, so what "
                    + "changed is usually a property the model stopped carrying.",
                    finding.Facts.TryGetValue(SetDriftFindings.Feeding, out var leaning)
                        ? leaning
                        : null,
                    Reached(session, finding)));
        }

        /// <summary>
        /// The way to the elements that left a set, and the verbs for deciding about it.
        ///
        /// The elements are the whole diagnosis: a set that lost its ductwork because somebody
        /// deleted the mezzanine is a different morning from one that lost it because a property
        /// was renamed, and only looking at them tells the two apart.
        /// </summary>
        private static UIElement? Reached(Session session, Finding finding)
        {
            var said = Ui.Stack();
            var judgement = Judge(session, finding, said);
            var keys = finding.AllElements();

            if (keys.Count == 0)
            {
                var only = Ui.Stack(judgement, said);
                only.Margin = new Thickness(0, 6, 0, 0);
                return only;
            }

            var show = Ui.Quiet("Show me what left"
                + (finding.Children.Count < Count(finding) ? " (the first " + keys.Count + ")" : string.Empty),
                () =>
                {
                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);
                });

            var row = Ui.Stack(Ui.Across(show), judgement, said);
            row.Margin = new Thickness(0, 6, 0, 0);
            return row;
        }

        /// <summary>How many left the set, as the finding recorded it.</summary>
        private static int Count(Finding finding) =>
            finding.Facts.TryGetValue(SetDriftFindings.Left, out var left)
            && int.TryParse(left, out var many)
                ? many
                : finding.Children.Count;

        /// <summary>Whether a drift is the kind that disarms rather than merely narrows.</summary>
        private static bool Fault(Finding finding) =>
            finding.Facts.TryGetValue(SetDriftFindings.Now, out var now)
            && string.Equals(now, "0", StringComparison.Ordinal);

        /// <summary>
        /// What leans on each set, so a drift finding can say what it disarmed.
        ///
        /// Clash tests and scoped rules, because those are the two the product can read without
        /// asking anybody: a test's sides come back from the host, and a rule set names its scopes.
        /// The rest - a clearance input, an appearance layer - live in one tab's memory, and a
        /// half-answer is better than a wrong one, so they are simply not claimed.
        /// </summary>
        private static Dictionary<string, List<SetUse>> Uses(Session session)
        {
            var uses = new Dictionary<string, List<SetUse>>(StringComparer.OrdinalIgnoreCase);

            void On(string? name, string what, string named)
            {
                if (string.IsNullOrWhiteSpace(name)) return;

                if (!uses.TryGetValue(name!, out var on)) uses[name!] = on = new List<SetUse>();

                on.Add(new SetUse(what, named));
            }

            try
            {
                if (session.Clash.IsAvailable)
                    foreach (var test in session.Clash.Tests())
                    {
                        var sides = session.Clash.Sides(test.Id);

                        if (sides == null) continue;

                        On(sides.SetA, "clash test", test.Name);
                        On(sides.SetB, "clash test", test.Name);
                    }
            }
            catch (Exception)
            {
                // No clash engine, or a host that would not answer. The findings are still worth
                // having without the list of what they cost.
            }

            try
            {
                if (IdsWindow.Draft(session, out _) is IdsDocument rules)
                    foreach (var specification in rules.Specifications)
                        foreach (var scope in IdsScopeSpecification.ScopesOf(specification))
                            if (scope.Kind == IdsScopeKind.Set)
                                On(scope.Name, "rule", specification.Name);
            }
            catch (Exception)
            {
                // No rules of this project's own.
            }

            return uses;
        }

        /// <summary>The verdict, said once and in one line.</summary>
        private static UIElement Answer(FederationReview review)
        {
            return Ui.Stack(
                Bench.Verdict(review.Verdict(),
                    review.Grade == ReviewGrade.Fault ? Tone.Bad
                    : review.Grade == ReviewGrade.Watch ? Tone.Warn
                    : Tone.Good),
                Ui.Across(
                    Ui.Figure(Measure.Count(review.Faults), "to settle",
                              review.Faults > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(Measure.Count(review.Watches), "worth a look",
                              review.Watches > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(Measure.Count(review.Clean), "clean")));
        }

        /// <summary>
        /// Put what the sweep found into the findings register, and hand back what it kept.
        ///
        /// The review previously reported what it found, offered to select the elements, and
        /// discarded everything on leaving the tab: nothing could be marked accepted, assigned, or
        /// raised as an issue. Keeping the findings is what makes those possible.
        ///
        /// Keyed by the rule, so the caller can put each card's verbs on the finding behind it.
        /// </summary>
        private static Dictionary<string, Finding> Keep(Session session, FederationReview review)
        {
            var found = HealthFindings.For(review);
            var register = session.Findings.Findings;

            register.Observe(FindingSource.Health, found, DateTime.UtcNow.Ticks,
                             origin: session.Store.Path);

            session.Findings.Save();

            var kept = new Dictionary<string, Finding>(StringComparer.Ordinal);

            // What the register holds, not the copy this run made: a rule already known keeps the
            // decision somebody took on it, and that is the record the card must act on.
            foreach (var finding in found)
                if (register.Find(finding.Id) is Finding held && finding.Discriminator != null)
                    kept[finding.Discriminator] = held;

            return kept;
        }

        /// <summary>One finding, in the vocabulary's own shape.</summary>
        private static UIElement Found(Session session, ReviewFinding finding, Finding? kept) =>
            Ui.Finding(finding.Grade == ReviewGrade.Fault, finding.Check, finding.Headline,
                       finding.Detail, finding.Fix,
                       finding.Examples.Count == 0 ? null : string.Join(", ", finding.Examples),
                       Reach(session, finding, kept));

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
        private static UIElement? Reach(Session session, ReviewFinding finding, Finding? kept)
        {
            var said = Ui.Stack();

            // A finding about models rather than geometry still gets the judgement verbs: "this
            // model was moved" is exactly the kind of thing somebody accepts once and does not
            // want asked again next Monday. It is only the selection verbs it cannot have.
            var judgement = kept == null ? null : Judge(session, kept, said);

            if (finding.Affected.Count == 0)
            {
                if (judgement == null) return null;

                var verbs = Ui.Stack(judgement, said);
                verbs.Margin = new Thickness(0, 6, 0, 0);
                return verbs;
            }

            var keys = finding.Affected;

            var show = Ui.Quiet("Show me " + (keys.Count == 1 ? "it" : "all " + Ui.Count(keys.Count, "of them")),
                () =>
                {
                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);
                });

            var only = Ui.Quiet("Isolate them", () =>
            {
                session.Model.Select(keys);
                session.View.ZoomTo(keys, 2.0);
                Isolate(session, keys);
            });

            var row = Ui.Across(show, only);
            row.Margin = new Thickness(0, 6, 0, 0);

            if (judgement == null) return row;

            var both = Ui.Stack(row, judgement, said);
            both.Margin = new Thickness(0, 6, 0, 0);
            return both;
        }

        /// <summary>
        /// The verbs that make a review finding actionable rather than only readable: settle it, and
        /// put it on an issue.
        ///
        /// "Settle it" rather than a status picker, because a review finding carries one decision:
        /// it is known and it has been accepted. Five statuses would be four nobody uses. The
        /// caption depends on where the finding already is, so the button is never a no-op.
        /// </summary>
        private static UIElement Judge(Session session, Finding kept, Panel said)
        {
            var settle = Ui.Quiet(
                kept.Status == FindingStatus.Approved ? "Reopen it" : "Settle it",
                () =>
                {
                    kept.Status = kept.Status == FindingStatus.Approved
                        ? FindingStatus.Active
                        : FindingStatus.Approved;

                    said.Children.Clear();

                    if (!session.Findings.Save())
                    {
                        said.Children.Add(Ui.Problem("That could not be kept: "
                            + (session.Findings.LastSaveProblem
                               ?? "there was nowhere to write.")));

                        return;
                    }

                    said.Children.Add(Ui.Done(kept.Status == FindingStatus.Approved
                        ? "Settled. The next sweep will still find it and will still say so, but "
                          + "it will say you have accepted it."
                        : "Open again."));
                });

            var raise = Ui.Quiet("Raise an issue", () =>
            {
                said.Children.Clear();

                var (made, problem) = IssueViews.Raise(session, id =>
                {
                    var issue = new Issue(id, kept.Title)
                    {
                        Type = "Quality",
                        Source = kept.Rule,
                        Description = kept.Detail
                            ?? (kept.Facts.TryGetValue(HealthFindings.Fix, out var fix)
                                ? fix
                                : null),
                    };

                    IssueFindings.Attach(issue, new[] { kept });

                    foreach (var element in kept.AllElements())
                        if (!issue.Elements.Contains(element)) issue.Elements.Add(element);

                    return issue;
                }, 1);

                if (problem != null)
                {
                    said.Children.Add(Ui.Problem(problem));
                    return;
                }

                said.Children.Add(Ui.Info(Ui.Count(made, "issue") + " raised.",
                    "Open the register", () => IssueViews.Open(null)));
            });

            var status = Ui.Small(kept.Status == FindingStatus.Approved
                ? "Settled"
                : kept.IsPresent ? "Open" : "Not found by the latest sweep");

            var row = Ui.Across(settle, raise, status);
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
                    + "own cached copy, dated to the last time this file was opened.",
                    "Repoint it, or refresh from the folder it moved to, before coordinating.",
                    missing, "source-missing");

            if (stale.Count > 0)
                review.Watch(Check, Ui.Count(stale.Count, "model") + " has changed since this file was saved",
                    "The federation is showing what was published when it was saved, not what is on "
                    + "disk now.",
                    "Refresh, then run this again.",
                    stale, "source-stale");

            if (rows.Count == 0)
                return (Ui.Empty("Nothing is loaded.", "Append or merge a model to start."), false);

            var loaded = Ui.Table(rows,
                ("Model", 150), ("Discipline", 100), ("Source", 190), ("Path", 0));

            // The paths column is the reason to copy: "which files is this federation actually
            // pointing at" is a question answered by pasting the list somewhere, not by reading
            // four columns off a screen and retyping them.
            return (Ui.Picking(loaded,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(loaded, picked), say, "model"))),
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
                                             finding.Affected, finding.Rule));
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
                    "Either it was removed deliberately, or an append was lost on save.",
                    null,
                    intake.Missing.Select(m => m.DisplayName).ToList(), "model-gone");

            var body = Ui.Stack(Ui.Line(intake.Explain(), 1, true), Ui.Line(manifest.Explain(), 0.8));

            if (intake.NothingNew && intake.Missing.Count == 0)
                body.Children.Add(Ui.Info("Nothing has been republished since the last sweep. A run "
                    + "against exactly last week's files produces exactly last week's board - worth "
                    + "knowing before the meeting rather than during it."));

            var units = session.Profile.Units;
            var table = manifest.ModelTable(units);

            var models = Ui.Table(table.Rows.Select(r => new TableRow(r,
                r[0] ?? string.Empty, r[2] ?? string.Empty, r[3] ?? string.Empty,
                r[5] ?? string.Empty, r[6] ?? string.Empty)).ToList(),
                ("Model", 200), ("Source last written", 150), ("Size", 80),
                ("Placement", 170), ("Elements", 0));

            body.Children.Add(Ui.Picking(models,
                new Ui.OnPicked("Copy {n}",
                    (picked, say) => Ui.Copy(Ui.Lines(models, picked), say, "model"))));

            if (intake.HasPrevious)
            {
                var register = intake.Table();

                body.Children.Add(Ui.Eyebrow("Since the last sweep"));

                var since = Ui.Table(register.Rows.Select(r => new TableRow(r,
                    r[0] ?? string.Empty, r[1] ?? string.Empty, r[2] ?? string.Empty,
                    r[3] ?? string.Empty)).ToList(),
                    ("Model", 200), ("Discipline", 120), ("State", 200), ("Source last written", 0));

                // "What changed since last week" is the line in the meeting agenda, and it gets
                // written by hand off this table every week.
                body.Children.Add(Ui.Picking(since,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(since, picked), say, "model"))));
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
                    "The federation gives no notification of this, and every clash produced "
                    + "afterwards is in the wrong place.",
                    "Run the clash tests again once it is back.",
                    intake.Moved.Select(m => m.DisplayName).ToList(), "model-moved");

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
                    + "thing most easily overlooked.",
                    null,
                    carried.Select(m => m.DisplayName).ToList(), "model-transformed");
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
                    + "or a bare name to look in any category.", null, "code-absent");

                return;
            }

            if (coverage.Invalid > 0)
                review.Fault(Check, Ui.Count(coverage.Invalid, "element")
                    + " carries a value " + table.Name + " does not list",
                    "Either the table is missing a row, or the model carries a value outside the "
                    + "agreed list. The first is a table edit; the second needs resolving with "
                    + "the author.",
                    null,
                    coverage.InvalidValues.Take(6).ToList(), "code-unlisted");
        }

        /// <summary>Which table and property the code check should use.</summary>
        private sealed class CodeSource
        {
            /// <summary>The chosen table, or null when the project has none.</summary>
            public string? Table { get; set; }

            /// <summary>The property carrying the code, as "Category / Name" or a bare name.</summary>
            public string Property { get; set; } = string.Empty;

            /// <summary>The setup group holding the two, or null when there is nothing to choose.</summary>
            public UIElement? Fields { get; set; }
        }

        /// <summary>
        /// The one thing on this surface anybody can be asked to choose, as a setup group.
        ///
        /// It defaults to the first table and to a property named after it, which is right often
        /// enough that most people never touch it. A project with no code tables gets no group at
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

            // A picker, because the property carrying the code is a name the model already knows
            // and typing it from memory is how the check reports "nothing carries it" all morning.
            var property = Pickers.Property(source.Property, typed => source.Property = typed, 240);

            var picker = Ui.Choice(tables.Names, source.Table, pick =>
            {
                source.Table = pick;
                property.Text = pick;
            });

            source.Fields = Bench.Group("Which property carries the code",
                Ui.Field("Table", picker),
                Ui.Field("Property", property, "\"Category / Name\", or a bare name for any category"));

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
        /// the model. Nothing in this group is CamelWorks' opinion about your data. The file is the
        /// rules, and the only thing to set up is the file, which somebody else already wrote.
        ///
        /// The hard part is that IDS speaks IFC and a federation does not. See
        /// <see cref="IdsProjection"/> for how the two are bridged, and note that every guess it
        /// makes is reported on screen rather than folded silently into the result.
        /// </summary>
        private static UIElement IdsGroup(Session live, Panel results)
        {
            // The file a client sent is the file they will send a revision of, and the property
            // their exporter puts the IFC class in does not change between Tuesdays.
            var kept = ToolMemory.Of(live.Store, "project.ids");

            var path = Ui.Text(kept.Text("file"), text => kept.Set("file", text), 200);

            // A picker over the model's property names: when a run says "put one of these in the
            // box", the box should already be offering them rather than asking for a retype.
            var classSource = Pickers.Property(kept.Text("classFrom"),
                                               text => kept.Set("classFrom", text), 200);

            // What to check, as opposed to what each rule is about. Two different questions: this
            // one narrows the traversal, so a rule set written for the whole job can be run over
            // one level on a Tuesday without editing a rule.
            // Where the rules come from. This project's own are not always a file and sometimes
            // cannot be one: a rule scoped to a set in this model has no place in an IDS, so a
            // check that read files and nothing else could never run the rules the IDS tab exists
            // to write.
            var from = kept.Text("from");

            if (string.IsNullOrWhiteSpace(from)) from = FromFile;

            var source = Ui.Choice(new[] { FromProject, FromFile }, from, pick =>
            {
                from = pick;
                kept.Set("from", pick);
            }, 200);

            var over = kept.Text("over");

            if (string.IsNullOrWhiteSpace(over)) over = Everything;

            var scopePicker = Ui.Choice(RunScopes(live), over, pick =>
            {
                over = pick;
                kept.Set("over", pick);
            }, 200);

            var run = Ui.Runner("Check the rules", results, primary: false, work: job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                IdsDocument document;
                string file;

                if (from == FromProject)
                {
                    var mine = IdsWindow.Draft(session, out var why);

                    if (mine == null)
                    {
                        results.Children.Add(Ui.Problem(why ?? "This project has no rules yet."));
                        return;
                    }

                    document = mine;
                    file = "this project's rules";
                }
                else
                {
                    file = path.Text.Trim();

                    if (file.Length == 0)
                    {
                        results.Children.Add(Ui.Problem(
                            "Enter the path of the supplied rule file (.ids or .bcids), or switch "
                            + "\"Rules from\" to this project's own."));

                        return;
                    }

                    if (!File.Exists(file))
                    {
                        results.Children.Add(Ui.Problem("There is no file at " + file + "."));
                        return;
                    }

                    // Either kind, told apart by what the bytes are rather than by what the file
                    // is called: a rule set somebody sent may be an IDS anyone reads or a
                    // BIMCamel one carrying rules no IDS can hold, and a run should open both.
                    IdsReadResult read;

                    try
                    {
                        read = IdsFile.Read(File.ReadAllBytes(file));
                    }
                    catch (Exception e)
                    {
                        results.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                        return;
                    }

                    foreach (var warning in read.Warnings) results.Children.Add(Ui.Note(warning));

                    if (!read.IsUsable || read.Document == null)
                    {
                        results.Children.Add(Ui.Empty("Nothing to check.",
                            "That file carried no rules CamelWorks could use."));
                        return;
                    }

                    document = read.Document;
                }

                // The model's own groupings each rule names, resolved once. A traversal each, so
                // only the ones actually named are walked.
                var claimed = Claimed(session, document, job, out var missing);

                if (claimed == null) return;   // stopped

                foreach (var absent in missing)
                    results.Children.Add(Ui.Problem(
                        "No set called \"" + absent + "\" is in this document, so every rule about "
                        + "it applied to nothing. Rename the rule's set, or make the set."));

                var projection = IdsProjection.For(document, PropertyAddress.Parse(classSource.Text),
                                                   claimed);

                var elements = new List<IdsElement>();

                foreach (var item in session.Model.Traverse(Over(over)))
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

                // It used to reveal one row's failures on selection change, with a line underneath
                // saying so. Two specifications that fail on the same wall are one conversation
                // with the architect and not two, and the way to have it is to see both sets at
                // once - so the reveal became an action on however many rows are picked.
                var failures = Keep(session, report, System.IO.Path.GetFileName(file));

                results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Select {n} in the model", Reveal),
                    new Ui.OnPicked("Raise {n} as issues",
                        (picked, say) => RaiseIds(session, picked, failures, say)),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "specification"))));

                // The panel answers "are we ready to issue". The workbook answers "what do I fix",
                // and it is asked by somebody who is not sitting in Navisworks.
                results.Children.Add(Ui.Actions(Ui.Primary("Excel...",
                    () => Workbook(results, document, report, session.NowTicks))));

                // Property failures have a pen in this suite — Data Manager's Write tab can put
                // the missing value on the elements. Offered only when a failure actually is a
                // property one: an entity, classification or aggregation failure is the
                // exporter's to fix, and a button pretending otherwise would be a lie.
                if (report.Results.Any(r => r.Violations.Any(v => v.Requirement is IdsPropertyFacet)))
                {
                    results.Children.Add(Ui.Actions(Ui.Quiet(
                        "Fill the failing properties in Data Manager...",
                        () => Ui.Told(Shell.AppWindow.Show("data", "write")))));

                    results.Children.Add(Ui.Small("Write puts the value on the elements in this "
                        + "federation, which is enough to pass this check and issue. The authoring "
                        + "model still needs the same fix, and the workbook is how its author "
                        + "hears about it."));
                }

                foreach (var detail in Details(report)) results.Children.Add(detail);

                // At the press, which is the moment somebody committed to the path and the
                // property name. Next Tuesday's revision opens on both.
                kept.Keep();

                session.Record(ActivityKind.Exchange,
                               "checked against " + document.Title + ": " + report,
                               file + (over == Everything ? string.Empty : " over " + over));
            });

            return Bench.Group("The rules",
                Ui.Field("Rules from", source,
                         "this project's own, or a supplied file"),
                Ui.PathField("Rule file", path, PathKind.Open, IdsFile.Filter),
                Ui.Field("Check over", scopePicker,
                         "the whole federation, or one part of it"),
                Ui.Field("IFC class from", classSource,
                         "leave it empty unless a run tells you to fill it in"),
                run,
                Author(results));
        }

        /// <summary>
        /// Put what the check found into the findings register, and hand back what it kept.
        ///
        /// Without this the tab could select the failures and write a workbook but remember nothing:
        /// run it, agree with the architect that the forty doors without a fire rating are being
        /// scheduled separately, run it again the next day, and it reports forty again with nowhere
        /// that agreement was recorded.
        ///
        /// Keyed by the specification, so a selected row can act on the finding behind it.
        /// </summary>
        private static Dictionary<string, Finding> Keep(Session session, IdsReport report,
                                                        string? document)
        {
            var found = IdsFindings.For(report, document,
                id => ElementKey.TryParse(id, out var key) ? key : default);

            var register = session.Findings.Findings;

            register.Observe(FindingSource.Ids, found, DateTime.UtcNow.Ticks,
                             origin: session.Store.Path);

            session.Findings.Save();

            var kept = new Dictionary<string, Finding>(StringComparer.Ordinal);

            foreach (var finding in found)
                if (finding.Discriminator != null
                    && register.Find(finding.Id) is Finding held)
                    kept[finding.Discriminator] = held;

            return kept;
        }

        /// <summary>
        /// Raise an issue for each picked specification the model failed.
        ///
        /// One issue per specification rather than one over all of them: a specification is what
        /// somebody argues about, and two requirements failing is two conversations even when the
        /// same wall is in both.
        /// </summary>
        private static void RaiseIds(Session session, IReadOnlyList<object> picked,
                                     IReadOnlyDictionary<string, Finding> kept, Panel say)
        {
            say.Children.Clear();

            var findings = picked.OfType<IdsSpecificationResult>()
                .Select(r => kept.TryGetValue(IdsFindings.Named(r.Specification), out var f)
                    ? f
                    : null)
                .Where(f => f != null)
                .Select(f => f!)
                .ToList();

            if (findings.Count == 0)
            {
                say.Children.Add(Ui.Problem("Nothing there to raise: the picked specifications "
                    + "are the ones this model met."));

                return;
            }

            var at = 0;

            var (made, problem) = IssueViews.Raise(session, id =>
            {
                var finding = findings[at++];

                var issue = new Issue(id, finding.Title)
                {
                    Type = "Quality",
                    Source = finding.Facts.TryGetValue(IdsFindings.Document, out var from)
                        ? from
                        : "IDS",
                    Description = finding.Detail
                        ?? (finding.Facts.TryGetValue(IdsFindings.Why, out var why) ? why : null),
                };

                IssueFindings.Attach(issue, new[] { finding });

                foreach (var element in finding.AllElements())
                    if (!issue.Elements.Contains(element)) issue.Elements.Add(element);

                return issue;
            }, findings.Count);

            if (problem != null)
            {
                say.Children.Add(Ui.Problem(problem));
                return;
            }

            say.Children.Add(Ui.Info(Ui.Count(made, "issue") + " raised.",
                "Open the register", () => IssueViews.Open(null)));
        }

        /// <summary>
        /// Select every failing element of the picked specifications, as one selection.
        ///
        /// Deduplicated by element id before anything is resolved: a wall that fails three
        /// specifications is one wall, and selecting it three times is three COM lookups for the
        /// same answer.
        /// </summary>
        /// <param name="picked">The specification results the user picked.</param>
        /// <param name="say">Where to report what happened.</param>
        private static void Reveal(IReadOnlyList<object> picked, Panel say)
        {
            say.Children.Clear();

            var live = Host.Current;

            if (live == null)
            {
                say.Children.Add(Ui.Problem(Host.NoModel));
                return;
            }

            var keys = new List<ElementKey>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in picked)
            {
                if (!(item is IdsSpecificationResult result)) continue;

                foreach (var violation in result.Violations)
                {
                    if (!seen.Add(violation.ElementId)) continue;
                    if (ElementKey.TryParse(violation.ElementId, out var key)) keys.Add(key);
                }
            }

            if (keys.Count == 0)
            {
                say.Children.Add(Ui.Note("Nothing failed in those, or the failures name elements "
                    + "this federation no longer has."));
                return;
            }

            live.Model.Select(keys);

            say.Children.Add(Ui.Done("Selected " + Ui.Count(keys.Count, "element") + " in the model."));
        }

        /// <summary>
        /// The other direction: the requirements this project sets, rather than the ones it is set.
        ///
        /// A client's IDS answers "have we given them what they asked for". It says nothing about
        /// what the job has decided among itself, which is argued about all week and then enforced
        /// by eye. Written as an IDS, the same file checks the model here, travels to the architect
        /// and comes back with the next revision.
        /// </summary>
        private static UIElement Author(Panel results) =>
            Ui.Quiet("Write IDS...", () =>
            {
                var problem = ToolViews.Open("ids");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

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

            if (!Ui.Asked(dialog)) return;

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
            + Ui.Count(projection.Classed, "element") + " carried an IFC class"
            + (projection.Scoped > 0
                ? " · " + Ui.Count(projection.Scoped, "element") + " named by a set"
                : string.Empty), 0.7);

        /// <summary>Rules this project wrote for itself, including the ones no file can hold.</summary>
        private const string FromProject = "This project's rules";

        /// <summary>Rules somebody sent, as an IDS file.</summary>
        private const string FromFile = "An IDS file";

        /// <summary>Checking the whole federation.</summary>
        private const string Everything = "Everything loaded";

        /// <summary>Checking whatever is selected.</summary>
        private const string Picked = "What is selected";

        /// <summary>What the run can be pointed at: the whole job, the selection, or a saved set.</summary>
        private static IReadOnlyList<string> RunScopes(Session session)
        {
            var choices = new List<string> { Everything, Picked };

            choices.AddRange(SetNames(session));

            return choices;
        }

        /// <summary>The saved sets this document holds, by name.</summary>
        private static IReadOnlyList<string> SetNames(Session session)
        {
            try
            {
                return session.Model.Sets()
                              .Select(set => set.DisplayName)
                              .Where(name => !string.IsNullOrWhiteSpace(name))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                              .ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>What a run-level choice means to the traversal.</summary>
        private static TraversalScope Over(string? choice) =>
            choice == Picked ? TraversalScope.CurrentSelection
            : choice == Everything || string.IsNullOrWhiteSpace(choice) ? TraversalScope.WholeDocument
            : TraversalScope.Set(choice!);

        /// <summary>
        /// Which of the model's own groupings claimed each element, for the scopes this rule set
        /// names. Null when the job was stopped.
        ///
        /// One traversal per named scope, and only the named ones: resolving every set a document
        /// holds would cost a walk each for sets no rule mentions. A set the document does not
        /// have is reported rather than silently matching nothing, because a rule about a
        /// misspelled set otherwise passes as "not applicable".
        /// </summary>
        private static Dictionary<string, IReadOnlyList<string>>? Claimed(
            Session session, IdsDocument document, Job job, out IReadOnlyList<string> missing)
        {
            var absent = new List<string>();

            missing = absent;

            var wanted = IdsProjection.ScopesWanted(document);

            if (wanted.Count == 0) return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            var known = new HashSet<string>(SetNames(session), StringComparer.OrdinalIgnoreCase);
            var claimed = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var scope in wanted)
            {
                if (scope.Kind == IdsScopeKind.Set && !known.Contains(scope.Name))
                {
                    absent.Add(scope.Name);
                    continue;
                }

                job.Say("Reading " + scope.Describe() + "...");

                var scopeOf = scope.Kind == IdsScopeKind.Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.Set(scope.Name);

                var read = 0;

                foreach (var item in session.Model.Traverse(scopeOf))
                {
                    if (!job.Step(read++, "elements read")) return null;

                    var id = item.Key.ToString();

                    if (!claimed.TryGetValue(id, out var tokens))
                        claimed[id] = tokens = new List<string>();

                    if (!tokens.Contains(scope.Token, StringComparer.OrdinalIgnoreCase))
                        tokens.Add(scope.Token);
                }
            }

            return claimed.ToDictionary(c => c.Key, c => (IReadOnlyList<string>)c.Value,
                                        StringComparer.Ordinal);
        }

        /// <summary>The things a reader has to know before believing the rows.</summary>
        private static IEnumerable<UIElement> Caveats(IdsDocument document, IdsProjection projection)
        {
            // How many rules actually needed a class. When every rule says who it is about in
            // this model's own terms, no class is wanted and the absence of one is not a fault -
            // saying it is would be crying wolf on the exact federation this was built for.
            var needsClass = document.Specifications
                .Count(spec => spec != null && !IdsScopeSpecification.IsScoped(spec));

            if (projection.Classed == 0 && projection.Read > 0 && needsClass > 0)
            {
                yield return Ui.Problem(
                    "Nothing in this federation carried an IFC class, so "
                    + (needsClass == document.Specifications.Count
                        ? "every specification written about an IFC entity matched nothing"
                        : Ui.Count(needsClass, "specification") + " written about an IFC entity "
                          + "matched nothing")
                    + ". That is a property-naming difference, not an empty model.");

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
        // Project Settings
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Project settings: the values CamelWorks derived, and every override, on one surface.
        ///
        /// This screen is skippable. Every value on it already has an answer, so nothing here has to
        /// be visited before any other feature works, and the project file is created lazily with
        /// nothing blocking on it.
        ///
        /// The register of derived answers is the working material and so is the stage; the inputs
        /// that belong to the project — parties, priorities, code tables, the roster — are the setup
        /// column, with saving the overrides as the single action at its foot. The operations that
        /// write into the model itself stay at the bottom of the stage.
        /// </summary>
        public static UIElement Settings()
        {
            var session = Host.Current;

            if (session == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

            var profile = session.Profile;
            var status = Ui.Stack();
            var rows = Ui.Stack();

            // The register runs to thirty-odd rows and somebody arriving is nearly always after
            // one of them by name. A search at the top of the list, not a scroll.
            var find = Ui.Text(string.Empty, "search the register", null, 220);

            void Fill()
            {
                rows.Children.Clear();

                var wanted = find.Text.Trim();

                foreach (var setting in profile.Settings)
                {
                    if (wanted.Length > 0 && !Matches(setting, wanted)) continue;

                    rows.Children.Add(Row(setting));
                }

                if (rows.Children.Count == 0)
                    rows.Children.Add(Ui.Empty("Nothing matches \"" + wanted + "\".",
                        "The register holds what CamelWorks worked out from the models - units, "
                        + "tolerances, the names it found."));
            }

            find.TextChanged += (s, e) => Fill();

            Fill();

            // The way out of a decision somebody regrets. Every row here is an override of
            // something CamelWorks worked out for itself, and a register that can only be
            // overridden is one people are afraid to touch.
            var reset = Ui.Quiet("Reset all", () =>
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
            // The failures first when there are any: a screen reporting that the project file
            // could not be read, and putting that below thirty rows of settings, is a screen that
            // has filed its own alarm at the bottom of the scroll.
            var broken = Ui.Stack();

            if (session.Store.UsedBackup)
                broken.Children.Add(Ui.Note("The project file could not be read and its backup was used "
                                            + "instead. Nothing is lost that was saved before the last write."));

            if (session.Store.ConcurrencyNote.Length > 0)
                broken.Children.Add(Ui.Note(session.Store.ConcurrencyNote));

            // Read-only is the one store problem that stops every button in the product, so it is
            // said wherever the project file is talked about. It used to be on the front door,
            // which no longer exists.
            if (session.Store.IsReadOnly)
                broken.Children.Add(Ui.Note(session.Store.ReadOnlyReason ?? string.Empty));

            // Parties, code tables and the roster are reference data somebody sets up once and
            // then forgets; laid out beside the register they tripled the height of a screen whose
            // subject is the register. Each is behind its own symbol at the foot, live-applying,
            // where the rest of the product keeps its setup.
            var foot = new WrapPanel();

            foot.Children.Add(save);
            foot.Children.Add(new Border { Width = 14 });

            foot.Children.Add(Ui.Icon(Glyphs.Person, "Who a clash can be given to", () =>
                Ui.Configure("Parties and priorities", null,
                    new Ui.Page("Who a clash can be given to", () => Parties(session)))));

            foot.Children.Add(Ui.Icon(Glyphs.Table, "Code tables", () =>
                Ui.Configure("Code tables", null,
                    new Ui.Page("Code tables", () => Codes(session)))));

            foot.Children.Add(Ui.Icon(Glyphs.Group, "Who is on the job", () =>
                Ui.Configure("The roster", null,
                    new Ui.Page("Who is on the job", () => People(session)))));

            return Bench.Surface(
                null,
                foot,
                Ui.Stack(
                    broken,
                    Cycle(session),
                    Recent(session),
                    Ui.Rule(),
                    Ui.Eyebrow("What CamelWorks worked out"),
                    Ui.Field("Find", find),
                    rows,
                    Ui.Actions(reset),
                    status,
                    ModelWrites(session),
                    Ui.Split("Project file", session.Store.Where)));
        }


        // ===================================================================================
        // The week, and what has happened on this job
        //
        // Both were on a front door of their own, which was a window that opened onto a menu.
        // They belong with the project file: the week is this project's rhythm, and the trail is
        // what has been done to this project. Everything else that screen showed - the list of
        // apps, the count of models - is a row of ribbon buttons and a line on Models.
        // ===================================================================================

        /// <summary>
        /// The three steps of the week, with when each last happened.
        ///
        /// Only the first undone step leads: on a project's first day all three are undone, and
        /// three blue buttons is the same as none.
        /// </summary>
        /// <param name="session">The open model.</param>
        private static UIElement Cycle(Session session)
        {
            var rows = Ui.Stack();

            var label = Ui.Eyebrow("The week");
            label.Margin = new Thickness(0, 0, 0, 4);
            rows.Children.Add(label);

            var step = 0;
            var led = false;

            foreach (var kind in ActivityKind.Cycle)
            {
                step++;

                var here = kind;
                var latest = session.Store.Activity.Latest(here);
                var never = latest == null;

                var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                line.Children.Add(Marker(step, here));

                var left = Ui.Stack(
                    Ui.Line(ActivityKind.Title(here), 1, true),
                    Ui.Line(never ? ActivityKind.Purpose(here) : latest!.Summary, 0.75),
                    Ui.Across(never
                        ? Ui.Pill("Not done yet", Tone.Warn)
                        : Ui.Pill(ActivityLog.Ago(latest!.WhenTicks, session.NowTicks), Tone.Good)));

                left.Margin = new Thickness(11, 0, 10, 0);
                Grid.SetColumn(left, 1);
                line.Children.Add(left);

                var lead = never && !led;
                if (never) led = true;

                var go = GoLabel(here);
                var route = RouteFor(here);

                var button = lead
                    ? Ui.Primary(go, () => Ui.Told(ToolViews.Open(route)))
                    : Ui.Button(go, () => Ui.Told(ToolViews.Open(route)));

                button.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(button, 2);
                line.Children.Add(button);

                rows.Children.Add(line);
                if (step < ActivityKind.Cycle.Count) rows.Children.Add(Ui.Rule());
            }

            return rows;
        }

        /// <summary>The step's number, in the colour of the app it opens.</summary>
        /// <param name="step">Which step it is.</param>
        /// <param name="kind">The activity the step stands for.</param>
        private static UIElement Marker(int step, string kind)
        {
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock
                {
                    Text = step.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            badge.SetResourceReference(Border.BackgroundProperty, "Fam." + FamilyOf(kind));
            return badge;
        }

        /// <summary>Which family's colour the step wears.</summary>
        /// <param name="kind">The activity.</param>
        private static string FamilyOf(string kind) =>
            kind == ActivityKind.Reconcile ? "Project" : "Coordinate";

        /// <summary>Which tool the step opens.</summary>
        /// <param name="kind">The activity.</param>
        private static string RouteFor(string kind)
        {
            if (kind == ActivityKind.Reconcile) return "health";
            if (kind == ActivityKind.Regroup) return "issuelist";

            return "clashreport";
        }

        /// <summary>What the step's button says.</summary>
        /// <param name="kind">The activity.</param>
        private static string GoLabel(string kind)
        {
            if (kind == ActivityKind.Reconcile) return "Check the models";
            if (kind == ActivityKind.Regroup) return "Open the board";

            return "Write the report";
        }

        /// <summary>What has been done to this project lately.</summary>
        /// <param name="session">The open model.</param>
        private static UIElement Recent(Session session)
        {
            var entries = session.Store.Activity.Entries.Take(12).ToList();

            var stack = Ui.Stack();

            var label = Ui.Eyebrow("Recent");
            label.Margin = new Thickness(0, 12, 0, 4);
            stack.Children.Add(label);

            if (entries.Count == 0)
            {
                stack.Children.Add(Ui.Empty("Nothing has happened on this project yet.",
                    "CamelWorks writes a line here every time it changes the model, produces a "
                    + "report or runs the rules - so a project handed over mid-job says what was "
                    + "done to it."));

                return stack;
            }

            var rows = entries.Select(e => new TableRow(e,
                ActivityKind.Title(e.Kind),
                e.Summary,
                ActivityLog.Ago(e.WhenTicks, session.NowTicks))).ToList();

            var recent = Ui.Table(rows, ("What", 110), ("Happened", 0), ("When", 110));

            stack.Children.Add(Ui.Picking(recent,
                new Ui.OnPicked("Copy {n}",
                    (picked, say) => Ui.Copy(Ui.Lines(recent, picked), say, "entry"))));

            return stack;
        }

        /// <summary>
        /// Who a clash can be assigned to, and what the priorities are called.
        ///
        /// Left alone, the board still works: the assignee is free text and the priorities are the
        /// built-in three. Filling them in turns two free-text boxes into pickers, which is worth
        /// about a minute a week.
        /// </summary>
        private static UIElement Parties(Session session)
        {
            var status = Ui.Stack();

            var parties = Ui.Text(string.Join(", ", session.Profile.Parties), null, 200);
            var priorities = Ui.Text(string.Join(", ", session.Profile.Priorities), null, 200);

            var save = Ui.Icon(Glyphs.Save, "Save these", () =>
            {
                status.Children.Clear();

                Override(session, ProfileKeys.Parties, parties.Text);
                Override(session, ProfileKeys.Priorities, priorities.Text);

                status.Children.Add(session.Store.Save()
                    ? Ui.Done("Saved. The board's assign picker and the report's per-party split "
                              + "both read this.")
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
            });

            return Bench.Group("Who a clash can be given to",
                Ui.Field("Parties", parties, "comma separated; derived from the models until set"),
                Ui.Field("Priorities", priorities, "empty keeps the built-in three"),
                Ui.Actions(save),
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
            var body = Ui.Text(string.Empty, null, 200);

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
                        Ui.Icon(Glyphs.MoveIn, "Load this table", () =>
                        {
                            name.Text = mine;
                            body.Text = table.ToText();
                        }),
                        Ui.Icon(Glyphs.Bin, "Remove this table", () =>
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

            return Bench.Group("Code tables",
                list,
                Ui.Field("Name", name),
                Ui.Field("Rows", body, "commas or tabs between them; # starts a comment"),
                add,
                Ui.Small("Paste the column straight out of the spreadsheet your client sent."),
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

            var count = Ui.Runner("Count what CamelWorks has written", status, primary: false, work: job =>
            {
                var carrying = Carrying(session, job, out var read);

                if (read < 0) return;

                status.Children.Add(Ui.Line(Ui.Count(carrying.Count, "element")
                    + " of " + Ui.Count(read, "element") + " carry a CamelWorks property.", 1, true));

                if (carrying.Count == 0)
                {
                    status.Children.Add(Ui.Note("Nothing to remove. CamelWorks keeps its work in the "
                        + "project file beside the model; properties are only written when a tool "
                        + "is asked to write them."));

                    return;
                }

                status.Children.Add(Ui.Button("Remove them from " + Ui.Count(carrying.Count, "element"),
                    () => Strip(session, carrying, status)));
            });

            return Ui.Stack(
                Ui.Eyebrow("Take back what CamelWorks wrote"),
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
                Ui.Refused(status, write);
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
            var list = Ui.Stack();

            void Fill()
            {
                var roster = TeamWindow.Read(session);

                list.Children.Clear();

                foreach (var member in roster.Members
                             .OrderBy(m => m.Name, StringComparer.CurrentCulture))
                {
                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                    row.Children.Add(Ui.Line(member.Name, member.IsActive ? 1 : 0.5, true));

                    var under = member.Under();

                    if (under.Length > 0) row.Children.Add(Ui.Pill(under, Tone.Plain));

                    if (member.Email == null) row.Children.Add(Ui.Pill("no address", Tone.Warn));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 8, 0);

                    list.Children.Add(row);
                }

                if (roster.Members.Count == 0)
                    list.Children.Add(Ui.Small("No members yet. Until the roster is populated, an "
                        + "assignment is whatever name was typed on the board."));
            }

            Fill();

            // One editor, not two.
            //
            // This page had its own name-and-organisation form, and the assign list on the board
            // had no way to reach it at all - so the roster was edited in a settings page nobody
            // opens, by somebody who had already given up and typed a name. It is the same window
            // now, opened from here and from every assign list, writing to the same two places.
            return Bench.Group("Who is on the job",
                list,
                Ui.Actions(Ui.Primary("Open the team",
                                      () => TeamWindow.Show(list, session, Fill))),
                Ui.Small("Kept beside you as well as in this project, so the same people are "
                         + "there the next time you open a model — and whoever opens this job "
                         + "gets the team with it. A clash can be given to one of them, or to a "
                         + "whole role, company or team."));
        }

        /// <summary>Whether a register row answers to what somebody typed in the search box.</summary>
        private static bool Matches(ProfileSetting setting, string wanted)
        {
            bool Has(string? text) =>
                text != null && text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

            return Has(setting.Title) || Has(setting.Value) || Has(setting.Because) || Has(setting.Derived);
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

            status.Children.Add(Bench.Ready(
                "Opens the IFC exporter, which docks as its own panel.",
                "It ships inside CamelWorks and is the same tool as the standalone BIMCamel "
                + "exporter. The mapping, the classification and the writing all live in its panel, "
                + "so the two settings forms can never disagree."));

            var open = Ui.Primary("Open the IFC exporter", () =>
            {
                status.Children.Clear();

                var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(ExporterPaneKey);

                if (!(record is DockPanePluginRecord pane))
                {
                    // Since the exporter is part of this install, "not registered" almost never
                    // means "not installed" - it means the year folder that loaded does not match
                    // this Navisworks. A DLL built for the wrong year loads its ribbon button and
                    // then silently fails to register its dock pane, which is exactly this symptom.
                    status.Children.Add(Ui.Problem(
                        "The IFC exporter panel is not registered with this Navisworks, although it "
                        + "ships with CamelWorks. That almost always means the build that loaded was "
                        + "made for a different release - 2024 is API v21, 2025 v22, 2026 v23 and "
                        + "2027 v24 - or that the exporter's bundle was removed from "
                        + "%APPDATA%\\Autodesk\\ApplicationPlugins. Reinstalling CamelWorks puts it back."));

                    return;
                }

                if (!pane.IsLoaded) pane.LoadPlugin();

                if (pane.LoadedPlugin is DockPanePlugin dock)
                {
                    dock.Visible = true;
                    status.Children.Add(Ui.Line("Opened. It docks as its own panel.", 0.75));
                }
            });

            // Somebody who came here for "get this model out to somebody else" and does not
            // actually need IFC should not have to go back to the tab strip to find out what else
            // this app writes. Each of these is a tab of the same app.
            var elsewhere = Ui.Stack(
                Ui.Rule(),
                Ui.Eyebrow("Or write it as"),
                Ui.Small("IFC is the one that carries properties. These do not."),
                Ui.Actions(
                    Ui.Quiet("GLB, for a browser or a phone...",
                             () => Ui.Told(Shell.AppWindow.Show("exports", "exportglb"))),
                    Ui.Quiet("DXF, for a CAD package...",
                             () => Ui.Told(Shell.AppWindow.Show("exports", "exportdxf"))),
                    Ui.Quiet("A spreadsheet of properties...",
                             () => Ui.Told(Shell.AppWindow.Show("exports", "exportsheet")))));

            return Bench.Surface(null, open, Ui.Stack(status, elsewhere));
        }
    }
}
