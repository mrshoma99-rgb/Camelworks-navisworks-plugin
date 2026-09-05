using System;
using System.Collections.Generic;
using System.Globalization;

namespace CamelWorks.Core.Measurement
{
    /// <summary>How a project wants its numbers written.</summary>
    public enum UnitSystem
    {
        /// <summary>Metres, to millimetre resolution.</summary>
        Metric = 0,

        /// <summary>Whole millimetres. What a fabrication drawing is dimensioned in.</summary>
        Millimetres = 1,

        /// <summary>Feet and fractional inches.</summary>
        Imperial = 2,
    }

    /// <summary>
    /// The one place a number becomes text.
    ///
    /// Every screen and every export calls this; no feature formats a length itself. The same clash
    /// printed "2.45", "2450" and a feet-and-inches figure on three surfaces of one product is how a
    /// trade sets out from the wrong figure, and it is the kind of fault that does not show up in
    /// testing because each screen is individually right.
    ///
    /// The unit choice is a project setting rather than a follower of the Navisworks display unit.
    /// It is seeded once from the model's own units so nothing has to be configured, and then stays:
    /// switching the host to millimetres to read a dimension must not reissue every report in
    /// different units.
    ///
    /// Everything in CamelWorks is held in metres internally, so this is the only conversion in the
    /// product and there is exactly one rounding step per printed number.
    /// </summary>
    public sealed class Measure
    {
        private const double MetresPerInch = 0.0254;

        // A printed number carries thousands separators, so the number that reads it must accept
        // them - otherwise what this class prints, it cannot read back.
        private const NumberStyles Readable = NumberStyles.Float | NumberStyles.AllowThousands;
        private const double SquareMetresPerSquareFoot = 0.09290304;
        private const double CubicMetresPerCubicFoot = 0.028316846592;

        /// <summary>What a value that is not a number reads as. Never "NaN", never blank.</summary>
        public const string Nothing = "—";

        private static readonly int[] Fractions = { 2, 4, 8, 16, 32, 64 };

        /// <summary>The inch fractions worth offering, coarsest first.</summary>
        public static IReadOnlyList<int> Denominators { get; } = Fractions;

        /// <summary>Create a formatter.</summary>
        /// <param name="system">Which system to write in.</param>
        /// <param name="inchDenominator">
        /// The finest inch fraction to round to. Ignored outside <see cref="UnitSystem.Imperial"/>.
        /// Anything not a power of two from 2 to 64 falls back to sixteenths.
        /// </param>
        public Measure(UnitSystem system = UnitSystem.Metric, int inchDenominator = 16)
        {
            System = system;
            InchDenominator = Array.IndexOf(Fractions, inchDenominator) >= 0 ? inchDenominator : 16;
        }

        /// <summary>Which system this writes in.</summary>
        public UnitSystem System { get; }

        /// <summary>The finest inch fraction it rounds to.</summary>
        public int InchDenominator { get; }

        /// <summary>Metres, to millimetre resolution.</summary>
        public static Measure Metric { get; } = new Measure(UnitSystem.Metric);

        /// <summary>Whole millimetres.</summary>
        public static Measure Millimetres { get; } = new Measure(UnitSystem.Millimetres);

        /// <summary>Feet and sixteenths.</summary>
        public static Measure Imperial { get; } = new Measure(UnitSystem.Imperial);

        /// <summary>How the three systems read on a settings screen, in the order to offer them.</summary>
        public static IReadOnlyList<string> Choices { get; } =
            new[] { "Metres", "Millimetres", "Feet and inches" };

        /// <summary>
        /// Read a system from whatever a profile, a file or a person wrote.
        ///
        /// Deliberately forgiving: "mm", "Millimetres", "millimeters" and "Metric (mm)" all mean the
        /// same thing, and a saved profile that predates a wording change must not silently change
        /// a project's units.
        /// </summary>
        /// <param name="word">The written form. Null or unrecognised means metres.</param>
        /// <param name="inchDenominator">The finest inch fraction to round to.</param>
        public static Measure Of(string? word, int inchDenominator = 16)
        {
            if (string.IsNullOrWhiteSpace(word)) return new Measure(UnitSystem.Metric, inchDenominator);

            var text = word!.Trim();

            if (Says(text, "imperial") || Says(text, "feet") || Says(text, "foot")
                || Says(text, "inch") || Says(text, "yard") || Says(text, "mile") || Says(text, "ft"))
                return new Measure(UnitSystem.Imperial, inchDenominator);

            if (Says(text, "millim") || Says(text, "mm"))
                return new Measure(UnitSystem.Millimetres, inchDenominator);

            return new Measure(UnitSystem.Metric, inchDenominator);
        }

