using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// An IDS check as the workbook somebody actually works through.
    ///
    /// The panel says which specifications failed, which is the answer to "are we ready to issue".
    /// It is not the answer to "what do I fix", and that is what gets asked next - by somebody who
    /// is not in Navisworks, on a list they can sort, filter and tick off.
    ///
    /// Two sheets, because they are two jobs. The summary is one row per specification, for the
    /// person deciding. The failures are one row per element per requirement, for the person
    /// fixing - element first, so it sorts into a work list.
    /// </summary>
    public static class IdsSheet
    {
        /// <summary>The tab the summary goes on.</summary>
        public const string SummarySheet = "Specifications";

        /// <summary>The tab the failures go on.</summary>
        public const string FailureSheet = "What to fix";

        /// <summary>Build the workbook.</summary>
        /// <param name="document">The IDS that was checked.</param>
        /// <param name="report">What the check found.</param>
        /// <param name="whenTicks">When the check ran. Zero leaves the line out.</param>
        public static byte[] Write(IdsDocument? document, IdsReport? report, long whenTicks = 0) =>
            XlsxWriter.Write(Sheets(document, report, whenTicks));

        /// <summary>The sheets, so a caller can put them in a bigger workbook.</summary>
        /// <param name="document">The IDS that was checked.</param>
        /// <param name="report">What the check found.</param>
        /// <param name="whenTicks">When the check ran. Zero leaves the line out.</param>
        public static IReadOnlyList<XlsxSheet> Sheets(IdsDocument? document, IdsReport? report,
                                                      long whenTicks = 0)
        {
            var summary = new XlsxSheet(SummarySheet);

            summary.Row("IDS", document?.Title ?? "(none)");

            if (document?.Version != null) summary.Row("Version", document.Version);
            if (document?.Author != null) summary.Row("Author", document.Author);

            var when = When(whenTicks);
            if (when != null) summary.Row("Checked", when);

            summary.Row("Elements read",
                        (report?.ElementsChecked ?? 0).ToString("N0", CultureInfo.InvariantCulture));
            summary.Row();

            summary.Row("Specification", "Verdict", "Applies to", "Pass", "Fail", "Instructions");

            // The header is the row above the rows, and the freeze has to follow it down. Frozen at
            // row one, the six lines of provenance above would scroll away and the column names
            // with them.
            summary.FreezeHeader = false;

            var failures = new XlsxSheet(FailureSheet);

            failures.Row("Element", "Specification", "Requirement", "What is wrong", "Instructions");

            foreach (var result in report?.Results ?? Array.Empty<IdsSpecificationResult>())
            {
                var spec = result.Specification;

                summary.Row(
                    spec.Name,
                    Verdict(result),
                    result.NothingApplied && !result.CountOutOfRange
                        ? "nothing"
                        : result.Applicable.ToString("N0", CultureInfo.InvariantCulture),
                    result.NothingApplied ? null : result.Passed.ToString("N0", CultureInfo.InvariantCulture),
                    result.Failed > 0 ? result.Failed.ToString("N0", CultureInfo.InvariantCulture) : null,
                    spec.Instructions);

                foreach (var violation in result.Violations)
                    failures.Row(violation.ElementId, spec.Name, violation.Requirement.Describe(),
                                 Wrong(violation.Failure),
                                 violation.Requirement.Instructions ?? spec.Instructions);

                // Said in the sheet, not just on screen. A workbook that quietly holds the first
                // hundred failures of four hundred is one somebody works through and believes they
                // have finished.
                if (result.ViolationsTruncated)
                    failures.Row(null, spec.Name, null,
                                 "more failures than this report lists - fix these and run it again",
                                 null);
            }

            return new[] { summary, failures };
        }

        /// <summary>The verdict in a word, matching what the panel says.</summary>
        /// <param name="result">One specification's outcome.</param>
        public static string Verdict(IdsSpecificationResult? result)
        {
            if (result == null) return "unknown";

            if (result.CountOutOfRange)
                return result.Applicable < result.Specification.MinOccurs
                    ? "too few matched"
                    : "too many matched";

            // Never "pass". A specification that matched nothing has told the reader nothing, and
            // showing it green is how an audit gets passed by a model that never loaded properly.
            if (result.NothingApplied) return "nothing matched";

            return result.IsSatisfied ? "pass" : "fail";
        }

        private static string Wrong(IdsFailure failure) => failure switch
        {
            IdsFailure.Missing => "missing",
            IdsFailure.WrongValue => "value not acceptable",
            IdsFailure.Prohibited => "present and must not be",
            _ => "fails",
        };

        private static string? When(long ticks)
        {
            if (ticks <= 0) return null;

            try
            {
                return new DateTime(ticks, DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z";
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
}
