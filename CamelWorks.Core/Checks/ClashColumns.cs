using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The columns a clash board draws, declared once for both clash legs.
    ///
    /// The board used to hold these three itself: two element names read straight off the facts at
    /// nine sites, and a depth figure toned against a tolerance field the board kept for whichever
    /// run was open. Declared here instead, so the document leg and the file leg cannot disagree
    /// about what a column means, and so the board has no clash knowledge left in it to carry into
    /// the next check.
    ///
    /// Everything numeric is metres, because that is what the producer now writes on both legs.
    /// The board reads <see cref="ResultColumn.Number"/> and never the fact string.
    /// </summary>
    public static class ClashColumns
    {
        /// <summary>What the first element's column is called.</summary>
        public const string ItemA = "Item 1";

        /// <summary>What the second element's column is called.</summary>
        public const string ItemB = "Item 2";

        /// <summary>What the depth column is called.</summary>
        public const string Worst = "Worst";

        /// <summary>
        /// The tolerance a clash test is toned against when the host records none.
        ///
        /// Clash's own default, held here rather than on the board. On the board it was one field
        /// shared by whatever check was open, so a check with no tolerance at all would have had
        /// its rows toned red at 25 mm.
        /// </summary>
        public const double DefaultToleranceMetres = 0.025;

        /// <summary>
        /// The three columns, in the order the board lays them out.
        /// </summary>
        /// <param name="thresholdMetres">
        /// The tolerance the run a finding came from was set to, in metres, or null when the run
        /// records none. Only the sign-free magnitude is used.
        /// </param>
        public static IReadOnlyList<ResultColumn> Declare(Func<Finding, double?> thresholdMetres)
        {
            if (thresholdMetres == null) throw new ArgumentNullException(nameof(thresholdMetres));

            return new List<ResultColumn>
            {
                new ResultColumn(ItemA, ItemA, ColumnKind.Element)
                {
                    ElementIndex = 0,
                    Text = finding => Said(finding, ItemA),
                },
                new ResultColumn(ItemB, ItemB, ColumnKind.Element)
                {
                    ElementIndex = 1,
                    Text = finding => Said(finding, ItemB),
                },
                new ResultColumn(ClashFindings.Distance, Worst, ColumnKind.Number)
                {
                    Unit = "mm",
                    Width = 64,
                    RightAligned = true,
                    Number = ClashFindings.DistanceMetres,

                    // Whole millimetres, which is what this column has always drawn. Not the
                    // project's own Measure: routing it through that would rewrite every figure
                    // on the board in one phase that is meant to move the plumbing and nothing
                    // else.
                    Format = metres =>
                        (metres * 1000).ToString("0", CultureInfo.InvariantCulture) + " mm",

                    Bucket = Banded,
                    IsBad = finding => Deeper(finding, thresholdMetres),
                },
            };
        }

        /// <summary>
        /// Which range a depth falls in, for the filter menu.
        ///
        /// Ranges rather than values: the menu offers what a column actually holds, and a depth
        /// column over a real test holds several hundred distinct strings, which is a list nobody
        /// can pick out of. The bands are fixed millimetre steps rather than fractions of the
        /// run's tolerance, so two runs at different tolerances band the same physical depth the
        /// same way and a board banded across runs stays readable.
        /// </summary>
        /// <param name="metres">The depth, in metres; negative is a penetration.</param>
        public static string Banded(double metres)
        {
            var mm = metres * 1000;

            if (mm > 0) return "Clear";
            if (mm > -1) return "Touching";
            if (mm > -10) return "Under 10 mm";
            if (mm > -25) return "10 to 25 mm";
            if (mm > -100) return "25 to 100 mm";

            return "Over 100 mm";
        }

        /// <summary>
        /// Whether a clash is a penetration deeper than the run's tolerance.
        ///
        /// Only a penetration: a positive distance is a clearance result that met its gap, and
        /// toning it red would say a passing measurement had failed. A run that records no
        /// tolerance falls back to <see cref="DefaultToleranceMetres"/>, which is what this
        /// figure has always been toned against when the host would not say.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="thresholdMetres">The run's tolerance, in metres, or null.</param>
        public static bool Deeper(Finding? finding, Func<Finding, double?> thresholdMetres)
        {
            if (finding == null || thresholdMetres == null) return false;

            var metres = ClashFindings.DistanceMetres(finding);

            if (metres == null) return false;

            var threshold = thresholdMetres(finding);

            var against = threshold.HasValue && Math.Abs(threshold.Value) > 0
                ? Math.Abs(threshold.Value)
                : DefaultToleranceMetres;

            return metres.Value <= -against;
        }

        /// <summary>One fact, or null.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="key">Which fact.</param>
        private static string? Said(Finding? finding, string key) =>
            finding != null && finding.Facts.TryGetValue(key, out var value) ? value : null;
    }
}