        private static bool Says(string text, string token) =>
            text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The unit a length column is in, for the heading rather than every cell.</summary>
        public string LengthUnit =>
            System == UnitSystem.Imperial ? "ft-in" : System == UnitSystem.Millimetres ? "mm" : "m";

        /// <summary>The unit an area column is in.</summary>
        public string AreaUnit => System == UnitSystem.Imperial ? "ft²" : "m²";

        /// <summary>The unit a volume column is in.</summary>
        public string VolumeUnit => System == UnitSystem.Imperial ? "ft³" : "m³";

        /// <summary>A length, from metres.</summary>
        /// <param name="metres">The length. Not-a-number and infinity read as <see cref="Nothing"/>.</param>
        public string Length(double metres) => Length(metres, signed: false);

        /// <summary>
        /// A signed difference, from metres.
        ///
        /// The sign is always written, because a delta column where "12 mm" might mean either
        /// direction is worse than no column. Zero carries no sign.
        /// </summary>
        /// <param name="metres">The difference.</param>
        public string Delta(double metres) => Length(metres, signed: true);

        /// <summary>
        /// A length for a table cell whose column heading already states the unit.
        ///
        /// Repeating "mm" down four hundred rows costs a column of width and tells the reader
        /// nothing the heading did not. Feet and inches have no bare form — the marks are the
        /// notation — so they come back whole.
        /// </summary>
        /// <param name="metres">The length.</param>
        public string Bare(double metres)
        {
            if (!Real(metres)) return Nothing;

            switch (System)
            {
                case UnitSystem.Millimetres:
                    return Math.Round(metres * 1000, MidpointRounding.AwayFromZero)
                               .ToString("N0", CultureInfo.InvariantCulture);

                case UnitSystem.Imperial:
                    return FeetAndInches(metres);

                default:
                    return Fixed(metres, 3);
            }
        }

        /// <summary>An area, from square metres.</summary>
        /// <param name="squareMetres">The area.</param>
        public string Area(double squareMetres)
        {
            if (!Real(squareMetres)) return Nothing;

            return System == UnitSystem.Imperial
                ? Fixed(squareMetres / SquareMetresPerSquareFoot, 2) + " ft²"
                : Fixed(squareMetres, 2) + " m²";
        }

        /// <summary>A volume, from cubic metres.</summary>
        /// <param name="cubicMetres">The volume.</param>
        public string Volume(double cubicMetres)
        {
            if (!Real(cubicMetres)) return Nothing;

            return System == UnitSystem.Imperial
                ? Fixed(cubicMetres / CubicMetresPerCubicFoot, 2) + " ft³"
                : Fixed(cubicMetres, 3) + " m³";
        }

        /// <summary>
        /// An angle, in degrees.
        ///
        /// The same in every system: nobody has ever wanted a rotation in radians on a coordination
        /// report, and there is no imperial degree.
        /// </summary>
        /// <param name="degrees">The angle.</param>
        public string Angle(double degrees)
        {
            if (!Real(degrees)) return Nothing;

            // A rotation of 359.9° is a rotation of -0.1°, and reading it as the former has sent
            // more than one model to site at the wrong orientation.
            var wrapped = degrees % 360;
            if (wrapped > 180) wrapped -= 360;
            if (wrapped < -180) wrapped += 360;

            return Fixed(wrapped, 2) + "°";
        }

