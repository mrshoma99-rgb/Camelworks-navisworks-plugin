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
        /// <summary>
        /// One value a condition can be set to, and what it reads as.
        ///
        /// The two are not always the same word. A clash result names the test it came from by
        /// the host's own id, and a picker offering a column of GUIDs is a picker nobody can use.
        /// </summary>
        internal sealed class Choice
        {
            /// <summary>Offer a value under its own name.</summary>
            /// <param name="value">What the condition is set to.</param>
            /// <param name="said">What the picker calls it, or null to use the value.</param>
            internal Choice(string value, string? said = null)
            {
                Value = value ?? string.Empty;
                Said = string.IsNullOrWhiteSpace(said) ? Value : said!;
            }

            /// <summary>What the condition is set to.</summary>
            internal string Value { get; }

            /// <summary>What the picker calls it.</summary>
            internal string Said { get; }
        }

        /// <summary>What the field picker calls the entry that asks for a property by name.</summary>
        private const string ByProperty = "A property, by name...";

        /// <summary>How far up the tree the property picker offers to look.</summary>
        private static readonly int[] Ups = { 0, 1, 2, 3, 5, 10 };

        private readonly ResultQuery _root;
        private readonly IReadOnlyList<QueryField> _fields;
        private readonly Action _onChanged;
        private readonly Func<string, IReadOnlyList<Choice>>? _offers;

        /// <summary>
        /// The conditions and brackets drawn as one line rather than as their controls.
        ///
        /// Held by the editor rather than saved with the search: it is what the reader is looking
        /// at, not what the rule finds, and a project file that carried it would make two people
        /// open the same rule and disagree about it. Kept on the node itself, so a condition stays
        /// as it was left while the search is redrawn around it.
        /// </summary>
        private readonly HashSet<ResultQuery> _shut = new HashSet<ResultQuery>();

        /// <summary>Build an editor over one search.</summary>
        /// <param name="root">The search, edited in place.</param>
        /// <param name="check">The check whose results are searched, for the columns it declares.</param>
        /// <param name="onChanged">Called after every edit.</param>
        /// <param name="offers">
        /// The values worth offering for a field, by field id, or null to type every value.
        ///
        /// A status, a priority, a party and a test all come from a list the project already
        /// holds, and typing one by hand is how a rule ends up matching nothing because the word
        /// was spelt differently. Only the whole-value operators use it: "contains" is a
        /// fragment, and a picker of whole values is the wrong control for one.
        /// </param>
        internal SearchEditor(ResultQuery root, ICheck? check, Action onChanged,
                              Func<string, IReadOnlyList<Choice>>? offers = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            _offers = offers;
            _fields = ResultFields.For(check);

            View = Ui.Stack();

            // Everything already written arrives shut, so a search of nine conditions reads as
            // nine lines. What somebody adds opens, because they are about to fill it in.
            Shut(_root);

            Redraw();
        }

        /// <summary>The conditions, and the ways to add one.</summary>
        internal StackPanel View { get; }

        /// <summary>Draw the whole search again.</summary>
        private void Redraw()
        {
            View.Children.Clear();
            View.Children.Add(Group(_root, top: true, first: true));
        }

        /// <summary>Draw everything inside a group as one line.</summary>
        /// <param name="group">The group.</param>
        private void Shut(ResultQuery group)
        {
            foreach (var one in group.Inside)
            {
                _shut.Add(one);

                if (!one.IsTerm) Shut(one);
            }
        }

        /// <summary>The symbol that opens a condition or a bracket, and shuts it again.</summary>
        /// <param name="node">What it opens.</param>
        private UIElement Chevron(ResultQuery node)
        {
            var shut = _shut.Contains(node);

            return Ui.Icon(shut ? Glyphs.Right : Glyphs.Down,
                           shut ? "Open this to change it" : "Close this",
                           () =>
                           {
                               if (!_shut.Remove(node)) _shut.Add(node);

                               Redraw();
                           });
        }

        /// <summary>
        /// One condition as a sentence, for when it is shut.
        ///
        /// Everything the controls hold, in the order they hold it, so shutting a condition loses
        /// nothing but the ability to change it.
        /// </summary>
        /// <param name="term">The condition.</param>
        private string Said(QueryTerm term)
        {
            var known = _fields.FirstOrDefault(one =>
                string.Equals(one.Id, term.Field, StringComparison.Ordinal));

            var named = ResultFields.ReadProperty(term.Field, out var category, out var name)
                ? (category.Length == 0 ? name : category + " / " + name)
                  + (term.Ancestors > 0 ? " or above" : string.Empty)
                : known?.Title ?? Strange(term.Field);

            var where = term.Where == QueryWhere.Result
                ? string.Empty
                : ResultFields.Worded(term.Where) + ": ";

            var said = where + named + " " + ResultFields.Worded(term.Op);

            if (QueryTerm.Bare(term.Op)) return said;

            if (!QueryTerm.Numeric(term.Op))
                return said + " \u201c" + (term.Text ?? string.Empty) + "\u201d";

            said += " " + Said(term.Number);

            if (term.Op == QueryOp.Between) said += " and " + Said(term.Number2);

            return said + (string.IsNullOrEmpty(known?.Unit) ? string.Empty : " " + known!.Unit);
        }

        /// <summary>Say something changed, and draw it again.</summary>
        private void Touched()
        {
            Redraw();
            _onChanged();
        }

        /// <summary>
        /// One group: its conditions, each carrying how it joins to the one above it.
        ///
        /// The outermost group has no bar and no border — it is the search itself. A group inside
        /// it is a bracket, drawn as a block with its own connector down the left, which is what
        /// says how the whole bracket joins to what is above it.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="top">True for the outermost group.</param>
        /// <param name="first">True when nothing is drawn above it, so it has no connector.</param>
        private UIElement Group(ResultQuery group, bool top, bool first)
        {
            var held = Ui.Stack();
            var shut = !top && _shut.Contains(group);

            if (shut)
            {
                var head = new WrapPanel();

                head.Children.Add(Spaced(Chevron(group)));

                var line = Ui.Line(Counted(group), 1);

                line.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(Spaced(line));

                held.Children.Add(head);
            }
            else
            {
                if (!top) held.Children.Add(Chevroned(group));

                for (var at = 0; at < group.Inside.Count; at++)
                {
                    var one = group.Inside[at];

                    held.Children.Add(one.IsTerm
                        ? Clause(group, one, at == 0)
                        : Group(one, top: false, first: at == 0));
                }

                if (group.Inside.Count == 0)
                    held.Children.Add(Ui.Small("No conditions, so this finds every result."));

                held.Children.Add(Adding(group));
            }

            if (top) return held;

            var box = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = held,
            };

            box.Tint(Border.BorderBrushProperty, "Bd");
            box.Tint(Border.BackgroundProperty, "Card");

            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };

            // The bar down the left of a bracket, which is where its own connector lives. A
            // bracket joined to what is above it by "or" says so once, beside the whole block,
            // rather than on the first condition inside it.
            var bar = first
                ? (UIElement)new Border { Width = 4 }
                : Connector(group, tall: true);

            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);

            var drop = Ui.Icon(Glyphs.Bin, "Remove this bracket", () =>
            {
                Parent(_root, group)?.Inside.Remove(group);
                Touched();
            });

            drop.VerticalAlignment = VerticalAlignment.Top;
            drop.Margin = new Thickness(4, 8, 0, 0);

            DockPanel.SetDock(drop, Dock.Right);
            row.Children.Add(drop);

            row.Children.Add(box);

            return row;
        }

        /// <summary>The line at the top of an open bracket, which is how it is shut again.</summary>
        /// <param name="group">The bracket.</param>
        private UIElement Chevroned(ResultQuery group)
        {
            var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

            head.Children.Add(Spaced(Chevron(group)));

            var line = Ui.Small("Bracket");

            line.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(Spaced(line));

            return head;
        }

        /// <summary>What a shut bracket says it holds.</summary>
        /// <param name="group">The bracket.</param>
        private static string Counted(ResultQuery group) =>
            "Bracket \u2014 " + Ui.Count(group.Clauses, "condition")
            + (group.Negate ? ", inverted" : string.Empty);

        /// <summary>The two ways to add to a group.</summary>
        /// <param name="group">The group.</param>
        private UIElement Adding(ResultQuery group)
        {
            var add = Ui.Actions(
                Ui.Button("Add condition", () =>
                {
                    group.Inside.Add(new ResultQuery(
                        new QueryTerm(ResultFields.Status, QueryOp.Is)));

                    Touched();
                }),
                Ui.Quiet("Add bracket", () =>
                {
                    group.Inside.Add(new ResultQuery());
                    Touched();
                }));

            add.Margin = new Thickness(0, 4, 0, 0);

            return add;
        }

        /// <summary>
        /// The chip that says how something joins to what is above it.
        ///
        /// Coloured and pressed rather than a dropdown: there are two of them, they are read down
        /// a column at a glance, and a dropdown per row would be a column of chevrons saying
        /// nothing until each is opened.
        /// </summary>
        /// <param name="node">What the connector belongs to.</param>
        /// <param name="tall">True for a bracket's bar, which runs the height of the block.</param>
        private UIElement Connector(ResultQuery node, bool tall)
        {
            var either = node.Joiner == QueryJoin.Any;

            var press = new System.Windows.Controls.Button
            {
                Content = either ? "or" : "and",
                Padding = new Thickness(6, 2, 6, 2),
                MinWidth = tall ? 26 : 44,
                VerticalAlignment = tall ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = either
                    ? "Either this or what is above it. Press to require both."
                    : "Both this and what is above it. Press to accept either.",
            };

            // The skin has no chip style. QuietBtn draws its own ground and ignores a Background
            // set on it, so the two read as the same grey box; Btn honours it, which is what makes
            // "and" and "or" tell apart down a column without reading each word.
            press.Wearing("Btn");

            press.Tint(System.Windows.Controls.Control.BackgroundProperty,
                       either ? "AccentWash" : "BlueBg");

            press.Tint(System.Windows.Controls.Control.BorderBrushProperty,
                       either ? "Accent" : "BlueBd");

            press.Tint(System.Windows.Controls.Control.ForegroundProperty,
                       either ? "Text" : "BlueTx");

            press.BorderThickness = new Thickness(1);

            press.Click += (s, e) => Ui.Safely(() =>
            {
                node.Joiner = either ? QueryJoin.All : QueryJoin.Any;
                Touched();
            });

            return press;
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
        /// <param name="first">True when nothing is drawn above it, so it has no connector.</param>
        private UIElement Clause(ResultQuery group, ResultQuery node, bool first)
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

            // How it joins to the condition above it. Not on the first: there is nothing above it
            // to join to, and a chip there would invite somebody to set one.
            if (!first) row.Children.Add(Connector(node, tall: false));

            row.Children.Add(Spaced(Chevron(node)));

            if (_shut.Contains(node))
            {
                var line = Ui.Line(Said(term), 1);

                line.VerticalAlignment = VerticalAlignment.Center;
                line.TextWrapping = TextWrapping.Wrap;
                line.MaxWidth = 380;

                row.Children.Add(Spaced(line));
                row.Children.Add(Spaced(Dropping(group, node, "Remove this condition")));

                return row;
            }

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

                        term.Where = chosen.Across ? QueryWhere.BetweenElements
                            : chosen.OnElement ? QueryWhere.AnyElement
                            : QueryWhere.Result;
                    }

                    // The operator has to be one the new field takes: a text field cannot be
                    // "between", and a number field cannot "start with".
                    var offered = ResultFields.Operators(
                        property ? null : _fields.FirstOrDefault(one => one.Id == term.Field),
                        QueryValue.Text, term.Where);

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
            // Which element, for the fields that are read from one. A measurement across the
            // pair offers the one scope it has, rather than a picker with a single entry that
            // cannot be changed.
            var scopes = ResultFields.ScopesFor(known);

            if (elemental && scopes.Count > 1)
                row.Children.Add(Spaced(Ui.Choice(
                    scopes.Select(ResultFields.Worded).ToList(),
                    ResultFields.Worded(term.Where == QueryWhere.Result
                                        ? QueryWhere.AnyElement : term.Where),
                    picked =>
                    {
                        term.Where = scopes.First(one => ResultFields.Worded(one) == picked);

                        var offered = ResultFields.Operators(known, kind, term.Where);

                        if (!offered.Contains(term.Op)) term.Op = offered[0];

                        Touched();
                    }, 150)));
            else if (elemental)
                row.Children.Add(Spaced(Ui.Line(ResultFields.Worded(scopes[0]), 0.7)));

            // How it is compared.
            var operators = ResultFields.Operators(known, kind, term.Where);

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
                        typed => { term.Number = Read(typed); _onChanged(); }, Ui.Wide.Number)));

                    if (term.Op == QueryOp.Between)
                    {
                        row.Children.Add(Spaced(Ui.Line("and", 0.7)));

                        row.Children.Add(Spaced(Ui.Text(Said(term.Number2),
                            typed => { term.Number2 = Read(typed); _onChanged(); }, Ui.Wide.Number)));
                    }

                    var unit = known?.Unit;

                    if (!string.IsNullOrEmpty(unit))
                        row.Children.Add(Spaced(Ui.Line(unit!, 0.7)));
                }
                else
                {
                    var offered = Offered(term);

                    if (offered.Count > 0)
                        row.Children.Add(Spaced(Ui.Choice(
                            offered.Select(one => one.Said).ToList(),
                            Named(offered, term.Text),
                            picked =>
                            {
                                term.Text = offered.First(one => one.Said == picked).Value;
                                Touched();
                            }, Ui.Wide.Name)));
                    else
                        row.Children.Add(Spaced(Ui.Text(term.Text, "Value",
                            typed => { term.Text = typed; _onChanged(); }, Ui.Wide.Word)));
                }
            }

            row.Children.Add(Spaced(Dropping(group, node, "Remove this condition")));

            // What the field means, where it needs saying. A measurement taken off the elements
            // rather than read off a column has limits, and they belong beside the condition
            // rather than in a manual.
            if (string.IsNullOrEmpty(known?.Hint)) return row;

            var says = Ui.Small(known!.Hint!);

            says.Margin = new Thickness(30, 0, 0, 6);
            says.MaxWidth = 460;

            return Ui.Stack(row, says);
        }

        /// <summary>
        /// The values a condition offers instead of a box to type in, or nothing.
        ///
        /// Only for the whole-value operators, and only when the caller supplied a list for the
        /// field. Whatever the condition already says is kept in the list whether the project
        /// still offers it or not: a rule written last month against a party who has left must
        /// not silently become a rule about somebody else.
        /// </summary>
        /// <param name="term">The condition.</param>
        private IReadOnlyList<Choice> Offered(QueryTerm term)
        {
            if (_offers == null) return Array.Empty<Choice>();
            if (term.Op != QueryOp.Is && term.Op != QueryOp.IsNot) return Array.Empty<Choice>();

            var found = _offers(term.Field) ?? Array.Empty<Choice>();

            if (found.Count == 0) return found;

            var said = term.Text;

            if (string.IsNullOrEmpty(said)
                || found.Any(one => string.Equals(one.Value, said, StringComparison.OrdinalIgnoreCase)))
                return found;

            var kept = new List<Choice> { new Choice(said!) };

            kept.AddRange(found);

            return kept;
        }

        /// <summary>What the picker shows for whatever the condition is set to.</summary>
        /// <param name="offered">The values on offer, which always include the one held.</param>
        /// <param name="value">What the condition is set to.</param>
        private static string Named(IReadOnlyList<Choice> offered, string? value) =>
            offered.FirstOrDefault(one =>
                string.Equals(one.Value, value, StringComparison.OrdinalIgnoreCase))?.Said
            ?? offered[0].Said;

        /// <summary>Take a condition or a bracket out of its group.</summary>
        /// <param name="group">The group it is in.</param>
        /// <param name="node">What to remove.</param>
        /// <param name="tip">What the symbol says it does.</param>
        private UIElement Dropping(ResultQuery group, ResultQuery node, string tip) =>
            Ui.Icon(Glyphs.Bin, tip, () =>
            {
                group.Inside.Remove(node);
                _shut.Remove(node);

                Touched();
            });

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
