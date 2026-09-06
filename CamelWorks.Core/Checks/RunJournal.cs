using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// What one run's findings stood at when it last ran: a frozen reference for the next
    /// comparison.
    ///
    /// The journal does not compute the delta. Matching one run against the previous one is per
    /// check, through <see cref="ICheck.Aliases"/>, and regression is computed at intake by
    /// <see cref="FindingIntake.Reopened"/>. What is left is the one job a journal is good for:
    /// holding still, so a comparison does not shift under a colleague saving mid-read, and
    /// recording who the open findings were with at that moment, which cannot be reconstructed
    /// from live state afterwards.
    ///
    /// One journal per check and run, and two of them kept — the same one-undo-slot shape the clash
    /// board already has. Retention is stated rather than left open because a journal is a full id
    /// list: twelve runs of eighteen thousand results at two journals each is the largest thing in
    /// the project file, and an unbounded history would double it on every run.
    ///
    /// A run with no journal is a first run. Nothing is seeded from anything else, because there is
    /// no function from a snapshot back to a finding id, and a seed invented for one would report a
    /// first run as either wholly new or wholly unchanged.
    /// </summary>
    public sealed class RunJournal
    {
        /// <summary>The project-file section this shares with <see cref="CheckRunStore"/>.</summary>
        public const string Section = "checks";

        /// <summary>Where the journals sit inside that section.</summary>
        public const string Key = "journals";

        /// <summary>What the current journal is called inside a slot.</summary>
        public const string CurrentKey = "current";

        /// <summary>What the one kept before it is called inside a slot.</summary>
        public const string PreviousKey = "previous";

        /// <summary>Record what a run stood at.</summary>
        /// <param name="checkKey">Which check, as <see cref="ICheck.Key"/>.</param>
        /// <param name="runId">Which run.</param>
        /// <param name="whenTicks">When it ran, as UTC ticks.</param>
        /// <param name="statuses">Each finding id and the status it held.</param>
        /// <param name="openByParty">How many open findings each party held.</param>
        public RunJournal(string checkKey, string runId, long whenTicks,
                          IReadOnlyDictionary<string, FindingStatus>? statuses,
                          IReadOnlyDictionary<string, int>? openByParty)
        {
            if (string.IsNullOrWhiteSpace(checkKey))
                throw new ArgumentException("checkKey is required", nameof(checkKey));
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("runId is required", nameof(runId));

            CheckKey = checkKey;
            RunId = runId;
            WhenTicks = whenTicks;
            Statuses = statuses ?? new Dictionary<string, FindingStatus>(StringComparer.Ordinal);
            OpenByParty = openByParty ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Which check.</summary>
        public string CheckKey { get; }

        /// <summary>Which run.</summary>
        public string RunId { get; }

        /// <summary>When it ran, as UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>Each finding id and the status it held at that moment.</summary>
        public IReadOnlyDictionary<string, FindingStatus> Statuses { get; }

        /// <summary>
        /// How many open findings each party held at that moment, kept rather than re-derived: a
        /// past run's parties cannot be worked out from what the register holds now.
        /// </summary>
        public IReadOnlyDictionary<string, int> OpenByParty { get; }

        /// <summary>How many findings it recorded.</summary>
        public int Count => Statuses.Count;

        /// <summary>True when it holds the id.</summary>
        /// <param name="findingId">The finding id.</param>
        public bool Holds(string? findingId) => findingId != null && Statuses.ContainsKey(findingId);

        /// <summary>
        /// The slot key one check's run is journalled under.
        ///
        /// Both halves are escaped before they are joined. Neither a check key nor a run id is
        /// validated for the separator, and a host names its tests whatever the user typed, so
        /// check "a" with run "b/c" and check "a/b" with run "c" would otherwise write to one slot
        /// and each read the other's journal.
        /// </summary>
        /// <param name="checkKey">The check's key.</param>
        /// <param name="runId">The run id.</param>
        public static string Slot(string checkKey, string runId) =>
            Escaped(checkKey) + "/" + Escaped(runId);

        /// <summary>One half of a slot key, with the separator made unambiguous.</summary>
        /// <param name="part">The check key or the run id.</param>
        private static string Escaped(string? part) =>
            (part ?? string.Empty).Replace("~", "~~").Replace("/", "~s");

        /// <summary>
        /// Take a journal of what a run stands at now.
        /// </summary>
        /// <param name="checkKey">Which check.</param>
        /// <param name="runId">Which run.</param>
        /// <param name="whenTicks">When, as UTC ticks.</param>
        /// <param name="findings">The findings the run reported. Findings naming another run are left out.</param>
        public static RunJournal Of(string checkKey, string runId, long whenTicks,
                                    IEnumerable<Finding>? findings)
        {
            var statuses = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);
            var parties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in findings ?? Array.Empty<Finding>())
            {
                if (finding == null) continue;

                var runs = CheckFacts.RunsOf(finding);

                if (runs.Count > 0 && !runs.Contains(runId, StringComparer.Ordinal)) continue;

                statuses[finding.Id] = finding.Status;

                if (!FindingStatusLattice.IsOpen(finding.Status)) continue;

                var party = string.IsNullOrWhiteSpace(finding.AssignedTo)
                    ? "(unassigned)" : finding.AssignedTo!.Trim();

                parties[party] = (parties.TryGetValue(party, out var held) ? held : 0) + 1;
            }

            return new RunJournal(checkKey, runId, whenTicks, statuses, parties);
        }

        /// <summary>
        /// What a board drawing every run of a check at once compares against: the runs' journals
        /// as one reference.
        ///
        /// A journal is per run, because that is the unit a run comparison is written and kept
        /// for. A board that draws twelve tests at once still needs one reference to compare
        /// against, and reading twelve deltas to print one line would mean twelve "gone" lists
        /// that overlap wherever a result belongs to two runs.
        ///
        /// Not written back. This exists to be handed to <see cref="CheckDelta.Between"/>, and
        /// writing it would put a thirteenth journal in the project file under a run id no check
        /// has.
        ///
        /// Null when every journal is null, so a board none of whose runs has ever run still reads
        /// as a first run rather than as an empty comparison.
        /// </summary>
        /// <param name="checkKey">Which check.</param>
        /// <param name="journals">The runs' journals. Nulls are runs that have never run.</param>
        public static RunJournal? Together(string checkKey, IEnumerable<RunJournal?>? journals)
        {
            var statuses = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);
            var parties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var when = 0L;
            var any = false;

            foreach (var journal in journals ?? Array.Empty<RunJournal?>())
            {
                if (journal == null) continue;

                any = true;
                when = Math.Max(when, journal.WhenTicks);

                foreach (var pair in journal.Statuses) statuses[pair.Key] = pair.Value;

                // Summed, which is how the rail tallies too: a finding in two runs is counted
                // under both and drawn once.
                foreach (var pair in journal.OpenByParty)
                    parties[pair.Key] = (parties.TryGetValue(pair.Key, out var held) ? held : 0)
                                        + pair.Value;
            }

            return any ? new RunJournal(checkKey, EveryRun, when, statuses, parties) : null;
        }

        /// <summary>
        /// What the run is called on a journal that is several runs read as one.
        ///
        /// Not a run id any check offers, and deliberately not one: a merged journal is a
        /// comparison reference and must never be written to a slot.
        /// </summary>
        public const string EveryRun = "*";

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var statuses = JsonValue.Object();

            foreach (var pair in Statuses.OrderBy(p => p.Key, StringComparer.Ordinal))
                statuses.Set(pair.Key, FindingStatusLattice.ToName(pair.Value));

            var parties = JsonValue.Object();

            foreach (var pair in OpenByParty.OrderBy(p => p.Key, StringComparer.Ordinal))
                parties.Set(pair.Key, pair.Value);

            return JsonValue.Object()
                .Set("check", CheckKey)
                .Set("run", RunId)
                .Set("when", WhenTicks)
                .Set("statuses", statuses)
                .Set("parties", parties);
        }

        /// <summary>Read one back. Null when the record names no check or no run.</summary>
        /// <param name="json">The stored record, or null.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static RunJournal? FromJson(JsonValue? json, IList<string>? notes = null)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var checkKey = json["check"].AsString();
            var runId = json["run"].AsString();

            if (string.IsNullOrWhiteSpace(checkKey) || string.IsNullOrWhiteSpace(runId))
            {
                notes?.Add("A run journal named no check or no run and was left out.");
                return null;
            }

            var statuses = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);
            var stored = json["statuses"];
            var unreadable = 0;

            foreach (var id in stored.Keys)
            {
                if (FindingStatusLattice.TryParse(stored[id].AsString(), out var status))
                    statuses[id] = status;
                else unreadable++;
            }

            if (unreadable > 0)
                notes?.Add(unreadable.ToString(CultureInfo.InvariantCulture)
                           + " journalled finding(s) held a status this build does not know.");

            var parties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var held = json["parties"];

            foreach (var party in held.Keys) parties[party] = (int)held[party].AsLong();

            return new RunJournal(checkKey!, runId!, json["when"].AsLong(), statuses, parties);
        }

        /// <summary>The journal a run last wrote, or null when it has never run.</summary>
        /// <param name="store">The project file.</param>
        /// <param name="checkKey">Which check.</param>
        /// <param name="runId">Which run.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static RunJournal? Current(ProjectStore store, string checkKey, string runId,
                                          IList<string>? notes = null) =>
            Read(store, checkKey, runId, CurrentKey, notes);

        /// <summary>The journal before that, or null when there has only been one run.</summary>
        /// <param name="store">The project file.</param>
        /// <param name="checkKey">Which check.</param>
        /// <param name="runId">Which run.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static RunJournal? Previous(ProjectStore store, string checkKey, string runId,
                                           IList<string>? notes = null) =>
            Read(store, checkKey, runId, PreviousKey, notes);

        /// <summary>
        /// Write this journal into the project file as the current one, moving the current one into
        /// the previous slot and dropping whatever was there. Every other key in the section — the
        /// saved runs among them — is left as it was.
        /// </summary>
        /// <param name="store">The project file.</param>
        public void Write(ProjectStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            var section = store.Section(Section);
            var journals = section[Key];

            if (journals.Kind != JsonKind.Object)
            {
                journals = JsonValue.Object();
                section.Set(Key, journals);
            }

            var slot = journals[Slot(CheckKey, RunId)];

            if (slot.Kind != JsonKind.Object)
            {
                slot = JsonValue.Object();
                journals.Set(Slot(CheckKey, RunId), slot);
            }

            var current = slot[CurrentKey];

            if (current.Kind == JsonKind.Object) slot.Set(PreviousKey, current);

            slot.Set(CurrentKey, ToJson());
        }

        private static RunJournal? Read(ProjectStore store, string checkKey, string runId,
                                        string which, IList<string>? notes)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(checkKey) || string.IsNullOrWhiteSpace(runId)) return null;

            return FromJson(store.Section(Section)[Key][Slot(checkKey, runId)][which], notes);
        }

        /// <inheritdoc />
        public override string ToString() =>
            Slot(CheckKey, RunId) + ": " + Count.ToString(CultureInfo.InvariantCulture) + " finding(s)";
    }
}