        /// <summary>
        /// Read a length a person typed, in metres.
        ///
        /// The counterpart of <see cref="Length(double)"/>, and it exists for the same reason: a
        /// screen that prints millimetres and then asks for metres is a screen that will be typed
        /// into wrongly, and the mistake is invisible because both numbers are plausible.
        ///
        /// A written unit always wins — "2100 mm", "6' 10", "83 in" mean what they say whatever the
        /// project is set to. A bare number is read in this project's own unit: metres, millimetres
        /// or feet.
        /// </summary>
        /// <param name="text">What the user typed.</param>
        /// <param name="metres">The length, when it could be read.</param>
        /// <returns>True when the text was a length.</returns>
        public bool TryRead(string? text, out double metres)
        {
            metres = 0;

            if (string.IsNullOrWhiteSpace(text)) return false;

            var body = text!.Trim();

            var negative = body.StartsWith("-", StringComparison.Ordinal);
            if (negative || body.StartsWith("+", StringComparison.Ordinal)) body = body.Substring(1).Trim();

            if (body.Length == 0) return false;

            var ok = body.IndexOf('\'') >= 0 || body.IndexOf('"') >= 0
                ? TryFeetAndInches(body, out metres)
                : TryDecimal(body, out metres);

            if (ok && negative) metres = -metres;

            return ok;
        }

        /// <summary>A number with an optional written unit, or a bare number in the project's unit.</summary>
        private bool TryDecimal(string body, out double metres)
        {
            metres = 0;

            var split = body.Length;
            while (split > 0 && !char.IsDigit(body[split - 1])) split--;

            var number = body.Substring(0, split).Trim();
            var unit = body.Substring(split).Trim();

            if (!double.TryParse(number, Readable, CultureInfo.InvariantCulture, out var value))
                return false;

            if (unit.Length == 0)
            {
                metres = System == UnitSystem.Millimetres ? value / 1000
                       : System == UnitSystem.Imperial ? value * 12 * MetresPerInch
                       : value;

                return true;
            }

            switch (unit.ToLowerInvariant())
            {
                case "m": metres = value; return true;
                case "mm": metres = value / 1000; return true;
                case "cm": metres = value / 100; return true;
                case "km": metres = value * 1000; return true;
                case "in": metres = value * MetresPerInch; return true;
                case "ft": metres = value * 12 * MetresPerInch; return true;
                case "yd": metres = value * 36 * MetresPerInch; return true;
                default: return false;
            }
        }

        /// <summary>Feet and inches as anybody actually writes them: 6' 10 1/2", 6'-10", 10 1/2".</summary>
        private static bool TryFeetAndInches(string body, out double metres)
        {
            metres = 0;

            var feet = 0.0;
            var rest = body;

            var foot = rest.IndexOf('\'');

            if (foot >= 0)
            {
                if (!double.TryParse(rest.Substring(0, foot).Trim(), Readable,
                                     CultureInfo.InvariantCulture, out feet))
                    return false;

                rest = rest.Substring(foot + 1).TrimStart('-', ' ');
            }

            rest = rest.TrimEnd('"', ' ');

            var inches = 0.0;

            if (rest.Length > 0)
            {
                // "10 1/2" is two tokens; "1/2" on its own is one. Anything else is not a length.
                var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2) return false;

                foreach (var part in parts)
                {
                    var slash = part.IndexOf('/');

                    if (slash < 0)
                    {
                        if (!double.TryParse(part, Readable, CultureInfo.InvariantCulture,
                                             out var whole)) return false;

                        inches += whole;
                        continue;
                    }

                    if (!double.TryParse(part.Substring(0, slash), Readable,
                                         CultureInfo.InvariantCulture, out var top)
                        || !double.TryParse(part.Substring(slash + 1), Readable,
                                            CultureInfo.InvariantCulture, out var bottom)
                        || bottom == 0)
                        return false;

                    inches += top / bottom;
                }
            }

            metres = (feet * 12 + inches) * MetresPerInch;

            return true;
        }

        /// <summary>The unit words <see cref="MetresPer"/> understands, for an error message.</summary>
        public const string UnitWords = "Use one of: mm, cm, m, km, in, ft, yd, mi.";

