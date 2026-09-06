using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Clash results, as findings.
    ///
    /// The first producer to move. Every check in this product emits the same shape of thing — a
    /// problem, at a place, that somebody has to decide about — and each had grown its own record
    /// and storage. Clash goes first because it has the most at stake and an existing store to be
    /// measured against.
    ///
    /// Identity: a clash finding is its test, its two elements, and where it is. The last is not
    /// decoration — a duct that crosses one beam twice is two results, and a key that ignored
    /// position would fold them into one, the same mistake that put two topics at one path in a BCF
    /// archive. Position is quantised into a cell, so a clash that shifts inside its cell is still
    /// itself, and a clash that shifts further is matched by looking at the neighbouring cells,
    /// which is where tolerance belongs.
    ///
    /// What the host says is an observation rather than a verdict. The engine recomputes New, Active
    /// and Resolved on every run, so reading them back as truth would overwrite a recorded decision
    /// with a machine's guess. Only the two statuses a person sets and the engine never invents —
    /// Reviewed and Approved — are taken from the host as decisions; the rest ride as the run's
    /// opinion and lose to the register's own status through the lattice.
    /// </summary>
    public static class ClashFindings
    {
        /// <summary>
        /// The discriminator that separates one crossing of a pair from another.
        ///
        /// The clash key's cell, and nothing else: the elements are already in the identity, and
        /// the test is the rule.
        /// </summary>
        /// <param name="key">The clash key.</param>
        public static string CellOf(ClashKey key) =>
            key.CellX.ToString(CultureInfo.InvariantCulture) + ","
            + key.CellY.ToString(CultureInfo.InvariantCulture) + ","
            + key.CellZ.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// One clash result, as a finding.
        /// </summary>
        /// <param name="testName">The test that found it — the finding's rule, as it reads.</param>
        /// <param name="result">The result, as the adapter read it.</param>
        /// <param name="cell">The cell size the keys are made with.</param>
        /// <param name="testId">
        /// The host's own id for the test, which is what the identity is made from.
        ///
        /// Renaming a clash test is a first-class feature — <see cref="ClashTestNames"/> exists
        /// only to persist renames on seats where the host refuses the write. Identity made from
        /// the name meant a rename detached every status, assignee, due date and comment under
        /// that test, with no error and nothing on screen.
        /// </param>
        /// <param name="metresPerUnit">
        /// How many metres one model unit is, so the point and the distance are stored in metres.
        ///
        /// The host reports both in the document's own units. A finding read from a file is in
        /// metres already, so a board holding both would otherwise be comparing feet with metres
        /// under one column heading, and the cell a clash is identified by would be a different
        /// physical size on the two legs. One is the right value for a file read and for a metric
        /// document alike.
        /// </param>
        public static Finding From(string testName, ClashResultInfo result,
                                   double cell = ClashKey.DefaultCell, string? testId = null,
                                   double metresPerUnit = 1.0)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var scale = metresPerUnit > 0 ? metresPerUnit : 1.0;

            var rule = string.IsNullOrWhiteSpace(testName) ? "(unnamed test)" : testName;

            var elements = new List<ElementKey>();

            if (!result.A.IsEmpty) elements.Add(result.A);
            if (!result.B.IsEmpty) elements.Add(result.B);

            // Metres before the key, not after: the cell is the occurrence discriminator and a
            // cell measured in feet on one leg and metres on the other splits one clash into two
            // findings with two topic guids.
            var x = result.PointX * scale;
            var y = result.PointY * scale;
            var z = result.PointZ * scale;

            // Only a pair has a key: a result the adapter could not resolve both sides of is
            // identified by the host's own id, and asking for a key would throw.
            var key = elements.Count == 2
                ? ClashKey.Create(result.A, result.B, x, y, z, cell)
                : (ClashKey?)null;

            var where = key.HasValue ? CellOf(key.Value) : result.Id;

            var finding = Finding.Create(FindingSource.Clash, rule,
                                         result.Name ?? "Clash", elements, where,
                                         identity: Identity(testId));

            // The topic this exports as, derived here so a markup coming back from somebody
            // else's tool finds its way home without re-deriving the key.
            //
            // The other half of the agreement is in FindingBcf.TopicGuidFor, which prefers a
            // stamped value over one derived from the finding id. Both writers therefore emit the
            // same topic for the same clash; dropping either half gives one clash two guids.
            if (key.HasValue) finding.TopicGuid = ClashBcf.TopicGuidFor(key.Value);

            // Read out of the host, so the host has it. Anything set on the board afterwards
            // that the host refuses will say so for itself.
            finding.HostHasIt = true;

            finding.Group = string.IsNullOrWhiteSpace(result.GroupName) ? null : result.GroupName;
            finding.X = x;
            finding.Y = y;
            finding.Z = z;
            finding.Detail = Detail(result, scale);

            // The host's own id, so the projection can find the result again without re-deriving
            // it. Not part of identity: the engine reissues these on a re-run.
            finding.Facts[HostId] = result.Id;

            Fact(finding, "Item 1", result.NameA);
            Fact(finding, "Item 2", result.NameB);
            Fact(finding, "Category 1", result.CategoryA);
            Fact(finding, "Category 2", result.CategoryB);

            if (result.Distance != null)
                finding.Facts[Distance] = Written(result.Distance.Value * scale);

            if (!string.IsNullOrWhiteSpace(result.HostAssignedTo))
                finding.AssignedTo = result.HostAssignedTo;

            // See the type remarks: only a status the engine cannot invent is a decision.
            if (FindingStatusLattice.TryParse(result.HostStatus, out var status)
                && FindingStatusLattice.IsHumanJudgement(status))
                finding.Status = status;

            foreach (var comment in result.Comments)
                if (comment != null && !string.IsNullOrWhiteSpace(comment.Body))
                    finding.Comments.Add(new FindingComment(comment.Author, comment.WhenTicks,
                                                            comment.Body) { InHost = true });

            return finding;
        }

        /// <summary>Where the host's own result id is kept, for the projection to find it by.</summary>
        public const string HostId = "Host id";

        /// <summary>
        /// Where how deep the clash is is kept, in metres, negative for a penetration.
        ///
        /// The key is not versioned and is never renamed, although what it holds changed from
        /// model units to metres. <see cref="Findings.FindingRegister"/> merges facts additively
        /// and removes none, so a producer that stopped writing one key and started writing
        /// another would leave the old key drawing its last value on every held finding for the
        /// life of the project. Writing the new number under the old name means the first read
        /// after this change overwrites it.
        /// </summary>
        public const string Distance = "Distance";

        /// <summary>
        /// How deep a clash is, in metres, or null when the producer wrote no distance.
        ///
        /// The typed accessor a column reads. Every caller goes through this rather than parsing
        /// the fact for itself, so there is one place that knows the unit and the format.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public static double? DistanceMetres(Finding? finding) =>
            finding != null
            && finding.Facts.TryGetValue(Distance, out var written)
            && !string.IsNullOrWhiteSpace(written)
            && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
                               out var metres)
                ? metres
                : (double?)null;

        /// <summary>
        /// A length as the fact carries it: metres, to the micrometre.
        ///
        /// Six decimal places rather than the three this used to write. Three was a millimetre of
        /// a model unit, which is the resolution the board prints at, so two clashes a quarter of
        /// a millimetre apart wrote the same string and sorted as equals. Six is below anything a
        /// clash test is run at and still writes a round number as a round number, which matters
        /// because this string is what a BCF description and a spreadsheet cell carry.
        /// </summary>
        /// <param name="metres">The length.</param>
        public static string Written(double metres) =>
            metres.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>Where the test id is kept, for the projection to find the result by.</summary>
        public const string HostTest = "Host test";

        /// <summary>
        /// What stands for a clash test in a finding's identity.
        ///
        /// The host's id where there is one. A test with no id — a hand-raised issue, a board
        /// hydrated from the record — falls back to nothing, and <see cref="Finding.Create"/>
        /// then hashes the rule as it always did.
        /// </summary>
        /// <param name="testId">The host's id for the test, or null.</param>
        public static string? Identity(string? testId) =>
            string.IsNullOrWhiteSpace(testId) ? null : "test:" + testId!.Trim();

        /// <summary>
        /// Whether a finding belongs to one of the tests just read, by id rather than by name.
        ///
        /// The scope a run is observed under. Keyed on the name, a renamed test put its old
        /// findings outside the scope, so they were never marked absent and lingered as orphans
        /// beside a fresh set that arrived as New.
        /// </summary>
        /// <param name="testIds">The host ids of the tests that were read.</param>
        public static Func<Finding, bool> Scope(IEnumerable<string>? testIds)
        {
            var held = new HashSet<string>(testIds ?? Array.Empty<string>(),
                                           StringComparer.Ordinal);

            return finding =>
                finding != null
                && finding.Facts.TryGetValue(HostTest, out var id)
                && id != null
                && held.Contains(id);
        }

        /// <summary>
        /// A whole test's results, as findings.
        /// </summary>
        /// <param name="testId">The host's id for the test, kept so the projection can write back.</param>
        /// <param name="testName">The test's name — the findings' rule.</param>
        /// <param name="results">Its results.</param>
        /// <param name="cell">The cell size the keys are made with.</param>
        /// <param name="metresPerUnit">How many metres one model unit is; 1.0 for a file read.</param>
        public static IReadOnlyList<Finding> ForTest(string testId, string testName,
                                                     IEnumerable<ClashResultInfo>? results,
                                                     double cell = ClashKey.DefaultCell,
                                                     double metresPerUnit = 1.0)
        {
            var found = new List<Finding>();

            foreach (var result in results ?? Array.Empty<ClashResultInfo>())
            {
                if (result == null) continue;

                var finding = From(testName, result, cell, testId, metresPerUnit);

                finding.Facts[HostTest] = testId;
                found.Add(finding);
            }

            return found;
        }

        /// <summary>
        /// The second line: what this clash is, in the terms the row already shows.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <param name="scale">How many metres one model unit is.</param>
        private static string? Detail(ClashResultInfo result, double scale)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(result.NameA) || !string.IsNullOrWhiteSpace(result.NameB))
                parts.Add((result.NameA ?? "?") + " against " + (result.NameB ?? "?"));

            // Derived from the number, and now genuinely the unit it names. The sentence said
            // "m" while the number was in document units, so on a foot-unit model it read as a
            // clash three metres deep.
            if (result.Distance != null)
                parts.Add((result.Distance.Value * scale)
                              .ToString("0.###", CultureInfo.InvariantCulture) + " m");

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        private static void Fact(Finding finding, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) finding.Facts[name] = value;
        }
    }
}
