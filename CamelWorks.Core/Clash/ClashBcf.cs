using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Clash's view of a BCF pack: the topic a clash is written under, and a returned file read as
    /// clash results.
    ///
    /// This used to be the second of three writers over one format. It writes nothing now:
    /// <see cref="FindingBcf"/> writes every check's pack, including this one's, and what is left
    /// here is the part that is genuinely about clash — the topic guid a clash key derives, the
    /// neighbouring cells a moved clash may have gone out under, and the projection of a returned
    /// pack onto <see cref="ClashItem"/>s for the board that draws them.
    /// </summary>
    public static class ClashBcf
    {
        /// <summary>The topic type every clash is written as.</summary>
        public const string TopicType = "Clash";

        /// <summary>
        /// The topic a clash is always written as.
        ///
        /// This is what keeps the round trip working. Everything else a returned file might carry —
        /// the sidecar, the element keys in <c>AuthoringToolId</c> — is ours, and a tool that
        /// re-authors the archive drops all of it. The topic GUID is the one field it cannot drop:
        /// it is the topic's identity, it names the folder, and BCF's own merge model is built on
        /// it. Because it is a plain function of the clash, the receiving side can compute it again
        /// from the model, so a file that comes back stripped to nothing but its topics still lands
        /// on the right results.
        ///
        /// The whole key is used, including where the clash is. This was briefly derived from the
        /// element pair alone, on the reasoning that a conflict which shifts a few millimetres
        /// between runs is the same conflict. That was wrong: a duct that crosses one beam twice is
        /// two results and would have become one topic — two <c>markup.bcf</c> entries at one path,
        /// which is a malformed archive whose second topic most readers silently drop. Coarsely
        /// decomposed models, where a whole system is one item, make that common.
        ///
        /// Uniqueness has to win, so identity is exact. Tolerance for a clash that moved belongs at
        /// match time, where the sync can look through the neighbouring cells for a topic that
        /// matched nothing, and only for the few that did not.
        /// </summary>
        /// <param name="key">The clash.</param>
        public static string TopicGuidFor(ClashKey key) => BcfGuid.For(key.ToString());

        /// <summary>
        /// The topic ids a clash could have been written as if it has moved since - itself first,
        /// then the twenty-six cells around it.
        ///
        /// For the sync, and only for the topics that matched nothing exactly: a conflict whose
        /// point drifted across a cell boundary between runs is still that conflict, and coming
        /// home as a topic nobody can place would reset a status somebody had already set.
        /// </summary>
        /// <param name="key">The clash as the model holds it now.</param>
        public static IEnumerable<string> NearbyTopicGuidsFor(ClashKey key) =>
            key.NeighbourCells().Select(TopicGuidFor);

        /// <summary>
        /// Read a BCF back as clash results, whoever wrote it.
        ///
        /// One reader underneath, in <see cref="FindingBcf.Read"/>: the archive, the topics and the
        /// sidecar are the same for every check, and what happens here is only the projection onto
        /// the shape the clash board and the file check draw. A file that has been through another
        /// tool comes back with the sidecar gone entirely; that is read as a review round on what
        /// BCF does carry rather than as an unreadable file.
        /// </summary>
        /// <param name="input">The archive. Left open.</param>
        public static ClashPackRead Read(Stream? input)
        {
            var pack = FindingBcf.Read(input);

            if (pack.Problem != null) return Refuse(pack.Problem);

            var results = new List<ClashPackResult>();
            var tests = new List<ClashPackTest>();
            var byTest = new Dictionary<string, List<ClashPackResult>>(StringComparer.OrdinalIgnoreCase);

            foreach (var topic in pack.Topics)
            {
                if (topic == null) continue;

                var result = Result(topic, pack.Record(topic.Guid));

                results.Add(result);

                if (!byTest.TryGetValue(result.Test, out var list))
                {
                    list = new List<ClashPackResult>();
                    byTest[result.Test] = list;
                }

                list.Add(result);
            }

            foreach (var pair in byTest.OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
            {
                var members = pair.Value.Select(one => one.Item).Where(one => one != null).ToList();

                tests.Add(new ClashPackTest(
                    pair.Key,
                    pair.Value.Select(one => one.Folder).FirstOrDefault(one => one != null),
                    Kind(members!), Tolerance(members!), Counts(pair.Value),
                    pair.Value.Select(one => one.Group)
                              .Where(one => one != null)
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Count()));
            }

            return new ClashPackRead(tests, results, pack.Warnings, null) { Topics = pack.Topics };
        }

        /// <summary>
        /// One topic, read back as a clash result.
        ///
        /// The topic decides everything a reviewer touches; the sidecar decides everything about
        /// the model. Where the sidecar is missing - a topic somebody's own tool added, or a whole
        /// file that has been through one - the model facts are recovered as far as BCF describes
        /// them: the element keys off the viewpoint's components, and the point off the camera.
        /// </summary>
        private static ClashPackResult Result(BcfTopic topic, FindingRecord? record)
        {
            var test = record?.Rule
                       ?? FindingLabels.Value(topic, FindingLabels.Rule)
                       ?? "Imported";

            var group = FindingLabels.Value(topic, FindingLabels.Group);
            var point = Point(topic, record);
            var item = Item(topic, record, test, point);

            var result = new ClashPackResult(topic.Title, test, group, Status(topic),
                                             point.X, point.Y, point.Z, item)
            {
                FileGuid = topic.Guid,
                Folder = FindingLabels.Value(topic, FindingLabels.Folder),
                AssignedTo = string.IsNullOrWhiteSpace(topic.AssignedTo) ? null : topic.AssignedTo,
                Comments = topic.Comments
                                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Text))
                                .Select(c => new ClashComment(c.Author, c.Date.UtcTicks, c.Text!))
                                .ToList(),
                Title = topic.Title,
                Priority = string.IsNullOrWhiteSpace(topic.Priority) ? null : topic.Priority,
                Stage = string.IsNullOrWhiteSpace(topic.Stage) ? null : topic.Stage,
                DueTicks = topic.DueDate?.UtcTicks ?? 0,

                // Only the reviewer's own tags. Ours came out of the writer and saying them back
                // would be a note about nothing.
                Labels = topic.Labels.Where(one => !FindingLabels.Ours(one)).ToList(),
            };

            if (item != null) item.Review = Review(result);

            return result;
        }

        /// <summary>The clash item, when the file says enough to make one.</summary>
        private static ClashItem? Item(BcfTopic topic, FindingRecord? record, string test,
                                       (double X, double Y, double Z) point)
        {
            ClashKey key;

            if (!Keyed(record, point, out key))
            {
                // No sidecar. The components are the only identity left, and BCF's own field for a
                // tool's native id is exactly where ours was written - so a file that went through
                // another tool and kept its components still lands on the right elements.
                var keys = new List<ElementKey>();

                foreach (var component in topic.Viewpoints.SelectMany(v => v.Selection))
                {
                    if (component != null
                        && ElementKey.TryParse(component.AuthoringToolId, out var one)
                        && !one.IsEmpty)
                        keys.Add(one);
                }

                if (keys.Count < 2) return null;

                key = ClashKey.Create(keys[0], keys[1], point.X, point.Y, point.Z);
            }

            var item = new ClashItem(key, test)
            {
                Name = topic.Title,
                TopicGuid = topic.Guid,
                Status = Status(topic),
                AssignedTo = string.IsNullOrWhiteSpace(topic.AssignedTo) ? null : topic.AssignedTo,
                CarriedGroup = FindingLabels.Value(topic, FindingLabels.Group),
                TestFolder = FindingLabels.Value(topic, FindingLabels.Folder),
                Level = FindingLabels.Value(topic, FindingLabels.Level),
                Grid = FindingLabels.Value(topic, FindingLabels.Grid),
                Zone = FindingLabels.Value(topic, FindingLabels.Zone),
                X = point.X,
                Y = point.Y,
                Z = point.Z,
            };

            if (record != null)
            {
                // Every fact the producer wrote, verbatim. The sidecar is generic, so what comes
                // back is whatever the check put on the finding rather than a list of clash fields
                // this reader knows the names of.
                foreach (var fact in record.Facts)
                    if (!string.IsNullOrWhiteSpace(fact.Value))
                        item.Properties[fact.Key] = fact.Value;

                // Where the writing document had the two elements, so this one can reach them
                // without keying the whole federation to find a hash.
                if (record.Located.Count > 0)
                {
                    item.PathIdA = record.Located[0].PathId;
                    item.TreeA = record.Located[0].TreePath;
                }

                if (record.Located.Count > 1)
                {
                    item.PathIdB = record.Located[1].PathId;
                    item.TreeB = record.Located[1].TreePath;
                }

                item.CategoryA = Fact(record, ClashFindings.CategoryA);
                item.CategoryB = Fact(record, ClashFindings.CategoryB);
                item.Distance = Metres(Fact(record, ClashFindings.Distance));
            }

            foreach (var comment in topic.Comments)
                if (comment != null && !string.IsNullOrWhiteSpace(comment.Text))
                    item.Comments.Add(new ClashComment(comment.Author, comment.Date.UtcTicks,
                                                       comment.Text!));

            // The GlobalIds the file names its components by, so a file that has been through
            // another tool still goes back out able to select its elements.
            var globalIds = topic.Viewpoints
                                 .SelectMany(v => v.Selection)
                                 .Where(c => c?.IfcGuid != null)
                                 .Select(c => c.IfcGuid!)
                                 .ToList();

            if (globalIds.Count > 0) item.IfcGuidA = globalIds[0];
            if (globalIds.Count > 1) item.IfcGuidB = globalIds[1];

            return item;
        }

        /// <summary>
        /// The clash key the sidecar names, exactly as the writing seat held it.
        ///
        /// The two element keys and the cell the finding was discriminated by, which is the string
        /// a clash key parses from. Rebuilding it from the point instead would quantise a second
        /// time and can land a clash in the neighbouring cell, which is a different topic guid.
        /// </summary>
        private static bool Keyed(FindingRecord? record, (double X, double Y, double Z) point,
                                  out ClashKey key)
        {
            key = default;

            if (record == null || record.Elements.Count != 2) return false;

            var a = record.Elements[0].ToString();
            var b = record.Elements[1].ToString();

            // Canonical order, as ClashKey.Create writes it: the record's element list is in the
            // order the producer reported the pair.
            if (string.CompareOrdinal(a, b) > 0) { var swap = a; a = b; b = swap; }

            if (!string.IsNullOrEmpty(record.Discriminator)
                && ClashKey.TryParse(a + "|" + b + "|" + record.Discriminator, out key))
                return true;

            key = ClashKey.Create(record.Elements[0], record.Elements[1], point.X, point.Y, point.Z);

            return true;
        }

        /// <summary>
        /// Where the clash is.
        ///
        /// The sidecar says exactly, in metres. Without it, the camera is what is left: it was
        /// placed looking at the point, so following its direction by the distance it was stood
        /// back gets there - approximately, which is what an imported file can honestly offer, and
        /// near enough to focus on.
        /// </summary>
        private static (double X, double Y, double Z) Point(BcfTopic topic, FindingRecord? record)
        {
            if (record?.X != null && record.Y != null && record.Z != null)
                return (record.X.Value, record.Y.Value, record.Z.Value);

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
        /// <see cref="ClashStatusMap"/> holds the mapping, and every path in and out reads it
        /// through that one call.
        /// </summary>
        private static FindingStatus Status(BcfTopic topic) =>
            ClashStatusMap.Read(topic.TopicStatus);

        /// <summary>
        /// The reviewer's own fields, as a record the project file can keep.
        ///
        /// Null when they set none, so an ordinary result carries nothing and the register stays
        /// the size of what somebody actually wrote.
        /// </summary>
        /// <param name="result">The returned result.</param>
        /// <param name="expectedName">What this export would have called it, so a real retitle can
        /// be told from the generated name coming back unchanged.</param>
        /// <param name="source">Which file it arrived in.</param>
        /// <param name="whenTicks">When it arrived, as UTC ticks. Zero for now.</param>
        public static ClashAside? Review(ClashPackResult? result, string? expectedName = null,
                                         string? source = null, long whenTicks = 0)
        {
            if (result?.FileGuid == null) return null;

            var aside = new ClashAside(result.FileGuid)
            {
                Priority = result.Priority,
                Stage = result.Stage,
                DueTicks = result.DueTicks,
                Source = source,
                WhenTicks = whenTicks > 0 ? whenTicks : DateTime.UtcNow.Ticks,
            };

            if (result.Title != null
                && !string.Equals(result.Title, expectedName ?? result.Name, StringComparison.Ordinal))
                aside.Title = result.Title;

            foreach (var label in result.Labels) aside.Labels.Add(label);

            return aside.IsEmpty ? null : aside;
        }

        /// <summary>
        /// What the reviewer said that a Navisworks clash result has nowhere to keep.
        ///
        /// BCF carries a priority, a due date, a stage, a title and any tags the reviewer adds. A
        /// clash result in Navisworks carries none of them, and dropping them silently is the
        /// failure this round trip exists to avoid: a due date is set, it goes nowhere, and that is
        /// discovered at the coordination meeting.
        ///
        /// So they are written as a comment on the clash, which is the one field the host does have
        /// and which travels back out on the next export. Null when the reviewer changed none of
        /// them, so an ordinary sync adds no noise.
        /// </summary>
        /// <param name="result">The returned result.</param>
        /// <param name="expectedName">What this export would have called it, so a real retitle can
        /// be told from the generated name coming back unchanged.</param>
        public static string? Aside(ClashPackResult? result, string? expectedName = null)
        {
            if (result == null) return null;

            var said = new List<string>();

            if (result.Priority != null) said.Add("priority " + result.Priority);
            if (result.Stage != null) said.Add("stage " + result.Stage);

            if (result.DueTicks > 0)
                said.Add("due " + new DateTime(result.DueTicks, DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (result.Title != null
                && !string.Equals(result.Title, expectedName ?? result.Name, StringComparison.Ordinal))
                said.Add("titled \"" + result.Title + "\"");

            if (result.Labels.Count > 0)
                said.Add("tagged " + string.Join(", ", result.Labels));

            return said.Count == 0
                ? null
                : "Set in the reviewer's tool, which this model has no field for: "
                  + string.Join("; ", said) + ".";
        }

        // -------------------------------------------------------------------------------------
        // Shared
        // -------------------------------------------------------------------------------------

        private static ClashPackRead Refuse(string why) =>
            new ClashPackRead(Array.Empty<ClashPackTest>(), Array.Empty<ClashPackResult>(),
                              Array.Empty<string>(), why);

        private static string? Fact(FindingRecord record, string name) =>
            record.Facts.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        /// <summary>A length fact, back as metres, or null when there is none to read.</summary>
        private static double? Metres(string? written) =>
            written != null
            && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
                               out var metres)
                ? metres
                : (double?)null;

        /// <summary>
        /// What kind of test these results came from.
        ///
        /// Inferred rather than left blank, because the receiving tool shows it and "unknown" reads
        /// as a fault in the export. A test whose results all stand apart is a clearance test; one
        /// whose results overlap is a hard one.
        /// </summary>
        private static MatrixTest Kind(IReadOnlyList<ClashItem> members) =>
            members.Any(one => one.Distance.HasValue && one.Distance.Value > 0)
            && !members.Any(one => one.OverlapVolume.HasValue && one.OverlapVolume.Value > 0)
                ? MatrixTest.Clearance
                : MatrixTest.Hard;

        private static double Tolerance(IReadOnlyList<ClashItem> members)
        {
            var distances = members.Where(one => one.Distance.HasValue && one.Distance.Value > 0)
                                   .Select(one => one.Distance!.Value)
                                   .ToList();

            return distances.Count == 0 ? 0 : distances.Min();
        }

        private static IReadOnlyDictionary<FindingStatus, int> Counts(
            IEnumerable<ClashPackResult> results)
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
