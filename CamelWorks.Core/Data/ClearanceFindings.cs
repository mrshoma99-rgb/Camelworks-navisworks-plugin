using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// Clear-height breaches, as findings.
    ///
    /// <b>The second producer to move, and the one with most to gain.</b> The clearance board kept
    /// its judgement on the row and carried it between runs in memory: close a breach on Tuesday,
    /// close Navisworks, and on Wednesday it is open again. That is the behaviour that makes a
    /// check nobody re-runs, and it is exactly what the findings store exists to end.
    ///
    /// <b>Identity.</b> The floor surface, what is over it, and where — the same shape as a clash,
    /// for the same reason. Two short spots under one slab-and-duct pair are two problems and two
    /// decisions; a spot that shifts a little as the model moves is still itself, because the
    /// position is quantised into a cell rather than compared exactly.
    ///
    /// <b>What is not in the identity.</b> The minimum somebody asked for. Re-running at 2.4 m
    /// after running at 2.1 m finds the same low soffit and must not present it as a new one with
    /// last week's decision lost; the number it fell short of is a fact about the run, and the
    /// board shows it as one.
    /// </summary>
    public static class ClearanceFindings
    {
        /// <summary>
        /// The rule name every clear-height finding carries.
        ///
        /// A constant, not the check's settings: a rule named after the minimum would make every
        /// finding a different finding the moment somebody changed the minimum.
        /// </summary>
        public const string Rule = "Clear height";

        /// <summary>Where what is over the floor is kept.</summary>
        public const string Obstruction = "Obstruction";

        /// <summary>Where the measured height is kept, in metres.</summary>
        public const string Measured = "Clear height";

        /// <summary>Where the minimum this run asked for is kept, in metres.</summary>
        public const string Wanted = "Least allowed";

        /// <summary>Where how far short it fell is kept, in metres.</summary>
        public const string Shortfall = "Short by";

        /// <summary>
        /// How big a cell the position is quantised into, in metres.
        ///
        /// Larger than a clash cell on purpose: a clash is a point where two solids meet, and a
        /// low soffit is an area somebody walks under. Two metres is about the width of the space
        /// the check is describing, so a breach that drifts within one is still the same breach.
        /// </summary>
        public const double DefaultCell = 2.0;

        /// <summary>
        /// The discriminator that separates one short spot from another under the same pair.
        /// </summary>
        /// <param name="x">Where it is, in metres.</param>
        /// <param name="y">Where it is, in metres.</param>
        /// <param name="cell">The cell size, in metres.</param>
        public static string CellOf(double x, double y, double cell = DefaultCell)
        {
            var size = cell > 0 ? cell : DefaultCell;

            return ((int)Math.Floor(x / size)).ToString(CultureInfo.InvariantCulture) + ","
                   + ((int)Math.Floor(y / size)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One breach, as a finding.
        /// </summary>
        /// <param name="span">The span the check measured.</param>
        /// <param name="floor">The surface somebody walks on.</param>
        /// <param name="over">What is over it.</param>
        /// <param name="minimum">The least height this run asked for, in metres.</param>
        /// <param name="title">How the board names the row.</param>
        /// <param name="cell">The cell size the position is quantised into, in metres.</param>
        public static Finding From(HeadroomSpan span, ElementKey floor, ElementKey over,
                                   double minimum, string? title = null,
                                   double cell = DefaultCell)
        {
            if (span == null) throw new ArgumentNullException(nameof(span));

            var elements = new List<ElementKey>();

            if (!floor.IsEmpty) elements.Add(floor);
            if (!over.IsEmpty) elements.Add(over);

            var finding = Finding.Create(
                FindingSource.Headroom, Rule,
                string.IsNullOrWhiteSpace(title) ? Name(span) : title!,
                elements, CellOf(span.X, span.Y, cell));

            finding.X = span.X;
            finding.Y = span.Y;

            // Mid-gap rather than the floor or the soffit: it is the height somebody's head is at,
            // and it is what puts the camera in the space rather than inside the slab.
            finding.Z = span.Floor.MaxZ + (span.Clear / 2);

            finding.Detail = Detail(span, minimum);

            Fact(finding, Obstruction, span.Obstruction.Name);
            Fact(finding, Measured, Metres(span.Clear));
            Fact(finding, Wanted, Metres(minimum));

            var shortBy = Math.Max(0, minimum - span.Clear);

            if (shortBy > 0) finding.Facts[Shortfall] = Metres(shortBy);

            return finding;
        }

        /// <summary>A whole run's breaches, as findings.</summary>
        /// <param name="result">What the check measured.</param>
        /// <param name="keyOf">The element key for one of the check's boxes, by its id.</param>
        /// <param name="cell">The cell size the position is quantised into, in metres.</param>
        public static IReadOnlyList<Finding> For(HeadroomResult? result,
                                                 Func<string, ElementKey>? keyOf,
                                                 double cell = DefaultCell)
        {
            var found = new List<Finding>();

            if (result == null) return found;

            foreach (var span in result.Spans)
            {
                if (span == null) continue;

                var floor = keyOf == null ? default : keyOf(span.Floor.Id);
                var over = keyOf == null ? default : keyOf(span.Obstruction.Id);

                found.Add(From(span, floor, over, result.Minimum, null, cell));
            }

            return found;
        }

        /// <summary>How a breach reads on the board when nobody has renamed it.</summary>
        /// <param name="span">The span.</param>
        public static string Name(HeadroomSpan span) =>
            span == null
                ? Rule
                : Metres(span.Clear) + " m under " + Short(span.Obstruction.Name);

        private static string Detail(HeadroomSpan span, double minimum) =>
            Metres(span.Clear) + " m clear where " + Metres(minimum) + " m was asked for, over "
            + Short(span.Floor.Name);

        private static string Metres(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Short(string? name) =>
            string.IsNullOrWhiteSpace(name) ? "something unnamed" : name!;

        private static void Fact(Finding finding, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) finding.Facts[name] = value;
        }
    }
}
