using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>What came back from a .bcfzip.</summary>
    public sealed class BcfReadResult
    {
        internal BcfReadResult(BcfVersion version, IReadOnlyList<BcfTopic> topics, IReadOnlyList<string> warnings)
        {
            Version = version; Topics = topics; Warnings = warnings;
        }

        /// <summary>The version the file declared, or the one that best fits what was in it.</summary>
        public BcfVersion Version { get; }

        /// <summary>The topics.</summary>
        public IReadOnlyList<BcfTopic> Topics { get; }

        /// <summary>
        /// What was wrong with the file, in words, having read it anyway.
        ///
        /// A BCF that another tool exported is a file nobody here can fix. Refusing it because one
        /// topic is missing a creation author helps nobody; saying which topic, and carrying on, is
        /// the behaviour that lets somebody actually receive the issues.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Text files in the archive that the BCF schema knows nothing about, by path.
        ///
        /// A tool is free to put its own files in a .bcfzip, and this is how ours gets its back:
        /// the facts BCF has no field for - a clash's tolerance, its distance, the keys that land
        /// an edit on the right element - travel in one, and a round trip through somebody else's
        /// tool simply drops it, which reads here as an archive with no extras and a review round
        /// that still works on everything BCF does carry.
        /// </summary>
        public IReadOnlyDictionary<string, string> Extras { get; internal set; } =
            new Dictionary<string, string>(0);

        /// <summary>The one-line readout.</summary>
        public override string ToString() =>
            "BCF " + (Version == BcfVersion.V21 ? "2.1" : "3.0") + " · "
            + Topics.Count.ToString("N0", CultureInfo.InvariantCulture) + " topics"
            + (Warnings.Count > 0
                ? " · " + Warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings"
                : string.Empty);
    }

    /// <summary>
    /// Reads a .bcfzip of either version.
    ///
    /// Strict on write, lenient on read. The file being read was produced by another tool and is
    /// routinely a little wrong: a 3.0 file with 2.1's flat comments, a missing creation author, a
    /// topic folder whose name does not match the GUID inside it. None of that is fixable from here
    /// and refusing the file helps nobody, so the reader accepts either shape for every element that
    /// moved between versions and reports what it had to accept.
    /// </summary>
    public static class BcfReader
    {
        /// <summary>Read a .bcfzip.</summary>
        /// <param name="input">The archive. Left open.</param>
        public static BcfReadResult Read(Stream input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var warnings = new List<string>();
            var topics = new List<BcfTopic>();
            var version = BcfVersion.V21;
            var extras = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(0);

            using (var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
            {
                var versionEntry = zip.Entries.FirstOrDefault(
                    e => string.Equals(e.FullName, "bcf.version", StringComparison.OrdinalIgnoreCase));

                if (versionEntry == null)
                {
                    warnings.Add("the archive has no bcf.version; it was read as BCF 2.1");
                }
                else
                {
                    var declared = Parse(versionEntry, warnings)?.Attribute("VersionId")?.Value;
                    if (declared != null && declared.StartsWith("3", StringComparison.Ordinal)) version = BcfVersion.V30;
                    else if (declared == null) warnings.Add("bcf.version declares no VersionId; it was read as BCF 2.1");
                }

                // Ordinal by path, so two reads of one archive produce topics in the same order
                // however the zip happens to be laid out.
                var markups = zip.Entries
                    .Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.Ordinal)
                    .ToList();

                if (markups.Count == 0) warnings.Add("the archive contains no markup.bcf files, so it has no topics");

                // Indexed once. Finding each viewpoint's .bcfv by scanning zip.Entries meant two
                // passes over every entry per viewpoint: an 18,000-clash pack holds about 54,000
                // entries, so one import was up to 2x10^9 string comparisons.
                var files = new Files(zip);

                foreach (var entry in markups)
                {
                    var root = Parse(entry, warnings);
                    if (root == null) continue;

                    var topic = ReadTopic(root, entry.FullName, warnings, files);
                    if (topic != null) topics.Add(topic);
                }

                extras = Extra(zip, warnings);
            }

            return new BcfReadResult(version, topics, warnings) { Extras = extras };
        }

        /// <summary>
        /// The archive's non-BCF text files, by path.
        ///
        /// Capped, and text only. An imported .bcfzip is untrusted input: it can be any size and
        /// contain anything, and a reader that pulls every entry it does not recognise into memory
        /// is a reader somebody can hand a decompression bomb.
        /// </summary>
        private static IReadOnlyDictionary<string, string> Extra(ZipArchive zip,
                                                                 List<string> warnings)
        {
            const long MostPerFile = 32L * 1024 * 1024;
            const int MostFiles = 32;

            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skipped = 0;

            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (found.Count >= MostFiles) { skipped++; continue; }

                var path = entry.FullName;

                if (path.Length == 0 || path.EndsWith("/", StringComparison.Ordinal)) continue;

                if (path.Equals("bcf.version", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("project.bcfp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".bcfv", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.Length > MostPerFile) { skipped++; continue; }

                try
                {
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        found[path] = reader.ReadToEnd();
                    }
                }
                catch (Exception)
                {
                    // Not text, or not readable. Neither is worth failing an import for: the
                    // topics are the file, and this is the part a tool may not even have written.
                    skipped++;
                }
            }

            if (skipped > 0)
                warnings.Add(skipped.ToString(CultureInfo.InvariantCulture)
                    + " extra file(s) in the archive were too large, too many or not text, and "
                    + "were left unread. The topics themselves are unaffected.");

            return found;
        }

        /// <summary>
        /// The archive's entries, indexed once.
        ///
        /// A .bcfv is named for its viewpoint's GUID and sits beside the markup that references it,
        /// so the exact path answers almost every lookup. The two looser tiers are for tools that
        /// file them elsewhere or prefix the name, and they are reached only when the exact path
        /// misses.
        /// </summary>
        private sealed class Files
        {
            private readonly Dictionary<string, ZipArchiveEntry> _byPath =
                new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, ZipArchiveEntry> _byName =
                new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            private readonly List<ZipArchiveEntry> _visualisations = new List<ZipArchiveEntry>();

            internal Files(ZipArchive zip)
            {
                foreach (var entry in zip.Entries)
                {
                    // First wins, so a duplicated path reads the same way every time.
                    if (!_byPath.ContainsKey(entry.FullName)) _byPath[entry.FullName] = entry;

                    if (!entry.FullName.EndsWith(".bcfv", StringComparison.OrdinalIgnoreCase))
                        continue;

                    _visualisations.Add(entry);

                    var cut = entry.FullName.LastIndexOf('/');
                    var name = cut < 0 ? entry.FullName : entry.FullName.Substring(cut + 1);

                    if (!_byName.ContainsKey(name)) _byName[name] = entry;
                }
            }

            /// <summary>The .bcfv for one viewpoint, or null when the archive carries none.</summary>
            /// <param name="folder">The folder the markup referencing it sits in.</param>
            /// <param name="guid">The viewpoint's GUID.</param>
            internal ZipArchiveEntry? Visualisation(string folder, string guid)
            {
                var wanted = guid + ".bcfv";

                if (_byPath.TryGetValue(folder + wanted, out var exact)) return exact;

                if (_byName.TryGetValue(wanted, out var named)
                    && named.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return named;

                foreach (var entry in _visualisations)
                    if (entry.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                        && entry.FullName.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                        return entry;

                return null;
            }
        }

        private static XElement? Parse(ZipArchiveEntry entry, List<string> warnings)
        {
            try
            {
                using (var stream = entry.Open())
                {
                    // No DTD processing, no external resolution: an imported BCF is untrusted
                    // input, and an XML parser that fetches what a document tells it to is a way
                    // to make a coordination file reach across the network.
                    var settings = new System.Xml.XmlReaderSettings
                    {
                        DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                        XmlResolver = null,
                        IgnoreWhitespace = true,
                    };

                    using (var reader = System.Xml.XmlReader.Create(stream, settings))
                        return XDocument.Load(reader).Root;
                }
            }
            catch (System.Xml.XmlException e)
            {
                warnings.Add(entry.FullName + " is not readable XML and was skipped: " + e.Message);
                return null;
            }
            catch (InvalidDataException e)
            {
                warnings.Add(entry.FullName + " could not be decompressed and was skipped: " + e.Message);
                return null;
            }
        }

        private static BcfTopic? ReadTopic(XElement markup, string path, List<string> warnings,
                                           Files files)
        {
            var element = markup.Element("Topic");
            if (element == null)
            {
                warnings.Add(path + " has no Topic element and was skipped");
                return null;
            }

            var guid = element.Attribute("Guid")?.Value;
            var title = Text(element, "Title");

            if (string.IsNullOrWhiteSpace(title))
            {
                // The schema requires a title in both versions, and a topic without one cannot be
                // shown on a board at all. Named rather than dropped silently.
                warnings.Add(path + " has a topic with no title; it was given one from its folder");
                title = "Untitled topic";
            }

            var author = Text(element, "CreationAuthor");
            if (string.IsNullOrWhiteSpace(author))
            {
                warnings.Add(path + " has a topic with no creation author");
                author = "unknown";
            }

            var created = Date(Text(element, "CreationDate"));
            if (created == null)
            {
                warnings.Add(path + " has a topic with no readable creation date");
                created = DateTimeOffset.MinValue;
            }

            var topic = new BcfTopic(guid ?? path, title!, created.Value, author!)
            {
                TopicType = element.Attribute("TopicType")?.Value ?? "Issue",
                TopicStatus = element.Attribute("TopicStatus")?.Value ?? "Open",
                Priority = Text(element, "Priority"),
                Stage = Text(element, "Stage"),
                Description = Text(element, "Description"),
                AssignedTo = Text(element, "AssignedTo"),
                ModifiedAuthor = Text(element, "ModifiedAuthor"),
                ModifiedDate = Date(Text(element, "ModifiedDate")),
                DueDate = Date(Text(element, "DueDate")),
            };

            // 2.1 repeats <Labels>; 3.0 wraps <Label> in <Labels>. Both accepted, because plenty
            // of files in the wild carry one version's shape under the other's declaration.
            foreach (var label in Either(element, "Labels", "Labels", "Label"))
                topic.Labels.Add(label);

            foreach (var link in Either(element, "ReferenceLink", "ReferenceLinks", "ReferenceLink"))
                topic.ReferenceLinks.Add(link);

            foreach (var related in RelatedGuids(element))
                topic.RelatedTopics.Add(related);

            // 2.1 puts comments under Markup; 3.0 puts them under Topic. Look in both.
            foreach (var comment in Comments(markup).Concat(Comments(element)))
                topic.Comments.Add(comment);

            var folder = path.LastIndexOf('/') < 0 ? string.Empty : path.Substring(0, path.LastIndexOf('/') + 1);

            foreach (var viewpoint in Viewpoints(markup).Concat(Viewpoints(element)))
            {
                // The markup names the viewpoint; the camera, the selection and the colouring are
                // in the .bcfv beside it. Reading only the name was reading only half a viewpoint:
                // an imported issue arrived with nothing to look at and no elements selected,
                // which is most of what a viewpoint is for.
                Visualisation(files, folder, viewpoint, warnings);

                topic.Viewpoints.Add(viewpoint);
            }

            return topic;
        }

        private static IEnumerable<BcfComment> Comments(XElement parent)
        {
            var direct = parent.Elements("Comment");
            var wrapped = parent.Element("Comments")?.Elements("Comment") ?? Enumerable.Empty<XElement>();

            foreach (var element in direct.Concat(wrapped))
            {
                var guid = element.Attribute("Guid")?.Value;
                var date = Date(Text(element, "Date")) ?? DateTimeOffset.MinValue;
                var author = Text(element, "Author") ?? "unknown";

                yield return new BcfComment(guid ?? author + date.ToString("O", CultureInfo.InvariantCulture),
                                            date, author, Text(element, "Comment"))
                {
                    ViewpointGuid = element.Element("Viewpoint")?.Attribute("Guid")?.Value,
                    ModifiedDate = Date(Text(element, "ModifiedDate")),
                    ModifiedAuthor = Text(element, "ModifiedAuthor"),
                };
            }
        }

        /// <summary>Fill a viewpoint in from its .bcfv, when the archive carries one.</summary>
        private static void Visualisation(Files files, string folder, BcfViewpoint viewpoint,
                                          List<string> warnings)
        {
            var entry = files.Visualisation(folder, viewpoint.Guid);

            if (entry == null) return;

            var root = Parse(entry, warnings);

            if (root == null) return;

            viewpoint.Camera = Camera(root);

            var components = root.Element("Components");

            if (components == null) return;

            // 2.1 lists the selection directly under Components; both versions wrap it in
            // Selection, and files in the wild do both.
            foreach (var element in Elements(components, "Selection", "Component"))
                viewpoint.Selection.Add(Component(element));

            var visibility = components.Element("Visibility");

            if (visibility != null)
            {
                viewpoint.DefaultVisibility =
                    !string.Equals(visibility.Attribute("DefaultVisibility")?.Value, "false",
                                   StringComparison.OrdinalIgnoreCase);

                foreach (var element in Elements(visibility, "Exceptions", "Component"))
                    viewpoint.VisibilityExceptions.Add(Component(element));
            }

            var colouring = components.Element("Coloring") ?? components.Element("Colouring");

            if (colouring == null) return;

            foreach (var colour in colouring.Elements("Color"))
            {
                var named = colour.Attribute("Color")?.Value;

                if (string.IsNullOrWhiteSpace(named)) continue;

                // 3.0 wraps a colour's components; 2.1 lists them directly.
                var members = colour.Elements("Component")
                    .Concat(colour.Element("Components")?.Elements("Component")
                            ?? Enumerable.Empty<XElement>())
                    .Select(Component)
                    .ToList();

                if (members.Count > 0) viewpoint.Colouring.Add(new BcfColouring(named!, members));
            }
        }

        /// <summary>The named children of a wrapper, or of the parent when there is no wrapper.</summary>
        private static IEnumerable<XElement> Elements(XElement parent, string wrapper, string name)
        {
            var inside = parent.Element(wrapper);

            return inside == null ? Enumerable.Empty<XElement>() : inside.Elements(name);
        }

        private static BcfComponent Component(XElement element) =>
            new BcfComponent(element.Attribute("IfcGuid")?.Value,
                             Text(element, "AuthoringToolId"),
                             Text(element, "OriginatingSystem"));

        /// <summary>The camera, whichever of the two kinds the file carries.</summary>
        private static BcfCamera? Camera(XElement root)
        {
            var perspective = root.Element("PerspectiveCamera");

            if (perspective != null)
            {
                var (x, y, z) = Vector(perspective, "CameraViewPoint");
                var (dx, dy, dz) = Vector(perspective, "CameraDirection");
                var (ux, uy, uz) = Vector(perspective, "CameraUpVector");

                return BcfCamera.Perspective(x, y, z, dx, dy, dz, ux, uy, uz,
                                             Number(perspective, "FieldOfView", 60),
                                             Number(perspective, "AspectRatio",
                                                    BcfCamera.DefaultAspectRatio));
            }

            var orthographic = root.Element("OrthogonalCamera");

            if (orthographic == null) return null;

            var (ox, oy, oz) = Vector(orthographic, "CameraViewPoint");
            var (odx, ody, odz) = Vector(orthographic, "CameraDirection");
            var (oux, ouy, ouz) = Vector(orthographic, "CameraUpVector");

            return BcfCamera.Orthographic(ox, oy, oz, odx, ody, odz, oux, ouy, ouz,
                                          Number(orthographic, "ViewToWorldScale", 1),
                                          Number(orthographic, "AspectRatio",
                                                 BcfCamera.DefaultAspectRatio));
        }

        private static (double X, double Y, double Z) Vector(XElement parent, string name)
        {
            var element = parent.Element(name);

            return element == null
                ? (0, 0, 0)
                : (Number(element, "X", 0), Number(element, "Y", 0), Number(element, "Z", 0));
        }

        private static double Number(XElement parent, string name, double fallback) =>
            double.TryParse(Text(parent, name), System.Globalization.NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        private static IEnumerable<BcfViewpoint> Viewpoints(XElement parent)
        {
            // 2.1 names each entry "Viewpoints"; 3.0 wraps "ViewPoint" entries in "Viewpoints".
            // The names collide, so the wrapper is recognised by having element children rather
            // than by its name.
            var candidates = parent.Elements("Viewpoints")
                .Concat(parent.Elements("Viewpoints").Elements("ViewPoint"))
                .Concat(parent.Elements("ViewPoint"));

            foreach (var element in candidates)
            {
                var guid = element.Attribute("Guid")?.Value;
                if (guid == null) continue;

                yield return new BcfViewpoint(guid);
            }
        }

        private static IEnumerable<string> RelatedGuids(XElement topic)
        {
            var direct = topic.Elements("RelatedTopic");
            var wrapped = topic.Element("RelatedTopics")?.Elements("RelatedTopic") ?? Enumerable.Empty<XElement>();

            return direct.Concat(wrapped)
                .Select(e => e.Attribute("Guid")?.Value)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!);
        }

        private static IEnumerable<string> Either(XElement parent, string flatName, string wrapperName, string innerName)
        {
            foreach (var element in parent.Elements(flatName))
            {
                // A 2.1 flat element holds text; a 3.0 wrapper holds children under the same name.
                if (element.HasElements) continue;
                if (!string.IsNullOrWhiteSpace(element.Value)) yield return element.Value.Trim();
            }

            var wrapper = parent.Element(wrapperName);
            if (wrapper == null) yield break;

            foreach (var element in wrapper.Elements(innerName))
                if (!string.IsNullOrWhiteSpace(element.Value)) yield return element.Value.Trim();
        }

        private static string? Text(XElement parent, string name)
        {
            var value = parent.Element(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static DateTimeOffset? Date(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
                ? value
                : (DateTimeOffset?)null;
        }
    }
}
