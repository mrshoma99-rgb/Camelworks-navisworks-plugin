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
    /// <b>The scan is bounded and lazy.</b> Reading every property of a half-million-element
    /// federation to fill a dropdown would freeze the host for the sake of autocomplete, so the
    /// scan visits geometry elements round-robin across models, stops at a cap, and is cached
    /// until the federation's shape changes. A dropdown that opens instantly with most of the
    /// answer beats one that opens perfectly a minute later.
    /// </summary>
    public static class Pickers
    {
        /// <summary>How many elements the scan reads before it stops. Enough for every ordinary federation's vocabulary.</summary>
        public const int ScanCap = 600;

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
                             .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// A property-tab picker (the category a property appears under in the host).
        /// </summary>
        /// <param name="current">The value as it stands.</param>
        /// <param name="onChanged">Called with every edit or pick.</param>
        /// <param name="width">Minimum width.</param>
        public static ComboBox Tab(string? current, Action<string>? onChanged, double width = 170) =>
            Lazy(current, onChanged, width,
                 () => Scan().Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

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

        /// <summary>Throw the cache away, for a screen that knows the model just changed.</summary>
        public static void Forget()
        {
            lock (Lock) _found = null;
        }

        /// <summary>
        /// An editable combo whose items arrive the first time it is opened.
        /// </summary>
        private static ComboBox Lazy(string? current, Action<string>? onChanged, double width,
                                     Func<IEnumerable<string>> supply)
        {
            var box = new ComboBox
            {
                MinWidth = width,
                IsEditable = true,
                Text = current ?? string.Empty,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            var filled = false;

            box.DropDownOpened += (s, e) =>
            {
                if (filled) return;
                filled = true;

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
        /// Round-robin across models rather than first-N of the tree, because the first N
        /// elements of a federation are usually one model's — and the property the user wants is
        /// on the other discipline's.
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
            var clock = Stopwatch.StartNew();

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!item.HasGeometry) continue;
                if (read >= ScanCap || clock.ElapsedMilliseconds > 1500) break;

                read++;

                foreach (var property in item.Properties())
                    seen.Add((property.Category, property.Name));
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
