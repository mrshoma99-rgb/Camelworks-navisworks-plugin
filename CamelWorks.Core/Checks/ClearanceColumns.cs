using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The four columns a clearance breach fills: the surface, what is over it, the height that was
    /// there and how far short it fell.
    ///
    /// Declared here rather than on the board, for the reason <see cref="ClashColumns"/> is: the
    /// board draws, sorts, bands, filters and exports a column without knowing what check declared
    /// it, and every number a column hands over is metres at full precision.
    /// </summary>
    public static class ClearanceColumns
    {
        /// <summary>What the surface column is called.</summary>
        public const string Surface = "Surface";

        /// <summary>What the obstruction column is called.</summary>
        public const string Over = "What is over it";

        /// <summary>What the measured-height column is called.</summary>
        public const string Clear = "Clear height";

        /// <summary>What the shortfall column is called.</summary>
        public const string Short = "Short by";

        /// <summary>
        /// The four columns, in the order the board lays them out.
        /// </summary>
        /// <param name="minimumMetres">
        /// The minimum the run a finding came from asked for, in metres, or null when it recorded
        /// none. What the height is toned against, so a 2.100 m corridor run and a 2.400 m plant
        /// run are not held to one number.
        /// </param>
        public static IReadOnlyList<ResultColumn> Declare(Func<Finding, double?> minimumMetres)
        {
            if (minimumMetres == null) throw new ArgumentNullException(nameof(minimumMetres));

            return new List<ResultColumn>
            {
                new ResultColumn(ClearanceFindings.Surface, Surface, ColumnKind.Element)
                {
                    ElementIndex = 0,
                    Width = 220,
                    Text = finding => Said(finding, ClearanceFindings.Surface),
                },
                new ResultColumn(ClearanceFindings.Obstruction, Over, ColumnKind.Element)
                {
                    ElementIndex = 1,
                    Width = 220,
                    Text = finding => Said(finding, ClearanceFindings.Obstruction),
                },
                new ResultColumn(ClearanceFindings.Measured, Clear, ColumnKind.Number)
                {
                    Width = 96,
                    RightAligned = true,
                    Unit = "m",
                    Number = finding => ClearanceFindings.MetresOf(finding, ClearanceFindings.Measured),
                    Format = metres => ClearanceFindings.Printed(metres) + " m",
                    Bucket = Band,
                    IsBad = finding => Under(finding, minimumMetres),
                },
                new ResultColumn(ClearanceFindings.Shortfall, Short, ColumnKind.Number)
                {
                    Width = 96,
                    RightAligned = true,
                    Unit = "m",
                    Number = finding => ClearanceFindings.MetresOf(finding, ClearanceFindings.Shortfall),
                    Format = metres => ClearanceFindings.Printed(metres) + " m",
                    Bucket = Band,
                    IsBad = finding => Under(finding, minimumMetres),
                },
            };
        }

        /// <summary>Whether a breach is under the minimum its own run asked for.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="minimumMetres">The run's minimum, in metres, or null when it recorded none.</param>
        private static bool Under(Finding finding, Func<Finding, double?> minimumMetres)
        {
            var wanted = minimumMetres(finding);
            var measured = ClearanceFindings.MetresOf(finding, ClearanceFindings.Measured);

            return wanted != null && measured != null && measured.Value < wanted.Value;
        }

        /// <summary>A height in the tenth of a metre it falls in, for the range filter.</summary>
        /// <param name="metres">The height.</param>
        private static string Band(double metres)
        {
            var tenth = Math.Floor(metres * 10) / 10;

            return tenth.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>What a finding's fact says, or null when it carries none.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="key">Which fact.</param>
        private static string? Said(Finding? finding, string key) =>
            finding != null && finding.Facts.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}
