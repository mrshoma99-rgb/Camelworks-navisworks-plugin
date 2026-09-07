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

        /// <summary>
        /// A page built to be wrong, so a clean run proves the reader works rather than only that
        /// it said nothing.
        ///
        /// The first run of this reader over twenty-six surfaces reported no faults. That is the
        /// answer a reader with a bug gives and the answer a sound one gives, and there is no way
        /// to tell them apart from the output. So every run also reads this: a stack whose
        /// children are pulled onto each other by a negative margin, and a fixed-width panel
        /// holding text wider than itself. Both faults must be found, or the run says the reader
        /// is broken rather than that the product is clean.
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

            // Wider than the slot it is given, and it neither trims nor wraps, so it is cut off.
            //
            page.Children.Add(new Border { Height = 8 });

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

            var found = new List<Fault>(Of(page));

            // The clipped line is measured and arranged by hand rather than put in a panel and
            // trusted to come out short. Two attempts at that — a fixed-width Border, then a
            // fixed grid column — both handed the text its full width, and the reader was right
            // both times to report no fault. Arranged into a rectangle smaller than it asked for,
            // there is nothing left to be wrong about.
            var line = new TextBlock
            {
                Text = "a line of text far wider than sixty pixels",
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
            };

            line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            line.Arrange(new Rect(0, 0, 60, line.DesiredSize.Height));

            found.AddRange(Of(line));

            var over = found.Any(one => one.Kind == "overlapping");
            var cut = found.Any(one => one.Kind == "cut off");

            if (over && cut) return null;

            // What it saw, not only that it saw the wrong thing. Two runs were spent guessing at
            // why the clipped line was missed, which is two runs more than reporting the numbers
            // would have cost.
            var said = "the layout reader is broken: it found "
                     + (over ? "the overlap" : "no overlap")
                     + " and " + (cut ? "the clipped text" : "no clipped text")
                     + " in a page built to hold both, so a clean run over the product proves "
                     + "nothing. It reported: "
                     + (found.Count == 0
                            ? "nothing at all"
                            : string.Join(" | ", found.Select(one => one.ToString())));

            said += ". The canary's line is laid out free at " + Size(Wide(line))
                  + " and was arranged into " + Size(line.RenderSize.Width);

            return said;
        }

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

            // Nothing with no size: a collapsed pane's contents are not cut off, they are simply
            // not drawn, and reporting them buries the faults that matter.
            if (element is FrameworkElement here && Shown(here)
                && here.RenderSize.Width > Slack && here.RenderSize.Height > Slack)
                Cut(here, path, found, scrollsWide, scrollsTall);

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

            // Text is measured against the text, not against DesiredSize.
            //
            // WPF clamps DesiredSize to the room it offered, so a line of text in a slot half its
            // length reports asking for exactly what it got. The only way to know a line is cut
            // is to lay it out again with no constraint and compare, which is what this does: the
            // reader's first working run found the deliberate overlap in its own canary and not
            // the deliberate clipping, because of this.
            if (here is TextBlock line && !string.IsNullOrEmpty(line.Text))
            {
                var wanted = Wide(line);

                if (!scrollsWide && wanted - line.RenderSize.Width > Slack)
                    found.Add(new Fault(
                        "cut off",
                        Named(line) + " is " + Size(wanted) + " wide in "
                        + Size(line.RenderSize.Width) + ", and neither trims nor wraps",
                        path));

                return;
            }

            // The margin is in DesiredSize and not in RenderSize, so comparing the two as they
            // come reports every element with a margin as cut off. The reader's first working run
            // called 216 of them faults on three pages, all of them margins.
            var margin = here.Margin;

            var asked = new Size(Math.Max(0, here.DesiredSize.Width - margin.Left - margin.Right),
                                 Math.Max(0, here.DesiredSize.Height - margin.Top - margin.Bottom));

            // A scroller's own content is meant to exceed it on the axis that scrolls.
            var wide = !scrollsWide && asked.Width - here.RenderSize.Width > Slack;
            var tall = !scrollsTall && asked.Height - here.RenderSize.Height > Slack;

            if (!wide && !tall) return;

            var said = Named(here) + " asked for "
                     + Size(asked.Width) + " x " + Size(asked.Height)
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
        /// How wide a line of text is when nothing constrains it.
        ///
        /// Laid out from the block's own typeface and size, because the block itself will not say:
        /// its DesiredSize is whatever it was offered.
        /// </summary>
        /// <param name="line">The text block.</param>
        private static double Wide(TextBlock line)
        {
            var typeface = new Typeface(line.FontFamily, line.FontStyle, line.FontWeight,
                                        line.FontStretch);

            var drawn = new FormattedText(line.Text, CultureInfo.InvariantCulture,
                                          line.FlowDirection, typeface, line.FontSize,
                                          Brushes.Black, 96);

            return drawn.WidthIncludingTrailingWhitespace
                 + line.Padding.Left + line.Padding.Right;
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
