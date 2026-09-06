using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Clash
{
    /// <summary>One test as the file records it.</summary>
    public sealed class ClashPackTest
    {
        internal ClashPackTest(string name, string? folder, MatrixTest kind, double tolerance,
                              IReadOnlyDictionary<FindingStatus, int> counts, int groups)
        {
            Name = name; Folder = folder; Kind = kind; Tolerance = tolerance;
            Counts = counts; Groups = groups;
        }

        /// <summary>The test's name.</summary>
        public string Name { get; }

        /// <summary>The folder it sits in, when the matrix nested it.</summary>
        public string? Folder { get; }

        /// <summary>What kind of test it is.</summary>
        public MatrixTest Kind { get; }

        /// <summary>Its tolerance, in model units.</summary>
        public double Tolerance { get; }

        /// <summary>How many results stand at each status.</summary>
        public IReadOnlyDictionary<FindingStatus, int> Counts { get; }

        /// <summary>How many folders the results are in.</summary>
        public int Groups { get; }

        /// <summary>How many results it holds.</summary>
        public int Total => Counts.Values.Sum();

        /// <inheritdoc />
        public override string ToString() => Name + " (" + Measure.Count(Total) + ")";
    }

    /// <summary>One result a file held, whether or not we could tell which elements it is about.</summary>
    public sealed class ClashPackResult
    {
        internal ClashPackResult(string name, string test, string? group, FindingStatus status,
                                double x, double y, double z, ClashItem? item)
        {
            Name = name; Test = test; Group = group; Status = status;
            X = x; Y = y; Z = z; Item = item;
        }

        /// <summary>The file's own id for the result, or null when it carried none.</summary>
        public string? FileGuid { get; internal set; }

        /// <summary>Who the file says it is assigned to, or null.</summary>
        public string? AssignedTo { get; internal set; }

        /// <summary>The authored comment trail the file carried, oldest first.</summary>
        public IReadOnlyList<ClashComment> Comments { get; internal set; } =
            Array.Empty<ClashComment>();

        /// <summary>
        /// The title the file gave it, when that is not the name we would have generated.
        ///
        /// A reviewer who retitles a topic has said something, and a clash result's name is not
        /// the place to put it - renaming results on a synced model would fight the next run.
        /// </summary>
        public string? Title { get; internal set; }

        /// <summary>The priority the file carried. Navisworks clash data has no such field.</summary>
        public string? Priority { get; internal set; }

        /// <summary>The stage the file carried. Navisworks clash data has no such field.</summary>
        public string? Stage { get; internal set; }

        /// <summary>When it is due, as UTC ticks. Zero when the file did not say.</summary>
        public long DueTicks { get; internal set; }

        /// <summary>Labels the file carried that are not ours - the reviewer's own tags.</summary>
        public IReadOnlyList<string> Labels { get; internal set; } = Array.Empty<string>();

        /// <summary>What the file calls it.</summary>
        public string Name { get; }

        /// <summary>Which test it came from.</summary>
        public string Test { get; }

        /// <summary>Which folder inside that test, or null.</summary>
        public string? Group { get; }

        /// <summary>Where it stands.</summary>
        public FindingStatus Status { get; }

        /// <summary>Where it is.</summary>
        public double X { get; }

        /// <summary>Where it is.</summary>
        public double Y { get; }

        /// <summary>Where it is.</summary>
        public double Z { get; }

        /// <summary>The item, when the file carried enough to identify one. Null otherwise.</summary>
        public ClashItem? Item { get; }

        /// <summary>True when this result can be matched back to elements in the model.</summary>
        public bool IsIdentified => Item != null;

        /// <inheritdoc />
        public override string ToString() => Test + " / " + Name;
    }

    /// <summary>A file, read.</summary>
    public sealed class ClashPackRead
    {
        internal ClashPackRead(IReadOnlyList<ClashPackTest> tests, IReadOnlyList<ClashPackResult> results,
                              IReadOnlyList<string> warnings, string? problem)
        {
            Tests = tests; Results = results; Warnings = warnings; Problem = problem;
        }

        /// <summary>The tests the file declared.</summary>
        public IReadOnlyList<ClashPackTest> Tests { get; }

        /// <summary>Every result, identified or not.</summary>
        public IReadOnlyList<ClashPackResult> Results { get; }

        /// <summary>Anything worth saying about what was read.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem { get; }

        /// <summary>
        /// The BCF topics behind these results.
        ///
        /// Kept so a caller can do something with the topics that are not clashes at all - the
        /// ones a reviewer raised in their own tool, which belong in the issue register rather
        /// than nowhere.
        /// </summary>
        public IReadOnlyList<OpenBim.BcfTopic> Topics { get; internal set; } =
            Array.Empty<OpenBim.BcfTopic>();

        /// <summary>True when the file gave up something.</summary>
        public bool IsUsable => Problem == null && Results.Count > 0;

        /// <summary>The results that can be matched back to elements.</summary>
        public IReadOnlyList<ClashItem> Items =>
            Results.Where(r => r.Item != null).Select(r => r.Item!).ToList();

        /// <summary>How many results named no elements we could resolve.</summary>
        public int Unidentified => Results.Count(r => !r.IsIdentified);

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Problem != null) return Problem;
            if (Results.Count == 0) return "The file holds no clash results.";

            var line = Measure.Count(Results.Count)
                     + (Results.Count == 1 ? " result across " : " results across ")
                     + Measure.Count(Tests.Count) + (Tests.Count == 1 ? " test" : " tests");

            if (Unidentified > 0)
                line += ", " + Measure.Count(Unidentified) + " of which name elements this "
                      + "federation does not have";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
