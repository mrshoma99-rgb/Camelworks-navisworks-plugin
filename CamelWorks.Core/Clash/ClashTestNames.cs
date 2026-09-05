using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// What this project calls a clash test, when the document will not record it.
    ///
    /// Renaming a test and filing it in a folder are writes into the host's clash data, and a
    /// published NWD holds that data read-only, so on the seat where most coordination review happens
    /// both verbs refused. The rule this product works to is that the database is the record and the
    /// document is a projection of it: a decision is kept whether or not the file will take it.
    /// Statuses, assignments, comments and groups already worked that way through the findings
    /// register; a test's own name and folder had nowhere to go and were lost.
    ///
    /// They go here. The board applies these over whatever the host reports, the sync writes them
    /// into the document on a seat that can take them, and a document that never can still shows the
    /// coordinator the names they chose.
    ///
    /// Keyed by the host's test id, which survives a rename, so renaming a test twice replaces the
    /// entry rather than orphaning it, and a test deleted in Navisworks leaves an entry that never
    /// matches again.
    /// </summary>
    public sealed class ClashTestNames
    {
        /// <summary>Where this lives inside the clash section of the project file.</summary>
        public const string Key = "testnames";

        private readonly Dictionary<string, TestAlias> _by =
            new Dictionary<string, TestAlias>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What this project calls one test.</summary>
        public sealed class TestAlias
        {
            /// <summary>The name, or null when only the folder was changed.</summary>
            public string? Name { get; set; }

            /// <summary>The folder, or null when only the name was changed.</summary>
            public string? Folder { get; set; }

            /// <summary>Whether the folder was deliberately cleared rather than never set.</summary>
            public bool FolderCleared { get; set; }

            /// <summary>True when it says nothing at all.</summary>
            public bool IsEmpty =>
                string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Folder)
                && !FolderCleared;
        }

        /// <summary>How many tests this project has renamed or refiled.</summary>
        public int Count => _by.Count;

        /// <summary>Every test id this holds something for.</summary>
        public IReadOnlyCollection<string> Ids => _by.Keys.ToList();

        /// <summary>What this project calls a test, or null when it has nothing to say.</summary>
        /// <param name="testId">The host's test id.</param>
        public TestAlias? For(string? testId) =>
            string.IsNullOrWhiteSpace(testId) ? null
            : _by.TryGetValue(testId!, out var found) ? found
            : null;

        /// <summary>Call a test something.</summary>
        /// <param name="testId">The host's test id.</param>
        /// <param name="name">The name. Blank or null leaves the name alone.</param>
        public void Rename(string? testId, string? name)
        {
            if (string.IsNullOrWhiteSpace(testId) || string.IsNullOrWhiteSpace(name)) return;

            Held(testId!).Name = name!.Trim();
        }

        /// <summary>
        /// File a test in a folder, or take it out of one.
        /// </summary>
        /// <param name="testId">The host's test id.</param>
        /// <param name="folder">The folder, or null or blank to take it to the root.</param>
        public void Refile(string? testId, string? folder)
        {
            if (string.IsNullOrWhiteSpace(testId)) return;

            var held = Held(testId!);
            var wanted = (folder ?? string.Empty).Trim();

            // Cleared and never-set are different answers: one overrides the folder the document
            // reports, the other defers to it.
            held.Folder = wanted.Length == 0 ? null : wanted;
            held.FolderCleared = wanted.Length == 0;
        }

        /// <summary>Forget what this project called a test, because the document took it.</summary>
        /// <param name="testId">The host's test id.</param>
        /// <param name="name">True to forget the name.</param>
        /// <param name="folder">True to forget the folder.</param>
        public void Landed(string? testId, bool name = false, bool folder = false)
        {
            if (string.IsNullOrWhiteSpace(testId) || !_by.TryGetValue(testId!, out var held)) return;

            if (name) held.Name = null;

            if (folder)
            {
                held.Folder = null;
                held.FolderCleared = false;
            }

            if (held.IsEmpty) _by.Remove(testId!);
        }

        private TestAlias Held(string testId)
        {
            if (_by.TryGetValue(testId, out var found)) return found;

            var made = new TestAlias();
            _by[testId] = made;
            return made;
        }

        /// <summary>Read it back out of the project file.</summary>
        /// <param name="json">The stored value, or null.</param>
        public static ClashTestNames FromJson(JsonValue? json)
        {
            var names = new ClashTestNames();

            if (json == null || json.Kind != JsonKind.Object) return names;

            foreach (var testId in json.Keys)
            {
                var record = json[testId];
                if (record.Kind != JsonKind.Object) continue;

                var entry = new TestAlias
                {
                    Name = record["name"].AsString(),
                    Folder = record["folder"].AsString(),
                    FolderCleared = record["rooted"].AsBool(),
                };

                if (!entry.IsEmpty) names._by[testId] = entry;
            }

            return names;
        }

        /// <summary>Write it into the project file.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            foreach (var pair in _by.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value.IsEmpty) continue;

                var record = JsonValue.Object();

                if (!string.IsNullOrWhiteSpace(pair.Value.Name))
                    record.Set("name", JsonValue.String(pair.Value.Name!));

                if (!string.IsNullOrWhiteSpace(pair.Value.Folder))
                    record.Set("folder", JsonValue.String(pair.Value.Folder!));

                if (pair.Value.FolderCleared) record.Set("rooted", JsonValue.Bool(true));

                json.Set(pair.Key, record);
            }

            return json;
        }
    }
}
