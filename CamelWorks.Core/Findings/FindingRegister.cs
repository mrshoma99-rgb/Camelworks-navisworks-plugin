using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Findings
{
    /// <summary>What a read of a check's results did to the register.</summary>
    public sealed class FindingIntake
    {
        internal FindingIntake(int added, int updated, int absent,
                               IReadOnlyList<string> reopened, IReadOnlyList<string> notes)
        {
            Added = added; Updated = updated; Absent = absent; Reopened = reopened; Notes = notes;
        }

        /// <summary>Findings this run reported for the first time.</summary>
        public int Added { get; }

        /// <summary>Findings already held, reconciled with what the run reported.</summary>
        public int Updated { get; }

        /// <summary>Findings the register holds that this run no longer reports.</summary>
        public int Absent { get; }

        /// <summary>
        /// Findings held closed — Approved or Resolved — that this run reported present again.
        ///
        /// Computed here rather than from a journal of what a run stood at. The status lattice
        /// promotes and never regresses, so a re-run cannot pull an approved finding back and
        /// "was closed then, is open now" is false for every machine-produced finding. The moment
        /// the merge happens is the only place that knows the register was holding a closed finding
        /// and the run reported it again, so this is where it is recorded. The register does not
        /// demote them; this is the report, and the board's Regressed line.
        /// </summary>
        public IReadOnlyList<string> Reopened { get; }

        /// <summary>Anything worth saying about the intake.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Added.ToString("N0", CultureInfo.InvariantCulture) + " new, "
            + Updated.ToString("N0", CultureInfo.InvariantCulture) + " already known"
            + (Absent > 0
                ? ", " + Absent.ToString("N0", CultureInfo.InvariantCulture) + " no longer reported"
                : string.Empty) + ".";

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Every finding this project holds, whatever produced it.
    ///
    /// One record for every check. Clash results, clearance breaches, IDS failures, model health
    /// faults and revision differences were each heading for their own store, status field and
    /// export path. They converge here, so the board, the report, the BCF exchange and the issue
    /// links are each written once.
    ///
    /// A run is an observation rather than an authority. <see cref="Observe"/> takes what a check
    /// found and reconciles it with what is held: a finding already known keeps the decision
    /// recorded about it and takes the run's fresh facts; a finding the run no longer reports is
    /// marked absent rather than deleted, because that is the record of the fix and an issue may
    /// point at it. This keeps the register in step with the model without letting the model
    /// overwrite decisions it cannot itself hold.
    ///
    /// Scoped by source: an observation names which source it speaks for, so a clash run never marks
    /// the IDS findings absent.
    /// </summary>
    public sealed class FindingRegister
    {
        private readonly Dictionary<string, Finding> _findings =
            new Dictionary<string, Finding>(StringComparer.Ordinal);

        private Dictionary<string, (Finding Owner, FindingLocation Location)>? _locations;

        /// <summary>How many findings are held, present or not.</summary>
        public int Count => _findings.Count;

        /// <summary>Whether anything is held.</summary>
        public bool IsEmpty => _findings.Count == 0;

        /// <summary>Every finding, in no particular order.</summary>
        public IReadOnlyCollection<Finding> All => _findings.Values;

        /// <summary>Findings the latest run still reports.</summary>
        public IEnumerable<Finding> Present => _findings.Values.Where(f => f.IsPresent);

        /// <summary>Findings from one source.</summary>
        /// <param name="source">Which check. One of <see cref="FindingSource"/>.</param>
        public IEnumerable<Finding> From(string source) =>
            _findings.Values.Where(f => string.Equals(f.Source, source, StringComparison.Ordinal));

        /// <summary>How many are still somebody's to deal with.</summary>
        public int Open => _findings.Values.Count(f => FindingStatusLattice.IsOpen(f.Status));

        /// <summary>
        /// The finding with this id, or null.
        ///
        /// Findings only. A location is not a register entry and never becomes one, so a location's
        /// id returns null here — see <see cref="FindLocation"/>.
        /// </summary>
        /// <param name="id">The derived id.</param>
        public Finding? Find(string? id) =>
            id != null && _findings.TryGetValue(id, out var finding) ? finding : null;

        /// <summary>
        /// How many locations every finding holds between them.
        ///
        /// Not part of <see cref="Count"/> or <see cref="Open"/>. It exists so a screen can say
        /// "37 rules, 1,204 locations" rather than implying 37 problems are 1,204.
        ///
        /// Summed, not counted off the lookup index. Three specifications each failing the same
        /// five hundred walls are fifteen hundred places to visit, and an index keyed by location
        /// id would report five hundred if any producer ever repeated an id between findings.
        /// </summary>
        public int LocationCount => _findings.Values.Sum(f => f.Locations.Count);

        /// <summary>The finding that owns the location with this id, or null.</summary>
        /// <param name="locationId">The location's id.</param>
        public Finding? OwnerOfLocation(string? locationId) =>
            locationId != null && Index().TryGetValue(locationId, out var found) ? found.Owner : null;

        /// <summary>
        /// The location with this id, or null.
        ///
        /// A location id is only as unique as the producer that made it, so every producer in Core
        /// prefixes its finding's id. Where two findings still hold one id, the first in register
        /// order owns the lookup, and both this and <see cref="OwnerOfLocation"/> then answer for
        /// that one; a caller sent to a place would be sent to the wrong finding's place.
        /// </summary>
        /// <param name="locationId">The location's id.</param>
        public FindingLocation? FindLocation(string? locationId) =>
            locationId != null && Index().TryGetValue(locationId, out var found) ? found.Location : null;

        /// <summary>Put a finding in, replacing any held under the same id.</summary>
        /// <param name="finding">The finding.</param>
        public void Put(Finding? finding)
        {
            if (finding == null) return;

            _findings[finding.Id] = finding;
            _locations = null;
        }

        /// <summary>Forget one.</summary>
        /// <param name="id">The derived id.</param>
        public bool Remove(string? id)
        {
            if (id == null || !_findings.Remove(id)) return false;

            _locations = null;
            return true;
        }

        /// <summary>Forget everything.</summary>
        public void Clear()
        {
            _findings.Clear();
            _locations = null;
        }

        // Built on the first lookup and dropped whenever the set of findings changes. A register
        // of eighteen thousand clash leaves has no locations at all, so building it eagerly on
        // every read would be work for nothing on the commonest board there is.
        private Dictionary<string, (Finding Owner, FindingLocation Location)> Index()
        {
            if (_locations != null) return _locations;

            var index = new Dictionary<string, (Finding, FindingLocation)>(StringComparer.Ordinal);

            foreach (var finding in _findings.Values.OrderBy(f => f.Id, StringComparer.Ordinal))
                foreach (var location in finding.Locations)
                    if (!index.ContainsKey(location.Id)) index[location.Id] = (finding, location);

            _locations = index;
            return index;
        }

        // Notes written against a place, moved onto this run's location for the same place. The
        // incoming locations are newly built by the producer and have none of their own, so this is
        // an add rather than a merge; Repeats still guards the case where a producer supplies some.
        private static void CarryNotes(IReadOnlyList<FindingLocation> outgoing,
                                       IReadOnlyList<FindingLocation> incoming)
        {
            if (outgoing.Count == 0 || incoming.Count == 0) return;

            var noted = new Dictionary<string, FindingLocation>(StringComparer.Ordinal);

            foreach (var location in outgoing)
                if (location.Notes.Count > 0 && !noted.ContainsKey(location.Id))
                    noted[location.Id] = location;

            if (noted.Count == 0) return;

            foreach (var location in incoming)
            {
                if (!noted.TryGetValue(location.Id, out var was)) continue;

                foreach (var note in was.Notes)
                    if (!location.Notes.Any(n => n.Repeats(note))) location.Notes.Add(note);
            }
        }

        /// <summary>
        /// Take what a check just found and reconcile it with what is held.
        /// </summary>
        /// <param name="source">Which check ran. Only this source's findings are considered.</param>
        /// <param name="found">What it found.</param>
        /// <param name="whenTicks">When it ran, as UTC ticks.</param>
        /// <param name="origin">What to record as where these came from.</param>
        /// <param name="scope">
        /// Which of this source's held findings the run covers, when it did not cover all of them.
        /// A clash run over three tests must not mark the other tests' findings absent. Null means
        /// the run covered everything this source has.
        /// </param>
        public FindingIntake Observe(string source, IReadOnlyList<Finding>? found,
                                     long whenTicks, string? origin = null,
                                     Func<Finding, bool>? scope = null)
        {
            var reported = found ?? Array.Empty<Finding>();
            var added = 0;
            var updated = 0;
            var notes = new List<string>();
            var reopened = new List<string>();

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fresh in reported)
            {
                if (fresh == null) continue;

                if (!string.Equals(fresh.Source, source, StringComparison.Ordinal))
                {
                    notes.Add("A " + fresh.Source + " finding arrived in a " + source
                              + " run and was left alone.");

                    continue;
                }

                seen.Add(fresh.Id);

                fresh.LastSeenTicks = whenTicks;
                fresh.IsPresent = true;
                fresh.Origin = origin ?? fresh.Origin;

                if (!_findings.TryGetValue(fresh.Id, out var held))
                {
                    _findings[fresh.Id] = fresh;
                    _locations = null;
                    added++;
                    continue;
                }

                // Recorded before the merge, which cannot demote it. Somebody had settled this and
                // the run has reported it again; the board says so rather than the register
                // quietly re-opening it.
                if (FindingStatusLattice.IsClosed(held.Status)) reopened.Add(held.Id);

                // The run's facts are newer; the decision is not the run's to make. MergeFrom
                // promotes status and never regresses it, so a re-run that reports a clash as New
                // cannot undo somebody having approved it.
                held.MergeFrom(fresh, otherIsNewer: true);

                // A title somebody set and the host would not take is not the run's to reword.
                if (!held.Pending.Contains(FindingField.Title)) held.Title = fresh.Title;

                held.Detail = fresh.Detail;
                held.X = fresh.X;
                held.Y = fresh.Y;
                held.Z = fresh.Z;
                held.LastSeenTicks = whenTicks;
                held.IsPresent = true;
                held.Origin = origin ?? held.Origin;

                foreach (var fact in fresh.Facts) held.Facts[fact.Key] = fact.Value;

                // This run's locations in place of the last run's; keeping the old set would leave
                // "show me all of them" pointing at a set that has moved on. A location carries no
                // decision, so nothing triaged is lost, but it may carry notes, and the producers
                // build fresh locations with none. Notes are therefore carried onto the incoming
                // location with the same id. A note on an id this run no longer reports goes with
                // the location it was written against.
                if (fresh.HasLocations || held.HasLocations)
                {
                    CarryNotes(held.Locations, fresh.Locations);
                    held.ReplaceLocations(fresh.Locations);
                    _locations = null;
                }

                foreach (var comment in fresh.Comments)
                    if (!held.Comments.Any(c => c.Repeats(comment)))
                        held.Comments.Add(comment);

                updated++;
            }

            // What this source held, that this run covered, and did not report.
            var absent = 0;

            foreach (var held in _findings.Values)
            {
                if (!string.Equals(held.Source, source, StringComparison.Ordinal)
                    || seen.Contains(held.Id) || !held.IsPresent) continue;
                if (scope != null && !scope(held)) continue;

                held.IsPresent = false;
                absent++;
            }

            return new FindingIntake(added, updated, absent, reopened, notes);
        }

        /// <summary>
        /// Merge another register into this one — a colleague's file, an import.
        /// </summary>
        /// <param name="other">Theirs.</param>
        /// <param name="theirsIsNewer">Whether their edits are the later ones.</param>
        public FindingIntake Merge(FindingRegister? other, bool theirsIsNewer)
        {
            var added = 0;
            var updated = 0;

            foreach (var theirs in other?.All ?? Enumerable.Empty<Finding>())
            {
                if (!_findings.TryGetValue(theirs.Id, out var held))
                {
                    _findings[theirs.Id] = theirs;
                    _locations = null;
                    added++;
                    continue;
                }

                held.MergeFrom(theirs, theirsIsNewer);

                foreach (var comment in theirs.Comments)
                    if (!held.Comments.Any(c => c.Repeats(comment)))
                        held.Comments.Add(comment);

                updated++;
            }

            return new FindingIntake(added, updated, 0, Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("version", FindingJson.Version)
                .Set("findings", JsonValue.Array(
                    _findings.Values.OrderBy(f => f.Id, StringComparer.Ordinal)
                                    .Select(FindingJson.Write)));

        /// <summary>Read a register back. Never null: nothing held is the ordinary state.</summary>
        /// <param name="json">What was saved, or null.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static FindingRegister FromJson(JsonValue? json, IList<string>? notes = null)
        {
            var register = new FindingRegister();

            if (json == null || json.Kind != JsonKind.Object) return register;

            var version = (int)json["version"].AsLong(FindingJson.Version);

            if (version > FindingJson.Version)
                notes?.Add("These findings were written by a newer version of CamelWorks. What "
                           + "this one understands has been read.");

            var renamed = 0;
            var unreadable = 0;

            foreach (var record in json["findings"].Items)
            {
                var finding = FindingJson.Read(record, out var wasRenamed);

                if (finding == null) { unreadable++; continue; }
                if (wasRenamed) renamed++;

                register._findings[finding.Id] = finding;
                register._locations = null;
            }

            if (unreadable > 0)
                notes?.Add(unreadable.ToString(CultureInfo.InvariantCulture)
                           + " finding(s) could not be read and were left out.");

            if (renamed > 0)
                notes?.Add(renamed.ToString(CultureInfo.InvariantCulture)
                           + " finding(s) were re-identified, because how an id is derived changed "
                           + "since they were written. Their decisions are intact; a link from an "
                           + "issue to one of them may need making again.");

            return register;
        }
    }
}
