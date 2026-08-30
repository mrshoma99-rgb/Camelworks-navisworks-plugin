using System;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// The 22-character compressed GUID an IFC file identifies elements by.
    ///
    /// BCF names elements by their IFC GlobalId, and a receiving tool looks the string up verbatim.
    /// So a value that merely came out of a property called "GUID" must be checked before it is
    /// written as one: hand a viewer a Revit element id in that field and it finds nothing, reports
    /// nothing, and the topic silently points at an empty selection. Refusing is better, because
    /// the writer counts unresolvable components and the export says how many.
    /// </summary>
    public static class IfcGuid
    {
        /// <summary>How long a compressed IFC GUID is.</summary>
        public const int Length = 22;

        // IFC's base-64 alphabet, which is its own: digits, letters, then _ and $.
        private const string Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>True when the text is a compressed IFC GUID.</summary>
        /// <param name="text">The candidate.</param>
        public static bool IsCompressed(string? text)
        {
            if (text == null || text.Length != Length) return false;

            // The first character encodes only two bits of the 128, so three quarters of the
            // alphabet cannot legally appear there. Checking it rejects most look-alikes for free.
            if (Alphabet.IndexOf(text[0]) > 3) return false;

            foreach (var c in text)
                if (Alphabet.IndexOf(c) < 0)
                    return false;

            return true;
        }

        /// <summary>The text when it is a compressed IFC GUID, or null.</summary>
        /// <param name="text">The candidate.</param>
        public static string? OrNull(string? text) => IsCompressed(text) ? text : null;

        /// <summary>
        /// A compressed IFC GUID from whatever the model calls it, or null when it is neither.
        ///
        /// Two shapes reach here. An IFC-derived element carries its GlobalId already compressed,
        /// and that is passed through untouched. Anything else is accepted only if it really is a
        /// 128-bit GUID - braced, hyphenated or bare - which is then compressed the way IFC does
        /// it. A Revit element id, a sequence number, a name: none of those parse, and none of
        /// them are written, because a receiving tool looks this string up verbatim and a wrong
        /// one points the topic at an empty selection.
        /// </summary>
        /// <param name="text">Whatever the property held.</param>
        public static string? From(string? text)
        {
            var value = (text ?? string.Empty).Trim();

            if (value.Length == 0) return null;

            if (IsCompressed(value)) return value;

            return Guid.TryParse(value, out var guid) ? Compress(guid) : null;
        }

        /// <summary>
        /// A GUID in IFC's own base-64.
        ///
        /// The 16 bytes are taken in the order the canonical text writes them, which is the order
        /// IFC compresses - not <c>Guid.ToByteArray</c>, which puts the first three fields in
        /// little-endian and would produce a legal-looking string naming a different element.
        ///
        /// One byte becomes two characters and each of the remaining five groups of three becomes
        /// four, which is where the 22 comes from.
        /// </summary>
        /// <param name="guid">The GUID.</param>
        public static string Compress(Guid guid)
        {
            var text = guid.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var bytes = new byte[16];

            for (var i = 0; i < 16; i++)
                bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);

            var sb = new System.Text.StringBuilder(Length);

            Chunk(sb, bytes, 0, 1, 2);

            for (var at = 1; at < 16; at += 3) Chunk(sb, bytes, at, 3, 4);

            return sb.ToString();
        }

        /// <summary>Some bytes as a big-endian number, in a fixed count of base-64 digits.</summary>
        private static void Chunk(System.Text.StringBuilder sb, byte[] bytes, int at, int count,
                                  int digits)
        {
            var value = 0u;

            for (var i = 0; i < count; i++) value = (value << 8) | bytes[at + i];

            var written = new char[digits];

            for (var i = digits - 1; i >= 0; i--)
            {
                written[i] = Alphabet[(int)(value % 64)];
                value /= 64;
            }

            sb.Append(written);
        }
    }
}
