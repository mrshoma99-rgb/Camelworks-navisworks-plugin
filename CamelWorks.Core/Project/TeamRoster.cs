using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>One person on the job.</summary>
    public sealed class TeamMember
    {
        /// <summary>Name somebody.</summary>
        /// <param name="name">What they are called.</param>
        /// <param name="party">Which organisation they are from.</param>
        /// <param name="email">Where a BCF topic reaches them.</param>
        public TeamMember(string name, string? party = null, string? email = null)
        {
            Name = (name ?? string.Empty).Trim();

            if (Name.Length == 0) throw new ArgumentException("a member needs a name", nameof(name));

            Party = Or(party);
            Email = Or(email);
        }

        /// <summary>What they are called.</summary>
        public string Name { get; }

        /// <summary>Which organisation, or null when nobody has said.</summary>
        public string? Party { get; set; }

        /// <summary>Where a BCF topic reaches them.</summary>
        public string? Email { get; set; }

        /// <summary>Whether they are still on the job.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Anything worth remembering about them, in the author's words.</summary>
        public string? Note { get; set; }

        /// <summary>
        /// What identifies them in a saved assignment.
        ///
        /// Their name, cased down. Not a generated id: a roster is typed by a person, edited by
        /// another, and mailed between machines, and a name is the thing all three of those agree
        /// on. A rename breaks the link, which is the honest outcome - the assignment then reads as
        /// somebody who is no longer on the roster rather than silently following the rename onto
        /// whoever took the row.
        /// </summary>
        public string Id => Name.ToLowerInvariant();

        /// <summary>How they read on a row.</summary>
        public string Describe() => Party == null ? Name : Name + " (" + Party + ")";

        /// <summary>Write them down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("name", Name);

            if (Party != null) json.Set("party", Party);
            if (Email != null) json.Set("email", Email);
            if (Note != null) json.Set("note", Note);
            if (!IsActive) json.Set("active", false);

            return json;
        }

        /// <summary>Read one back, or null when there is no name to read.</summary>
        public static TeamMember? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var name = (json["name"].AsString() ?? string.Empty).Trim();

            if (name.Length == 0) return null;

            return new TeamMember(name, json["party"].AsString(), json["email"].AsString())
            {
                Note = json["note"].AsString(),
                IsActive = json["active"].AsBool(true),
            };
        }

        private static string? Or(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            return value.Length == 0 ? null : value;
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>Who is assigned to what, kept beside the board rather than inside it.</summary>
    public sealed class AssignmentBook
    {
        private readonly Dictionary<string, List<string>> _byGroup =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many groups have somebody on them.</summary>
        public int Count => _byGroup.Count;

        /// <summary>Every group that has somebody on it.</summary>
        public IReadOnlyCollection<string> Groups => _byGroup.Keys;

        /// <summary>Who is on a group, in the order they were put there.</summary>
        /// <param name="groupId">The group.</param>
        public IReadOnlyList<string> For(string? groupId)
        {
            if (groupId == null || !_byGroup.TryGetValue(groupId, out var found))
                return Array.Empty<string>();

            return found;
        }

        /// <summary>
        /// Put people on a group, replacing whoever was there.
        ///
        /// Replacing rather than adding, because the control this comes from is a list of ticks:
        /// unticking somebody has to take them off, and an add-only book would make that
        /// impossible to express.
        /// </summary>
        /// <param name="groupId">The group.</param>
        /// <param name="memberIds">Who, by <see cref="TeamMember.Id"/>.</param>
        public void Assign(string? groupId, IEnumerable<string>? memberIds)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return;

            var id = groupId!.Trim();

            var people = (memberIds ?? Array.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (people.Count == 0) { _byGroup.Remove(id); return; }

            _byGroup[id] = people;
        }

        /// <summary>How many groups a person is on.</summary>
        /// <param name="memberId">Who, by <see cref="TeamMember.Id"/>.</param>
        public int Load(string? memberId)
        {
            if (string.IsNullOrWhiteSpace(memberId)) return 0;

            var id = memberId!.Trim().ToLowerInvariant();

            return _byGroup.Count(g => g.Value.Contains(id, StringComparer.Ordinal));
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            foreach (var pair in _byGroup.OrderBy(p => p.Key, StringComparer.Ordinal))
                json.Set(pair.Key, JsonValue.Array(pair.Value.Select(JsonValue.String)));

            return json;
        }

        /// <summary>Read one back.</summary>
        public static AssignmentBook FromJson(JsonValue? json)
        {
            var book = new AssignmentBook();

            if (json == null) return book;

            foreach (var key in json.Keys)
                book.Assign(key, json[key].Items.Select(i => i.AsString())
                                                .Where(v => v != null)
                                                .Select(v => v!));

            return book;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Measure.Count(Count) + (Count == 1 ? " group assigned" : " groups assigned");
    }

    /// <summary>
    /// The people on the job, so "assigned to" has a vocabulary rather than a text box.
    ///
    /// A party name typed into a box is a party name typed differently next week, and a board
    /// filtered by it quietly splits in two. More than that: an assignment is to a person, and a
    /// person has an email address - which is the difference between a BCF file somebody receives
    /// and one that sits in a folder waiting to be noticed.
    ///
    /// <b>Identified by name, not by a generated id.</b> A roster is typed by one person, edited by
    /// another and mailed between machines; the name is the only thing all three agree on. A rename
    /// breaks the assignment, which is the honest outcome: it then reads as somebody no longer on
    /// the roster rather than silently following the rename onto whoever took the row.
    ///
    /// <b>Nothing has to be set up first.</b> With an empty roster every screen behaves as it did -
    /// a party is whatever somebody types. The roster only ever adds a list to choose from.
    /// </summary>
    public sealed class TeamRoster
    {
        private readonly List<TeamMember> _members = new List<TeamMember>();

        /// <summary>Everybody, in the order they were added.</summary>
        public IList<TeamMember> Members => _members;

        /// <summary>The ones still on the job.</summary>
        public IReadOnlyList<TeamMember> Active => _members.Where(m => m.IsActive).ToList();

        /// <summary>True when nobody has been added.</summary>
        public bool IsEmpty => _members.Count == 0;

        /// <summary>
        /// Every organisation the roster mentions, in order of how many people they have.
        ///
        /// Which is the order a picker wants: the main contractor and the two big trades first,
        /// and the one-person consultant last.
        /// </summary>
        public IReadOnlyList<string> Parties =>
            _members.Where(m => m.IsActive && m.Party != null)
                    .GroupBy(m => m.Party!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Key)
                    .ToList();

        /// <summary>Add somebody and hand them back for editing.</summary>
        /// <param name="name">What they are called.</param>
        /// <param name="party">Which organisation.</param>
        /// <param name="email">Where a topic reaches them.</param>
        public TeamMember Add(string name, string? party = null, string? email = null)
        {
            var member = new TeamMember(name, party, email);

            // A name already on the roster is an edit, not a second person. Two rows with one name
            // would make every assignment ambiguous, since the name is the identity.
            var existing = _members.FirstOrDefault(
                m => string.Equals(m.Id, member.Id, StringComparison.Ordinal));

            if (existing != null)
            {
                existing.Party = member.Party ?? existing.Party;
                existing.Email = member.Email ?? existing.Email;
                existing.IsActive = true;
                return existing;
            }

            _members.Add(member);
            return member;
        }

        /// <summary>Take somebody off. Returns whether there was anybody to take off.</summary>
        /// <param name="memberId">Who, by <see cref="TeamMember.Id"/>.</param>
        public bool Remove(string? memberId)
        {
            var found = Find(memberId);

            return found != null && _members.Remove(found);
        }

        /// <summary>Find somebody by their name, their id or their address.</summary>
        /// <param name="what">A name, an id, or an email address.</param>
        public TeamMember? Find(string? what)
        {
            var text = (what ?? string.Empty).Trim();

            if (text.Length == 0) return null;

            return _members.FirstOrDefault(
                       m => string.Equals(m.Id, text, StringComparison.OrdinalIgnoreCase))
                ?? _members.FirstOrDefault(
                       m => m.Email != null
                            && string.Equals(m.Email, text, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Everybody from one organisation.</summary>
        /// <param name="party">The organisation, or null for the people with none.</param>
        public IReadOnlyList<TeamMember> In(string? party)
        {
            var named = (party ?? string.Empty).Trim();

            if (named.Length == 0)
                return _members.Where(m => m.IsActive && m.Party == null).ToList();

            return _members
                .Where(m => m.IsActive
                            && string.Equals(m.Party, named, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Who a group of work should probably go to.
        ///
        /// The party first, because that is the decision a rule already made. Ordered by how much
        /// each person is already carrying, so the suggestion spreads work rather than piling it on
        /// whoever happens to sort first.
        /// </summary>
        /// <param name="party">The organisation the work belongs to.</param>
        /// <param name="book">What everybody is already carrying, or null.</param>
        public IReadOnlyList<TeamMember> Suggest(string? party, AssignmentBook? book = null)
        {
            var within = In(party);

            if (within.Count == 0) return within;

            return book == null
                ? within
                : within.OrderBy(m => book.Load(m.Id))
                        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>Everybody assigned to a group, in roster order.</summary>
        /// <param name="book">The assignments.</param>
        /// <param name="groupId">The group.</param>
        public IReadOnlyList<TeamMember> On(AssignmentBook? book, string? groupId)
        {
            if (book == null) return Array.Empty<TeamMember>();

            return book.For(groupId)
                       .Select(Find)
                       .Where(m => m != null)
                       .Select(m => m!)
                       .ToList();
        }

        /// <summary>
        /// The names on a group that the roster no longer has.
        ///
        /// Shown rather than swept up: somebody left the job and their work did not, and the row
        /// saying so is how it gets reassigned instead of forgotten.
        /// </summary>
        /// <param name="book">The assignments.</param>
        /// <param name="groupId">The group.</param>
        public IReadOnlyList<string> Strangers(AssignmentBook? book, string? groupId)
        {
            if (book == null) return Array.Empty<string>();

            return book.For(groupId).Where(id => Find(id) == null).ToList();
        }

        /// <summary>Where a topic reaches whoever is on a group.</summary>
        /// <param name="book">The assignments.</param>
        /// <param name="groupId">The group.</param>
        public IReadOnlyList<string> Addresses(AssignmentBook? book, string? groupId) =>
            On(book, groupId).Where(m => m.Email != null).Select(m => m.Email!).ToList();

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (_members.Count == 0)
                return "Nobody on the roster yet, so a party is whatever somebody types.";

            var active = Active.Count;
            var parties = Parties.Count;

            var line = Measure.Count(active) + (active == 1 ? " person" : " people");

            if (parties > 0)
                line += " across " + Measure.Count(parties)
                      + (parties == 1 ? " organisation" : " organisations");

            var gone = _members.Count - active;

            if (gone > 0) line += ", " + Measure.Count(gone) + " no longer on the job";

            var unreachable = Active.Count(m => m.Email == null);

            if (unreachable > 0)
                line += "; " + Measure.Count(unreachable) + " with no address, so a BCF file cannot "
                      + "be sent to them";

            return line + ".";
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() => JsonValue.Array(_members.Select(m => m.ToJson()));

        /// <summary>Read one back.</summary>
        public static TeamRoster FromJson(JsonValue? json)
        {
            var roster = new TeamRoster();

            if (json == null) return roster;

            foreach (var item in json.Items)
            {
                var member = TeamMember.FromJson(item);

                if (member == null) continue;

                // Through Add, so a file with the same name twice in it reads as one person rather
                // than making every assignment to that name ambiguous.
                var added = roster.Add(member.Name, member.Party, member.Email);

                added.Note = member.Note ?? added.Note;
                added.IsActive = member.IsActive;
            }

            return roster;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
