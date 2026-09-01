using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Forty sets from the spreadsheet a project already keeps them in.
    ///
    /// The Sets tab could make one set, or a family splitting one property into its values. What a
    /// job actually starts with is a schedule - six disciplines, forty sets, two or three conditions
    /// each, filed into folders - and it exists as a sheet somebody was sent. Turning it into search
    /// sets by hand takes an afternoon, gets done once, and gets done again from nothing when the
    /// next revision lands.
    ///
    /// A window rather than a pane for the same reason the matrix is: mapping a spreadsheet means
    /// looking at the spreadsheet, and there is no version of that which fits in a column four
    /// hundred and seventy pixels wide.
    /// </summary>
    public static class ScheduleWindow
    {

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Editor(session).View;

        private sealed class Editor
        {
            private const string Conditions = "one condition per row";
            private const string Columns = "one set per row, a column per property";

            private readonly Session _session;
            private readonly SheetSource _sheet = new SheetSource();
            private readonly SetSheetMapping _mapping = new SetSheetMapping();

            private readonly StackPanel _plan = Ui.Stack();
            private readonly StackPanel _warnings = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _shape = Ui.Stack();

            private SetPlan? _planned;

            internal Editor(Session session)
            {
                _session = session;
                View = Build();
            }

            internal UIElement View { get; }

            /// <summary>
            /// Three decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns, so "Publish" was on screen beside a workbook path
            /// nobody had typed yet. The dependency is real: nothing to plan until the schedule
            /// has been read, nothing to publish until the plan says what it would make.
            ///
            /// The sheet preview stays on the first step with the row and column numbers, because
            /// those numbers are read straight off it.
            /// </summary>
            private UIElement Build()
            {
                var sheet = Ui.Stack(
                    Bench.Group(string.Empty, _sheet.Picker()),
                    Bench.Group("Where things are", Layout(), _shape),
                    Bench.Group("The sheet as it stands", _sheet.Preview()));

                var plan = Ui.Stack(_plan);
                var make = Make();

                ShowShape();

                var steps = new[]
                {
                    new Ui.Step("The sheet", () => sheet,
                                () => _planned == null
                                      ? "Read the schedule out of the sheet first." : null),
                    new Ui.Step("What it would make", () => plan,
                                () => _planned == null || _planned.IsEmpty
                                      ? "Nothing in the schedule to make sets from." : null),
                    new Ui.Step("Make them", () => make),
                };

                // No finish button of its own: the last step carries Ui.Commit, which is the
                // two-stage "show me, then do it" a write into the set library has always used.
                return Ui.Steps(steps, null, null);
            }

            private UIElement Layout()
            {
                var pick = Ui.Choice(new[] { Conditions, Columns }, Conditions, chosen =>
                {
                    _mapping.Layout = chosen == Columns ? SetSheetLayout.Columns : SetSheetLayout.Conditions;
                    ShowShape();
                }, 250);

                return Ui.Field("The sheet is", pick);
            }

            /// <summary>
            /// The mapping fields the chosen layout actually needs.
            ///
            /// Rather than showing all eleven and greying five of them out: a field that does not
            /// apply is not a disabled field, it is a field that is not part of this question.
            /// </summary>
            private void ShowShape()
            {
                _shape.Children.Clear();

                var common = Ui.Stack(
                    Ui.Pair(Number("Heading row", _mapping.HeaderRow, v => _mapping.HeaderRow = v),
                            Number("Set name column", _mapping.NameColumn, v => _mapping.NameColumn = v)),
                    Number("Folder column", _mapping.FolderColumn, v => _mapping.FolderColumn = v));

                _shape.Children.Add(common);

                if (_mapping.Layout == SetSheetLayout.Conditions)
                {
                    _shape.Children.Add(Ui.Stack(
                        Ui.Pair(Number("Category column", _mapping.CategoryColumn, v => _mapping.CategoryColumn = v),
                                Number("Property column", _mapping.PropertyColumn, v => _mapping.PropertyColumn = v)),
                        Ui.Pair(Number("Operator column", _mapping.OperatorColumn, v => _mapping.OperatorColumn = v,
                                       "Zero reads every row as Equals."),
                                Number("Value column", _mapping.ValueColumn, v => _mapping.ValueColumn = v))));
                }
                else
                {
                    _shape.Children.Add(Ui.Stack(
                        Number("First value column", _mapping.FirstValueColumn, v => _mapping.FirstValueColumn = v,
                               "Headings are properties; filled cells become conditions."),
                        // Fed from the model, because this is the field that decides whether all
                        // forty sets match anything: the sheet knows its property names, but the
                        // category is spelled the host's way, not the schedule's.
                        Ui.Field("Property category",
                                 Pickers.Tab(_mapping.Category, text => _mapping.Category = text, 180),
                                 "One category for the whole sheet.")));
                }

                _shape.Children.Add(Ui.Actions(Ui.Runner("Read the schedule", _warnings, Read)));

                // The Runner's output panel, on the Runner's own step. It was on the next one,
                // behind a gate that does not open until the read has worked.
                _shape.Children.Add(Ui.Loose(_warnings));
            }

            private Panel Number(string label, int value, Action<int> set, string? hint = null) =>
                Ui.Field(label, Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        && parsed >= 0) set(parsed);
                }, 70), hint);

            private void Read(Job job)
            {
                job.Say("Reading the sheet...");

                _plan.Children.Clear();
                _output.Children.Clear();
                _planned = null;

                var read = SheetSets.Read(_sheet.Chosen, _mapping);

                foreach (var warning in read.Warnings) _warnings.Children.Add(Ui.Note(warning));

                if (!read.IsUsable)
                {
                    _plan.Children.Add(Ui.Empty("Nothing read yet.",
                        "Open a workbook, say where the schedule sits in it, and read it."));
                    return;
                }

                var planned = SetSchedule.Plan(read.Rows);
                _planned = planned;

                _plan.Children.Add(Ui.Line(planned.Explain(), 1, true));

                foreach (var problem in planned.Problems) _warnings.Children.Add(Ui.Note(problem));

                var rows = planned.Sets.Take(300)
                    .Select(s => new TableRow(s, s.Name, s.Folder ?? "(top)",
                                              Ui.Count(s.Conditions, "condition")))
                    .ToList();

                var listed = Ui.Table(rows, ("Set", 260), ("Folder", 160), ("From", 120));

                _plan.Children.Add(Ui.Picking(listed,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(listed, picked), say, "set"))));
            }

            private UIElement Make()
            {
                var commit = Ui.Commit("Try them without saving anything", "Publish", "set",
                    plan: (job, into) => Rehearse(job, into),
                    apply: job => Publish(job));

                return Ui.Stack(commit, _output);
            }

            /// <summary>
            /// Run every set's rule and report what it would hold, writing nothing.
            ///
            /// The dry run is the whole difference between a schedule somebody trusts and one they
            /// delete. Forty sets published blind means forty sets nobody has checked, and the two
            /// that matched nothing are indistinguishable from the thirty-eight that worked until
            /// somebody opens each one.
            /// </summary>
            private int Rehearse(Job job, Panel into)
            {
                var planned = _planned;

                if (planned == null || planned.IsEmpty)
                {
                    into.Children.Add(Ui.Note("Read a schedule first."));
                    return 0;
                }

                var rows = new List<TableRow>();
                var empty = 0;
                var done = 0;

                foreach (var set in planned.Sets)
                {
                    if (!job.Step(done++, planned.Sets.Count, "rules tried")) return 0;

                    var outcome = _session.Search.Run(DnfCompiler.Compile(set.Expression));

                    if (outcome.Count == 0) empty++;

                    rows.Add(new TableRow(set, set.Name,
                                          outcome.Count.ToString("N0", CultureInfo.InvariantCulture),
                                          outcome.IsLive ? "live search" : "fixed list"));
                }

                var costed = Ui.Table(rows, ("Set", 260), ("Would hold", 110), ("As", 120));

                into.Children.Add(Ui.Picking(costed,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(costed, picked), say, "set"))));

                if (empty > 0)
                    into.Children.Add(Ui.Note(Ui.Count(empty, "set")
                        + " would match nothing at all. Usually the property name or its category is "
                        + "spelled differently in this federation than in the schedule."));

                return planned.Sets.Count;
            }

            private void Publish(Job job)
            {
                var planned = _planned;
                if (planned == null) return;

                var made = 0;
                var live = 0;

                foreach (var set in planned.Sets)
                {
                    if (!job.Step(made, planned.Sets.Count, "sets published")) break;

                    var folders = string.IsNullOrWhiteSpace(set.Folder)
                        ? null
                        : new[] { set.Folder! };

                    // The comment is what makes a generated set survive. Somebody opening this model
                    // without CamelWorks sees a search set with no explanation; with this they see
                    // what it was built from and can rebuild it.
                    var outcome = _session.Search.PublishInFolder(
                        folders, set.Name, DnfCompiler.Compile(set.Expression),
                        "Built by CamelWorks from a set schedule: "
                        + Ui.Count(set.Conditions, "condition") + ", all of which must match.");

                    if (outcome.IsLive) live++;
                    made++;
                }

                _output.Children.Add(Ui.Line("Published " + Ui.Count(made, "set") + ".", 1, true));

                if (live < made)
                    _output.Children.Add(Ui.Note(Ui.Count(made - live, "set")
                        + " was saved as a fixed list rather than a live search, because its rule "
                        + "needs more than one search and a Navisworks search set holds one. Those "
                        + "will not pick up elements added later."));

                _session.Record(ActivityKind.Regroup,
                    "published " + Ui.Count(made, "set") + " from a schedule");

                // The schedule's sets are the input everything downstream asks for by name, and
                // the wizard used to end on a count. The two tabs whose first field is "which
                // set" are one press away instead of a hunt through the ribbon.
                if (made > 0)
                    _output.Children.Add(Ui.Across(
                        Ui.Button("Colour them...",
                                  () => Ui.Told(AppWindow.Show("scenes", "appearance"))),
                        Ui.Button("Take them off",
                                  () => Ui.Told(AppWindow.Show("quantify", "takeoff")))));
            }
        }
    }
}
