using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// What a model review found, as findings.
    ///
    /// <b>The producer with least to lose and most to gain.</b> The review had no persistence at
    /// all: it said "37 elements sit on top of each other at the origin", offered to select them,
    /// and forgot the whole thing the moment the tab was left. Nobody could mark one as accepted,
    /// nothing could be assigned, and there was no way to raise an issue from one. All of that
    /// arrives at once by emitting findings.
    ///
    /// <b>Parent and child.</b> One rule failing four hundred elements is one thing to decide
    /// about and four hundred things to look at. So the rule emits a parent carrying the decision
    /// and a child per element carrying the location — which is what <see cref="Finding"/> was
    /// shaped for, and the first producer to use it.
    ///
    /// <b>Identity is the rule, not the sentence.</b> A review headline reads "37 elements sit on
    /// top of each other", with the count in the words: identifying off one would hand every
    /// re-run a fresh finding and throw away every decision anybody had taken. The rule id is what
    /// identifies, and it does not move when the answer does.
    /// </summary>
    public static class HealthFindings
    {
        /// <summary>Where the review's own grade is kept.</summary>
        public const string Grade = "Grade";

        /// <summary>Where what to do about it is kept.</summary>
        public const string Fix = "What to do";

        /// <summary>Where how many it was about at the last run is kept.</summary>
        public const string Counted = "Elements";

        /// <summary>
        /// One review finding, as a finding with a child per element it names.
        /// </summary>
        /// <param name="finding">What the review said.</param>
        /// <param name="cap">
        /// How many children to make at most. A rule that fails ninety thousand elements is one
        /// decision and ninety thousand rows nobody scrolls, and the file it would write is worse
        /// than useless. The parent still says how many there were.
        /// </param>
        public static Finding From(ReviewFinding finding, int cap = DefaultCap)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            var parent = Finding.CreateParent(FindingSource.Health, finding.Check,
                                              finding.Headline, finding.Named);

            parent.Detail = finding.Detail;
            parent.Facts[Grade] = finding.Grade.ToString();

            if (!string.IsNullOrWhiteSpace(finding.Fix)) parent.Facts[Fix] = finding.Fix;

            if (finding.Affected.Count > 0)
                parent.Facts[Counted] =
                    finding.Affected.Count.ToString(CultureInfo.InvariantCulture);

            // A fault is a thing somebody has to settle; a watch is a thing worth knowing. Neither
            // is a decision anybody has taken, so both start where every finding starts.
            var made = 0;

            foreach (var element in finding.Affected)
            {
                if (element.IsEmpty || made >= Math.Max(0, cap)) continue;

                parent.AddChild(Finding.Create(FindingSource.Health, finding.Check,
                                               finding.Headline, new[] { element },
                                               finding.Named));
                made++;
            }

            return parent;
        }

        /// <summary>
        /// How many children one rule may make.
        ///
        /// Five hundred: enough that "show me all of them" is still the whole answer on any
        /// federation somebody actually coordinates, and few enough that a pathological rule
        /// cannot write a hundred-megabyte file behind somebody's back.
        /// </summary>
        public const int DefaultCap = 500;

        /// <summary>A whole review, as findings. Clean checks say nothing, so they make none.</summary>
        /// <param name="review">The review.</param>
        /// <param name="cap">How many children each rule may make.</param>
        public static IReadOnlyList<Finding> For(FederationReview? review, int cap = DefaultCap)
        {
            var found = new List<Finding>();

            if (review == null) return found;

            foreach (var finding in review.Findings)
                if (finding != null && finding.Grade != ReviewGrade.Clean)
                    found.Add(From(finding, cap));

            return found;
        }
    }
}
