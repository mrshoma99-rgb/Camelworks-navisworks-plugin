using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Findings
{
    /// <summary>One authored remark on a finding.</summary>
    public sealed class FindingComment
    {
        /// <summary>Write one.</summary>
        /// <param name="author">Who wrote it.</param>
        /// <param name="whenTicks">When, as UTC ticks. Zero when the source did not say.</param>
        /// <param name="body">What they wrote.</param>
        public FindingComment(string? author, long whenTicks, string body)
        {
            Author = string.IsNullOrWhiteSpace(author) ? "(unknown)" : author!.Trim();
            WhenTicks = whenTicks;
            Body = body ?? string.Empty;
        }

        /// <summary>Who wrote it.</summary>
        public string Author { get; }

        /// <summary>When, as UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>What they wrote.</summary>
        public string Body { get; }

        /// <summary>Whether this is the same remark as another - same author, same words, same day.</summary>
        /// <param name="other">The other remark.</param>
        public bool Repeats(FindingComment? other) =>
            other != null
            && string.Equals(Author, other.Author, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Body, other.Body, StringComparison.Ordinal);

        /// <inheritdoc />
        public override string ToString() => Author + ": " + Body;
    }

    /// <summary>What produced a finding. Not a category for display — a provenance.</summary>
    public enum FindingSource
    {
        /// <summary>A clash result from the host's clash engine.</summary>
        Clash = 0,

        /// <summary>Raised by hand from a selection — "no access to this valve".</summary>
        Manual = 1,

        /// <summary>A model, set or data health rule.</summary>
        Health = 2,

        /// <summary>A headroom or access check.</summary>
        Headroom = 3,

        /// <summary>An IDS specification the model failed.</summary>
        Ids = 4,

        /// <summary>A set that stopped returning what it used to.</summary>
        SetDrift = 5,

        /// <summary>
        /// A difference between two revisions of the model.
        ///
        /// Added last, and numbered last, deliberately: a finding's identity is hashed from this
        /// member's NAME, so appending one leaves every existing finding's id — and with it every
        /// sign-off anybody has already given — exactly as it was.
        /// </summary>
        Compare = 6,
    }

    /// <summary>
    /// The one record every producer emits.
    ///
    /// This is a simplification, not a feature. Clash results, hand-raised issues, health rule
    /// failures, headroom breaches, IDS failures and set drift were each heading for their own
    /// record, their own status field, their own board columns and their own export path — six
    /// near-identical subsystems, and six places for the same bug. They converge here, which means
    /// the board, the report, the BCF exporter and the journals are each written once.
    ///
    /// <b>Parent and child.</b> One rule failing 400 elements is one thing to decide about and 400
    /// things to look at. Modelling that as 400 findings drowns the board — the exact failure the
    /// whole product exists to fix — and modelling it as one loses the ability to point at an
    /// element. So a rule emits a parent carrying the decision, and children carrying the
    /// locations. Status, assignee and due date live on the parent; children inherit and are never
    /// separately triaged.
    ///
    /// <b>Identity is derived, never allocated.</b> A finding's id is a hash of what makes it that
    /// finding, so the same problem in next week's run is the same id without anyone storing a
    /// mapping. That is what lets status survive a re-run, and it is why the id is not a GUID.
    /// </summary>
    public sealed class Finding
    {
        private readonly List<Finding> _children = new List<Finding>();

        private Finding(string id, FindingSource source, string rule, string title,
                        IReadOnlyList<ElementKey> elements, KeyRung weakestRung,
                        string? discriminator)
        {
            Id = id;
            Source = source;
            Rule = rule;
            Title = title;
            Elements = elements;
            WeakestRung = weakestRung;
            Discriminator = discriminator;
        }

        /// <summary>
        /// What made this finding distinct from another by the same rule on the same elements.
        ///
        /// Kept because the id is derived from it: reading a finding back has to rebuild the id
        /// the same way it was built, and a discriminator that was consumed and forgotten would
        /// make that impossible.
        /// </summary>
        public string? Discriminator { get; }

        /// <summary>Derived identity — stable across runs. See the type remarks.</summary>
        public string Id { get; }

        /// <summary>What produced it.</summary>
        public FindingSource Source { get; }

        /// <summary>The rule or test that produced it, as the user named it.</summary>
        public string Rule { get; }

        /// <summary>
        /// One line, as the board shows it.
        ///
        /// Settable, unlike identity: a later run may word the same finding better, and a
        /// reviewer's tool may retitle it. Neither changes which finding it is.
        /// </summary>
        public string Title { get; set; }

        /// <summary>The elements this finding points at.</summary>
        public IReadOnlyList<ElementKey> Elements { get; }

        /// <summary>
        /// The weakest identity rung any of its elements rests on — the confidence of any match
        /// made on this finding. Carried so the board can filter on it rather than presenting a
        /// rung-3 guess with the same authority as a rung-1 fact.
        /// </summary>
        public KeyRung WeakestRung { get; }

        /// <summary>Children, for a rule that failed many elements. Empty for a leaf.</summary>
        public IReadOnlyList<Finding> Children => _children;

        /// <summary>True when this finding carries children and is therefore the unit of decision.</summary>
        public bool IsParent => _children.Count > 0;

        // --- triage state; on a parent this governs its children too ---

        /// <summary>Where it is in the process.</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>Responsible party id, or free text before a party registry exists.</summary>
        public string? AssignedTo { get; set; }

        /// <summary>Due date as a day number, or null. Not a DateTime: these are dates, not instants,
        /// and a timezone on a due date has caused more bugs than it has ever solved.</summary>
        public int? DueDayNumber { get; set; }

        /// <summary>Priority, from the project's configurable list. Null until somebody sets one.</summary>
        public string? Priority { get; set; }

        /// <summary>The stage it belongs to, when a reviewer's tool said one. No host field holds this.</summary>
        public string? Stage { get; set; }

        /// <summary>The folder or group it was put in, when the producer or a reviewer said one.</summary>
        public string? Group { get; set; }

        /// <summary>Tags, from a reviewer's own tool. Never the labels this product writes itself.</summary>
        public IList<string> Labels { get; } = new List<string>();

        /// <summary>The authored trail, oldest first.</summary>
        public IList<FindingComment> Comments { get; } = new List<FindingComment>();

        // --- where and what ---

        /// <summary>
        /// A second line: the distance, the requirement that failed, the measured value.
        ///
        /// Free text on purpose. Every source has its own handful of numbers, and giving each of
        /// them a field on this record is how one record becomes seven again.
        /// </summary>
        public string? Detail { get; set; }

        /// <summary>
        /// Where to look, in model units, or null when the producer gave no point.
        ///
        /// Carried rather than derived, because deriving it means resolving elements against the
        /// host - which is the slow thing the board must not do to show a row.
        /// </summary>
        public double? X { get; set; }

        /// <inheritdoc cref="X"/>
        public double? Y { get; set; }

        /// <inheritdoc cref="X"/>
        public double? Z { get; set; }

        /// <summary>
        /// The producer's own numbers, by name - distance, overlap, grid reference, level.
        ///
        /// The escape hatch that stops this record growing a field per source. Shown by the board
        /// as columns when a source declares them, and carried through export untouched.
        /// </summary>
        public IDictionary<string, string?> Facts { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // --- provenance and projection ---

        /// <summary>Where it last came from: a check run, or the file a markup arrived in.</summary>
        public string? Origin { get; set; }

        /// <summary>When it was last reported by a run, as UTC ticks.</summary>
        public long LastSeenTicks { get; set; }

        /// <summary>
        /// Whether the latest run still reports it.
        ///
        /// A finding the newest run does not mention is kept, not deleted: it is the record of the
        /// thing having been fixed, an issue may point at it, and a check re-run over a narrower
        /// scope must not silently destroy history. The board hides them by default and can show
        /// them.
        /// </summary>
        public bool IsPresent { get; set; } = true;

        /// <summary>
        /// Whether the host holds this finding's decisions too.
        ///
        /// The database is authoritative and the host is a projection of it. False means the
        /// projection has not landed - a published NWD refusing the write, a source the host has
        /// no field for at all - and <see cref="HostProblem"/> says which. Nothing is lost either
        /// way; this is what the board reads to say so.
        /// </summary>
        public bool HostHasIt { get; set; }

        /// <summary>Why the projection did not land, or null.</summary>
        public string? HostProblem { get; set; }

        /// <summary>
        /// Create a leaf finding.
        /// </summary>
        /// <param name="source">What produced it.</param>
        /// <param name="rule">The rule or test name.</param>
        /// <param name="title">One line for the board.</param>
        /// <param name="elements">The elements it points at. Order does not affect identity.</param>
        /// <param name="discriminator">
        /// Anything else that makes this finding distinct from another by the same rule on the same
        /// elements — a clash cell, a property name, a measured value bucket. Omit when the rule and
        /// elements are already unique.
        /// </param>
        public static Finding Create(
            FindingSource source,
            string rule,
            string title,
            IReadOnlyList<ElementKey> elements,
            string? discriminator = null)
        {
            if (string.IsNullOrWhiteSpace(rule)) throw new ArgumentException("rule is required", nameof(rule));
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            // Element ORDER must not change identity: two producers listing the same pair the other
            // way round are reporting the same thing.
            var ordered = elements.Select(e => e.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray();

            var parts = new List<string?> { source.ToString(), rule, discriminator };
            parts.AddRange(ordered.Select(s => (string?)s));

            var id = Hash.Of(Hash.ComponentWidth, parts.ToArray());
            var weakest = elements.Count == 0 ? KeyRung.InstanceGuid : elements.Max(e => e.Rung);

            return new Finding(id, source, rule, title, elements, weakest, discriminator);
        }

        /// <summary>
        /// Create a parent for a rule that failed many elements. Its identity depends on the rule
        /// and its own elements — NOT on the children, so a rule that fails 401 elements next week
        /// instead of 400 is still the same finding and keeps its sign-off.
        /// </summary>
        public static Finding CreateParent(FindingSource source, string rule, string title, string? discriminator = null) =>
            Create(source, rule, title, Array.Empty<ElementKey>(), discriminator);

        /// <summary>
        /// Add a child. Children inherit the parent's triage state and are never separately
        /// triaged, so this rejects a child that has already been given one.
        /// </summary>
        public Finding AddChild(Finding child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (ReferenceEquals(child, this)) throw new ArgumentException("a finding cannot be its own child", nameof(child));
            if (child.IsParent) throw new ArgumentException("children are leaves; nesting beyond one level makes the board a tree nobody reads", nameof(child));

            if (child.Status != FindingStatus.New || child.AssignedTo != null || child.DueDayNumber != null)
                throw new ArgumentException(
                    "a child carries no triage state of its own — set it on the parent, which is the unit of decision",
                    nameof(child));

            _children.Add(child);
            return this;
        }

        /// <summary>
        /// Every element this finding covers, itself and its children, without duplicates.
        /// What the section box, the isolate and the report image are all built from.
        /// </summary>
        public IReadOnlyList<ElementKey> AllElements()
        {
            var seen = new List<ElementKey>(Elements);
            foreach (var child in _children)
                foreach (var e in child.Elements)
                    if (!seen.Contains(e)) seen.Add(e);
            return seen;
        }

        /// <summary>
        /// Reconcile this finding with another edit of the same finding. Status promotes
        /// (see <see cref="FindingStatusLattice"/>); assignee, due date and priority are
        /// latest-wins, which is why the caller passes which one is later.
        /// </summary>
        public void MergeFrom(Finding other, bool otherIsNewer)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.Id != Id)
                throw new ArgumentException("refusing to merge two different findings", nameof(other));

            Status = FindingStatusLattice.Merge(Status, other.Status);

            if (!otherIsNewer) return;
            if (other.AssignedTo != null) AssignedTo = other.AssignedTo;
            if (other.DueDayNumber != null) DueDayNumber = other.DueDayNumber;
            if (other.Priority != null) Priority = other.Priority;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Id + " [" + Source + "/" + FindingStatusLattice.ToName(Status) + "] " + Title
            + (IsParent ? " (+" + _children.Count.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
    }
}
