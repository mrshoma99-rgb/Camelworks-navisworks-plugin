using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// The fields every line of a matrix carries, and how they are filled in.
    ///
    /// One scheme for both axes. A square matrix is one discipline list mirrored, so a field added
    /// for the rows is a field the columns want too, and keeping two lists would mean writing
    /// every change twice and reading a board where the two disagreed.
    ///
    /// The scheme is what makes a forty-line matrix editable. A line's values are not typed one at
    /// a time unless somebody asks for that: a field says how it is filled — counted down the
    /// axis, the same on every line, or by hand — and <see cref="Apply"/> writes it across the
    /// board. Changing where the numbering starts is then one edit rather than forty.
    ///
    /// A line can be taken out of the scheme for any one field. That is what
    /// <see cref="MatrixLine.Pinned"/> holds, and it is per field rather than per line because a
    /// line whose number was set by hand still wants its code counted with the rest. It keeps its
    /// place in the count, so taking one line out leaves every other line's value where it was.
    /// </summary>
    public sealed class MatrixScheme
    {
        private readonly List<MatrixField> _fields = new List<MatrixField>();

        /// <summary>Start from the four the board has always carried.</summary>
        public MatrixScheme()
        {
            _fields.Add(new MatrixField(MatrixField.NameKey, "Name"));
            _fields.Add(new MatrixField(MatrixField.NumberKey, "Number"));
            _fields.Add(new MatrixField(MatrixField.CodeKey, "Code"));
            _fields.Add(new MatrixField(MatrixField.FolderKey, "Folder"));
        }

        /// <summary>The fields, in the order they are offered and drawn.</summary>
        public IReadOnlyList<MatrixField> Fields => _fields;

        /// <summary>The field with this key, or null.</summary>
        /// <param name="key">The key.</param>
        public MatrixField? Field(string? key) =>
            _fields.FirstOrDefault(one => string.Equals(one.Key, (key ?? string.Empty).Trim(),
                                                        StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether a field with this key is already here.</summary>
        /// <param name="key">The key.</param>
        public bool Has(string? key) => Field(key) != null;

        /// <summary>
        /// Add a field, or return the one already under that key.
        ///
        /// Adding a key twice returns the existing field rather than a second one. Two fields
        /// under one key would produce one token that read from either, so a pattern would extract
        /// a value that depended on which was reached first.
        /// </summary>
        /// <param name="key">The key, which is what the token is built from.</param>
        /// <param name="title">What it is called on screen.</param>
        public MatrixField Add(string key, string? title = null)
        {
            var found = Field(key);

            if (found != null) return found;

            var made = new MatrixField(key, title);

            _fields.Add(made);

            return made;
        }

        /// <summary>
        /// Take a field away, and with it every value lines held under it.
        ///
        /// The four built-in fields refuse: the headers, the diff and the export all name them, so
        /// a board without a number field is a board half the product cannot draw.
        /// </summary>
        /// <param name="key">The field to remove.</param>
        /// <param name="lines">Every line, so the values go with it.</param>
        /// <returns>True when it was removed.</returns>
        public bool Remove(string? key, IEnumerable<MatrixLine>? lines = null)
        {
            var found = Field(key);

            if (found == null || found.IsBuiltIn) return false;

            _fields.Remove(found);

            foreach (var line in lines ?? Array.Empty<MatrixLine>())
            {
                line.Values.Remove(found.Key);
                line.Pinned.Remove(found.Key);
            }

            return true;
        }

        /// <summary>Move a field to a new position, from zero.</summary>
        /// <param name="key">The field.</param>
        /// <param name="to">Where it should sit.</param>
        public bool Move(string? key, int to)
        {
            var found = Field(key);

            if (found == null) return false;

            var at = _fields.IndexOf(found);
            var wanted = Math.Max(0, Math.Min(_fields.Count - 1, to));

            if (at == wanted) return false;

            _fields.RemoveAt(at);
            _fields.Insert(wanted, found);

            return true;
        }

        /// <summary>
        /// Write every filled field across one axis.
        ///
        /// Walked in the order the lines are drawn, because a series is about position: the value
        /// a line gets is decided by how many lines came before it, which is what makes inserting
        /// a discipline renumber the ones under it rather than leaving a gap.
        ///
        /// A line that has pinned a field keeps what it holds and still takes its place in the
        /// count. Skipping it would mean that pinning one row renumbered every row under it —
        /// thirty-seven edits to make one — which is the churn this whole mechanism exists to
        /// remove. Holding the place costs one value out of the run and leaves every other line
        /// exactly where it was.
        /// </summary>
        /// <param name="lines">One axis, in the order it is drawn.</param>
        /// <returns>How many values were written.</returns>
        public int Apply(IReadOnlyList<MatrixLine>? lines)
        {
            if (lines == null || lines.Count == 0) return 0;

            var wrote = 0;

            foreach (var field in _fields.Where(one => one.Fill != MatrixFill.ByHand))
            {
                // One counter per folder when the field restarts, and one for the whole axis
                // otherwise, so both settle out of the same walk.
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines)
                {
                    if (line == null) continue;

                    var bucket = field.PerFolder ? line.Folder ?? string.Empty : string.Empty;

                    counts.TryGetValue(bucket, out var at);
                    counts[bucket] = at + 1;

                    // Counted, then left alone: the place is taken either way, so the lines under
                    // a pinned one do not move.
                    if (line.Pinned.Contains(field.Key)) continue;

                    var said = field.At(at);

                    if (string.Equals(line.Value(field.Key), said, StringComparison.Ordinal))
                        continue;

                    line.Set(field.Key, said);
                    wrote++;
                }
            }

            return wrote;
        }

        /// <summary>A copy of this scheme.</summary>
        public MatrixScheme Copy()
        {
            var made = new MatrixScheme();

            made._fields.Clear();

            foreach (var field in _fields) made._fields.Add(field.Copy());

            return made;
        }

        /// <summary>Write it down, or null when it is the scheme every board starts with.</summary>
        public JsonValue? ToJson()
        {
            // A board nobody has added a field to writes nothing, so an untouched matrix saves
            // exactly the bytes it did before this existed.
            if (_fields.All(one => one.IsBuiltIn && one.Fill == MatrixFill.ByHand))
                return null;

            return JsonValue.Array(_fields.Select(one => one.ToJson()));
        }

        /// <summary>Read one back, falling to the starting scheme when there is nothing to read.</summary>
        /// <param name="json">What was written.</param>
        public static MatrixScheme FromJson(JsonValue? json)
        {
            var made = new MatrixScheme();

            if (json == null || json.Items == null || json.Items.Count == 0) return made;

            made._fields.Clear();

            foreach (var held in json.Items)
            {
                var field = MatrixField.FromJson(held);

                if (field != null && !made.Has(field.Key)) made._fields.Add(field);
            }

            // A file that lost a built-in field still has to open, so the four are put back where
            // they belong rather than leaving a board the headers cannot draw.
            foreach (var key in MatrixField.BuiltIn.Reverse())
                if (!made.Has(key))
                    made._fields.Insert(0, new MatrixField(key));

            return made;
        }
    }
}
