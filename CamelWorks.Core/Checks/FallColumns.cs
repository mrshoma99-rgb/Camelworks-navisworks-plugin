using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The three columns a fall hazard fills: how big the opening is, how wide the gap is, and how
    /// much edge has no handrail.
    ///
    /// Declared here rather than on the board, for the reason <see cref="ClearanceColumns"/> is:
    /// the board draws, sorts, bands, filters and exports a column without knowing what check
    /// declared it, and every number handed over is metres at full precision.
    ///
    /// The two passes fill different columns. An opening has an area and a gap and no edge length;
    /// a dangerous run has an edge length and the gap it faces and no area. A blank is the honest
    /// answer for the other, and the board already draws one.
    /// </summary>
    public static class FallColumns
    {
        /// <summary>What the opening-area column is called.</summary>
        public const string Area = "Opening";

        /// <summary>What the widest-gap column is called.</summary>
        public const string Gap = "Widest gap";

        /// <summary>What the unprotected-edge column is called.</summary>
        public const string Edge = "Edge with no rail";

        /// <summary>The three columns, in the order the board lays them out.</summary>
        public static IReadOnlyList<ResultColumn> Declare() =>
            new List<ResultColumn>
            {
                new ResultColumn(FallHazardFindings.Area, Area, ColumnKind.Number)
                {
                    Width = 110,
                    RightAligned = true,
                    Unit = "m²",
                    Number = finding => FallHazardFindings.MetresOf(finding, FallHazardFindings.Area),
                    Format = area => FallHazardFindings.Printed(area) + " m²",
                    Bucket = Band,
                },
                new ResultColumn(FallHazardFindings.WidestGap, Gap, ColumnKind.Number)
                {
                    Width = 110,
                    RightAligned = true,
                    Unit = "m",
                    Number = finding => FallHazardFindings.MetresOf(finding, FallHazardFindings.WidestGap),
                    Format = gap => FallHazardFindings.Printed(gap) + " m",
                    Bucket = Band,
                    IsBad = Over,
                },
                new ResultColumn(FallHazardFindings.EdgeLength, Edge, ColumnKind.Number)
                {
                    Width = 130,
                    RightAligned = true,
                    Unit = "m",
                    Number = finding => FallHazardFindings.MetresOf(finding, FallHazardFindings.EdgeLength),
                    Format = length => FallHazardFindings.Printed(length) + " m",
                    Bucket = Band,
                    IsBad = Over,
                },
            };

        /// <summary>Whether the gap is over the limit the run that found it allowed.</summary>
        /// <param name="finding">The finding.</param>
        private static bool Over(Finding finding)
        {
            var gap = FallHazardFindings.MetresOf(finding, FallHazardFindings.WidestGap);
            var limit = FallHazardFindings.MetresOf(finding, FallHazardFindings.Limit);

            return gap != null && limit != null && limit.Value > 0 && gap.Value > limit.Value;
        }

        /// <summary>A number in the tenth it falls in, for the range filter.</summary>
        /// <param name="value">The number.</param>
        private static string Band(double value)
        {
            var tenth = Math.Floor(value * 10) / 10;

            return tenth.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
