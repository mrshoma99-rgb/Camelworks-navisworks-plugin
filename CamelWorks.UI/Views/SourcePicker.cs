using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

            /// <summary>The elements, when the source is what was selected.</summary>
            public List<ElementKey> Keys { get; set; } = new List<ElementKey>();

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
                    ? Keys.Count == 0
                    : Picked.Count == 0;
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
        public static UIElement Drawn(ISeat seat, string label, Pick side,
                                      Action? onChanged = null, string? blocked = null)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (side == null) throw new ArgumentNullException(nameof(side));

            var holder = new ContentControl();

            void Draw()
            {
                holder.Content = Body(seat, label, side, () =>
                {
                    onChanged?.Invoke();
                }, Draw, blocked);
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
        private static UIElement Body(ISeat seat, string label, Pick side,
                                      Action onChanged, Action onRedraw, string? blocked)
        {
            var held = Ui.Stack();

            if (!string.IsNullOrEmpty(label)) held.Children.Add(Ui.Eyebrow(label));

            held.Children.Add(Ui.Segmented(new[] { FromSets, FromModels, FromSelection },
                                           side.From, picked =>
            {
                side.From = picked;
                onRedraw();
                onChanged();
            }));

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
                MaxWidth = 380,
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
