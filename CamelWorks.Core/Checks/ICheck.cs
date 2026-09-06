using System;
using System.Collections.Generic;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// One kind of check: clash detection, clearance, fall hazard, model audit, IDS, compare, set
    /// drift.
    ///
    /// A descriptor rather than a screen. It names itself, lists its runs, reads those runs into
    /// findings, declares the columns its findings fill, and offers whichever of the six
    /// capabilities it actually has. The tab holds one check at a time and draws whatever it
    /// returns, so adding a check is one class and one <see cref="CheckCatalogue.Add"/> call and
    /// touches the board, the register and the existing engines nowhere.
    ///
    /// A null capability is the honest answer rather than a degraded mode: six of the seven checks
    /// have nowhere to echo a decision to and no host groups to nest into, and a board that had to
    /// ask about it in a branch of its own would carry that branch for every check.
    /// </summary>
    public interface ICheck
    {
        /// <summary>
        /// The stable key, written to the project file and used as the finding source.
        ///
        /// Never renamed once findings exist. A finding's id is hashed from its source, so renaming
        /// a key re-derives every id under it and detaches every status, assignee, comment and
        /// issue attachment recorded against them.
        /// </summary>
        string Key { get; }

        /// <summary>What the picker shows: "Clash detection", "Clearance", "Fall hazard".</summary>
        string Name { get; }

        /// <summary>What one run is called: "test", "run", "specification", "rule".</summary>
        string RunWord { get; }

        /// <summary>What one result is called: "clash", "breach", "opening", "failure".</summary>
        string ResultWord { get; }

        /// <summary>False when this seat cannot offer the check at all.</summary>
        bool IsAvailable { get; }

        /// <summary>Why not, in one sentence, for the empty state. Null when available.</summary>
        string? Unavailable { get; }

        /// <summary>The per-result columns this check declares, beyond the standing ones.</summary>
        IReadOnlyList<ResultColumn> Columns { get; }

        /// <summary>
        /// How bad, normalised: 1.0 is exactly at the run's threshold, null is no measure.
        ///
        /// Read through the check's own typed accessors and never by parsing a fact string, for
        /// the reasons on <see cref="ResultColumn"/>.
        /// </summary>
        /// <param name="finding">The finding.</param>
        double? Severity(Finding finding);

        /// <summary>
        /// What a severity band is called, in this check's own words. The check owns the wording;
        /// the board bands on the number and formats nothing.
        /// </summary>
        /// <param name="severity">The severity, as <see cref="Severity"/> returned it.</param>
        string SeverityBand(double? severity);

        /// <summary>
        /// Ids this finding may also be recognised under when one run is matched against the
        /// previous one.
        ///
        /// Empty for every check but clash, which offers the 26 neighbouring position cells: a
        /// clash finding's id contains its quantised cell, so a model re-exported 40 mm out would
        /// otherwise report every clash gone and every clash new. A match made through an alias is
        /// counted separately in <see cref="CheckDelta.MatchedOnAlias"/>, because a large count
        /// there means the model has moved.
        /// </summary>
        /// <param name="finding">The finding.</param>
        IEnumerable<string> Aliases(Finding finding);

        /// <summary>Every run this check can offer, without reading any results.</summary>
        IReadOnlyList<CheckRun> Runs();

        /// <summary>
        /// Read results.
        /// </summary>
        /// <param name="runIds">Which runs, or null for every run.</param>
        /// <param name="onProgress">Called with how far it has got; returning false stops it. Null does not report.</param>
        CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress);

        /// <summary>Echoing decisions into the host document, or null when the results live nowhere but here.</summary>
        ICheckHostEcho? HostEcho { get; }

        /// <summary>Nesting into the host's own groups, or null when the host has no groups to nest.</summary>
        ICheckGrouping? Grouping { get; }

        /// <summary>Re-running, or null when a run cannot be run from this tab.</summary>
        ICheckRunner? Runner { get; }

        /// <summary>Renaming and refiling, or null when the run names are not ours to change.</summary>
        ICheckNaming? Naming { get; }

        /// <summary>Element-property columns, or null when there are no elements to read them off.</summary>
        ICheckProperties? Properties { get; }

        /// <summary>Focus, isolate and snapshots, or null when the findings point at nothing.</summary>
        ICheckGeometry? Geometry { get; }
    }
}
