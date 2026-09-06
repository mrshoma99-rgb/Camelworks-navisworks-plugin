using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// Fall hazards, as findings: one per flagged opening, one per dangerous edge run.
    ///
    /// The elements come from the raster's owner grid, so a finding names the slabs the opening is
    /// cut in and the floor the edge runs along. Without them a fall hazard would be a coordinate
    /// and nothing else — nothing to focus, isolate, attach an issue to or send in a pack, which
    /// is what the source produced.
    ///
    /// Identity: the run, the elements, where it is, and which pass found it. The run is in the
    /// identity because two runs over the same floor at different limits are two judgements; the
    /// pass is in it because an opening and the edge around it sit at the same place and are two
    /// pieces of work — one is a hole to cover and the other is a handrail to fit.
    ///
    /// The limit a run asked for is a fact and not part of the identity, for the reason it is on a
    /// clearance breach: re-running at a stricter limit must not lose last week's judgement.
    ///
    /// Every length here is metres.
    /// </summary>
    public static class FallHazardFindings
    {
        /// <summary>Where the opening's plan area is kept, in square metres.</summary>
        public const string Area = "Opening area";

        /// <summary>Where the widest clear span across the void is kept, in metres.</summary>
        public const string WidestGap = "Widest gap";

        /// <summary>Where the length of unprotected edge is kept, in metres.</summary>
        public const string EdgeLength = "Edge length";

        /// <summary>Where the limit the run asked for is kept, in metres.</summary>
        public const string Limit = "Gap allowed";

        /// <summary>Where which pass found it is kept.</summary>
        public const string Pass = "Found by";

        /// <summary>What <see cref="Pass"/> says for a finding from the openings pass.</summary>
        public const string OpeningPass = "The openings pass";

        /// <summary>What <see cref="Pass"/> says for a finding from the edge pass.</summary>
        public const string EdgePass = "The edge pass";

        /// <summary>
        /// How big a cell the position is quantised into, in metres.
        ///
        /// Two metres, as a clearance breach is: an opening is an area somebody could fall
        /// through rather than a point, so one that drifts by less than its own width as the model
        /// moves is still the same opening.
        /// </summary>
        public const double DefaultCell = 2.0;

        /// <summary>
        /// How many elements a finding names.
        ///
        /// A rim can touch a dozen slabs where a floor is modelled in bays, and a Focus that
        /// selects a dozen slabs frames the storey rather than the hole. The commonest few are the
        /// ones the opening is actually cut in.
        /// </summary>
        public const int MostElements = 8;

        /// <summary>The discriminator that separates one hazard from another under the same run.</summary>
        /// <param name="x">Where it is, across, in metres.</param>
        /// <param name="y">Where it is, along, in metres.</param>
        /// <param name="cell">The cell size, in metres.</param>
        public static string CellOf(double x, double y, double cell = DefaultCell)
        {
            var size = cell > 0 ? cell : DefaultCell;

            return ((int)Math.Floor(x / size)).ToString(CultureInfo.InvariantCulture) + ","
                   + ((int)Math.Floor(y / size)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One flagged opening, as a finding.
        /// </summary>
        /// <param name="opening">The opening.</param>
        /// <param name="elements">The elements around it, from the owner grid.</param>
        /// <param name="names">What those elements are called, for the sentence. May be empty.</param>
        /// <param name="levelMetres">The elevation the plan was cut at, in metres.</param>
        /// <param name="limitMetres">The gap this run allowed, in metres.</param>
        /// <param name="cell">The cell size the position is quantised into, in metres.</param>
        /// <param name="runId">The run's stable id, which is part of the finding's identity.</param>
        /// <param name="runName">What the run is called, which is the finding's rule.</param>
        public static Finding FromOpening(FloorOpening opening, IReadOnlyList<ElementKey>? elements,
                                          IReadOnlyList<string?>? names, double levelMetres,
                                          double limitMetres, double cell = DefaultCell,
                                          string? runId = null, string? runName = null)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));

            var finding = Made(elements, runId, runName,
                               Printed(opening.WidestGapMetres) + " m opening in " + Named(names),
                               CellOf(opening.CentreX, opening.CentreY, cell) + "/opening");

            finding.X = opening.CentreX;
            finding.Y = opening.CentreY;
            finding.Z = levelMetres;

            finding.Detail =
                "An opening " + Printed(opening.AreaSquareMetres) + " m² in plan, "
                + Printed(opening.WidestGapMetres) + " m across at its widest, where "
                + Printed(limitMetres) + " m was allowed. Found enclosed by floor at "
                + Printed(levelMetres) + " m.";

            finding.Facts[Pass] = OpeningPass;
            finding.Facts[Area] = Written(opening.AreaSquareMetres);
            finding.Facts[WidestGap] = Written(opening.WidestGapMetres);
            finding.Facts[Limit] = Written(limitMetres);

            return finding;
        }

        /// <summary>
        /// One dangerous edge run, as a finding.
        /// </summary>
        /// <param name="run">The run.</param>
        /// <param name="elements">The floor elements along it, from the owner grid.</param>
        /// <param name="names">What those elements are called, for the sentence. May be empty.</param>
        /// <param name="levelMetres">The elevation the plan was cut at, in metres.</param>
        /// <param name="cell">The cell size the position is quantised into, in metres.</param>
        /// <param name="runId">The run's stable id, which is part of the finding's identity.</param>
        /// <param name="runName">What the run is called, which is the finding's rule.</param>
        public static Finding FromEdge(EdgeRun run, IReadOnlyList<ElementKey>? elements,
                                       IReadOnlyList<string?>? names, double levelMetres,
                                       double cell = DefaultCell,
                                       string? runId = null, string? runName = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var finding = Made(elements, runId, runName,
                               Printed(run.LengthMetres) + " m of unprotected edge on " + Named(names),
                               CellOf(run.CentreX, run.CentreY, cell) + "/edge");

            finding.X = run.CentreX;
            finding.Y = run.CentreY;
            finding.Z = levelMetres;

            finding.Detail =
                Printed(run.LengthMetres) + " m of floor edge with no handrail along it, facing a "
                + Printed(run.FacedGapMetres) + " m gap where " + Printed(run.LimitMetres)
                + " m was allowed.";

            finding.Facts[Pass] = EdgePass;
            finding.Facts[EdgeLength] = Written(run.LengthMetres);
            finding.Facts[WidestGap] = Written(run.FacedGapMetres);
            finding.Facts[Limit] = Written(run.LimitMetres);

            return finding;
        }

        /// <summary>
        /// A whole run's hazards, as findings: the flagged openings, then the dangerous edge runs.
        /// </summary>
        /// <param name="grid">The plan the openings were found on.</param>
        /// <param name="edges">The classified edges, or null when only the openings pass ran.</param>
        /// <param name="owners">The element behind each owner index, in owner order.</param>
        /// <param name="names">What each of those elements is called, in the same order.</param>
        /// <param name="levelMetres">The elevation the plan was cut at, in metres.</param>
        /// <param name="limitMetres">The gap this run allowed, in metres.</param>
        /// <param name="cell">The cell size the position is quantised into, in metres.</param>
        /// <param name="runId">The run's stable id.</param>
        /// <param name="runName">What the run is called.</param>
        public static IReadOnlyList<Finding> For(FloorGapGrid? grid, FloorEdgeResult? edges,
                                                 IReadOnlyList<ElementKey>? owners,
                                                 IReadOnlyList<string?>? names,
                                                 double levelMetres, double limitMetres,
                                                 double cell = DefaultCell,
                                                 string? runId = null, string? runName = null)
        {
            var found = new List<Finding>();

            if (grid == null) return found;

            foreach (var opening in grid.Openings)
            {
                if (!opening.Flagged) continue;

                found.Add(FromOpening(opening, Elements(opening.Owners, owners),
                                      Names(opening.Owners, names), levelMetres, limitMetres,
                                      cell, runId, runName));
            }

            foreach (var run in edges?.Runs ?? Array.Empty<EdgeRun>())
                found.Add(FromEdge(run, Elements(run.Owners, owners), Names(run.Owners, names),
                                   levelMetres, cell, runId, runName));

            return found;
        }

        /// <summary>
        /// A length or an area a fact carries, back as a number, or null when it carries none.
        ///
        /// The typed accessor the columns and the severity read, so nothing above this parses a
        /// fact string for itself.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="key">Which fact.</param>
        public static double? MetresOf(Finding? finding, string? key) =>
            finding != null
            && key != null
            && finding.Facts.TryGetValue(key, out var written)
            && !string.IsNullOrWhiteSpace(written)
            && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : (double?)null;

        /// <summary>Whether a finding came from the edge pass rather than the openings pass.</summary>
        /// <param name="finding">The finding.</param>
        public static bool IsEdge(Finding? finding) =>
            finding != null
            && finding.Facts.TryGetValue(Pass, out var pass)
            && string.Equals(pass, EdgePass, StringComparison.Ordinal);

        /// <summary>A number as a fact carries it: full precision, so nothing is lost to rounding.</summary>
        /// <param name="value">The number.</param>
        public static string Written(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>A number as a sentence prints it, to the millimetre.</summary>
        /// <param name="value">The number.</param>
        public static string Printed(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The finding, with its identity, rule and run membership set.</summary>
        /// <param name="elements">The elements it names.</param>
        /// <param name="runId">The run's stable id.</param>
        /// <param name="runName">What the run is called.</param>
        /// <param name="title">How the board names the row.</param>
        /// <param name="discriminator">What separates it from another hazard under the same run.</param>
        private static Finding Made(IReadOnlyList<ElementKey>? elements, string? runId,
                                    string? runName, string title, string discriminator)
        {
            var keys = new List<ElementKey>();

            foreach (var key in elements ?? Array.Empty<ElementKey>())
                if (!key.IsEmpty && keys.Count < MostElements) keys.Add(key);

            var finding = Finding.Create(
                FindingSource.FallHazard,
                string.IsNullOrWhiteSpace(runName) ? "Fall hazard" : runName!,
                title, keys, discriminator,
                identity: string.IsNullOrWhiteSpace(runId) ? null : runId);

            if (!string.IsNullOrWhiteSpace(runId)) CheckFacts.SetRuns(finding, new[] { runId! });

            return finding;
        }

        /// <summary>The elements a set of owner indices stands for.</summary>
        /// <param name="owners">The owner indices.</param>
        /// <param name="keys">The element behind each owner index, in owner order.</param>
        private static IReadOnlyList<ElementKey> Elements(IReadOnlyList<int> owners,
                                                          IReadOnlyList<ElementKey>? keys)
        {
            var found = new List<ElementKey>();

            if (keys == null) return found;

            foreach (var owner in owners)
                if (owner >= 0 && owner < keys.Count) found.Add(keys[owner]);

            return found;
        }

        /// <summary>What the elements behind a set of owner indices are called.</summary>
        /// <param name="owners">The owner indices.</param>
        /// <param name="names">What each element is called, in owner order.</param>
        private static IReadOnlyList<string?> Names(IReadOnlyList<int> owners,
                                                    IReadOnlyList<string?>? names)
        {
            var found = new List<string?>();

            if (names == null) return found;

            foreach (var owner in owners)
                if (owner >= 0 && owner < names.Count) found.Add(names[owner]);

            return found;
        }

        /// <summary>
        /// What the sentence calls the floor: the commonest element around it, and how many others
        /// there are.
        /// </summary>
        /// <param name="names">The element names, commonest first.</param>
        private static string Named(IReadOnlyList<string?>? names)
        {
            var kept = new List<string>();

            foreach (var name in names ?? Array.Empty<string?>())
                if (!string.IsNullOrWhiteSpace(name)) kept.Add(name!);

            if (kept.Count == 0) return "the floor";
            if (kept.Count == 1) return kept[0];

            return kept[0] + " and " + (kept.Count - 1).ToString(CultureInfo.InvariantCulture)
                   + (kept.Count == 2 ? " other" : " others");
        }
    }
}
