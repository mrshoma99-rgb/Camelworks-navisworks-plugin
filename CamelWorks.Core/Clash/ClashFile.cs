using System.IO;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Reads a clash file.
    ///
    /// BCF is the only clash file this product writes or reads. It is what the rest of the
    /// industry reads, and one format means one round trip: the pack that goes out to a
    /// stakeholder is the pack that comes back, through the same reader.
    ///
    /// Told apart by content rather than by name. A .bcfzip is a zip and begins <c>PK</c>.
    /// Extensions get renamed, mailed and re-saved by other tools, so a reader that trusts the
    /// extension refuses good files.
    /// </summary>
    public static class ClashFile
    {
        /// <summary>What a clash file written by this product is called.</summary>
        public const string Extension = ".bcfzip";

        /// <summary>The filter for a file dialog.</summary>
        public const string Filter = "Clash file (BCF)|*.bcfzip;*.bcf|All files|*.*";

        /// <summary>Read a clash file.</summary>
        /// <param name="bytes">The file.</param>
        public static ClashPackRead Read(byte[]? bytes)
        {
            if (!IsArchive(bytes)) return ClashBcf.Read(null);

            using (var stream = new MemoryStream(bytes!, writable: false))
            {
                return ClashBcf.Read(stream);
            }
        }

        /// <summary>Whether these bytes are a zip - which is to say, a BCF.</summary>
        /// <param name="bytes">The start of a file.</param>
        public static bool IsArchive(byte[]? bytes) =>
            bytes != null && bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B;
    }
}
