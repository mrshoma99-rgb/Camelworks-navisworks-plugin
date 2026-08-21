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
    }
}
