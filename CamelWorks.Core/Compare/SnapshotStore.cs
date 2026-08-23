using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Compare
{
    /// <summary>One baseline, as the picker lists it — without reading the whole file.</summary>
    public sealed class BaselineEntry
    {
        internal BaselineEntry(string file, string label, DateTimeOffset? takenUtc,
                               int elements, int models, PropertyWatch watch, string documentPath)
        {
            File = file;
            Label = label;
            TakenUtc = takenUtc;
            Elements = elements;
            Models = models;
            Watch = watch;
            DocumentPath = documentPath;
        }

        /// <summary>The file name inside the baselines folder.</summary>
        public string File { get; }

        /// <summary>How it reads on the picker.</summary>
        public string Label { get; }

        /// <summary>When it was taken.</summary>
        public DateTimeOffset? TakenUtc { get; }

        /// <summary>How many elements it holds.</summary>
        public int Elements { get; }

        /// <summary>How many models it covers.</summary>
        public int Models { get; }

        /// <summary>Which properties it recorded.</summary>
        public PropertyWatch Watch { get; }

        /// <summary>The document it was taken from.</summary>
        public string DocumentPath { get; }

        /// <summary>How it reads on one line.</summary>
        public string Describe()
        {
            var when = TakenUtc.HasValue
                ? TakenUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                : "date unknown";

            var name = Label.Length > 0 ? Label : when;

            return name + " — " + ComparePolicy.Count(Elements) + " elements, "
                   + ComparePolicy.Count(Models) + (Models == 1 ? " model" : " models")
                   + (Label.Length > 0 ? ", " + when : string.Empty);
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        internal JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("file", File)
                .Set("label", Label)
                .Set("elements", Elements)
                .Set("models", Models)
                .Set("watch", Watch.ToString())
                .Set("document", DocumentPath);

            if (TakenUtc.HasValue) json.Set("taken", TakenUtc.Value.ToUnixTimeSeconds());

            return json;
        }

        internal static BaselineEntry? FromJson(JsonValue? json)
        {
            var file = json?["file"].AsString();
            if (string.IsNullOrWhiteSpace(file)) return null;

            return new BaselineEntry(
                file!,
                json!["label"].AsString() ?? string.Empty,
                json["taken"].Kind == JsonKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(json["taken"].AsLong())
                    : (DateTimeOffset?)null,
                (int)json["elements"].AsLong(),
                (int)json["models"].AsLong(),
                Enum.TryParse<PropertyWatch>(json["watch"].AsString(), ignoreCase: true, out var watch)
                    ? watch
                    : PropertyWatch.Standard,
                json["document"].AsString() ?? string.Empty);
        }
    }

    /// <summary>
    /// Where baselines are kept, and how they are found again.
    ///
    /// Beside the document, in the same hidden folder as the project sidecar, so that a job handed
    /// over on a memory stick arrives with its history rather than leaving it on somebody's C
    /// drive. A baseline is useless to the person who did not take it, and that is exactly the
    /// person who ends up asking what changed.
    ///
    /// <b>A small index, so the picker does not read the files.</b> A snapshot of a real federation
    /// is tens of megabytes; opening six of them to fill in a dropdown is several seconds of
    /// nothing happening every time somebody opens the tab. The index holds the six lines the
    /// picker needs, and <see cref="Rebuild"/> puts it back from the files themselves when it is
    /// missing, deleted or out of step — so it is a cache and never the truth.
    /// </summary>
    public sealed class SnapshotStore
    {
        /// <summary>The folder baselines live in, inside the project's sidecar folder.</summary>
        public const string FolderName = "compare";

        /// <summary>The index file's name.</summary>
        public const string IndexName = "baselines.json";

        private readonly IFileSystem _fs;

        /// <summary>Open the store for a folder.</summary>
        /// <param name="fs">The file system.</param>
        /// <param name="folder">Where baselines live, or null when there is nowhere to put them.</param>
        public SnapshotStore(IFileSystem fs, string? folder)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            Folder = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim();
        }

        /// <summary>Where baselines live, or null when there is nowhere to put them.</summary>
        public string? Folder { get; }

        /// <summary>True when nothing can be saved — an unsaved document with no user folder.</summary>
        public bool IsMemoryOnly => Folder == null;

        /// <summary>Why the last save failed, or null.</summary>
        public string? LastProblem { get; private set; }

        /// <summary>
        /// The baselines folder for a document.
        /// </summary>
        /// <param name="documentPath">The saved document's full path, or null.</param>
        /// <param name="userDirectory">A per-user folder to fall back to, or null.</param>
        public static string? FolderFor(string? documentPath, string? userDirectory)
        {
            var (path, _) = ProjectStore.Locate(documentPath, userDirectory);
            if (path == null) return null;

            var directory = PathText.Directory(path);
            return directory == null ? null : PathText.Join(directory, FolderName);
        }

        /// <summary>Every baseline, newest first.</summary>
        public IReadOnlyList<BaselineEntry> Baselines()
        {
            if (Folder == null) return Array.Empty<BaselineEntry>();

            var text = _fs.ReadAllText(PathText.Join(Folder, IndexName));
            var entries = new List<BaselineEntry>();

            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    foreach (var item in JsonReader.Parse(text!)["baselines"].Items)
                    {
                        var entry = BaselineEntry.FromJson(item);
                        if (entry != null && _fs.Exists(PathText.Join(Folder, entry.File))) entries.Add(entry);
                    }
                }
                catch
                {
                    // An unreadable index is a cache problem, never a data problem. Falling through
                    // to the rebuild is what makes it one.
                    entries.Clear();
                }
            }

            if (entries.Count == 0) entries.AddRange(Rebuild());

            return entries
                .OrderByDescending(e => e.TakenUtc ?? DateTimeOffset.MinValue)
                .ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The most recent baseline, or null when there is none.</summary>
        public BaselineEntry? Latest() => Baselines().FirstOrDefault();

        /// <summary>
        /// Read the index back from the files themselves.
        ///
        /// Expensive — it opens every snapshot — and correct, which is the right trade for the
        /// once-in-a-while case of somebody copying a baseline into the folder by hand. That being
        /// possible at all is the point of a plain-text format.
        /// </summary>
        public IReadOnlyList<BaselineEntry> Rebuild()
        {
            if (Folder == null) return Array.Empty<BaselineEntry>();

            var entries = new List<BaselineEntry>();

            foreach (var name in _fs.ListFiles(Folder))
            {
                if (!name.EndsWith(ModelSnapshot.Extension, StringComparison.OrdinalIgnoreCase)) continue;

                var snapshot = ModelSnapshot.Read(_fs.ReadAllText(PathText.Join(Folder, name)));
                if (snapshot == null) continue;

                entries.Add(new BaselineEntry(name, snapshot.Label, snapshot.TakenUtc,
                                              snapshot.Count, snapshot.Models.Count, snapshot.Watch,
                                              snapshot.DocumentPath));
            }

            if (entries.Count > 0) Index(entries);

            return entries;
        }

        /// <summary>Read one back, or null when it cannot be read.</summary>
        /// <param name="entry">Which baseline.</param>
        public ModelSnapshot? Load(BaselineEntry? entry)
        {
            if (entry == null || Folder == null) return null;

            return ModelSnapshot.Read(_fs.ReadAllText(PathText.Join(Folder, entry.File)));
        }

        /// <summary>
        /// Save a snapshot as a baseline. Returns the entry, or null with
        /// <see cref="LastProblem"/> set.
        /// </summary>
        /// <param name="snapshot">What to save.</param>
        public BaselineEntry? Save(ModelSnapshot? snapshot)
        {
            LastProblem = null;

            if (snapshot == null) { LastProblem = "there is nothing to save"; return null; }

            if (Folder == null)
            {
                LastProblem = "this document has never been saved, so there is nowhere to keep a "
                              + "baseline beside it. Save the document first.";
                return null;
            }

            var file = Name(snapshot);

            try
            {
                _fs.CreateDirectory(Folder);
                AtomicFile.Write(_fs, PathText.Join(Folder, file), snapshot.Write());
            }
            catch (Exception e)
            {
                LastProblem = "the baseline could not be written: " + e.Message;
                return null;
            }

            var entry = new BaselineEntry(file, snapshot.Label, snapshot.TakenUtc, snapshot.Count,
                                          snapshot.Models.Count, snapshot.Watch, snapshot.DocumentPath);

            var all = Baselines().Where(e => !string.Equals(e.File, file, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add(entry);
            Index(all);

            return entry;
        }

        /// <summary>Delete a baseline and take it out of the index.</summary>
        /// <param name="entry">Which baseline.</param>
        public bool Delete(BaselineEntry? entry)
        {
            LastProblem = null;

            if (entry == null || Folder == null) return false;

            try
            {
                _fs.Delete(PathText.Join(Folder, entry.File));
            }
            catch (Exception e)
            {
                LastProblem = "the baseline could not be deleted: " + e.Message;
                return false;
            }

            Index(Baselines().Where(e => !string.Equals(e.File, entry.File, StringComparison.OrdinalIgnoreCase)).ToList());
            return true;
        }

        private void Index(IReadOnlyList<BaselineEntry> entries)
        {
            if (Folder == null) return;

            var json = JsonValue.Object().Set("baselines", JsonValue.Array(entries.Select(e => e.ToJson())));

            try
            {
                _fs.CreateDirectory(Folder);
                AtomicFile.Write(_fs, PathText.Join(Folder, IndexName), json.ToJson(indented: true));
            }
            catch
            {
                // The index is a cache. Failing to write it must never fail the save that was the
                // actual point, because the next Rebuild puts it back.
            }
        }

        /// <summary>
        /// The file name for a snapshot: its date, then whatever is usable of its label.
        ///
        /// Date first so the folder sorts chronologically in any file browser, which is how
        /// somebody finds last month's baseline when the index is gone and CamelWorks is not open.
        /// </summary>
        /// <param name="snapshot">The snapshot.</param>
        public static string Name(ModelSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var when = (snapshot.TakenUtc ?? DateTimeOffset.MinValue).UtcDateTime
                .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            var label = Safe(snapshot.Label);

            return label.Length == 0
                ? when + ModelSnapshot.Extension
                : when + "-" + label + ModelSnapshot.Extension;
        }

        private static string Safe(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;

            var sb = new StringBuilder();

            foreach (var c in label!.Trim())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('-');

                if (sb.Length >= 40) break;
            }

            return sb.ToString().Trim('-');
        }
    }
}
