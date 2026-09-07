using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A long list of rows where only the rows on screen exist.
    ///
    /// The clash board previously drew every result it was showing as a grid of a couple of dozen
    /// elements, on screen or three thousand rows below it. A test with five thousand results is
    /// well over a hundred thousand WPF elements, rebuilt whenever anything moved: a keystroke in
    /// the search box, a tick on one row, every mouse-move of a column drag. That is the freeze, and
    /// making the row cheaper does not fix a list that builds all of them.
    ///
    /// So the rows arrive as plain objects and become visuals only when scrolled to. The panel
    /// recycles containers, so scrolling a long board reuses a screenful rather than making new
    /// ones.
    ///
    /// A subclass rather than a data template, because every row on this board is built in code —
    /// its cells, its menu, its tick, the colours it takes from the skin — and a template would mean
    /// writing all of it twice, in two languages, and keeping them in step. Overriding how a
    /// container is prepared keeps the one builder and still gets the virtualisation.
    /// </summary>
    public sealed class BoardList : ItemsControl
    {
        private readonly Dictionary<object, ContentControl> _live =
            new Dictionary<object, ContentControl>();

        /// <summary>Make one.</summary>
        /// <param name="build">Turns one item into the row that shows it.</param>
        public BoardList(Func<object, UIElement?> build)
        {
            Build = build ?? throw new ArgumentNullException(nameof(build));

            // The list takes the keyboard, not the rows. Row containers are recycled - the
            // container holding row four hundred a moment ago is holding row twelve now - so
            // focus parked on one would follow the scroll rather than the reader. There is no
            // focus ring: the read row is already marked, and a second outline around the whole
            // table would say the table was the thing selected.
            Focusable = true;
            FocusVisualStyle = null;

            // The three that make it virtualise: a panel that only realises what is visible,
            // recycling so scrolling reuses containers, and item scrolling so the panel knows
            // what a unit of scrolling is.
            var panel = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
            ItemsPanel = new ItemsPanelTemplate(panel);

            VirtualizingPanel.SetIsVirtualizing(this, true);
            VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);

            // A row is not a uniform height — a band header is taller than a result — so the
            // panel is told to measure rather than assume.
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(this, true);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.PaddingProperty, new Thickness(0));
            scroll.SetValue(ScrollViewer.FocusableProperty, false);

            var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(presenter);

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            border.AppendChild(scroll);

            Template = new ControlTemplate(typeof(BoardList)) { VisualTree = border };
        }

        /// <summary>Turns one item into the row that shows it.</summary>
        public Func<object, UIElement?> Build { get; }

        /// <summary>
        /// Draw one row again, leaving every other row alone.
        ///
        /// For a change that is about one row and nothing else — a tick, a status written on it.
        /// Rebuilding the list for that is what this class exists to stop.
        /// </summary>
        /// <param name="item">The item whose row changed.</param>
        public void Redraw(object? item)
        {
            if (item == null) return;

            if (_live.TryGetValue(item, out var container)) Fill(container, item);
        }

        /// <summary>
        /// Where the list is scrolled to, so a rebuild can put it back.
        ///
        /// Opening a clash group or folding a band changes how many lines there are, so the
        /// list has to be given a new set - and a new set starts at the top. That is the whole
        /// of "the table jumps to the top when I open a group": the offset was never wrong, it
        /// was thrown away. Held and restored around the swap, the rows under the pointer stay
        /// under the pointer.
        /// </summary>
        public double Offset
        {
            get => Scroller()?.VerticalOffset ?? 0;

            set
            {
                var scroll = Scroller();

                if (scroll == null || value <= 0) return;

                // Laid out, then scrolled, then laid out again — all before this returns.
                //
                // A list handed a new set of items has not been measured, so its ScrollableHeight
                // is still zero: the restore clamped the offset to zero, scrolled there, and the
                // deferred pass a moment later put it back. That is the table jumping to the top and
                // returning a second later; the restore was not losing the offset, it was scrolling
                // to the top with it in hand.
                //
                // Measuring first costs one layout of a screenful of rows, which is what
                // virtualising this list bought, and it means the scroll lands on a real height.
                // Laying out again after applies the offset in the same pass, so the first frame is
                // already in the right place.
                scroll.UpdateLayout();

                scroll.ScrollToVerticalOffset(Math.Min(value, Math.Max(0, scroll.ScrollableHeight)));

                scroll.UpdateLayout();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var to = Scroller();

                    if (to == null) return;

                    var wanted = Math.Min(value, Math.Max(0, to.ScrollableHeight));

                    if (Math.Abs(to.VerticalOffset - wanted) > 0.5)
                        to.ScrollToVerticalOffset(wanted);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// Scroll one row into view, realising it if the panel has not built it yet.
        ///
        /// For the keyboard: moving down off the bottom of the screen has to bring the next row
        /// on, and the row it moves to may not exist as a visual at all. A realised row is asked
        /// to bring itself into view; an unrealised one is reached by scrolling to roughly where
        /// it sits and then asking again once the panel has built that stretch.
        /// </summary>
        /// <param name="item">The item to show.</param>
        public void Reveal(object? item)
        {
            if (item == null) return;

            if (_live.TryGetValue(item, out var container))
            {
                container.BringIntoView();
                return;
            }

            var scroll = Scroller();
            var items = Items;

            if (scroll == null || items == null || items.Count == 0) return;

            var at = items.IndexOf(item);

            if (at < 0) return;

            scroll.UpdateLayout();

            scroll.ScrollToVerticalOffset(
                Math.Max(0, scroll.ScrollableHeight) * at / Math.Max(1, items.Count - 1));

            // The panel builds the rows for that stretch on the next layout pass, so the row
            // cannot be asked to show itself until after one.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_live.TryGetValue(item, out var built)) built.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Put the list back at the first row, for a rebuild that changed what it is showing.
        ///
        /// The counterpart to <see cref="Offset"/>: holding the place is the default, so the
        /// handful of rebuilds that are a different set of rows entirely have to ask for the top.
        /// </summary>
        public void Top() => Scroller()?.ScrollToVerticalOffset(0);

        /// <summary>The scroll viewer inside the template, once the template has been applied.</summary>
        private ScrollViewer? Scroller()
        {
            ApplyTemplate();

            if (VisualTreeHelper.GetChildrenCount(this) == 0) return null;

            var border = VisualTreeHelper.GetChild(this, 0) as Border;

            return border?.Child as ScrollViewer;
        }

        /// <inheritdoc />
        protected override DependencyObject GetContainerForItemOverride() =>
            new ContentControl
            {
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

        /// <inheritdoc />
        protected override bool IsItemItsOwnContainerOverride(object item) => false;

        /// <inheritdoc />
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);

            if (!(element is ContentControl container)) return;

            _live[item] = container;
            Fill(container, item);
        }

        /// <inheritdoc />
        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            // Recycling hands the same container to a different row, so the one being let go must
            // stop being the answer for the row that had it.
            if (item != null && _live.TryGetValue(item, out var held)
                && ReferenceEquals(held, element))
                _live.Remove(item);

            base.ClearContainerForItemOverride(element, item);
        }

        private void Fill(ContentControl container, object item)
        {
            try
            {
                container.Content = Build(item);
            }
            catch (Exception e)
            {
                // A row that will not build is one bad row, not a board that failed to open.
                container.Content = Ui.Problem("This row could not be drawn: " + Ui.Describe(e));
            }
        }
    }
}
