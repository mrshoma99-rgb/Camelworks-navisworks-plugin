using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The filter bar over a <see cref="ClashPick"/>: one chip per parameter, saying where it
    /// stands, and a menu holding every other parameter the results carry.
    ///
    /// One control, because there is one question. The Exchange tab asks it before it writes a
    /// pack and the Report tab asks it before it writes a document, and the Report tab had its
    /// own answer: three status tick boxes, three priority tick boxes and a four-way order
    /// picker, over the same results the Exchange bar filters on twenty-four parameters and
    /// every property the models carry. A second selector over one set of data is a second
    /// selector to keep correct, and the weaker one decides what a coordinator can send.
    ///
    /// Everything it edits lives in the pick, which is saved data, so a screen using this bar
    /// gets saved filters and a written description for nothing.
    /// </summary>
    public static class ClashPicking
    {
        /// <summary>The parameters a bar always shows, whether or not they are narrowing.</summary>
        private static readonly string[] Standing = { "status", "assignee", "storey", "level" };

        /// <summary>
        /// The bar.
        /// </summary>
        /// <param name="pick">The pick being edited. The bar writes its clauses.</param>
        /// <param name="items">The results in hand, for the parameters and values they carry.</param>
        /// <param name="plan">The project's storeys, so elevation reads as a level name.</param>
        /// <param name="issued">What has already gone out, or null when nothing has.</param>
        /// <param name="redraw">What to run when the pick changes.</param>
        /// <param name="says">The words in front of the chips.</param>
        public static UIElement Bar(ClashPick pick, IReadOnlyList<ClashItem> items,
                                    LevelPlan? plan, ClashIssueLog? issued, Action redraw,
                                    string says = "Only results where")
        {
            if (pick == null) throw new ArgumentNullException(nameof(pick));

            var held = items ?? Array.Empty<ClashItem>();
            var act = redraw ?? (() => { });

            var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };

            var lead = Ui.Small(says ?? string.Empty);

            lead.VerticalAlignment = VerticalAlignment.Center;
            lead.Margin = new Thickness(0, 2, 8, 2);

            bar.Children.Add(lead);

            var facets = new List<ClashFacet>();

            foreach (var key in Standing)
            {
                var found = ClashFacets.Of(key, held, plan, issued);

                if (found != null) facets.Add(found);
            }

            // Anything already filtered on, so a clause added from the menu gets a chip of its
            // own rather than disappearing.
            foreach (var clause in pick.Clauses)
            {
                if (facets.Any(one => Same(one.Key, clause.Facet))) continue;

                var found = ClashFacets.Of(clause.Facet, held, plan, issued);

                if (found != null) facets.Add(found);
            }

            foreach (var facet in facets)
                bar.Children.Add(Chip(pick, facet, held, act));

            // The rest of the parameters, counted and named. "and one more" said neither how many
            // there were nor what they were about.
            var rest = ClashFacets.Every(held, plan, issued)
                .Where(one => !facets.Any(shown => Same(shown.Key, one.Key)))
                .ToList();

            if (rest.Count > 0)
            {
                var add = Ui.Link("Add a filter ("
                                  + rest.Count.ToString(CultureInfo.InvariantCulture)
                                  + " more)", () => { });

                add.Click += (s, e) => Ui.Safely(() => Parts.PopMenu(add, Facets(pick, held, plan, issued, act)));
                add.Margin = new Thickness(4, 2, 0, 2);
                add.VerticalAlignment = VerticalAlignment.Center;
                add.ToolTip = "Filter on: "
                            + string.Join(", ", rest.Take(12).Select(one => one.Title))
                            + (rest.Count > 12 ? ", and more" : string.Empty) + ".";

                bar.Children.Add(add);
            }

            // What the filters are keeping out. A bar of chips says what is let through and never
            // how much of the board that leaves behind, which is the number a coordinator is
            // actually deciding on.
            var kept = pick.Take(held, plan, issued).Count;

            if (kept < held.Count)
            {
                var left = Ui.Small(Ui.Count(held.Count - kept, "result") + " left out");

                left.VerticalAlignment = VerticalAlignment.Center;
                left.Margin = new Thickness(10, 2, 0, 2);

                bar.Children.Add(left);
            }

            return bar;
        }

        /// <summary>
        /// The picker that says what the results are ordered by, and which way.
        ///
        /// The order the results are written in, which is not the order the table on screen
        /// happens to be sorted in. The direction only appears once there is an order to reverse:
        /// left in place it was a symbol permanently unavailable on the setting the screen opens
        /// on.
        /// </summary>
        /// <param name="pick">The pick being edited. This writes its sort and its direction.</param>
        /// <param name="items">The results in hand, for the parameters they carry.</param>
        /// <param name="plan">The project's storeys.</param>
        /// <param name="issued">What has already gone out, or null when nothing has.</param>
        /// <param name="redraw">What to run when the order changes.</param>
        /// <param name="asItCame">What the unordered choice is called on this screen.</param>
        public static UIElement Ordering(ClashPick pick, IReadOnlyList<ClashItem> items,
                                         LevelPlan? plan, ClashIssueLog? issued, Action redraw,
                                         string asItCame = "As on the board")
        {
            if (pick == null) throw new ArgumentNullException(nameof(pick));

            var held = items ?? Array.Empty<ClashItem>();
            var act = redraw ?? (() => { });

            var facets = ClashFacets.Every(held, plan, issued);

            var offered = new List<string> { asItCame };

            offered.AddRange(facets.Select(one => By + Lowered(one.Title)));

            var now = pick.Sort == null
                ? asItCame
                : By + Lowered(facets.FirstOrDefault(one => Same(one.Key, pick.Sort))?.Title
                               ?? pick.Sort!);

            if (!offered.Contains(now)) offered.Add(now);

            var box = Ui.Choice(offered, now, said =>
            {
                pick.Sort = string.Equals(said, asItCame, StringComparison.Ordinal)
                    ? null
                    : facets.FirstOrDefault(
                        one => string.Equals(By + Lowered(one.Title), said,
                                             StringComparison.OrdinalIgnoreCase))?.Key;

                act();
            }, 190);

            box.ToolTip = "The order the results are written in, which is not the order the table "
                        + "happens to be sorted in.";

            if (pick.Sort == null) return box;

            var way = Ui.Icon(pick.Descending ? Glyphs.Down : Glyphs.Up,
                              pick.Descending ? "Descending" : "Ascending",
                              () => Ui.Safely(() =>
                              {
                                  pick.Descending = !pick.Descending;
                                  act();
                              }));

            return Ui.Across(box, way);
        }

        /// <summary>What the order chooser puts before a parameter's name.</summary>
        private const string By = "By ";

        /// <summary>A parameter's title, as it reads inside a phrase.</summary>
        /// <param name="title">The title.</param>
        private static string Lowered(string title) =>
            string.IsNullOrEmpty(title)
                ? title
                : char.ToLowerInvariant(title[0]) + title.Substring(1);

        /// <summary>One parameter, as a chip that says which of its values are let through.</summary>
        /// <param name="pick">The pick being edited.</param>
        /// <param name="facet">The parameter.</param>
        /// <param name="items">The results in hand, for the values to offer.</param>
        /// <param name="redraw">What to run when the clause changes.</param>
        private static UIElement Chip(ClashPick pick, ClashFacet facet,
                                      IReadOnlyList<ClashItem> items, Action redraw)
        {
            var here = facet;

            var clause = pick.Clauses.FirstOrDefault(one => Same(one.Facet, here.Key));

            var button = Ui.Quiet(
                here.Title + ": " + (clause == null ? "any" : Values(clause)),
                () => Ui.Safely(() => Choose(pick, here, items, redraw)));

            button.Margin = new Thickness(0, 2, 6, 2);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.ToolTip = clause == null
                ? "Every value goes. Press to choose which."
                : clause.Describe(here) + ". Press to change it.";

            // A set filter is tinted, so the bar reads at a glance as which parameters are
            // narrowing what goes out and which are letting everything through.
            if (clause == null) return button;

            button.Tint(Control.ForegroundProperty, "Accent");
            button.Margin = new Thickness(0, 2, 0, 2);

            // A filter in force is a chip you can take off. Every one of them was a link that
            // reopened the value list, so clearing one meant opening it and unticking whatever
            // was in there.
            var off = Ui.Icon(Glyphs.Close, "Stop filtering on " + here.Title, () => Ui.Safely(() =>
            {
                pick.Clauses.Remove(clause);
                redraw();
            }));

            off.VerticalAlignment = VerticalAlignment.Center;
            off.Margin = new Thickness(0, 2, 6, 2);

            var chip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            chip.Children.Add(button);
            chip.Children.Add(off);

            return chip;
        }

        /// <summary>The values a clause names, short enough for a chip.</summary>
        /// <param name="clause">The clause.</param>
        private static string Values(ClashClause clause)
        {
            if (clause.Values.Count == 0) return "a range";

            var said = string.Join(", ", clause.Values.Take(2));

            return clause.Values.Count > 2
                ? said + " +" + (clause.Values.Count - 2).ToString(CultureInfo.InvariantCulture)
                : said;
        }

        /// <summary>Every parameter a filter can be about, as a menu.</summary>
        /// <param name="pick">The pick being edited.</param>
        /// <param name="items">The results in hand.</param>
        /// <param name="plan">The project's storeys.</param>
        /// <param name="issued">What has already gone out.</param>
        /// <param name="redraw">What to run when a clause is set.</param>
        private static IReadOnlyList<Parts.Entry> Facets(ClashPick pick,
                                                         IReadOnlyList<ClashItem> items,
                                                         LevelPlan? plan, ClashIssueLog? issued,
                                                         Action redraw)
        {
            var entries = new List<Parts.Entry> { Parts.Entry.Heading("Filter by") };

            foreach (var facet in ClashFacets.Every(items, plan, issued))
            {
                var here = facet;

                entries.Add(Parts.Entry.Act(here.Title, null,
                                            () => Choose(pick, here, items, redraw)));
            }

            return entries;
        }

        /// <summary>Ask which values of one parameter go in, from the ones the results hold.</summary>
        /// <param name="pick">The pick being edited.</param>
        /// <param name="facet">The parameter chosen.</param>
        /// <param name="items">The results in hand, for the values to offer.</param>
        /// <param name="redraw">What to run when the clause is set.</param>
        private static void Choose(ClashPick pick, ClashFacet facet,
                                   IReadOnlyList<ClashItem> items, Action redraw)
        {
            var held = ClashFacets.Values(facet, items);

            if (held.Count == 0)
            {
                Ui.Told("No result carries a value for " + facet.Title.ToLowerInvariant() + ".");

                return;
            }

            var standing = pick.Clauses.FirstOrDefault(one => Same(one.Facet, facet.Key));

            var box = Ui.Stack();

            var taken = new HashSet<string>(standing?.Values ?? Array.Empty<string>(),
                                            StringComparer.OrdinalIgnoreCase);

            foreach (var value in held.Take(60))
            {
                var here = value;

                box.Children.Add(Ui.Check(here, taken.Contains(here), on =>
                {
                    if (on) taken.Add(here);
                    else taken.Remove(here);
                }));
            }

            var page = new Ui.Page(facet.Title,
                () => Bench.Group(facet.Title + " values to include", box));

            Ui.Configure(facet.Title, () =>
            {
                // One clause per parameter. A second clause on the same one would have to hold as
                // well as the first, so choosing New and then Active would send nothing.
                if (standing != null) pick.Clauses.Remove(standing);

                if (taken.Count > 0)
                {
                    var clause = pick.Add(facet.Key);

                    foreach (var one in taken) clause.Values.Add(one);
                }

                redraw();
            }, page);
        }

        /// <summary>Whether two facet keys name the same parameter.</summary>
        /// <param name="a">One key.</param>
        /// <param name="b">The other.</param>
        private static bool Same(string? a, string? b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
