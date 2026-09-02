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
    /// <b>Why this is here.</b> The clash board drew every result it was showing as a grid of a
    /// couple of dozen elements, all of them, into a stack panel — on screen or three thousand
    /// rows below it. A test with five thousand results is well over a hundred thousand WPF
    /// elements, built from scratch every time anything moved: a keystroke in the search box, a
    /// tick on one row, every mouse-move of a column drag. That is the freeze, and no amount of
    /// making the row cheaper fixes a list that builds all of them.
    ///
    /// So the rows arrive as plain objects and become visuals only when they are scrolled to.
    /// The panel recycles containers, so scrolling a long board reuses a screenful of them rather
    /// than making new ones.
    ///
    /// <b>Why a subclass and not a data template.</b> Every row on this board is built in code —
    /// its cells, its menu, its tick, the colours it takes from the skin — and a template would
    /// mean writing all of it twice, in two languages, and keeping them in step. Overriding how a
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

                // Twice, and the first one is what stops the flicker.
                //
                // Immediately, so a rebuild that has already been measured - the common case -
                // never renders a frame at the top. Then again after layout, because folding a
                // band away shortens the content and the offset has to be re-clamped against a
                // height that did not exist yet. Doing only the second gave a list that visibly
                // jumped to the top and back, which is what "it jumps up and down" was.
                scroll.ScrollToVerticalOffset(Math.Min(value, Math.Max(0, scroll.ScrollableHeight)));

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
