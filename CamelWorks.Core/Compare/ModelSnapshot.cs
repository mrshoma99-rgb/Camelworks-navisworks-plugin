using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// A federation as it stood at one moment: which models were loaded, and a fingerprint per
    /// element.
    ///
    /// This is what makes comparison possible at all without both revisions being open. Navisworks
    /// holds one version of a model at a time; the question people actually ask — "what changed
    /// since we issued this" — is about a version that is no longer on the machine. A snapshot is
    /// the only thing left that remembers it.
    ///
    /// <b>Plain text, one line per element.</b> Same reasoning as the exporter's revision manifest:
    /// it stays small, an ordinary tool can diff it, and a person can open it and see what the
    /// comparison thinks it knows. JSON would triple the size of a file whose only structure is a
    /// list.
    /// </summary>
    public sealed class ModelSnapshot
    {
        /// <summary>The first line of the file. Any other first line is not a snapshot.</summary>
        public const string Header = "CamelWorks-compare 1";

        /// <summary>The extension a snapshot is saved under.</summary>
        public const string Extension = ".cwsnapshot";

        /// <summary>
        /// Separates the three parts of one recorded property inside a field.
        ///
        /// ASCII unit separator, written as a code point rather than as itself: a literal control
        /// character in a source file survives no round trip through a diff, a patch or a review.
        /// </summary>
        internal const char Unit = (char)0x1F;

        /// <summary>How it reads on screen — a date, a milestone, an issue number.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>When it was taken, UTC.</summary>
        public DateTimeOffset? TakenUtc { get; set; }

        /// <summary>The document it was taken from.</summary>
        public string DocumentPath { get; set; } = string.Empty;

        /// <summary>The document's length unit, as the host names it.</summary>
        public string Units { get; set; } = string.Empty;

        /// <summary>Which properties were recorded — and so which a comparison can speak about.</summary>
        public PropertyWatch Watch { get; set; } = PropertyWatch.Standard;

        /// <summary>
        /// The named properties, when <see cref="Watch"/> is <see cref="PropertyWatch.Named"/>.
        ///
        /// Recorded because a comparison must only speak about properties BOTH sides were watching.
        /// Without this, a baseline taken with three named properties compared against a run
        /// watching the standard list reports every standard property on every element as newly
        /// arrived — a hundred thousand changes, none of them real.
        /// </summary>
        public IReadOnlyList<PropertyRef> Watched
        {
            get => _watched;
            set => _watched = value == null ? Array.Empty<PropertyRef>() : value.ToArray();
        }

        private IReadOnlyList<PropertyRef> _watched = Array.Empty<PropertyRef>();

        /// <summary>The properties that were held to be noise when this was taken.</summary>
        public IReadOnlyList<PropertyRef> Ignored
        {
            get => _ignored;
            set => _ignored = value == null ? Array.Empty<PropertyRef>() : value.ToArray();
        }

        private IReadOnlyList<PropertyRef> _ignored = ComparePolicy.NoisyByDefault;

        /// <summary>
        /// The settings a comparison must hold this snapshot to.
        ///
        /// Not the settings of the run comparing it — those describe what is being watched NOW.
        /// A property only takes part when both sides were watching it, and this is how the
        /// baseline's half of that is asked.
        /// </summary>
        public CompareOptions Terms() => new CompareOptions
        {
            Watch = Watch,
            Watched = _watched,
            Ignored = _ignored,
        };

        /// <summary>One record per model that was loaded.</summary>
        public IReadOnlyList<ModelRecord> Models
        {
            get => _models;
            set => _models = value == null ? Array.Empty<ModelRecord>() : value.ToArray();
        }

        private IReadOnlyList<ModelRecord> _models = Array.Empty<ModelRecord>();

        /// <summary>Every element, in the order the walk found them.</summary>
        public IReadOnlyList<ElementFingerprint> Elements => _elements;

        private readonly List<ElementFingerprint> _elements = new List<ElementFingerprint>();

        /// <summary>True when nothing was recorded.</summary>
        public bool IsEmpty => _elements.Count == 0;

        /// <summary>How many elements it holds.</summary>
        public int Count => _elements.Count;

        /// <summary>Add an element.</summary>
        /// <param name="print">The fingerprint.</param>
        public void Add(ElementFingerprint print)
        {
            if (print == null) throw new ArgumentNullException(nameof(print));
            _elements.Add(print);
        }

        /// <summary>Add several.</summary>
        /// <param name="prints">The fingerprints.</param>
        public void AddRange(IEnumerable<ElementFingerprint> prints)
        {
            if (prints == null) throw new ArgumentNullException(nameof(prints));
            foreach (var print in prints) Add(print);
        }

        /// <summary>The elements belonging to one model scope.</summary>
        /// <param name="scope">The model scope hash.</param>
        public IReadOnlyList<ElementFingerprint> InScope(string? scope) =>
            _elements.Where(e => string.Equals(e.ModelScope, scope, StringComparison.Ordinal)).ToList();

        /// <summary>How it reads on a picker — its label, or its date, and how big it is.</summary>
        public string Describe()
        {
            var name = Label.Length > 0
                ? Label
                : TakenUtc.HasValue
                    ? TakenUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                    : "unlabelled";

            return name + " — " + ComparePolicy.Count(Count) + " elements from "
                   + ComparePolicy.Count(_models.Count)
                   + (_models.Count == 1 ? " model" : " models");
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        /// <summary>Write the whole snapshot out as its text format.</summary>
        public string Write()
        {
            var sb = new StringBuilder();

            sb.Append(Header).Append('\n');
            Line(sb, "label", Label);
            Line(sb, "taken", TakenUtc.HasValue
                ? TakenUtc.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            Line(sb, "document", DocumentPath);
            Line(sb, "units", Units);
            Line(sb, "watch", Watch.ToString());
            Line(sb, "watched", Refs(_watched));
            Line(sb, "ignored", Refs(_ignored));
            Line(sb, "elements", Count.ToString(CultureInfo.InvariantCulture));

            foreach (var model in _models)
            {
                sb.Append("model\t").Append(Clean(model.Scope))
                  .Append('\t').Append(Clean(model.DisplayName))
                  .Append('\t').Append(Clean(model.SourcePath))
                  .Append('\t').Append(model.Modified.HasValue
                      ? model.Modified.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
                      : string.Empty)
                  .Append('\t').Append(model.Bytes.HasValue
                      ? model.Bytes.Value.ToString(CultureInfo.InvariantCulture)
                      : string.Empty)
                  .Append('\t').Append(model.Items.HasValue
                      ? model.Items.Value.ToString(CultureInfo.InvariantCulture)
                      : string.Empty)
                  .Append('\n');
            }

            foreach (var e in _elements) Row(sb, e);

            return sb.ToString();
        }

        private static void Row(StringBuilder sb, ElementFingerprint e)
        {
            sb.Append(((int)e.Rung).ToString(CultureInfo.InvariantCulture))
              .Append('\t').Append(e.Local)
              .Append('\t').Append(e.Scoped)
              .Append('\t').Append(e.ModelScope)
              .Append('\t').Append(Number(e.CentreX))
              .Append('\t').Append(Number(e.CentreY))
              .Append('\t').Append(Number(e.CentreZ))
              .Append('\t').Append(Number(e.SizeX))
              .Append('\t').Append(Number(e.SizeY))
              .Append('\t').Append(Number(e.SizeZ))
              .Append('\t').Append(e.HasGeometry ? "g" : "-")
              .Append('\t').Append(e.MeshHash ?? "-")
              .Append('\t').Append(Clean(e.Name))
              .Append('\t').Append(Clean(e.Category))
              .Append('\t').Append(Clean(e.Type));

            foreach (var v in e.Values)
            {
                sb.Append('\t').Append(Clean(v.Property.Category))
                  .Append(Unit).Append(Clean(v.Property.Name))
                  .Append(Unit).Append(Clean(v.Value));
            }

            sb.Append('\n');
        }

        /// <summary>
        /// Read a snapshot back, or null when the text is not one.
        ///
        /// Null rather than an exception: this is a file a person can move, rename and truncate,
        /// and a comparison that cannot find its baseline has an ordinary answer — offer to take
        /// one. A malformed element line is skipped rather than failing the load, for the same
        /// reason the exporter's manifest does it: most of a damaged baseline is still a real
        /// comparison, and refusing it turns a bad line into a lost week.
        /// </summary>
        /// <param name="text">The file's contents.</param>
        public static ModelSnapshot? Read(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text!.Split('\n');
            if (lines.Length == 0 || lines[0].TrimEnd('\r') != Header) return null;

            var snapshot = new ModelSnapshot();
            var models = new List<ModelRecord>();

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;

                var fields = line.Split('\t');

                if (fields.Length == 1) { Head(snapshot, line); continue; }

                if (fields[0] == "model")
                {
                    var record = Model(fields);
                    if (record != null) models.Add(record);
                    continue;
                }

                var print = Element(fields);
                if (print != null) snapshot._elements.Add(print);
            }

            snapshot.Models = models;
            return snapshot;
        }

        private static void Head(ModelSnapshot snapshot, string line)
        {
            var at = line.IndexOf('=');
            if (at < 0) return;

            var key = line.Substring(0, at);
            var value = line.Substring(at + 1);

            switch (key)
            {
                case "label": snapshot.Label = value; break;
                case "document": snapshot.DocumentPath = value; break;
                case "units": snapshot.Units = value; break;

                case "taken":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                        snapshot.TakenUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    break;

                case "watched": snapshot.Watched = Refs(value); break;
                case "ignored": snapshot.Ignored = Refs(value); break;

                case "watch":
                    // A snapshot written by a later build may name a mode this one has never heard
                    // of. Falling back to Standard would be a claim about the file's contents that
                    // nothing here can support, so an unknown mode reads as All: compare whatever
                    // is actually in the file rather than assume a list.
                    snapshot.Watch = Enum.TryParse<PropertyWatch>(value, ignoreCase: true, out var watch)
                        ? watch
                        : PropertyWatch.All;
                    break;
            }
        }

        private static ModelRecord? Model(string[] fields)
        {
            if (fields.Length < 4) return null;

            var record = new ModelRecord(fields[1], fields[2], fields[3]);

            if (fields.Length > 4 && long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var when))
                record.Modified = DateTimeOffset.FromUnixTimeSeconds(when);

            if (fields.Length > 5 && long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                record.Bytes = bytes;

            if (fields.Length > 6 && int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var items))
                record.Items = items;

            return record;
        }

        private static ElementFingerprint? Element(string[] fields)
        {
            if (fields.Length < 15) return null;
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rung)) return null;
            if (rung < (int)KeyRung.InstanceGuid || rung > (int)KeyRung.Geometry) return null;

            var print = new ElementFingerprint((KeyRung)rung, fields[1], fields[2], fields[3])
            {
                CentreX = Real(fields[4]),
                CentreY = Real(fields[5]),
                CentreZ = Real(fields[6]),
                SizeX = Real(fields[7]),
                SizeY = Real(fields[8]),
                SizeZ = Real(fields[9]),
                HasGeometry = fields[10] == "g",
                MeshHash = fields[11] == "-" ? null : fields[11],
                Name = fields[12],
                Category = fields[13],
                Type = fields[14],
            };

            if (fields.Length > 15)
            {
                var values = new List<WatchedValue>(fields.Length - 15);

                for (var i = 15; i < fields.Length; i++)
                {
                    var parts = fields[i].Split(Unit);
                    if (parts.Length < 3) continue;

                    values.Add(new WatchedValue(new PropertyRef(parts[0], parts[1]), parts[2]));
                }

                print.Values = values;
            }

            return print;
        }

        private static string Refs(IReadOnlyList<PropertyRef> refs) =>
            string.Join(Unit.ToString(), refs.Select(r => r.ToStorage()));

        private static IReadOnlyList<PropertyRef> Refs(string value) =>
            value.Length == 0
                ? Array.Empty<PropertyRef>()
                : value.Split(Unit)
                       .Where(part => part.Length > 0)
                       .Select(PropertyRef.FromStorage)
                       .Where(r => !r.IsEmpty)
                       .ToArray();

        private static void Line(StringBuilder sb, string key, string value) =>
            sb.Append(key).Append('=').Append(Clean(value)).Append('\n');

        /// <summary>
        /// One numeric field.
        ///
        /// Named Real and not Read: <see cref="Read(string)"/> is the public entry point, and an
        /// overload differing from it only in a nullable annotation is the same signature to the
        /// runtime — which the compiler says so plainly that it is worth never learning twice.
        /// </summary>
        private static double Real(string field) =>
            double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? "0"
                : value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>
        /// A tab, a newline or a unit separator inside a value would break the format.
        ///
        /// Replaced rather than escaped, exactly as the exporter's manifest does it: a name is a
        /// label, not a payload, and an escaping scheme is a parser somebody has to maintain for a
        /// case that does not arise.
        /// </summary>
        private static string Clean(string? value) =>
            string.IsNullOrEmpty(value)
                ? string.Empty
                : value!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace(Unit, ' ');
    }
}
