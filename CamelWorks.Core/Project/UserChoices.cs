using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// The small settings somebody sets once and expects to still be there tomorrow.
    ///
    /// <b>The person's, not the project's.</b> How far Focus stands back, how big a marker is,
    /// whether clicking a row moves the camera - none of that is a fact about the job, and putting
    /// it in the project file would mean the coordinator who prefers a close camera imposing it on
    /// everybody else who opens the same model. It lives beside the user, in their own roaming
    /// application data, and follows them from job to job.
    ///
    /// <b>Nothing here is worth failing over.</b> A settings file that cannot be read is a set of
    /// defaults, and one that cannot be written is a preference that lasts until the session ends.
    /// Neither is a reason to stop somebody working, so every path in here is guarded and silent.
    /// </summary>
    public sealed class UserChoices
    {
        private readonly Dictionary<string, string> _by =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where the file lives, or null when this machine will not say.</summary>
        public static string? Path
        {
            get
            {
                try
                {
                    var roaming = Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData);

                    return string.IsNullOrWhiteSpace(roaming)
                        ? null
                        : System.IO.Path.Combine(roaming, "CamelWorks", "choices.json");
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>How many choices are held.</summary>
        public int Count => _by.Count;

        /// <summary>What was chosen, or the fallback.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="fallback">What it is when nobody has chosen.</param>
        public bool Get(string key, bool fallback) =>
            _by.TryGetValue(key ?? string.Empty, out var held)
            && bool.TryParse(held, out var value)
                ? value
                : fallback;

        /// <summary>What was chosen, or the fallback.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="fallback">What it is when nobody has chosen.</param>
        public double Get(string key, double fallback) =>
            _by.TryGetValue(key ?? string.Empty, out var held)
            && double.TryParse(held, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        /// <summary>What was chosen, or the fallback.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="fallback">What it is when nobody has chosen.</param>
        public string Get(string key, string fallback) =>
            _by.TryGetValue(key ?? string.Empty, out var held) && held.Length > 0
                ? held
                : fallback;

        /// <summary>Remember a choice.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="value">What was chosen.</param>
        public void Set(string key, bool value) =>
            Put(key, value ? "true" : "false");

        /// <summary>Remember a choice.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="value">What was chosen.</param>
        public void Set(string key, double value) =>
            Put(key, value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>Remember a choice.</summary>
        /// <param name="key">Which choice.</param>
        /// <param name="value">What was chosen.</param>
        public void Set(string key, string? value) => Put(key, value ?? string.Empty);

        private void Put(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            _by[key] = value;
        }

        /// <summary>Read what this person has chosen before, or defaults.</summary>
        public static UserChoices Read()
        {
            var choices = new UserChoices();
            var path = Path;

            if (path == null) return choices;

            try
            {
                if (!File.Exists(path)) return choices;

                var json = JsonReader.Parse(File.ReadAllText(path));

                if (json == null || json.Kind != JsonKind.Object) return choices;

                foreach (var key in json.Keys)
                {
                    var held = json[key];

                    choices._by[key] =
                        held.Kind == JsonKind.String ? held.AsString() ?? string.Empty
                        : held.Kind == JsonKind.Bool ? (held.AsBool() ? "true" : "false")
                        : held.Kind == JsonKind.Number
                            ? held.AsDouble().ToString("R", CultureInfo.InvariantCulture)
                            : string.Empty;
                }
            }
            catch (Exception)
            {
                // An unreadable settings file is a set of defaults, which is what this already is.
            }

            return choices;
        }

        /// <summary>Write them back.</summary>
        /// <returns>True when they reached the disk.</returns>
        public bool Save()
        {
            var path = Path;

            if (path == null) return false;

            try
            {
                var folder = System.IO.Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder!);

                var json = JsonValue.Object();

                foreach (var pair in _by.OrderBy(p => p.Key, StringComparer.Ordinal))
                    json.Set(pair.Key, JsonValue.String(pair.Value));

                File.WriteAllText(path, json.ToJson(true));
                return true;
            }
            catch (Exception)
            {
                // A preference that could not be written lasts until the session ends, which is
                // not worth interrupting anybody over.
                return false;
            }
        }
    }
}
