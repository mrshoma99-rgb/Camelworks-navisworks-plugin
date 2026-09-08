using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Identity;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Where a check looks: saved sets, whole models, or what is selected.
    ///
    /// One control, because there were three. A clash test's two sides were this - a row of three
    /// origins over a ticked list with a Find box - while a clearance run's "search for walkable
    /// surfaces in" and a fall-hazard run's three sources were each a single-value field that
    /// opened a search window. Three shapes for one question, on three tabs of one app.
    ///
    /// This is the clash one, which is the one that can say what a coordinator means: a discipline
    /// is rarely one set, so a source is several of them, and the three origins are on the face
    /// rather than behind a press.
    /// </summary>
    public static class SourcePicker
    {
        /// <summary>What the sets origin is called.</summary>
        public const string FromSets = "Saved sets";

        /// <summary>What the models origin is called.</summary>
        public const string FromModels = "Whole models";

        /// <summary>What the selection-tree origin is called.</summary>
        public const string FromTree = "Model tree";

        /// <summary>What the selection origin is called.</summary>
        public const string FromSelection = "What is selected";

        /// <summary>How tall the list of sources grows before it scrolls.</summary>
        private const double ListHeight = 190;

        /// <summary>
        /// What one source is set to, held by the screen that owns it and edited in place.
        /// </summary>
        public sealed class Pick
        {
            /// <summary>Which origin this source is built from.</summary>
            public string From { get; set; } = FromSets;

            /// <summary>The lines ticked, in the order they were ticked.</summary>
            public List<string> Picked { get; } = new List<string>();

            /// <summary>
            /// The elements, when the source is what was selected, or the nodes when it is
            /// levels of the model tree.
            /// </summary>
            public List<ElementKey> Keys { get; set; } = new List<ElementKey>();

            /// <summary>What each key under <see cref="FromTree"/> is called, for the summary.</summary>
            public Dictionary<string, string> Named { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Whether a line is ticked.</summary>
            /// <param name="said">The line.</param>
            public bool Has(string said) =>
                Picked.Contains(said, StringComparer.OrdinalIgnoreCase);

            /// <summary>Tick or untick a line.</summary>
            /// <param name="said">The line.</param>
            /// <param name="on">True to tick it.</param>
            public void Put(string said, bool on)
            {
                if (on)
                {
                    if (!Has(said)) Picked.Add(said);
                    return;
                }

                Picked.RemoveAll(one =>
                    string.Equals(one, said, StringComparison.OrdinalIgnoreCase));
            }

            /// <summary>Whether anything at all is on this source.</summary>
            public bool IsEmpty =>
                string.Equals(From, FromSelection, StringComparison.Ordinal)
                || string.Equals(From, FromTree, StringComparison.Ordinal)
                    ? Keys.Count == 0
                    : Picked.Count == 0;
        }

        /// <summary>
        /// One further origin a caller offers beside the three, for a side that can also say
        /// something a list of names cannot.
        ///
        /// A clearance run's obstruction side is the reason this exists: "everything except the
        /// surfaces" is not a set, a model or a selection, and it is the setting most of those runs
        /// are left on.
        /// </summary>
        public sealed class Extra
        {
            /// <summary>What the segment is called. Also what <see cref="Pick.From"/> holds.</summary>
            public string Label { get; set; } = string.Empty;

            /// <summary>The sentence shown in place of a list when this origin is picked.</summary>
            public string Note { get; set; } = string.Empty;
        }

        /// <summary>An origin that is everything loaded, for a caller that offers one.</summary>
        public static Extra Everywhere { get; } = new Extra
        {
            Label = CheckSource.Everywhere,
            Note = "Every element in every loaded model.",
        };

        /// <summary>An origin that is whatever the other side of the run did not take.</summary>
        public static Extra Remainder { get; } = new Extra
        {
            Label = CheckSource.Remainder,
            Note = "Everything loaded except the elements the surface side took.",
        };

        /// <summary>What a saved source looks like on the form.</summary>
        /// <param name="source">The saved source.</param>
        public static Pick Read(CheckSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            switch (source.Origin)
            {
                case CheckOrigin.Everything: return new Pick { From = CheckSource.Everywhere };
                case CheckOrigin.TheRest: return new Pick { From = CheckSource.Remainder };

                case CheckOrigin.Selection:
                    return new Pick { From = FromSelection, Keys = source.Keys.ToList() };

                case CheckOrigin.Branches:
                    return new Pick { From = FromTree, Keys = source.Keys.ToList() };

                case CheckOrigin.Models:
                    return Ticked(FromModels, source);

                default:
                    return Ticked(FromSets, source);
            }
        }

        private static Pick Ticked(string from, CheckSource source)
        {
            var pick = new Pick { From = from };

            foreach (var name in source.Names) pick.Picked.Add(name);

            return pick;
        }

        /// <summary>What the form holds, as a source a check can save and read.</summary>
        /// <param name="pick">What the form holds.</param>
        public static CheckSource Written(Pick pick)
        {
            if (pick == null) throw new ArgumentNullException(nameof(pick));

            if (string.Equals(pick.From, CheckSource.Everywhere, StringComparison.Ordinal))
                return CheckSource.Everything();

            if (string.Equals(pick.From, CheckSource.Remainder, StringComparison.Ordinal))
                return CheckSource.Rest();

            if (string.Equals(pick.From, FromSelection, StringComparison.Ordinal))
                return CheckSource.Selection(pick.Keys);

            if (string.Equals(pick.From, FromTree, StringComparison.Ordinal))
                return CheckSource.Branches(pick.Keys);

            return string.Equals(pick.From, FromModels, StringComparison.Ordinal)
                ? CheckSource.Models(pick.Picked.ToArray())
                : CheckSource.Sets(pick.Picked.ToArray());
        }

        /// <summary>
        /// Draw one source.
        ///
        /// It redraws itself when the origin changes or the selection is re-read, and tells the
        /// caller through <paramref name="onChanged"/> so the screen can save what it holds. The
        /// caller does not rebuild it: a form that redraws itself on every tick loses the Find box
        /// the moment somebody types in it.
        /// </summary>
        /// <param name="seat">The seat, for the sets, the models and the selection.</param>
        /// <param name="label">What this source is called on the form.</param>
        /// <param name="side">What it is set to, edited in place.</param>
        /// <param name="onChanged">Called after anything is picked or unpicked.</param>
        /// <param name="blocked">Why the document cannot be read, or null.</param>
        /// <param name="extras">Further origins this side offers, or null for the three.</param>
        public static FrameworkElement Drawn(ISeat seat, string label, Pick side,
                                      Action? onChanged = null, string? blocked = null,
                                      IReadOnlyList<Extra>? extras = null)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (side == null) throw new ArgumentNullException(nameof(side));

            var holder = new ContentControl();

            void Draw()
            {
                holder.Content = Body(seat, label, side, () =>
                {
                    onChanged?.Invoke();
                }, Draw, blocked, extras);
            }

            Draw();

            return holder;
        }

        /// <summary>The source's own controls, built once per draw.</summary>
        /// <param name="seat">The seat.</param>
        /// <param name="label">What this source is called.</param>
        /// <param name="side">What it is set to.</param>
        /// <param name="onChanged">Called after a pick.</param>
        /// <param name="onRedraw">Draws the whole source again.</param>
        /// <param name="blocked">Why the document cannot be read, or null.</param>
        /// <param name="extras">Further origins this side offers, or null for the three.</param>
        private static UIElement Body(ISeat seat, string label, Pick side,
                                      Action onChanged, Action onRedraw, string? blocked,
                                      IReadOnlyList<Extra>? extras)
        {
            var held = Ui.Stack();

            if (!string.IsNullOrEmpty(label)) held.Children.Add(Ui.Eyebrow(label));

            var origins = new List<string> { FromSets, FromModels, FromTree, FromSelection };

            if (extras != null) origins.AddRange(extras.Select(one => one.Label));

            held.Children.Add(Ui.Segmented(origins.ToArray(), side.From, picked =>
            {
                side.From = picked;
                onRedraw();
                onChanged();
            }));

            var standing = extras?.FirstOrDefault(one =>
                string.Equals(one.Label, side.From, StringComparison.Ordinal));

            // An extra origin names what it reads on its own, so there is nothing to tick. The
            // sentence is still drawn, because a segment with an empty card under it reads as a
            // list that failed to load.
            if (standing != null)
            {
                held.Children.Add(Ui.Small(standing.Note));

                return Framed(held, blocked);
            }

            if (string.Equals(side.From, FromTree, StringComparison.Ordinal))
            {
                var told = Ui.Small(string.Empty);

                void Counted()
                {
                    told.Text = side.Keys.Count == 0
                        ? "Nothing picked yet. Tick a level and the run reads everything under it."
                        : Ui.Count(side.Keys.Count, "part") + " of the tree: "
                          + string.Join(", ", side.Keys
                              .Select(key => side.Named.TryGetValue(key.ToString(), out var name)
                                  ? name
                                  : "one level")
                              .Take(4));
                }

                held.Children.Add(Parts.Branching(
                    under => Seated.Offer(() => seat.Tree(under)),
                    key => side.Keys.Contains(key),
                    (node, on) =>
                    {
                        if (on)
                        {
                            if (!side.Keys.Contains(node.Key)) side.Keys.Add(node.Key);

                            side.Named[node.Key.ToString()] = node.Name;
                        }
                        else
                        {
                            side.Keys.Remove(node.Key);
                            side.Named.Remove(node.Key.ToString());
                        }

                        Counted();
                        onChanged();
                    },
                    "This seat will not offer a model tree.",
                    () =>
                    {
                        side.Keys.Clear();
                        side.Named.Clear();
                        Counted();
                        onChanged();
                    }));

                Counted();

                held.Children.Add(told);

                return Framed(held, blocked);
            }

            if (string.Equals(side.From, FromSelection, StringComparison.Ordinal))
            {
                var keys = Seated.Offer(() => seat.Selected());

                side.Keys = keys.ToList();

                held.Children.Add(Ui.Small(keys.Count == 0
                    ? "Nothing was selected in Navisworks when this was read. Select the elements "
                      + "this is about, then press Read the selection."
                    : Ui.Count(keys.Count, "element") + ", as selected when this was read. A saved "
                      + "set is re-read every time the check runs; this list is not."));

                // The selection is read while this is being drawn, so without this there is no
                // way to ask again: going to Navisworks and selecting something changed nothing.
                held.Children.Add(Ui.Link("Read the selection", () => Ui.Safely(() =>
                {
                    onRedraw();
                    onChanged();
                })));

                return Framed(held, blocked);
            }

            var models = string.Equals(side.From, FromModels, StringComparison.Ordinal);

            var lines = models
                ? seat.Loaded().Select(one => one.Name).ToList()
                : seat.Sets().Select(Full).ToList();

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
                (line, on) => { side.Put(line, on); Said(); onChanged(); },
                models ? "Nothing is loaded." : "This document holds no saved sets.",
                () => { side.Picked.Clear(); Said(); onChanged(); }));

            Said();

            held.Children.Add(says);

            return Framed(held, blocked);
        }

        /// <summary>A set as one line: its folder, then its name.</summary>
        /// <param name="set">The set.</param>
        public static string Full(SeatSet set) =>
            set.Folder.Length == 0 ? set.Name : set.Folder + " / " + set.Name;

        /// <summary>
        /// A source in its own card, greyed with a reason when the document refuses.
        /// </summary>
        /// <param name="held">The source's controls.</param>
        /// <param name="blocked">Why the document cannot be read, or null.</param>
        private static UIElement Framed(Panel held, string? blocked)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 9, 10, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),

                // No ceiling. The card sits in a panel that is already the narrower half of the
                // tab, and a cap here left the origins wrapping onto a second line inside a card
                // with white space beside it.
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = held,
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            if (blocked == null) return card;

            card.IsEnabled = false;
            card.ToolTip = blocked;

            return card;
        }

        /// <summary>How tall the ticked list is allowed to grow. Read by the callers' layout.</summary>
        public static double Height => ListHeight;
    }
}
