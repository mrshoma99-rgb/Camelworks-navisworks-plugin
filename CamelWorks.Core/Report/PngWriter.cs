using System;
using System.IO;
using System.IO.Compression;

namespace CamelWorks.Core.Report
{
    /// <summary>
    /// An 8-bit RGBA raster, as PNG bytes.
    ///
    /// Beside <see cref="PngImage"/>, which reads one. Written here rather than handing a raster
    /// out as width, height and pixels because every consumer of a picture in this product already
    /// takes PNG: the BCF pack writes PNG snapshots, the PDF report passes PNG data straight into
    /// a FlateDecode stream, and the HTML report embeds it. A raster handed out raw would make
    /// each of them grow an encoder.
    ///
    /// One IDAT, no interlace, truecolour with alpha, filter None on every row. No System.Drawing:
    /// Core is netstandard2.0 and a test asserts it references only the framework, so the whole
    /// path is testable on a machine with no Windows on it.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Encode a raster.</summary>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="rgba">
        /// The pixels: width × height × 4 bytes, red, green, blue and alpha per pixel, top row
        /// first.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
        /// <exception cref="ArgumentException">The buffer is not the size the dimensions need.</exception>
        public static byte[] Encode(int width, int height, byte[]? rgba)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "the width must be positive");

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "the height must be positive");

            if (rgba == null) throw new ArgumentNullException(nameof(rgba));

            var expected = checked(width * height * 4);

            if (rgba.Length != expected)
                throw new ArgumentException(
                    "the pixel buffer is " + rgba.Length + " bytes and a " + width + "×" + height
                    + " RGBA image needs " + expected, nameof(rgba));

            using (var output = new MemoryStream())
            {
                output.Write(Signature, 0, Signature.Length);

                var header = new byte[13];

                BigEndian(header, 0, (uint)width);
                BigEndian(header, 4, (uint)height);
                header[8] = 8;   // bit depth
                header[9] = 6;   // colour type: truecolour with alpha
                header[10] = 0;  // compression
                header[11] = 0;  // filter
                header[12] = 0;  // interlace

                Chunk(output, "IHDR", header);
                Chunk(output, "IDAT", Scanlines(width, height, rgba));
                Chunk(output, "IEND", Array.Empty<byte>());

                return output.ToArray();
            }
        }

        /// <summary>The rows, each behind a filter byte, deflated inside a zlib wrapper.</summary>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="rgba">The pixels.</param>
        private static byte[] Scanlines(int width, int height, byte[] rgba)
        {
            var stride = width * 4;
            var raw = new byte[height * (stride + 1)];

            for (var y = 0; y < height; y++)
            {
                var at = y * (stride + 1);

                raw[at] = 0; // filter: None

                Array.Copy(rgba, y * stride, raw, at + 1, stride);
            }

            using (var compressed = new MemoryStream())
            {
                // The zlib header DEFLATE is wrapped in: 32K window, no dictionary.
                compressed.WriteByte(0x78);
                compressed.WriteByte(0x01);

                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);

                var adler = Adler32(raw);

                compressed.WriteByte((byte)(adler >> 24));
                compressed.WriteByte((byte)(adler >> 16));
                compressed.WriteByte((byte)(adler >> 8));
                compressed.WriteByte((byte)adler);

                return compressed.ToArray();
            }
        }

        /// <summary>One chunk: length, type, data and the CRC of the last two.</summary>
        /// <param name="stream">Where it goes.</param>
        /// <param name="type">The four-letter chunk type.</param>
        /// <param name="data">Its payload.</param>
        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];

            BigEndian(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);

            var name = new byte[4];

            for (var i = 0; i < 4; i++) name[i] = (byte)type[i];

            stream.Write(name, 0, 4);
            stream.Write(data, 0, data.Length);

            var crc = new byte[4];

            BigEndian(crc, 0, Crc32(name, data));
            stream.Write(crc, 0, 4);
        }

        /// <summary>Write a four-byte value, most significant byte first.</summary>
        /// <param name="buffer">Where it goes.</param>
        /// <param name="offset">Where in the buffer.</param>
        /// <param name="value">The value.</param>
        private static void BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        /// <summary>The Adler-32 checksum zlib ends its stream with.</summary>
        /// <param name="data">The uncompressed bytes.</param>
        private static uint Adler32(byte[] data)
        {
            const uint mod = 65521;
            uint a = 1, b = 0;

            foreach (var value in data)
            {
                a = (a + value) % mod;
                b = (b + a) % mod;
            }

            return (b << 16) | a;
        }

        /// <summary>The CRC-32 table PNG chunks are checked with.</summary>
        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                var c = n;

                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

                table[n] = c;
            }

            return table;
        }

        /// <summary>One chunk's CRC, over its type and its data.</summary>
        /// <param name="type">The chunk type bytes.</param>
        /// <param name="data">The chunk payload.</param>
        private static uint Crc32(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFFu;

            foreach (var value in type) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

            foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
