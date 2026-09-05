using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Inputs fed from the model, so a property name never has to be typed from memory.
    ///
    /// Half the fields in this product previously asked for a property, a tab or a category as bare
    /// text, which means switching to the host, opening an element, reading the spelling and typing
    /// it back. The model already knows every name it carries; these pickers read them once, on
    /// first open, and offer them. Every picker stays editable, because a name the bounded scan did
    /// not reach is still valid.
    ///
    /// Nothing is read until it is asked for. Reading properties off a half-million-element
    /// federation to fill a dropdown is a walk of the model on the host's own thread, so the
    /// property and tab pickers open on one line offering to scan rather than scanning. Typing the
    /// name is faster and touches the model not at all; the list is for when the spelling is not
    /// remembered. Once a scan has run it is cached until the federation changes shape, so the offer
    /// appears once.
    ///
    /// The scan is bounded three ways: a cap on elements read, a budget on the host's thread, and an
    /// early stop once the vocabulary stops growing.
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
                 _ => Scan().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)
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
                 _ => Scan().Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                 onDemand: true);

        /// <summary>
        /// A property picker filled from the elements the clashes are between.
        ///
        /// Not the model's properties. A property column on the clash board reads off a clashing
        /// element, so the names worth offering are the ones those elements carry. The general
        /// picker samples the federation instead — capped, and stopping once fifty elements in a row
        /// add nothing — which reads thousands of elements nobody asked about and can still miss the
        /// one property only the clashing pipes have.
        ///
        /// Across every test, because a column added on this board is added to all of them.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox ClashProperty(string? current, Action<string>? onChanged,
                                             double width = 200) =>
            Lazy(current, onChanged, width,
                 report => Clashing(report).Select(p => p.Name)
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                 onDemand: true,
                 watching: "Reading what the clashing elements carry");

        /// <summary>
        /// A property-tab picker filled from the elements the clashes are between.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox ClashTab(string? current, Action<string>? onChanged,
                                        double width = 170) =>
            Lazy(current, onChanged, width,
                 report => Clashing(report).Select(p => p.Tab)
                                           .Where(t => t.Length > 0)
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                 onDemand: true,
                 watching: "Reading what the clashing elements carry");

        /// <summary>The property vocabulary of the clashing elements, or nothing without a model.</summary>
        /// <param name="report">Progress, returning false to stop.</param>
        private static IReadOnlyList<ClashPropertyName> Clashing(Func<int, bool>? report)
        {
            var session = Host.Current;

            if (session == null || !session.Clash.IsAvailable)
                return Array.Empty<ClashPropertyName>();

            // The same settling rule the model scan uses, over the right population: an element
            // that clashes appears in as many results as it clashes in, and a federation's
            // vocabulary repeats immediately.
            return session.Clash.PropertyVocabulary(Settled, report);
        }

        /// <summary>
        /// A saved-set picker: editable, filled from the document's selection sets on open.
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox Set(string? current, Action<string>? onChanged, double width = 200) =>
            Lazy(current, onChanged, width, _ =>
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
        /// <param name="watching">
        /// What to call the search while it runs. Set for a supplier that can take a noticeable
        /// while: the fill then happens inside a window with a progress line and a Stop rather
        /// than behind a wait cursor, which is a frozen screen with a nicer pointer.
        /// </param>
        private static ComboBox Lazy(string? current, Action<string>? onChanged, double width,
                                     Func<Func<int, bool>?, IEnumerable<string>> supply,
                                     bool onDemand = false, string? watching = null)
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

                var found = new List<string>();

                if (watching != null)
                {
                    // A window with a bar and a Stop. The supplier reports as it goes, so what is
                    // on screen is the search happening rather than a pointer saying to wait.
                    Ui.Working(watching, (onProgress, keepGoing) =>
                    {
                        try
                        {
                            found.AddRange(supply(seen =>
                            {
                                onProgress(seen);
                                return keepGoing();
                            }));
                        }
                        catch (Exception)
                        {
                            // A picker that cannot read is a text box, which is what it was
                            // yesterday. Not worth an error over autocomplete.
                        }
                    });

                    foreach (var item in found) box.Items.Add(item);
                }
                else
                {
                    try
                    {
                        foreach (var item in supply(null)) box.Items.Add(item);
                    }
                    catch (Exception)
                    {
                        // As above.
                    }
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
        /// Read from the whole document rather than one screen's elements. A picker's job is to
        /// spell a property name, and the names live on the model rather than on whatever a board is
        /// showing.
        ///
        /// This walks the tree depth-first. An earlier comment here claimed it went round-robin
        /// across models so the vocabulary came from every discipline rather than the first in the
        /// federation; it never did, because the traversal seam has no per-model scope to interleave,
        /// so a scan that stops early stops inside the first model. The names offered are therefore
        /// that model's, and the box stays editable for the rest.
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
