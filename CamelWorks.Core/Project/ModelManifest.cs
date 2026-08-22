using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>What one model was, at the moment a run happened.</summary>
    public sealed class ModelRecord
    {
        /// <summary>Create a record.</summary>
        /// <param name="scope">The scope hash every key from this model carries.</param>
        /// <param name="displayName">How the host names it.</param>
        /// <param name="sourcePath">Where it came from.</param>
        public ModelRecord(string scope, string displayName, string sourcePath)
        {
            Scope = scope ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "(unnamed model)" : displayName.Trim();
            SourcePath = sourcePath ?? string.Empty;
        }

        /// <summary>The scope hash every key from this model carries.</summary>
        public string Scope { get; }

        /// <summary>How the host names it.</summary>
        public string DisplayName { get; }

        /// <summary>Where it came from.</summary>
        public string SourcePath { get; }

        /// <summary>When the source file was last written, UTC.</summary>
        public DateTimeOffset? Modified { get; set; }

        /// <summary>How many bytes it is.</summary>
        public long? Bytes { get; set; }

        /// <summary>The unit it was authored in.</summary>
        public string? ModelUnits { get; set; }

        /// <summary>True when the host is holding it somewhere other than where it was authored.</summary>
        public bool HasTransform { get; set; }

        /// <summary>How many elements carrying geometry it contributed.</summary>
        public int? Items { get; set; }

        /// <summary>Its extent, when somebody measured it.</summary>
        public BoundingBox? Bounds { get; set; }

        /// <summary>The discipline in force for it, when the profile worked one out.</summary>
        public string? Discipline { get; set; }

        /// <summary>
        /// Where the federation was holding it: twelve numbers, as <c>IModelSource.Placement</c>
        /// writes them. Kept so a placement that changes can be put back.
        /// </summary>
        public IReadOnlyList<double>? Placement { get; set; }

        /// <summary>
        /// True when this model is somewhere other than where the other record had it.
        ///
        /// Compared at four decimal places, which is a tenth of a millimetre in metres: a re-export
        /// nudges the last bit of a double, and a health check that called that a move would cry
        /// wolf every week.
        /// </summary>
        /// <param name="other">The record to compare against.</param>
        public bool MovedSince(ModelRecord? other)
        {
            if (other?.Placement == null || Placement == null) return false;
            if (other.Placement.Count != Placement.Count) return true;

            for (var i = 0; i < Placement.Count; i++)
                if (Math.Abs(Placement[i] - other.Placement[i]) > 0.0001)
                    return true;

            return false;
        }

        /// <summary>The timestamp as a report prints it.</summary>
        public string When =>
            Modified.HasValue
                ? Modified.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                : Measure.Nothing;

        /// <summary>The size as a report prints it.</summary>
        public string Size => Bytes.HasValue ? Readable(Bytes.Value) : Measure.Nothing;

        /// <summary>
        /// True when this is the same file, byte for byte as far as anything here can tell.
        ///
        /// Same size and same timestamp. Not a hash: hashing a two-gigabyte NWC across a site VPN
        /// to answer "did this change" costs more than the answer is worth, and the pair is wrong
        /// only when somebody rewrites a file to exactly its old length within the same minute.
        /// </summary>
        /// <param name="other">The record to compare against.</param>
        public bool LooksIdenticalTo(ModelRecord? other) =>
            other != null && Bytes == other.Bytes
            && Modified.HasValue == other.Modified.HasValue
            && (!Modified.HasValue
                || Math.Abs((Modified!.Value - other.Modified!.Value).TotalSeconds) < 1);

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("scope", JsonValue.String(Scope))
                .Set("name", JsonValue.String(DisplayName))
                .Set("path", JsonValue.String(SourcePath));

            if (Modified.HasValue) json.Set("modified", JsonValue.Number(Modified.Value.ToUnixTimeSeconds()));
            if (Bytes.HasValue) json.Set("bytes", JsonValue.Number(Bytes.Value));
            if (ModelUnits != null) json.Set("units", JsonValue.String(ModelUnits));
            if (HasTransform) json.Set("moved", JsonValue.Bool(true));
            if (Items.HasValue) json.Set("items", JsonValue.Number(Items.Value));
            if (Discipline != null) json.Set("discipline", JsonValue.String(Discipline));

            if (Placement != null && Placement.Count == 12)
                json.Set("placement", JsonValue.Array(Placement.Select(JsonValue.Number)));

            if (Bounds.HasValue)
            {
                var b = Bounds.Value;

                json.Set("bounds", JsonValue.Array(new[]
                {
                    JsonValue.Number(b.MinX), JsonValue.Number(b.MinY), JsonValue.Number(b.MinZ),
                    JsonValue.Number(b.MaxX), JsonValue.Number(b.MaxY), JsonValue.Number(b.MaxZ),
                }));
            }

            return json;
        }

        /// <summary>Read one back, or null when it is not a record.</summary>
        /// <param name="json">What was written.</param>
        public static ModelRecord? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var scope = json["scope"].AsString();
            if (scope == null) return null;

            var record = new ModelRecord(scope, json["name"].AsString() ?? "(unnamed model)",
                                         json["path"].AsString() ?? string.Empty)
            {
                ModelUnits = json["units"].AsString(),
                HasTransform = json["moved"].AsBool(),
                Discipline = json["discipline"].AsString(),
            };

            if (json["modified"].Kind == JsonKind.Number)
                record.Modified = DateTimeOffset.FromUnixTimeSeconds(json["modified"].AsLong());

            if (json["bytes"].Kind == JsonKind.Number) record.Bytes = json["bytes"].AsLong();
            if (json["items"].Kind == JsonKind.Number) record.Items = (int)json["items"].AsLong();

            var placement = json["placement"].Items;

            if (placement.Count == 12) record.Placement = placement.Select(v => v.AsDouble()).ToList();

            var bounds = json["bounds"].Items;

            if (bounds.Count == 6)
                record.Bounds = new BoundingBox(bounds[0].AsDouble(), bounds[1].AsDouble(),
                                                bounds[2].AsDouble(), bounds[3].AsDouble(),
                                                bounds[4].AsDouble(), bounds[5].AsDouble());

            return record;
        }

        /// <inheritdoc />
        public override string ToString() => DisplayName + " - " + When;

        private static string Readable(long bytes)
        {
            if (bytes < 1024) return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";

            double size = bytes;
            var suffixes = new[] { "KB", "MB", "GB", "TB" };
            var at = -1;

            do
            {
                size /= 1024;
                at++;
            }
            while (size >= 1024 && at < suffixes.Length - 1);

            return size.ToString(size >= 100 ? "N0" : "N1", CultureInfo.InvariantCulture) + " " + suffixes[at];
        }
    }

    /// <summary>One clash test, and when it last ran.</summary>
    public sealed class TestRecord
    {
        /// <summary>Create a record.</summary>
        /// <param name="name">The test's name.</param>
        /// <param name="lastRun">When it last ran, UTC.</param>
        /// <param name="results">How many results it produced.</param>
        public TestRecord(string name, DateTimeOffset? lastRun = null, int? results = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed test)" : name.Trim();
            LastRun = lastRun;
            Results = results;
        }

        /// <summary>The test's name.</summary>
        public string Name { get; }

        /// <summary>When it last ran.</summary>
        public DateTimeOffset? LastRun { get; }

        /// <summary>How many results it produced.</summary>
        public int? Results { get; }

        /// <summary>When it last ran, as a report prints it.</summary>
        public string When =>
            LastRun.HasValue
                ? LastRun.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                : "never run";

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("name", JsonValue.String(Name));

            if (LastRun.HasValue) json.Set("run", JsonValue.Number(LastRun.Value.ToUnixTimeSeconds()));
            if (Results.HasValue) json.Set("results", JsonValue.Number(Results.Value));

            return json;
        }

        /// <summary>Read one back, or null.</summary>
        /// <param name="json">What was written.</param>
        public static TestRecord? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var name = json["name"].AsString();
            if (name == null) return null;

            return new TestRecord(name,
                json["run"].Kind == JsonKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(json["run"].AsLong())
                    : (DateTimeOffset?)null,
                json["results"].Kind == JsonKind.Number ? (int)json["results"].AsLong() : (int?)null);
        }

        /// <inheritdoc />
        public override string ToString() => Name + " - " + When;
    }

    /// <summary>
    /// What this run was run against.
    ///
    /// "Which revision of the structural model was this against?" is the first question in the
    /// Wednesday meeting and the only question six months later, and no coordination tool in this
    /// price bracket answers it. A PDF that names its own inputs — every model, its path, its
    /// timestamp, its size, its units, whether anything moved it, and which tests ran when — is the
    /// difference between a report and a screenshot.
    ///
    /// It is also the record the intake register compares against, so the same six scalars serve
    /// both "what was this run against" and "what arrived this week".
    /// </summary>
    public sealed class ModelManifest
    {
        /// <summary>Create a manifest.</summary>
        /// <param name="models">One record per loaded model.</param>
        /// <param name="tests">One record per clash test.</param>
        public ModelManifest(IReadOnlyList<ModelRecord>? models = null,
                             IReadOnlyList<TestRecord>? tests = null)
        {
            Models = models ?? Array.Empty<ModelRecord>();
            Tests = tests ?? Array.Empty<TestRecord>();
        }

        /// <summary>One record per loaded model.</summary>
        public IReadOnlyList<ModelRecord> Models { get; }

        /// <summary>One record per clash test.</summary>
        public IReadOnlyList<TestRecord> Tests { get; }

        /// <summary>Which CamelWorks wrote it.</summary>
        public string? Version { get; set; }

        /// <summary>The project it was written for.</summary>
        public string? ProjectName { get; set; }

        /// <summary>Who it says produced it.</summary>
        public string? Author { get; set; }

        /// <summary>When it was taken, UTC.</summary>
        public DateTimeOffset? TakenUtc { get; set; }

        /// <summary>True when nothing was loaded.</summary>
        public bool IsEmpty => Models.Count == 0;

        /// <summary>
        /// Take a manifest of what is open.
        /// </summary>
        /// <param name="models">The loaded models.</param>
        /// <param name="profile">The project profile, for identity and per-model discipline.</param>
        /// <param name="tests">One record per clash test, when the host has an engine.</param>
        /// <param name="itemCounts">Geometry-item counts by model scope, when somebody counted.</param>
        /// <param name="bounds">Extents by model scope, when somebody measured.</param>
        /// <param name="version">Which CamelWorks is writing it.</param>
        /// <param name="takenUtc">When it was taken. Passed in rather than read, so a report is reproducible.</param>
        public static ModelManifest Of(IReadOnlyList<IModelSource>? models,
                                       ProjectProfile? profile = null,
                                       IReadOnlyList<TestRecord>? tests = null,
                                       IReadOnlyDictionary<string, int>? itemCounts = null,
                                       IReadOnlyDictionary<string, BoundingBox>? bounds = null,
                                       string? version = null,
                                       DateTimeOffset? takenUtc = null)
        {
            var records = new List<ModelRecord>();

            foreach (var model in models ?? Array.Empty<IModelSource>())
            {
                if (model == null) continue;

                var record = new ModelRecord(model.Scope, model.DisplayName, model.SourcePath)
                {
                    Modified = model.SourceModifiedUtc,
                    Bytes = model.SourceBytes,
                    ModelUnits = model.ModelUnits,
                    HasTransform = model.HasTransform,
                    Discipline = profile?.DisciplineOf(model.Scope),
                    Placement = model.Placement,
                };

                if (itemCounts != null && itemCounts.TryGetValue(model.Scope, out var count))
                    record.Items = count;

                if (bounds != null && bounds.TryGetValue(model.Scope, out var box)) record.Bounds = box;

                records.Add(record);
            }

            return new ModelManifest(records, tests)
            {
                Version = version,
                ProjectName = profile?.ProjectName,
                Author = profile?.Author,
                TakenUtc = takenUtc,
            };
        }

        /// <summary>The models, as a report prints them.</summary>
        /// <param name="units">How this project writes a length.</param>
        public TableBlock ModelTable(Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            var table = new TableBlock("Model", "Discipline", "Source last written", "Size", "Units",
                                       "Placement", "Elements", "Extent (" + measure.LengthUnit + ")");

            foreach (var model in Models)
                table.Row(model.DisplayName,
                          model.Discipline ?? string.Empty,
                          model.When,
                          model.Size,
                          model.ModelUnits ?? Measure.Nothing,
                          model.HasTransform ? "moved by the federation" : "as authored",
                          model.Items.HasValue ? Measure.Count(model.Items.Value) : Measure.Nothing,
                          Extent(model.Bounds, measure));

            return table;
        }

        /// <summary>The tests, as a report prints them.</summary>
        public TableBlock TestTable()
        {
            var table = new TableBlock("Test", "Last run", "Results");

            foreach (var test in Tests)
                table.Row(test.Name, test.When,
                          test.Results.HasValue ? Measure.Count(test.Results.Value) : Measure.Nothing);

            return table;
        }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsEmpty) return "nothing loaded";

            var s = Measure.Count(Models.Count) + (Models.Count == 1 ? " model" : " models");

            var dated = Models.Count(m => m.Modified.HasValue);
            if (dated < Models.Count) s += ", " + Measure.Count(Models.Count - dated) + " with no readable source";

            var moved = Models.Count(m => m.HasTransform);
            if (moved > 0) s += ", " + Measure.Count(moved) + " moved by the federation";

            if (Tests.Count > 0) s += ", " + Measure.Count(Tests.Count)
                                        + (Tests.Count == 1 ? " test" : " tests");

            return s;
        }

        /// <summary>
        /// What arrived, what is unchanged, what is missing, since a previous manifest.
        ///
        /// The intake register. Compared by scope rather than by path, so a model re-pointed at a
        /// new folder is still the same model and shows as changed rather than as one arrival and
        /// one disappearance.
        /// </summary>
        /// <param name="previous">Last week's manifest, or null for a first run.</param>
        public ModelIntake Since(ModelManifest? previous) => new ModelIntake(this, previous);

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("models", JsonValue.Array(Models.Select(m => m.ToJson())))
                .Set("tests", JsonValue.Array(Tests.Select(t => t.ToJson())));

            if (Version != null) json.Set("version", JsonValue.String(Version));
            if (ProjectName != null) json.Set("project", JsonValue.String(ProjectName));
            if (Author != null) json.Set("author", JsonValue.String(Author));
            if (TakenUtc.HasValue) json.Set("taken", JsonValue.Number(TakenUtc.Value.ToUnixTimeSeconds()));

            return json;
        }

        /// <summary>Read one back. An unreadable record is dropped, never guessed at.</summary>
        /// <param name="json">What was written.</param>
        public static ModelManifest FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return new ModelManifest();

            var models = json["models"].Items.Select(ModelRecord.FromJson)
                                             .Where(m => m != null).Select(m => m!).ToList();

            var tests = json["tests"].Items.Select(TestRecord.FromJson)
                                           .Where(t => t != null).Select(t => t!).ToList();

            return new ModelManifest(models, tests)
            {
                Version = json["version"].AsString(),
                ProjectName = json["project"].AsString(),
                Author = json["author"].AsString(),
                TakenUtc = json["taken"].Kind == JsonKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(json["taken"].AsLong())
                    : (DateTimeOffset?)null,
            };
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static string Extent(BoundingBox? box, Measure units)
        {
            if (!box.HasValue) return Measure.Nothing;

            var b = box.Value;

            return units.Bare(b.MaxX - b.MinX) + " x " + units.Bare(b.MaxY - b.MinY)
                 + " x " + units.Bare(b.MaxZ - b.MinZ);
        }
    }

    /// <summary>
    /// What changed between two manifests: the model intake register.
    ///
    /// The question it answers is the one that opens every coordination meeting — who published
    /// this week and who did not — and today it is answered by opening properties on eight files.
    /// </summary>
    public sealed class ModelIntake
    {
        internal ModelIntake(ModelManifest now, ModelManifest? before)
        {
            var previous = (before?.Models ?? Array.Empty<ModelRecord>())
                .GroupBy(m => m.Scope, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var arrived = new List<ModelRecord>();
            var unchanged = new List<ModelRecord>();
            var changed = new List<ModelRecord>();
            var moved = new List<ModelRecord>();

            foreach (var model in now.Models)
            {
                if (!previous.TryGetValue(model.Scope, out var was)) { arrived.Add(model); continue; }

                if (model.MovedSince(was)) moved.Add(model);

                if (model.LooksIdenticalTo(was)) unchanged.Add(model); else changed.Add(model);
            }

            var here = new HashSet<string>(now.Models.Select(m => m.Scope), StringComparer.Ordinal);

            Arrived = arrived;
            Unchanged = unchanged;
            Changed = changed;
            Moved = moved;
            Before = previous;
            Missing = previous.Values.Where(m => !here.Contains(m.Scope)).ToList();
            HasPrevious = before != null && before.Models.Count > 0;
        }

        /// <summary>Models that were not in the previous manifest.</summary>
        public IReadOnlyList<ModelRecord> Arrived { get; }

        /// <summary>Models that look byte-identical to last time.</summary>
        public IReadOnlyList<ModelRecord> Unchanged { get; }

        /// <summary>Models whose source has been rewritten.</summary>
        public IReadOnlyList<ModelRecord> Changed { get; }

        /// <summary>
        /// Models the federation is now holding somewhere else.
        ///
        /// The classic silent killer, and the one nothing else catches: a transform is applied by
        /// somebody dragging a model in the units-and-transform dialog, nobody notices, and every
        /// clash from that moment on is in the wrong place. It is listed separately from a
        /// republish because it is a different mistake with a different fix.
        /// </summary>
        public IReadOnlyList<ModelRecord> Moved { get; }

        /// <summary>Models that were in the previous manifest and are not loaded now.</summary>
        public IReadOnlyList<ModelRecord> Missing { get; }

        /// <summary>True when there was a previous manifest to compare against.</summary>
        public bool HasPrevious { get; }

        /// <summary>What each model was, by scope, so a placement can be put back.</summary>
        public IReadOnlyDictionary<string, ModelRecord> Before { get; }

        /// <summary>
        /// True when nobody has published since the last run, and nothing has been moved.
        ///
        /// Worth saying out loud: a week's coordination run against exactly last week's files
        /// produces exactly last week's board, and a coordinator who does not know that spends the
        /// meeting explaining why nothing moved.
        ///
        /// A model the federation has been moved counts as news even though its file is untouched.
        /// It is in fact the most important news there is, and an early "nobody has published"
        /// would bury exactly the thing worth reporting.
        /// </summary>
        public bool NothingNew =>
            HasPrevious && Arrived.Count == 0 && Changed.Count == 0 && Moved.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (!HasPrevious)
                return "first run - " + Measure.Count(Arrived.Count)
                     + (Arrived.Count == 1 ? " model" : " models") + ", nothing to compare against";

            if (NothingNew && Missing.Count == 0)
                return "nobody has published since the last run - all "
                     + Measure.Count(Unchanged.Count) + " models are byte-identical";

            var parts = new List<string>();

            if (Moved.Count > 0) parts.Add(Measure.Count(Moved.Count) + " MOVED BY THE FEDERATION");
            if (Changed.Count > 0) parts.Add(Measure.Count(Changed.Count) + " republished");
            if (Arrived.Count > 0) parts.Add(Measure.Count(Arrived.Count) + " new");
            if (Unchanged.Count > 0) parts.Add(Measure.Count(Unchanged.Count) + " unchanged");
            if (Missing.Count > 0) parts.Add(Measure.Count(Missing.Count) + " no longer loaded");

            return string.Join(", ", parts);
        }

        /// <summary>The register, as a screen or a report shows it.</summary>
        public TableBlock Table()
        {
            var table = new TableBlock("Model", "Discipline", "State", "Source last written", "Size");

            var moved = new HashSet<string>(Moved.Select(m => m.Scope), StringComparer.Ordinal);

            foreach (var (model, state) in Arrived.Select(m => (m, "new"))
                         .Concat(Changed.Select(m => (m, "republished")))
                         .Concat(Unchanged.Select(m => (m, "unchanged")))
                         .Concat(Missing.Select(m => (m, "no longer loaded"))))
                table.Row(model.DisplayName, model.Discipline ?? string.Empty,
                          moved.Contains(model.Scope) ? state + ", moved by the federation" : state,
                          model.When, model.Size);

            return table;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
