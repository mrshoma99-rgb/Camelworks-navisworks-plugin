using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CamelWorks.Core.Field
{
    /// <summary>How much of a QR code can be lost before it stops reading.</summary>
    public enum QrLevel
    {
        /// <summary>About a seventh. The smallest symbol, and the least forgiving of a scuffed label.</summary>
        Low = 0,

        /// <summary>About a sixth. What most printed codes use.</summary>
        Medium,

        /// <summary>About a quarter.</summary>
        High,

        /// <summary>About a third. What survives site.</summary>
        Highest,
    }

    /// <summary>A QR code, as a square of dark and light modules.</summary>
    public sealed class QrSymbol
    {
        private readonly bool[] _dark;

        internal QrSymbol(int version, QrLevel level, int mask, int size, bool[] dark, string text)
        {
            Version = version;
            Level = level;
            Mask = mask;
            Size = size;
            _dark = dark;
            Text = text;
        }

        /// <summary>Which of the forty symbol sizes this is.</summary>
        public int Version { get; }

        /// <summary>How much of it can be lost.</summary>
        public QrLevel Level { get; }

        /// <summary>Which of the eight masks was laid over the data.</summary>
        public int Mask { get; }

        /// <summary>How many modules across it is.</summary>
        public int Size { get; }

        /// <summary>What it says.</summary>
        public string Text { get; }

        /// <summary>Whether one module is dark.</summary>
        /// <param name="row">Row, from the top.</param>
        /// <param name="column">Column, from the left.</param>
        public bool IsDark(int row, int column) =>
            row >= 0 && column >= 0 && row < Size && column < Size && _dark[row * Size + column];

        /// <summary>
        /// The symbol as SVG, ready to sit in a label.
        ///
        /// Vector rather than a bitmap because a QR code is printed and then read by a camera at an
        /// angle in bad light: a bitmap scaled to fit a label gets soft edges between modules and
        /// that is exactly what makes a code take four tries to scan.
        /// </summary>
        /// <param name="millimetres">How wide the finished symbol should be, quiet zone included.</param>
        /// <param name="quiet">How many modules of clear space round it. Four is what the standard asks for.</param>
        public string Svg(double millimetres = 25, int quiet = 4)
        {
            var margin = Math.Max(0, quiet);
            var across = Size + margin * 2;
            var width = millimetres > 0 ? millimetres : 25;

            var svg = new StringBuilder();

            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ")
               .Append("width=\"").Append(Number(width)).Append("mm\" ")
               .Append("height=\"").Append(Number(width)).Append("mm\" ")
               .Append("viewBox=\"0 0 ").Append(across.ToString(CultureInfo.InvariantCulture))
               .Append(' ').Append(across.ToString(CultureInfo.InvariantCulture)).Append("\" ")
               .Append("shape-rendering=\"crispEdges\" role=\"img\">");

            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"");

            // One path of many rectangles rather than one rectangle each: a version 20 symbol is
            // nine thousand modules, and nine thousand elements is a page a browser thinks about.
            for (var row = 0; row < Size; row++)
            {
                var run = 0;

                for (var column = 0; column <= Size; column++)
                {
                    if (column < Size && _dark[row * Size + column]) { run++; continue; }

                    if (run > 0)
                    {
                        svg.Append('M').Append((column - run + margin).ToString(CultureInfo.InvariantCulture))
                           .Append(' ').Append((row + margin).ToString(CultureInfo.InvariantCulture))
                           .Append('h').Append(run.ToString(CultureInfo.InvariantCulture))
                           .Append("v1h-").Append(run.ToString(CultureInfo.InvariantCulture)).Append('z');
                        run = 0;
                    }
                }
            }

            svg.Append("\"/></svg>");

            return svg.ToString();
        }

        /// <summary>The symbol as lines of text, which is what a test can read and a person can eyeball.</summary>
        /// <param name="dark">The character a dark module is drawn with.</param>
        /// <param name="light">The character a light module is drawn with.</param>
        public IReadOnlyList<string> Rows(char dark = '#', char light = '.')
        {
            var rows = new List<string>(Size);

            for (var row = 0; row < Size; row++)
            {
                var line = new char[Size];

                for (var column = 0; column < Size; column++)
                    line[column] = _dark[row * Size + column] ? dark : light;

                rows.Add(new string(line));
            }

            return rows;
        }

        private static string Number(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public override string ToString() =>
            "QR version " + Version.ToString(CultureInfo.InvariantCulture)
            + " " + QrCode.Word(Level) + ", " + Size.ToString(CultureInfo.InvariantCulture)
            + " modules across";
    }

    /// <summary>
    /// A QR code, made here rather than fetched from anywhere.
    ///
    /// The symbol is ISO/IEC 18004: byte mode, one segment, versions 1 to 40, all four error
    /// correction levels. Written out rather than pulled in because a plug-in that ships inside
    /// Navisworks cannot afford a dependency for one small thing, and because the thing it makes
    /// has to be right - a label that takes four tries to scan on a cold morning is worse than no
    /// label, and nobody finds out until somebody is standing in front of the pipe.
    ///
    /// <b>Byte mode only, on purpose.</b> Numeric and alphanumeric modes pack tighter, and what
    /// goes in these is a link or an identifier - mixed case, slashes, hyphens - which neither of
    /// them can hold. One mode that takes anything beats three that each take a subset and a
    /// segmenting pass to choose between them.
    ///
    /// <b>What it cannot do.</b> It writes codes; it does not read them. Reading one means a camera
    /// and a person standing in front of the work, which is a phone, not a plug-in. So the sheet
    /// this feeds carries the identifier in words underneath every symbol - somebody who cannot
    /// scan it can still type it.
    /// </summary>
    public static class QrCode
    {
        /// <summary>The largest symbol there is.</summary>
        public const int MaxVersion = 40;

        /// <summary>How many bytes the biggest symbol holds at the most forgiving level.</summary>
        public const int MaxBytes = 2953;

        // Per version, then L, M, Q, H: error-correction codewords per block, then the two groups
        // of blocks as {count, data codewords each}. The second group is {0, 0} where there is only
        // one. Straight out of the standard's tables - facts, not choices.
        private static readonly int[][,] Spec = Table();

        private static readonly byte[][] Centres = Alignment();

        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        static QrCode()
        {
            var x = 1;

            for (var i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;

                x <<= 1;

                if ((x & 0x100) != 0) x ^= 0x11D;
            }

            for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        /// <summary>Make a symbol.</summary>
        /// <param name="text">What it should say. Encoded as UTF-8.</param>
        /// <param name="level">How much of it should survive being scuffed.</param>
        /// <returns>The symbol, or null when the text is empty or too long to fit any symbol.</returns>
        public static QrSymbol? Encode(string? text, QrLevel level = QrLevel.Medium)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var bytes = Encoding.UTF8.GetBytes(text!);
            var version = Fits(bytes.Length, level);

            if (version == 0) return null;

            var words = Interleave(Codewords(bytes, version, level), version, level);
            var size = 17 + version * 4;
            var kind = new sbyte[size * size];

            Skeleton(kind, version, size);

            var spots = Spots(kind, size);
            var laid = new bool[size * size];

            for (var i = 0; i < size * size; i++) laid[i] = kind[i] == 1;

            for (var at = 0; at < spots.Count; at++)
            {
                var bit = at < words.Length * 8 && ((words[at / 8] >> (7 - at % 8)) & 1) != 0;

                laid[spots[at]] = bit;
            }

            var best = int.MaxValue;
            bool[]? chosen = null;
            var chosenMask = 0;

            for (var mask = 0; mask < 8; mask++)
            {
                var trial = (bool[])laid.Clone();

                foreach (var spot in spots)
                    if (Masked(spot / size, spot % size, mask))
                        trial[spot] = !trial[spot];

                PutFormat(trial, size, level, mask);

                if (version >= 7) PutVersion(trial, size, version);

                var score = Penalty(trial, size);

                if (score >= best) continue;

                best = score;
                chosen = trial;
                chosenMask = mask;
            }

            return new QrSymbol(version, level, chosenMask, size, chosen!, text!);
        }

        /// <summary>The smallest symbol that will hold that many bytes, or zero when none will.</summary>
        /// <param name="length">How many bytes.</param>
        /// <param name="level">How much of it should survive being scuffed.</param>
        public static int Fits(int length, QrLevel level)
        {
            if (length <= 0) return 0;

            for (var version = 1; version <= MaxVersion; version++)
                if (4 + CountBits(version) + length * 8 <= Capacity(version, level) * 8)
                    return version;

            return 0;
        }

        /// <summary>What a level is called, in words.</summary>
        /// <param name="level">The level.</param>
        public static string Word(QrLevel level) => level switch
        {
            QrLevel.Low => "low",
            QrLevel.Medium => "medium",
            QrLevel.High => "high",
            QrLevel.Highest => "highest",
            _ => level.ToString(),
        };

        /// <summary>How many bytes one symbol of that version and level holds.</summary>
        /// <param name="version">Which of the forty sizes.</param>
        /// <param name="level">How much of it should survive being scuffed.</param>
        public static int Holds(int version, QrLevel level)
        {
            if (version < 1 || version > MaxVersion) return 0;

            var room = Capacity(version, level) * 8 - 4 - CountBits(version);

            return room < 0 ? 0 : room / 8;
        }

        // ---- the message ---------------------------------------------------------------------

        private static int CountBits(int version) => version <= 9 ? 8 : 16;

        private static int Capacity(int version, QrLevel level)
        {
            var row = Spec[version - 1];
            var at = (int)level;

            return row[at, 1] * row[at, 2] + row[at, 3] * row[at, 4];
        }

        private static byte[] Codewords(byte[] data, int version, QrLevel level)
        {
            var capacity = Capacity(version, level);
            var bits = new List<bool>(capacity * 8);

            void Put(int value, int width)
            {
                for (var i = width - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
            }

            Put(0b0100, 4);
            Put(data.Length, CountBits(version));

            foreach (var b in data) Put(b, 8);

            // The terminator is up to four zeroes, and only as many of them as still fit - a symbol
            // filled to the last bit gets none, which is what the standard says and what an
            // unconditional four would quietly overflow.
            for (var i = 0; i < 4 && bits.Count < capacity * 8; i++) bits.Add(false);

            while (bits.Count % 8 != 0) bits.Add(false);

            var words = new byte[capacity];

            for (var i = 0; i < bits.Count / 8; i++)
            {
                var word = 0;

                for (var j = 0; j < 8; j++) word = (word << 1) | (bits[i * 8 + j] ? 1 : 0);

                words[i] = (byte)word;
            }

            for (int i = bits.Count / 8, turn = 0; i < capacity; i++, turn++)
                words[i] = turn % 2 == 0 ? (byte)0xEC : (byte)0x11;

            return words;
        }

        private static byte[] Generator(int degree)
        {
            // (x - a^0)(x - a^1)... multiplied out, highest order first, so the leading term is 1
            // and the division below is plain synthetic division.
            var poly = new byte[] { 1 };

            for (var i = 0; i < degree; i++)
            {
                var next = new byte[poly.Length + 1];

                for (var j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= Multiply(poly[j], Exp[i]);
                }

                poly = next;
            }

            return poly;
        }

        private static byte Multiply(byte a, byte b) =>
            a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

        private static byte[] Remainder(byte[] block, int offset, int length, int count)
        {
            var poly = Generator(count);
            var rest = new byte[length + count];

            Array.Copy(block, offset, rest, 0, length);

            for (var i = 0; i < length; i++)
            {
                var lead = rest[i];

                if (lead == 0) continue;

                for (var j = 0; j < poly.Length; j++)
                    rest[i + j] ^= Multiply(poly[j], lead);
            }

            var check = new byte[count];

            Array.Copy(rest, length, check, 0, count);

            return check;
        }

        private static byte[] Interleave(byte[] words, int version, QrLevel level)
        {
            var row = Spec[version - 1];
            var at = (int)level;
            var check = row[at, 0];

            var counts = new List<int>();

            for (var i = 0; i < row[at, 1]; i++) counts.Add(row[at, 2]);
            for (var i = 0; i < row[at, 3]; i++) counts.Add(row[at, 4]);

            var starts = new int[counts.Count];
            var running = 0;

            for (var i = 0; i < counts.Count; i++) { starts[i] = running; running += counts[i]; }

            var checks = new byte[counts.Count][];

            for (var i = 0; i < counts.Count; i++)
                checks[i] = Remainder(words, starts[i], counts[i], check);

            var longest = 0;

            foreach (var count in counts) longest = Math.Max(longest, count);

            var stream = new List<byte>(words.Length + counts.Count * check);

            for (var i = 0; i < longest; i++)
                for (var block = 0; block < counts.Count; block++)
                    if (i < counts[block]) stream.Add(words[starts[block] + i]);

            for (var i = 0; i < check; i++)
                for (var block = 0; block < counts.Count; block++) stream.Add(checks[block][i]);

            return stream.ToArray();
        }

        // ---- the matrix ----------------------------------------------------------------------

        // 0 light and fixed, 1 dark and fixed, -1 free for data.
        private static void Skeleton(sbyte[] kind, int version, int size)
        {
            for (var i = 0; i < kind.Length; i++) kind[i] = -1;

            void Finder(int top, int left)
            {
                for (var dr = -1; dr <= 7; dr++)
                    for (var dc = -1; dc <= 7; dc++)
                    {
                        int r = top + dr, c = left + dc;

                        if (r < 0 || c < 0 || r >= size || c >= size) continue;

                        var on = (dr >= 0 && dr <= 6 && (dc == 0 || dc == 6))
                                 || (dc >= 0 && dc <= 6 && (dr == 0 || dr == 6))
                                 || (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4);

                        kind[r * size + c] = (sbyte)(on ? 1 : 0);
                    }
            }

            Finder(0, 0);
            Finder(0, size - 7);
            Finder(size - 7, 0);

            for (var i = 8; i < size - 8; i++)
            {
                kind[6 * size + i] = (sbyte)(1 - i % 2);
                kind[i * size + 6] = (sbyte)(1 - i % 2);
            }

            if (version >= 2)
            {
                var centres = Centres[version - 2];

                foreach (var r in centres)
                    foreach (var c in centres)
                    {
                        // Left out only where a finder already stands. Testing "is this module
                        // taken" instead also leaves out the two patterns that sit on the timing
                        // lines, which frees forty modules the standard says are not free - and a
                        // symbol with forty spare modules is one nothing can read.
                        if ((r <= 8 && c <= 8) || (r <= 8 && c >= size - 9) || (r >= size - 9 && c <= 8))
                            continue;

                        for (var dr = -2; dr <= 2; dr++)
                            for (var dc = -2; dc <= 2; dc++)
                                kind[(r + dr) * size + c + dc] =
                                    (sbyte)(Math.Max(Math.Abs(dr), Math.Abs(dc)) == 1 ? 0 : 1);
                    }
            }

            kind[(size - 8) * size + 8] = 1;                       // the dark module

            for (var i = 0; i < 9; i++)                            // format information, kept clear
            {
                if (kind[8 * size + i] == -1) kind[8 * size + i] = 0;
                if (kind[i * size + 8] == -1) kind[i * size + 8] = 0;
            }

            for (var i = 0; i < 8; i++)
            {
                if (kind[8 * size + size - 1 - i] == -1) kind[8 * size + size - 1 - i] = 0;
                if (kind[(size - 1 - i) * size + 8] == -1) kind[(size - 1 - i) * size + 8] = 0;
            }

            if (version < 7) return;

            for (var i = 0; i < 6; i++)                            // version information, kept clear
                for (var j = 0; j < 3; j++)
                {
                    kind[(size - 11 + j) * size + i] = 0;
                    kind[i * size + size - 11 + j] = 0;
                }
        }

        /// <summary>Where the data goes, in the order it goes there.</summary>
        private static List<int> Spots(sbyte[] kind, int size)
        {
            var spots = new List<int>(size * size);
            var column = size - 1;
            var upward = true;

            while (column > 0)
            {
                if (column == 6) column--;                         // the vertical timing line

                for (var step = 0; step < size; step++)
                {
                    var row = upward ? size - 1 - step : step;

                    for (var side = 0; side < 2; side++)
                    {
                        var at = row * size + column - side;

                        if (kind[at] == -1) spots.Add(at);
                    }
                }

                upward = !upward;
                column -= 2;
            }

            return spots;
        }

        private static bool Masked(int row, int column, int mask) => mask switch
        {
            0 => (row + column) % 2 == 0,
            1 => row % 2 == 0,
            2 => column % 3 == 0,
            3 => (row + column) % 3 == 0,
            4 => (row / 2 + column / 3) % 2 == 0,
            5 => row * column % 2 + row * column % 3 == 0,
            6 => (row * column % 2 + row * column % 3) % 2 == 0,
            _ => ((row + column) % 2 + row * column % 3) % 2 == 0,
        };

        private static void PutFormat(bool[] dark, int size, QrLevel level, int mask)
        {
            var bits = Bch15((Indicator(level) << 3) | mask);

            for (var i = 0; i < 15; i++)                           // round the top-left finder
            {
                var bit = ((bits >> i) & 1) != 0;

                if (i < 6) dark[i * size + 8] = bit;
                else if (i == 6) dark[7 * size + 8] = bit;
                else if (i == 7) dark[8 * size + 8] = bit;
                else if (i == 8) dark[8 * size + 7] = bit;
                else dark[8 * size + 14 - i] = bit;
            }

            for (var i = 0; i < 15; i++)                           // split between the other two
            {
                var bit = ((bits >> i) & 1) != 0;

                if (i < 8) dark[8 * size + size - 1 - i] = bit;
                else dark[(size - 15 + i) * size + 8] = bit;
            }

            dark[(size - 8) * size + 8] = true;                    // never a format bit
        }

        private static void PutVersion(bool[] dark, int size, int version)
        {
            var bits = Bch18(version);

            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                int row = i / 3, column = i % 3;

                dark[(size - 11 + column) * size + row] = bit;
                dark[row * size + size - 11 + column] = bit;
            }
        }

        /// <summary>Two bits saying which level, in the order the standard numbers them.</summary>
        private static int Indicator(QrLevel level) => level switch
        {
            QrLevel.Low => 0b01,
            QrLevel.Medium => 0b00,
            QrLevel.High => 0b11,
            _ => 0b10,
        };

        private static int Bch15(int value)
        {
            var rest = value << 10;

            for (var i = 4; i >= 0; i--)
                if ((rest & (1 << (i + 10))) != 0) rest ^= 0b10100110111 << i;

            return ((value << 10) | rest) ^ 0b101010000010010;
        }

        private static int Bch18(int version)
        {
            var rest = version << 12;

            for (var i = 5; i >= 0; i--)
                if ((rest & (1 << (i + 12))) != 0) rest ^= 0b1111100100101 << i;

            return (version << 12) | rest;
        }

        private static int Penalty(bool[] dark, int size)
        {
            var total = 0;

            for (var line = 0; line < size * 2; line++)
            {
                var run = 0;
                var previous = false;
                var started = false;

                for (var i = 0; i < size; i++)
                {
                    var cell = line < size ? dark[line * size + i] : dark[i * size + line - size];

                    if (started && cell == previous) run++;
                    else { run = 1; previous = cell; started = true; }

                    if (run == 5) total += 3;
                    else if (run > 5) total += 1;
                }
            }

            for (var row = 0; row < size - 1; row++)
                for (var column = 0; column < size - 1; column++)
                {
                    var first = dark[row * size + column];

                    if (first == dark[row * size + column + 1]
                        && first == dark[(row + 1) * size + column]
                        && first == dark[(row + 1) * size + column + 1]) total += 3;
                }

            // The finder's own 1:1:3:1:1 with four light modules on one side of it. Anywhere else
            // in the symbol it is what makes a scanner think it has found a corner.
            var forward = new[] { true, false, true, true, true, false, true, false, false, false, false };
            var backward = new[] { false, false, false, false, true, false, true, true, true, false, true };

            for (var line = 0; line < size * 2; line++)
                for (var start = 0; start + 11 <= size; start++)
                {
                    var hitForward = true;
                    var hitBackward = true;

                    for (var i = 0; i < 11; i++)
                    {
                        var at = start + i;
                        var cell = line < size ? dark[line * size + at] : dark[at * size + line - size];

                        if (cell != forward[i]) hitForward = false;
                        if (cell != backward[i]) hitBackward = false;
                    }

                    if (hitForward) total += 40;
                    if (hitBackward) total += 40;
                }

            var count = 0;

            foreach (var cell in dark) if (cell) count++;

            var cells = size * size;

            // How far off half the symbol is dark, in whole steps of five per cent. Kept in whole
            // numbers so the step it lands on cannot move with the floating-point wind.
            total += ((Math.Abs(count * 20 - cells * 10) + cells - 1) / cells - 1) * 10;

            return total;
        }

        // ---- the standard's own tables ---------------------------------------------------------

        private static int[][,] Table()
        {
            // Per version: L, M, Q, H. Each is {error-correction codewords per block,
            // first group's block count, its data codewords each, second group's block count,
            // its data codewords each}. A second group of {0, 0} means there is only one.
            var raw = new[,,]
            {
                /*  1 */ { { 7, 1, 19, 0, 0 }, { 10, 1, 16, 0, 0 }, { 13, 1, 13, 0, 0 }, { 17, 1, 9, 0, 0 } },
                /*  2 */ { { 10, 1, 34, 0, 0 }, { 16, 1, 28, 0, 0 }, { 22, 1, 22, 0, 0 }, { 28, 1, 16, 0, 0 } },
                /*  3 */ { { 15, 1, 55, 0, 0 }, { 26, 1, 44, 0, 0 }, { 18, 2, 17, 0, 0 }, { 22, 2, 13, 0, 0 } },
                /*  4 */ { { 20, 1, 80, 0, 0 }, { 18, 2, 32, 0, 0 }, { 26, 2, 24, 0, 0 }, { 16, 4, 9, 0, 0 } },
                /*  5 */ { { 26, 1, 108, 0, 0 }, { 24, 2, 43, 0, 0 }, { 18, 2, 15, 2, 16 }, { 22, 2, 11, 2, 12 } },
                /*  6 */ { { 18, 2, 68, 0, 0 }, { 16, 4, 27, 0, 0 }, { 24, 4, 19, 0, 0 }, { 28, 4, 15, 0, 0 } },
                /*  7 */ { { 20, 2, 78, 0, 0 }, { 18, 4, 31, 0, 0 }, { 18, 2, 14, 4, 15 }, { 26, 4, 13, 1, 14 } },
                /*  8 */ { { 24, 2, 97, 0, 0 }, { 22, 2, 38, 2, 39 }, { 22, 4, 18, 2, 19 }, { 26, 4, 14, 2, 15 } },
                /*  9 */ { { 30, 2, 116, 0, 0 }, { 22, 3, 36, 2, 37 }, { 20, 4, 16, 4, 17 }, { 24, 4, 12, 4, 13 } },
                /* 10 */ { { 18, 2, 68, 2, 69 }, { 26, 4, 43, 1, 44 }, { 24, 6, 19, 2, 20 }, { 28, 6, 15, 2, 16 } },
                /* 11 */ { { 20, 4, 81, 0, 0 }, { 30, 1, 50, 4, 51 }, { 28, 4, 22, 4, 23 }, { 24, 3, 12, 8, 13 } },
                /* 12 */ { { 24, 2, 92, 2, 93 }, { 22, 6, 36, 2, 37 }, { 26, 4, 20, 6, 21 }, { 28, 7, 14, 4, 15 } },
                /* 13 */ { { 26, 4, 107, 0, 0 }, { 22, 8, 37, 1, 38 }, { 24, 8, 20, 4, 21 }, { 22, 12, 11, 4, 12 } },
                /* 14 */ { { 30, 3, 115, 1, 116 }, { 24, 4, 40, 5, 41 }, { 20, 11, 16, 5, 17 }, { 24, 11, 12, 5, 13 } },
                /* 15 */ { { 22, 5, 87, 1, 88 }, { 24, 5, 41, 5, 42 }, { 30, 5, 24, 7, 25 }, { 24, 11, 12, 7, 13 } },
                /* 16 */ { { 24, 5, 98, 1, 99 }, { 28, 7, 45, 3, 46 }, { 24, 15, 19, 2, 20 }, { 30, 3, 15, 13, 16 } },
                /* 17 */ { { 28, 1, 107, 5, 108 }, { 28, 10, 46, 1, 47 }, { 28, 1, 22, 15, 23 }, { 28, 2, 14, 17, 15 } },
                /* 18 */ { { 30, 5, 120, 1, 121 }, { 26, 9, 43, 4, 44 }, { 28, 17, 22, 1, 23 }, { 28, 2, 14, 19, 15 } },
                /* 19 */ { { 28, 3, 113, 4, 114 }, { 26, 3, 44, 11, 45 }, { 26, 17, 21, 4, 22 }, { 26, 9, 13, 16, 14 } },
                /* 20 */ { { 28, 3, 107, 5, 108 }, { 26, 3, 41, 13, 42 }, { 30, 15, 24, 5, 25 }, { 28, 15, 15, 10, 16 } },
                /* 21 */ { { 28, 4, 116, 4, 117 }, { 26, 17, 42, 0, 0 }, { 28, 17, 22, 6, 23 }, { 30, 19, 16, 6, 17 } },
                /* 22 */ { { 28, 2, 111, 7, 112 }, { 28, 17, 46, 0, 0 }, { 30, 7, 24, 16, 25 }, { 24, 34, 13, 0, 0 } },
                /* 23 */ { { 30, 4, 121, 5, 122 }, { 28, 4, 47, 14, 48 }, { 30, 11, 24, 14, 25 }, { 30, 16, 15, 14, 16 } },
                /* 24 */ { { 30, 6, 117, 4, 118 }, { 28, 6, 45, 14, 46 }, { 30, 11, 24, 16, 25 }, { 30, 30, 16, 2, 17 } },
                /* 25 */ { { 26, 8, 106, 4, 107 }, { 28, 8, 47, 13, 48 }, { 30, 7, 24, 22, 25 }, { 30, 22, 15, 13, 16 } },
                /* 26 */ { { 28, 10, 114, 2, 115 }, { 28, 19, 46, 4, 47 }, { 28, 28, 22, 6, 23 }, { 30, 33, 16, 4, 17 } },
                /* 27 */ { { 30, 8, 122, 4, 123 }, { 28, 22, 45, 3, 46 }, { 30, 8, 23, 26, 24 }, { 30, 12, 15, 28, 16 } },
                /* 28 */ { { 30, 3, 117, 10, 118 }, { 28, 3, 45, 23, 46 }, { 30, 4, 24, 31, 25 }, { 30, 11, 15, 31, 16 } },
                /* 29 */ { { 30, 7, 116, 7, 117 }, { 28, 21, 45, 7, 46 }, { 30, 1, 23, 37, 24 }, { 30, 19, 15, 26, 16 } },
                /* 30 */ { { 30, 5, 115, 10, 116 }, { 28, 19, 47, 10, 48 }, { 30, 15, 24, 25, 25 }, { 30, 23, 15, 25, 16 } },
                /* 31 */ { { 30, 13, 115, 3, 116 }, { 28, 2, 46, 29, 47 }, { 30, 42, 24, 1, 25 }, { 30, 23, 15, 28, 16 } },
                /* 32 */ { { 30, 17, 115, 0, 0 }, { 28, 10, 46, 23, 47 }, { 30, 10, 24, 35, 25 }, { 30, 19, 15, 35, 16 } },
                /* 33 */ { { 30, 17, 115, 1, 116 }, { 28, 14, 46, 21, 47 }, { 30, 29, 24, 19, 25 }, { 30, 11, 15, 46, 16 } },
                /* 34 */ { { 30, 13, 115, 6, 116 }, { 28, 14, 46, 23, 47 }, { 30, 44, 24, 7, 25 }, { 30, 59, 16, 1, 17 } },
                /* 35 */ { { 30, 12, 121, 7, 122 }, { 28, 12, 47, 26, 48 }, { 30, 39, 24, 14, 25 }, { 30, 22, 15, 41, 16 } },
                /* 36 */ { { 30, 6, 121, 14, 122 }, { 28, 6, 47, 34, 48 }, { 30, 46, 24, 10, 25 }, { 30, 2, 15, 64, 16 } },
                /* 37 */ { { 30, 17, 122, 4, 123 }, { 28, 29, 46, 14, 47 }, { 30, 49, 24, 10, 25 }, { 30, 24, 15, 46, 16 } },
                /* 38 */ { { 30, 4, 122, 18, 123 }, { 28, 13, 46, 32, 47 }, { 30, 48, 24, 14, 25 }, { 30, 42, 15, 32, 16 } },
                /* 39 */ { { 30, 20, 117, 4, 118 }, { 28, 40, 47, 7, 48 }, { 30, 43, 24, 22, 25 }, { 30, 10, 15, 67, 16 } },
                /* 40 */ { { 30, 19, 118, 6, 119 }, { 28, 18, 47, 31, 48 }, { 30, 34, 24, 34, 25 }, { 30, 20, 15, 61, 16 } },
            };

            var table = new int[MaxVersion][,];

            for (var version = 0; version < MaxVersion; version++)
            {
                var row = new int[4, 5];

                for (var level = 0; level < 4; level++)
                    for (var field = 0; field < 5; field++)
                        row[level, field] = raw[version, level, field];

                table[version] = row;
            }

            return table;
        }

        private static byte[][] Alignment() => new[]
        {
            /*  2 */ new byte[] { 6, 18 },
            /*  3 */ new byte[] { 6, 22 },
            /*  4 */ new byte[] { 6, 26 },
            /*  5 */ new byte[] { 6, 30 },
            /*  6 */ new byte[] { 6, 34 },
            /*  7 */ new byte[] { 6, 22, 38 },
            /*  8 */ new byte[] { 6, 24, 42 },
            /*  9 */ new byte[] { 6, 26, 46 },
            /* 10 */ new byte[] { 6, 28, 50 },
            /* 11 */ new byte[] { 6, 30, 54 },
            /* 12 */ new byte[] { 6, 32, 58 },
            /* 13 */ new byte[] { 6, 34, 62 },
            /* 14 */ new byte[] { 6, 26, 46, 66 },
            /* 15 */ new byte[] { 6, 26, 48, 70 },
            /* 16 */ new byte[] { 6, 26, 50, 74 },
            /* 17 */ new byte[] { 6, 30, 54, 78 },
            /* 18 */ new byte[] { 6, 30, 56, 82 },
            /* 19 */ new byte[] { 6, 30, 58, 86 },
            /* 20 */ new byte[] { 6, 34, 62, 90 },
            /* 21 */ new byte[] { 6, 28, 50, 72, 94 },
            /* 22 */ new byte[] { 6, 26, 50, 74, 98 },
            /* 23 */ new byte[] { 6, 30, 54, 78, 102 },
            /* 24 */ new byte[] { 6, 28, 54, 80, 106 },
            /* 25 */ new byte[] { 6, 32, 58, 84, 110 },
            /* 26 */ new byte[] { 6, 30, 58, 86, 114 },
            /* 27 */ new byte[] { 6, 34, 62, 90, 118 },
            /* 28 */ new byte[] { 6, 26, 50, 74, 98, 122 },
            /* 29 */ new byte[] { 6, 30, 54, 78, 102, 126 },
            /* 30 */ new byte[] { 6, 26, 52, 78, 104, 130 },
            /* 31 */ new byte[] { 6, 30, 56, 82, 108, 134 },
            /* 32 */ new byte[] { 6, 34, 60, 86, 112, 138 },
            /* 33 */ new byte[] { 6, 30, 58, 86, 114, 142 },
            /* 34 */ new byte[] { 6, 34, 62, 90, 118, 146 },
            /* 35 */ new byte[] { 6, 30, 54, 78, 102, 126, 150 },
            /* 36 */ new byte[] { 6, 24, 50, 76, 102, 128, 154 },
            /* 37 */ new byte[] { 6, 28, 54, 80, 106, 132, 158 },
            /* 38 */ new byte[] { 6, 32, 58, 84, 110, 136, 162 },
            /* 39 */ new byte[] { 6, 26, 54, 82, 110, 138, 166 },
            /* 40 */ new byte[] { 6, 30, 58, 86, 114, 142, 170 },
        };
    }
}
