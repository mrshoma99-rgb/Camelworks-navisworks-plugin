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
    /// One journal per check and run, and one of them kept. A journal is a full id list — twelve
    /// runs of eighteen thousand results is the largest thing in the project file — and only the
    /// last one is read: the comparison is against what the run stood at when it last ran.
    /// <see cref="Write"/> therefore replaces it, and a caller that needs the outgoing journal must
    /// read <see cref="Current"/> before it writes.
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

        // Which run reported each finding, on a journal that is several runs merged. Null on a
        // per-run journal, where the answer is always RunId.
        private readonly IReadOnlyDictionary<string, string>? _runOf;

        /// <summary>Record what a run stood at.</summary>
        /// <param name="checkKey">Which check, as <see cref="ICheck.Key"/>.</param>
        /// <param name="runId">Which run.</param>
        /// <param name="whenTicks">When it ran, as UTC ticks.</param>
        /// <param name="statuses">Each finding id and the status it held.</param>
        /// <param name="openByParty">How many open findings each party held.</param>
        /// <param name="runOfFinding">
        /// Which run reported each finding, for a journal that is several runs read as one. Null
        /// means every finding is <paramref name="runId"/>'s, which is what a per-run journal is.
        /// </param>
        public RunJournal(string checkKey, string runId, long whenTicks,
                          IReadOnlyDictionary<string, FindingStatus>? statuses,
                          IReadOnlyDictionary<string, int>? openByParty,
                          IReadOnlyDictionary<string, string>? runOfFinding = null)
        {
            _runOf = runOfFinding;

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
        /// Which run reported a journalled finding, or null when the journal does not hold it.
        ///
        /// This journal's run, unless it is several runs read as one — see <see cref="Together"/> —
        /// in which case it is the run whose journal the finding came from. A screen drawing one
        /// run of a board uses it to say what went from that run rather than from the read.
        /// </summary>
        /// <param name="findingId">The finding id.</param>
        public string? RunOf(string? findingId) =>
            !Holds(findingId) ? null
            : _runOf != null && _runOf.TryGetValue(findingId!, out var run) ? run : RunId;

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
        ///
        /// Two things a merge has to settle, because two runs can hold one finding id:
        ///
        ///   * The status. The later journal wins, by <see cref="WhenTicks"/>, and on equal ticks
        ///     the status further along <see cref="FindingStatusLattice"/> does. Taking whichever
        ///     journal was enumerated last made the answer depend on the order the runs were read
        ///     in, so the same two runs could merge differently from one board to the next.
        ///   * <see cref="OpenByParty"/>. Summed, so a finding that two runs both report open is
        ///     counted under its party once per run. The journal holds counts and not ids, so the
        ///     double count cannot be taken out here; it is a per-run tally read as one, which is
        ///     what the rail draws. <see cref="Statuses"/> and <see cref="Count"/> are per finding
        ///     and do not double.
        /// </summary>
        /// <param name="checkKey">Which check.</param>
        /// <param name="journals">The runs' journals. Nulls are runs that have never run.</param>
        public static RunJournal? Together(string checkKey, IEnumerable<RunJournal?>? journals)
        {
            var statuses = new Dictionary<string, FindingStatus>(StringComparer.Ordinal);
            var parties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var runOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var settled = new Dictionary<string, long>(StringComparer.Ordinal);
            var when = 0L;
            var any = false;

            foreach (var journal in journals ?? Array.Empty<RunJournal?>())
            {
                if (journal == null) continue;

                any = true;
                when = Math.Max(when, journal.WhenTicks);

                foreach (var pair in journal.Statuses)
                {
                    if (settled.TryGetValue(pair.Key, out var was)
                        && !Wins(journal.WhenTicks, pair.Value, was, statuses[pair.Key])) continue;

                    statuses[pair.Key] = pair.Value;
                    settled[pair.Key] = journal.WhenTicks;
                    runOf[pair.Key] = journal.RunId;
                }

                foreach (var pair in journal.OpenByParty)
                    parties[pair.Key] = (parties.TryGetValue(pair.Key, out var held) ? held : 0)
                                        + pair.Value;
            }

            return any ? new RunJournal(checkKey, EveryRun, when, statuses, parties, runOf) : null;
        }

        /// <summary>Whether one journal's record of a finding replaces another's.</summary>
        /// <param name="ticks">When the arriving journal was taken.</param>
        /// <param name="status">What it says the finding stood at.</param>
        /// <param name="heldTicks">When the journal already merged was taken.</param>
        /// <param name="held">What that one says.</param>
        private static bool Wins(long ticks, FindingStatus status, long heldTicks,
                                 FindingStatus held) =>
            ticks != heldTicks
                ? ticks > heldTicks
                : FindingStatusLattice.Merge(status, held) == status && status != held;

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

        /// <summary>
        /// Write this journal into the project file as the run's current one, replacing what was
        /// there. Every other key in the section — the saved runs among them — is left as it was.
        ///
        /// A caller comparing against the outgoing journal reads <see cref="Current"/> first: after
        /// this, the run's current journal is this one.
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
