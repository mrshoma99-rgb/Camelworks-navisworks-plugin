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
        internal FindingIntake(int added, int updated, int absent, IReadOnlyList<string> notes)
        {
            Added = added; Updated = updated; Absent = absent; Notes = notes;
        }

        /// <summary>Findings this run reported for the first time.</summary>
        public int Added { get; }

        /// <summary>Findings already held, reconciled with what the run reported.</summary>
        public int Updated { get; }

        /// <summary>Findings the register holds that this run no longer reports.</summary>
        public int Absent { get; }

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
    /// <b>One record for every check.</b> Clash results, clearance breaches, IDS failures, model
    /// health faults and revision differences were each heading for their own store, their own
    /// status field and their own export path. They converge here, which is what lets the board,
    /// the report, the BCF exchange and the issue links each be written once.
    ///
    /// <b>A run is an observation, not an authority.</b> <see cref="Observe"/> takes what a check
    /// just found and reconciles it with what is held: a finding already known keeps the decision
    /// somebody made about it and takes the run's fresh facts; a finding the run no longer reports
    /// is marked absent rather than deleted, because that is the record of it having been fixed
    /// and an issue may point at it. This is what keeps the register from disagreeing with the
    /// model without letting the model overwrite decisions it cannot itself hold.
    ///
    /// <b>Scoped by source.</b> An observation names which source it speaks for, so a clash run
    /// never marks the IDS findings absent.
    /// </summary>
    public sealed class FindingRegister
    {
        private readonly Dictionary<string, Finding> _findings =
            new Dictionary<string, Finding>(StringComparer.Ordinal);

        /// <summary>How many findings are held, present or not.</summary>
        public int Count => _findings.Count;

        /// <summary>Whether anything is held.</summary>
        public bool IsEmpty => _findings.Count == 0;

        /// <summary>Every finding, in no particular order.</summary>
        public IReadOnlyCollection<Finding> All => _findings.Values;

        /// <summary>Findings the latest run still reports.</summary>
        public IEnumerable<Finding> Present => _findings.Values.Where(f => f.IsPresent);

        /// <summary>Findings from one source.</summary>
        /// <param name="source">Which check.</param>
        public IEnumerable<Finding> From(FindingSource source) =>
            _findings.Values.Where(f => f.Source == source);

        /// <summary>How many are still somebody's to deal with.</summary>
        public int Open => _findings.Values.Count(f => FindingStatusLattice.IsOpen(f.Status));

        /// <summary>The finding with this id, or null.</summary>
        /// <param name="id">The derived id.</param>
        public Finding? Find(string? id) =>
            id != null && _findings.TryGetValue(id, out var finding) ? finding : null;

        /// <summary>Put a finding in, replacing any held under the same id.</summary>
        /// <param name="finding">The finding.</param>
        public void Put(Finding? finding)
        {
            if (finding == null) return;

            _findings[finding.Id] = finding;
        }

        /// <summary>Forget one.</summary>
        /// <param name="id">The derived id.</param>
        public bool Remove(string? id) => id != null && _findings.Remove(id);

        /// <summary>Forget everything.</summary>
        public void Clear() => _findings.Clear();

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
        public FindingIntake Observe(FindingSource source, IReadOnlyList<Finding>? found,
                                     long whenTicks, string? origin = null,
                                     Func<Finding, bool>? scope = null)
        {
            var reported = found ?? Array.Empty<Finding>();
            var added = 0;
            var updated = 0;
            var notes = new List<string>();

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fresh in reported)
            {
                if (fresh == null) continue;

                if (fresh.Source != source)
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
                    added++;
                    continue;
                }

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

                foreach (var comment in fresh.Comments)
                    if (!held.Comments.Any(c => c.Repeats(comment)))
                        held.Comments.Add(comment);

                updated++;
            }

            // What this source held, that this run covered, and did not report.
            var absent = 0;

            foreach (var held in _findings.Values)
            {
                if (held.Source != source || seen.Contains(held.Id) || !held.IsPresent) continue;
                if (scope != null && !scope(held)) continue;

                held.IsPresent = false;
                absent++;
            }

            return new FindingIntake(added, updated, absent, notes);
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
                    added++;
                    continue;
                }

                held.MergeFrom(theirs, theirsIsNewer);

                foreach (var comment in theirs.Comments)
                    if (!held.Comments.Any(c => c.Repeats(comment)))
                        held.Comments.Add(comment);

                updated++;
            }

            return new FindingIntake(added, updated, 0, Array.Empty<string>());
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
                           + "this one understands has been read; anything newer was left alone "
                           + "rather than guessed at.");

            var renamed = 0;
            var unreadable = 0;

            foreach (var record in json["findings"].Items)
            {
                var finding = FindingJson.Read(record, out var wasRenamed);

                if (finding == null) { unreadable++; continue; }
                if (wasRenamed) renamed++;

                register._findings[finding.Id] = finding;
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
