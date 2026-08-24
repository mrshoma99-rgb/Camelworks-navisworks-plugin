using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Going through the board one group at a time, with both hands on the keyboard.
    ///
    /// The board is for deciding what to look at. Review is for looking: it frames one group, waits,
    /// and takes a key. A coordinator clearing sixty groups on a Tuesday afternoon does the same six
    /// things sixty times, and doing them by finding a row, clicking it, clicking a dropdown and
    /// clicking Apply is four hundred clicks that a keystroke each would have been.
    ///
    /// <b>Modeless and small.</b> It sits over the model rather than in the panel, because the whole
    /// point is to be looking at the geometry — a review screen that covered the thing being
    /// reviewed would be a worse version of the board.
    ///
    /// <b>Nothing is written until it is closed.</b> Every key press moves the board in memory; the
    /// window writes once, on the way out, and says how many it moved. Sixty saves of a project file
    /// two people share is sixty chances to collide.
    /// </summary>
    public static class ReviewWindow
    {
        private static Window? _open;

        /// <summary>
        /// Open review on the board as it stands.
        /// </summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            var board = CoordinateViews.LastBoard;

            if (board == null)
                return "Load the board in Triage first. Review walks what is on it, and there is "
                     + "nothing on it yet.";

            var groups = CoordinateViews.Reviewable(board);

            if (groups.Count == 0)
                return "Everything on the board is approved or resolved. There is nothing left to "
                     + "review, which is the point of the exercise.";

            if (_open != null)
            {
                _open.Activate();
                return null;
            }

            var state = new Walk(session, board, groups);

            var window = new Window
            {
                Title = "CamelWorks review",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Topmost = true,
                Content = state.Build(),
            };

            window.KeyDown += (_, e) => state.Key(e);

            window.Closed += (_, __) =>
            {
                _open = null;
                state.Finish();
            };

            _open = window;

            // No owner: the host's main window is not a WPF window, and asking WPF to own a Win32
            // handle it does not know about is how a review window ends up behind Navisworks with
            // no way to get it back.
            window.Show();

            state.Go(0);

            return null;
        }

        /// <summary>One pass through the board, holding where it has got to.</summary>
        private sealed class Walk
        {
            private readonly Session _session;
            private readonly ClashPipelineResult _board;
            private readonly IReadOnlyList<ClashGroup> _groups;

            private readonly TextBlock _where = Ui.Line(string.Empty, 1, true);
            private readonly TextBlock _what = Ui.Line(string.Empty, 0.85);
            private readonly TextBlock _instruction = Ui.Line(string.Empty, 0.85);
            private readonly TextBlock _said = Ui.Line(string.Empty, 0.75);
            private readonly StackPanel _annotate = Ui.Stack();

            private int _at;
            private int _moved;
            private bool _boxed;

            internal Walk(Session session, ClashPipelineResult board, IReadOnlyList<ClashGroup> groups)
            {
                _session = session;
                _board = board;
                _groups = groups;
            }

            internal UIElement Build() =>
                Ui.Scroll(Ui.Stack(
                    _where,
                    _what,
                    _instruction,
                    Ui.Across(
                        Ui.Button("Previous", () => Go(_at - 1)),
                        Ui.Button("Next", () => Go(_at + 1)),
                        Ui.Button("Box it", ToggleBox)),
                    Ui.Across(
                        Ui.Button("Reviewed", () => Mark(FindingStatus.Reviewed)),
                        Ui.Button("Approved", () => Mark(FindingStatus.Approved)),
                        Ui.Button("Resolved", () => Mark(FindingStatus.Resolved)),
                        Ui.Button("Active", () => Mark(FindingStatus.Active))),
                    Ui.Field("What to do", _annotate),
                    _said,
                    Ui.Note("Keys: N and P move, 1 reviewed, 2 approved, 3 resolved, 0 active, "
                            + "B boxes, A annotates. Nothing is written until this window closes.")));

            /// <summary>Take a key, so a review can be done without the mouse.</summary>
            internal void Key(KeyEventArgs e)
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.N:
                    case System.Windows.Input.Key.Right:
                        Go(_at + 1); break;

                    case System.Windows.Input.Key.P:
                    case System.Windows.Input.Key.Left:
                        Go(_at - 1); break;

                    case System.Windows.Input.Key.D1: Mark(FindingStatus.Reviewed); break;
                    case System.Windows.Input.Key.D2: Mark(FindingStatus.Approved); break;
                    case System.Windows.Input.Key.D3: Mark(FindingStatus.Resolved); break;
                    case System.Windows.Input.Key.D0: Mark(FindingStatus.Active); break;
                    case System.Windows.Input.Key.B: ToggleBox(); break;
                    case System.Windows.Input.Key.A: Annotate(); break;
                    default: return;
                }

                e.Handled = true;
            }

            /// <summary>Frame one group and say what it is.</summary>
            internal void Go(int index)
            {
                if (_groups.Count == 0) return;

                _at = index < 0 ? _groups.Count - 1 : index >= _groups.Count ? 0 : index;

                var group = _groups[_at];
                var units = _session.Profile.Units;

                _where.Text = (_at + 1).ToString(CultureInfo.InvariantCulture) + " of "
                            + _groups.Count.ToString(CultureInfo.InvariantCulture) + " - " + group.Name;

                var first = group.Items.FirstOrDefault();

                _what.Text = Ui.Count(group.Items.Count, "clash")
                           + " - " + (group.AssignedTo ?? PartyLedger.Nobody)
                           + " - " + FindingStatusLattice.ToName(Held(group))
                           + (first == null ? string.Empty
                              : " - " + units.Bare(first.X) + ", " + units.Bare(first.Y)
                                + ", " + units.Bare(first.Z) + " " + units.LengthUnit);

                _instruction.Text = group.Instruction.Describe(units);

                _annotate.Children.Clear();
                _annotate.Children.Add(Editor(group, units));

                Frame(group);
            }

            /// <summary>Select and zoom to the group, and re-box it when boxing is on.</summary>
            private void Frame(ClashGroup group)
            {
                var keys = group.Items.SelectMany(i => new[] { i.Key.A, i.Key.B }).Distinct().ToList();

                if (keys.Count == 0) return;

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 2.0);

                if (!_boxed) return;

                var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();

                if (points.Count > 0)
                    _session.View.SetSectionBox(CoordinateViews.BoxOf(points), Actions.SectionMargin);
            }

            /// <summary>
            /// Box the group, or switch boxing off without losing the box.
            ///
            /// Section Box works on 2024 only through the internal clip-plane surface; this goes
            /// through the documented one, so it works on every year the product supports.
            /// </summary>
            private void ToggleBox()
            {
                _boxed = !_boxed;

                if (!_boxed)
                {
                    _session.View.SetSectionBoxEnabled(false);
                    _said.Text = "Boxing off. The box is kept, so B puts it back.";
                    return;
                }

                Frame(_groups[_at]);
                _said.Text = "Boxed to this group, with " + _session.Profile.Units.Length(Actions.SectionMargin)
                           + " of context around it.";
            }

            /// <summary>Move this group's results, promote-only unless it is being re-opened.</summary>
            private void Mark(FindingStatus status)
            {
                var group = _groups[_at];
                var moved = 0;

                foreach (var item in group.Items)
                {
                    // Active is the one that goes downwards: it is somebody saying, in front of the
                    // geometry, that this is not done. Everything else only ever moves forward.
                    var next = status == FindingStatus.Active
                        ? status
                        : FindingStatusLattice.Merge(item.Status, status);

                    if (next == item.Status) continue;

                    item.Status = next;
                    moved++;
                }

                _moved += moved;

                _said.Text = moved == 0
                    ? group.Name + " was already " + FindingStatusLattice.ToName(status) + " or further."
                    : Ui.Count(moved, "result") + " in " + group.Name + " now "
                      + FindingStatusLattice.ToName(status) + ".";

                _what.Text = Ui.Count(group.Items.Count, "clash")
                           + " - " + (group.AssignedTo ?? PartyLedger.Nobody)
                           + " - " + FindingStatusLattice.ToName(Held(group));

                // Moving on is what somebody wants next nine times in ten, and the tenth can press P.
                Go(_at + 1);
            }

            /// <summary>The instruction editor, which is the sixth key.</summary>
            private UIElement Editor(ClashGroup group, Measure units)
            {
                var verb = group.Instruction.ActionWord;
                var way = group.Instruction.DirectionWord;

                var howFar = Ui.Text(group.Instruction.Amount.HasValue
                    ? units.Bare(group.Instruction.Amount.Value) : string.Empty, null, 80);

                var verbPicker = Ui.Choice(Resolution.Actions, verb, v => verb = v);
                var wayPicker = Ui.Choice(Resolution.Directions, way, v => way = v);

                var record = Ui.Button("Record", () =>
                {
                    double? amount = null;

                    if (!string.IsNullOrWhiteSpace(howFar.Text))
                    {
                        if (!units.TryRead(howFar.Text, out var read))
                        {
                            _said.Text = "\"" + howFar.Text.Trim() + "\" is not a distance.";
                            return;
                        }

                        amount = read;
                    }

                    var instruction = new Resolution(Resolution.ActionOf(verb), null,
                                                     Resolution.AxisOf(way), amount);

                    group.Instruct(instruction);

                    _instruction.Text = instruction.Describe(units);
                    _said.Text = "Recorded.";
                });

                return Ui.Across(verbPicker, wayPicker, howFar, record);
            }

            /// <summary>Put the keyboard focus in the instruction editor.</summary>
            private void Annotate() => _annotate.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.First));

            /// <summary>The group's status: the least-progressed member.</summary>
            private static FindingStatus Held(ClashGroup group) =>
                group.Items.Count == 0 ? FindingStatus.Active : group.Items.Min(i => i.Status);

            /// <summary>Write once, on the way out.</summary>
            internal void Finish()
            {
                _session.View.SetSectionBoxEnabled(false);

                if (_moved == 0) return;

                CoordinateViews.SaveBoard(_session, _board,
                    "reviewed the board: " + Ui.Count(_moved, "result") + " moved");

                _session.Store.Save();
            }
        }
    }
}
