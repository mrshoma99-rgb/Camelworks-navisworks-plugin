using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Field
{
    /// <summary>What kind of thing is hanging off an element.</summary>
    public enum AttachmentKind
    {
        /// <summary>Something to read: a drawing, a data sheet, a method statement.</summary>
        Document = 0,

        /// <summary>Something to look at, taken on site.</summary>
        Photo,

        /// <summary>Somewhere else: a document management system, a model viewer, a ticket.</summary>
        Link,
    }

    /// <summary>One thing hanging off one element.</summary>
    public sealed class Attachment
    {
        internal Attachment(ElementKey key, AttachmentKind kind, string where, string name,
                            bool exists)
        {
            Key = key;
            Kind = kind;
            Where = where;
            Name = name;
            Exists = exists;
        }

        /// <summary>Which element it hangs off.</summary>
        public ElementKey Key { get; }

        /// <summary>What kind of thing it is.</summary>
        public AttachmentKind Kind { get; }

        /// <summary>Where it is: a full path, or an address.</summary>
        public string Where { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; }

        /// <summary>Whether it is there. Always true for an address, which nothing here can check.</summary>
        public bool Exists { get; }

        /// <summary>Its extension, lower case, with the dot, or empty.</summary>
        public string Extension => Attachments.ExtensionOf(Name);

        /// <inheritdoc />
        public override string ToString() => Name + (Exists ? string.Empty : " (not there)");
    }

    /// <summary>Which property says where an element's things are.</summary>
    public sealed class AttachmentRule
    {
        /// <summary>Make a rule.</summary>
        /// <param name="kind">What kind of thing the property points at.</param>
        /// <param name="property">Which property holds the folder or the address.</param>
        public AttachmentRule(AttachmentKind kind, PropertyRef property)
        {
            Kind = kind;
            Property = property;
        }

        /// <summary>What kind of thing the property points at.</summary>
        public AttachmentKind Kind { get; }

        /// <summary>Which property holds it.</summary>
        public PropertyRef Property { get; }

        /// <summary>
        /// A folder every relative path is taken from, so a model that says "Sheets\A-101.pdf" can
        /// be moved between machines without every link breaking.
        /// </summary>
        public string? Root { get; set; }

        /// <summary>
        /// Which file types count, with their dots. Empty takes everything in the folder.
        ///
        /// The reason a photo rule does not offer somebody the folder's thumbnail database.
        /// </summary>
        public IList<string> Extensions { get; } = new List<string>();

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("kind", Kind.ToString())
                .Set("property", Property.Name)
                .Set("extensions", JsonValue.Array(Extensions.Select(JsonValue.String)));

            if (Property.Category.Length > 0) json.Set("category", Property.Category);
            if (Root != null) json.Set("root", Root);

            return json;
        }

        /// <summary>Read one back, or null when it names no property.</summary>
        /// <param name="json">What was written.</param>
        public static AttachmentRule? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var name = (json["property"].AsString() ?? string.Empty).Trim();

            if (name.Length == 0) return null;

            if (!Enum.TryParse<AttachmentKind>(json["kind"].AsString() ?? "Document", out var kind))
                kind = AttachmentKind.Document;

            var rule = new AttachmentRule(
                kind, new PropertyRef((json["category"].AsString() ?? string.Empty).Trim(), name))
            {
                Root = json["root"].AsString(),
            };

            foreach (var item in json["extensions"].Items)
            {
                var extension = (item.AsString() ?? string.Empty).Trim().ToLowerInvariant();

                if (extension.Length > 0) rule.Extensions.Add(extension);
            }

            return rule;
        }

        /// <inheritdoc />
        public override string ToString() => Kind + " from " + Property;
    }

    /// <summary>What was found hanging off a set of elements.</summary>
    public sealed class AttachmentReport
    {
        internal AttachmentReport(IReadOnlyList<Attachment> found, int elements, int carrying,
                                  int missing, IReadOnlyList<string> emptyFolders,
                                  IReadOnlyList<string> notes, string? refusal)
        {
            Found = found;
            Elements = elements;
            Carrying = carrying;
            Missing = missing;
            EmptyFolders = emptyFolders;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>Everything found.</summary>
        public IReadOnlyList<Attachment> Found { get; }

        /// <summary>How many elements were looked at.</summary>
        public int Elements { get; }

        /// <summary>How many said where something was.</summary>
        public int Carrying { get; }

        /// <summary>How many named a folder or a file that is not there.</summary>
        public int Missing { get; }

        /// <summary>Folders that are there and hold nothing a rule wanted.</summary>
        public IReadOnlyList<string> EmptyFolders { get; }

        /// <summary>Anything worth saying about it.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is nothing, when there is nothing.</summary>
        public string? Refusal { get; }

        /// <summary>True when something was found.</summary>
        public bool IsUsable => Refusal == null && Found.Count > 0;

        /// <summary>What one element has hanging off it.</summary>
        /// <param name="key">The element.</param>
        public IReadOnlyList<Attachment> For(ElementKey key) =>
            Found.Where(a => a.Key == key).ToList();

        /// <summary>What it found, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            // Whether anything was found or not, the counts below still have to be said. Returning
            // early on "nothing found" is what threw away the one distinction this whole thing
            // exists to draw - a folder that is not there and a folder that is empty need two
            // different replies, and "nothing is attached" is neither of them.
            var text = Found.Count == 0
                ? "Nothing is attached to any of those "
                  + Elements.ToString("N0", CultureInfo.InvariantCulture) + " elements."
                : Found.Count.ToString("N0", CultureInfo.InvariantCulture)
                  + (Found.Count == 1 ? " thing attached to " : " things attached to ")
                  + Carrying.ToString("N0", CultureInfo.InvariantCulture) + " of "
                  + Elements.ToString("N0", CultureInfo.InvariantCulture) + " elements.";

            if (Found.Count == 0 && Carrying == 0)
                text += " None of them carries the property the rule reads, so nothing was even "
                     + "looked for.";

            if (Missing > 0)
                text += " " + Missing.ToString("N0", CultureInfo.InvariantCulture)
                     + (Missing == 1 ? " element points" : " elements point")
                     + " somewhere that is not there.";

            if (EmptyFolders.Count > 0)
                text += " " + EmptyFolders.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (EmptyFolders.Count == 1 ? " folder is" : " folders are")
                     + " there and empty.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The paperwork that belongs to an element, found from what the model already says.
    ///
    /// Every project keeps the documents somewhere and the model somewhere else, and the join
    /// between them is a property nobody thinks of as a link: a folder name, a drawing number, an
    /// address into a document system. This reads that property, resolves it, and says what is
    /// actually there - which turns "the O&amp;M is in the folder" into a list a person can open.
    ///
    /// One mechanism, three uses. A folder of data sheets, a folder of site photos and an address
    /// into a document management system are the same idea pointed at different things, and
    /// building three of them would have produced three sets of the same bug.
    ///
    /// <b>What is not here.</b> Nothing writes back into the authoring tool. iConstruct ships a
    /// Revit add-in for that, and a Navisworks plug-in cannot do it from this side - so
    /// <see cref="HandBack"/> writes the file that a Revit-side import consumes instead, keyed on
    /// the authoring tool's own element id, which the model already carries. That is the honest
    /// half: this end produces something the other end can read, rather than pretending to reach
    /// across.
    /// </summary>
    public static class Attachments
    {
        /// <summary>Where saved rules live in the project store.</summary>
        public const string StoreKey = "attachments";

        /// <summary>How many things one run will find.</summary>
        public const int Ceiling = 20000;

        /// <summary>The properties a folder of documents usually hides under.</summary>
        public static IReadOnlyList<string> DocumentNames { get; } = new[]
        {
            "Documents", "Document Folder", "InfoBin", "InfoBin Dir", "O&M", "Data Sheet",
        };

        /// <summary>The properties a folder of site photos usually hides under.</summary>
        public static IReadOnlyList<string> PhotoNames { get; } = new[]
        {
            "Photos", "Photo Folder", "PhotoBin", "PhotoBin Dir", "Site Photos",
        };

        /// <summary>What counts as a photo when a rule does not say.</summary>
        public static IReadOnlyList<string> PhotoTypes { get; } = new[]
        {
            ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".webp", ".bmp",
        };

        /// <summary>What counts as a document when a rule does not say.</summary>
        public static IReadOnlyList<string> DocumentTypes { get; } = new[]
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".dwg", ".dxf", ".txt", ".msg", ".rtf",
        };

        /// <summary>
        /// Find what is attached.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="rules">Which properties to read, and what they point at.</param>
        /// <param name="files">
        /// The file system to look in. Behind the seam, so this is testable without a disk - which
        /// matters, because "the folder is there and empty" and "the folder is not there" are two
        /// different answers a person needs and both of them are awkward to arrange for real.
        /// </param>
        public static AttachmentReport Find(IEnumerable<ReportElement>? elements,
                                            IEnumerable<AttachmentRule>? rules,
                                            IFileSystem? files)
        {
            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();
            var wanted = (rules ?? Enumerable.Empty<AttachmentRule>()).Where(r => r != null).ToList();
            var notes = new List<string>();

            if (scope.Count == 0)
                return Nothing(notes, "There are no elements in scope. Select what you want the "
                    + "paperwork for, or widen the scope.");

            if (wanted.Count == 0)
                return Nothing(notes, "No rule says which property holds the link. Point one at the "
                    + "property the model uses - a folder name, a drawing number, an address.");

            var found = new List<Attachment>();
            var empty = new List<string>();
            var carrying = new HashSet<ElementKey>();
            var missing = new HashSet<ElementKey>();
            var capped = false;

            foreach (var element in scope)
            {
                foreach (var rule in wanted)
                {
                    var said = element.Value(rule.Property);

                    if (string.IsNullOrWhiteSpace(said)) continue;

                    carrying.Add(element.Key);

                    var where = Full(said!.Trim(), rule.Root);

                    if (rule.Kind == AttachmentKind.Link)
                    {
                        // An address is taken at its word. Nothing here can reach a document
                        // system to ask whether it is really there, and a red cross beside a
                        // perfectly good link because the plug-in has no network is worse than
                        // no answer.
                        found.Add(new Attachment(element.Key, rule.Kind, where, Leaf(where), true));
                        continue;
                    }

                    if (files == null)
                    {
                        missing.Add(element.Key);
                        continue;
                    }

                    // A property that names one file rather than a folder is ordinary, and losing
                    // the link because somebody was more specific than the rule expected would be
                    // silly. Asked first, because a file is not a directory and the seam says so.
                    if (files.Exists(where))
                    {
                        found.Add(new Attachment(element.Key, rule.Kind, where, Leaf(where), true));
                        continue;
                    }

                    if (!files.DirectoryExists(where))
                    {
                        missing.Add(element.Key);
                        continue;
                    }

                    IReadOnlyList<string> inside;

                    try
                    {
                        inside = files.ListFiles(where);
                    }
                    catch (Exception)
                    {
                        missing.Add(element.Key);
                        continue;
                    }

                    var kept = 0;

                    // ListFiles gives names, not paths - which is what the seam promises and what
                    // both sides of it do. Joining here rather than expecting paths back is the
                    // difference between a link that opens and one that says "C:\Sheets" nine times.
                    foreach (var name in inside.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!Wanted(name, rule)) continue;

                        if (found.Count >= Ceiling) { capped = true; break; }

                        found.Add(new Attachment(element.Key, rule.Kind, Join(where, name), name, true));
                        kept++;
                    }

                    if (kept == 0 && !empty.Contains(where, StringComparer.OrdinalIgnoreCase))
                        empty.Add(where);
                }
            }

            if (capped)
                notes.Add("It stopped at " + Ceiling.ToString("N0", CultureInfo.InvariantCulture)
                          + ". Narrow the scope, or say which file types the rule wants.");

            return new AttachmentReport(found, scope.Count, carrying.Count, missing.Count, empty,
                                        notes, null);
        }

        /// <summary>
        /// Which of the properties in scope look like they hold a link, so nothing has to be guessed.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="kind">What kind of link is being looked for.</param>
        public static IReadOnlyList<PropertyRef> Likely(IEnumerable<ReportElement>? elements,
                                                        AttachmentKind kind)
        {
            var wanted = kind == AttachmentKind.Photo ? PhotoNames : DocumentNames;

            var fields = DataReport.Fields(elements)
                .Where(f => f.Filled > 0)
                .ToList();

            var named = fields
                .Where(f => wanted.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                .Select(f => f.Field)
                .ToList();

            if (named.Count > 0) return named;

            // Nothing is called what it usually is, so fall back to what the values look like:
            // something with a separator in it, which a folder has and a mark does not.
            return fields
                .Where(f => f.Example != null
                            && (f.Example.IndexOf('\\') >= 0 || f.Example.IndexOf('/') >= 0))
                .Select(f => f.Field)
                .ToList();
        }

        /// <summary>
        /// The file a Revit-side import reads, keyed on the authoring tool's own element id.
        ///
        /// The honest half of the thing this product cannot do. Nothing on the Navisworks side can
        /// write into a Revit model - that needs an add-in living in Revit - so what happens here
        /// is the half that can: produce a sheet the other end consumes, keyed on the id Revit
        /// itself uses, which the model is already carrying because the exporter put it there.
        /// </summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="id">The property holding the authoring tool's own id.</param>
        /// <param name="carry">Which properties to send back.</param>
        public static SheetTable HandBack(IEnumerable<ReportElement>? elements, PropertyRef id,
                                          IEnumerable<PropertyRef>? carry)
        {
            var scope = (elements ?? Enumerable.Empty<ReportElement>()).Where(e => e != null).ToList();
            var fields = (carry ?? Enumerable.Empty<PropertyRef>()).ToList();

            var headers = new List<string?> { id.Name.Length == 0 ? "Id" : id.Name };

            headers.AddRange(fields.Select(f => (string?)f.Name));

            var rows = new List<IReadOnlyList<string?>> { headers };

            foreach (var element in scope)
            {
                var found = element.Value(id);

                // Without the authoring tool's id there is nothing for the other end to match on,
                // so the row would arrive and land nowhere. Left out rather than sent as a blank.
                if (string.IsNullOrWhiteSpace(found)) continue;

                var cells = new List<string?> { found!.Trim() };

                cells.AddRange(fields.Select(f => element.Value(f)));

                rows.Add(cells);
            }

            return new SheetTable("Back to the model", rows);
        }

        /// <summary>Every saved rule, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<AttachmentRule> Saved(JsonValue? json)
        {
            var saved = new List<AttachmentRule>();

            if (json == null) return saved;

            foreach (var item in json.Items)
            {
                var rule = AttachmentRule.FromJson(item);

                if (rule != null) saved.Add(rule);
            }

            return saved;
        }

        /// <summary>The list written down.</summary>
        /// <param name="rules">The rules.</param>
        public static JsonValue Keep(IEnumerable<AttachmentRule>? rules) =>
            JsonValue.Array((rules ?? Enumerable.Empty<AttachmentRule>())
                .Where(r => r != null && r.Property.Name.Length > 0)
                .Select(r => r.ToJson()));

        /// <summary>The extension, lower case, with its dot, or empty.</summary>
        /// <param name="name">A file name or a path.</param>
        public static string ExtensionOf(string? name)
        {
            var text = Leaf(name);
            var dot = text.LastIndexOf('.');

            return dot <= 0 ? string.Empty : text.Substring(dot).ToLowerInvariant();
        }

        /// <summary>The last part of a path, whichever slash it uses.</summary>
        /// <param name="path">The path.</param>
        public static string Leaf(string? path)
        {
            var text = (path ?? string.Empty).Trim().TrimEnd('\\', '/');

            var cut = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));

            return cut < 0 ? text : text.Substring(cut + 1);
        }

        private static string Join(string directory, string name) =>
            directory.TrimEnd('\\', '/') + "\\" + name;

        private static bool Wanted(string file, AttachmentRule rule)
        {
            var extension = ExtensionOf(file);

            if (rule.Extensions.Count > 0)
                return rule.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

            return rule.Kind switch
            {
                AttachmentKind.Photo => PhotoTypes.Contains(extension, StringComparer.OrdinalIgnoreCase),
                AttachmentKind.Document => DocumentTypes.Contains(extension, StringComparer.OrdinalIgnoreCase),
                _ => true,
            };
        }

        private static string Full(string said, string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return said;

            // Anything the model states in full is left alone: a UNC share, a drive letter, or an
            // address. Only a relative path is taken from the root, which is the whole point of
            // having one.
            if (said.StartsWith("\\\\", StringComparison.Ordinal)) return said;
            if (said.IndexOf("://", StringComparison.Ordinal) > 0) return said;
            if (said.Length > 1 && said[1] == ':') return said;
            if (said.Length > 0 && (said[0] == '\\' || said[0] == '/')) return said;

            return root!.TrimEnd('\\', '/') + "\\" + said.TrimStart('\\', '/');
        }

        private static AttachmentReport Nothing(IReadOnlyList<string> notes, string refusal) =>
            new AttachmentReport(Array.Empty<Attachment>(), 0, 0, 0, Array.Empty<string>(),
                                 notes, refusal);
    }
}
