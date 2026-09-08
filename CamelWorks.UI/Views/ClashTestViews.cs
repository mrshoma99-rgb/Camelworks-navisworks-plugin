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
        /// The form, to put in the panel beside the list of tests.
        ///
        /// In place rather than in a window. A pop-up over a tab that is already a list on the
        /// left and a settings panel on the right put the form somewhere the list could not be
        /// read from, and made adding a clash test a different act from adding any other kind of
        /// test, which is set up in that panel.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="check">The clash check that will hold it.</param>
        /// <param name="named">A name nothing in the document is called yet.</param>
        /// <param name="onMade">Called with the host's id for the test once it exists.</param>
        /// <param name="onDropped">Called when the form is abandoned.</param>
        public static UIElement Panel(ISeat seat, ClashCheck check, string named,
                                      Action<string> onMade, Action onDropped)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (check == null) throw new ArgumentNullException(nameof(check));

            return new Form(seat, check, named, onMade, onDropped).Panel();
        }

        // -----------------------------------------------------------------------------------

        private sealed class Form
        {
            /// <summary>How tall a side's list of sources may grow before it scrolls.</summary>
            private const double ListHeight = 190;

            private readonly ISeat _seat;
            private readonly ClashCheck _check;
            private readonly Action<string> _onMade;
            private readonly Action _onDropped;
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _body = Ui.Stack();

            private string _name;
            private string? _folder;
            private MatrixTest _kind = MatrixTest.Hard;
            private double _tolerance = MatrixOptions.DefaultTolerance;

            private readonly Chosen _a = new Chosen();
            private readonly Chosen _b = new Chosen();

            internal Form(ISeat seat, ClashCheck check, string named, Action<string> onMade,
                          Action onDropped)
            {
                _seat = seat;
                _check = check;
                _onMade = onMade;
                _onDropped = onDropped;
                _name = named;
            }

            private Measure Units => _seat.Profile.Units;

            /// <summary>Why the document will not take a new test, or null when it will.</summary>
            private string? Blocked =>
                !_seat.Clash.IsAvailable
                    ? _seat.ClashProblem ?? "This Navisworks edition has no clash engine."
                    : !_seat.Clash.CanEdit
                        ? _seat.Clash.LastProblem ?? "This document is open read-only."
                        : null;

            /// <summary>The form with its own actions, as the Create tab's panel holds it.</summary>
            internal UIElement Panel()
            {
                Redraw();

                // No "Make the test" button.
                //
                // Every other kind of test on this tab is saved as you go, and the footer says
                // so. This one had a commit button because a clash test's two sides are settled
                // when the host takes it and cannot be re-pointed afterwards - so something has
                // to decide the moment it is made. That is a real constraint and it is not a
                // reason for a button: the moment both sides have something on them is the
                // moment, and the form knows when that is.
                //
                // Which is also why it sat at the foot of a form long enough to scroll. Somebody
                // filling in two sides had to find their way to the bottom of the panel to
                // finish, on the one tab where nothing else asks that.
                var drop = Ui.Quiet("Cancel", () => Ui.Safely(_onDropped));

                var foot = Ui.Actions(drop);

                foot.Margin = new Thickness(0, 10, 0, 0);

                return Ui.Stack(Ui.Loose(_body), Ui.Loose(_log), foot);
            }

            /// <summary>The fields alone, for the harness.</summary>
            internal UIElement Body() => Build();

            private UIElement Build()
            {
                var blocked = Blocked;
                var sets = Sets();
                var models = _seat.ModelNames();

                return Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Across(
                            // Settle on the name too: with both sides already picked, typing the
                            // name is the last thing left, and the test is made when it lands.
                            Sized(Ui.Field("Name", Ui.Text(_name, text =>
                                {
                                    _name = text;
                                    Settle();
                                }, Ui.Wide.Name)), 200),
                            Sized(Ui.Field("Folder", Folder()), 190))),

                    // The sides first and together. They are the test: the two numbers under them
                    // are what it is judged at, and they can be changed afterwards where the sides
                    // cannot.
                    Bench.Group("What is tested against what",
                        Ui.Small("The test is made as soon as both sides have something on them. "
                                 + "Its name, folder, kind and tolerance can all be changed "
                                 + "afterwards; its two sides cannot, because Navisworks will "
                                 + "not re-point a test that exists."),
                        Side("First side", _a, sets, models, blocked),
                        Side("Second side", _b, sets, models, blocked)),

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
            /// Which folder the new test is filed in.
            ///
            /// The same control the panel beside this uses, and the settings panels of the other
            /// two checks. This was a closed dropdown carrying "The root", the folders, and "A
            /// new folder..." - which opened a prompt - while filing a test that already existed
            /// was a bare text box. Two different answers to one question, a window apart.
            /// </summary>
            private UIElement Folder() =>
                Ui.Folder(_folder, Folders(),
                          said => _folder = said.Length == 0 ? null : said, 190);

            /// <summary>The clash folders this document already has, named once each.</summary>
            private IReadOnlyList<string> Folders() => Seated.Folders(_check);

            /// <summary>
            /// One side: which kind of source it is built from, and which of them.
            ///
            /// A list with ticks and a Find box, not a dropdown. Both sides used to be one combo
            /// each holding the selection, every loaded model and every saved set in one run —
            /// on a federation that is several hundred lines, unsearchable, and one of them could
            /// be picked. A side is a discipline and a discipline is several sets.
            /// </summary>
            /// <param name="label">Which side it is.</param>
            /// <param name="side">What it is set to, edited in place.</param>
            /// <param name="sets">The document's saved sets.</param>
            /// <param name="models">The models loaded.</param>
            /// <param name="blocked">Why the document cannot be read, or null.</param>
            private UIElement Side(string label, Chosen side, IReadOnlyList<SeatSet> sets,
                                   IReadOnlyList<string> models, string? blocked)
            {
                var held = Ui.Stack();

                held.Children.Add(Ui.Eyebrow(label));

                var kinds = Ui.Segmented(new[] { FromSets, FromModels, FromSelection },
                                         side.From, picked =>
                {
                    side.From = picked;
                    Redraw();
                });

                held.Children.Add(kinds);

                if (string.Equals(side.From, FromSelection, StringComparison.Ordinal))
                {
                    var keys = Seated.Offer(() => _seat.Selected());

                    side.Keys = keys.ToList();

                    held.Children.Add(Ui.Small(keys.Count == 0
                        ? "Nothing was selected in Navisworks when this was read. Select the "
                          + "elements this side is about, then press Read the selection."
                        : Ui.Count(keys.Count, "element") + ", as selected when this was read. A "
                          + "saved set is re-read every time the test runs; this list is not."));

                    // The way to read it again.
                    //
                    // The selection is read once, while this line is being drawn. Going to
                    // Navisworks and selecting something changes nothing here, because nothing
                    // asks again - so a side set to "What is selected" said nothing was selected
                    // however much was, and the only way to get a different answer was to switch
                    // the line to something else and back. That is the whole of "it doesn't see
                    // the Navisworks selection".
                    held.Children.Add(Ui.Link("Read the selection", () => Ui.Safely(Redraw)));

                    return Framed(held, blocked);
                }

                // The shared ticked list: one filter loop, one empty state, one thing to fix.
                var models_ = string.Equals(side.From, FromModels, StringComparison.Ordinal);

                var lines = models_
                    ? models.ToList()
                    : sets.Select(Full).ToList();

                var says = Ui.Small(string.Empty);

                void Said()
                {
                    var count = side.Picked.Count;

                    says.Text = count == 0
                        ? lines.Count == 0 ? string.Empty : "Nothing selected yet."
                        : count == 1
                            ? side.Picked[0]
                            : Ui.Count(count, "of them") + ": " + string.Join(", ", side.Picked);
                }

                held.Children.Add(Parts.Ticked(lines, side.Has,
                    (line, on) => { side.Put(line, on); Said(); Settle(); },
                    models_ ? "Nothing is loaded." : "This document holds no saved sets.",
                    () => { side.Picked.Clear(); Said(); }));

                Said();

                held.Children.Add(says);

                return Framed(held, blocked);
            }

            /// <summary>A side in its own card, greyed with a reason when the document refuses.</summary>
            /// <param name="held">The side's controls.</param>
            /// <param name="blocked">Why the document cannot be read, or null.</param>
            private static UIElement Framed(Panel held, string? blocked)
            {
                // Capped like every other field on this form. Left to stretch, a side ran the
                // width of whatever pane it was put in while the fields above it stopped at 380,
                // so the two sides read as a different kind of thing from the rest of the form.
                var card = new Border
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 9, 10, 10),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    MaxWidth = 520,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = held,
                };

                card.Tint(Border.BackgroundProperty, "Card2");
                card.Tint(Border.BorderBrushProperty, "Bd");

                if (blocked != null)
                {
                    card.IsEnabled = false;
                    card.ToolTip = blocked;
                }

                return card;
            }

            /// <summary>Make it, or say why the host would not.</summary>
            /// <summary>
            /// Make the test if the form is ready, and say nothing if it is not.
            ///
            /// Called after every change rather than from a button. Quiet on a form that is not
            /// finished: a half-filled side is somebody still working, not somebody who made a
            /// mistake, so it does not complain about the second side while the first is being
            /// picked.
            ///
            /// Once. The flag matters because the calls that reach here come from a redraw, and
            /// the host taking a test is not something to do twice on one form.
            /// </summary>
            private void Settle()
            {
                if (_made) return;
                if (Blocked != null) return;
                if (string.IsNullOrWhiteSpace(_name)) return;

                var ready = Read(_a);
                var also = Read(_b);

                if (ready == null || also == null) return;

                _made = true;

                Make();
            }

            /// <summary>True once the host has taken this form's test, so it is never made twice.</summary>
            private bool _made;

            private void Make()
            {
                _log.Children.Clear();

                var a = Read(_a);
                var b = Read(_b);

                if (a == null || b == null)
                {
                    _log.Children.Add(Ui.Problem("A clash test is between two things. Both sides "
                                                 + "need something on them."));
                    _made = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(_name))
                {
                    _log.Children.Add(Ui.Problem("Give the test a name."));
                    _made = false;
                    return;
                }

                var spec = new ClashTestSpec(_name.Trim(), a, b,
                                             _kind == MatrixTest.Duplicate ? 0 : _tolerance,
                                             _folder, _kind);

                var made = _check.Make(spec, out var problem);

                if (made == null)
                {
                    _log.Children.Add(Ui.Problem(problem ?? "The document would not take it."));

                    // Left open, so a refusal is something to correct on the form rather than a
                    // form that has quietly stopped trying.
                    _made = false;
                    return;
                }

                _seat.Record(ActivityKind.Regroup, "made the clash test " + spec.Name);

                _onMade(made.Value.Id);
            }

            /// <summary>What one side resolves to, or null when nothing is on it.</summary>
            /// <param name="side">The side.</param>
            private ClashSide? Read(Chosen side)
            {
                if (string.Equals(side.From, FromSelection, StringComparison.Ordinal))
                    return side.Keys.Count == 0 ? null : ClashSide.Elements(side.Keys);

                if (side.Picked.Count == 0) return null;

                if (string.Equals(side.From, FromModels, StringComparison.Ordinal))
                {
                    // The scope of each model ticked, not its name.
                    //
                    // ClashSide.Models is written in scopes - the host matches them against the
                    // scope of each loaded model, which is a hash of the file it came from - and
                    // this handed it the names off the list instead. A name never equals a hash,
                    // so a test with a whole-models side was refused with "None of the models
                    // this test is about are loaded any more", on a document where all of them
                    // were. Whole models could not be used at all.
                    var scopes = _seat.Loaded()
                        .Where(one => side.Has(one.Name))
                        .Select(one => one.Scope)
                        .Where(one => one.Length > 0)
                        .ToList();

                    return scopes.Count == 0 ? null : ClashSide.Models(scopes);
                }

                var named = Sets()
                    .Where(one => side.Has(Full(one)))
                    .Select(one => new ClashSetName(one.Name,
                                                    one.Folder.Length == 0 ? null : one.Folder))
                    .ToList();

                return named.Count == 0 ? null : ClashSide.Sets(named);
            }

            /// <summary>
            /// Draw the form again, so a change to one field reaches the rest.
            ///
            /// And settle first: every change that could finish the form comes through here, so
            /// this is where the test is made. Before the draw, because making it hands the tab
            /// its id and the tab replaces this form with the panel for the test that now exists.
            /// </summary>
            private void Redraw()
            {
                Settle();

                _body.Children.Clear();
                _body.Children.Add(Ui.Loose(Build()));
            }

            private const string FromSets = "Saved sets";
            private const string FromModels = "Whole models";
            private const string FromSelection = "What is selected";

            /// <summary>A set as one line: its folder, then its name.</summary>
            /// <param name="set">The set.</param>
            private static string Full(SeatSet set) =>
                set.Folder.Length == 0 ? set.Name : set.Folder + " / " + set.Name;

            /// <summary>Every saved set the document holds, or none when it will not say.</summary>
            private IReadOnlyList<SeatSet> Sets() => _seat.Sets();

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(Panel field, double width)
            {
                field.Width = width;
                field.HorizontalAlignment = HorizontalAlignment.Left;

                return field;
            }

            /// <summary>One side as the form holds it: which kind of source, and which of them.</summary>
            private sealed class Chosen
            {
                /// <summary>Which kind of source this side is built from.</summary>
                internal string From { get; set; } = FromSets;

                /// <summary>The lines ticked, in the order they were ticked.</summary>
                internal List<string> Picked { get; } = new List<string>();

                /// <summary>The elements, when the side is what was selected.</summary>
                internal List<Core.Identity.ElementKey> Keys { get; set; } =
                    new List<Core.Identity.ElementKey>();

                /// <summary>Whether a line is ticked.</summary>
                /// <param name="said">The line.</param>
                internal bool Has(string said) =>
                    Picked.Contains(said, StringComparer.OrdinalIgnoreCase);

                /// <summary>Tick or untick a line.</summary>
                /// <param name="said">The line.</param>
                /// <param name="on">True to tick it.</param>
                internal void Put(string said, bool on)
                {
                    if (on)
                    {
                        if (!Has(said)) Picked.Add(said);
                        return;
                    }

                    Picked.RemoveAll(one =>
                        string.Equals(one, said, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }
}
