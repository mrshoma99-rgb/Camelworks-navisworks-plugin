using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Search;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The search a rule finds its results with.
    ///
    /// Every test here is written on findings rather than on clash items, because the point of the
    /// engine is that one search language serves every check.
    /// </summary>
    public class ResultSearchTests
    {
        /// <summary>A world over one fake document, with one check's columns for every result.</summary>
        private sealed class World : IResultWorld
        {
            private readonly FakeDocument _document;
            private readonly IReadOnlyList<ResultColumn> _columns;

            internal World(FakeDocument document, params ResultColumn[] columns)
            {
                _document = document;
                _columns = columns;
            }

            public IModelItem? Element(ElementKey key) => _document.Resolve(key);

            public IReadOnlyList<ResultColumn> Columns(Finding finding) => _columns;

            public string? IssueOf(Finding finding) => null;
        }

        /// <summary>A world with no document behind it, as a board read from a file has.</summary>
        private sealed class Nowhere : IResultWorld
        {
            public IModelItem? Element(ElementKey key) => null;

            public IReadOnlyList<ResultColumn> Columns(Finding finding) =>
                Array.Empty<ResultColumn>();

            /// <summary>What is on an issue, by finding id.</summary>
            internal Dictionary<string, string> Raised { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public string? IssueOf(Finding finding) =>
                Raised.TryGetValue(finding.Id, out var said) ? said : null;
        }

        /// <summary>A finding over the given elements, told apart by its title.</summary>
        private static Finding Found(string name, params ElementKey[] elements) =>
            Finding.Create(FindingSource.Clash, "Hard clash", name, elements, discriminator: name);

        /// <summary>One clause, as a whole search.</summary>
        private static ResultQuery One(QueryTerm term) => new ResultQuery(term);

        [Fact]
        public void An_empty_search_finds_everything()
        {
            var findings = new[] { Found("a"), Found("b") };

            Assert.Equal(2, ResultSearch.Run(new ResultQuery(), findings, new Nowhere()).Count);
            Assert.Equal(2, ResultSearch.Run(null, findings, new Nowhere()).Count);
        }

        [Fact]
        public void A_standing_field_is_searched_without_any_document()
        {
            var open = Found("a");
            var done = Found("b");

            done.Status = FindingStatus.Resolved;
            done.AssignedTo = "Mechanical";

            var byStatus = One(new QueryTerm(ResultFields.Status, QueryOp.Is) { Text = "Resolved" });
            var byParty = One(new QueryTerm(ResultFields.Assignee, QueryOp.Is) { Text = "Mechanical" });

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(byStatus, new[] { open, done }, new Nowhere()).Select(f => f.Title));

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(byParty, new[] { open, done }, new Nowhere()).Select(f => f.Title));
        }

        [Fact]
        public void Clauses_stack_with_all_and_any_and_nest()
        {
            var one = Found("a");
            var two = Found("b");
            var three = Found("c");

            one.AssignedTo = "Mechanical";
            one.Priority = "High";

            two.AssignedTo = "Electrical";
            two.Priority = "High";

            three.AssignedTo = "Mechanical";
            three.Priority = "Low";

            // Mechanical AND High.
            var both = new ResultQuery(QueryJoin.All);

            both.Inside.Add(One(new QueryTerm(ResultFields.Assignee, QueryOp.Is) { Text = "Mechanical" }));
            both.Inside.Add(One(new QueryTerm(ResultFields.Priority, QueryOp.Is) { Text = "High" }));

            Assert.Equal(new[] { "a" },
                ResultSearch.Run(both, new[] { one, two, three }, new Nowhere()).Select(f => f.Title));

            // High AND (Mechanical OR Electrical).
            var nested = new ResultQuery(QueryJoin.All);
            var either = new ResultQuery(QueryJoin.Any);

            either.Inside.Add(One(new QueryTerm(ResultFields.Assignee, QueryOp.Is) { Text = "Mechanical" }));
            either.Inside.Add(One(new QueryTerm(ResultFields.Assignee, QueryOp.Is) { Text = "Electrical" }));

            nested.Inside.Add(One(new QueryTerm(ResultFields.Priority, QueryOp.Is) { Text = "High" }));
            nested.Inside.Add(either);

            Assert.Equal(new[] { "a", "b" },
                ResultSearch.Run(nested, new[] { one, two, three }, new Nowhere()).Select(f => f.Title));
        }

        [Fact]
        public void A_group_can_be_inverted()
        {
            var one = Found("a");
            var two = Found("b");

            one.AssignedTo = "Mechanical";
            two.AssignedTo = "Electrical";

            var not = new ResultQuery(QueryJoin.All) { Negate = true };

            not.Inside.Add(One(new QueryTerm(ResultFields.Assignee, QueryOp.Is) { Text = "Mechanical" }));

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(not, new[] { one, two }, new Nowhere()).Select(f => f.Title));
        }

        [Fact]
        public void A_label_is_matched_per_entry_not_across_the_joined_list()
        {
            var one = Found("a");

            one.Labels.Add("Fire");
            one.Labels.Add("Level 03");

            var whole = One(new QueryTerm(ResultFields.Label, QueryOp.Is) { Text = "Fire" });
            var across = One(new QueryTerm(ResultFields.Label, QueryOp.Is) { Text = "FireLevel 03" });

            Assert.Single(ResultSearch.Run(whole, new[] { one }, new Nowhere()));
            Assert.Empty(ResultSearch.Run(across, new[] { one }, new Nowhere()));
        }

        [Fact]
        public void A_check_column_is_searched_by_the_key_it_declares()
        {
            var near = Found("a");
            var far = Found("b");

            near.Facts["Distance"] = "0.01";
            far.Facts["Distance"] = "0.40";

            var column = new ResultColumn("Distance", "Distance", ColumnKind.Number)
            {
                Number = finding => finding.Facts.TryGetValue("Distance", out var said)
                                    && double.TryParse(said, System.Globalization.NumberStyles.Float,
                                                       System.Globalization.CultureInfo.InvariantCulture,
                                                       out var value)
                    ? value
                    : (double?)null,
            };

            var world = new World(new FakeDocument(), column);

            var close = One(new QueryTerm(ResultFields.Column("Distance"), QueryOp.LessThan)
            {
                Number = 0.1,
            });

            Assert.Equal(new[] { "a" },
                ResultSearch.Run(close, new[] { near, far }, world).Select(f => f.Title));

            var span = One(new QueryTerm(ResultFields.Column("Distance"), QueryOp.Between)
            {
                Number = 0.1,
                Number2 = 1.0,
            });

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(span, new[] { near, far }, world).Select(f => f.Title));
        }

        [Fact]
        public void A_column_another_check_declared_simply_does_not_answer()
        {
            var finding = Found("a");

            // No column of this key in the world at all, which is what a fall hazard looks like to
            // a clause written about a clash's overlap.
            var term = One(new QueryTerm(ResultFields.Column("Overlap"), QueryOp.GreaterThan)
            {
                Number = 0,
            });

            Assert.Empty(ResultSearch.Run(term, new[] { finding }, new World(new FakeDocument())));
        }

        [Fact]
        public void An_element_property_is_searched_with_the_text_operators()
        {
            var document = new FakeDocument();
            var model = document.AddModel("ARCH", "arch.nwc");

            var duct = document.AddItem(model, "Duct 1", "Ducts");
            duct.WithProperty("Element", "System Name", "SUP-01");

            var pipe = document.AddItem(model, "Pipe 1", "Pipes");
            pipe.WithProperty("Element", "System Name", "CHW-02");

            var world = new World(document);

            var found = Found("a", duct.Key);
            var other = Found("b", pipe.Key);

            var starts = One(new QueryTerm(ResultFields.Property("Element", "System Name"),
                                           QueryOp.StartsWith)
            {
                Where = QueryWhere.AnyElement,
                Text = "SUP",
            });

            Assert.Equal(new[] { "a" },
                ResultSearch.Run(starts, new[] { found, other }, world).Select(f => f.Title));

            var lacks = One(new QueryTerm(ResultFields.Property("Element", "System Name"),
                                          QueryOp.DoesNotContain)
            {
                Where = QueryWhere.AnyElement,
                Text = "SUP",
            });

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(lacks, new[] { found, other }, world).Select(f => f.Title));
        }

        [Fact]
        public void A_property_is_taken_from_the_nearest_ancestor_that_carries_it()
        {
            var document = new FakeDocument();
            var model = document.AddModel("MEP", "mep.nwc");

            var storey = document.AddItem(model, "Level 03", "Level");
            storey.WithProperty("Element", "Elevation", "9.6");

            var run = document.AddItem(model, "Duct run", "Ducts");
            run.Above = storey;

            var duct = document.AddItem(model, "Duct 1", "Ducts");
            duct.Above = run;

            var world = new World(document);
            var finding = Found("a", duct.Key);

            // The element itself carries no elevation, and neither does its run.
            var onItself = One(new QueryTerm(ResultFields.Property("Element", "Elevation"),
                                             QueryOp.IsNotEmpty)
            {
                Where = QueryWhere.AnyElement,
            });

            Assert.Empty(ResultSearch.Run(onItself, new[] { finding }, world));

            var upTwo = One(new QueryTerm(ResultFields.Property("Element", "Elevation"), QueryOp.Is)
            {
                Where = QueryWhere.AnyElement,
                Ancestors = 2,
                Text = "9.6",
            });

            Assert.Single(ResultSearch.Run(upTwo, new[] { finding }, world));
        }

        [Fact]
        public void The_two_sides_are_compared_with_each_other()
        {
            var document = new FakeDocument();
            var arch = document.AddModel("ARCH", "arch.nwc");
            var mep = document.AddModel("MEP", "mep.nwc");

            var wall = document.AddItem(arch, "Wall 1", "Walls");
            var beam = document.AddItem(arch, "Beam 1", "Framing");
            var duct = document.AddItem(mep, "Duct 1", "Ducts");

            var world = new World(document);

            var withinOne = Found("a", wall.Key, beam.Key);
            var acrossTwo = Found("b", wall.Key, duct.Key);

            var same = One(new QueryTerm(ResultFields.Model, QueryOp.Same)
            {
                Where = QueryWhere.BetweenElements,
            });

            Assert.Equal(new[] { "a" },
                ResultSearch.Run(same, new[] { withinOne, acrossTwo }, world).Select(f => f.Title));

            var differ = One(new QueryTerm(ResultFields.Model, QueryOp.Different)
            {
                Where = QueryWhere.BetweenElements,
            });

            Assert.Equal(new[] { "b" },
                ResultSearch.Run(differ, new[] { withinOne, acrossTwo }, world).Select(f => f.Title));
        }

        [Fact]
        public void Any_and_every_and_each_side_are_different_questions()
        {
            var document = new FakeDocument();
            var model = document.AddModel("MEP", "mep.nwc");

            var duct = document.AddItem(model, "Duct 1", "Ducts");
            var wall = document.AddItem(model, "Wall 1", "Walls");

            var world = new World(document);
            var finding = Found("a", duct.Key, wall.Key);

            ResultQuery Categorised(QueryWhere where) =>
                One(new QueryTerm(ResultFields.Category, QueryOp.Is)
                {
                    Where = where,
                    Text = "Ducts",
                });

            Assert.Single(ResultSearch.Run(Categorised(QueryWhere.AnyElement), new[] { finding }, world));
            Assert.Empty(ResultSearch.Run(Categorised(QueryWhere.EveryElement), new[] { finding }, world));
            Assert.Single(ResultSearch.Run(Categorised(QueryWhere.FirstElement), new[] { finding }, world));
            Assert.Empty(ResultSearch.Run(Categorised(QueryWhere.SecondElement), new[] { finding }, world));
        }

        [Fact]
        public void An_element_clause_finds_nothing_when_the_model_is_not_open()
        {
            var key = ElementKey.FromTreePath("MEP", "mep.nwc", "Duct 1");
            var finding = Found("a", key);

            var term = One(new QueryTerm(ResultFields.Category, QueryOp.IsNot)
            {
                Where = QueryWhere.AnyElement,
                Text = "Ducts",
            });

            // Not a match, even though the operator is a negative one: the clause is a claim about
            // an element, and an element that cannot be resolved has not shown that it holds.
            Assert.Empty(ResultSearch.Run(term, new[] { finding }, new Nowhere()));
        }

        [Fact]
        public void A_value_that_is_not_a_number_does_not_answer_a_number_clause_either_way()
        {
            var document = new FakeDocument();
            var model = document.AddModel("MEP", "mep.nwc");

            var duct = document.AddItem(model, "Duct 1", "Ducts");
            duct.WithProperty("Element", "Width", "N/A");

            var world = new World(document);
            var finding = Found("a", duct.Key);

            var under = One(new QueryTerm(ResultFields.Property("Element", "Width"), QueryOp.LessThan)
            {
                Where = QueryWhere.AnyElement,
                Number = 5,
            });

            var over = One(new QueryTerm(ResultFields.Property("Element", "Width"), QueryOp.AtLeast)
            {
                Where = QueryWhere.AnyElement,
                Number = 5,
            });

            Assert.Empty(ResultSearch.Run(under, new[] { finding }, world));
            Assert.Empty(ResultSearch.Run(over, new[] { finding }, world));
        }

        [Fact]
        public void A_search_survives_being_written_and_read()
        {
            var query = new ResultQuery(QueryJoin.Any) { Negate = true };

            query.Inside.Add(new ResultQuery(
                new QueryTerm(ResultFields.Property("Element", "System Name"), QueryOp.Contains)
                {
                    Where = QueryWhere.EveryElement,
                    Text = "SUP",
                    Ancestors = 2,
                }));

            var inner = new ResultQuery(QueryJoin.All);

            inner.Inside.Add(new ResultQuery(
                new QueryTerm(ResultFields.Column("Distance"), QueryOp.Between)
                {
                    Number = 0.1,
                    Number2 = 0.5,
                }));

            query.Inside.Add(inner);

            var read = ResultQuery.FromJson(query.ToJson(), out var lost);

            Assert.Equal(0, lost);
            Assert.True(read.Negate);
            Assert.Equal(QueryJoin.Any, read.Join);
            Assert.Equal(2, read.Clauses);

            var first = read.Inside[0].Term!;

            Assert.Equal(ResultFields.Property("Element", "System Name"), first.Field);
            Assert.Equal(QueryWhere.EveryElement, first.Where);
            Assert.Equal(QueryOp.Contains, first.Op);
            Assert.Equal("SUP", first.Text);
            Assert.Equal(2, first.Ancestors);

            var second = read.Inside[1].Inside[0].Term!;

            Assert.Equal(QueryOp.Between, second.Op);
            Assert.Equal(0.1, second.Number, 6);
            Assert.Equal(0.5, second.Number2, 6);
        }

        [Fact]
        public void A_clause_this_build_cannot_read_is_reported_rather_than_widening_the_search()
        {
            var json = JsonValue.Object()
                .Set("join", JsonValue.String("All"))
                .Set("inside", JsonValue.Array(new[]
                {
                    JsonValue.Object().Set("term", JsonValue.Object()
                        .Set("field", JsonValue.String("status"))
                        .Set("where", JsonValue.String("Result"))
                        .Set("op", JsonValue.String("SoundsLike"))),
                }));

            var read = ResultQuery.FromJson(json, out var lost);

            Assert.Equal(1, lost);
            Assert.Equal(0, read.Clauses);
        }

        [Fact]
        public void The_fields_offered_follow_the_check_whose_results_are_searched()
        {
            var check = new FakeCheck("Fake", "Fake check");
            var declared = check.Columns.First(one => one.Kind == ColumnKind.Number);

            var fields = ResultFields.For(check);

            Assert.Contains(fields, one => one.Id == ResultFields.Status);
            Assert.Contains(fields, one => one.Id == ResultFields.Column(declared.Key)
                                           && one.Value == QueryValue.Number);
            Assert.Contains(fields, one => one.Id == ResultFields.Category && one.OnElement);

            // Nothing clash-shaped is named anywhere in the field list.
            Assert.DoesNotContain(fields, one => one.Title.IndexOf("clash",
                StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