        /// <summary>
        /// How many metres one of a named unit is, or null when the name is not a unit.
        ///
        /// For converting a number that is already in a stated unit — a duct size in millimetres
        /// into metres for a schedule — rather than for reading a length a person typed, which is
        /// <see cref="TryRead"/>'s job.
        /// </summary>
        /// <param name="unit">The unit's name or abbreviation.</param>
        public static double? MetresPer(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return null;

            switch (unit!.Trim().ToLowerInvariant())
            {
                case "mm": case "millimetre": case "millimetres": case "millimeter": case "millimeters":
                    return 0.001;

                case "cm": case "centimetre": case "centimetres": case "centimeter": case "centimeters":
                    return 0.01;

                case "m": case "metre": case "metres": case "meter": case "meters":
                    return 1;

                case "km": case "kilometre": case "kilometres": case "kilometer": case "kilometers":
                    return 1000;

                case "in": case "inch": case "inches":
                    return MetresPerInch;

                case "ft": case "foot": case "feet":
                    return 12 * MetresPerInch;

                case "yd": case "yard": case "yards":
                    return 36 * MetresPerInch;

                case "mi": case "mile": case "miles":
                    return 63360 * MetresPerInch;

                default:
                    return null;
            }
        }

        /// <summary>A count of something, with thousands separated.</summary>
        /// <param name="count">The count.</param>
        public static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>How this formatter reads on a settings screen.</summary>
        public string Describe() =>
            System == UnitSystem.Imperial
                ? "feet and inches, to the nearest 1/" + InchDenominator.ToString(CultureInfo.InvariantCulture)
                : System == UnitSystem.Millimetres ? "whole millimetres" : "metres, to the millimetre";

        /// <inheritdoc />
        public override string ToString() => Describe();

        private string Length(double metres, bool signed)
        {
            if (!Real(metres)) return Nothing;

            var sign = !signed || metres == 0 ? string.Empty : metres > 0 ? "+" : "-";
            var size = signed ? Math.Abs(metres) : metres;

            switch (System)
            {
                case UnitSystem.Millimetres:
                {
                    // Rounded to the millimetre before the sign is decided, so a -0.4 mm delta
                    // prints "0 mm" rather than "-0 mm".
                    var mm = Math.Round(size * 1000, MidpointRounding.AwayFromZero);
                    if (mm == 0) return "0 mm";
                    return sign + mm.ToString("N0", CultureInfo.InvariantCulture) + " mm";
                }

                case UnitSystem.Imperial:
                    return sign + FeetAndInches(size);

                default:
                {
                    var rounded = Math.Round(size, 3, MidpointRounding.AwayFromZero);
                    if (rounded == 0) return "0.000 m";
                    return sign + rounded.ToString("N3", CultureInfo.InvariantCulture) + " m";
                }
            }
        }

        /// <summary>
        /// Feet and inches, rounded once to the chosen fraction.
        ///
        /// Rounding the whole length in ticks rather than the inches separately is what keeps
        /// 11.96875 inches from printing as <c>0' 12"</c>: the carry happens in integer arithmetic
        /// before anything is split up.
        /// </summary>
        private string FeetAndInches(double metres)
        {
            var d = InchDenominator;
            var ticks = (long)Math.Round(Math.Abs(metres) / MetresPerInch * d, MidpointRounding.AwayFromZero);
            var negative = metres < 0;

            var feet = ticks / (12L * d);
            var rest = ticks % (12L * d);
            var inches = rest / d;
            var numerator = rest % d;

            var text = feet.ToString("N0", CultureInfo.InvariantCulture) + "' "
                     + inches.ToString(CultureInfo.InvariantCulture);

            if (numerator > 0)
            {
                var divisor = Gcd(numerator, d);

                text += " " + (numerator / divisor).ToString(CultureInfo.InvariantCulture)
                      + "/" + (d / divisor).ToString(CultureInfo.InvariantCulture);
            }

            return (negative ? "-" : string.Empty) + text + "\"";
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }

            return a;
        }

        private static string Fixed(double value, int places)
        {
            var rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);

            // Round first, then test: -0.001 at two places is zero, and "-0.00" is not a number
            // anybody wants to read.
            if (rounded == 0) rounded = 0;

            return rounded.ToString("N" + places.ToString(CultureInfo.InvariantCulture),
                                    CultureInfo.InvariantCulture);
        }

        private static bool Real(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
