using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Clash
{
    /// <summary>How the file is written.</summary>
    public sealed class ClashXmlOptions
    {
        /// <summary>The smart tag our own key travels in, so a round-trip is exact.</summary>
        public const string KeyTag = "CamelWorks Key";

        /// <summary>Model units, as the header declares them. Navisworks writes "m", "mm", "ft".</summary>
        public string Units { get; set; } = "m";

        /// <summary>Which document this came from, for the header.</summary>
        public string? SourceFile { get; set; }

        /// <summary>
        /// Whether the groups are written as folders rather than flattened away.
        ///
        /// The flag both competitors expose, and for a reason: a reviewer who wants the file in
        /// their own tool usually wants it flat, and a reviewer who is going to work through it
        /// wants the folders that took an afternoon to make. Neither is the right default for the
        /// other, so it is asked rather than assumed.
        /// </summary>
        public bool KeepGroups { get; set; } = true;

        /// <summary>Whether each result carries its two elements' names and properties.</summary>
        public bool IncludeObjects { get; set; } = true;

        /// <summary>What each test is, where the caller knows. Anything absent is inferred.</summary>
        public IDictionary<string, MatrixTest> Kinds { get; } =
            new Dictionary<string, MatrixTest>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When the file was written, in ticks. Zero leaves the date out.</summary>
        public long WhenTicks { get; set; }
    }

    /// <summary>One test as the file records it.</summary>
    public sealed class ClashXmlTest
    {
        internal ClashXmlTest(string name, string? folder, MatrixTest kind, double tolerance,
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

    /// <summary>What was written, and anything the writing had to decide.</summary>
    public sealed class ClashXmlDocument
    {
        internal ClashXmlDocument(string text, IReadOnlyList<ClashXmlTest> tests, int results,
                                 IReadOnlyList<string> warnings)
        {
            Text = text; Tests = tests; Results = results; Warnings = warnings;
        }

        /// <summary>The file.</summary>
        public string Text { get; }

        /// <summary>The tests it holds.</summary>
        public IReadOnlyList<ClashXmlTest> Tests { get; }

        /// <summary>How many results went into it.</summary>
        public int Results { get; }

        /// <summary>Anything worth saying about what was written.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Results == 0
                ? "Nothing to write."
                : Measure.Count(Results) + (Results == 1 ? " result across " : " results across ")
                  + Measure.Count(Tests.Count) + (Tests.Count == 1 ? " test." : " tests.");

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>One result a file held, whether or not we could tell which elements it is about.</summary>
    public sealed class ClashXmlResult
    {
        internal ClashXmlResult(string name, string test, string? group, FindingStatus status,
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
    public sealed class ClashXmlRead
    {
        internal ClashXmlRead(IReadOnlyList<ClashXmlTest> tests, IReadOnlyList<ClashXmlResult> results,
                              IReadOnlyList<string> warnings, string? problem)
        {
            Tests = tests; Results = results; Warnings = warnings; Problem = problem;
        }

        /// <summary>The tests the file declared.</summary>
        public IReadOnlyList<ClashXmlTest> Tests { get; }

        /// <summary>Every result, identified or not.</summary>
        public IReadOnlyList<ClashXmlResult> Results { get; }

        /// <summary>Anything worth saying about what was read.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem { get; }

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

    /// <summary>
    /// Clash results as the interchange format everyone else already reads.
    ///
    /// A report is a deliverable; this is a baseline. It is the file a reviewer opens in their own
    /// tool, works through, and sends back, and it is the only clash format the rest of the
    /// industry has agreed on. Without it a coordination cycle ends at a PDF, and the answers come
    /// back as an email.
    ///
    /// <b>Written to be read by others, and read back exactly by us.</b> The shape is the one
    /// Navisworks writes - an exchange holding a batch test holding tests holding results - so
    /// another tool opens it without being told anything. Our own identity travels in a smart tag,
    /// which every reader is already obliged to skip over, so a file that goes out and comes back
    /// lands on the same elements it left from.
    ///
    /// <b>A file we did not write is still worth reading.</b> Without our smart tag a result cannot
    /// be matched to elements, but its name, position, status and test are all there, and saying
    /// "four hundred results, none of which name elements this federation has" is far more use than
    /// refusing the file.
    /// </summary>
    public static class ClashXml
    {
        /// <summary>Stands for "in no folder", so a grouping key is never null.</summary>
        private const string Loose = "\u0000 no folder";

        /// <summary>Write the file.</summary>
        /// <param name="items">The results, loose. Every one carries the test it came from.</param>
        /// <param name="groups">The board's groups, when the folders are worth keeping.</param>
        /// <param name="options">Units, folders, and what each test is.</param>
        public static ClashXmlDocument Write(IEnumerable<ClashItem>? items,
                                             IEnumerable<ClashGroup>? groups = null,
                                             ClashXmlOptions? options = null)
        {
            var how = options ?? new ClashXmlOptions();
            var warnings = new List<string>();

            var all = (items ?? Array.Empty<ClashItem>()).Where(i => i != null).ToList();

            // Which group each result is in, and which groups span more than one test. A group is
            // written inside a test because that is where the format puts it, so one spanning two
            // tests is written twice - said out loud, because a reviewer counting folders would
            // otherwise find one more than the board showed.
            var groupOf = new Dictionary<ClashKey, string>();
            var spanning = new List<string>();

            foreach (var group in groups ?? Array.Empty<ClashGroup>())
            {
                if (group == null) continue;

                var tests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var member in group.Items)
                {
                    if (member == null) continue;
                    groupOf[member.Key] = group.Name;
                    tests.Add(member.TestName);
                }

                if (tests.Count > 1) spanning.Add(group.Name);
            }

            // No board groups handed in, but items carrying their own group names: the review
            // round writes from the manager's rows, whose grouping lives on each item. Folding
            // them here means the reviewer's folders survive as real <clashgroup> folders rather
            // than only as a tag ours alone reads back.
            if (groupOf.Count == 0)
            {
                foreach (var item in all)
                    if (!string.IsNullOrWhiteSpace(item.CarriedGroup))
                        groupOf[item.Key] = item.CarriedGroup!;
            }

            if (how.KeepGroups && spanning.Count > 0)
                warnings.Add(Measure.Count(spanning.Count)
                    + (spanning.Count == 1 ? " group holds" : " groups hold")
                    + " results from more than one test, so each appears as a folder under every "
                    + "test it reaches: " + string.Join(", ", spanning.Take(4)) + ".");

            var written = new List<ClashXmlTest>();

            var s = new StringBuilder();
            s.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            s.Append("<exchange units=\"").Append(Xml.Escape(how.Units)).Append('"');

            if (!string.IsNullOrWhiteSpace(how.SourceFile))
                s.Append(" filename=\"").Append(Xml.Escape(how.SourceFile)).Append('"');

            s.Append(">\n");
            s.Append("  <batchtest name=\"CamelWorks\" internal_name=\"clashtests\" units=\"")
             .Append(Xml.Escape(how.Units)).Append("\">\n");
            s.Append("    <clashtests>\n");

            foreach (var test in all
                         .GroupBy(i => i.TestName, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var members = test.ToList();
                var kind = Kind(test.Key, members, how);
                var counts = Counts(members);

                var folder = members.Select(m => m.TestFolder)
                                    .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));

                s.Append("      <clashtest name=\"").Append(Xml.Escape(test.Key))
                 .Append("\" test_type=\"").Append(Word(kind))
                 .Append("\" status=\"ok\" tolerance=\"")
                 .Append(Tolerance(members).ToString("0.000000", CultureInfo.InvariantCulture))
                 .Append("\">\n");

                if (!string.IsNullOrWhiteSpace(folder))
                    s.Append("        <clashtestfolder>").Append(Xml.Escape(folder))
                     .Append("</clashtestfolder>\n");

                Summary(s, counts);

                s.Append("        <clashresults>\n");

                var folders = 0;
                var numbered = 0;

                if (how.KeepGroups)
                {
                    foreach (var band in members
                                 .GroupBy(m => groupOf.TryGetValue(m.Key, out var named) ? named : Loose,
                                          StringComparer.Ordinal)
                                 .OrderBy(g => string.Equals(g.Key, Loose, StringComparison.Ordinal) ? 1 : 0)
                                 .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.Equals(band.Key, Loose, StringComparison.Ordinal))
                        {
                            foreach (var member in band) WriteResult(s, member, ++numbered, how, 10);
                            continue;
                        }

                        folders++;

                        s.Append("          <clashgroup name=\"").Append(Xml.Escape(band.Key))
                         .Append("\">\n");

                        foreach (var member in band) WriteResult(s, member, ++numbered, how, 12);

                        s.Append("          </clashgroup>\n");
                    }
                }
                else
                {
                    foreach (var member in members) WriteResult(s, member, ++numbered, how, 10);
                }

                s.Append("        </clashresults>\n");
                s.Append("      </clashtest>\n");

                written.Add(new ClashXmlTest(test.Key, folder, kind, Tolerance(members),
                                             counts, folders));
            }

            s.Append("    </clashtests>\n");
            s.Append("  </batchtest>\n");
            s.Append("</exchange>\n");

            return new ClashXmlDocument(s.ToString(), written, all.Count, warnings);
        }

        /// <summary>Read a file back.</summary>
        /// <param name="text">The file.</param>
        public static ClashXmlRead Read(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Refuse("There is nothing in that file.");

            XElement root;

            try
            {
                root = XElement.Parse(text, LoadOptions.None);
            }
            catch (System.Xml.XmlException e)
            {
                return Refuse("That file is not valid XML: " + e.Message);
            }

            var tests = new List<ClashXmlTest>();
            var results = new List<ClashXmlResult>();
            var warnings = new List<string>();

            var found = root.Name.LocalName == "clashtest"
                ? new[] { root }
                : root.Descendants().Where(e => e.Name.LocalName == "clashtest").ToArray();

            if (found.Length == 0)
                return Refuse(root.Name.LocalName == "exchange"
                    ? "That file is a Navisworks exchange but holds no clash tests."
                    : "That is not a clash file - it has no <clashtest> in it.");

            foreach (var test in found)
            {
                var name = Value(test, "name") ?? "Unnamed test";
                var kind = KindOf(Value(test, "test_type"));

                double.TryParse(Value(test, "tolerance"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var tolerance);

                var folder = test.Elements().FirstOrDefault(e => e.Name.LocalName == "clashtestfolder");

                var counts = new Dictionary<FindingStatus, int>();

                foreach (var entry in test.Descendants().Where(e => e.Name.LocalName == "clashresult"))
                {
                    var band = entry.Ancestors().FirstOrDefault(a => a.Name.LocalName == "clashgroup");

                    var result = ReadResult(entry, name,
                                            band == null ? null : Value(band, "name"), folder?.Value);

                    results.Add(result);

                    counts[result.Status] = counts.TryGetValue(result.Status, out var many)
                        ? many + 1
                        : 1;
                }

                var folders = test.Descendants().Count(e => e.Name.LocalName == "clashgroup");

                tests.Add(new ClashXmlTest(name, folder?.Value, kind, tolerance, counts, folders));
            }

            var strangers = results.Count(r => !r.IsIdentified);

            if (strangers > 0 && strangers == results.Count)
                warnings.Add("No result in that file carries a CamelWorks key, so none of them can "
                    + "be matched to elements here. It reads as a record - names, positions and "
                    + "statuses - rather than something the board can carry over.");
            else if (strangers > 0)
                warnings.Add(Measure.Count(strangers) + " of " + Measure.Count(results.Count)
                    + " results name elements this federation does not have. They are usually from "
                    + "a revision the file was written against and this document has moved past.");

            return new ClashXmlRead(tests, results, warnings, null);
        }

        // ---------------------------------------------------------------------------------------
        // Writing
        // ---------------------------------------------------------------------------------------

        private static void WriteResult(StringBuilder s, ClashItem item, int number,
                                        ClashXmlOptions how, int indent)
        {
            var pad = new string(' ', indent);

            s.Append(pad).Append("<clashresult name=\"Clash").Append(number)
             .Append("\" status=\"").Append(Word(item.Status))
             .Append("\" guid=\"").Append(Stamp(item))
             .Append('"');

            if (item.Distance.HasValue)
                s.Append(" distance=\"")
                 .Append(item.Distance.Value.ToString("0.000000", CultureInfo.InvariantCulture))
                 .Append('"');

            // The review round's two markup fields. Attributes another reader shrugs past, and
            // exactly what the sync needs back.
            if (!string.IsNullOrWhiteSpace(item.AssignedTo))
                s.Append(" assignedto=\"").Append(Xml.Escape(item.AssignedTo!)).Append('"');

            s.Append(">\n");

            if (item.Comments.Count > 0)
            {
                s.Append(pad).Append("  <comments>\n");

                foreach (var comment in item.Comments)
                {
                    if (comment == null || string.IsNullOrWhiteSpace(comment.Body)) continue;

                    s.Append(pad).Append("    <comment user=\"").Append(Xml.Escape(comment.Author))
                     .Append("\" when=\"")
                     .Append(comment.WhenTicks.ToString(CultureInfo.InvariantCulture))
                     .Append("\"><body>").Append(Xml.Escape(comment.Body))
                     .Append("</body></comment>\n");
                }

                s.Append(pad).Append("  </comments>\n");
            }

            s.Append(pad).Append("  <clashpoint><pos3f x=\"")
             .Append(item.X.ToString("0.000000", CultureInfo.InvariantCulture)).Append("\" y=\"")
             .Append(item.Y.ToString("0.000000", CultureInfo.InvariantCulture)).Append("\" z=\"")
             .Append(item.Z.ToString("0.000000", CultureInfo.InvariantCulture))
             .Append("\" /></clashpoint>\n");

            var placed = Located(item);

            if (placed != null)
                s.Append(pad).Append("  <gridlocation>").Append(Xml.Escape(placed))
                 .Append("</gridlocation>\n");

            if (how.WhenTicks > 0) Date(s, how.WhenTicks, pad + "  ");

            s.Append(pad).Append("  <smarttags>\n");

            Tag(s, pad + "    ", ClashXmlOptions.KeyTag, item.Key.ToString());
            Tag(s, pad + "    ", "Level", item.Level);
            Tag(s, pad + "    ", "Grid", item.Grid);
            Tag(s, pad + "    ", "Zone", item.Zone);
            Tag(s, pad + "    ", "Group", item.CarriedGroup);

            if (item.OverlapVolume.HasValue)
                Tag(s, pad + "    ", "Overlap volume",
                    item.OverlapVolume.Value.ToString("0.######", CultureInfo.InvariantCulture));

            if (item.CrossingAngleDegrees.HasValue)
                Tag(s, pad + "    ", "Crossing angle",
                    item.CrossingAngleDegrees.Value.ToString("0.#", CultureInfo.InvariantCulture));

            s.Append(pad).Append("  </smarttags>\n");

            if (how.IncludeObjects)
            {
                s.Append(pad).Append("  <clashobjects>\n");
                Side(s, pad + "    ", item.Key.A, item.ModelA, item.DisciplineA, item.SystemA,
                     item.CategoryA);
                Side(s, pad + "    ", item.Key.B, item.ModelB, item.DisciplineB, item.SystemB,
                     item.CategoryB);
                s.Append(pad).Append("  </clashobjects>\n");
            }

            s.Append(pad).Append("</clashresult>\n");
        }

        private static void Side(StringBuilder s, string pad, ElementKey key, string? model,
                                 string? discipline, string? system, string? category)
        {
            s.Append(pad).Append("<clashobject>\n");

            Property(s, pad + "  ", "Element", key.ToString());
            Property(s, pad + "  ", "Model", model);
            Property(s, pad + "  ", "Discipline", discipline);
            Property(s, pad + "  ", "System", system);
            Property(s, pad + "  ", "Category", category);

            if (!string.IsNullOrWhiteSpace(model))
                s.Append(pad).Append("  <layer>").Append(Xml.Escape(model)).Append("</layer>\n");

            s.Append(pad).Append("</clashobject>\n");
        }

        private static void Property(StringBuilder s, string pad, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            s.Append(pad).Append("<objectattribute><name>").Append(Xml.Escape(name))
             .Append("</name><value>").Append(Xml.Escape(value))
             .Append("</value></objectattribute>\n");
        }

        private static void Tag(StringBuilder s, string pad, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            s.Append(pad).Append("<smarttag><name>").Append(Xml.Escape(name))
             .Append("</name><value>").Append(Xml.Escape(value))
             .Append("</value></smarttag>\n");
        }

        private static void Date(StringBuilder s, long ticks, string pad)
        {
            DateTime when;

            try
            {
                when = new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }

            s.Append(pad).Append("<createddate><date year=\"").Append(when.Year)
             .Append("\" month=\"").Append(when.Month).Append("\" day=\"").Append(when.Day)
             .Append("\" hour=\"").Append(when.Hour).Append("\" minute=\"").Append(when.Minute)
             .Append("\" second=\"").Append(when.Second).Append("\" /></createddate>\n");
        }

        private static void Summary(StringBuilder s, IReadOnlyDictionary<FindingStatus, int> counts)
        {
            s.Append("        <summary");

            foreach (var status in new[]
            {
                FindingStatus.New, FindingStatus.Active, FindingStatus.Reviewed,
                FindingStatus.Approved, FindingStatus.Resolved,
            })
            {
                counts.TryGetValue(status, out var many);
                s.Append(' ').Append(Word(status)).Append("=\"").Append(many).Append('"');
            }

            s.Append(" total=\"").Append(counts.Values.Sum()).Append("\" />\n");
        }

        /// <summary>
        /// A stable id for a result, so the same clash keeps the same guid across exports.
        ///
        /// Derived from the key rather than generated, which is the whole point: a reviewer's tool
        /// tracks results by guid, and a fresh guid every export turns last week's reviewed clash
        /// into this week's new one.
        /// </summary>
        private static string Stamp(ClashItem item)
        {
            // FNV-1a twice, with different offsets, into the sixteen bytes a GUID wants. Not a
            // hash anybody has to trust - it is an identifier, and what it has to be is the same
            // every time the same clash is exported.
            var text = item.Key.ToString();

            var low = Fold(text, 14695981039346656037UL);
            var high = Fold(text + "|b", 1099511628211UL);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:x8}-{1:x4}-{2:x4}-{3:x4}-{4:x12}",
                (uint)(low >> 32), (ushort)(low >> 16), (ushort)low,
                (ushort)(high >> 48), high & 0xFFFFFFFFFFFFUL);
        }

        private static ulong Fold(string text, ulong seed)
        {
            unchecked
            {
                var hash = seed;

                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        private static IReadOnlyDictionary<FindingStatus, int> Counts(IEnumerable<ClashItem> items)
        {
            var counts = new Dictionary<FindingStatus, int>();

            foreach (var item in items)
                counts[item.Status] = counts.TryGetValue(item.Status, out var many) ? many + 1 : 1;

            return counts;
        }

        /// <summary>The tolerance the test must have had, read back off the results.</summary>
        private static double Tolerance(IReadOnlyList<ClashItem> items)
        {
            var distances = items.Where(i => i.Distance.HasValue)
                                 .Select(i => Math.Abs(i.Distance!.Value))
                                 .ToList();

            return distances.Count == 0 ? 0 : distances.Max();
        }

        /// <summary>
        /// What kind of test this was, told or inferred.
        ///
        /// Inferred rather than defaulted, because the distinction is visible in the data: every
        /// distance positive means nothing was touching, which is a clearance test. Guessing "hard"
        /// for a clearance test would put the wrong word in a file somebody else reads.
        /// </summary>
        private static MatrixTest Kind(string name, IReadOnlyList<ClashItem> items,
                                       ClashXmlOptions how)
        {
            if (how.Kinds.TryGetValue(name, out var told)) return told;

            var measured = items.Where(i => i.Distance.HasValue).ToList();

            return measured.Count > 0 && measured.All(i => i.Distance!.Value > 0)
                ? MatrixTest.Clearance
                : MatrixTest.Hard;
        }

        private static MatrixTest KindOf(string? word) =>
            string.Equals(word, "clearance", StringComparison.OrdinalIgnoreCase) ? MatrixTest.Clearance
            : string.Equals(word, "duplicates", StringComparison.OrdinalIgnoreCase) ? MatrixTest.Duplicate
            : string.Equals(word, "duplicate", StringComparison.OrdinalIgnoreCase) ? MatrixTest.Duplicate
            : MatrixTest.Hard;

        private static string Word(MatrixTest kind) => kind switch
        {
            MatrixTest.Clearance => "clearance",
            MatrixTest.Duplicate => "duplicates",
            _ => "hard",
        };

        private static string Word(FindingStatus status) => status switch
        {
            FindingStatus.Active => "active",
            FindingStatus.Reviewed => "reviewed",
            FindingStatus.Approved => "approved",
            FindingStatus.Resolved => "resolved",
            _ => "new",
        };

        private static FindingStatus Status(string? word) =>
            string.Equals(word, "active", StringComparison.OrdinalIgnoreCase) ? FindingStatus.Active
            : string.Equals(word, "reviewed", StringComparison.OrdinalIgnoreCase) ? FindingStatus.Reviewed
            : string.Equals(word, "approved", StringComparison.OrdinalIgnoreCase) ? FindingStatus.Approved
            : string.Equals(word, "resolved", StringComparison.OrdinalIgnoreCase) ? FindingStatus.Resolved
            : FindingStatus.New;

        private static string? Located(ClashItem item)
        {
            var grid = string.IsNullOrWhiteSpace(item.Grid) ? null : item.Grid;
            var level = string.IsNullOrWhiteSpace(item.Level) ? null : item.Level;

            if (grid == null) return level;

            return level == null ? grid : grid + " : " + level;
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------------

        private static ClashXmlResult ReadResult(XElement entry, string test, string? group,
                                                 string? folder)
        {
            var name = Value(entry, "name") ?? "Clash";
            var status = Status(Value(entry, "status") ?? Value(entry, "resultstatus"));
            var assigned = Value(entry, "assignedto");
            var trail = Trail(entry);

            var point = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "pos3f");

            double.TryParse(Value(point, "x"), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var x);
            double.TryParse(Value(point, "y"), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var y);
            double.TryParse(Value(point, "z"), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var z);

            var tags = Tags(entry);

            tags.TryGetValue(ClashXmlOptions.KeyTag, out var wire);

            if (!ClashKey.TryParse(wire, out var key))
                return Dressed(new ClashXmlResult(name, test, group, status, x, y, z, null),
                               entry, assigned, trail);

            var item = new ClashItem(key, test)
            {
                Status = status,
                TestFolder = string.IsNullOrWhiteSpace(folder) ? null : folder,
                X = x,
                Y = y,
                Z = z,
            };

            if (tags.TryGetValue("Level", out var level)) item.Level = level;
            if (tags.TryGetValue("Grid", out var grid)) item.Grid = grid;
            if (tags.TryGetValue("Zone", out var zone)) item.Zone = zone;
            if (tags.TryGetValue("Group", out var carried)) item.CarriedGroup = carried;

            if (tags.TryGetValue("Overlap volume", out var overlap)
                && double.TryParse(overlap, NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out var volume))
                item.OverlapVolume = volume;

            if (tags.TryGetValue("Crossing angle", out var angle)
                && double.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out var degrees))
                item.CrossingAngleDegrees = degrees;

            if (double.TryParse(Value(entry, "distance"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var distance))
                item.Distance = distance;

            // A group written into the file is a group the reviewer worked in, and losing it on the
            // way back is how a folder structure quietly becomes an afternoon somebody repeats.
            if (group != null && item.CarriedGroup == null) item.CarriedGroup = group;

            item.AssignedTo = assigned;
            foreach (var comment in trail) item.Comments.Add(comment);

            Sides(entry, item);

            return Dressed(new ClashXmlResult(name, test, group, status, x, y, z, item),
                           entry, assigned, trail);
        }

        /// <summary>The markup fields, put on the read row whether or not it identified.</summary>
        private static ClashXmlResult Dressed(ClashXmlResult result, XElement entry,
                                              string? assigned, IReadOnlyList<ClashComment> trail)
        {
            result.FileGuid = Value(entry, "guid");
            result.AssignedTo = assigned;
            result.Comments = trail;
            return result;
        }

        /// <summary>The authored comment trail a result carries, oldest first.</summary>
        private static IReadOnlyList<ClashComment> Trail(XElement entry)
        {
            var trail = new List<ClashComment>();

            foreach (var comment in entry.Descendants().Where(e => e.Name.LocalName == "comment"))
            {
                var body = comment.Elements().FirstOrDefault(e => e.Name.LocalName == "body")?.Value
                           ?? comment.Value;

                if (string.IsNullOrWhiteSpace(body)) continue;

                long.TryParse(Value(comment, "when"), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out var when);

                trail.Add(new ClashComment(Value(comment, "user") ?? "(unknown)", when, body.Trim()));
            }

            return trail;
        }

        /// <summary>The two elements' properties, in the order the file lists them.</summary>
        private static void Sides(XElement entry, ClashItem item)
        {
            var objects = entry.Descendants()
                               .Where(e => e.Name.LocalName == "clashobject")
                               .Take(2)
                               .ToList();

            for (var i = 0; i < objects.Count; i++)
            {
                var read = Properties(objects[i]);

                read.TryGetValue("Model", out var model);
                read.TryGetValue("Discipline", out var discipline);
                read.TryGetValue("System", out var system);
                read.TryGetValue("Category", out var category);

                if (i == 0)
                {
                    item.ModelA = model;
                    item.DisciplineA = discipline;
                    item.SystemA = system;
                    item.CategoryA = category;
                }
                else
                {
                    item.ModelB = model;
                    item.DisciplineB = discipline;
                    item.SystemB = system;
                    item.CategoryB = category;
                }
            }
        }

        private static Dictionary<string, string?> Tags(XElement entry) =>
            Pairs(entry, "smarttag");

        private static Dictionary<string, string?> Properties(XElement entry) =>
            Pairs(entry, "objectattribute");

        private static Dictionary<string, string?> Pairs(XElement entry, string element)
        {
            var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in entry.Descendants().Where(e => e.Name.LocalName == element))
            {
                var name = pair.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
                var value = pair.Elements().FirstOrDefault(e => e.Name.LocalName == "value");

                if (name == null || string.IsNullOrWhiteSpace(name.Value)) continue;

                pairs[name.Value.Trim()] = value?.Value;
            }

            return pairs;
        }

        private static string? Value(XElement? element, string name) =>
            element?.Attributes().FirstOrDefault(
                a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

        private static ClashXmlRead Refuse(string why) =>
            new ClashXmlRead(Array.Empty<ClashXmlTest>(), Array.Empty<ClashXmlResult>(),
                             Array.Empty<string>(), why);
    }
}
