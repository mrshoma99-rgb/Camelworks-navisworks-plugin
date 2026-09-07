using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// What changed between a run's last journal and what the register holds now.
    ///
    /// Two jobs, kept apart because they are answered in different places.
    ///
    /// Matching is per check, through <see cref="ICheck.Aliases"/>. A finding id contains whatever
    /// its producer used to tell one result from another, and for clash that is a quantised
    /// position cell which drifts when the model is re-exported. Matching on the id alone would
    /// report every clash on a model that moved 40 mm as gone and new at once, so a check offers
    /// the ids a finding may also be recognised under and a match made through one is counted in
    /// <see cref="MatchedOnAlias"/>.
    ///
    /// Regression is not computed here by comparing two statuses.
    /// <see cref="FindingStatusLattice"/> is promote-only and
    /// <see cref="FindingRegister.Observe"/> never regresses, so "was closed in the journal and is
    /// open now" is false for every machine-produced finding and would print a nought forever.
    /// <see cref="FindingIntake.Reopened"/> is the answer, recorded at the moment the merge saw a
    /// closed finding reported present again.
    /// </summary>
    public sealed class CheckDelta
    {
        private CheckDelta(bool noPreviousRun, IReadOnlyList<string> added,
                           IReadOnlyList<string> persisting, IReadOnlyList<string> regressed,
                           IReadOnlyList<string> gone, int matchedOnAlias,
                           IReadOnlyDictionary<string, string>? goneRun = null)
        {
            NoPreviousRun = noPreviousRun;
            New = added;
            Persisting = persisting;
            Regressed = regressed;
            Gone = gone;
            MatchedOnAlias = matchedOnAlias;
            _goneRun = goneRun;
        }

        // Which run reported each gone finding, off the journal it was in. Null when the caller
        // compared against a single run's journal, where every gone finding is that run's.
        private readonly IReadOnlyDictionary<string, string>? _goneRun;

        /// <summary>
        /// True when there is no journal at all, as against a journal that held nothing.
        ///
        /// The board prints "no previous run recorded" for the first and a count for the second, so
        /// a first run does not read as a wholesale change. Nothing is new and nothing is gone when
        /// there is nothing to compare against: saying so costs one sentence and inventing a
        /// comparison would make every first run a false alarm.
        /// </summary>
        public bool NoPreviousRun { get; }

        /// <summary>Findings the previous journal did not hold, by any id this check recognises.</summary>
        public IReadOnlyList<string> New { get; }

        /// <summary>Findings the previous journal held and this run still reports.</summary>
        public IReadOnlyList<string> Persisting { get; }

        /// <summary>
        /// Findings held closed that this run reported present again, as
        /// <see cref="FindingIntake.Reopened"/> recorded them, narrowed to the ones this run speaks
        /// for and the register still holds present.
        /// </summary>
        public IReadOnlyList<string> Regressed { get; }

        /// <summary>Findings the previous journal held that nothing in this run answers to.</summary>
        public IReadOnlyList<string> Gone { get; }

        /// <summary>
        /// How many matched only through an alias rather than exactly. A large one means the model
        /// has moved; it is a diagnostic, not a count of results.
        /// </summary>
        public int MatchedOnAlias { get; }

        /// <summary>
        /// The same delta, restricted to the findings that reached the board.
        ///
        /// A read observes every result into the register, and the board then collapses duplicates
        /// and lets the rules take results out. Printed raw, "twelve new" over a board holding four
        /// is two populations in one row and the reader cannot find the other eight, so a screen
        /// restricts the delta to what it draws.
        ///
        /// <see cref="Gone"/> is not restricted by the rows. A finding the rules suppressed is not
        /// gone from the model, and it never entered that list: the run reported it and it was
        /// claimed. What is on the list is the findings the journal held and nothing answered for,
        /// which by definition are on no board. <see cref="MatchedOnAlias"/> is carried across
        /// whole for the same reason — it is a diagnostic about the read, not a count of rows.
        /// </summary>
        /// <param name="shown">The finding ids on the board. Null restricts nothing.</param>
        /// <param name="runId">
        /// The run the screen draws, when it draws one. <see cref="Gone"/> is then the findings
        /// that run's journal held, rather than every run's: a per-test screen printing the whole
        /// read's gone count says a number its own rows cannot account for. Null keeps the whole
        /// read's, which is what a board over every run wants.
        /// </param>
        public CheckDelta OnBoard(IEnumerable<string>? shown, string? runId = null)
        {
            if (shown == null && runId == null) return this;

            var kept = shown == null ? null
                : new HashSet<string>(shown.Where(id => id != null), StringComparer.Ordinal);

            return new CheckDelta(NoPreviousRun,
                                  Restricted(New, kept),
                                  Restricted(Persisting, kept),
                                  Restricted(Regressed, kept),
                                  runId == null ? Gone : GoneIn(runId),
                                  MatchedOnAlias, _goneRun);
        }

        /// <summary>
        /// The findings one run's journal held that nothing in this read answers for.
        ///
        /// The whole list when the comparison was against a single run's journal, since every
        /// gone finding is then that run's.
        /// </summary>
        /// <param name="runId">The run.</param>
        public IReadOnlyList<string> GoneIn(string? runId) =>
            runId == null || _goneRun == null ? Gone
            : Gone.Where(id => _goneRun.TryGetValue(id, out var run)
                               && string.Equals(run, runId, StringComparison.Ordinal)).ToList();

        /// <summary>One of the three lists, restricted to the rows a screen draws.</summary>
        /// <param name="ids">The list.</param>
        /// <param name="kept">The ids on the board, or null to restrict nothing.</param>
        private static IReadOnlyList<string> Restricted(IReadOnlyList<string> ids,
                                                        HashSet<string>? kept) =>
            kept == null ? ids : ids.Where(kept.Contains).ToList();

        /// <summary>True when nothing came, went or came back.</summary>
        public bool IsQuiet => New.Count == 0 && Gone.Count == 0 && Regressed.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (NoPreviousRun) return "No previous run recorded.";

            var s = Count(New) + " new, " + Count(Persisting) + " still there, " + Count(Gone) + " gone";

            if (Regressed.Count > 0) s += ", " + Count(Regressed) + " reported again after being closed";

            if (MatchedOnAlias > 0)
                s += ". " + Count(MatchedOnAlias) + " matched on a neighbouring position rather than "
                     + "exactly, which means the model has moved";

            return s + ".";
        }

        /// <summary>
        /// Compare what a run stood at with what the register holds now.
        /// </summary>
        /// <param name="before">
        /// The run's last journal, or null when it has never run. Null is
        /// <see cref="NoPreviousRun"/> and not an empty comparison.
        /// </param>
        /// <param name="intake">
        /// What the read just did to the register, which is where regression comes from. Null
        /// reports nothing regressed, for a caller comparing two states without a read between
        /// them.
        /// </param>
        /// <param name="now">The register as the read left it.</param>
        /// <param name="scope">Which held findings this run speaks for — <see cref="CheckRead.Scope"/>.</param>
        /// <param name="aliases">
        /// The ids a finding may also be recognised under, as <see cref="ICheck.Aliases"/> offers
        /// them. Null offers none, which is what every check but clash does.
        /// </param>
        public static CheckDelta Between(RunJournal? before, FindingIntake? intake,
                                         FindingRegister now, Func<Finding, bool> scope,
                                         Func<Finding, IEnumerable<string>>? aliases)
        {
            if (now == null) throw new ArgumentNullException(nameof(now));
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            // Scoped like the other three. The intake covers whatever the read covered, which can
            // be several runs, so a delta for one run would otherwise report the reopenings of
            // every run the read touched. A reopening whose finding the register no longer holds
            // as present is left out for the same reason.
            var regressed = (IReadOnlyList<string>)(intake?.Reopened ?? Array.Empty<string>())
                .Where(id =>
                {
                    var finding = now.Find(id);

                    return finding != null && finding.IsPresent && scope(finding);
                })
                .ToList();

            if (before == null)
                return new CheckDelta(true, Array.Empty<string>(), Array.Empty<string>(),
                                      regressed, Array.Empty<string>(), 0);

            var added = new List<string>();
            var persisting = new List<string>();
            var matchedOnAlias = 0;

            // What the journal held that this run has answered for. A journalled id is claimed
            // once: two findings offering the same alias must not both count as the same one
            // returning, or a model that moved would report fewer gone than it has.
            var claimed = new HashSet<string>(StringComparer.Ordinal);

            var present = now.Present.Where(scope)
                             .OrderBy(finding => finding.Id, StringComparer.Ordinal).ToList();

            var unmatched = new List<Finding>(present.Count);

            // Pass one: every exact match, before any alias is spent. Done per finding instead, a
            // finding sorting earlier could take through an alias the very id another finding owns
            // exactly, and that finding would then read as new while the alias match reported the
            // model as having moved. The winner would also depend on the order the register
            // enumerates, so the same model would report differently from one run to the next.
            foreach (var finding in present)
            {
                if (before.Holds(finding.Id) && claimed.Add(finding.Id)) persisting.Add(finding.Id);
                else unmatched.Add(finding);
            }

            // Pass two: the aliases, over what is left and against what pass one did not claim.
            foreach (var finding in unmatched)
            {
                var matched = false;

                foreach (var alias in aliases?.Invoke(finding) ?? Array.Empty<string>())
                {
                    if (alias == null || !before.Holds(alias) || !claimed.Add(alias)) continue;

                    persisting.Add(finding.Id);
                    matchedOnAlias++;
                    matched = true;
                    break;
                }

                if (!matched) added.Add(finding.Id);
            }

            persisting.Sort(StringComparer.Ordinal);
            added.Sort(StringComparer.Ordinal);

            var gone = before.Statuses.Keys.Where(id => !claimed.Contains(id))
                             .OrderBy(id => id, StringComparer.Ordinal).ToList();

            // Which run each gone finding was journalled by, so a screen drawing one run can say
            // what went from that run rather than from the whole read.
            var goneRun = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var id in gone)
            {
                var run = before.RunOf(id);

                if (run != null) goneRun[id] = run;
            }

            return new CheckDelta(false, added, persisting, regressed, gone, matchedOnAlias, goneRun);
        }

        private static string Count(IReadOnlyCollection<string> of) => Count(of.Count);

        private static string Count(int how) => how.ToString("N0", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
