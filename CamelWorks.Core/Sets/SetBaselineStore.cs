using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Sets
{
    /// <summary>
    /// What every watched set held the last time anybody looked.
    ///
    /// <b>Its own file, beside the project.</b> Membership is element keys, and a federation's
    /// sets between them can name most of the model - hundreds of kilobytes that would otherwise
    /// sit in a project file rewritten every time any tool remembers a textbox. The findings went
    /// to their own file for exactly this reason and this follows them.
    ///
    /// <b>One reading per set, not a history.</b> Drift is measured against the last time, because
    /// that is the question somebody asks: has this changed since I trusted it. A history would be
    /// a second feature - and the model comparison already exists for anybody who wants one.
    /// </summary>
    public sealed class SetBaselineStore
    {
        /// <summary>What the file is called, beside the project file.</summary>
        public const string Extension = ".sets.json";

        /// <summary>The shape's version, so a later change can be told apart.</summary>
        public const int Version = 1;

        private readonly IFileSystem _fs;
        private readonly Dictionary<string, SetReading> _readings;

        private SetBaselineStore(IFileSystem fs, string? path, string where,
                                 Dictionary<string, SetReading> readings,
                                 IReadOnlyList<string> notes)
        {
            _fs = fs;
            Path = path;
            Where = where;
            _readings = readings;
            Notes = notes;
        }

        /// <summary>Where the file is, or null when there is nowhere to keep one.</summary>
        public string? Path { get; }

        /// <summary>Where these live, in words, for a pane to say.</summary>
        public string Where { get; }

        /// <summary>Anything the opening had to report.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why the last save did not happen, or null.</summary>
        public string? LastSaveProblem { get; private set; }

        /// <summary>Whether there is nowhere to write.</summary>
        public bool IsMemoryOnly => Path == null;

        /// <summary>How many sets have been read before.</summary>
        public int Count => _readings.Count;

        /// <summary>The last reading of every set, in no particular order.</summary>
        public IReadOnlyCollection<SetReading> All => _readings.Values;

        /// <summary>The last reading of one set, or null when it has never been read.</summary>
        /// <param name="name">The set's name.</param>
        public SetReading? Find(string? name) =>
            name != null && _readings.TryGetValue(name.Trim(), out var reading) ? reading : null;

        /// <summary>Take this run's readings as the new baseline.</summary>
        /// <param name="readings">What every set holds now.</param>
        public void Keep(IEnumerable<SetReading>? readings)
        {
            foreach (var reading in readings ?? Array.Empty<SetReading>())
            {
                if (reading == null) continue;

                // A set the document no longer has is dropped rather than kept as an empty
                // reading: keeping it would report it gone again on every run for ever, and once
                // is the honest number of times to say a thing.
                if (reading.Absent) _readings.Remove(reading.Name);
                else _readings[reading.Name] = reading;
            }
        }

        /// <summary>Forget one set's baseline, so the next run treats it as never read.</summary>
        /// <param name="name">The set's name.</param>
        public bool Forget(string? name) =>
            name != null && _readings.Remove(name.Trim());

        /// <summary>Forget everything.</summary>
        public void Clear() => _readings.Clear();

        /// <summary>Open the baselines beside a project file.</summary>
        /// <param name="fs">The file system.</param>
        /// <param name="project">The project file this belongs to.</param>
        public static SetBaselineStore Open(IFileSystem fs, ProjectStore? project)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));

            var path = PathFor(project?.Path);

            if (path == null)
                return new SetBaselineStore(fs, null,
                    "in memory only — nothing will be kept when Navisworks closes",
                    New(), Array.Empty<string>());

            var notes = new List<string>();
            string? text = null;

            try
            {
                text = fs.ReadAllText(path);
            }
            catch (Exception e)
            {
                notes.Add("The set baselines could not be read, so this session starts with none: "
                          + e.Message);
            }

            var readings = New();

            if (text != null)
            {
                if (JsonReader.TryParse(text, out var json)) Read(json, readings, notes);
                else
                    notes.Add("The set baselines are not readable JSON, so this session starts "
                              + "with none. The file has been left alone rather than overwritten.");
            }

            return new SetBaselineStore(fs, path,
                "beside the document, in " + ProjectStore.SidecarFolder, readings, notes);
        }

        /// <summary>Where the baselines for a project file live.</summary>
        /// <param name="projectPath">The project file's path, or null.</param>
        public static string? PathFor(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) return null;

            var directory = System.IO.Path.GetDirectoryName(projectPath!);
            var stem = System.IO.Path.GetFileNameWithoutExtension(projectPath!);

            if (directory == null || string.IsNullOrEmpty(stem)) return null;

            return PathText.Join(directory, stem + Extension);
        }

        /// <summary>
        /// Write it.
        ///
        /// Returns false rather than throwing, and leaves the reason on
        /// <see cref="LastSaveProblem"/>: a failed save is a line on a pane, not something every
        /// caller wraps in a try.
        /// </summary>
        public bool Save()
        {
            LastSaveProblem = null;

            if (IsMemoryOnly)
            {
                LastSaveProblem = "there is nowhere to keep set baselines — " + Where;
                return false;
            }

            try
            {
                var folder = System.IO.Path.GetDirectoryName(Path!);

                if (!string.IsNullOrEmpty(folder)) _fs.CreateDirectory(folder!);

                AtomicFile.Write(_fs, Path!, ToJson().ToJson());
                return true;
            }
            catch (IOException e)
            {
                LastSaveProblem = "could not write " + Path + ": " + e.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                LastSaveProblem = "no permission to write " + Path;
                return false;
            }
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("version", Version)
                .Set("sets", JsonValue.Array(
                    _readings.Values
                        .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(Write)));

        /// <inheritdoc />
        public override string ToString() =>
            Count.ToString(CultureInfo.InvariantCulture)
            + (Count == 1 ? " set read before, " : " sets read before, ") + Where;

        // -----------------------------------------------------------------------------------

        private static Dictionary<string, SetReading> New() =>
            new Dictionary<string, SetReading>(StringComparer.OrdinalIgnoreCase);

        private static JsonValue Write(SetReading reading)
        {
            var json = JsonValue.Object()
                .Set("name", reading.Name)
                .Set("held", reading.Held);

            if (reading.WhenTicks > 0) json.Set("when", reading.WhenTicks);
            if (!reading.IsWhole) json.Set("sampled", true);

            if (reading.Members.Count > 0)
                json.Set("members", JsonValue.Array(
                    reading.Members.Select(k => JsonValue.String(k.ToString()))));

            return json;
        }

        private static void Read(JsonValue? json, Dictionary<string, SetReading> readings,
                                 IList<string> notes)
        {
            var unreadable = 0;

            foreach (var record in (json ?? JsonValue.Object())["sets"].Items)
            {
                var name = record["name"].AsString();

                if (string.IsNullOrWhiteSpace(name)) { unreadable++; continue; }

                var members = new List<ElementKey>();

                foreach (var member in record["members"].Items)
                    if (ElementKey.TryParse(member.AsString(), out var key) && !key.IsEmpty)
                        members.Add(key);

                readings[name!] = new SetReading(name!, members, record["when"].AsLong(),
                                                 whole: !record["sampled"].AsBool(),
                                                 held: (int)record["held"].AsLong());
            }

            if (unreadable > 0)
                notes.Add(unreadable.ToString(CultureInfo.InvariantCulture)
                    + " set baseline(s) could not be read and were left out. The rest are here.");
        }
    }
}
