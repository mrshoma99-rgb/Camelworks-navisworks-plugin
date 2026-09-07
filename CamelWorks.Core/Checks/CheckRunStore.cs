using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// A saved, named, re-runnable run of one check, as the project file holds it.
    ///
    /// The settings are an open bag of strings, for the reason <see cref="Findings.Finding.Facts"/>
    /// is: a clearance run stores a minimum, surfaces, targets, a slope and a minimum area; a
    /// fall-hazard run stores a level, a band, a cell size, a gap and whether handrails count. The
    /// store has no need to know what any of those words mean, so a new check adds a run type
    /// without a schema change, and a key this build does not know is round-tripped untouched
    /// rather than dropped.
    /// </summary>
    public sealed class CheckRunSpec
    {
        /// <summary>Describe a saved run.</summary>
        /// <param name="checkKey">Which check owns it, as <see cref="ICheck.Key"/>.</param>
        /// <param name="id">Its id, unique within that check.</param>
        /// <param name="name">What it is called.</param>
        public CheckRunSpec(string checkKey, string id, string name)
        {
            if (string.IsNullOrWhiteSpace(checkKey))
                throw new ArgumentException("checkKey is required", nameof(checkKey));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));

            CheckKey = checkKey;
            Id = id;
            Name = name ?? string.Empty;
        }

        /// <summary>Which check owns it.</summary>
        public string CheckKey { get; }

        /// <summary>Its id, unique within that check.</summary>
        public string Id { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; set; }

        /// <summary>The folder it is filed in, or null for the root.</summary>
        public string? Folder { get; set; }

        /// <summary>Everything the check needs to run it again, by its own names.</summary>
        public IDictionary<string, string?> Settings { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When it last ran, as UTC ticks, or zero for a run saved and never run.</summary>
        public long LastRunTicks { get; set; }

        /// <summary>
        /// What the last run of this run could not do, in its own words.
        ///
        /// Per run and saved with it, because a caveat belongs to the answer it qualifies. A check
        /// that held these in a field of its own lost run A's caveat the moment run B started, and
        /// showed A's caveat to somebody reading B. They are written by
        /// <see cref="ICheckRunner.Run"/> and read back by <c>Read</c>.
        /// </summary>
        public IList<string> Notes { get; } = new List<string>();

        /// <summary>Replace what the last run of this run said with what this one said.</summary>
        /// <param name="notes">What it could not do. Null or empty leaves it saying nothing.</param>
        public void Said(IEnumerable<string>? notes)
        {
            Notes.Clear();

            foreach (var note in notes ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(note)) Notes.Add(note);
        }

        /// <summary>
        /// The same run under a new id and name, for a second corridor or the next storey.
        ///
        /// Most of a second test is the first one: ten corridors on ten levels differ by a name, a
        /// zone and sometimes a height, and without this each of them is the whole form again.
        ///
        /// What is not copied is what the copy has not earned. The last-run time is cleared, so the
        /// copy reads as saved and never run rather than as already answered; the notes go, because
        /// they describe a run that happened to the original; and the caller names the derived keys
        /// its own check writes — the record of what a run last read, and any list of results
        /// somebody dropped — because those are keyed to findings this copy has not produced.
        /// </summary>
        /// <param name="id">The copy's id, unique within the check.</param>
        /// <param name="name">What the copy is called.</param>
        /// <param name="without">Settings keys the copy must not inherit, or null for none.</param>
        public CheckRunSpec CopyAs(string id, string name, IEnumerable<string>? without = null)
        {
            var copy = new CheckRunSpec(CheckKey, id, name) { Folder = Folder };

            var dropped = new HashSet<string>(without ?? Array.Empty<string>(),
                                              StringComparer.OrdinalIgnoreCase);

            foreach (var setting in Settings)
                if (!dropped.Contains(setting.Key)) copy.Settings[setting.Key] = setting.Value;

            return copy;
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var settings = JsonValue.Object();

            foreach (var pair in Settings.OrderBy(p => p.Key, StringComparer.Ordinal))
                settings.Set(pair.Key, JsonValue.String(pair.Value));

            var json = JsonValue.Object()
                .Set("check", CheckKey)
                .Set("id", Id)
                .Set("name", Name);

            if (Folder != null) json.Set("folder", Folder);
            if (LastRunTicks != 0) json.Set("lastRun", LastRunTicks);

            if (Notes.Count > 0)
            {
                var notes = JsonValue.Array();

                foreach (var note in Notes) notes.Add(JsonValue.String(note));

                json.Set("notes", notes);
            }

            return json.Set("settings", settings);
        }

        /// <summary>Read one back. Null when the record names no check or no id.</summary>
        /// <param name="json">The stored record, or null.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static CheckRunSpec? FromJson(JsonValue? json, IList<string>? notes = null)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var checkKey = json["check"].AsString();
            var id = json["id"].AsString();

            if (string.IsNullOrWhiteSpace(checkKey) || string.IsNullOrWhiteSpace(id))
            {
                notes?.Add("A saved run named no check or no id and was left out.");
                return null;
            }

            var spec = new CheckRunSpec(checkKey!, id!, json["name"].AsString() ?? string.Empty)
            {
                Folder = json["folder"].AsString(),
                LastRunTicks = json["lastRun"].AsLong(),
            };

            foreach (var note in json["notes"].Items)
            {
                var said = note.AsString();

                if (!string.IsNullOrWhiteSpace(said)) spec.Notes.Add(said!);
            }

            var settings = json["settings"];

            foreach (var key in settings.Keys)
                spec.Settings[key] = settings[key].AsString();

            return spec;
        }

        /// <inheritdoc />
        public override string ToString() => CheckKey + "/" + Id + " " + Name;
    }

    /// <summary>
    /// The saved runs of every check, in the project file.
    ///
    /// Two things share the <c>checks</c> section — this under <see cref="Key"/> and
    /// <see cref="RunJournal"/> under its own — and each reads and writes only its own key. A store
    /// that wrote the section wholesale would erase the other on every save.
    /// </summary>
    public sealed class CheckRunStore
    {
        /// <summary>The project-file section both this and <see cref="RunJournal"/> live in.</summary>
        public const string Section = "checks";

        /// <summary>Where the saved runs sit inside that section.</summary>
        public const string Key = "runs";

        private readonly List<CheckRunSpec> _specs = new List<CheckRunSpec>();

        /// <summary>How many runs are saved, across every check.</summary>
        public int Count => _specs.Count;

        /// <summary>Every saved run, in no particular order.</summary>
        public IReadOnlyList<CheckRunSpec> All => _specs;

        /// <summary>The runs one check has saved.</summary>
        /// <param name="checkKey">The check's key.</param>
        public IReadOnlyList<CheckRunSpec> For(string? checkKey) =>
            _specs.Where(spec => Same(spec.CheckKey, checkKey)).ToList();

        /// <summary>One saved run, or null.</summary>
        /// <param name="checkKey">The check's key.</param>
        /// <param name="id">The run id.</param>
        public CheckRunSpec? Find(string? checkKey, string? id) =>
            _specs.FirstOrDefault(spec => Same(spec.CheckKey, checkKey) && Same(spec.Id, id));

        /// <summary>Save a run, replacing any held under the same check and id.</summary>
        /// <param name="spec">The run.</param>
        public void Put(CheckRunSpec? spec)
        {
            if (spec == null) return;

            var held = Find(spec.CheckKey, spec.Id);

            if (held != null) _specs[_specs.IndexOf(held)] = spec;
            else _specs.Add(spec);
        }

        /// <summary>Forget a run. True when it was there.</summary>
        /// <param name="checkKey">The check's key.</param>
        /// <param name="id">The run id.</param>
        public bool Remove(string? checkKey, string? id)
        {
            var held = Find(checkKey, id);

            return held != null && _specs.Remove(held);
        }

        /// <summary>Serialise the runs alone. The rest of the section is not this type's to write.</summary>
        public JsonValue ToJson() =>
            JsonValue.Array(_specs
                .OrderBy(spec => spec.CheckKey, StringComparer.Ordinal)
                .ThenBy(spec => spec.Id, StringComparer.Ordinal)
                .Select(spec => spec.ToJson()));

        /// <summary>Read the saved runs out of a project file. Never null: nothing saved is ordinary.</summary>
        /// <param name="store">The project file.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static CheckRunStore Read(ProjectStore store, IList<string>? notes = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            return FromJson(store.Section(Section)[Key], notes);
        }

        /// <summary>Read the saved runs back out of what <see cref="ToJson"/> wrote.</summary>
        /// <param name="json">The stored value, or null.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static CheckRunStore FromJson(JsonValue? json, IList<string>? notes = null)
        {
            var runs = new CheckRunStore();
            var unreadable = 0;

            foreach (var record in json?.Items ?? Array.Empty<JsonValue>())
            {
                var spec = CheckRunSpec.FromJson(record, notes);

                if (spec == null) { unreadable++; continue; }

                runs.Put(spec);
            }

            if (unreadable > 0)
                notes?.Add(unreadable.ToString(CultureInfo.InvariantCulture)
                           + " saved run(s) could not be read and were left out.");

            return runs;
        }

        /// <summary>
        /// Write the saved runs into the project file, leaving every other key in the section — the
        /// journals among them — exactly as it was.
        /// </summary>
        /// <param name="store">The project file.</param>
        public void Write(ProjectStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            store.Section(Section).Set(Key, ToJson());
        }

        private static bool Same(string? a, string? b) =>
            a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override string ToString() =>
            Count.ToString(CultureInfo.InvariantCulture) + " saved run(s)";
    }
}
