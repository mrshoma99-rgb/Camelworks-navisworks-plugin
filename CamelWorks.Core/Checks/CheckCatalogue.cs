using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Every check the session can offer, built once when the session opens.
    ///
    /// The registry is what keeps adding a check to one class and one <see cref="Add"/> call: no
    /// enum member, no switch, no board edit and no schema version bump. An empty catalogue is the
    /// ordinary state of a session on a seat where nothing is available, and the picker says so
    /// rather than the tab refusing to open.
    /// </summary>
    public sealed class CheckCatalogue
    {
        private readonly List<ICheck> _checks = new List<ICheck>();

        /// <summary>Every registered check, in the order it was added.</summary>
        public IReadOnlyList<ICheck> All => _checks;

        /// <summary>The checks this seat can actually offer.</summary>
        public IReadOnlyList<ICheck> Available => _checks.Where(check => check.IsAvailable).ToList();

        /// <summary>How many are registered, available or not.</summary>
        public int Count => _checks.Count;

        /// <summary>
        /// Register a check.
        ///
        /// Two checks under one key are refused rather than silently resolved. The key is the
        /// finding source, so a duplicate would leave two producers writing findings nothing can
        /// tell apart, and the failure would surface as a board that scopes the wrong rows.
        /// </summary>
        /// <param name="check">The check.</param>
        public void Add(ICheck check)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));

            if (string.IsNullOrWhiteSpace(check.Key))
                throw new ArgumentException("a check must have a key", nameof(check));

            if (Find(check.Key) != null)
                throw new ArgumentException("a check is already registered under the key "
                                            + check.Key, nameof(check));

            _checks.Add(check);
        }

        /// <summary>The check with this key, or null when none is registered under it.</summary>
        /// <param name="key">The key, as it was written to the project file.</param>
        public ICheck? Find(string? key) =>
            key == null ? null
            : _checks.FirstOrDefault(check =>
                  string.Equals(check.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public override string ToString() =>
            Count.ToString(CultureInfo.InvariantCulture) + " check(s), "
            + Available.Count.ToString(CultureInfo.InvariantCulture) + " available";
    }
}
