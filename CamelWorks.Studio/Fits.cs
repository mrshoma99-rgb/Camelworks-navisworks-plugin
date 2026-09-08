using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CamelWorks.Studio
{
    /// <summary>
    /// Whether a laid-out page fits: nothing sitting on top of anything else.
    ///
    /// This exists because reading screenshots does not catch it. A field clipped to "Clas" by the
    /// box beside it, a label under a button, two rows overlapping by four pixels — each is
    /// obvious once pointed at and easy to miss in a picture of forty controls, and each shipped.
    /// The layout system already knows: it measured what every element asked for and then told it
    /// what it got. This reads the difference back.
    ///
    /// One fault is reported: **overlapping** — two children of one stack, dock or wrap panel
    /// whose arranged rectangles intersect. Grids and canvases are excluded: layering children is
    /// what they are for, and every focus ring, badge and overlay in this product is a Grid cell
    /// drawn over another.
    ///
    /// Clipping is not reported, and not for want of trying. The obvious test — an element
    /// arranged smaller than the size it asked for — cannot fire: WPF's Arrange takes the larger
    /// of the rectangle it is given and the element's DesiredSize, so RenderSize is never the
    /// smaller of the two. Five runs went into establishing that, three of them guessing. A
    /// clipped line is cut by an ancestor's clip geometry rather than by its own size, so finding
    /// one means transforming its bounds into each clipping ancestor and comparing there. That is
    /// worth building; it is not what this does, and a check that cannot fire is worse than no
    /// check, because its silence reads as a pass.
    ///
    /// It runs on the same laid-out tree the shoot photographs, so what it reports is what the
    /// picture shows.
    /// </summary>
    internal static class Fits
    {
        /// <summary>Under this many pixels is rounding, not a fault.</summary>
        private const double Slack = 0.75;

        /// <summary>
        /// A page built to be wrong, so a clean run proves the reader works rather than only that
        /// it said nothing.
        ///
        /// The first run of this reader over twenty-six surfaces reported no faults. That is the
        /// answer a reader with a bug gives and the answer a sound one gives, and there is no way
        /// to tell them apart from the output. So every run also reads this: a stack whose
        /// children are pulled onto each other by a negative margin. The overlap must be found,
        /// or the run says the reader is broken rather than that the product is clean.
        /// </summary>
        internal static FrameworkElement Canary()
        {
            var page = new StackPanel { Width = 200 };

            var overlapping = new StackPanel { Orientation = Orientation.Horizontal };

            overlapping.Children.Add(new Border
            {
                Width = 90,
                Height = 24,
                Background = Brushes.Gainsboro,
            });

            overlapping.Children.Add(new Border
            {
                Width = 90,
                Height = 24,
                Background = Brushes.Silver,

                // Pulled onto the one before it. This is the fault, deliberately.
                Margin = new Thickness(-40, 0, 0, 0),
            });

            page.Children.Add(overlapping);

            return page;
        }

        /// <summary>
        /// What the reader finds in <see cref="Canary"/>, or a line saying it is broken.
        ///
        /// Returns null when both faults are found, which is the only passing answer.
        /// </summary>
        internal static string? Broken()
        {
            var page = Canary();

            page.Measure(new Size(200, double.PositiveInfinity));
            page.Arrange(new Rect(0, 0, 200, page.DesiredSize.Height));
            page.UpdateLayout();

            var found = Of(page);

            if (found.Any(one => one.Kind == "overlapping")) return null;

            return "the layout reader is broken: it found no overlap in a page built to hold "
                 + "one, so a clean run over the product proves nothing. It reported: "
                 + (found.Count == 0
                        ? "nothing at all"
                        : string.Join(" | ", found.Select(one => one.ToString())));
        }

        /// <summary>One thing wrong with a page's layout.</summary>
        internal sealed class Fault
        {
            /// <summary>Build one.</summary>
            /// <param name="kind">What sort of fault it is.</param>
            /// <param name="said">What is wrong, in one line.</param>
            /// <param name="where">The path down the tree to it.</param>
            internal Fault(string kind, string said, string where)
            {
                Kind = kind;
                Said = said;
                Where = where;
            }

            /// <summary>What sort of fault this is.</summary>
            internal string Kind { get; }

            /// <summary>What is wrong.</summary>
            internal string Said { get; }

            /// <summary>The path down the tree to it.</summary>
            internal string Where { get; }

            /// <summary>The line the log carries.</summary>
            public override string ToString() => Kind + ": " + Said + "  [" + Where + "]";
        }

        /// <summary>
        /// Read a laid-out page back.
        /// </summary>
        /// <param name="page">The page, already measured and arranged.</param>
        internal static IReadOnlyList<Fault> Of(FrameworkElement page)
        {
            var found = new List<Fault>();

            if (page == null) return found;

            Walk(page, "page", found);

            return found;
        }

        /// <summary>Walk one element and everything under it.</summary>
        /// <param name="element">Where to start.</param>
        /// <param name="path">How this element was reached.</param>
        /// <param name="found">Where faults are collected.</param>
        private static void Walk(DependencyObject element, string path, List<Fault> found)
        {
            var children = Under(element);

            Overlapping(element, children, path, found);

            foreach (var child in children)
                Walk(child, path + " > " + Named(child), found);
        }

        /// <summary>
        /// Whether any two children of a panel that lays its children out in a line intersect.
        ///
        /// Only those panels. A Grid stacks its children in a cell on purpose, a Canvas positions
        /// them absolutely, and a ContentPresenter or a template's root has one child.
        /// </summary>
        /// <param name="parent">The panel.</param>
        /// <param name="children">Its children.</param>
        /// <param name="path">How the panel was reached.</param>
        /// <param name="found">Where faults are collected.</param>
        private static void Overlapping(DependencyObject parent, IReadOnlyList<DependencyObject> children,
                                        string path, List<Fault> found)
        {
            if (!(parent is StackPanel || parent is DockPanel || parent is WrapPanel
                  || parent is VirtualizingStackPanel))
                return;

            var boxes = new List<(string Name, Rect Box)>();

            foreach (var child in children)
            {
                if (!(child is FrameworkElement one) || !Shown(one)) continue;
                if (one.RenderSize.Width <= Slack || one.RenderSize.Height <= Slack) continue;

                Point at;

                try
                {
                    at = one.TranslatePoint(new Point(0, 0), (UIElement)parent);
                }
                catch (InvalidOperationException)
                {
                    // Not in the same tree any more. Nothing to compare it against.
                    continue;
                }

                boxes.Add((Named(one), new Rect(at, one.RenderSize)));
            }

            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var over = Rect.Intersect(boxes[i].Box, boxes[j].Box);

                    if (over.IsEmpty || over.Width <= Slack || over.Height <= Slack) continue;

                    found.Add(new Fault(
                        "overlapping",
                        boxes[i].Name + " and " + boxes[j].Name + " share "
                        + Size(over.Width) + " x " + Size(over.Height) + " of "
                        + parent.GetType().Name,
                        path));
                }
        }

        /// <summary>
        /// Whether an element takes part in the layout.
        ///
        /// Its own Visibility, not IsVisible. IsVisible is false for anything not connected to a
        /// rendered window, and both the pages this reads are detached: the canary is measured on
        /// its own, and the shoot takes the page off its window before it photographs it. So the
        /// reader skipped every element of every page and reported twenty-six surfaces clean,
        /// including the one built to be broken.
        /// </summary>
        /// <param name="element">The element.</param>
        private static bool Shown(UIElement element) =>
            element.Visibility == Visibility.Visible;

        /// <summary>Every visual child of an element.</summary>
        /// <param name="element">The parent.</param>
        private static IReadOnlyList<DependencyObject> Under(DependencyObject element)
        {
            var found = new List<DependencyObject>();

            if (element == null) return found;

            var count = VisualTreeHelper.GetChildrenCount(element);

            for (var i = 0; i < count; i++) found.Add(VisualTreeHelper.GetChild(element, i));

            return found;
        }

        /// <summary>
        /// What to call an element in a report.
        ///
        /// The text it carries where it has any, because "TextBlock" in a page of two hundred of
        /// them names nothing.
        /// </summary>
        /// <param name="element">The element.</param>
        private static string Named(DependencyObject element)
        {
            var kind = element?.GetType().Name ?? "null";

            switch (element)
            {
                case TextBlock words when !string.IsNullOrWhiteSpace(words.Text):
                    return kind + " \"" + Short(words.Text) + "\"";
                case ContentControl held when held.Content is string said && said.Length > 0:
                    return kind + " \"" + Short(said) + "\"";
                case TextBox box when !string.IsNullOrWhiteSpace(box.Text):
                    return kind + " \"" + Short(box.Text) + "\"";
                case FrameworkElement one when !string.IsNullOrEmpty(one.Name):
                    return kind + " " + one.Name;
                default:
                    return kind;
            }
        }

        /// <summary>Text cut to something a log line can carry.</summary>
        /// <param name="said">The text.</param>
        private static string Short(string said)
        {
            var one = said.Replace("\r", " ").Replace("\n", " ").Trim();

            return one.Length <= 32 ? one : one.Substring(0, 31) + "…";
        }

        /// <summary>A length, to one decimal place.</summary>
        /// <param name="value">The length.</param>
        private static string Size(double value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
