using System.Linq;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class FederationReviewTests
    {
        [Fact]
        public void Nothing_checked_says_so_rather_than_passing()
        {
            // The empty state and the clean state are different answers and must not share a
            // sentence. "Fit to coordinate against" on a federation nobody has checked is a lie
            // the tab would be telling before the button was pressed.
            var review = new FederationReview();

            Assert.True(review.IsEmpty);
            Assert.Equal(ReviewGrade.Clean, review.Grade);
            Assert.Contains("Nothing has been checked", review.Verdict());
        }

        [Fact]
        public void A_check_that_ran_clean_is_counted_not_discarded()
        {
            var review = new FederationReview().Ran("Models").Ran("Geometry").Ran("Codes");

            Assert.Equal(3, review.Clean);
            Assert.Equal(0, review.Faults);
            Assert.Equal(ReviewGrade.Clean, review.Grade);
            Assert.Contains("3 checks ran, nothing found", review.Verdict());
        }

        [Fact]
        public void The_worst_finding_sets_the_verdict()
        {
            var review = new FederationReview()
                .Ran("Codes")
                .Watch("Geometry", "Four elements sit on the origin")
                .Fault("Models", "ARCH.nwc has moved since the last check");

            Assert.Equal(ReviewGrade.Fault, review.Grade);
            Assert.Equal(1, review.Faults);
            Assert.Equal(1, review.Watches);
            Assert.Contains("Not fit to coordinate against", review.Verdict());
        }

        [Fact]
        public void Findings_come_back_worst_first()
        {
            // The reader gets one screen and reads the top of it. If a run produces a moved model
            // and eleven cosmetic notes, the moved model has to be the first line, whichever check
            // happened to report first.
            var review = new FederationReview()
                .Watch("Geometry", "watch one")
                .Fault("Models", "the fault")
                .Watch("Codes", "watch two");

            var order = review.Findings.Select(f => f.Headline).ToList();

            Assert.Equal(new[] { "the fault", "watch one", "watch two" }, order);
        }

        [Fact]
        public void Registering_a_finding_registers_its_check()
        {
            var review = new FederationReview().Watch("Geometry", "something");

            Assert.Contains("Geometry", review.Checks);
            Assert.Equal(0, review.Clean);
        }

        [Fact]
        public void A_check_with_two_findings_is_still_one_check()
        {
            // Clean counts checks, not findings; two problems in the geometry check must not make
            // the arithmetic on screen read minus one.
            var review = new FederationReview()
                .Ran("Models")
                .Watch("Geometry", "one")
                .Fault("Geometry", "two");

            Assert.Equal(2, review.Checks.Count);
            Assert.Equal(1, review.Clean);
        }

        [Fact]
        public void A_clean_grade_passed_in_is_not_a_finding()
        {
            var review = new FederationReview()
                .Add(new ReviewFinding(ReviewGrade.Clean, "Codes", "everything is coded"));

            Assert.Empty(review.Findings);
            Assert.Equal(1, review.Clean);
        }

        [Fact]
        public void A_skipped_check_is_named_with_its_reason_and_does_not_count_as_clean()
        {
            // A check that could not run is the one case where silence is dangerous: no code table
            // means the code check never looked, and a screen saying "nothing found" would be
            // reporting an absence of looking as an absence of problems.
            var review = new FederationReview().Ran("Models").Skipped("Codes", "no code table on this project");

            Assert.Equal(1, review.Clean);
            Assert.Single(review.Missing);
            Assert.Contains("no code table", review.Missing[0]);
        }

        [Fact]
        public void The_singular_is_not_the_plural()
        {
            var one = new FederationReview().Fault("Models", "x");
            var two = new FederationReview().Watch("Models", "x").Watch("Codes", "y");

            Assert.Contains("1 problem to settle", one.Verdict());
            Assert.Contains("2 things worth a look", two.Verdict());
        }
    }
}
