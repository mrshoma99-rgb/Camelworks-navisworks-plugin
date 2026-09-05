using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>A test as the document has it now.</summary>
    public sealed class ClashTestRef
    {
        /// <summary>Name it.</summary>
        /// <param name="name">What the test is called today.</param>
        /// <param name="id">The host's own id for it, where the host has one.</param>
        /// <param name="results">How many results it holds.</param>
        public ClashTestRef(string name, string? id = null, int results = 0)
        {
            var trimmed = (id ?? string.Empty).Trim();

            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed test)" : name.Trim();
            Id = trimmed.Length == 0 ? null : trimmed;
            Results = results;
        }

        /// <summary>What it is called today.</summary>
        public string Name { get; }

        /// <summary>The host's own id, or null when the host does not give one.</summary>
        public string? Id { get; }

        /// <summary>How many results it holds.</summary>
        public int Results { get; }

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>One test as a profile remembers it.</summary>
    public sealed class ProfileTest
    {
        internal ProfileTest(string name, string? id, bool chosen = true, long lastExportedTicks = 0)
        {
            Name = name;
            Id = id;
            Chosen = chosen;
            LastExportedTicks = lastExportedTicks;
        }

        /// <summary>What it was called when the profile last saw it.</summary>
        public string Name { get; internal set; }

        /// <summary>The host's id, when there was one.</summary>
        public string? Id { get; }

        /// <summary>Whether it goes in the export.</summary>
        public bool Chosen { get; set; }

        /// <summary>When it was last written into a file, in ticks. Zero for never.</summary>
        public long LastExportedTicks { get; internal set; }

        /// <summary>True when it has never been exported.</summary>
        public bool NeverExported => LastExportedTicks <= 0;

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("name", Name).Set("chosen", Chosen);

            if (Id != null) json.Set("id", Id);
            if (LastExportedTicks > 0) json.Set("exported", LastExportedTicks);

            return json;
        }

        /// <summary>Read one back, or null when there is no name to read.</summary>
        public static ProfileTest? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var name = (json["name"].AsString() ?? string.Empty).Trim();

            if (name.Length == 0) return null;

            return new ProfileTest(name, json["id"].AsString(),
                                   json["chosen"].AsBool(true), json["exported"].AsLong());
        }

        /// <inheritdoc />
        public override string ToString() => Name + (Chosen ? string.Empty : " (not chosen)");
    }

    /// <summary>A test the profile found again under a different name.</summary>
    public sealed class ProfileRename
    {
        internal ProfileRename(string was, string now)
        {
            Was = was; Now = now;
        }

        /// <summary>What the profile called it.</summary>
        public string Was { get; }

        /// <summary>What the document calls it.</summary>
        public string Now { get; }

        /// <inheritdoc />
        public override string ToString() => "\"" + Was + "\" is now \"" + Now + "\"";
    }

    /// <summary>What a profile found when it was held up against the document.</summary>
    public sealed class ProfileMatch
    {
        internal ProfileMatch(IReadOnlyList<ProfileTest> present, IReadOnlyList<ProfileTest> missing,
                              IReadOnlyList<ClashTestRef> arrived, IReadOnlyList<ProfileRename> renamed)
        {
            Present = present; Missing = missing; Arrived = arrived; Renamed = renamed;
        }

        /// <summary>The profile's tests the document still has.</summary>
        public IReadOnlyList<ProfileTest> Present { get; }

        /// <summary>The profile's tests the document no longer has.</summary>
        public IReadOnlyList<ProfileTest> Missing { get; }

        /// <summary>Tests the document has that the profile had never seen.</summary>
        public IReadOnlyList<ClashTestRef> Arrived { get; }

        /// <summary>Tests found again under a different name.</summary>
        public IReadOnlyList<ProfileRename> Renamed { get; }

        /// <summary>True when the document is exactly what the profile remembers.</summary>
        public bool IsQuiet => Missing.Count == 0 && Arrived.Count == 0 && Renamed.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsQuiet)
                return Present.Count == 0
                    ? "This profile has no tests in it yet."
                    : "All " + Measure.Count(Present.Count) + " tests are where the profile left them.";

            var parts = new List<string>();

            if (Renamed.Count > 0) parts.Add(Measure.Count(Renamed.Count) + " renamed");
            if (Arrived.Count > 0) parts.Add(Measure.Count(Arrived.Count) + " new");
            if (Missing.Count > 0) parts.Add(Measure.Count(Missing.Count) + " gone");

            return string.Join(", ", parts) + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>The results a profile picked out, and what it left behind.</summary>
    public sealed class ExportSelection
    {
        internal ExportSelection(IReadOnlyList<ClashItem> items, IReadOnlyList<ClashGroup> groups,
                                 int byTest, int byStatus, int byParty)
        {
            Items = items; Groups = groups; ByTest = byTest; ByStatus = byStatus; ByParty = byParty;
        }

        /// <summary>What goes in the file.</summary>
        public IReadOnlyList<ClashItem> Items { get; }

        /// <summary>The folders those results are in, trimmed to the results going in.</summary>
        public IReadOnlyList<ClashGroup> Groups { get; }

        /// <summary>How many were left out because their test was not chosen.</summary>
        public int ByTest { get; }

        /// <summary>How many were left out by the status filter.</summary>
        public int ByStatus { get; }

        /// <summary>How many were left out by the party filter.</summary>
        public int ByParty { get; }

        /// <summary>How many were left out altogether.</summary>
        public int Left => ByTest + ByStatus + ByParty;

        /// <summary>True when there is something to write.</summary>
        public bool IsUsable => Items.Count > 0;

        /// <summary>The one-line readout, which always says what is not going.</summary>
        public string Explain()
        {
            if (Items.Count == 0)
                return Left == 0
                    ? "Nothing on the board to export."
                    : Left == 1
                        ? "Nothing left after the filters - the one result on the board was left out."
                        : "Nothing left after the filters - all " + Measure.Count(Left)
                          + " results were left out.";

            var line = Measure.Count(Items.Count)
                     + (Items.Count == 1 ? " result" : " results");

            if (Groups.Count > 0)
                line += " in " + Measure.Count(Groups.Count)
                      + (Groups.Count == 1 ? " folder" : " folders");

            if (Left == 0) return line + ", the whole board.";

            var why = new List<string>();

            if (ByTest > 0) why.Add(Measure.Count(ByTest) + " from tests not chosen");
            if (ByStatus > 0) why.Add(Measure.Count(ByStatus) + " at a status left out");
            if (ByParty > 0) why.Add(Measure.Count(ByParty) + " assigned elsewhere");

            return line + "; " + string.Join(", ", why) + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Which of the board goes into the file, saved so the same answer comes out next week.
    ///
    /// The choice is never "all of it". A weekly issue is one discipline's tests at three statuses; a
    /// handover is everything resolved; a sub-contractor gets what is assigned to them. Re-deciding
    /// that every week costs time and makes two consecutive issues cover different things.
    ///
    /// Tests are matched by the host's id first and by name second, so a test that was renamed is
    /// still the test the profile chose. The rename is reported rather than swallowed, because a
    /// name changing usually means a scope changed with it.
    ///
    /// A test the profile never saw goes in. A new test is the one thing a baseline must not miss —
    /// results nobody has looked at yet — so it arrives selected and is named so it can be
    /// deselected deliberately.
    ///
    /// What was left out is always stated. A file that holds a third of the board without saying so
    /// leads to an argument about what was sent.
    /// </summary>
    public sealed class ExportProfile
    {
        private readonly List<ProfileTest> _tests = new List<ProfileTest>();

        /// <summary>What this profile is called.</summary>
        public string Name { get; set; } = "Everything";

        /// <summary>The tests it knows about.</summary>
        public IList<ProfileTest> Tests => _tests;

        /// <summary>Which statuses go in. Empty means all of them.</summary>
        public ISet<FindingStatus> Statuses { get; } = new HashSet<FindingStatus>();

        /// <summary>Which parties' work goes in. Empty means all of them.</summary>
        public ISet<string> Parties { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the folders survive into the file.</summary>
        public bool KeepGroups { get; set; } = true;

        /// <summary>Whether each result carries its two elements.</summary>
        public bool IncludeObjects { get; set; } = true;

        /// <summary>Model units, as the file's header declares them.</summary>
        public string Units { get; set; } = "m";

        /// <summary>The tests that would go in.</summary>
        public IReadOnlyList<ProfileTest> Chosen => _tests.Where(t => t.Chosen).ToList();

        /// <summary>
        /// Hold the profile up against the document, adopting what has changed.
        ///
        /// Adopting rather than reporting-only, because a profile that reports a rename and then
        /// keeps looking for the old name is a profile that reports it again every week.
        /// </summary>
        /// <param name="available">The tests the document has now.</param>
        public ProfileMatch Match(IEnumerable<ClashTestRef>? available)
        {
            var refs = (available ?? Array.Empty<ClashTestRef>()).Where(r => r != null).ToList();

            var byId = new Dictionary<string, ClashTestRef>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, ClashTestRef>(StringComparer.OrdinalIgnoreCase);

            foreach (var one in refs)
            {
                if (one.Id != null && !byId.ContainsKey(one.Id)) byId[one.Id] = one;
                if (!byName.ContainsKey(one.Name)) byName[one.Name] = one;
            }

            var present = new List<ProfileTest>();
            var missing = new List<ProfileTest>();
            var renamed = new List<ProfileRename>();
            var claimed = new HashSet<ClashTestRef>();

            foreach (var test in _tests)
            {
                ClashTestRef? found = null;

                if (test.Id != null && byId.TryGetValue(test.Id, out var known)) found = known;

                // The name is the fallback, not the first answer: two tests may share a name, and
                // an id that matches is a fact where a name that matches is a guess.
                if (found == null && byName.TryGetValue(test.Name, out var named)) found = named;

                if (found == null) { missing.Add(test); continue; }

                if (!string.Equals(found.Name, test.Name, StringComparison.Ordinal))
                {
                    // A difference of case is the document's spelling, not a rename. Reporting it
                    // would put a line in front of somebody every week that says nothing happened.
                    if (!string.Equals(found.Name, test.Name, StringComparison.OrdinalIgnoreCase))
                        renamed.Add(new ProfileRename(test.Name, found.Name));

                    test.Name = found.Name;
                }

                claimed.Add(found);
                present.Add(test);
            }

            var arrived = refs.Where(r => !claimed.Contains(r)).ToList();

            foreach (var one in arrived) _tests.Add(new ProfileTest(one.Name, one.Id));

            return new ProfileMatch(present, missing, arrived, renamed);
        }

        /// <summary>Pick the results out of the board.</summary>
        /// <param name="items">Everything on the board.</param>
        /// <param name="groups">The board's folders, or null when it has none.</param>
        public ExportSelection Choose(IEnumerable<ClashItem>? items,
                                      IEnumerable<ClashGroup>? groups = null)
        {
            var board = (items ?? Array.Empty<ClashItem>()).Where(i => i != null).ToList();

            var wanted = new HashSet<string>(
                _tests.Where(t => t.Chosen).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            // A profile with no tests in it has not been held up against a document yet, and
            // refusing everything would read as "the board is empty" rather than "say what to send".
            var everyTest = _tests.Count == 0;

            var folders = (groups ?? Array.Empty<ClashGroup>()).Where(g => g != null).ToList();

            var partyOf = new Dictionary<ClashKey, string?>();

            foreach (var group in folders)
                foreach (var member in group.Items)
                    if (member != null) partyOf[member.Key] = group.AssignedTo;

            var keeping = new List<ClashItem>();
            var byTest = 0;
            var byStatus = 0;
            var byParty = 0;

            foreach (var item in board)
            {
                if (!everyTest && !wanted.Contains(item.TestName)) { byTest++; continue; }

                if (Statuses.Count > 0 && !Statuses.Contains(item.Status)) { byStatus++; continue; }

                if (Parties.Count > 0)
                {
                    partyOf.TryGetValue(item.Key, out var party);

                    // An unassigned result is not "somebody else's" - it is nobody's, and leaving it
                    // out of every party's file is how it never reaches anyone.
                    if (party != null && !Parties.Contains(party)) { byParty++; continue; }
                }

                keeping.Add(item);
            }

            var kept = new HashSet<ClashKey>(keeping.Select(i => i.Key));
            var trimmed = new List<ClashGroup>();

            foreach (var group in folders)
            {
                var survivors = group.Items.Where(m => m != null && kept.Contains(m.Key)).ToList();

                if (survivors.Count == 0) continue;

                // A fresh group rather than the board's own, because trimming the board's would
                // change what the board shows as a side effect of writing a file.
                var copy = new ClashGroup(group.Id, group.Name)
                {
                    AssignedTo = group.AssignedTo,
                    Priority = group.Priority,
                    IsPinned = group.IsPinned,
                };

                foreach (var member in survivors) copy.Items.Add(member);

                trimmed.Add(copy);
            }

            return new ExportSelection(keeping, trimmed, byTest, byStatus, byParty);
        }

        /// <summary>The writer's settings, from the profile's.</summary>
        /// <param name="sourceFile">Which document this came from.</param>
        /// <param name="whenTicks">When the file is being written.</param>
        public ClashXmlOptions Options(string? sourceFile = null, long whenTicks = 0) =>
            new ClashXmlOptions
            {
                Units = Units,
                KeepGroups = KeepGroups,
                IncludeObjects = IncludeObjects,
                SourceFile = sourceFile,
                WhenTicks = whenTicks,
            };

        /// <summary>
        /// The same profile, for the BCF writer.
        ///
        /// Two option types rather than one because the formats differ in what they can be told:
        /// BCF has no units declaration (its cameras are metres by definition) and no flag for
        /// whether each result carries its elements' properties (its components carry ids, not
        /// properties). What both share - which groups travel, where the file came from, when -
        /// is what a profile is actually about, and it is carried across unchanged.
        /// </summary>
        /// <param name="sourceFile">The document this came from.</param>
        /// <param name="whenTicks">When the file is being written.</param>
        /// <param name="metresPerUnit">Model units to metres, for the viewpoint cameras.</param>
        public ClashBcfOptions BcfOptions(string? sourceFile = null, long whenTicks = 0,
                                          double metresPerUnit = 1.0) =>
            new ClashBcfOptions
            {
                KeepGroups = KeepGroups,
                SourceFile = sourceFile,
                WhenTicks = whenTicks,
                MetresPerUnit = metresPerUnit <= 0 ? 1.0 : metresPerUnit,
            };

        /// <summary>
        /// Record that these tests have just been written into a file.
        ///
        /// The column that turns a list of tests into a work list: a coordinator opening this on a
        /// Friday wants to see which tests went out last Friday and which have never gone at all.
        /// </summary>
        /// <param name="names">The tests that were written.</param>
        /// <param name="whenTicks">When.</param>
        public int Stamp(IEnumerable<string>? names, long whenTicks)
        {
            if (names == null || whenTicks <= 0) return 0;

            var written = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var stamped = 0;

            foreach (var test in _tests)
            {
                if (!written.Contains(test.Name)) continue;

                test.LastExportedTicks = whenTicks;
                stamped++;
            }

            return stamped;
        }

        /// <summary>Add a test by hand, for a profile being built before a document is open.</summary>
        /// <param name="name">The test's name.</param>
        /// <param name="id">The host's id, when known.</param>
        public ProfileTest Add(string name, string? id = null)
        {
            var stem = (id ?? string.Empty).Trim();

            var test = new ProfileTest(string.IsNullOrWhiteSpace(name) ? "(unnamed test)" : name.Trim(),
                                       stem.Length == 0 ? null : stem);
            _tests.Add(test);
            return test;
        }

        /// <summary>Write it for the project file.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("name", Name)
                .Set("units", Units)
                .Set("keepGroups", KeepGroups)
                .Set("includeObjects", IncludeObjects)
                .Set("tests", JsonValue.Array(_tests.Select(t => t.ToJson())))
                .Set("statuses", JsonValue.Array(
                    Statuses.OrderBy(s => (int)s).Select(s => JsonValue.String(s.ToString()))))
                .Set("parties", JsonValue.Array(
                    Parties.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(JsonValue.String)));

        /// <summary>Read one back.</summary>
        public static ExportProfile FromJson(JsonValue? json)
        {
            var profile = new ExportProfile();

            if (json == null) return profile;

            profile.Name = json["name"].AsString("Everything") ?? "Everything";
            profile.Units = json["units"].AsString("m") ?? "m";
            profile.KeepGroups = json["keepGroups"].AsBool(true);
            profile.IncludeObjects = json["includeObjects"].AsBool(true);

            foreach (var item in json["tests"].Items)
            {
                var test = ProfileTest.FromJson(item);
                if (test != null) profile._tests.Add(test);
            }

            foreach (var item in json["statuses"].Items)
            {
                var word = item.AsString();

                // An unrecognised status is dropped rather than defaulted to New: a filter that
                // silently grows to include something is worse than one that lost a line.
                if (word != null && Enum.TryParse<FindingStatus>(word, true, out var status))
                    profile.Statuses.Add(status);
            }

            foreach (var item in json["parties"].Items)
            {
                var party = (item.AsString() ?? string.Empty).Trim();
                if (party.Length > 0) profile.Parties.Add(party);
            }

            return profile;
        }

        /// <summary>When a test last went out, as a list prints it.</summary>
        /// <param name="test">The test.</param>
        public static string When(ProfileTest? test)
        {
            if (test == null || test.NeverExported) return "never";

            try
            {
                return new DateTime(test.LastExportedTicks, DateTimeKind.Utc).ToLocalTime()
                    .ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "never";
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " (" + Measure.Count(Chosen.Count) + " of " + Measure.Count(_tests.Count) + " tests)";
    }
}
