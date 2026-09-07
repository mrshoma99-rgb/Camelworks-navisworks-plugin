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
    /// The review previously had no persistence: it reported what it found, offered to select the
    /// elements, and discarded everything on leaving the tab. Nothing could be marked accepted,
    /// assigned, or raised as an issue. Emitting findings supplies all three.
    ///
    /// One finding, many locations. One rule failing four hundred elements is one thing to decide
    /// about and four hundred places to look at, so the rule emits one finding carrying the decision
    /// and a location per element carrying the place.
    ///
    /// Identity is the rule rather than the headline. A review headline reads "37 elements sit on
    /// top of each other", with the count in the words, so identifying off one would give every
    /// re-run a fresh finding and discard every decision. The rule id does not move when the answer
    /// does.
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
        /// One review finding, as a finding with a location per element it names.
        /// </summary>
        /// <param name="finding">What the review said.</param>
        /// <param name="cap">
        /// How many locations to make at most. A rule that fails ninety thousand elements is one
        /// decision and ninety thousand places nobody scrolls, and the file it would write is worse
        /// than useless. The finding still says how many there were.
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

                parent.AddLocation(new FindingLocation(parent.Id + "/" + element, new[] { element },
                                                       Held(element)));
                made++;
            }

            return parent;
        }

        /// <summary>
        /// How firmly one element is identified, for the per-place line.
        ///
        /// The review has no per-element text — every element it names failed the same rule for the
        /// same reason, and that reason is the finding's own headline. Repeating the headline on
        /// every place says nothing about the place. What does differ per place is the rung its key
        /// rests on, which is what decides whether the coordinator can trust being sent there.
        /// </summary>
        /// <param name="element">The element at this place.</param>
        private static string Held(ElementKey element) => element.Rung switch
        {
            KeyRung.InstanceGuid => "Identified by instance GUID.",
            KeyRung.TreePath => "Identified by tree path; a re-organised tree breaks it.",
            _ => "Identified by category and size only; treat the match as a proposal.",
        };

        /// <summary>
        /// How many locations one rule may make.
        ///
        /// Five hundred: enough that "show me all of them" is still the whole answer on any
        /// federation somebody actually coordinates, and few enough that a pathological rule
        /// cannot write a hundred-megabyte file behind somebody's back.
        /// </summary>
        public const int DefaultCap = 500;

        /// <summary>A whole review, as findings. Clean checks say nothing, so they make none.</summary>
        /// <param name="review">The review.</param>
        /// <param name="cap">How many locations each rule may make.</param>
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
