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
    /// Whether a laid-out page fits: nothing cut off, nothing sitting on top of anything else.
    ///
    /// This exists because reading screenshots does not catch it. A field clipped to "Clas" by the
    /// box beside it, a label under a button, two rows overlapping by four pixels — each is
    /// obvious once pointed at and easy to miss in a picture of forty controls, and each shipped.
    /// The layout system already knows: it measured what every element asked for and then told it
    /// what it got. This reads the difference back.
    ///
    /// Two faults are reported.
    ///
    /// **Cut off.** An element whose arranged size is smaller than the size it asked for. Text
    /// that trims or wraps is excluded — an ellipsis is a decision, not an accident — as is
    /// anything inside a scroller on that axis, where being taller than the slot is the point.
    ///
    /// **Overlapping.** Two children of one stack, dock or wrap panel whose arranged rectangles
    /// intersect. Grids and canvases are excluded: layering children is what they are for, and
    /// every focus ring, badge and overlay in this product is a Grid cell drawn over another.
    ///
    /// It runs on the same laid-out tree the shoot photographs, so what it reports is what the
    /// picture shows.
    /// </summary>
    internal static class Fits
    {
        /// <summary>Under this many pixels is rounding, not a fault.</summary>
        private const double Slack = 0.75;

        /// <summary>One thing wrong with a page's layout.</summary>
        internal sealed class Fault
        {
            /// <summary>Build one.</summary>
            /// <param name="kind">"cut off" or "overlapping".</param>
            /// <param name="said">What is wrong, in one line.</param>
            /// <param name="where">The path down the tree to it.</param>
            internal Fault(string kind, string said, string where)
            {
                Kind = kind;
                Said = said;
                Where = where;
            }

            /// <summary>Which of the two faults this is.</summary>
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

            Walk(page, "page", found, scrollsWide: false, scrollsTall: false);

            return found;
        }

        /// <summary>Walk one element and everything under it.</summary>
        /// <param name="element">Where to start.</param>
        /// <param name="path">How this element was reached.</param>
        /// <param name="found">Where faults are collected.</param>
        /// <param name="scrollsWide">Whether an ancestor scrolls horizontally.</param>
        /// <param name="scrollsTall">Whether an ancestor scrolls vertically.</param>
        private static void Walk(DependencyObject element, string path, List<Fault> found,
                                 bool scrollsWide, bool scrollsTall)
        {
            if (element is ScrollViewer scroller)
            {
                scrollsWide |= scroller.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                scrollsTall |= scroller.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            }

            if (element is FrameworkElement here && here.IsVisible) Cut(here, path, found,
                                                                       scrollsWide, scrollsTall);

            var children = Under(element);

            Overlapping(element, children, path, found);

            foreach (var child in children)
                Walk(child, path + " > " + Named(child), found, scrollsWide, scrollsTall);
        }

        /// <summary>
        /// Whether an element was given less room than it asked for.
        /// </summary>
        /// <param name="here">The element.</param>
        /// <param name="path">How it was reached.</param>
        /// <param name="found">Where faults are collected.</param>
        /// <param name="scrollsWide">Whether an ancestor scrolls horizontally.</param>
        /// <param name="scrollsTall">Whether an ancestor scrolls vertically.</param>
        private static void Cut(FrameworkElement here, string path, List<Fault> found,
                                bool scrollsWide, bool scrollsTall)
        {
            // Text that says what it does when it runs out of room is not cut off: it trims to an
            // ellipsis, or it wraps onto another line and asks for the height instead.
            if (here is TextBlock words
                && (words.TextTrimming != TextTrimming.None || words.TextWrapping != TextWrapping.NoWrap))
                return;

            // A scroller's own content is meant to exceed it on the axis that scrolls.
            var wide = !scrollsWide && here.DesiredSize.Width - here.RenderSize.Width > Slack;
            var tall = !scrollsTall && here.DesiredSize.Height - here.RenderSize.Height > Slack;

            if (!wide && !tall) return;

            var said = Named(here) + " asked for "
                     + Size(here.DesiredSize.Width) + " x " + Size(here.DesiredSize.Height)
                     + " and was given "
                     + Size(here.RenderSize.Width) + " x " + Size(here.RenderSize.Height);

            found.Add(new Fault("cut off", said, path));
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
                if (!(child is FrameworkElement one) || !one.IsVisible) continue;
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
