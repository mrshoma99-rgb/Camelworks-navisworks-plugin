using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Inputs fed from the model, so nobody types a property name from memory.
    ///
    /// Half the fields in this product used to ask for a property, a tab or a category as bare
    /// text — which means the user alt-tabs to the host, opens an element, reads the spelling,
    /// and types it back in. The model already knows every name it carries; these pickers read
    /// them once, on first open, and offer them. Every picker stays editable, because a name the
    /// bounded scan did not reach is still a valid answer.
    ///
    /// <b>Nothing is read until somebody asks.</b> Reading properties off a half-million-element
    /// federation to fill a dropdown is a walk of the model, and it happens on the host's own
    /// thread — so the property and tab pickers open on one line offering to go and look, rather
    /// than going. Typing the name is always the faster answer and it never touches the model at
    /// all; the list is for when you cannot remember the spelling. Once a scan has run it is
    /// cached until the federation changes shape, so the offer only appears once.
    ///
    /// The scan itself is bounded twice over: a cap on elements read, a budget on the host's
    /// thread, and an early stop as soon as the vocabulary stops growing.
    /// </summary>
    public static class Pickers
    {
        /// <summary>How many elements the scan reads before it stops. Enough for every ordinary federation's vocabulary.</summary>
        public const int ScanCap = 600;

        /// <summary>
        /// How long the scan may hold the host's thread, in milliseconds.
        ///
        /// It was 1500. This runs inside <c>DropDownOpened</c>, which is the host's own UI
        /// thread, so the number is not a budget for a background task — it is how long
        /// Navisworks is frozen with no menu on screen after somebody clicks a combo box. A
        /// second and a half of that reads as a hang, and it was reported as one.
        /// </summary>
        public const int Budget = 300;

        /// <summary>How many elements in a row may add no new property name before the scan stops.</summary>
        public const int Settled = 50;

        private static readonly object Lock = new object();
        private static List<(string Category, string Name)>? _found;
        private static int _shape;

        /// <summary>
        /// A property-name picker: editable, filled from the model on first open.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox Property(string? current, Action<string>? onChanged, double width = 200) =>
            Lazy(current, onChanged, width,
                 () => Scan().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                 onDemand: true);

        /// <summary>
        /// A property-tab picker (the category a property appears under in the host).
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox Tab(string? current, Action<string>? onChanged, double width = 170) =>
            Lazy(current, onChanged, width,
                 () => Scan().Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                 onDemand: true);

        /// <summary>
        /// A saved-set picker: editable, filled from the document's selection sets on open.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox Set(string? current, Action<string>? onChanged, double width = 200) =>
            Lazy(current, onChanged, width, () =>
            {
                var session = Host.Current;
                if (session == null) return Enumerable.Empty<string>();

                return session.Model.Sets()
                              .Select(set => set.DisplayName)
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            });

        static Pickers() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Throw the cache away.
        ///
        /// Called when the document changes as well as by a screen that knows the model just
        /// changed - a picker offering the last federation's sets and levels is a picker that
        /// fills a box with a name nothing in this model carries.
        /// </summary>
        public static void Forget()
        {
            lock (Lock) _found = null;
        }

        /// <summary>
        /// An editable combo whose items arrive when somebody asks for them.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        /// <param name="supply">Where the names come from.</param>
        /// <param name="onDemand">
        /// True when filling the list costs a walk of the model, so the list offers to go looking
        /// rather than going. False for a supplier the document answers instantly.
        /// </param>
        private static ComboBox Lazy(string? current, Action<string>? onChanged, double width,
                                     Func<IEnumerable<string>> supply, bool onDemand = false)
        {
            var box = new ComboBox
            {
                MinWidth = width,
                IsEditable = true,
                Text = current ?? string.Empty,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                ToolTip = onDemand
                    ? "Type the name, or open the list and look through the model for it."
                    : null,
            };

            var filled = false;

            void Fill()
            {
                filled = true;
                box.Items.Clear();

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    foreach (var item in supply()) box.Items.Add(item);
                }
                catch (Exception)
                {
                    // A picker that cannot read the model is a text box, which is what it was
                    // yesterday. Not worth an error over autocomplete.
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }

                if (box.Items.Count == 0)
                    box.Items.Add(new ComboBoxItem
                    {
                        Content = "Nothing found — type the name",
                        IsEnabled = false,
                    });
            }

            box.DropDownOpened += (s, e) =>
            {
                if (filled) return;

                // A list that costs a walk offers the walk instead of taking it.
                //
                // This used to scan the moment the dropdown opened, which put a walk of the
                // federation between the click and the menu appearing — the host with nothing on
                // screen, on its own thread, for as long as the walk took. The scan is bounded
                // now, and it is still not something to spend on somebody who opened the list to
                // check they had typed the name right.
                if (!onDemand)
                {
                    Fill();
                    return;
                }

                if (box.Items.Count > 0) return;

                var look = new ComboBoxItem { Content = "Look through the model for names…" };

                look.PreviewMouseLeftButtonUp += (a, b) =>
                {
                    // Handled, or the sentinel's own words land in the box as if they were a
                    // property called "Look through the model for names".
                    b.Handled = true;
                    box.IsDropDownOpen = false;

                    Fill();

                    box.IsDropDownOpen = true;
                };

                box.Items.Add(look);
            };

            if (onChanged != null)
            {
                // Both paths: a pick from the list lands in Text after the event, so the handler
                // reads the box rather than the event args and stays one code path.
                box.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                               new TextChangedEventHandler((s, e) => onChanged(box.Text)));
                box.SelectionChanged += (s, e) =>
                {
                    if (box.SelectedItem is string pick) onChanged(pick);
                };
            }

            return box;
        }

        /// <summary>
        /// The model's property vocabulary, read once and kept until the federation changes shape.
        ///
        /// <b>The whole document, not one screen's elements.</b> A picker's job is to spell a
        /// property name, and the names live on the model rather than on whatever a board happens
        /// to be showing.
        ///
        /// <b>What it is not.</b> This walked the tree depth-first and the comment here claimed it
        /// went round-robin across models, so that the vocabulary came from every discipline
        /// rather than whichever one is first in the federation. It never did: the traversal seam
        /// has no per-model scope to interleave, so a scan that stops early stops inside the first
        /// model. The names it offers are therefore that model's, and the box stays editable for
        /// the rest — which was always the reason it is editable.
        /// </summary>
        private static IReadOnlyList<(string Category, string Name)> Scan()
        {
            var session = Host.Current;
            if (session == null) return Array.Empty<(string, string)>();

            var shape = session.Model.Models.Count * 397
                        ^ string.Join("|", session.Model.Models.Select(m => m.Scope)).GetHashCode();

            lock (Lock)
            {
                if (_found != null && _shape == shape) return _found;
            }

            var seen = new HashSet<(string, string)>();
            var read = 0;
            var quiet = 0;
            var clock = Stopwatch.StartNew();

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                // At the top, before anything else looks at the item.
                //
                // These two lines were below the HasGeometry test, which meant a node without
                // geometry skipped both of them - so the walk was bounded only by how many
                // GEOMETRY items it met. A federation whose tree is mostly groups, layers and
                // inserts never reached either guard, and the scan ran to the end of the model
                // with the host's thread in it. That is the freeze: it happens before the
                // dropdown can paint, because the dropdown opens on the thread doing the walk.
                if (read >= ScanCap || clock.ElapsedMilliseconds > Budget) break;

                if (!item.HasGeometry) continue;

                read++;

                var before = seen.Count;

                foreach (var property in item.Properties())
                    seen.Add((property.Category, property.Name));

                // A federation's property vocabulary is small and repeats immediately: the
                // fiftieth duct carries the names the first one did. Once fifty elements in a
                // row have added nothing, there is nothing left to find, and reading six hundred
                // to prove it is six hundred COM enumerations nobody is waiting for.
                quiet = seen.Count == before ? quiet + 1 : 0;

                if (quiet >= Settled) break;
            }

            var found = seen.ToList();

            lock (Lock)
            {
                _found = found;
                _shape = shape;
            }

            return found;
        }
    }
}
