using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// One read of one or more of a check's runs.
    ///
    /// Everything a check produces is a <see cref="Finding"/>, so a read is the runs it covered,
    /// the findings it produced, the predicate those findings are observed under, and whatever the
    /// read could not do.
    /// </summary>
    public sealed class CheckRead
    {
        /// <summary>Record a read.</summary>
        /// <param name="runs">The runs this read covered.</param>
        /// <param name="findings">Every result, already a finding.</param>
        /// <param name="scope">
        /// The predicate <see cref="FindingRegister.Observe"/> is scoped by. Null derives it from
        /// the run ids in <paramref name="runs"/> through <see cref="CheckFacts.ScopeOf"/>, which
        /// is what a check that writes <see cref="CheckFacts.Runs"/> wants.
        /// </param>
        /// <param name="notes">What the read could not do.</param>
        public CheckRead(IReadOnlyList<CheckRun>? runs, IReadOnlyList<Finding>? findings,
                         Func<Finding, bool>? scope = null, IReadOnlyList<string>? notes = null)
        {
            Runs = runs ?? Array.Empty<CheckRun>();
            Findings = findings ?? Array.Empty<Finding>();
            Notes = notes ?? Array.Empty<string>();
            Scope = scope ?? CheckFacts.ScopeOf(Runs.Select(run => run.Id));
        }

        /// <summary>The runs this read covered.</summary>
        public IReadOnlyList<CheckRun> Runs { get; }

        /// <summary>Every result, already a finding. Parents carry locations; leaves do not.</summary>
        public IReadOnlyList<Finding> Findings { get; }

        /// <summary>
        /// Which held findings this read speaks for, so reading three of twelve runs does not mark
        /// the other nine absent. Never null.
        ///
        /// Not optional and not a convenience. A read observed with no scope marks every finding of
        /// that source the read did not report absent, which is correct only while a check has
        /// exactly one run and silently wrong the moment it has two.
        /// </summary>
        public Func<Finding, bool> Scope { get; }

        /// <summary>
        /// What the read could not do — a run the host would not open, geometry the document would
        /// not give up. Shown on the board rather than swallowed.
        /// </summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The findings this read reported for one run.</summary>
        /// <param name="runId">The run id.</param>
        public IReadOnlyList<Finding> For(string? runId) =>
            Findings.Where(finding => CheckFacts.IsIn(finding, runId)).ToList();

        /// <summary>How many findings this read reported for one run.</summary>
        /// <param name="runId">The run id.</param>
        public int CountFor(string? runId) =>
            Findings.Count(finding => CheckFacts.IsIn(finding, runId));

        /// <inheritdoc />
        public override string ToString() =>
            Findings.Count.ToString(CultureInfo.InvariantCulture) + " finding(s) from "
            + Runs.Count.ToString(CultureInfo.InvariantCulture) + " run(s)";
    }
}
