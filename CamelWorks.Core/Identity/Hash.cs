using System;
using System.Security.Cryptography;
using System.Text;

namespace CamelWorks.Core.Identity
{
    /// <summary>
    /// The one hashing primitive identity uses: SHA-256 truncated to a fixed hex width.
    ///
    /// Why not <c>string.GetHashCode</c> or <c>HashCode.Combine</c>: both are unstable across
    /// process runs and across .NET versions BY DESIGN. Every value produced here is written into
    /// a sidecar file and later compared against a value produced by a different build, on a
    /// different machine, months later. It has to be a real digest.
    /// </summary>
    public static class Hash
    {
        /// <summary>Hex width of a component hash. 16 hex chars = 64 bits.</summary>
        public const int ComponentWidth = 16;

        /// <summary>
        /// Hex width of a model-scope hash. 8 hex chars = 32 bits — it only has to separate the
        /// handful of models loaded into one document, never the world.
        /// </summary>
        public const int ScopeWidth = 8;

        // Unit separator, and a distinct marker for "this part was null". Both are control
        // characters precisely because they cannot occur in a Navisworks path or property name.
        private const char Sep = '\u001F';
        private const char NullMark = '\u0000';

        /// <summary>
        /// Hash the given parts to <paramref name="hexWidth"/> lowercase hex characters.
        /// Parts are escaped and separated, so ("a","bc") and ("ab","c") cannot collide, and a
        /// null part is distinct from an empty one.
        /// </summary>
        public static string Of(int hexWidth, params string?[] parts)
        {
            if (hexWidth <= 0 || hexWidth > 64)
                throw new ArgumentOutOfRangeException(nameof(hexWidth), "hexWidth must be 1..64");
            if (parts == null) throw new ArgumentNullException(nameof(parts));

            var sb = Builder();

            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(Sep);
                Escape(sb, parts[i]);
            }

            var digest = Digest(sb);
            var hex = new char[hexWidth];

            for (var i = 0; i < hexWidth; i++)
            {
                var nibble = (i & 1) == 0 ? digest[i / 2] >> 4 : digest[i / 2] & 0xF;

                hex[i] = (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
            }

            return new string(hex);
        }

        // One hasher and one set of buffers per thread. SHA256.Create() per call meant three
        // disposable digest objects for every element key made, and the host document index makes one
        // per geometry leaf: 1.5 million of them on a 500k-leaf federation before any clash work.
        // Per-thread rather than shared because a hasher holds state between the calls that make
        // one digest.
        [ThreadStatic] private static SHA256? _sha;
        [ThreadStatic] private static StringBuilder? _text;
        [ThreadStatic] private static char[]? _chars;
        [ThreadStatic] private static byte[]? _bytes;

        /// <summary>How large a buffer is worth holding on to between calls.</summary>
        private const int MostKept = 4096;

        private static StringBuilder Builder()
        {
            var sb = _text;

            if (sb == null) return _text = new StringBuilder(64);

            // A tree path long enough to grow the builder past the cap is one key out of half a
            // million, and holding its capacity for the life of the thread is a megabyte kept for
            // nothing.
            if (sb.Capacity > MostKept) return _text = new StringBuilder(64);

            sb.Length = 0;

            return sb;
        }

        /// <summary>
        /// The digest of what the builder holds, encoded as UTF-8 without an intermediate string.
        /// </summary>
        /// <param name="sb">The escaped, separated parts.</param>
        private static byte[] Digest(StringBuilder sb)
        {
            var length = sb.Length;

            var chars = _chars != null && _chars.Length >= length
                ? _chars
                : new char[Math.Max(64, length)];

            if (chars.Length <= MostKept) _chars = chars;

            sb.CopyTo(0, chars, 0, length);

            var needed = Encoding.UTF8.GetByteCount(chars, 0, length);

            var bytes = _bytes != null && _bytes.Length >= needed
                ? _bytes
                : new byte[Math.Max(128, needed)];

            if (bytes.Length <= MostKept) _bytes = bytes;

            var written = Encoding.UTF8.GetBytes(chars, 0, length, bytes, 0);

            return (_sha ??= SHA256.Create()).ComputeHash(bytes, 0, written);
        }

        private static void Escape(StringBuilder sb, string? part)
        {
            if (part == null) { sb.Append(NullMark); return; }
            foreach (var c in part)
            {
                if (c == Sep || c == NullMark || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
        }

        /// <summary>
        /// Quantise a coordinate onto a grid as a stable integer. Used wherever a position takes
        /// part in an identity — never a raw double, whose last bits differ between runs.
        /// </summary>
        public static long Quantise(double value, double grid)
        {
            if (grid <= 0 || double.IsNaN(grid) || double.IsInfinity(grid))
                throw new ArgumentOutOfRangeException(nameof(grid), "grid must be finite and positive");
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "coordinate must be finite");

            // AwayFromZero rather than banker's rounding: a value sitting exactly on a grid
            // boundary must land the same way every time, and symmetrically about zero.
            var rounded = Math.Round(value / grid, MidpointRounding.AwayFromZero);

            if (rounded > long.MaxValue || rounded < long.MinValue)
                throw new ArgumentOutOfRangeException(nameof(value), "coordinate is out of range for this grid");

            return (long)rounded;
        }
    }
}
