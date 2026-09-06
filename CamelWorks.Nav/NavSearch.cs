using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Sets;

namespace CamelWorks.Nav
{
    /// <summary>What running a compiled set expression produced.</summary>
    public sealed class SearchOutcome
    {
        internal SearchOutcome(IReadOnlyList<ElementKey> keys, ModelItemCollection items, bool isLive,
                               IReadOnlyList<string> notes)
        {
            Keys = keys; Items = items; IsLive = isLive; Notes = notes;
        }

        /// <summary>The elements it matched.</summary>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>The same elements as the host holds them.</summary>
        public ModelItemCollection Items { get; }

        /// <summary>
        /// True when the whole expression fitted into one host search, and can therefore be saved
        /// as a search set that re-runs.
        ///
        /// The distinction is worth surfacing rather than hiding. A live search set picks up
        /// tomorrow's ductwork; a fixed selection set does not, and a coordinator who thinks they
        /// have the first while holding the second finds out three weeks later.
        /// </summary>
        public bool IsLive { get; }

        /// <summary>Anything the user should know about how it ran.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>
        /// Why the set was not written, or null when it was.
        ///
        /// A search that matched four thousand elements and a document that would not keep the
        /// set are two different outcomes, and reporting the first while the second happened is
        /// how somebody finds out next week.
        /// </summary>
        public string? Problem { get; private set; }

        /// <summary>The same outcome, with the document's refusal on it.</summary>
        /// <param name="problem">Why nothing was written.</param>
        internal SearchOutcome Refused(string? problem)
        {
            Problem = problem ?? "The document would not take the set.";
            return this;
        }

        /// <summary>How many elements matched.</summary>
        public int Count => Keys.Count;

        /// <inheritdoc />
        public override string ToString() =>
            Count.ToString(CultureInfo.InvariantCulture) + (Count == 1 ? " element" : " elements")
            + (IsLive ? ", as a live search" : ", fixed");
    }

    /// <summary>
    /// Runs a compiled <see cref="SearchPlan"/> against the host, and publishes it as a native set.
    ///
    /// The compiler above produces disjunctive normal form because that is the only shape the host
    /// can run: one search ANDs its conditions, and the only way to get an OR is more than one
    /// search. This class is where that shape meets the API — union the clauses, intersect the
    /// included sets, and only then subtract, because subtracting first gives a wrong answer
    /// whenever a subtracted element would have been intersected away anyway.
    ///
    /// String comparisons ignore case. The host would honour case if asked; coordination data does
    /// not, and a set that misses "DUCT" because the rule said "Duct" is a defect.
    /// </summary>
    public sealed class NavSearch
    {
        private readonly NavDocument _document;

        /// <summary>Create a runner.</summary>
        /// <param name="document">The document to search.</param>
        public NavSearch(NavDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <summary>Run a plan.</summary>
        /// <param name="plan">The compiled expression.</param>
        public SearchOutcome Run(SearchPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var notes = new List<string>(plan.Warnings);
            var result = new ModelItemCollection();

            if (plan.MatchesNothing)
                return new SearchOutcome(Array.Empty<ElementKey>(), result, false, notes);

            if (plan.MatchesEverything)
            {
                foreach (var item in Everything()) result.Add(item);
                return new SearchOutcome(KeysOf(result), result, true, notes);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var clause in plan.Clauses)
            {
                foreach (var item in RunClause(clause, notes))
                {
                    // Union across clauses. Two clauses of a compiled OR routinely overlap, and the
                    // host collection would happily hold the same element twice.
                    var key = NavKeys.Of(item).ToString();
                    if (key.Length > 0 && !seen.Add(key)) continue;

                    result.Add(item);
                }
            }

            var live = plan.Clauses.Count == 1 && IsNative(plan.Clauses[0]);

            if (!live)
                notes.Add("This expression needs more than one host search, so it can only be saved "
                          + "as a fixed selection set — it will not pick up elements added later.");

            return new SearchOutcome(KeysOf(result), result, live, notes);
        }

        /// <summary>
        /// Save a plan as a native set the host shows in its own panel.
        /// </summary>
        /// <param name="name">What to call it.</param>
        /// <param name="plan">The compiled expression.</param>
        public SearchOutcome Publish(string name, SearchPlan plan)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));

            var outcome = Run(plan);

            var set = outcome.IsLive && plan.Clauses.Count == 1
                ? new SelectionSet(NativeSearch(plan.Clauses[0])!)
                : new SelectionSet(outcome.Items);

            set.DisplayName = name;

