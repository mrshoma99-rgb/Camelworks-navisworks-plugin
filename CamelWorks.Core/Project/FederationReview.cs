using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Project
{
    /// <summary>How badly a check came out.</summary>
    public enum ReviewGrade
    {
        /// <summary>The check ran and found nothing to say.</summary>
        Clean = 0,

        /// <summary>Worth a look before coordinating, but not a reason to stop.</summary>
        Watch = 1,

        /// <summary>Coordinating against this federation will produce wrong answers.</summary>
        Fault = 2,
    }

    /// <summary>
    /// One thing a check found, phrased so it can be ranked against everything else found.
    ///
    /// The point of the type is the grade. Before it, each check painted its own conclusion into
    /// its own card and the reader was left to work out which of five cards mattered most; a
    /// finding that carries its severity can be sorted, counted, and summed into one verdict.
    /// </summary>
    public sealed class ReviewFinding
    {
        /// <summary>A finding from one check.</summary>
        /// <param name="grade">How badly it came out.</param>
        /// <param name="check">Which check produced it, for the reader who wants to re-run it.</param>
        /// <param name="headline">The finding in one line, no hedging.</param>
        /// <param name="detail">Why it matters, in a sentence or two.</param>
        /// <param name="fix">What to do about it, if there is something to do.</param>
        /// <param name="examples">A handful of the elements or models concerned.</param>
        public ReviewFinding(ReviewGrade grade, string check, string headline,
                             string? detail = null, string? fix = null,
                             IReadOnlyList<string>? examples = null)
        {
            if (string.IsNullOrWhiteSpace(check)) throw new ArgumentException("check", nameof(check));
            if (string.IsNullOrWhiteSpace(headline)) throw new ArgumentException("headline", nameof(headline));

            Grade = grade;
            Check = check;
            Headline = headline;
            Detail = detail;
            Fix = fix;
            Examples = examples ?? Array.Empty<string>();
        }

        /// <summary>How badly it came out.</summary>
        public ReviewGrade Grade { get; }

        /// <summary>Which check produced it.</summary>
        public string Check { get; }

        /// <summary>The finding in one line.</summary>
        public string Headline { get; }

        /// <summary>Why it matters, or null.</summary>
        public string? Detail { get; }

        /// <summary>What to do about it, or null when there is nothing to do but know.</summary>
        public string? Fix { get; }

        /// <summary>A handful of the elements or models concerned.</summary>
        public IReadOnlyList<string> Examples { get; }

        /// <inheritdoc/>
        public override string ToString() => Check + ": " + Headline;
    }

    /// <summary>
    /// Every check that ran over this federation, and one answer for the lot of them.
    ///
    /// Five checks that each draw their own conclusion give the reader five conclusions and no
    /// answer. This collects the findings, ranks them worst first, and says the one thing anybody
    /// opens the tab to learn: whether the coordination about to be done on this federation will
    /// be worth anything.
    ///
    /// Checks that found nothing are kept by name rather than discarded. "Nothing found" is only
    /// reassuring when you can see what it was that found nothing.
    /// </summary>
    public sealed class FederationReview
    {
        private readonly List<ReviewFinding> _findings = new List<ReviewFinding>();
        private readonly List<string> _ran = new List<string>();
        private readonly List<string> _skipped = new List<string>();

        /// <summary>Note that a check ran, whatever it found.</summary>
        public FederationReview Ran(string check)
        {
            if (!string.IsNullOrWhiteSpace(check) && !_ran.Contains(check)) _ran.Add(check);
            return this;
        }

        /// <summary>Note that a check did not run, and why.</summary>
        public FederationReview Skipped(string check, string why)
        {
            if (!string.IsNullOrWhiteSpace(check))
                _skipped.Add(check + " - " + why);

            return this;
        }

        /// <summary>Record a finding. The check is registered as having run.</summary>
        public FederationReview Add(ReviewFinding finding)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            Ran(finding.Check);
            if (finding.Grade != ReviewGrade.Clean) _findings.Add(finding);

            return this;
        }

        /// <summary>Record a finding that stops coordination.</summary>
        public FederationReview Fault(string check, string headline, string? detail = null,
                                      string? fix = null, IReadOnlyList<string>? examples = null) =>
            Add(new ReviewFinding(ReviewGrade.Fault, check, headline, detail, fix, examples));

        /// <summary>Record a finding worth knowing that does not stop coordination.</summary>
        public FederationReview Watch(string check, string headline, string? detail = null,
                                      string? fix = null, IReadOnlyList<string>? examples = null) =>
            Add(new ReviewFinding(ReviewGrade.Watch, check, headline, detail, fix, examples));

        /// <summary>The findings, worst first, in the order each check reported them.</summary>
        public IReadOnlyList<ReviewFinding> Findings =>
            _findings.OrderByDescending(f => (int)f.Grade).ToList();

        /// <summary>The checks that ran, in the order they were registered.</summary>
        public IReadOnlyList<string> Checks => _ran;

        /// <summary>The checks that could not run, each with the reason.</summary>
        public IReadOnlyList<string> Missing => _skipped;

        /// <summary>How many findings stop coordination.</summary>
        public int Faults => _findings.Count(f => f.Grade == ReviewGrade.Fault);

        /// <summary>How many findings are worth a look.</summary>
        public int Watches => _findings.Count(f => f.Grade == ReviewGrade.Watch);

        /// <summary>How many checks ran and found nothing.</summary>
        public int Clean => Math.Max(0, _ran.Count - _findings.Select(f => f.Check).Distinct().Count());

        /// <summary>The worst grade anything came out at.</summary>
        public ReviewGrade Grade =>
            Faults > 0 ? ReviewGrade.Fault
            : Watches > 0 ? ReviewGrade.Watch
            : ReviewGrade.Clean;

        /// <summary>Whether any check ran at all.</summary>
        public bool IsEmpty => _ran.Count == 0;

        /// <summary>
        /// The answer, in one line: is this federation fit to coordinate against.
        ///
        /// Deliberately blunt in the failing case. The whole reason the tab exists is that
        /// Navisworks will open a federation with a model missing and three others moved and show
        /// it without comment, so a verdict that hedges is worse than no verdict at all.
        /// </summary>
        public string Verdict()
        {
            if (IsEmpty) return "Nothing has been checked yet.";

            if (Faults > 0)
                return "Not fit to coordinate against - " + Count(Faults, "problem") + " to settle first.";

            if (Watches > 0)
                return "Fit to coordinate against, with " + Count(Watches, "thing") + " worth a look.";

            return "Fit to coordinate against. " + Count(_ran.Count, "check") + " ran, nothing found.";
        }

        /// <inheritdoc/>
        public override string ToString() =>
            Count(_ran.Count, "check") + ", " + Faults.ToString(CultureInfo.InvariantCulture)
            + " failed, " + Watches.ToString(CultureInfo.InvariantCulture) + " worth a look";

        private static string Count(int many, string unit) =>
            many.ToString(CultureInfo.InvariantCulture) + " " + unit + (many == 1 ? string.Empty : "s");
    }
}
