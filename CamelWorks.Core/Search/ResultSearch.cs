using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Search
{
    /// <summary>
    /// What a search needs besides the result itself.
    ///
    /// Two things: the elements, which only the open document can resolve, and the columns the
    /// check that produced the result declares. Behind a seam so Core stays framework-only and the
    /// search is testable without a host — the same reason every other engine here takes one.
    ///
    /// A seat with no document answers null for every element, and a clause that asks about one
    /// then finds nothing rather than throwing. That is the honest answer on a board read from a
    /// file: the elements are named in the finding, but the model they were in is not open.
    /// </summary>
    public interface IResultWorld
    {
        /// <summary>The element behind a key, or null when it cannot be resolved here.</summary>
        /// <param name="key">The key.</param>
        IModelItem? Element(ElementKey key);

        /// <summary>The columns declared by the check that produced a result.</summary>
        /// <param name="finding">The result.</param>
        IReadOnlyList<ResultColumn> Columns(Finding finding);

        /// <summary>
        /// The issue a result is attached to, or null when nothing has been raised for it.
        ///
        /// Here rather than on the finding, because attachment is the issue's fact: an issue names
        /// the findings it is about, and a finding does not name its issue. "Group the results
        /// nobody has raised yet" is the question this answers.
        /// </summary>
        /// <param name="finding">The result.</param>
        string? IssueOf(Finding finding);
    }

    /// <summary>
    /// Running a search over results.
    ///
    /// Typed on <see cref="Finding"/> rather than on a clash item, which is what makes one search
    /// language serve every check: a result is a status, some facts, some elements and whatever its
    /// check declared it measured, and that is as true of a clearance shortfall and a fall hazard
    /// as it is of a clash.
    /// </summary>
    public static class ResultSearch
    {
        /// <summary>
        /// What a list of readings is joined with before the text operators see it.
        ///
        /// A unit separator, because no property value contains one. Labels and runs are lists and
        /// a clause about one asks whether any entry answers; joined with a space, "is Fire" would
        /// match a result labelled "Fire door".
        /// </summary>
        private const char Apart = '\u001F';

        /// <summary>The results a search finds, in the order they were given.</summary>
        /// <param name="query">The search. Null or empty finds everything.</param>
        /// <param name="findings">What to search.</param>
        /// <param name="world">Where elements and columns are read from.</param>
        public static IReadOnlyList<Finding> Run(ResultQuery? query, IEnumerable<Finding>? findings,
                                                 IResultWorld world) =>
            (findings ?? Array.Empty<Finding>())
                .Where(one => one != null && Matches(query, one, world))
                .ToList();

        /// <summary>Whether one result answers a search.</summary>
        /// <param name="query">The search. Null or empty matches.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">Where elements and columns are read from.</param>
        public static bool Matches(ResultQuery? query, Finding? finding, IResultWorld world)
        {
            if (finding == null) return false;
            if (query == null) return true;
            if (world == null) throw new ArgumentNullException(nameof(world));

            return Answered(query, finding, world);
        }

        /// <summary>Whether one node answers, with its negation applied.</summary>
        /// <param name="node">The clause or group.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static bool Answered(ResultQuery node, Finding finding, IResultWorld world)
        {
            var said = node.IsTerm
                ? Asked(node.Term!, finding, world)
                : Joined(node, finding, world);

            return node.Negate ? !said : said;
        }

        /// <summary>Whether a group answers, before its negation.</summary>
        /// <param name="group">The group.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static bool Joined(ResultQuery group, Finding finding, IResultWorld world)
        {
            // An empty group matches. A rule is written by adding conditions to it, and one that
            // found nothing until the first condition was finished would report nought found for
            // every half-written rule.
            if (group.Inside.Count == 0) return true;

            // Left to right, in the order they are drawn, each condition joined the way its own
            // connector says. No operator precedence: a bracket is how precedence is written, and
            // a bracket is a group somebody can see. Reading "A or B and C" as "A or (B and C)"
            // would make the screen and the answer disagree.
            var said = Answered(group.Inside[0], finding, world);

            for (var at = 1; at < group.Inside.Count; at++)
            {
                var one = group.Inside[at];

                // Short-circuited: an "or" after something that already holds, and an "and" after
                // something that already does not, cannot change the answer, and evaluating them
                // would read the model for nothing.
                if (one.Joiner == QueryJoin.Any)
                {
                    if (!said) said = Answered(one, finding, world);
                }
                else if (said)
                {
                    said = Answered(one, finding, world);
                }
            }

            return said;
        }

        /// <summary>Whether one clause answers.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static bool Asked(QueryTerm term, Finding finding, IResultWorld world)
        {
            switch (term.Where)
            {
                case QueryWhere.Result:
                    return Compared(term, Result(term, finding, world));

                case QueryWhere.BetweenElements:
                    return Across(term, finding, world);

                default:
                    return Sided(term, finding, world);
            }
        }

        /// <summary>Whether the elements a scope names answer.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static bool Sided(QueryTerm term, Finding finding, IResultWorld world)
        {
            // Only the elements that resolve. An element the open document cannot find is not an
            // element that answers "no": the negative operators would then hold for every result
            // on a board read from a file, where nothing resolves at all.
            var items = Sides(term.Where, finding)
                .Select(world.Element)
                .Where(item => item != null)
                .ToList();

            if (items.Count == 0) return false;

            var read = items.Select(item => OnItem(term, item)).ToList();

            return term.Where == QueryWhere.EveryElement
                ? read.All(value => Compared(term, value))
                : read.Any(value => Compared(term, value));
        }

        /// <summary>Whether the sides read the same, or differently.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static bool Across(QueryTerm term, Finding finding, IResultWorld world)
        {
            var items = finding.Elements
                .Select(world.Element)
                .Where(item => item != null)
                .ToList();

            // A comparison needs two things to compare. One element that resolves, or none, is
            // not a match either way round — including for "differs between them", which would
            // otherwise hold for every result whose model is not open.
            if (items.Count < 2) return false;

            var read = items.Select(item => OnItem(term, item)).ToList();

            var first = read[0];
            var same = read.Skip(1).All(value => Alike(first, value));

            return term.Op == QueryOp.Different ? !same : same;
        }

        /// <summary>The element keys one scope names.</summary>
        /// <param name="where">The scope.</param>
        /// <param name="finding">The result.</param>
        private static IEnumerable<ElementKey> Sides(QueryWhere where, Finding finding)
        {
            switch (where)
            {
                case QueryWhere.FirstElement:
                    return finding.Elements.Take(1);

                case QueryWhere.SecondElement:
                    return finding.Elements.Skip(1).Take(1);

                default:
                    return finding.Elements;
            }
        }

        /// <summary>Whether two readings are the same, counting absent as its own answer.</summary>
        /// <param name="a">One reading.</param>
        /// <param name="b">The other.</param>
        private static bool Alike(string? a, string? b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // ---- Reading a value -----------------------------------------------------------------

        /// <summary>What a clause reads off the result itself.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static string? Result(QueryTerm term, Finding finding, IResultWorld world)
        {
            var column = ResultFields.ReadColumn(term.Field);

            if (column != null) return Declared(column, finding, world);

            switch (term.Field)
            {
                case ResultFields.Title: return finding.Title;
                case ResultFields.Status: return FindingStatusLattice.ToName(finding.Status);
                case ResultFields.Assignee: return finding.AssignedTo;
                case ResultFields.Group: return finding.Group;
                case ResultFields.Priority: return finding.Priority;
                case ResultFields.Stage: return finding.Stage;
                case ResultFields.Source: return finding.Source;
                case ResultFields.Rule: return finding.Rule;
                case ResultFields.Issue: return world.IssueOf(finding);

                case ResultFields.Comments:
                    return finding.Comments.Count.ToString(CultureInfo.InvariantCulture);

                case ResultFields.Label:
                    return Listed(finding.Labels);

                case ResultFields.Run:
                    return Listed(CheckFacts.RunsOf(finding));

                default:
                    // Anything else is read off the facts, so a check that writes a fact nobody
                    // declared a column for is still searchable on it.
                    return finding.Facts.TryGetValue(term.Field, out var fact) ? fact : null;
            }
        }

        /// <summary>A list of readings as one value the operators can be run over.</summary>
        /// <param name="values">The entries.</param>
        private static string? Listed(IEnumerable<string>? values)
        {
            var held = (values ?? Array.Empty<string>())
                .Where(one => !string.IsNullOrEmpty(one))
                .ToList();

            return held.Count == 0 ? null : string.Join(Apart.ToString(), held);
        }

        /// <summary>What a check's declared column reads on this result.</summary>
        /// <param name="key">The column's key.</param>
        /// <param name="finding">The result.</param>
        /// <param name="world">The world.</param>
        private static string? Declared(string key, Finding finding, IResultWorld world)
        {
            var column = world.Columns(finding)
                              .FirstOrDefault(one => string.Equals(one.Key, key, StringComparison.Ordinal));

            // The column belongs to another check. Not an error: one search runs over the results
            // of several checks, and a clause about the overlap volume simply has no answer for a
            // fall hazard.
            if (column == null) return null;

            var number = column.Number?.Invoke(finding);

            if (number != null) return number.Value.ToString("R", CultureInfo.InvariantCulture);

            return column.Text?.Invoke(finding);
        }

        /// <summary>What a clause reads off one element.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="item">The element, or null when it cannot be resolved.</param>
        private static string? OnItem(QueryTerm term, IModelItem? item)
        {
            if (item == null) return null;

            if (ResultFields.ReadProperty(term.Field, out var category, out var name))
                return Held(item, category, name, term.Ancestors);

            switch (term.Field)
            {
                case ResultFields.ElementName: return item.DisplayName;
                case ResultFields.Category: return item.Category;
                case ResultFields.TypeName: return item.TypeName;
                case ResultFields.Model: return item.Model?.DisplayName;

                case ResultFields.TreePath:
                    return Listed(item.TreePath);

                default:
                    return null;
            }
        }

        /// <summary>
        /// One property, from the element or from the nearest ancestor within reach that carries it.
        ///
        /// Nearest rather than exactly N up: a federation nests to different depths, and a clause
        /// that had to name the exact level would answer for one model and not for the one beside
        /// it. The depth is a limit on how far to look, not an instruction to look only there.
        /// </summary>
        /// <param name="item">The element.</param>
        /// <param name="category">The property's category, or empty for whichever category has it.</param>
        /// <param name="name">The property's name.</param>
        /// <param name="up">How many ancestors to try after the element itself.</param>
        private static string? Held(IModelItem? item, string category, string name, int up)
        {
            var at = item;

            for (var step = 0; at != null && step <= Math.Max(0, up); step++)
            {
                var value = category.Length == 0
                    ? at.Properties()
                        .Where(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase))
                        .Select(one => one.Value)
                        .FirstOrDefault(one => !string.IsNullOrEmpty(one))
                    : at.Property(category, name);

                if (!string.IsNullOrEmpty(value)) return value;

                at = at.Parent;
            }

            return null;
        }

        // ---- Comparing it --------------------------------------------------------------------

        /// <summary>Whether a reading answers the clause.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="value">What was read, or null when there was nothing to read.</param>
        private static bool Compared(QueryTerm term, string? value)
        {
            var said = value ?? string.Empty;

            switch (term.Op)
            {
                case QueryOp.IsEmpty: return said.Trim().Length == 0;
                case QueryOp.IsNotEmpty: return said.Trim().Length != 0;
            }

            if (QueryTerm.Numeric(term.Op)) return Counted(term, said);

            var wanted = term.Text ?? string.Empty;

            // A list arrives joined by a separator no value contains, so the whole-value operators
            // are answered per entry: one of five labels matching is a match.
            var parts = said.Split(Apart);

            switch (term.Op)
            {
                case QueryOp.Is:
                    return parts.Any(one => string.Equals(one, wanted, StringComparison.OrdinalIgnoreCase));

                case QueryOp.IsNot:
                    return !parts.Any(one => string.Equals(one, wanted, StringComparison.OrdinalIgnoreCase));

                case QueryOp.Contains:
                    return Found(said, wanted);

                case QueryOp.DoesNotContain:
                    return !Found(said, wanted);

                case QueryOp.StartsWith:
                    return wanted.Length > 0
                           && parts.Any(one => one.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));

                case QueryOp.EndsWith:
                    return wanted.Length > 0
                           && parts.Any(one => one.EndsWith(wanted, StringComparison.OrdinalIgnoreCase));

                default:
                    return false;
            }
        }

        /// <summary>Whether the text appears in the value.</summary>
        /// <param name="said">The value.</param>
        /// <param name="wanted">The text.</param>
        private static bool Found(string said, string wanted) =>
            wanted.Length > 0 && said.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Whether a reading answers a number clause.</summary>
        /// <param name="term">The clause.</param>
        /// <param name="said">What was read.</param>
        private static bool Counted(QueryTerm term, string said)
        {
            // A value that is not a number does not answer a number clause, either way round. A
            // property holding "N/A" is not under five and is not not-under five; it is not a
            // length at all, and the honest answer is that the clause does not hold.
            if (!Number(said, out var value)) return false;

            switch (term.Op)
            {
                case QueryOp.Equals: return Near(value, term.Number);
                case QueryOp.NotEquals: return !Near(value, term.Number);
                case QueryOp.LessThan: return value < term.Number;
                case QueryOp.AtMost: return value <= term.Number;
                case QueryOp.GreaterThan: return value > term.Number;
                case QueryOp.AtLeast: return value >= term.Number;

                case QueryOp.Between:
                    var low = Math.Min(term.Number, term.Number2);
                    var high = Math.Max(term.Number, term.Number2);

                    return value >= low && value <= high;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Two lengths that are the same to within a tenth of a millimetre.
        ///
        /// Exact equality on a double read from a host measurement never holds, and a coordinator
        /// asking for a hundred-millimetre clearance means a hundred, not the bits.
        /// </summary>
        /// <param name="a">One.</param>
        /// <param name="b">The other.</param>
        private static bool Near(double a, double b) => Math.Abs(a - b) < 0.0001;

        /// <summary>
        /// A number out of a value.
        ///
        /// Invariant first, then the seat's own culture: a property is written by the authoring
        /// tool and may hold either separator, and refusing one of them would make a clause that
        /// works on one machine fail on the next.
        /// </summary>
        /// <param name="said">The value.</param>
        /// <param name="value">The number.</param>
        private static bool Number(string said, out double value)
        {
            var trimmed = said.Trim();

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
