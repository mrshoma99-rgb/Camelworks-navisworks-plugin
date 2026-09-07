using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// IDS specifications a model failed, as findings.
    ///
    /// The checker gives a verdict per specification and a violation per element, and the tab could
    /// select the failures and write a workbook. What it could not do was remember anything: run it,
    /// agree with the architect that the forty doors without a fire rating are being scheduled
    /// separately, run it again, and the tab reports forty again with nowhere that agreement was
    /// recorded.
    ///
    /// Identity is the specification rather than the count. A specification is the unit that is
    /// argued about — "every door carries a fire rating" — and the number failing it is this run's
    /// answer. Its own identifier is used when the file gives one, because a specification can be
    /// renamed without becoming a different requirement.
    ///
    /// A vacuous pass is not a pass. A specification that applied to nothing is reported by the
    /// checker as its own state and stays one here: "nothing in this model is a door" is a finding
    /// about the model, and burying it in a green tick is how a requirement stops being checked.
    /// </summary>
    public static class IdsFindings
    {
        /// <summary>The rule name every IDS finding carries.</summary>
        public const string Rule = "IDS";

        /// <summary>Where how many elements the specification applied to is kept.</summary>
        public const string Applicable = "Applies to";

        /// <summary>Where how many passed is kept.</summary>
        public const string Passed = "Pass";

        /// <summary>Where how many failed is kept.</summary>
        public const string Failed = "Fail";

        /// <summary>Where the document the specification came from is kept.</summary>
        public const string Document = "Specification file";

        /// <summary>Where what went wrong, in the checker's own words, is kept.</summary>
        public const string Why = "Why";

        /// <summary>
        /// How many locations one specification may make.
        ///
        /// The checker already caps the violations it collects, and this is the second cap: what
        /// reaches the register. A requirement nine thousand elements fail is one argument to have
        /// with one person, and nine thousand places is a file nobody opens twice.
        ///
        /// A thousand: the number the owner's own example uses — one specification, one decision,
        /// a thousand places to look — so the product produces the set the design reasons about.
        /// </summary>
        public const int DefaultCap = 1000;

        /// <summary>
        /// One specification's result, as a finding, or null when the model satisfied it.
        /// </summary>
        /// <param name="result">What the checker said about one specification.</param>
        /// <param name="document">What to record as where the specification came from.</param>
        /// <param name="keyOf">The element key for one of the checker's ids.</param>
        /// <param name="cap">How many locations to make at most.</param>
        public static Finding? From(IdsSpecificationResult result, string? document = null,
                                    Func<string, ElementKey>? keyOf = null,
                                    int cap = DefaultCap)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.IsSatisfied && !result.NothingApplied) return null;

            var named = Named(result.Specification);

            var parent = Finding.CreateParent(FindingSource.Ids, Rule, Headline(result), named);

            parent.Detail = result.Specification?.Description;

            Fact(parent, Document, document);
            Fact(parent, Why, Because(result));

            parent.Facts[Applicable] = Count(result.Applicable);
            parent.Facts[Passed] = Count(result.Passed);
            parent.Facts[Failed] = Count(result.Failed);

            if (keyOf == null) return parent;

            var made = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var violation in result.Violations)
            {
                if (violation == null || made >= Math.Max(0, cap)) continue;
                if (!seen.Add(violation.ElementId)) continue;

                var key = keyOf(violation.ElementId);

                if (key.IsEmpty) continue;

                var location = new FindingLocation(parent.Id + "/" + key, new[] { key },
                                                   violation.Requirement?.Describe());

                location.Facts[Why] = violation.Failure.ToString();

                parent.AddLocation(location);
                made++;
            }

            return parent;
        }

        /// <summary>A whole report, as findings. A specification the model satisfied makes none.</summary>
        /// <param name="report">What the checker said.</param>
        /// <param name="document">What to record as where the specifications came from.</param>
        /// <param name="keyOf">The element key for one of the checker's ids.</param>
        /// <param name="cap">How many locations each specification may make.</param>
        public static IReadOnlyList<Finding> For(IdsReport? report, string? document = null,
                                                 Func<string, ElementKey>? keyOf = null,
                                                 int cap = DefaultCap)
        {
            var found = new List<Finding>();

            if (report == null) return found;

            foreach (var result in report.Results)
            {
                if (result == null) continue;

                if (From(result, document, keyOf, cap) is Finding finding) found.Add(finding);
            }

            return found;
        }

        /// <summary>
        /// What identifies a specification across runs.
        ///
        /// Its own identifier when the file gives one, because a specification can be reworded
        /// without becoming a different requirement; its name otherwise, which is all there is.
        /// </summary>
        /// <param name="specification">The specification.</param>
        public static string Named(IdsSpecification? specification) =>
            specification == null
                ? "(unnamed)"
                : string.IsNullOrWhiteSpace(specification.Identifier)
                    ? specification.Name
                    : specification.Identifier!;

        /// <summary>How a failed specification reads on the board.</summary>
        /// <param name="result">The result.</param>
        public static string Headline(IdsSpecificationResult result)
        {
            if (result == null) return Rule;

            var name = result.Specification?.Name ?? "(unnamed specification)";

            if (result.NothingApplied) return name + " — nothing in this model was checked by it";

            return name + " — " + Count(result.Failed)
                   + (result.Failed == 1 ? " element fails it" : " elements fail it");
        }

        /// <summary>What went wrong, in a phrase, or null when the headline said it all.</summary>
        /// <param name="result">The result.</param>
        public static string? Because(IdsSpecificationResult result)
        {
            if (result == null) return null;

            if (result.NothingApplied)
                return "Nothing matched what it applies to, so it checked nothing. A requirement "
                       + "that quietly stops being checked is worse than one that fails.";

            if (result.CountOutOfRange)
                return "The number of matching elements is outside what the specification allows.";

            if (result.ViolationsTruncated)
                return "More elements fail this than the checker collected; the count is right and "
                       + "the list is not all of them.";

            return null;
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static void Fact(Finding finding, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) finding.Facts[name] = value;
        }
    }
}
