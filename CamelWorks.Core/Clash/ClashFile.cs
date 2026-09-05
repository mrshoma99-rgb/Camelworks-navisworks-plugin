using System;
using System.IO;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Reads a clash file, whichever of the two it is.
    ///
    /// BCF is what this product writes now, because it is what the rest of the industry reads. But
    /// every baseline and markup written before that change is a Navisworks clash XML in somebody's
    /// project folder, and upgrading should not make last month's review round unreadable. So the
    /// reader takes either and the writer only makes the new one.
    ///
    /// Told apart by content rather than by name. A .bcfzip is a zip and begins <c>PK</c>; clash XML
    /// is text. Extensions get renamed, mailed and re-saved by other tools, and a reader that trusts
    /// the extension refuses good files.
    /// </summary>
    public static class ClashFile
    {
        /// <summary>What a clash file written by this product is called.</summary>
        public const string Extension = ".bcfzip";

        /// <summary>The filter for a file dialog, new format first.</summary>
        public const string Filter =
            "Clash file (BCF)|*.bcfzip;*.bcf|Clash XML (older)|*.xml|All files|*.*";

        /// <summary>Read a clash file.</summary>
        /// <param name="bytes">The file.</param>
        public static ClashXmlRead Read(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return ClashXml.Read(null);

            if (IsArchive(bytes))
            {
                using (var stream = new MemoryStream(bytes, writable: false))
                {
                    return ClashBcf.Read(stream);
                }
            }

            return ClashXml.Read(Text(bytes));
        }

        /// <summary>Whether these bytes are a zip - which is to say, a BCF.</summary>
        /// <param name="bytes">The start of a file.</param>
        public static bool IsArchive(byte[]? bytes) =>
            bytes != null && bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B;

        /// <summary>The bytes as text, honouring a byte-order mark when there is one.</summary>
        private static string Text(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
                                                 detectEncodingFromByteOrderMarks: true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
