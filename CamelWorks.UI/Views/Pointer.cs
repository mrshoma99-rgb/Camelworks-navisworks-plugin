using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A mouse cursor carrying the symbol it is about to lay down.
    ///
    /// <b>An armed brush changed what a click does and nothing under the pointer said so.</b> In
    /// the matrix a click is either "select this cell" or "paint this cell", and which of the two
    /// it is was written in a strip along the top of the window - forty rows away from the hand
    /// doing the clicking, and read once at the start of a session if at all. The rest of the
    /// time it was learned by painting a cell nobody meant to paint.
    ///
    /// The cursor is the one thing that is always where the work is. Carrying the symbol, it says
    /// both facts at once and never needs looking up: that a brush is armed, and which one - so
    /// arming C and thinking you armed B is a thing you see before the first click rather than
    /// after the thirtieth.
    ///
    /// <b>Built rather than shipped.</b> The symbols are the matrix's own - any letter, in any
    /// colour somebody chose - so there is no set of cursor files that could cover them. This
    /// draws one: a pointer at the hot spot, and the symbol on its own wash hanging below and to
    /// the right, the way every drag cursor in Windows is shaped.
    /// </summary>
    public static class Pointer
    {
        /// <summary>How big the cursor is, in pixels each way.</summary>
        private const int Side = 32;

        /// <summary>Where the chip sits, clear of the pointer at the hot spot.</summary>
        private static readonly Rect Chip = new Rect(11, 11, 20, 20);

        /// <summary>
        /// A cursor carrying one symbol, or null when one cannot be built.
        ///
        /// <b>Null is a real answer.</b> This draws a bitmap and hands Windows a cursor file it
        /// has assembled by hand, and there is no version of that which is worth an exception
        /// reaching a coordinator mid-drag. Every caller has a stock cursor to fall back on, so
        /// the worst this can do is leave the pointer looking like a pen instead of like a "B".
        /// </summary>
        /// <param name="mark">The symbol, as it is drawn in the cells.</param>
        /// <param name="ground">The wash its cells carry.</param>
        /// <param name="ink">The colour the symbol is written in on that wash.</param>
        public static Cursor? Carrying(string mark, Brush ground, Brush ink)
        {
            try
            {
                var visual = new DrawingVisual();

                using (var paint = visual.RenderOpen())
                {
                    Carried(paint, mark, ground, ink, visual);
                    Arrow(paint);
                }

                var map = new RenderTargetBitmap(Side, Side, 96, 96, PixelFormats.Pbgra32);

                map.Render(visual);

                var pixels = new byte[Side * Side * 4];

                map.CopyPixels(pixels, Side * 4, 0);

                using (var file = new MemoryStream(Cursored(pixels)))
                {
                    return new Cursor(file);
                }
            }
            catch (Exception)
            {
                // Whatever went wrong - a font, a brush, a Windows version that will not take
                // the file - a stock cursor is the caller's answer and the session goes on.
                return null;
            }
        }

        /// <summary>The symbol on its own wash, as the cells draw it.</summary>
        /// <param name="paint">What to draw into.</param>
        /// <param name="mark">The symbol.</param>
        /// <param name="ground">The wash under it.</param>
        /// <param name="ink">The colour of the letters.</param>
        /// <param name="owner">The visual being drawn, which is asked what a pixel is worth.</param>
        private static void Carried(DrawingContext paint, string mark, Brush ground, Brush ink,
                                    Visual owner)
        {
            // A white edge so the chip reads against a coloured cell as well as against the
            // white of an empty one - a pale wash with no edge disappears into the sheet it is
            // hovering over, which is every wash the matrix hands out.
            paint.DrawRoundedRectangle(ground, new Pen(Brushes.White, 1), Chip, 4, 4);

            var words = new FormattedText(
                mark ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold,
                             FontStretches.Normal),
                12,
                ink,
                VisualTreeHelper.GetDpi(owner).PixelsPerDip)
            {
                MaxTextWidth = Chip.Width,
                MaxLineCount = 1,
                Trimming = TextTrimming.None,
            };

            paint.DrawText(words, new Point(Chip.X + ((Chip.Width - words.Width) / 2),
                                            Chip.Y + ((Chip.Height - words.Height) / 2)));
        }

        /// <summary>The pointer itself, with its tip at the hot spot.</summary>
        /// <param name="paint">What to draw into.</param>
        private static void Arrow(DrawingContext paint)
        {
            var shape = new StreamGeometry();

            using (var edge = shape.Open())
            {
                edge.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);

                edge.PolyLineTo(
                    new[]
                    {
                        new Point(0, 12.5),
                        new Point(3.1, 9.6),
                        new Point(5.3, 14),
                        new Point(7.4, 13),
                        new Point(5.1, 8.7),
                        new Point(9.3, 8.7),
                    },
                    isStroked: true,
                    isSmoothJoin: false);
            }

            shape.Freeze();

            // Black on a white outline, which is the one pointer that is legible on every ground
            // Windows has ever put one on.
            paint.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.4), shape);
        }

        /// <summary>
        /// The pixels, wrapped in the cursor file Windows expects.
        ///
        /// <b>A plain 32-bit bitmap, not a PNG.</b> Windows has taken PNG payloads in icons since
        /// Vista and is less dependable about them in cursors, and this has to work on whatever a
        /// coordinator's machine turns out to be. So: the directory, one entry, a bitmap header
        /// claiming twice its own height, the colours bottom-up, and an all-zero mask that the
        /// format insists on and that thirty-two bits of alpha make redundant.
        /// </summary>
        /// <param name="pixels">The rendered pixels, top-down, premultiplied.</param>
        private static byte[] Cursored(byte[] pixels)
        {
            var row = Side * 4;
            var colours = new byte[Side * row];

            for (var y = 0; y < Side; y++)
            {
                // Bottom-up, because that is the way a bitmap is stored.
                var from = (Side - 1 - y) * row;
                var to = y * row;

                for (var x = 0; x < Side; x++)
                {
                    var i = from + (x * 4);
                    var o = to + (x * 4);
                    var alpha = pixels[i + 3];

                    if (alpha == 0) continue;

                    // Out of premultiplied and back into plain colour, or every soft edge in the
                    // drawing comes out darkened against whatever it is over.
                    colours[o] = Plain(pixels[i], alpha);
                    colours[o + 1] = Plain(pixels[i + 1], alpha);
                    colours[o + 2] = Plain(pixels[i + 2], alpha);
                    colours[o + 3] = alpha;
                }
            }

            // One bit a pixel, rounded up to four bytes a row - which thirty-two pixels already
            // are. Left at zero: the alpha above says what is see-through.
            var mask = new byte[Side * 4];

            using (var memory = new MemoryStream())
            using (var write = new BinaryWriter(memory))
            {
                write.Write((ushort)0);                 // reserved
                write.Write((ushort)2);                 // a cursor, rather than an icon
                write.Write((ushort)1);                 // one image in it

                write.Write((byte)Side);                // how wide
                write.Write((byte)Side);                // how tall
                write.Write((byte)0);                   // no colour table
                write.Write((byte)0);                   // reserved
                write.Write((ushort)0);                 // hot spot, across
                write.Write((ushort)0);                 // hot spot, down
                write.Write((uint)(40 + colours.Length + mask.Length));
                write.Write((uint)22);                  // where the image starts

                write.Write(40);                        // the header's own size
                write.Write(Side);                      // how wide
                write.Write(Side * 2);                  // the colours and the mask, stacked
                write.Write((ushort)1);                 // planes
                write.Write((ushort)32);                // bits a pixel
                write.Write(0);                         // not compressed
                write.Write(colours.Length + mask.Length);
                write.Write(0);                         // pixels per metre, across
                write.Write(0);                         // pixels per metre, down
                write.Write(0);                         // colours used
                write.Write(0);                         // colours that matter

                write.Write(colours);
                write.Write(mask);
                write.Flush();

                return memory.ToArray();
            }
        }

        /// <summary>One channel, out of premultiplied alpha and back into plain colour.</summary>
        /// <param name="channel">What the channel holds now.</param>
        /// <param name="alpha">How opaque the pixel is.</param>
        private static byte Plain(byte channel, byte alpha) =>
            (byte)Math.Min(255, channel * 255 / alpha);
    }
}
