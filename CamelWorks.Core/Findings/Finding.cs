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

        /// <summary>
        /// Whether the host already carries this remark.
        ///
        /// A finding's trail holds both what the host read out and what somebody wrote here while
        /// the host would not take it. Without this the projection would write the whole trail
        /// back and double every conversation it had just read.
        /// </summary>
        public bool InHost { get; set; }

        /// <summary>Whether this is the same remark as another - same author, same words, same day.</summary>
        /// <param name="other">The other remark.</param>
        public bool Repeats(FindingComment? other) =>
            other != null
            && string.Equals(Author, other.Author, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Body, other.Body, StringComparison.Ordinal);

        /// <inheritdoc />
        public override string ToString() => Author + ": " + Body;
    }

    /// <summary>
    /// Which check produced a finding. A string, so the list of checks is open.
    ///
    /// Constants rather than an enum. A finding's id is hashed from this value, so the text of each
    /// constant is a persisted format: renaming one re-derives every id under it and detaches every
    /// status, assignee, comment and issue attachment already recorded against them. The values are
    /// the names the enum this replaced was written and hashed by, so no finding is re-identified.
    /// </summary>
    public static class FindingSource
    {
        /// <summary>A clash result from the host's clash engine.</summary>
        public const string Clash = "Clash";

        /// <summary>Raised by hand from a selection — "no access to this valve".</summary>
        public const string Manual = "Manual";

        /// <summary>A model, set or data health rule.</summary>
        public const string Health = "Health";

        /// <summary>A headroom or access check.</summary>
        public const string Headroom = "Headroom";

        /// <summary>An IDS specification the model failed.</summary>
        public const string Ids = "Ids";

        /// <summary>A set that stopped returning what it used to.</summary>
        public const string SetDrift = "SetDrift";

        /// <summary>A difference between two revisions of the model.</summary>
        public const string Compare = "Compare";

        /// <summary>A place somebody could fall from.</summary>
        public const string FallHazard = "FallHazard";

        /// <summary>
        /// The sources this build knows. A finding written by a check this build does not have is
        /// read and drawn by its own key rather than refused, so this list is not a gate.
        /// </summary>
        public static IReadOnlyList<string> Known { get; } = new[]
        {
            Clash, Manual, Health, Headroom, Ids, SetDrift, Compare, FallHazard,
        };
    }

    /// <summary>
    /// The names <see cref="Finding.Pending"/> uses. Constants rather than an enum, because they
    /// are written to the project file and an enum's numbering is not a promise.
    /// </summary>
    public static class FindingField
    {
        /// <summary>Where it is in the process.</summary>
        public const string Status = "status";

        /// <summary>Who it was given to.</summary>
        public const string AssignedTo = "assignee";

        /// <summary>The folder or group it was put in.</summary>
        public const string Group = "group";

        /// <summary>The name it was given.</summary>
        public const string Title = "name";

        /// <summary>The remarks written on it.</summary>
        public const string Comments = "comments";
    }

    /// <summary>
    /// The one record every producer emits.
    ///
    /// This is a simplification rather than a feature. Clash results, hand-raised issues, health
    /// rule failures, headroom breaches, IDS failures and set drift were each heading for their own
    /// record, status field, board columns and export path — six near-identical subsystems and six
    /// places for the same bug. They converge here, so the board, the report, the BCF exporter and
    /// the journals are each written once.
    ///
    /// A finding is the unit of decision; a location is one of the places it is. One rule failing
    /// 400 elements is one thing to decide about and 400 things to look at. Modelling that as 400
    /// findings floods the board and re-identifies a row whenever its element moves or is renamed;
    /// modelling it as one finding with no locations loses the ability to point at an element. So a
    /// rule emits one finding carrying the decision and a location per element. Status, assignee
    /// and due date live on the finding, and a location never carries any of them — see
    /// <see cref="FindingLocation"/>.
    ///
    /// Identity is derived, never allocated. A finding's id is a hash of what makes it that finding,
    /// so the same problem in next week's run has the same id with no stored mapping. That is what
    /// lets status survive a re-run, and why the id is not a GUID.
    /// </summary>
    public sealed class Finding
    {
        private readonly List<FindingLocation> _locations = new List<FindingLocation>();

        private Finding(string id, string source, string rule, string title,
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

        /// <summary>Which check produced it. One of <see cref="FindingSource"/>, or a key this build
        /// does not know, which is read through rather than refused.</summary>
        public string Source { get; }

        /// <summary>The rule or test that produced it, as the user named it.</summary>
        public string Rule { get; private set; }

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

        /// <summary>The places this finding is, for a rule that failed many elements. Empty when the
        /// finding's own elements are the whole of it.</summary>
        public IReadOnlyList<FindingLocation> Locations => _locations;

        /// <summary>True when this finding carries locations.</summary>
        public bool HasLocations => _locations.Count > 0;

        // --- triage state; the locations carry none of their own ---

        /// <summary>Where it is in the process.</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>Responsible party id, or free text before a party registry exists.</summary>
        public string? AssignedTo { get; set; }

        /// <summary>Due date as a day number, or null. Not a DateTime: these are dates, not instants,
        /// and a timezone on a due date has caused more bugs than it has ever solved.</summary>
        public int? DueDayNumber { get; set; }

        /// <summary>The due date as a date, or null.</summary>
        public DateTime? Due =>
            DueDayNumber != null ? DateOf(DueDayNumber.Value) : (DateTime?)null;

        /// <summary>Whether it is past its due date.</summary>
        /// <param name="today">Today, as a day number.</param>
        public bool IsOverdue(int today) => DueDayNumber != null && DueDayNumber.Value < today;

        /// <summary>
        /// A date as a day number - whole days since the calendar's first, in no timezone.
        ///
        /// The date's own <see cref="DateTime.Date"/> is taken and its kind discarded, because a
        /// due date is a day somebody named and not an instant anybody observed.
        /// </summary>
        /// <param name="when">The date.</param>
        public static int DayNumberFor(DateTime when) =>
            (int)(when.Date - DateTime.MinValue).TotalDays;

        /// <summary>A day number back as a date, at midnight, unspecified kind.</summary>
        /// <param name="dayNumber">The day number.</param>
        public static DateTime DateOf(int dayNumber) => DateTime.MinValue.AddDays(dayNumber);

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

        /// <summary>
        /// The BCF topic this finding exports as, when a producer derived one.
        ///
        /// Kept rather than recomputed because it is how a file coming back from somebody else's
        /// tool finds its way home: the markup carries the topic, not the elements this model
        /// knows. Not part of identity - it is derived FROM identity, and a finding whose topic
        /// is unset is still perfectly itself.
        /// </summary>
        public string? TopicGuid { get; set; }

        /// <summary>
        /// Which of this finding's fields the host has not taken.
        ///
        /// A set rather than a flag. A published NWD refuses every clash edit; an NWF takes most and
        /// refuses the occasional one. Recording only that the host is behind, without recording on
        /// what, would make the sync either write everything again — doubling comments — or guess.
        /// So each waiting field names itself, the sync offers exactly those, and a field that lands
        /// stops waiting.
        /// </summary>
        public ISet<string> Pending { get; } = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Whether anything on this finding is waiting for the host.</summary>
        public bool IsHeld => Pending.Count > 0;

        /// <summary>Mark a field as not yet in the host.</summary>
        /// <param name="field">One of <see cref="FindingField"/>.</param>
        public void Hold(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return;

            Pending.Add(field);
            HostHasIt = false;
        }

        /// <summary>
        /// The host took this field, so it is no longer waiting.
        ///
        /// One field at a time, because a sync that wrote three of four and cleared all four
        /// would lose the fourth. The finding counts as projected only once nothing is left.
        /// </summary>
        /// <param name="field">One of <see cref="FindingField"/>.</param>
        public void Landed(string? field)
        {
            if (field == null) return;

            Pending.Remove(field);

            // A remark that reached the host and stayed flagged unwritten would be sent again on
            // the next sync, and the conversation would double.
            if (field == FindingField.Comments)
                foreach (var comment in Comments) comment.InHost = true;

            if (Pending.Count > 0) return;

            HostHasIt = true;
            HostProblem = null;
        }

        /// <summary>The host took everything that was waiting, so nothing is.</summary>
        public void Released()
        {
            foreach (var field in Pending.ToList()) Landed(field);

            HostHasIt = true;
            HostProblem = null;

            foreach (var comment in Comments) comment.InHost = true;
        }

        /// <summary>
        /// What is waiting, in a phrase - short, because it sits in a table cell.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (Pending.Contains(FindingField.Status))
                parts.Add("status " + FindingStatusLattice.ToName(Status));

            if (Pending.Contains(FindingField.AssignedTo))
                parts.Add(string.IsNullOrEmpty(AssignedTo) ? "unassigned" : "to " + AssignedTo);

            if (Pending.Contains(FindingField.Group))
                parts.Add("group " + (Group ?? string.Empty));

            if (Pending.Contains(FindingField.Title)) parts.Add("named " + Title);

            if (Pending.Contains(FindingField.Comments))
            {
                var waiting = Comments.Count(c => !c.InHost);

                if (waiting > 0)
                    parts.Add(waiting.ToString(CultureInfo.InvariantCulture)
                              + (waiting == 1 ? " comment" : " comments"));
            }

            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

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
        /// <param name="identity">
        /// What stands for the rule in the identity, when the rule's own text is not stable.
        ///
        /// A clash test's rule is its name, because that is what a person reads on the board and
        /// in a BCF label — and renaming a test is a first-class feature of this product. Hashing
        /// the name meant a rename gave every finding under it a new identity, silently detaching
        /// its status, its assignee, its due date and its whole comment trail. The host's own id
        /// for the test goes here instead; the name stays on <see cref="Rule"/>, where it is read.
        /// </param>
        public static Finding Create(
            string source,
            string rule,
            string title,
            IReadOnlyList<ElementKey> elements,
            string? discriminator = null,
            string? identity = null)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("source is required", nameof(source));
            if (string.IsNullOrWhiteSpace(rule)) throw new ArgumentException("rule is required", nameof(rule));
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            // Element ORDER must not change identity: two producers listing the same pair the other
            // way round are reporting the same thing.
            var ordered = elements.Select(e => e.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray();

            var named = string.IsNullOrWhiteSpace(identity) ? rule : identity!.Trim();

            // Trimmed for the same reason identity is. " Clash" would otherwise hash as a source of
            // its own and miss the ordinal clash-identity branch, which is the re-identification 2.6
            // names arriving through padding rather than through case.
            var from = source.Trim();

            var parts = new List<string?> { from, named, discriminator };
            parts.AddRange(ordered.Select(s => (string?)s));

            var id = Hash.Of(Hash.ComponentWidth, parts.ToArray());
            var weakest = elements.Count == 0 ? KeyRung.InstanceGuid : elements.Max(e => e.Rung);

            return new Finding(id, from, rule, title, elements, weakest, discriminator);
        }

        /// <summary>
        /// Create a finding for a rule that failed many elements, to be given a location each. Its
        /// identity depends on the rule and the discriminator — NOT on the locations, so a rule that
        /// fails 401 elements next week instead of 400 is still the same finding and keeps its
        /// sign-off.
        /// </summary>
        /// <param name="source">What produced it.</param>
        /// <param name="rule">The rule or test name.</param>
        /// <param name="title">One line for the board.</param>
        /// <param name="discriminator">What makes it distinct from another finding by the same rule.</param>
        public static Finding CreateParent(string source, string rule, string title, string? discriminator = null) =>
            Create(source, rule, title, Array.Empty<ElementKey>(), discriminator);

        /// <summary>Add a place this finding is.</summary>
        /// <param name="location">The place.</param>
        public Finding AddLocation(FindingLocation location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));

            _locations.Add(location);
            return this;
        }

        /// <summary>
        /// Take this run's locations in place of the last run's.
        ///
        /// Keeping the old ones is not safe: a rule that failed four hundred elements last week and
        /// four hundred and one today would still show last week's four hundred, so "show me all of
        /// them" would point at a stale set and miss the new one.
        ///
        /// Triage is not lost, because a location carries none. Notes are: a location the run no
        /// longer reports takes its notes with it. Notes on an id the run still reports are carried
        /// across by <see cref="FindingRegister.Observe"/>, which is where the outgoing locations
        /// are still in hand; this method has only the incoming ones and does not carry anything.
        /// </summary>
        /// <param name="locations">This run's locations. Null or empty leaves none.</param>
        public void ReplaceLocations(IEnumerable<FindingLocation>? locations)
        {
            _locations.Clear();

            foreach (var location in locations ?? Array.Empty<FindingLocation>())
                if (location != null) _locations.Add(location);
        }

        /// <summary>
        /// Every element this finding covers, its own and its locations', without duplicates.
        /// What the section box, the isolate and the report image are all built from.
        /// </summary>
        public IReadOnlyList<ElementKey> AllElements()
        {
            var seen = new List<ElementKey>(Elements);
            foreach (var location in _locations)
                foreach (var e in location.Elements)
                    if (!seen.Contains(e)) seen.Add(e);
            return seen;
        }

        /// <summary>
        /// Reconcile this finding with another edit of the same finding. Status promotes
        /// (see <see cref="FindingStatusLattice"/>); assignee, due date and priority are
        /// latest-wins, which is why the caller passes which one is later.
        ///
        /// Fields in <see cref="Pending"/> are the exception. A pending field is one the host has
        /// not taken, so the host still reports the value it had before the change. Letting the
        /// newer read win there would revert the edit on every press of "Read the results", which is
        /// why the edit is held.
        /// </summary>
        public void MergeFrom(Finding other, bool otherIsNewer)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.Id != Id)
                throw new ArgumentException("refusing to merge two different findings", nameof(other));

            if (!Pending.Contains(FindingField.Status))
                Status = FindingStatusLattice.Merge(Status, other.Status);

            if (!otherIsNewer) return;

            // The rule is what a person reads — a clash test's name, and renaming one is a
            // first-class feature. Identity no longer depends on it, so the newest read is what
            // the board should show; keeping the first one meant a renamed test kept its old name
            // on every screen until somebody deleted the register.
            if (!string.IsNullOrWhiteSpace(other.Rule)) Rule = other.Rule;

            if (other.AssignedTo != null && !Pending.Contains(FindingField.AssignedTo))
                AssignedTo = other.AssignedTo;

            if (other.Group != null && !Pending.Contains(FindingField.Group))
                Group = other.Group;

            if (other.DueDayNumber != null) DueDayNumber = other.DueDayNumber;
            if (other.Priority != null) Priority = other.Priority;
            if (other.Stage != null) Stage = other.Stage;
            if (other.TopicGuid != null) TopicGuid = other.TopicGuid;

            foreach (var label in other.Labels)
                if (!Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    Labels.Add(label);
        }

        /// <inheritdoc />
        public override string ToString() =>
            Id + " [" + Source + "/" + FindingStatusLattice.ToName(Status) + "] " + Title
            + (HasLocations ? " (+" + _locations.Count.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
    }
}
