using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Testing
{
    /// <summary>
    /// An in-memory <see cref="IFileSystem"/> that can be told to fail at a chosen moment.
    ///
    /// The failure injection is the point. "The process died between writing the temp file and
    /// replacing the target" cannot be provoked against a real disk on demand, and it is the exact
    /// scenario atomic write exists for — so it has to be reachable in a test or the guarantee is
    /// only ever asserted in prose.
    /// </summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _log = new List<string>();

        /// <summary>Ordered record of every operation, for asserting how a write was performed.</summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary>When set, the next call to that operation throws. Cleared once it fires.</summary>
        public string? FailOnce { get; set; }

        /// <summary>Files currently present, as full paths.</summary>
        public IReadOnlyList<string> AllPaths => _files.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Seed a file.</summary>
        public InMemoryFileSystem With(string path, string text)
        {
            _files[path] = text;
            return this;
        }

        private void Trip(string operation)
        {
            _log.Add(operation);
            if (FailOnce == null || !operation.StartsWith(FailOnce, StringComparison.Ordinal)) return;

            FailOnce = null;
            throw new SimulatedIoException("simulated failure during: " + operation);
        }

        /// <inheritdoc />
        public bool Exists(string path) => _files.ContainsKey(path);

        /// <inheritdoc />
        public string? ReadAllText(string path)
        {
            Trip("read " + path);
            return _files.TryGetValue(path, out var text) ? text : null;
        }

        /// <inheritdoc />
        public void WriteAllText(string path, string text)
        {
            Trip("write " + path);
            _files[path] = text;
        }

        /// <inheritdoc />
        public void Replace(string source, string destination, string? backup)
        {
            Trip("replace " + destination);

            if (!_files.TryGetValue(source, out var incoming))
                throw new SimulatedIoException("source missing: " + source);

            if (_files.TryGetValue(destination, out var previous) && backup != null)
                _files[backup] = previous;

            _files[destination] = incoming;
            _files.Remove(source);
        }

        /// <inheritdoc />
        public void Delete(string path)
        {
            Trip("delete " + path);
            _files.Remove(path);
        }

        /// <inheritdoc />
        public void CreateDirectory(string directory)
        {
            Trip("mkdir " + directory);
            _directories.Add(directory);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListFiles(string directory)
        {
            Trip("list " + directory);

            // Both sides normalised, not just the keys. Normalising only the keys made this list
            // nothing at all for a directory given with backslashes — which is every directory
            // this product derives on Windows — so a store that saved a file correctly could not
            // then find it, and the failure looked like the store rather than the fake.
            var prefix = directory.TrimEnd('/', '\\').Replace('\\', '/') + "/";

            return _files.Keys
                .Where(k => k.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Replace('\\', '/').Substring(prefix.Length))
                .Where(n => !n.Contains("/"))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <inheritdoc />
        public bool DirectoryExists(string directory)
        {
            var prefix = directory.TrimEnd('/', '\\').Replace('\\', '/');

            // Made explicitly, or implied by something written into it - which is how the real one
            // behaves, and a fake that insisted on CreateDirectory first would pass tests the disk
            // would fail.
            return _directories.Any(d => string.Equals(d.TrimEnd('/', '\\').Replace('\\', '/'),
                                                       prefix, StringComparison.OrdinalIgnoreCase))
                || _files.Keys.Any(k => k.Replace('\\', '/')
                                         .StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Thrown by <see cref="InMemoryFileSystem"/> when a failure was injected.
    ///
    /// An <see cref="System.IO.IOException"/>, because that is what the stores catch. A fake
    /// throwing anything else cannot exercise the path a real disk failure takes: the injected
    /// failure would escape the store's own handling and the test would be asserting about a
    /// call stack the product never has.
    /// </summary>
    public sealed class SimulatedIoException : System.IO.IOException
    {
        /// <summary>Create the exception.</summary>
        public SimulatedIoException(string message) : base(message) { }
    }
}
