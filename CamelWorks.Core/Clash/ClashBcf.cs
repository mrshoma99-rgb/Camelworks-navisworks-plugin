using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>How a clash BCF is written.</summary>
    public sealed class ClashBcfOptions
    {
        /// <summary>Which schema to write.</summary>
        public BcfVersion Version { get; set; } = BcfVersion.V21;

        /// <summary>Which document this came from, for the project record and the descriptions.</summary>
        public string? SourceFile { get; set; }

        /// <summary>The project's name, for <c>project.bcfp</c>.</summary>
        public string? ProjectName { get; set; }

        /// <summary>When the file was written, in ticks. Zero means now.</summary>
        public long WhenTicks { get; set; }

        /// <summary>Who is writing it. Every topic needs an author and BCF will not invent one.</summary>
        public string Author { get; set; } = "CamelWorks";

        /// <summary>What each test is, where the caller knows. Anything absent is inferred.</summary>
        public IDictionary<string, MatrixTest> Kinds { get; } =
            new Dictionary<string, MatrixTest>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the group each result is in travels as a label.
        ///
        /// The same flag the XML writer carries, for the same reason: a reviewer who is going to
        /// work through the file wants the folders that took an afternoon to make, and a reviewer
        /// who wants it in their own tool usually wants it flat.
        /// </summary>
        public bool KeepGroups { get; set; } = true;

        /// <summary>Model units per metre, for the viewpoint cameras. BCF cameras are in metres.</summary>
        public double MetresPerUnit { get; set; } = 1.0;
    }

    /// <summary>A written clash BCF, in memory.</summary>
    public sealed class ClashBcfDocument
    {
        internal ClashBcfDocument(byte[] bytes, IReadOnlyList<ClashXmlTest> tests, int results,
                                  IReadOnlyList<string> warnings)
        {
            Bytes = bytes; Tests = tests; Results = results; Warnings = warnings;
        }

        /// <summary>The .bcfzip.</summary>
        public byte[] Bytes { get; }

        /// <summary>The tests it holds.</summary>
        public IReadOnlyList<ClashXmlTest> Tests { get; }

        /// <summary>How many results went into it, one topic each.</summary>
        public int Results { get; }

        /// <summary>Anything worth saying about what was written.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Results == 0
                ? "Nothing to write."
                : Measure.Count(Results) + (Results == 1 ? " clash across " : " clashes across ")
                  + Measure.Count(Tests.Count) + (Tests.Count == 1 ? " test." : " tests.");

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Clash results as BCF, losing nothing.
    ///
    /// <b>Why this replaces the clash XML.</b> The XML this product used to exchange clashes in is
    /// Navisworks' own report format. It works, and exactly one family of tools reads it. BCF is
    /// what the rest of the industry reads and writes - Solibri, BIMcollab, Revizto, BIMTrack,
    /// every issue tracker anyone actually runs a project on - so a clash file written as BCF is a
    /// clash file the architect can open. That is the whole point of exporting one.
    ///
    /// <b>Why nothing is lost by the change.</b> BCF has a field for perhaps a third of what a
    /// clash is: a title, a status, an assignee, comments, and a camera. It has none for the
    /// tolerance the test ran at, the distance between the two elements, the crossing angle, the
    /// level or grid reference, the host's own id for the result, or the keys that let an edit
    /// land back on the right element when the file comes home. A format that dropped those would
    /// not be a format we could run a review round on.
    ///
    /// So they travel in a file of our own, <see cref="Sidecar"/>, inside the same archive. The
    /// BCF specification requires a reader to ignore entries it does not recognise, which is what
    /// makes this safe rather than clever: every other tool reads the topics and never sees the
    /// sidecar, and we read both.
    ///
    /// <b>Which half wins when the file comes back.</b> The topics do, for everything BCF can
    /// carry - status, assignee, comments - because that is what the other tool edited and the
    /// whole reason for sending it. The sidecar wins for identity and geometry, because those are
    /// facts about the model that no reviewer changes and that their tool will have dropped. A
    /// file that has been through somebody else's tool comes back with the sidecar gone entirely;
    /// that reads here as a review round on what BCF does carry, which is most of what a reviewer
    /// does, rather than as a file that cannot be read.
    /// </summary>
    public static class ClashBcf
    {
        /// <summary>
        /// Where the facts BCF has no field for ride, inside the archive.
        ///
        /// <b>A file at the root, deliberately not a folder.</b> A .bcfzip's top-level directories
        /// are its topics - importers routinely enumerate them and read a <c>markup.bcf</c> out of
        /// each - so an extra directory that is not a topic is a shape some importer will meet for
        /// the first time in our file, and a folder whose name will not parse as a GUID is the
        /// kind of thing that throws rather than skips.
        ///
        /// A root-level file is a shape every importer already handles, because the format puts
        /// three of them there: <c>bcf.version</c>, <c>project.bcfp</c> and <c>extensions.xsd</c>.
        /// This sits beside those and looks like nothing new.
        /// </summary>
        public const string Sidecar = "camelworks-clash.json";

        /// <summary>Where it used to ride, still read so an early file keeps working.</summary>
        private const string OldSidecar = "camelworks/clash.json";

        /// <summary>The topic type every clash is written as.</summary>
        public const string TopicType = "Clash";

        /// <summary>What the sidecar's shape is called, so a future change can be told apart.</summary>
        public const int SidecarVersion = 1;

        // Prefixed labels: a coordinator filtering in somebody else's tool gets a usable list, and
        // a file coming home can tell a test name from a level even though BCF has one bag for
        // both.
        private const string TestLabel = "Test: ";
        private const string FolderLabel = "Folder: ";
        private const string GroupLabel = "Group: ";
        private const string LevelLabel = "Level: ";
        private const string GridLabel = "Grid: ";
        private const string ZoneLabel = "Zone: ";

        /// <summary>
        /// Write clash results as a .bcfzip.
        /// </summary>
        /// <param name="items">The results. Anything with an empty key is skipped and counted.</param>
        /// <param name="groups">The groups, when the board has any.</param>
        /// <param name="options">How to write it.</param>
        public static ClashBcfDocument Write(IEnumerable<ClashItem>? items,
                                             IEnumerable<ClashGroup>? groups = null,
                                             ClashBcfOptions? options = null)
        {
            var how = options ?? new ClashBcfOptions();
            var warnings = new List<string>();

            var all = (items ?? Array.Empty<ClashItem>()).Where(i => i != null).ToList();

            var groupOf = new Dictionary<ClashKey, string>();

            foreach (var group in groups ?? Array.Empty<ClashGroup>())
            {
                if (group == null) continue;

                foreach (var member in group.Items)
                    if (member != null) groupOf[member.Key] = group.Name;
            }

            var when = how.WhenTicks > 0
                ? new DateTimeOffset(how.WhenTicks, TimeSpan.Zero)
                : DateTimeOffset.UtcNow;

            var topics = new List<BcfTopic>();
            var records = new List<JsonValue>();
            var tests = new List<ClashXmlTest>();
            var unkeyed = 0;

            foreach (var test in all
                         .GroupBy(i => i.TestName, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var members = test.ToList();
                var numbered = 0;

                var folder = members.Select(m => m.TestFolder)
                                    .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));

                tests.Add(new ClashXmlTest(test.Key, folder, Kind(test.Key, members, how),
                                           Tolerance(members), Counts(members),
                                           members.Select(m => Group(m, groupOf))
                                                  .Where(g => g != null)
                                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                                  .Count()));

                foreach (var item in members)
                {
                    if (item.Key.IsEmpty)
                    {
                        // No key means no way back to the elements, and a topic that names no
                        // element is a note, not a clash. Counted rather than hidden.
                        unkeyed++;
                        continue;
                    }

                    var name = "Clash" + (++numbered).ToString(CultureInfo.InvariantCulture);
                    var group = how.KeepGroups ? Group(item, groupOf) : null;

                    var topic = Topic(item, name, group, when, how);

                    topics.Add(topic);
                    records.Add(Record(item, topic.Guid, name, group));
                }
            }

            if (unkeyed > 0)
                warnings.Add(Measure.Count(unkeyed)
                    + (unkeyed == 1 ? " result names no element" : " results name no elements")
                    + " by key, so " + (unkeyed == 1 ? "it was" : "they were")
                    + " left out: a topic that cannot be pointed at an element is a note rather "
                    + "than a clash.");

            var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Sidecar] = Sheet(records, tests, how, when).ToJson(),
            };

            byte[] bytes;
            BcfWriteResult written;

            using (var buffer = new MemoryStream())
            {
                var project = how.ProjectName == null && how.SourceFile == null
                    ? null
                    : new BcfProject(how.SourceFile ?? how.ProjectName!, how.ProjectName);

                written = BcfWriter.Write(buffer, topics, how.Version, project, extras);
                bytes = buffer.ToArray();
            }

            foreach (var note in written.Notes) warnings.Add(note);

            return new ClashBcfDocument(bytes, tests, topics.Count, warnings);
        }

        /// <summary>
        /// Read a clash BCF back, whoever wrote it.
        /// </summary>
        /// <param name="input">The archive. Left open.</param>
        public static ClashXmlRead Read(Stream? input)
        {
            if (input == null) return Refuse("There is nothing to read.");

            BcfReadResult read;

            try
            {
                read = BcfReader.Read(input);
            }
            catch (Exception error)
            {
                return Refuse("That file could not be read as BCF: " + error.Message);
            }

            var warnings = new List<string>(read.Warnings);

            if (read.Topics.Count == 0)
                return Refuse("That BCF holds no topics, so there is nothing to mark up.");

            var sheet = Sidecars(read, warnings);

            var results = new List<ClashXmlResult>();
            var tests = new List<ClashXmlTest>();
            var byTest = new Dictionary<string, List<ClashXmlResult>>(StringComparer.OrdinalIgnoreCase);
            var strangers = 0;

            foreach (var topic in read.Topics)
            {
                var record = sheet.Records.TryGetValue(topic.Guid, out var found) ? found : null;

                if (record == null) strangers++;

                var result = Result(topic, record);

                results.Add(result);

                if (!byTest.TryGetValue(result.Test, out var list))
                {
                    list = new List<ClashXmlResult>();
                    byTest[result.Test] = list;
                }

                list.Add(result);
            }

            foreach (var pair in byTest.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var known = sheet.Tests.TryGetValue(pair.Key, out var declared) ? declared : null;

                tests.Add(new ClashXmlTest(pair.Key,
                                           known?.Folder,
                                           known?.Kind ?? MatrixTest.Hard,
                                           known?.Tolerance ?? 0,
                                           Counts(pair.Value),
                                           pair.Value.Select(r => r.Group)
                                                     .Where(g => g != null)
                                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                                     .Count()));
            }

            if (sheet.Records.Count == 0)
                warnings.Add("This BCF carries no CamelWorks detail, so it has been through "
                    + "another tool - or was written by one. Statuses, assignments and comments "
                    + "all read normally; distances, tolerances and grid references were not in "
                    + "the file to read. Results still land back on their elements where the "
                    + "topic kept the element ids.");
            else if (strangers > 0)
                warnings.Add(Measure.Count(strangers)
                    + (strangers == 1 ? " topic was" : " topics were")
                    + " added after this file left here - by a reviewer's own tool, most likely. "
                    + (strangers == 1 ? "It is" : "They are") + " read as far as BCF describes "
                    + (strangers == 1 ? "it." : "them."));

            return new ClashXmlRead(tests, results, warnings, null);
        }

        // -------------------------------------------------------------------------------------
        // Writing
        // -------------------------------------------------------------------------------------

        private static BcfTopic Topic(ClashItem item, string name, string? group,
                                      DateTimeOffset when, ClashBcfOptions how)
        {
            // Keyed on the pair rather than on anything the run invented, so the same physical
            // clash is the same topic in every export - which is what lets a reviewer's tool merge
            // this week's file into last week's instead of doubling every issue.
            var topic = new BcfTopic(item.Key.PairId, name, when, how.Author)
            {
                TopicType = TopicType,
                TopicStatus = item.Status.ToString(),
                AssignedTo = string.IsNullOrWhiteSpace(item.AssignedTo) ? null : item.AssignedTo,
                Description = Describe(item),
            };

            topic.Labels.Add(TestLabel + item.TestName);

            if (!string.IsNullOrWhiteSpace(item.TestFolder)) topic.Labels.Add(FolderLabel + item.TestFolder);
            if (!string.IsNullOrWhiteSpace(group)) topic.Labels.Add(GroupLabel + group);
            if (!string.IsNullOrWhiteSpace(item.Level)) topic.Labels.Add(LevelLabel + item.Level);
            if (!string.IsNullOrWhiteSpace(item.Grid)) topic.Labels.Add(GridLabel + item.Grid);
            if (!string.IsNullOrWhiteSpace(item.Zone)) topic.Labels.Add(ZoneLabel + item.Zone);

            var n = 0;

            foreach (var comment in item.Comments)
            {
                if (comment == null) continue;

                var at = comment.WhenTicks > 0
                    ? new DateTimeOffset(comment.WhenTicks, TimeSpan.Zero)
                    : when;

                topic.Comments.Add(new BcfComment(topic.Guid + "-c" + (++n).ToString(CultureInfo.InvariantCulture),
                                                  at, comment.Author, comment.Body));
            }

            topic.Viewpoints.Add(Viewpoint(item, topic.Guid, how));

            return topic;
        }

        /// <summary>
        /// One viewpoint, looking at the clash point, with both elements named.
        ///
        /// The elements are what makes a topic actionable in the receiving tool: a camera position
        /// on its own puts the reviewer in roughly the right place, and a selection puts the two
        /// things that clash on their screen highlighted. Both are written, because a reviewer
        /// whose model has no IFC GUIDs still gets the camera.
        /// </summary>
        private static BcfViewpoint Viewpoint(ClashItem item, string guid, ClashBcfOptions how)
        {
            var scale = how.MetresPerUnit <= 0 ? 1.0 : how.MetresPerUnit;

            var point = (X: item.X * scale, Y: item.Y * scale, Z: item.Z * scale);

            var view = new BcfViewpoint(guid + "-v");

            view.Camera = BcfFraming.Around(new[] { point }).ToCamera();

            var a = Component(item.Key.A);
            var b = Component(item.Key.B);

            view.Selection.Add(a);
            view.Selection.Add(b);

            // Red for the first side, amber for the second: the receiving tool colours them and
            // the reviewer can see which is which without reading the description.
            view.Colouring.Add(new BcfColouring("FF3B30", new[] { a }));
            view.Colouring.Add(new BcfColouring("FF9F0A", new[] { b }));

            return view;
        }

        /// <summary>
        /// One element, as BCF names elements.
        ///
        /// The IFC GUID when the key is one, because that is the only id another tool can resolve;
        /// and our own key in <c>AuthoringToolId</c> regardless, because that is the field BCF
        /// provides for exactly this and it is what lands the edit back on the right element when
        /// the file comes home.
        /// </summary>
        private static BcfComponent Component(ElementKey key)
        {
            var text = key.IsEmpty ? null : key.ToString();

            return new BcfComponent(IfcGuid.OrNull(key.UnscopedValue), text, "CamelWorks");
        }

        /// <summary>What the topic says in prose, for a reader who has only the topic.</summary>
        private static string Describe(ClashItem item)
        {
            var lines = new List<string>();

            var a = item.ModelA ?? item.CategoryA ?? "one element";
            var b = item.ModelB ?? item.CategoryB ?? "another";

            lines.Add(a + " against " + b + ", in test \"" + item.TestName + "\".");

            var facts = new List<string>();

            if (item.Distance.HasValue)
                facts.Add("distance " + item.Distance.Value.ToString("0.###", CultureInfo.InvariantCulture) + " m");

            if (item.OverlapVolume.HasValue)
                facts.Add("overlap " + item.OverlapVolume.Value.ToString("0.####", CultureInfo.InvariantCulture) + " m³");

            if (item.CrossingAngleDegrees.HasValue)
                facts.Add("crossing at " + item.CrossingAngleDegrees.Value.ToString("0", CultureInfo.InvariantCulture) + "°");

            if (!string.IsNullOrWhiteSpace(item.Level)) facts.Add("level " + item.Level);
            if (!string.IsNullOrWhiteSpace(item.Grid)) facts.Add("grid " + item.Grid);
            if (!string.IsNullOrWhiteSpace(item.Zone)) facts.Add("zone " + item.Zone);

            if (facts.Count > 0) lines.Add(string.Join(", ", facts) + ".");

            return string.Join(" ", lines);
        }

        /// <summary>One result's row in the sidecar - everything BCF has no field for.</summary>
        private static JsonValue Record(ClashItem item, string topic, string name, string? group)
        {
            var json = JsonValue.Object()
                .Set("topic", topic)
                .Set("name", name)
                .Set("test", item.TestName)
                .Set("key", item.Key.ToString())
                .Set("x", item.X)
                .Set("y", item.Y)
                .Set("z", item.Z);

            void Text(string field, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) json.Set(field, value);
            }

            void Number(string field, double? value)
            {
                if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                    json.Set(field, value.Value);
            }

            Text("folder", item.TestFolder);
            Text("group", group);
            Text("level", item.Level);
            Text("grid", item.Grid);
            Text("zone", item.Zone);
            Text("modelA", item.ModelA);
            Text("modelB", item.ModelB);
            Text("categoryA", item.CategoryA);
            Text("categoryB", item.CategoryB);
            Text("disciplineA", item.DisciplineA);
            Text("disciplineB", item.DisciplineB);
            Text("systemA", item.SystemA);
            Text("systemB", item.SystemB);

            Number("distance", item.Distance);
            Number("overlap", item.OverlapVolume);
            Number("angle", item.CrossingAngleDegrees);

            if (item.SetsA.Count > 0)
                json.Set("setsA", JsonValue.Array(item.SetsA.OrderBy(s => s, StringComparer.Ordinal)
                                                            .Select(JsonValue.String)));

            if (item.SetsB.Count > 0)
                json.Set("setsB", JsonValue.Array(item.SetsB.OrderBy(s => s, StringComparer.Ordinal)
                                                            .Select(JsonValue.String)));

            if (item.Properties.Count > 0)
            {
                var props = JsonValue.Object();

                foreach (var pair in item.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                    props.Set(pair.Key, pair.Value);

                json.Set("props", props);
            }

            return json;
        }

        /// <summary>The whole sidecar.</summary>
        private static JsonValue Sheet(IReadOnlyList<JsonValue> records,
                                       IReadOnlyList<ClashXmlTest> tests,
                                       ClashBcfOptions how, DateTimeOffset when)
        {
            var json = JsonValue.Object()
                .Set("version", SidecarVersion)
                .Set("written", "CamelWorks")
                .Set("when", when.UtcTicks);

            if (!string.IsNullOrWhiteSpace(how.SourceFile)) json.Set("source", how.SourceFile);

            json.Set("tests", JsonValue.Array(tests.Select(t =>
            {
                var test = JsonValue.Object()
                    .Set("name", t.Name)
                    .Set("kind", t.Kind.ToString())
                    .Set("tolerance", t.Tolerance);

                if (!string.IsNullOrWhiteSpace(t.Folder)) test.Set("folder", t.Folder);

                return test;
            })));

            json.Set("results", JsonValue.Array(records));

            return json;
        }

        // -------------------------------------------------------------------------------------
        // Reading
        // -------------------------------------------------------------------------------------

        private sealed class TestDetail
        {
            internal string? Folder { get; set; }
            internal MatrixTest Kind { get; set; } = MatrixTest.Hard;
            internal double Tolerance { get; set; }
        }

        private sealed class SidecarSheet
        {
            internal Dictionary<string, JsonValue> Records { get; } =
                new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);

            internal Dictionary<string, TestDetail> Tests { get; } =
                new Dictionary<string, TestDetail>(StringComparer.OrdinalIgnoreCase);
        }

        private static SidecarSheet Sidecars(BcfReadResult read, List<string> warnings)
        {
            var sheet = new SidecarSheet();

            if ((!read.Extras.TryGetValue(Sidecar, out var text)
                 && !read.Extras.TryGetValue(OldSidecar, out text))
                || string.IsNullOrWhiteSpace(text))
                return sheet;

            if (!JsonReader.TryParse(text, out var json) || json.Kind != JsonKind.Object)
            {
                warnings.Add("This file carries a CamelWorks detail sheet that could not be read, "
                    + "so it was ignored. The topics themselves read normally.");

                return sheet;
            }

            var version = (int)json["version"].AsLong(SidecarVersion);

            if (version > SidecarVersion)
                warnings.Add("This file's CamelWorks detail was written by a newer version of "
                    + "CamelWorks. Whatever this one understands has been read; anything newer "
                    + "was left alone rather than guessed at.");

            foreach (var record in json["results"].Items)
            {
                var topic = record["topic"].AsString();

                if (!string.IsNullOrEmpty(topic)) sheet.Records[topic!] = record;
            }

            foreach (var test in json["tests"].Items)
            {
                var name = test["name"].AsString();

                if (string.IsNullOrEmpty(name)) continue;

                sheet.Tests[name!] = new TestDetail
                {
                    Folder = test["folder"].AsString(),
                    Kind = Enum.TryParse<MatrixTest>(test["kind"].AsString(), true, out var kind)
                        ? kind
                        : MatrixTest.Hard,
                    Tolerance = test["tolerance"].AsDouble(),
                };
            }

            return sheet;
        }

        /// <summary>
        /// One topic, read back as a clash result.
        ///
        /// The topic decides everything a reviewer touches; the sidecar decides everything about
        /// the model. Where the sidecar is missing - a topic somebody's own tool added, or a whole
        /// file that has been through one - the model facts are recovered as far as BCF describes
        /// them: the element keys off the viewpoint's components, and the point off the camera.
        /// </summary>
        private static ClashXmlResult Result(BcfTopic topic, JsonValue? record)
        {
            var name = record?["name"].AsString() ?? topic.Title;

            var test = record?["test"].AsString()
                       ?? Label(topic, TestLabel)
                       ?? "Imported";

            var group = record?["group"].AsString() ?? Label(topic, GroupLabel);

            var point = Point(topic, record);

            var item = Item(topic, record, test, point);

            var result = new ClashXmlResult(name, test, group, Status(topic),
                                            point.X, point.Y, point.Z, item)
            {
                FileGuid = topic.Guid,
                AssignedTo = string.IsNullOrWhiteSpace(topic.AssignedTo) ? null : topic.AssignedTo,
                Comments = topic.Comments
                                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Text))
                                .Select(c => new ClashComment(c.Author, c.Date.UtcTicks, c.Text!))
                                .ToList(),
            };

            return result;
        }

        /// <summary>The clash item, when the file says enough to make one.</summary>
        private static ClashItem? Item(BcfTopic topic, JsonValue? record, string test,
                                       (double X, double Y, double Z) point)
        {
            var keyed = record?["key"].AsString();

            ClashKey key;

            if (keyed == null || !ClashKey.TryParse(keyed, out key))
            {
                // No sidecar. The components are the only identity left, and BCF's own field for a
                // tool's native id is exactly where ours was written - so a file that went through
                // another tool and kept its components still lands on the right elements.
                var components = topic.Viewpoints
                                      .SelectMany(v => v.Selection)
                                      .Where(c => c != null)
                                      .ToList();

                var keys = new List<ElementKey>();

                foreach (var component in components)
                {
                    if (ElementKey.TryParse(component.AuthoringToolId, out var one) && !one.IsEmpty)
                        keys.Add(one);
                }

                if (keys.Count < 2) return null;

                key = ClashKey.Create(keys[0], keys[1], point.X, point.Y, point.Z);
            }

            var item = new ClashItem(key, test)
            {
                Status = Status(topic),
                AssignedTo = string.IsNullOrWhiteSpace(topic.AssignedTo) ? null : topic.AssignedTo,
                CarriedGroup = record?["group"].AsString() ?? Label(topic, GroupLabel),
                TestFolder = record?["folder"].AsString() ?? Label(topic, FolderLabel),
                Level = record?["level"].AsString() ?? Label(topic, LevelLabel),
                Grid = record?["grid"].AsString() ?? Label(topic, GridLabel),
                Zone = record?["zone"].AsString() ?? Label(topic, ZoneLabel),
                X = point.X,
                Y = point.Y,
                Z = point.Z,
            };

            if (record != null)
            {
                item.ModelA = record["modelA"].AsString();
                item.ModelB = record["modelB"].AsString();
                item.CategoryA = record["categoryA"].AsString();
                item.CategoryB = record["categoryB"].AsString();
                item.DisciplineA = record["disciplineA"].AsString();
                item.DisciplineB = record["disciplineB"].AsString();
                item.SystemA = record["systemA"].AsString();
                item.SystemB = record["systemB"].AsString();

                if (record.Has("distance")) item.Distance = record["distance"].AsDouble();
                if (record.Has("overlap")) item.OverlapVolume = record["overlap"].AsDouble();
                if (record.Has("angle")) item.CrossingAngleDegrees = record["angle"].AsDouble();

                foreach (var set in record["setsA"].Items)
                    if (set.AsString() is string named) item.SetsA.Add(named);

                foreach (var set in record["setsB"].Items)
                    if (set.AsString() is string named) item.SetsB.Add(named);

                var props = record["props"];

                foreach (var field in props.Keys) item.Properties[field] = props[field].AsString();
            }

            foreach (var comment in topic.Comments)
                if (comment != null && !string.IsNullOrWhiteSpace(comment.Text))
                    item.Comments.Add(new ClashComment(comment.Author, comment.Date.UtcTicks,
                                                       comment.Text!));

            return item;
        }

        /// <summary>
        /// Where the clash is.
        ///
        /// The sidecar says exactly, in model units. Without it, the camera is what is left: it
        /// was placed looking at the point, so following its direction by the distance it was
        /// stood back gets there - approximately, which is what an imported file can honestly
        /// offer, and near enough to focus on.
        /// </summary>
        private static (double X, double Y, double Z) Point(BcfTopic topic, JsonValue? record)
        {
            if (record != null && record.Has("x"))
                return (record["x"].AsDouble(), record["y"].AsDouble(), record["z"].AsDouble());

            var camera = topic.Viewpoints.FirstOrDefault(v => v?.Camera != null)?.Camera;

            if (camera == null) return (0, 0, 0);

            var length = Math.Sqrt(camera.DirectionX * camera.DirectionX
                                   + camera.DirectionY * camera.DirectionY
                                   + camera.DirectionZ * camera.DirectionZ);

            if (length <= 0) return (camera.X, camera.Y, camera.Z);

            var back = BcfFraming.MinimumDistance;

            return (camera.X + camera.DirectionX / length * back,
                    camera.Y + camera.DirectionY / length * back,
                    camera.Z + camera.DirectionZ / length * back);
        }

        /// <summary>
        /// The status, mapped from whatever word the sending tool uses.
        ///
        /// BCF does not fix its status list - a project defines its own in <c>extensions</c> - so
        /// an imported file says "Closed", "In Progress" or "Solucionado" as readily as one of
        /// ours. The five we hold are recognised by name and the rest are read for their sense,
        /// which beats reading every unfamiliar word as New.
        /// </summary>
        private static FindingStatus Status(BcfTopic topic)
        {
            var said = (topic.TopicStatus ?? string.Empty).Trim();

            if (Enum.TryParse<FindingStatus>(said, true, out var exact)) return exact;

            var lower = said.ToLowerInvariant();

            if (lower.Length == 0 || lower.Contains("open") || lower.Contains("new"))
                return FindingStatus.New;

            if (lower.Contains("close") || lower.Contains("resolve") || lower.Contains("solve")
                || lower.Contains("fixed") || lower.Contains("done"))
                return FindingStatus.Resolved;

            if (lower.Contains("approve") || lower.Contains("accept")) return FindingStatus.Approved;

            if (lower.Contains("review") || lower.Contains("verif")) return FindingStatus.Reviewed;

            return FindingStatus.Active;
        }

        /// <summary>One prefixed label's value, or null.</summary>
        private static string? Label(BcfTopic topic, string prefix)
        {
            foreach (var label in topic.Labels)
            {
                if (label != null && label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = label.Substring(prefix.Length).Trim();

                    if (value.Length > 0) return value;
                }
            }

            return null;
        }

        // -------------------------------------------------------------------------------------
        // Shared
        // -------------------------------------------------------------------------------------

        private static ClashXmlRead Refuse(string why) =>
            new ClashXmlRead(Array.Empty<ClashXmlTest>(), Array.Empty<ClashXmlResult>(),
                             Array.Empty<string>(), why);

        private static string? Group(ClashItem item, IReadOnlyDictionary<ClashKey, string> groupOf) =>
            groupOf.TryGetValue(item.Key, out var named)
                ? named
                : string.IsNullOrWhiteSpace(item.CarriedGroup) ? null : item.CarriedGroup;

        private static MatrixTest Kind(string test, IReadOnlyList<ClashItem> members,
                                       ClashBcfOptions how)
        {
            if (how.Kinds.TryGetValue(test, out var known)) return known;

            // A test whose results all carry a distance is a clearance test; one whose results
            // overlap is a hard one. Inferred rather than left blank, because the receiving tool
            // shows it and "unknown" reads as a fault in the export.
            if (members.Any(m => m.OverlapVolume.HasValue && m.OverlapVolume.Value > 0))
                return MatrixTest.Hard;

            return members.Any(m => m.Distance.HasValue && m.Distance.Value > 0)
                ? MatrixTest.Clearance
                : MatrixTest.Hard;
        }

        private static double Tolerance(IReadOnlyList<ClashItem> members)
        {
            var distances = members.Where(m => m.Distance.HasValue && m.Distance.Value > 0)
                                   .Select(m => m.Distance!.Value)
                                   .ToList();

            return distances.Count == 0 ? 0 : distances.Min();
        }

        private static IReadOnlyDictionary<FindingStatus, int> Counts(IEnumerable<ClashItem> members)
        {
            var counts = new Dictionary<FindingStatus, int>();

            foreach (var item in members)
            {
                counts.TryGetValue(item.Status, out var seen);
                counts[item.Status] = seen + 1;
            }

            return counts;
        }

        private static IReadOnlyDictionary<FindingStatus, int> Counts(IEnumerable<ClashXmlResult> results)
        {
            var counts = new Dictionary<FindingStatus, int>();

            foreach (var result in results)
            {
                counts.TryGetValue(result.Status, out var seen);
                counts[result.Status] = seen + 1;
            }

            return counts;
        }
    }
}
