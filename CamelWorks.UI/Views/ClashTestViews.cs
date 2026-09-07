using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Making one clash test, from the Create tab.
    ///
    /// The Clash matrix builds a grid of them at once, which is the right tool for a coordination
    /// round and the wrong one for a single test: to add "Level 3 ducts against the frame" a
    /// coordinator had to draw a two-by-two matrix or leave for the native clash window, make it
    /// there, and come back to run it. The host has taken a single test since the matrix was
    /// written — <see cref="IClashSource.Create"/> — and nothing but the matrix ever called it.
    ///
    /// Filled in before the test exists rather than edited after, because the host will not
    /// re-point a test that already exists. Its two sides are decided once; the name, the folder,
    /// the kind and the tolerance can all be changed afterwards on the settings panel.
    /// </summary>
    public static class ClashTestViews
    {
        /// <summary>The kinds of clash test the host offers, in the order the legend lists them.</summary>
        internal static IReadOnlyList<MatrixTest> Kinds { get; } = new[]
        {
            MatrixTest.Hard, MatrixTest.HardConservative, MatrixTest.Clearance, MatrixTest.Duplicate,
        };

        /// <summary>What each kind is for, in one line, on the picker that chooses it.</summary>
        /// <param name="kind">The kind.</param>
        internal static string Explains(MatrixTest kind) => kind switch
        {
            MatrixTest.Hard => "Solids overlapping. The usual first pass.",
            MatrixTest.HardConservative => "The triangles rather than the solids: slower, and it "
                                           + "catches more.",
            MatrixTest.Clearance => "Closer than they are allowed to be, by the tolerance.",
            MatrixTest.Duplicate => "The same thing modelled twice. There is no tolerance to set.",
            _ => string.Empty,
        };

        /// <summary>
        /// Ask for a new clash test and make it.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="check">The clash check that will hold it.</param>
        /// <param name="named">A name nothing in the document is called yet.</param>
        /// <param name="onMade">Called with the host's id for the test once it exists.</param>
        public static void New(ISeat seat, ClashCheck check, string named, Action<string> onMade)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (check == null) throw new ArgumentNullException(nameof(check));

            new Form(seat, check, named, onMade).Ask();
        }

        /// <summary>
        /// The form's fields alone, for the design harness.
        ///
        /// The form is a modal window and a harness page is an element, so what the harness draws
        /// is the body without the frame around it. The same builder, so a field added here appears
        /// in the picture without anybody remembering to add it twice.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="check">The clash check that would hold it.</param>
        /// <param name="named">What the test would be called.</param>
        public static UIElement Drawn(ISeat seat, ClashCheck check, string named) =>
            new Form(seat ?? throw new ArgumentNullException(nameof(seat)),
                     check ?? throw new ArgumentNullException(nameof(check)),
                     named, _ => { }).Body();

        // -----------------------------------------------------------------------------------

        private sealed class Form
        {
            private readonly ISeat _seat;
            private readonly ClashCheck _check;
            private readonly Action<string> _onMade;
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _body = Ui.Stack();

            private Window? _window;

            private string _name;
            private string? _folder;
            private MatrixTest _kind = MatrixTest.Hard;
            private double _tolerance = MatrixOptions.DefaultTolerance;
            private Pick _a;
            private Pick _b;

            internal Form(ISeat seat, ClashCheck check, string named, Action<string> onMade)
            {
                _seat = seat;
                _check = check;
                _onMade = onMade;
                _name = named;

                // The selection is the side somebody most often means when they add one test by
                // hand: they have just picked the pipes they are arguing about.
                _a = Pick.Nothing;
                _b = Pick.Nothing;
            }

            private Measure Units => _seat.Profile.Units;

            /// <summary>Why the document will not take a new test, or null when it will.</summary>
            private string? Blocked =>
                !_seat.Clash.IsAvailable
                    ? _seat.ClashProblem ?? "This Navisworks edition has no clash engine."
                    : !_seat.Clash.CanEdit
                        ? _seat.Clash.LastProblem ?? "This document is open read-only."
                        : null;

            /// <summary>
            /// A window of its own rather than <see cref="Ui.Configure"/>.
            ///
            /// The form redraws itself — picking Duplicate takes the tolerance field away, and
            /// picking a side rewrites the line under it — and it closes when the host has taken
            /// the test. A configure page is built once and offers neither.
            /// </summary>
            internal void Ask()
            {
                var window = new Window
                {
                    Title = "CamelWorks - a new clash test",
                    Width = 560,
                    SizeToContent = SizeToContent.Height,
                    MaxHeight = 700,
                    MinWidth = 460,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                };

                _window = window;

                var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

                var foot = new Border
                {
                    Margin = new Thickness(0, 8, 0, 0),
                    Child = Ui.Actions(Ui.Primary("Make the test", Make),
                                       Ui.Quiet("Cancel", () => window.Close())),
                };

                DockPanel.SetDock(foot, Dock.Bottom);
                root.Children.Add(foot);

                DockPanel.SetDock(_log, Dock.Bottom);
                root.Children.Add(Ui.Loose(_log));

                root.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(_body),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                var ground = new Border { Child = root };

                ground.Tint(Border.BackgroundProperty, "Bg");
                window.Content = ground;

                Skin.Apply(window);
                Shell.HostWindow.Own(window);

                Redraw();

                window.ShowDialog();
            }

            /// <summary>The fields, drawn once, with the picked sides read back into them.</summary>
            internal UIElement Body() => Build();

            private UIElement Build()
            {
                var blocked = Blocked;
                var sets = Sets();
                var models = Named(() => _seat.Models());

                return Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Across(
                            Sized(Ui.Field("Name", Ui.Text(_name, text => _name = text, 190)), 200),
                            Sized(Ui.Field("Folder", Ui.Text(_folder ?? string.Empty, "the root",
                                text => _folder =
                                    string.IsNullOrWhiteSpace(text) ? null : text, 160)), 170))),
                    // The sides first and together. They are the test: the two numbers under them
                    // are what it is judged at, and they can be changed afterwards where the sides
                    // cannot.
                    Bench.Group("What is tested against what",
                        Side("First side", () => _a, one => _a = one, sets, models, blocked),
                        Side("Second side", () => _b, one => _b = one, sets, models, blocked)),
                    Bench.Group("How it is judged",
                        Sized(Ui.Field("Kind of test",
                                 Ui.Choice(Kinds.Select(MatrixNaming.Worded).ToList(),
                                     MatrixNaming.Worded(_kind), pick =>
                                     {
                                         _kind = Kinds.FirstOrDefault(
                                             one => string.Equals(MatrixNaming.Worded(one), pick,
                                                                  StringComparison.Ordinal));

                                         Redraw();
                                     }, 230),
                                 Explains(_kind)),
                              380),
                        _kind == MatrixTest.Duplicate
                            ? null
                            : Sized(Ui.Field("Tolerance (" + Units.LengthUnit + ")",
                                       Ui.Text(Units.Bare(_tolerance), text =>
                                       {
                                           if (Units.TryRead(text, out var metres) && metres >= 0)
                                               _tolerance = metres;
                                       }, 90),
                                       _kind == MatrixTest.Clearance
                                           ? "How close the two sides may come before it is "
                                             + "reported."
                                           : "How far they may overlap before it is reported."),
                                    190)),
                    blocked == null ? null : Ui.Note(blocked));
            }

            /// <summary>
            /// One side, as a picker over everything the host can point a test at.
            ///
            /// The same five sources the matrix offers a row, in the same words, because a test
            /// made here and a test made there are the same object in the same document.
            /// </summary>
            /// <param name="label">Which side it is.</param>
            /// <param name="read">What the side is set to.</param>
            /// <param name="write">Sets it.</param>
            /// <param name="sets">The document's saved sets.</param>
            /// <param name="models">The models loaded.</param>
            /// <param name="blocked">Why the document cannot be read, or null.</param>
            private UIElement Side(string label, Func<Pick> read, Action<Pick> write,
                                   IReadOnlyList<SeatSet> sets, IReadOnlyList<string> models,
                                   string? blocked)
            {
                var here = read();
                var offered = new List<string> { Pick.Nothing.Said };

                offered.Add(Selection);
                offered.AddRange(models.Select(one => Model + one));
                offered.AddRange(sets.Select(one => Set + Full(one)));

                if (!offered.Contains(here.Said, StringComparer.Ordinal)) offered.Add(here.Said);

                var picker = Ui.Choice(offered, here.Said, pick =>
                {
                    write(Read(pick, sets));
                    Redraw();
                }, 380);

                if (blocked != null)
                {
                    picker.IsEnabled = false;
                    picker.ToolTip = blocked;
                }

                return Sized(Ui.Field(label, picker, here.Hint), 380);
            }

            /// <summary>What a picked line means, as a side.</summary>
            /// <param name="pick">The line.</param>
            /// <param name="sets">The document's saved sets, for the one it names.</param>
            private Pick Read(string pick, IReadOnlyList<SeatSet> sets)
            {
                if (string.Equals(pick, Selection, StringComparison.Ordinal))
                {
                    // Captured now, not resolved later. "Whatever is selected" is not a thing the
                    // host stores on a test, so a side made from the selection is the elements
                    // that were in it at this moment, and the hint says how many.
                    var keys = Named(() => _seat.Selected()).ToList();

                    return new Pick(Selection, ClashSide.Elements(keys),
                                    Ui.Count(keys.Count, "element") + ", as selected now. A saved "
                                    + "set is re-read every run; this is not.");
                }

                if (pick.StartsWith(Model, StringComparison.Ordinal))
                {
                    var scope = pick.Substring(Model.Length);

                    return new Pick(pick, ClashSide.Model(scope), "The whole model.");
                }

                if (pick.StartsWith(Set, StringComparison.Ordinal))
                {
                    var full = pick.Substring(Set.Length);

                    var set = sets.FirstOrDefault(
                        one => string.Equals(Full(one), full, StringComparison.Ordinal));

                    if (set == null) return Pick.Nothing;

                    return new Pick(pick, ClashSide.Set(set.Name,
                                                        set.Folder.Length == 0 ? null : set.Folder),
                                    (set.IsSearch
                                        ? "A search set: the host re-runs it every time the test "
                                          + "runs. "
                                        : "A selection set. ")
                                    + Ui.Count(set.Members, "element") + " now.");
                }

                return Pick.Nothing;
            }

            /// <summary>Make it, or say why the host would not.</summary>
            private void Make()
            {
                _log.Children.Clear();

                if (_a.Side == null || _b.Side == null)
                {
                    _log.Children.Add(Ui.Problem("A clash test is between two things. Point both "
                                                 + "sides at something."));
                    return;
                }

                if (string.IsNullOrWhiteSpace(_name))
                {
                    _log.Children.Add(Ui.Problem("Give the test a name."));
                    return;
                }

                var spec = new ClashTestSpec(_name.Trim(), _a.Side, _b.Side,
                                             _kind == MatrixTest.Duplicate ? 0 : _tolerance,
                                             _folder, _kind);

                var made = _check.Make(spec, out var problem);

                if (made == null)
                {
                    _log.Children.Add(Ui.Problem(problem ?? "The document would not take it."));
                    return;
                }

                _seat.Record(ActivityKind.Regroup, "made the clash test " + spec.Name);

                _window?.Close();

                _onMade(made.Value.Id);
            }

            /// <summary>Draw the form again, so a change to one field reaches the rest.</summary>
            private void Redraw()
            {
                _body.Children.Clear();
                _body.Children.Add(Ui.Loose(Build()));
            }

            private const string Selection = "What is selected now";
            private const string Model = "Model · ";
            private const string Set = "Set · ";

            /// <summary>A set as one line: its folder, then its name.</summary>
            /// <param name="set">The set.</param>
            private static string Full(SeatSet set) =>
                set.Folder.Length == 0 ? set.Name : set.Folder + " / " + set.Name;

            /// <summary>Every saved set the document holds, or none when it will not say.</summary>
            private IReadOnlyList<SeatSet> Sets() => Named(() => _seat.Sets());

            /// <summary>
            /// Whatever the document answers, or nothing when it will not: a picker that cannot be
            /// filled is a picker with nothing in it, never a screen that refuses to open.
            /// </summary>
            /// <typeparam name="T">What is being read.</typeparam>
            /// <param name="supply">Where it comes from.</param>
            private static IReadOnlyList<T> Named<T>(Func<IReadOnlyList<T>> supply)
            {
                try
                {
                    return supply();
                }
                catch (Exception)
                {
                    return Array.Empty<T>();
                }
            }

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(Panel field, double width)
            {
                field.Width = width;
                field.HorizontalAlignment = HorizontalAlignment.Left;

                return field;
            }

            /// <summary>One side as the form holds it: what the picker says, and what it means.</summary>
            private sealed class Pick
            {
                internal Pick(string said, ClashSide? side, string hint)
                {
                    Said = said;
                    Side = side;
                    Hint = hint;
                }

                /// <summary>A side pointed at nothing, which is what a new form starts with.</summary>
                internal static Pick Nothing { get; } =
                    new Pick("Not set", null, "Pick a set, a model, or what is selected now.");

                /// <summary>The line the picker shows.</summary>
                internal string Said { get; }

                /// <summary>What it resolves to, or null when it is not set.</summary>
                internal ClashSide? Side { get; }

                /// <summary>One line under the picker saying what was picked.</summary>
                internal string Hint { get; }
            }
        }
    }
}
