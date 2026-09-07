using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Search;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Writing a search: stacked conditions, joined all or any, with brackets.
    ///
    /// The shape a coordinator already knows from a search set, and the reason a rule can find
    /// anything rather than the thirteen things a fixed list of kinds allowed. A condition names a
    /// field, how it is compared, and what it is compared with; a bracket holds conditions of its
    /// own and joins them the other way.
    ///
    /// What a field can be is not written here. The standing result fields, whatever columns the
    /// check whose results are being searched declares, and any property an element carries all
    /// arrive from <see cref="ResultFields.For"/>, so a check that starts measuring something new
    /// is searchable on it without this file changing.
    ///
    /// Every change writes straight into the query it was given and then calls back. There is no
    /// apply: the search is the thing on screen.
    /// </summary>
    internal sealed class SearchEditor
    {
        /// <summary>What the field picker calls the entry that asks for a property by name.</summary>
        private const string ByProperty = "A property, by name...";

        /// <summary>How far up the tree the property picker offers to look.</summary>
        private static readonly int[] Ups = { 0, 1, 2, 3, 5, 10 };

        private readonly ResultQuery _root;
        private readonly IReadOnlyList<QueryField> _fields;
        private readonly Action _onChanged;

        /// <summary>Build an editor over one search.</summary>
        /// <param name="root">The search, edited in place.</param>
        /// <param name="check">The check whose results are searched, for the columns it declares.</param>
        /// <param name="onChanged">Called after every edit.</param>
        internal SearchEditor(ResultQuery root, ICheck? check, Action onChanged)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            _fields = ResultFields.For(check);

            View = Ui.Stack();

            Redraw();
        }

        /// <summary>The conditions, and the ways to add one.</summary>
        internal StackPanel View { get; }

        /// <summary>Draw the whole search again.</summary>
        private void Redraw()
        {
            View.Children.Clear();
            View.Children.Add(Group(_root, top: true));
        }

        /// <summary>Say something changed, and draw it again.</summary>
        private void Touched()
        {
            Redraw();
            _onChanged();
        }

        /// <summary>
        /// One group: how it joins, its conditions, and the two ways to add to it.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="top">True for the outermost group, which has no border and cannot be removed.</param>
        private UIElement Group(ResultQuery group, bool top)
        {
            var held = Ui.Stack();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            var join = Ui.Choice(new[] { "Match all of these", "Match any of these" },
                group.Join == QueryJoin.Any ? "Match any of these" : "Match all of these",
                picked =>
                {
                    group.Join = picked == "Match any of these" ? QueryJoin.Any : QueryJoin.All;
                    Touched();
                }, 170);

            var flip = Ui.Check("Invert", group.Negate, on => { group.Negate = on; Touched(); });

            flip.VerticalAlignment = VerticalAlignment.Center;
            flip.Margin = new Thickness(8, 0, 0, 0);
            flip.ToolTip = "Find the results this group does not match";

            DockPanel.SetDock(join, Dock.Left);
            head.Children.Add(join);

            if (!top)
            {
                var drop = Ui.Icon(Glyphs.Bin, "Remove this bracket", () =>
                {
                    Parent(_root, group)?.Inside.Remove(group);
                    Touched();
                });

                DockPanel.SetDock(drop, Dock.Right);
                head.Children.Add(drop);
            }

            head.Children.Add(Ui.Loose(flip));
            held.Children.Add(head);

            foreach (var one in group.Inside.ToList())
                held.Children.Add(one.IsTerm ? Clause(group, one) : Group(one, top: false));

            if (group.Inside.Count == 0)
                held.Children.Add(Ui.Small("No conditions, so this finds every result."));

            var add = Ui.Actions(
                Ui.Button("Add condition", () =>
                {
                    group.Inside.Add(new ResultQuery(
                        new QueryTerm(ResultFields.Status, QueryOp.Is)));

                    Touched();
                }),
                Ui.Quiet("Add bracket", () =>
                {
                    group.Inside.Add(new ResultQuery(
                        group.Join == QueryJoin.All ? QueryJoin.Any : QueryJoin.All));

                    Touched();
                }));

            add.Margin = new Thickness(0, 4, 0, 0);
            held.Children.Add(add);

            if (top) return held;

            var box = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(12, 4, 0, 6),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = held,
            };

            box.Tint(Border.BorderBrushProperty, "Bd");
            box.Tint(Border.BackgroundProperty, "Card");

            return box;
        }

        /// <summary>The group one node sits in, or null when it is the root.</summary>
        /// <param name="within">Where to look.</param>
        /// <param name="node">The node.</param>
        private static ResultQuery? Parent(ResultQuery within, ResultQuery node)
        {
            if (within.Inside.Contains(node)) return within;

            foreach (var one in within.Inside.Where(one => !one.IsTerm))
            {
                var found = Parent(one, node);

                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// One condition: what to read, where to read it, how to compare it, and what with.
        ///
        /// Wrapped rather than laid out in a fixed row, because five controls do not fit across a
        /// panel and the widths of the middle two follow what the first one is set to.
        /// </summary>
        /// <param name="group">The group it belongs to, so it can remove itself.</param>
        /// <param name="node">The condition.</param>
        private UIElement Clause(ResultQuery group, ResultQuery node)
        {
            var term = node.Term!;
            var property = ResultFields.ReadProperty(term.Field, out var category, out var name);

            var known = _fields.FirstOrDefault(one =>
                string.Equals(one.Id, term.Field, StringComparison.Ordinal));

            var kind = property
                ? QueryValue.Text
                : known?.Value ?? QueryValue.Text;

            var elemental = property || (known?.OnElement ?? false);

            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

            // What to read.
            var titles = _fields.Select(one => one.Title).ToList();

            titles.Add(ByProperty);

            // A field this check does not offer is named as itself rather than shown as the first
            // entry in the list. A condition written against another check's column, or against a
            // check that is not on this seat, must not read as a condition about something else.
            var stranger = !property && known == null ? Strange(term.Field) : null;

            if (stranger != null) titles.Insert(0, stranger);

            row.Children.Add(Spaced(Ui.Choice(titles,
                stranger ?? (property ? ByProperty : known!.Title),
                picked =>
                {
                    if (picked == stranger) return;

                    if (picked == ByProperty)
                    {
                        term.Field = ResultFields.Property(string.Empty, name);
                        term.Where = QueryWhere.AnyElement;
                    }
                    else
                    {
                        var chosen = _fields.First(one =>
                            string.Equals(one.Title, picked, StringComparison.Ordinal));

                        term.Field = chosen.Id;
                        term.Where = chosen.OnElement ? QueryWhere.AnyElement : QueryWhere.Result;
                    }

                    // The operator has to be one the new field takes: a text field cannot be
                    // "between", and a number field cannot "start with".
                    var offered = ResultFields.Operators(
                        property ? QueryValue.Text
                                 : _fields.FirstOrDefault(one => one.Id == term.Field)?.Value ?? QueryValue.Text,
                        term.Where == QueryWhere.BetweenElements);

                    if (!offered.Contains(term.Op)) term.Op = offered[0];

                    Touched();
                }, 190)));

            if (property)
            {
                row.Children.Add(Spaced(Ui.Text(category, "Category (optional)",
                    typed =>
                    {
                        term.Field = ResultFields.Property(typed.Trim(), name);
                        _onChanged();
                    }, 130)));

                row.Children.Add(Spaced(Ui.Text(name, "Property name",
                    typed =>
                    {
                        term.Field = ResultFields.Property(category, typed.Trim());
                        _onChanged();
                    }, 130)));

                row.Children.Add(Spaced(Ui.Choice(
                    Ups.Select(Upward).ToList(), Upward(Nearest(term.Ancestors)),
                    picked =>
                    {
                        term.Ancestors = Ups.FirstOrDefault(one => Upward(one) == picked);
                        _onChanged();
                    }, 150)));
            }

            // Which element, for the fields that are read from one.
            if (elemental)
                row.Children.Add(Spaced(Ui.Choice(
                    ResultFields.Scopes.Select(ResultFields.Worded).ToList(),
                    ResultFields.Worded(term.Where == QueryWhere.Result
                                        ? QueryWhere.AnyElement : term.Where),
                    picked =>
                    {
                        term.Where = ResultFields.Scopes.First(one =>
                            ResultFields.Worded(one) == picked);

                        var offered = ResultFields.Operators(kind, term.IsBetween);

                        if (!offered.Contains(term.Op)) term.Op = offered[0];

                        Touched();
                    }, 150)));

            // How it is compared.
            var operators = ResultFields.Operators(kind, term.IsBetween);

            row.Children.Add(Spaced(Ui.Choice(
                operators.Select(ResultFields.Worded).ToList(),
                ResultFields.Worded(operators.Contains(term.Op) ? term.Op : operators[0]),
                picked =>
                {
                    term.Op = operators.First(one => ResultFields.Worded(one) == picked);
                    Touched();
                }, 140)));

            // What it is compared with.
            if (!QueryTerm.Bare(term.Op))
            {
                if (QueryTerm.Numeric(term.Op))
                {
                    row.Children.Add(Spaced(Ui.Text(Said(term.Number),
                        typed => { term.Number = Read(typed); _onChanged(); }, 90)));

                    if (term.Op == QueryOp.Between)
                    {
                        row.Children.Add(Spaced(Ui.Line("and", 0.7)));

                        row.Children.Add(Spaced(Ui.Text(Said(term.Number2),
                            typed => { term.Number2 = Read(typed); _onChanged(); }, 90)));
                    }

                    var unit = known?.Unit;

                    if (!string.IsNullOrEmpty(unit))
                        row.Children.Add(Spaced(Ui.Line(unit!, 0.7)));
                }
                else
                {
                    row.Children.Add(Spaced(Ui.Text(term.Text, "Value",
                        typed => { term.Text = typed; _onChanged(); }, 160)));
                }
            }

            var remove = Ui.Icon(Glyphs.Bin, "Remove this condition", () =>
            {
                group.Inside.Remove(node);
                Touched();
            });

            row.Children.Add(Spaced(remove));

            return row;
        }

        /// <summary>
        /// A field this check does not declare, named as itself.
        ///
        /// It is still run: the search is over findings and one list can hold conditions about
        /// results of several checks, so a condition about a column only one of them declares
        /// simply finds nothing among the others.
        /// </summary>
        /// <param name="field">The field id.</param>
        private static string Strange(string field)
        {
            var column = ResultFields.ReadColumn(field);

            return column != null
                ? column + " (another check's)"
                : field + " (not offered here)";
        }

        /// <summary>The nearest depth the picker offers to one held.</summary>
        /// <param name="up">What the condition holds.</param>
        private static int Nearest(int up) =>
            Ups.Contains(up) ? up : Ups.OrderBy(one => Math.Abs(one - up)).First();

        /// <summary>How far up the tree to look, worded.</summary>
        /// <param name="up">The depth.</param>
        private static string Upward(int up) =>
            up == 0
                ? "On the element itself"
                : "Or up to " + up.ToString(CultureInfo.InvariantCulture) + " above";

        /// <summary>A number as a box shows it.</summary>
        /// <param name="value">The number.</param>
        private static string Said(double value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>A number out of a box, or nought.</summary>
        /// <param name="typed">What was typed.</param>
        private static double Read(string? typed) =>
            double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;

        /// <summary>One control on the row, with the gap that separates it from the next.</summary>
        /// <param name="control">The control.</param>
        private static UIElement Spaced(UIElement control)
        {
            if (control is FrameworkElement frame)
            {
                frame.Margin = new Thickness(0, 0, 6, 4);
                frame.VerticalAlignment = VerticalAlignment.Center;
            }

            return control;
        }
    }
}
