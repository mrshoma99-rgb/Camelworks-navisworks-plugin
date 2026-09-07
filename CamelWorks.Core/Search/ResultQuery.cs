using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Search
{
    /// <summary>How the clauses of one group are joined.</summary>
    public enum QueryJoin
    {
        /// <summary>Every clause in the group has to match.</summary>
        All = 0,

        /// <summary>One matching clause is enough.</summary>
        Any = 1,
    }

    /// <summary>Which of a result's elements a clause is asked about.</summary>
    public enum QueryWhere
    {
        /// <summary>The result itself: its status, its check's columns, what it is called.</summary>
        Result = 0,

        /// <summary>True when at least one of the result's elements answers.</summary>
        AnyElement = 1,

        /// <summary>True only when every one of them answers.</summary>
        EveryElement = 2,

        /// <summary>The first element only — on a clash, the first side.</summary>
        FirstElement = 3,

        /// <summary>The second element only — on a clash, the other side.</summary>
        SecondElement = 4,

        /// <summary>
        /// The two sides compared with each other rather than with a value.
        ///
        /// This is how "both in the same model", "on the same level" and "the same value for a
        /// parameter" are written, without a term per parameter somebody might pick.
        /// </summary>
        BetweenElements = 5,
    }

    /// <summary>How a value is compared.</summary>
    public enum QueryOp
    {
        /// <summary>Text, matched whole and without regard to case.</summary>
        Is = 0,

        /// <summary>Text, not matched whole.</summary>
        IsNot = 1,

        /// <summary>Text found anywhere in the value.</summary>
        Contains = 2,

        /// <summary>Text found nowhere in the value.</summary>
        DoesNotContain = 3,

        /// <summary>Value begins with the text.</summary>
        StartsWith = 4,

        /// <summary>Value ends with the text.</summary>
        EndsWith = 5,

        /// <summary>No value at all, or an empty one.</summary>
        IsEmpty = 6,

        /// <summary>Some value.</summary>
        IsNotEmpty = 7,

        /// <summary>Number equal to.</summary>
        Equals = 8,

        /// <summary>Number not equal to.</summary>
        NotEquals = 9,

        /// <summary>Number below.</summary>
        LessThan = 10,

        /// <summary>Number below or equal to.</summary>
        AtMost = 11,

        /// <summary>Number above.</summary>
        GreaterThan = 12,

        /// <summary>Number above or equal to.</summary>
        AtLeast = 13,

        /// <summary>Number within a range, both ends counted.</summary>
        Between = 14,

        /// <summary>The two sides read the same. <see cref="QueryWhere.BetweenElements"/> only.</summary>
        Same = 15,

        /// <summary>The two sides read differently. <see cref="QueryWhere.BetweenElements"/> only.</summary>
        Different = 16,
    }

    /// <summary>What kind of value a field holds, which decides the operators offered for it.</summary>
    public enum QueryValue
    {
        /// <summary>Words.</summary>
        Text = 0,

        /// <summary>A number, in metres where it is a length.</summary>
        Number = 1,
    }

    /// <summary>
    /// One clause of a search: a field, how it is compared, and what it is compared with.
    ///
    /// The field is a string id rather than a member, because the fields a search can offer are not
    /// known when this is written. Three families of them exist and only the first is fixed:
    ///
    /// <list type="bullet">
    /// <item>The standing result fields — status, assignee, group, priority, title — which every
    /// check's results carry, named by <see cref="ResultFields"/>.</item>
    /// <item>Whatever the check that produced the result declares as a column: a clash declares its
    /// overlap and its distance, a clearance its headroom. Named <c>column:</c> and its key, so a
    /// check that adds a measurement is searchable on it the day it does, with nothing added
    /// here.</item>
    /// <item>An element's own facts — its category, its type, its model, and any property it
    /// carries — named <c>property:</c> and the category and name.</item>
    /// </list>
    /// </summary>
    public sealed class QueryTerm
    {
        /// <summary>Write a clause.</summary>
        /// <param name="field">Which field, as <see cref="ResultFields"/> names them.</param>
        /// <param name="op">How it is compared.</param>
        public QueryTerm(string field, QueryOp op)
        {
            Field = string.IsNullOrWhiteSpace(field)
                ? throw new ArgumentException("field is required", nameof(field))
                : field.Trim();

            Op = op;
        }

        /// <summary>Which field.</summary>
        public string Field { get; set; }

        /// <summary>Which of the result's elements it asks about.</summary>
        public QueryWhere Where { get; set; } = QueryWhere.Result;

        /// <summary>How the value is compared.</summary>
        public QueryOp Op { get; set; }

        /// <summary>What it is compared with, for the text operators.</summary>
        public string? Text { get; set; }

        /// <summary>What it is compared with, for the number operators.</summary>
        public double Number { get; set; }

        /// <summary>The far end, for <see cref="QueryOp.Between"/>.</summary>
        public double Number2 { get; set; }

        /// <summary>
        /// How far up the tree to look for the property, or nought for the element itself.
        ///
        /// A model is a tree and the answer is often on a parent: the level a duct sits on, the
        /// system a run belongs to, the file a block came from. Given a depth the search takes the
        /// first ancestor within it that carries the property at all, so one clause answers for a
        /// federation whose models nest to different depths.
        /// </summary>
        public int Ancestors { get; set; }

        /// <summary>Whether this clause compares two sides rather than a side and a value.</summary>
        public bool IsBetween => Where == QueryWhere.BetweenElements;

        /// <summary>Whether the operator needs no value at all.</summary>
        /// <param name="op">The operator.</param>
        public static bool Bare(QueryOp op) =>
            op == QueryOp.IsEmpty || op == QueryOp.IsNotEmpty
            || op == QueryOp.Same || op == QueryOp.Different;

        /// <summary>Whether the operator reads a number rather than text.</summary>
        /// <param name="op">The operator.</param>
        public static bool Numeric(QueryOp op) =>
            op >= QueryOp.Equals && op <= QueryOp.Between;

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("field", JsonValue.String(Field))
                .Set("where", JsonValue.String(Where.ToString()))
                .Set("op", JsonValue.String(Op.ToString()));

            if (!string.IsNullOrEmpty(Text)) json.Set("text", JsonValue.String(Text!));
            if (Number != 0) json.Set("a", JsonValue.Number(Number));
            if (Number2 != 0) json.Set("b", JsonValue.Number(Number2));
            if (Ancestors != 0) json.Set("up", JsonValue.Number(Ancestors));

            return json;
        }

        /// <summary>Read one back, or null when the file does not hold one.</summary>
        /// <param name="json">The candidate.</param>
        public static QueryTerm? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var field = json["field"].AsString();

            // An operator or a scope this build does not know is not read as the first in its
            // enum: a clause saved by a newer build must not come back meaning something else and
            // then be run.
            if (string.IsNullOrWhiteSpace(field)
                || !Enum.TryParse<QueryOp>(json["op"].AsString(), out var op)
                || !Enum.TryParse<QueryWhere>(json["where"].AsString(), out var where))
                return null;

            return new QueryTerm(field!, op)
            {
                Where = where,
                Text = json["text"].AsString(),
                Number = json["a"].AsDouble(),
                Number2 = json["b"].AsDouble(),
                Ancestors = (int)json["up"].AsDouble(),
            };
        }
    }

    /// <summary>
    /// A search: clauses joined by all or any, with groups inside groups.
    ///
    /// The shape a search set has in every tool a coordinator already uses — a stack of conditions,
    /// joined one way or the other, and a bracket where a stack is not enough. A node is either a
    /// clause or a group of nodes; nothing else.
    /// </summary>
    public sealed class ResultQuery
    {
        /// <summary>An empty group, which matches everything.</summary>
        /// <param name="join">How its clauses are joined.</param>
        public ResultQuery(QueryJoin join = QueryJoin.All) => Join = join;

        /// <summary>One clause.</summary>
        /// <param name="term">The clause.</param>
        public ResultQuery(QueryTerm term) =>
            Term = term ?? throw new ArgumentNullException(nameof(term));

        /// <summary>The clause, or null when this is a group.</summary>
        public QueryTerm? Term { get; }

        /// <summary>How the group's children are joined. Meaningless on a clause.</summary>
        public QueryJoin Join { get; set; }

        /// <summary>True to invert whatever this node answers.</summary>
        public bool Negate { get; set; }

        /// <summary>The group's children, in the order they read.</summary>
        public IList<ResultQuery> Inside { get; } = new List<ResultQuery>();

        /// <summary>Whether this node is a clause rather than a group.</summary>
        public bool IsTerm => Term != null;

        /// <summary>
        /// True when the search asks nothing at all, and so matches every result.
        ///
        /// An empty group is deliberately everything rather than nothing: a rule is written by
        /// adding conditions to it, and one that matched nothing until the first condition was
        /// finished would report nought found for every half-written rule.
        /// </summary>
        public bool IsEverything =>
            !IsTerm && !Negate && Inside.Count == 0;

        /// <summary>How many clauses the whole tree holds.</summary>
        public int Clauses =>
            IsTerm ? 1 : Inside.Sum(one => one.Clauses);

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            if (Negate) json.Set("not", JsonValue.String("yes"));

            if (IsTerm)
            {
                json.Set("term", Term!.ToJson());
                return json;
            }

            json.Set("join", JsonValue.String(Join.ToString()));
            json.Set("inside", JsonValue.Array(Inside.Select(one => one.ToJson())));

            return json;
        }

        /// <summary>
        /// Read a search back.
        ///
        /// A clause this build cannot read is dropped rather than failing the load, the way the
        /// rest of the project file behaves — but a group whose every clause was dropped becomes
        /// an empty group, which matches everything, so a search that lost a condition is reported
        /// by <paramref name="lost"/> rather than silently widening.
        /// </summary>
        /// <param name="json">The saved search.</param>
        /// <param name="lost">Counts the clauses that could not be read.</param>
        public static ResultQuery FromJson(JsonValue? json, out int lost)
        {
            lost = 0;
            return Read(json, ref lost) ?? new ResultQuery();
        }

        /// <summary>Read one node.</summary>
        /// <param name="json">The node.</param>
        /// <param name="lost">Counts what could not be read.</param>
        private static ResultQuery? Read(JsonValue? json, ref int lost)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var negate = json["not"].AsString() == "yes";
            var term = json["term"];

            if (term != null && term.Kind == JsonKind.Object)
            {
                var read = QueryTerm.FromJson(term);

                if (read == null)
                {
                    lost++;
                    return null;
                }

                return new ResultQuery(read) { Negate = negate };
            }

            var group = new ResultQuery(
                Enum.TryParse<QueryJoin>(json["join"].AsString(), out var join) ? join : QueryJoin.All)
            {
                Negate = negate,
            };

            foreach (var one in json["inside"].Items)
            {
                var child = Read(one, ref lost);

                if (child != null) group.Inside.Add(child);
            }

            return group;
        }
    }
}
