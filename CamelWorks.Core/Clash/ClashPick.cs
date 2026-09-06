using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>One parameter of a clash result, by a name that can be saved and read back.</summary>
    public sealed class ClashFacet
    {
        private readonly Func<ClashItem, string?> _text;
        private readonly Func<ClashItem, double?>? _number;

        internal ClashFacet(string key, string title, Func<ClashItem, string?> text,
                            Func<ClashItem, double?>? number = null)
        {
            Key = key; Title = title; _text = text; _number = number;
        }

        /// <summary>What it is called in a saved filter.</summary>
        public string Key { get; }

        /// <summary>What it is called on screen.</summary>
        public string Title { get; }

        /// <summary>Whether it can be compared as a number as well as matched as text.</summary>
        public bool IsNumber => _number != null;

        /// <summary>What this result holds under it, or null.</summary>
        /// <param name="item">The result.</param>
        public string? Read(ClashItem? item) => item == null ? null : _text(item);

        /// <summary>What this result holds as a number, or null when it holds none.</summary>
        /// <param name="item">The result.</param>
        public double? Number(ClashItem? item) =>
            item == null || _number == null ? (double?)null : _number(item);

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Every parameter an export can be narrowed or ordered by.
    ///
    /// The list is built from the results in hand rather than declared once, because half of what
    /// a coordinator wants to filter on is not a field of the product at all: it is a property the
    /// models carry, or a storey the project declared. A fixed list would answer "filter by
    /// anything" with a menu of nine things.
    /// </summary>
    public static class ClashFacets
    {
        /// <summary>The key a property facet is saved under, ahead of the property's own name.</summary>
        public const string PropertyPrefix = "prop:";

        /// <summary>The facets every clash result has, whatever the job.</summary>
        /// <param name="plan">The project's storeys, so elevation reads as a level name.</param>
        public static IReadOnlyList<ClashFacet> Standing(LevelPlan? plan = null)
        {
            var list = new List<ClashFacet>
            {
                new ClashFacet("name", "Clash", i => i.Name),
                new ClashFacet("test", "Test", i => i.TestName),
                new ClashFacet("testFolder", "Test folder", i => i.TestFolder),
                new ClashFacet("group", "Group", i => i.CarriedGroup),
                new ClashFacet("status", "Status", i => i.Status.ToString(), i => (double)(int)i.Status),
                new ClashFacet("assignee", "Assigned to", i => i.AssignedTo),
                new ClashFacet("level", "Level", i => i.Level),
                new ClashFacet("zone", "Zone", i => i.Zone),
                new ClashFacet("grid", "Grid", i => i.Grid),
                new ClashFacet("models", "Models", i => i.ModelPair),
                new ClashFacet("modelA", "Model A", i => i.ModelA),
                new ClashFacet("modelB", "Model B", i => i.ModelB),
                new ClashFacet("disciplines", "Disciplines", i => i.DisciplinePair),
                new ClashFacet("systemA", "System A", i => i.SystemA),
                new ClashFacet("systemB", "System B", i => i.SystemB),
                new ClashFacet("categoryA", "Category A", i => i.CategoryA),
                new ClashFacet("categoryB", "Category B", i => i.CategoryB),
                new ClashFacet("elevation", "Elevation", i => Said(i.Z), i => i.Z),
                new ClashFacet("x", "X", i => Said(i.X), i => i.X),
                new ClashFacet("y", "Y", i => Said(i.Y), i => i.Y),
                new ClashFacet("distance", "Distance", i => Said(i.Distance), i => i.Distance),
                new ClashFacet("overlap", "Overlap volume",
                               i => Said(i.OverlapVolume), i => i.OverlapVolume),
                new ClashFacet("angle", "Crossing angle",
                               i => Said(i.CrossingAngleDegrees), i => i.CrossingAngleDegrees),
                new ClashFacet("comments", "Comments",
                               i => i.Comments.Count.ToString(CultureInfo.InvariantCulture),
                               i => i.Comments.Count),
            };

            // The storey a result stands on, which is the one a coordinator asks for by name. It
            // is only offered when the project has declared its levels: without a plan there is
            // no answer to give, and an empty facet in the menu reads as a broken one.
            if (plan != null && !plan.IsEmpty)
                list.Insert(7, new ClashFacet("storey", "Storey", i => plan.LevelAt(i.Z)));

            return list;
        }

        /// <summary>Every facet these results offer: the standing ones, and each property they carry.</summary>
        /// <param name="items">The results in hand.</param>
        /// <param name="plan">The project's storeys.</param>
        /// <param name="issued">What has already gone out, or null for a profile with no log.</param>
        public static IReadOnlyList<ClashFacet> Every(IEnumerable<ClashItem>? items,
                                                      LevelPlan? plan = null,
                                                      ClashIssueLog? issued = null)
        {
            var list = new List<ClashFacet>(Standing(plan));

            // What has already gone out, when there is a log to read it from. A profile written
            // before anything went out has none, and these would answer the same way for every
            // result.
            if (issued != null && issued.Count > 0) list.AddRange(issued.Facets());

            var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items ?? Array.Empty<ClashItem>())
            {
                if (item == null) continue;

                foreach (var pair in item.Properties)
                    if (!string.IsNullOrWhiteSpace(pair.Key)) keys.Add(pair.Key.Trim());
            }

            foreach (var key in keys)
            {
                var here = key;

                list.Add(new ClashFacet(PropertyPrefix + here, here,
                                        i => i.Properties.TryGetValue(here, out var held)
                                            ? held
                                            : null,
                                        i => Number(i.Properties.TryGetValue(here, out var held)
                                            ? held
                                            : null)));
            }

            return list;
        }

        /// <summary>The facet under this key, or null.</summary>
        /// <param name="key">The saved key.</param>
        /// <param name="items">The results in hand, for a property key.</param>
        /// <param name="plan">The project's storeys.</param>
        /// <param name="issued">What has already gone out, or null for a profile with no log.</param>
        public static ClashFacet? Of(string? key, IEnumerable<ClashItem>? items = null,
                                     LevelPlan? plan = null, ClashIssueLog? issued = null)
        {
            var said = (key ?? string.Empty).Trim();

            if (said.Length == 0) return null;

            var found = Standing(plan).FirstOrDefault(
                one => string.Equals(one.Key, said, StringComparison.OrdinalIgnoreCase));

            if (found != null) return found;

            if (issued != null)
            {
                var sent = issued.Facets().FirstOrDefault(
                    one => string.Equals(one.Key, said, StringComparison.OrdinalIgnoreCase));

                if (sent != null) return sent;
            }

            if (!said.StartsWith(PropertyPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            // A property facet is made on demand rather than looked up. A saved filter naming a
            // property nothing in this federation carries still has to load and still has to say
            // what it was about; it simply matches nothing.
            var name = said.Substring(PropertyPrefix.Length).Trim();

            if (name.Length == 0) return null;

            _ = items;

            return new ClashFacet(PropertyPrefix + name, name,
                                  i => i.Properties.TryGetValue(name, out var held) ? held : null,
                                  i => Number(i.Properties.TryGetValue(name, out var held)
                                      ? held
                                      : null));
        }

        /// <summary>Every value these results hold under one facet, in order and without repeats.</summary>
        /// <param name="facet">The facet.</param>
        /// <param name="items">The results.</param>
        public static IReadOnlyList<string> Values(ClashFacet? facet, IEnumerable<ClashItem>? items)
        {
            if (facet == null) return Array.Empty<string>();

            var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items ?? Array.Empty<ClashItem>())
            {
                var said = facet.Read(item);

                if (!string.IsNullOrWhiteSpace(said)) seen.Add(said!.Trim());
            }

            return seen.ToList();
        }

        private static string? Said(double? value) =>
            value == null
                ? null
                : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

        private static double? Number(string? said) =>
            double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out var held)
                ? held
                : (double?)null;
    }

    /// <summary>How a clause compares what a result holds against what the clause names.</summary>
    public enum ClashCompare
    {
        /// <summary>The value is one of the named ones.</summary>
        Is = 0,

        /// <summary>The value is none of the named ones.</summary>
        IsNot = 1,

        /// <summary>The value carries one of the named ones somewhere in it.</summary>
        Holds = 2,

        /// <summary>The result holds nothing under this facet.</summary>
        IsEmpty = 3,

        /// <summary>The result holds something under this facet.</summary>
        IsFilled = 4,

        /// <summary>The number is at or above the low bound.</summary>
        AtLeast = 5,

        /// <summary>The number is at or below the high bound.</summary>
        AtMost = 6,

        /// <summary>The number is between the two bounds, inclusive.</summary>
        Between = 7,
    }

    /// <summary>One condition an exported clash has to meet.</summary>
    public sealed class ClashClause
    {
        private readonly List<string> _values = new List<string>();

        /// <summary>Make one.</summary>
        /// <param name="facet">Which parameter it is about.</param>
        /// <param name="compare">How it compares.</param>
        public ClashClause(string? facet = null, ClashCompare compare = ClashCompare.Is)
        {
            Facet = (facet ?? string.Empty).Trim();
            Compare = compare;
        }

        /// <summary>Which parameter, by its saved key.</summary>
        public string Facet { get; set; }

        /// <summary>How it compares.</summary>
        public ClashCompare Compare { get; set; }

        /// <summary>
        /// The values it names, for the text comparisons.
        ///
        /// A list rather than one value, because the question a coordinator asks is "new or
        /// active", not "new" and then "active" as two clauses that between them match nothing.
        /// </summary>
        public IList<string> Values => _values;

        /// <summary>The low bound, for the number comparisons.</summary>
        public double Low { get; set; }

        /// <summary>The high bound, for the number comparisons.</summary>
        public double High { get; set; }

        /// <summary>Whether this result meets it.</summary>
        /// <param name="item">The result.</param>
        /// <param name="facet">The facet named, already resolved.</param>
        public bool Matches(ClashItem? item, ClashFacet? facet)
        {
            if (item == null) return false;

            // A clause naming a facet this federation does not have matches nothing rather than
            // everything. A filter that quietly widens when a property goes missing sends a
            // stakeholder the whole job.
            if (facet == null) return false;

            var said = (facet.Read(item) ?? string.Empty).Trim();

            switch (Compare)
            {
                case ClashCompare.IsEmpty:
                    return said.Length == 0;

                case ClashCompare.IsFilled:
                    return said.Length > 0;

                case ClashCompare.IsNot:
                    return !_values.Any(v => Same(said, v));

                case ClashCompare.Holds:
                    return _values.Any(v => said.IndexOf((v ?? string.Empty).Trim(),
                                                         StringComparison.OrdinalIgnoreCase) >= 0);

                case ClashCompare.AtLeast:
                case ClashCompare.AtMost:
                case ClashCompare.Between:
                {
                    var held = facet.Number(item);

                    // No number is not zero. A result whose engine reported no overlap volume
                    // cannot answer a question about overlap volume, and answering it as zero
                    // would quietly hold back results nobody excluded.
                    if (held == null) return false;

                    if (Compare == ClashCompare.AtLeast) return held.Value >= Low;
                    if (Compare == ClashCompare.AtMost) return held.Value <= High;

                    return held.Value >= Math.Min(Low, High) && held.Value <= Math.Max(Low, High);
                }

                default:
                    return _values.Any(v => Same(said, v));
            }
        }

        /// <summary>How the clause reads.</summary>
        /// <param name="facet">The facet named, already resolved.</param>
        public string Describe(ClashFacet? facet)
        {
            var title = facet?.Title ?? Facet;

            switch (Compare)
            {
                case ClashCompare.IsEmpty: return title + " is empty";
                case ClashCompare.IsFilled: return title + " is filled in";
                case ClashCompare.IsNot: return title + " is not " + Listed();
                case ClashCompare.Holds: return title + " holds " + Listed();
                case ClashCompare.AtLeast: return title + " at least " + Bare(Low);
                case ClashCompare.AtMost: return title + " at most " + Bare(High);
                case ClashCompare.Between:
                    return title + " between " + Bare(Math.Min(Low, High)) + " and "
                           + Bare(Math.Max(Low, High));
                default: return title + " is " + Listed();
            }
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("facet", Facet)
                .Set("compare", (long)Compare);

            if (_values.Count > 0)
                json.Set("values", JsonValue.Array(_values.Select(JsonValue.String)));

            if (Low != 0) json.Set("low", Low);
            if (High != 0) json.Set("high", High);

            return json;
        }

        /// <summary>Read one back, or null when it names no facet.</summary>
        /// <param name="json">What was written.</param>
        public static ClashClause? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var facet = (json["facet"].AsString() ?? string.Empty).Trim();

            if (facet.Length == 0) return null;

            var compare = (int)json["compare"].AsDouble(0);

            if (compare < 0 || compare > (int)ClashCompare.Between) compare = 0;

            var clause = new ClashClause(facet, (ClashCompare)compare)
            {
                Low = json["low"].AsDouble(0),
                High = json["high"].AsDouble(0),
            };

            foreach (var one in json["values"].Items)
            {
                var said = one.AsString();

                if (!string.IsNullOrWhiteSpace(said)) clause.Values.Add(said!);
            }

            return clause;
        }

        /// <inheritdoc />
        public override string ToString() => Describe(null);

        private string Listed() =>
            _values.Count == 0
                ? "nothing"
                : string.Join(" or ", _values.Select(v => "“" + v + "”"));

        private static bool Same(string said, string? value) =>
            string.Equals(said, (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Bare(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Which clashes go in a file, and in what order.
    ///
    /// A pack is sent to one stakeholder, so the question is never "every clash on the job": it is
    /// the mechanical contractor's open items on levels 3 to 5. Both halves of that are here — the
    /// clauses that narrow and the facet that orders — and both are saved with the export profile,
    /// because it is the same question every Friday.
    ///
    /// Every clause has to hold. Alternatives are said inside one clause, which is where a
    /// coordinator says them: "new or active" is one thought.
    /// </summary>
    public sealed class ClashPick
    {
        private readonly List<ClashClause> _clauses = new List<ClashClause>();

        /// <summary>The conditions, all of which have to hold.</summary>
        public IList<ClashClause> Clauses => _clauses;

        /// <summary>Which facet the results are ordered by, or null to leave them as they came.</summary>
        public string? Sort { get; set; }

        /// <summary>Whether the order runs backwards.</summary>
        public bool Descending { get; set; }

        /// <summary>True when nothing is being narrowed.</summary>
        public bool IsEverything => _clauses.Count == 0;

        /// <summary>Add a clause and hand it back.</summary>
        /// <param name="facet">Which parameter.</param>
        /// <param name="compare">How it compares.</param>
        public ClashClause Add(string? facet, ClashCompare compare = ClashCompare.Is)
        {
            var made = new ClashClause(facet, compare);

            _clauses.Add(made);

            return made;
        }

        /// <summary>The results that get through, in order.</summary>
        /// <param name="items">Every result on the board.</param>
        /// <param name="plan">The project's storeys, for the storey facet.</param>
        /// <param name="issued">What has already gone out, for a clause that names it.</param>
        public IReadOnlyList<ClashItem> Take(IEnumerable<ClashItem>? items, LevelPlan? plan = null,
                                             ClashIssueLog? issued = null)
        {
            var all = (items ?? Array.Empty<ClashItem>()).Where(one => one != null).ToList();

            // Resolved once for the whole pass. A property facet is built from its key, so
            // resolving per clause per result walks the same string work four hundred times.
            var facets = _clauses
                .Select(one => ClashFacets.Of(one.Facet, all, plan, issued))
                .ToList();

            var kept = new List<ClashItem>();

            foreach (var item in all)
            {
                var takes = true;

                for (var at = 0; at < _clauses.Count && takes; at++)
                    takes = _clauses[at].Matches(item, facets[at]);

                if (takes) kept.Add(item);
            }

            return Ordered(kept, plan);
        }

        /// <summary>The same results, put in the pick's order.</summary>
        /// <param name="items">The results to order.</param>
        /// <param name="plan">The project's storeys.</param>
        public IReadOnlyList<ClashItem> Ordered(IReadOnlyList<ClashItem>? items,
                                                LevelPlan? plan = null)
        {
            var all = (items ?? Array.Empty<ClashItem>()).ToList();

            var by = ClashFacets.Of(Sort, all, plan);

            if (by == null) return all;

            // A number facet sorts as numbers. Sorted as text, elevation runs 10, 100, 2 - which
            // is the one ordering nobody asked for.
            var sorted = by.IsNumber
                ? all.OrderBy(one => by.Number(one) ?? double.MaxValue).ToList()
                : all.OrderBy(one => by.Read(one) ?? string.Empty,
                              StringComparer.OrdinalIgnoreCase).ToList();

            if (Descending) sorted.Reverse();

            return sorted;
        }

        /// <summary>How the whole pick reads, in one line.</summary>
        /// <param name="plan">The project's storeys, so a facet reads by its title.</param>
        public string Describe(LevelPlan? plan = null)
        {
            if (_clauses.Count == 0) return "Every clash.";

            var said = string.Join(", ",
                _clauses.Select(one => one.Describe(ClashFacets.Of(one.Facet, null, plan))));

            return said + ".";
        }

        /// <summary>Write it down, or null when there is nothing to write.</summary>
        public JsonValue? ToJson()
        {
            if (_clauses.Count == 0 && string.IsNullOrWhiteSpace(Sort)) return null;

            var json = JsonValue.Object();

            if (_clauses.Count > 0)
                json.Set("clauses", JsonValue.Array(_clauses.Select(one => one.ToJson())));

            if (!string.IsNullOrWhiteSpace(Sort))
            {
                json.Set("sort", Sort!);

                if (Descending) json.Set("descending", true);
            }

            return json;
        }

        /// <summary>Read one back. A missing or empty record gives a pick that takes everything.</summary>
        /// <param name="json">What was written.</param>
        public static ClashPick FromJson(JsonValue? json)
        {
            var pick = new ClashPick();

            if (json == null) return pick;

            foreach (var one in json["clauses"].Items)
            {
                var clause = ClashClause.FromJson(one);

                if (clause != null) pick.Clauses.Add(clause);
            }

            var sort = json["sort"].AsString();

            if (!string.IsNullOrWhiteSpace(sort))
            {
                pick.Sort = sort!.Trim();
                pick.Descending = json["descending"].AsBool(false);
            }

            return pick;
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }
}