            // Replace rather than duplicate: publishing the same set twice is what a user does when
            // they change the rule, and two sets with one name is the outcome nobody wants.
            var existing = _document.Sets()
                .FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));

            return Replace(null, set, existing) ? outcome : outcome.Refused(LastProblem);
        }

        /// <summary>
        /// Save a plan as a native set inside a folder, with a note saying what it searches for.
        ///
        /// Navisworks has had folders in the Sets panel forever and no way to make one except by
        /// right-clicking, which is why a generated family of forty sets normally lands as forty
        /// entries in one flat list. A family that arrives already filed is the difference between
        /// a library somebody uses and a list somebody scrolls past.
        ///
        /// The comment matters as much as the folder. A colleague who does not have CamelWorks
        /// installed can select the set in the host's own panel and read exactly what it matches,
        /// which is the difference between a generated set that gets trusted and one that gets
        /// deleted the first time somebody cannot explain it.
        /// </summary>
        /// <param name="folders">The folder path, outermost first. Empty or null for the top.</param>
        /// <param name="name">What to call the set, inside its folder.</param>
        /// <param name="plan">The compiled expression.</param>
        /// <param name="comment">What to write on it, or null for nothing.</param>
        public SearchOutcome PublishInFolder(IReadOnlyList<string>? folders, string name,
                                             SearchPlan plan, string? comment)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));

            var outcome = Run(plan);

            var set = outcome.IsLive && plan.Clauses.Count == 1
                ? new SelectionSet(NativeSearch(plan.Clauses[0])!)
                : new SelectionSet(outcome.Items);

            set.DisplayName = name;

            var parent = EnsureFolder(folders);

            // Replace rather than duplicate, and scoped to the folder: two sets called "Ducts" in
            // two different level folders are two different sets, and treating them as one name
            // would leave the second overwriting the first.
            var existing = Children(parent)
                .OfType<SelectionSet>()
                .FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));

            if (!Replace(parent, set, existing)) return outcome.Refused(LastProblem);

            if (!string.IsNullOrWhiteSpace(comment))
            {
                // AddCopy copies, so the object we just built is not the one in the document. The
                // comment has to go on the copy, which is the last thing in the parent now.
                var added = Children(parent).OfType<SelectionSet>().LastOrDefault(
                    s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                if (added != null)
                {
                    try
                    {
                        _document.Document.SelectionSets.AddComment(
                            added, new Comment(comment, CommentStatus.New));
                    }
                    catch (Exception)
                    {
                        // A set that published without its note is still the set. Losing the whole
                        // publish because the comment would not attach is the worse trade.
                    }
                }
            }

            return outcome;
        }

        /// <summary>
        /// Find or make the folder chain, and hand back the group to add into.
        ///
        /// Matched case-insensitively against what is already there, so re-running a family with
        /// one value changed fills the existing folders rather than making "Level 01" beside
        /// "LEVEL 01".
        /// </summary>
        /// <param name="folders">The path, outermost first.</param>
        /// <returns>The group to add into. The root when the path is empty or cannot be made.</returns>
        public GroupItem EnsureFolder(IReadOnlyList<string>? folders)
        {
            GroupItem parent = _document.Document.SelectionSets.RootItem;

            if (folders == null) return parent;

            foreach (var wanted in folders)
            {
                if (string.IsNullOrWhiteSpace(wanted)) continue;

                var name = wanted.Trim();

                var found = Children(parent).OfType<FolderItem>().FirstOrDefault(
                    f => string.Equals(f.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    _document.Document.SelectionSets.AddCopy(parent, new FolderItem { DisplayName = name });

                    found = Children(parent).OfType<FolderItem>().LastOrDefault(
                        f => string.Equals(f.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                    // The host refused the folder. Better one flat set at the level above than an
                    // exception that loses the rest of the family.
                    if (found == null) return parent;
                }

                parent = found;
            }

            return parent;
        }

        /// <summary>The children of a group, as a list, and empty rather than null.</summary>
        private static IReadOnlyList<SavedItem> Children(GroupItem? group)
        {
            var items = new List<SavedItem>();

            if (group?.Children == null) return items;

            foreach (SavedItem child in group.Children) items.Add(child);

            return items;
        }

        /// <summary>
        /// Publish a fixed list of elements as a host selection set.
        ///
        /// The other Publish takes a rule and prefers a live search, because a set that re-runs
        /// keeps up with the model. This one is for elements that arrived by some other route — a
        /// graph's working set, a hand selection — where there is no rule to save and pretending
        /// there is one would produce a set that quietly means something different tomorrow.
        /// </summary>
        /// <param name="name">The set's name. An existing set of that name is replaced.</param>
        /// <param name="keys">The elements.</param>
        /// <returns>How many of them still resolved.</returns>
        public int PublishKeys(string name, IReadOnlyList<ElementKey> keys)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            var items = new ModelItemCollection();

            foreach (var key in keys)
            {
                if (_document.Resolve(key) is NavModelItem resolved) items.Add(resolved.Item);
            }

            var set = new SelectionSet(items) { DisplayName = name };

            var existing = _document.Sets()
                .FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));

            return Replace(null, set, existing) ? items.Count : 0;
        }

        /// <summary>
        /// Put a set into the document, replacing one of the same name.
        ///
        /// The new set is added before the old one is removed. The other order destroys a set when a
        /// write is refused: the document takes the removal, refuses the copy, and the set that was
        /// there is gone — which is what a read-only document does, and what a full disk does at the
        /// moment of the save. Adding first means the worst outcome is two sets with one name.
        /// </summary>
        /// <param name="parent">The folder, or null for the top level.</param>
        /// <param name="set">The set to put in.</param>
        /// <param name="existing">The one it replaces, or null.</param>
        private bool Replace(SavedItem? parent, SelectionSet set, SavedItem? existing)
        {
            _problem = null;

            try
            {
                if (parent == null) _document.Document.SelectionSets.AddCopy(set);
                else _document.Document.SelectionSets.AddCopy((GroupItem)parent, set);
            }
            catch (Exception error)
            {
                _problem = Explain(error);
                return false;
            }

            if (existing == null) return true;

            try
            {
                if (parent == null) _document.Document.SelectionSets.Remove(existing);
                else _document.Document.SelectionSets.Remove((GroupItem)parent, existing);
            }
            catch (Exception)
            {
                // The new set is in. Failing to take the old one out leaves two of one name,
                // which is untidy and visible - not the silent loss the other order produces.
            }

            return true;
        }

        private string? _problem;

        /// <summary>
        /// Why the document turned the last set away, or null when it took it.
        ///
        /// Selection sets live in the document, so a published NWD refuses every one. A tool that
        /// reported "published 40 sets" into a file holding none of them is the failure this
        /// exists to prevent.
        /// </summary>
        public string? LastProblem => _problem;

        private string Explain(Exception error)
        {
            var said = (error.Message ?? string.Empty).Trim();

            const string Head = "This document would not take the set. A published NWD is a "
                                + "snapshot: selection sets live in the document, so they cannot "
                                + "be written into one. Open the NWF the models were federated in.";

            return said.Length == 0 ? Head : Head + " The host said: " + said;
        }

        private IEnumerable<ModelItem> RunClause(SearchClause clause, ICollection<string> notes)
        {
            ModelItemCollection working;

            if (clause.Include.Count > 0)
            {
                working = Execute(Build(clause.Include));
            }
            else if (clause.IncludeSets.Count > 0)
            {
                working = new ModelItemCollection();
                foreach (var item in ItemsOfSet(clause.IncludeSets[0])) working.Add(item);
            }
            else
            {
                notes.Add("One part of this expression starts from every element in the model, "
                          + "because it only says what to leave out. On a large federation that is slow.");

                working = new ModelItemCollection();
                foreach (var item in Everything()) working.Add(item);
            }

            // Keyed once per item and kept. The working set was keyed to build the list, keyed
            // again to test membership at the end, and keyed a third and fourth time for every
            // set clause: four passes of the key ladder over the same items.
            var keyed = new Dictionary<ModelItem, string>();

            string KeyOf(ModelItem item)
            {
                if (keyed.TryGetValue(item, out var known)) return known;

                var made = NavKeys.Of(item).ToString();

                keyed[item] = made;

                return made;
            }

            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelItem item in working) keep.Add(KeyOf(item));

            // Intersections first. See the type remarks for why this cannot be reordered.
            var skipFirstSet = clause.Include.Count == 0;

            for (var i = skipFirstSet ? 1 : 0; i < clause.IncludeSets.Count; i++)
            {
                var members = new HashSet<string>(
                    ItemsOfSet(clause.IncludeSets[i]).Select(KeyOf), StringComparer.Ordinal);

                keep.IntersectWith(members);
            }

            foreach (var condition in clause.Exclude)
                foreach (var item in Execute(Build(new[] { condition })))
                    keep.Remove(KeyOf(item));

            foreach (var name in clause.ExcludeSets)
                foreach (var item in ItemsOfSet(name))
                    keep.Remove(KeyOf(item));

            foreach (ModelItem item in working)
                if (keep.Contains(KeyOf(item)))
                    yield return item;
        }

        private ModelItemCollection Execute(Search search)
        {
            try
            {
                return search.FindAll(_document.Document, false);
            }
            catch (Exception)
            {
                // A condition the host rejects at run time — a property category that does not
                // exist in this federation, most often. An empty result is the right answer.
                return new ModelItemCollection();
            }
        }

        private Search Build(IEnumerable<SetCondition> conditions)
        {
            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;

            foreach (var condition in conditions)
            {
                var native = Translate(condition);
                if (native != null) search.SearchConditions.Add(native);
            }

            return search;
        }

        private Search? NativeSearch(SearchClause clause) =>
            IsNative(clause) ? Build(clause.Include) : null;

        private static bool IsNative(SearchClause clause) =>
            clause.Include.Count > 0 && clause.Exclude.Count == 0
            && clause.IncludeSets.Count == 0 && clause.ExcludeSets.Count == 0;

        /// <summary>
        /// One condition, as the host expresses it.
        ///
        /// The numeric comparisons go through <c>CompareWith</c> with a real double rather than a
        /// display string, because the host compares display strings lexically — which puts "9"
        /// after "10" and makes every width filter quietly wrong.
        /// </summary>
        private static SearchCondition? Translate(SetCondition condition)
        {
            if (condition.Operator == SetOperator.HasCategory)
                return SearchCondition.HasCategoryByDisplayName(condition.Category);

            var property = condition.Property;
            if (string.IsNullOrWhiteSpace(property)) return null;

            var has = SearchCondition.HasPropertyByDisplayName(condition.Category, property!);
            var value = condition.Value ?? string.Empty;

            switch (condition.Operator)
            {
                case SetOperator.Defined:
                    return has;

                case SetOperator.Equals:
                    return has.EqualValue(VariantData.FromDisplayString(value)).IgnoreStringValueCase();

                case SetOperator.Contains:
                    return has.DisplayStringContains(value).IgnoreStringValueCase();

                case SetOperator.StartsWith:
                    return has.DisplayStringWildcard(Escape(value) + "*").IgnoreStringValueCase();

                case SetOperator.EndsWith:
                    return has.DisplayStringWildcard("*" + Escape(value)).IgnoreStringValueCase();

                case SetOperator.WildcardMatch:
                    return has.DisplayStringWildcard(value).IgnoreStringValueCase();

                case SetOperator.GreaterThan:
                    return Numeric(has, condition, SearchConditionComparison.NumericGreaterThan);

                case SetOperator.GreaterThanOrEqual:
                    return Numeric(has, condition, SearchConditionComparison.NumericGreaterThanOrEqual);

                case SetOperator.LessThan:
                    return Numeric(has, condition, SearchConditionComparison.NumericLessThan);

                case SetOperator.LessThanOrEqual:
                    return Numeric(has, condition, SearchConditionComparison.NumericLessThanOrEqual);

                default:
                    return null;
            }
        }

        private static SearchCondition Numeric(SearchCondition has, SetCondition condition,
                                               SearchConditionComparison comparison) =>
            has.CompareWith(comparison, VariantData.FromDouble(condition.NumericValue));

        // The host's wildcard syntax has no escape, so a literal * or ? in a StartsWith value would
        // silently widen the match. Replacing them with ? at least keeps the length right and the
        // match narrow, and it is the only option the API leaves.
        private static string Escape(string value) => value.Replace('*', '?');

        private IEnumerable<ModelItem> Everything() =>
            _document.Traverse(CamelWorks.Core.Abstractions.TraversalScope.WholeDocument)
                     .OfType<NavModelItem>()
                     .Select(i => i.Item);

        private IEnumerable<ModelItem> ItemsOfSet(string name)
        {
            var set = _document.Sets()
                .FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));

            if (set == null) return Enumerable.Empty<ModelItem>();

            try
            {
                return set.Search != null
                    ? set.Search.FindAll(_document.Document, false).Cast<ModelItem>()
                    : set.GetSelectedItems().Cast<ModelItem>();
            }
            catch (Exception)
            {
                return Enumerable.Empty<ModelItem>();
            }
        }

        private static IReadOnlyList<ElementKey> KeysOf(ModelItemCollection items)
        {
            var keys = new List<ElementKey>(items.Count);

            foreach (ModelItem item in items)
            {
                var key = NavKeys.Of(item);
                if (!key.IsEmpty) keys.Add(key);
            }

            return keys;
        }
    }
}
