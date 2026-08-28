using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Compare two things that are both here now.
    ///
    /// <b>Why this exists beside the baseline comparison.</b> Comparing the model against a
    /// snapshot of itself answers one question — what has happened since Tuesday — and it is not
    /// the only comparison anybody makes. The supplier sends back the model you gave them and you
    /// want to know what they did to it. Two search sets are supposed to hold the same thing and
    /// somebody suspects they do not. A revision arrives as a separate file and is appended beside
    /// the old one rather than replacing it. None of those has a snapshot, none of them is the
    /// model against its own past, and all of them are the same question: <i>what is in one and
    /// not the other, and what is in both but different.</i>
    ///
    /// <b>It reuses the whole comparison engine.</b> Each side becomes a snapshot taken here and
    /// now, and the ordinary compare runs between them — so the matching rungs, the tolerance, the
    /// moved-not-replaced pairing and the property diff all behave exactly as they do against a
    /// baseline. Two models pair as the only candidate on each side even when their names differ,
    /// which is precisely the supplier case.
    /// </summary>
    public static class SideBySideViews
    {
        private const string TwoModels = "Two models";
        private const string TwoSets = "Two sets";

        private static string _mode = TwoModels;
        private static string _leftModel = string.Empty;
        private static string _rightModel = string.Empty;
        private static string _leftSet = string.Empty;
        private static string _rightSet = string.Empty;

        private static readonly CompareSettings Settings = new CompareSettings();

        /// <summary>The surface.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Surface(Session session) => new Screen(session).Build();

        private sealed class Screen
        {
            private readonly Session _session;
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();

            private CompareResult? _last;
            private string _leftName = string.Empty;
            private string _rightName = string.Empty;

            private List<ElementChange> _shown = new List<ElementChange>();

            internal Screen(Session session) => _session = session;

            internal UIElement Build()
            {
                var stage = Ui.Stack(
                    Setup(),
                    Bench.Ready("Compares two things that are both loaded now — two models, or two "
                                + "sets — and says what is in one and not the other.",
                                "The supplier's model against yours, this week's revision against "
                                + "last week's appended beside it, or two sets that are supposed "
                                + "to hold the same thing."),
                    _log,
                    _results);

                return Bench.Surface(null, Foot(Ui.Runner("Compare", _log, Run)), stage);
            }

            private UIElement Setup()
            {
                var panel = Ui.Stack();

                panel.Children.Add(Bench.Group(string.Empty,
                    Ui.Segmented(new[] { TwoModels, TwoSets }, _mode, pick =>
                    {
                        _mode = pick;
                        Ui.Told(Shell.AppWindow.Show("track", "sidebyside"));
                    })));

                if (_mode == TwoModels)
                {
                    var names = _session.Model.Models.Select(m => m.DisplayName).ToList();

                    if (names.Count < 2)
                    {
                        panel.Children.Add(Bench.Group("The two models",
                            Ui.Note("This document has "
                                + (names.Count == 0 ? "no models loaded" : "one model loaded")
                                + ". Append the other one — the supplier's file, or last week's "
                                + "revision — and both sides can be picked here.")));

                        return panel;
                    }

                    if (!names.Contains(_leftModel)) _leftModel = names[0];
                    if (!names.Contains(_rightModel) || _rightModel == _leftModel)
                        _rightModel = names.FirstOrDefault(n => n != _leftModel) ?? names[0];

                    panel.Children.Add(Bench.Group("The two models",
                        Ui.Field("Before", Ui.Choice(names, _leftModel, pick => _leftModel = pick, 240),
                                 "what you sent, or last week's revision"),
                        Ui.Field("After", Ui.Choice(names, _rightModel, pick => _rightModel = pick, 240),
                                 "what came back, or this week's"),
                        Ui.Small("The two are paired as counterparts even when their file names do "
                                 + "not match, because you named them. Elements match on GUID, "
                                 + "tree path, then shape and position.")));
                }
                else
                {
                    panel.Children.Add(Bench.Group("The two sets",
                        Ui.Field("Before", Pickers.Set(_leftSet, pick => _leftSet = pick, 240),
                                 "a search set or a selection set"),
                        Ui.Field("After", Pickers.Set(_rightSet, pick => _rightSet = pick, 240)),
                        Ui.Small("Both sides are read out of this document, so the answer is what "
                                 + "is in one set and not the other - and, for what is in both, "
                                 + "whether it has moved or changed.")));
                }

                return panel;
            }

            /// <summary>The action row: compare, then the window the terms live in.</summary>
            private UIElement Foot(UIElement run)
            {
                var row = new WrapPanel();

                row.Children.Add(run);
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Icon(Glyphs.Tune, "What counts as a change", Configure));
                row.Children.Add(Ui.Icon(Glyphs.Report, "Write the differences to a spreadsheet", Sheet));

                return row;
            }

            /// <summary>What counts as a change, set once for the job.</summary>
            private void Configure() =>
                Ui.Configure("Side by side", null, new Ui.Page("What counts as a change", TermsPage));

            private UIElement TermsPage() => Bench.Group("What counts as a change",
                Ui.Field("Move over", Ui.Text(
                    Settings.Tolerance.ToString("0.####", CultureInfo.InvariantCulture),
                    text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                            out var value) && value >= 0)
                            Settings.Tolerance = value;
                    }, 90),
                    "model units - below this, nothing has moved"),
                Ui.Field("Properties", Ui.Choice(
                    new[] { "Standard list", "Every property", "No properties" },
                    Settings.Watch == PropertyWatch.All ? "Every property"
                    : Settings.Watch == PropertyWatch.None ? "No properties" : "Standard list",
                    pick => Settings.Watch = pick == "Every property" ? PropertyWatch.All
                                          : pick == "No properties" ? PropertyWatch.None
                                          : PropertyWatch.Standard, 150)),
                Ui.Field("Match on", Ui.Choice(
                    new[] { "GUID, tree path or shape", "GUID or tree path", "GUID only" },
                    Settings.WeakestRung == KeyRung.InstanceGuid ? "GUID only"
                    : Settings.WeakestRung == KeyRung.TreePath ? "GUID or tree path"
                    : "GUID, tree path or shape",
                    pick => Settings.WeakestRung = pick == "GUID only" ? KeyRung.InstanceGuid
                                                 : pick == "GUID or tree path" ? KeyRung.TreePath
                                                 : KeyRung.Geometry, 200)),
                Ui.Small("Bounding boxes and properties, not triangles: an element remodelled "
                         + "inside its own box reads as unchanged here. The baseline comparison "
                         + "on the Compare tab is the one that reads triangles."));

            // ---------------------------------------------------------------------------------
            // Running
            // ---------------------------------------------------------------------------------

            private void Run(Job job)
            {
                _results.Children.Clear();

                ModelSnapshot? left;
                ModelSnapshot? right;

                if (_mode == TwoModels)
                {
                    if (!Models(job, out left, out right)) return;
                }
                else
                {
                    if (!Sets(job, out left, out right)) return;
                }

                job.Say("Comparing...");

                var result = ModelCompare.Of(left, right, Settings);

                _last = result;

                _session.Record(ActivityKind.Reconcile,
                                "compared " + _leftName + " against " + _rightName,
                                result.Explain());

                Show(result);
            }

            /// <summary>
            /// Both models, out of one walk.
            ///
            /// One traversal split by model scope rather than two: the host walks a federation
            /// once and the second read would cost the same as the first for an answer already in
            /// memory.
            /// </summary>
            private bool Models(Job job, out ModelSnapshot? left, out ModelSnapshot? right)
            {
                left = right = null;

                var models = _session.Model.Models;

                var a = models.FirstOrDefault(m => string.Equals(m.DisplayName, _leftModel,
                                                                 StringComparison.Ordinal));
                var b = models.FirstOrDefault(m => string.Equals(m.DisplayName, _rightModel,
                                                                 StringComparison.Ordinal));

                if (a == null || b == null)
                {
                    _log.Children.Add(Ui.Problem("Pick a model on each side."));
                    return false;
                }

                if (string.Equals(a.Scope, b.Scope, StringComparison.Ordinal))
                {
                    _log.Children.Add(Ui.Problem("Those are the same model. Comparing it against "
                        + "itself answers nothing — pick the other file on one side."));
                    return false;
                }

                var taken = DateTimeOffset.UtcNow;

                var whole = SnapshotBuilder.Take(_session.Model, taken, "both", Settings,
                                                 TraversalScope.WholeDocument,
                                                 _session.Model.Document?.FileName,
                                                 _session.Profile.Units.LengthUnit,
                                                 keepGoing: () => !job.IsCancelled,
                                                 onProgress: p => job.Step(p.Kept, "elements recorded"));

                _leftName = a.DisplayName;
                _rightName = b.DisplayName;

                left = Side(whole, a, taken);
                right = Side(whole, b, taken);

                if (left.IsEmpty && right.IsEmpty)
                {
                    _log.Children.Add(Ui.Problem("Neither model has anything this comparison can "
                        + "read."));
                    return false;
                }

                return true;
            }

            /// <summary>One model's half of a whole-document snapshot, as a snapshot of its own.</summary>
            private static ModelSnapshot Side(ModelSnapshot whole, IModelSource model,
                                              DateTimeOffset taken)
            {
                var record = whole.Models.FirstOrDefault(
                                 m => string.Equals(m.Scope, model.Scope, StringComparison.Ordinal))
                             ?? new ModelRecord(model.Scope, model.DisplayName,
                                                model.SourcePath ?? string.Empty);

                var side = new ModelSnapshot
                {
                    Label = model.DisplayName,
                    TakenUtc = taken,
                    DocumentPath = whole.DocumentPath,
                    Units = whole.Units,

                    // The terms travel with the side, because the comparison reads them off the
                    // baseline: a side that forgot which properties were watched would report
                    // every watched value as newly appearing.
                    Watch = whole.Watch,
                    Watched = whole.Watched,
                    Ignored = whole.Ignored,
                    Models = new[] { record },
                };

                side.AddRange(whole.InScope(model.Scope));

                return side;
            }

            private bool Sets(Job job, out ModelSnapshot? left, out ModelSnapshot? right)
            {
                left = right = null;

                var a = _leftSet.Trim();
                var b = _rightSet.Trim();

                if (a.Length == 0 || b.Length == 0)
                {
                    _log.Children.Add(Ui.Problem("Pick a set on each side."));
                    return false;
                }

                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Children.Add(Ui.Problem("Those are the same set."));
                    return false;
                }

                var taken = DateTimeOffset.UtcNow;

                _leftName = a;
                _rightName = b;

                left = Read(a, taken, job);
                right = Read(b, taken, job);

                if (left.IsEmpty && right.IsEmpty)
                {
                    _log.Children.Add(Ui.Problem("Neither set resolves to anything in this "
                        + "document."));
                    return false;
                }

                return true;
            }

            private ModelSnapshot Read(string set, DateTimeOffset taken, Job job)
            {
                job.Say("Reading \"" + set + "\"...");

                return SnapshotBuilder.Take(_session.Model, taken, set, Settings,
                                            TraversalScope.Set(set),
                                            _session.Model.Document?.FileName,
                                            _session.Profile.Units.LengthUnit,
                                            keepGoing: () => !job.IsCancelled,
                                            onProgress: p => job.Step(p.Kept, "elements recorded"));
            }

            // ---------------------------------------------------------------------------------
            // Showing
            // ---------------------------------------------------------------------------------

            /// <summary>
            /// The verdict, in the words of what was actually compared.
            ///
            /// "Added" and "removed" are the baseline comparison's words and they are wrong here:
            /// nobody added anything to a supplier's model, and a set does not remove an element.
            /// It is in one side and not the other, and that is what the columns say.
            /// </summary>
            private void Show(CompareResult result)
            {
                _results.Children.Clear();

                _results.Children.Add(Ui.Across(
                    Ui.Figure(Skin.Num(result.Removed), "only in " + _leftName,
                              result.Removed > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(Skin.Num(result.Added), "only in " + _rightName,
                              result.Added > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(Skin.Num(result.Moved), "moved", result.Moved > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(Skin.Num(result.Changed), "changed",
                              result.Changed > 0 ? Tone.Bad : Tone.Plain),
                    Ui.Figure(Skin.Num(result.Unchanged), "the same", Tone.Good)));

                foreach (var note in result.Notes) _results.Children.Add(Ui.Note(note));

                if (result.Pairing.Ambiguities.Count > 0)
                    _results.Children.Add(Ui.Problem("Two models fitted equally well, so nothing "
                        + "was paired: " + string.Join("; ", result.Pairing.Ambiguities)));

                var changes = result.Changes.Where(c => !c.IsUnchanged).ToList();

                if (changes.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("They match.",
                        "Everything in " + _leftName + " is in " + _rightName + ", in the same "
                        + "place, with the same watched properties."));
                    return;
                }

                const int most = 2000;
                _shown = changes.Count > most ? changes.Take(most).ToList() : changes;

                var table = Ui.Table(_shown.Select(Line).ToList(),
                    ("What", 130), ("Element", 220), ("Category", 130), ("Type", 130),
                    ("Detail", 0))
                    .Menu(new Ui.OnMenu("Create an issue from {n}", picked => Raise(picked, false)),
                          new Ui.OnMenu("An issue for each of {n}", picked => Raise(picked, true)));

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Focus {n}", Focus, primary: true),
                    new Ui.OnPicked("Isolate {n}", Isolate),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "row"))));

                _results.Children.Add(Ui.Actions(Ui.Quiet("Show all", () =>
                {
                    _session.View.ShowAll();
                    _log.Children.Clear();
                    _log.Children.Add(Ui.Done("Everything is visible again."));
                })));

                if (changes.Count > most)
                    _results.Children.Add(Ui.Note(Ui.Count(changes.Count, "difference")
                        + " found, and the first " + most.ToString("N0", CultureInfo.InvariantCulture)
                        + " are on screen."));
            }

            private TableRow Line(ElementChange change) =>
                new TableRow(change,
                    Where(change),
                    change.Print.Name,
                    change.Print.Category,
                    change.Print.Type,
                    change.Summary())
                {
                    Tone = change.Bucket == ChangeBucket.Removed || change.Bucket == ChangeBucket.Added
                        ? Tone.Warn
                        : Tone.Bad,
                };

            private string Where(ElementChange change) =>
                change.Bucket switch
                {
                    ChangeBucket.Removed => "only in " + _leftName,
                    ChangeBucket.Added => "only in " + _rightName,
                    ChangeBucket.Moved => "moved",
                    _ => "changed",
                };

            private List<ElementKey> Keys(IReadOnlyList<object> picked, out int lost)
            {
                var keys = new List<ElementKey>();
                lost = 0;

                foreach (var change in picked.OfType<ElementChange>())
                {
                    if (change.Print.TryKey(out var key) && _session.Model.Resolve(key) != null)
                        keys.Add(key);
                    else lost++;
                }

                return keys;
            }

            private void Focus(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var keys = Keys(picked, out var lost);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Note("Nothing there is in this document to select."));
                    return;
                }

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.5);

                say.Children.Add(Ui.Done("Selected " + Ui.Count(keys.Count, "element") + "."
                    + (lost > 0 ? " " + Ui.Count(lost, "row") + " named nothing this document has."
                                : string.Empty)));
            }

            private void Isolate(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var keys = Keys(picked, out _);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Note("Nothing there is in this document to isolate."));
                    return;
                }

                _session.View.Isolate(keys);
                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.5);

                say.Children.Add(Ui.Done(Ui.Count(keys.Count, "element") + " isolated."));
            }

            /// <summary>Differences become issues, the way clash results and clearance do.</summary>
            private void Raise(IReadOnlyList<object> picked, bool each)
            {
                var rows = picked.OfType<ElementChange>().ToList();
                if (rows.Count == 0) return;

                var scale = _session.MetresPerUnit;

                Issue? Make(string id, IReadOnlyList<ElementChange> from)
                {
                    if (from.Count == 0) return null;

                    var first = from[0];

                    var title = from.Count == 1
                        ? first.Print.Name + " — " + Where(first)
                        : Ui.Count(from.Count, "difference") + " between " + _leftName + " and "
                          + _rightName;

                    var issue = new Issue(id, title)
                    {
                        Type = "Coordination",
                        Source = _leftName + " vs " + _rightName,
                        Description = string.Join(Environment.NewLine, from.Take(20).Select(c =>
                            c.Print.Name + ": " + Where(c) + " — " + c.Summary())),
                        HasPoint = true,
                        X = from.Average(c => c.Print.CentreX) * scale,
                        Y = from.Average(c => c.Print.CentreY) * scale,
                        Z = from.Average(c => c.Print.CentreZ) * scale,
                    };

                    foreach (var change in from)
                        if (change.Print.TryKey(out var key) && !issue.Elements.Contains(key))
                            issue.Elements.Add(key);

                    return issue;
                }

                var at = 0;

                var (made, problem) = IssueViews.Raise(_session,
                    id => each ? Make(id, new[] { rows[at++] }) : Make(id, rows),
                    each ? rows.Count : 1);

                _log.Children.Clear();

                _log.Children.Add(problem != null
                    ? Ui.Problem(problem)
                    : Ui.Info(Ui.Count(made, "issue") + " raised from "
                              + Ui.Count(rows.Count, "difference") + ".",
                              "Open the register", () => IssueViews.Open(null)));
            }

            /// <summary>The differences as a spreadsheet.</summary>
            private void Sheet()
            {
                _log.Children.Clear();

                if (_last == null || _shown.Count == 0)
                {
                    _log.Children.Add(Ui.Problem("Compare something first."));
                    return;
                }

                var block = new TableBlock("What", "Element", "Category", "Type", "Detail",
                                           "Before", "After");

                foreach (var change in _shown)
                    block.Row(Where(change), change.Print.Name, change.Print.Category,
                              change.Print.Type, change.Summary(),
                              change.Before?.Name ?? string.Empty,
                              change.After?.Name ?? string.Empty);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the differences to a spreadsheet",
                    Filter = "Excel|*.xlsx",
                    FileName = "side-by-side.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName,
                                                 XlsxWriter.Write("Differences", block));
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote a side-by-side comparison",
                                Ui.Count(_shown.Count, "row") + " to " + dialog.FileName);

                _log.Children.Add(Ui.Done("Wrote " + Ui.Count(_shown.Count, "row") + " to "
                                          + dialog.FileName + "."));
            }
        }
    }
}
