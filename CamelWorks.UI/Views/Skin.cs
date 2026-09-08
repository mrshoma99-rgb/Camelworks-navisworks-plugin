using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CamelWorks.UI.Views
{
    /// <summary>Which palette the pane is painted in.</summary>
    public enum SkinMode
    {
        /// <summary>Follow whatever Windows is set to, and change when it changes.</summary>
        Auto = 0,

        /// <summary>Always light.</summary>
        Light = 1,

        /// <summary>Always dark.</summary>
        Dark = 2,
    }

    /// <summary>
    /// The palette, and the one place that owns it.
    ///
    /// These values are the token set the BIMCamel IFC exporter already ships — the other plug-in
    /// from the same publisher, which docks beside this one in the same Navisworks. Two panes from
    /// one publisher that do not match is a worse outcome than any palette either could pick, so
    /// this is that palette, moved somewhere both can reach.
    ///
    /// Everything is looked up by key through <c>DynamicResource</c> rather than resolved once,
    /// which is what lets <see cref="Mode"/> change the whole pane in place instead of asking every
    /// open screen to rebuild.
    /// </summary>
    public static class Skin
    {
        private const string FileName = "skin.txt";

        private static readonly object Lock = new object();
        private static ResourceDictionary? _sheet;
        private static SkinMode _mode = SkinMode.Auto;
        private static bool _modeRead;
        private static bool _dark;

        /// <summary>Raised after the palette changes, for anything holding a resolved brush.</summary>
        public static event EventHandler? Changed;

        /// <summary>
        /// Why the skin is not loaded, or null when it is.
        ///
        /// Reported by the self-test and by the design harness rather than thrown, because the
        /// pane is usable without it and unusable if this takes the pane down.
        /// </summary>
        public static string? Problem { get; private set; }

        /// <summary>
        /// The skin, as one dictionary to merge into a pane.
        ///
        /// Never null and never throws. If the compiled dictionary cannot be loaded — a mangled
        /// install, a partial deployment — this hands back an empty one, and the pane comes up in
        /// Windows' own control styling rather than not coming up at all.
        /// </summary>
        public static ResourceDictionary Sheet
        {
            get
            {
                lock (Lock)
                {
                    if (_sheet != null) return _sheet;

                    var sheet = new ResourceDictionary();

                    try
                    {
                        sheet.MergedDictionaries.Add(new Themes.SkinSheet());
                    }
                    catch (Exception e)
                    {
                        // Not thrown on: an unstyled pane is a disappointment, a pane that throws
                        // on construction is a plug-in the user uninstalls. But not silent either.
                        // A duplicate x:Key makes the whole dictionary throw while it is being
                        // built, and every control in the product comes up in Windows' own styling
                        // with nothing anywhere saying why.
                        Problem = "The CamelWorks skin could not be loaded, so the windows are drawn in "
                                  + "Windows' own control styling. Everything still works. "
                                  + e.GetType().Name + ": " + e.Message;
                    }

                    _sheet = sheet;
                    Repaint(sheet, IsDark);
                    return sheet;
                }
            }
        }

        /// <summary>Which palette to use. Setting it repaints every open pane.</summary>
        public static SkinMode Mode
        {
            get
            {
                if (!_modeRead)
                {
                    _modeRead = true;
                    _mode = Read();
                }

                return _mode;
            }

            set
            {
                if (Mode == value) return;

                _modeRead = true;
                _mode = value;
                Write(value);
                Refresh();
            }
        }

        /// <summary>Whether the pane is currently painted dark.</summary>
        public static bool IsDark => Mode == SkinMode.Dark || (Mode == SkinMode.Auto && WindowsIsDark());

        /// <summary>
        /// Step to the next mode: auto, then light, then dark, then back.
        ///
        /// A single toggle would be simpler and would also make "follow Windows" unreachable once
        /// it had been left, which is the one of the three most people want.
        /// </summary>
        public static void Next() =>
            Mode = Mode == SkinMode.Auto ? SkinMode.Light
                 : Mode == SkinMode.Light ? SkinMode.Dark
                 : SkinMode.Auto;

        /// <summary>What the current mode is called, for a button that has to say so.</summary>
        public static string ModeName =>
            Mode == SkinMode.Auto ? "Theme: follows Windows"
            : Mode == SkinMode.Light ? "Theme: light"
            : "Theme: dark";

        /// <summary>Re-read the palette and tell everything holding a brush.</summary>
        public static void Refresh()
        {
            Repaint(Sheet, IsDark);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Merge the skin into something, once.</summary>
        /// <param name="element">The pane or window to paint.</param>
        public static void Apply(FrameworkElement element)
        {
            if (element != null) ApplyTo(element.Resources);
        }

        /// <summary>
        /// Merge the skin into a resource dictionary, once.
        ///
        /// Separate from <see cref="Apply(FrameworkElement)"/> because an application's resources
        /// are a dictionary without an element around them, and the design harness paints itself
        /// at that level so that every window it opens is already skinned.
        /// </summary>
        /// <param name="target">The dictionary to merge into.</param>
        public static void ApplyTo(ResourceDictionary target)
        {
            if (target == null) return;
            if (target.MergedDictionaries.Contains(Sheet)) return;

            target.MergedDictionaries.Add(Sheet);
        }

        /// <summary>
        /// A brush by key, for the parts built in code rather than from a template.
        /// </summary>
        /// <param name="key">A palette key, such as <c>Muted</c> or <c>AmberBg</c>.</param>
        public static Brush Of(string key)
        {
            if (Sheet[key] is Brush brush) return brush;

            // A missing key is a mistake in this file, not in the caller, and returning something
            // visible beats returning null and taking the pane down with a NullReferenceException
            // in a layout pass nobody can read a stack trace out of.
            return Brushes.Gray;
        }

        /// <summary>
        /// The accent for one family of tools.
        ///
        /// Every tool rendered in one grey reads as one tool. This is what the ribbon tints its
        /// icons with — deliberately not the brand accent, which everywhere else means "this is
        /// the thing to press".
        ///
        /// The families were workspaces once, and the ids are still theirs: the ribbon groups its
        /// buttons into panels, and each panel's title picks the family its icons are drawn in.
        /// </summary>
        /// <param name="family">A family id, such as <c>coordinate</c> or <c>publish</c>.</param>
        public static Brush Family(string? family)
        {
            if (string.IsNullOrEmpty(family)) return Of("Muted");
            return Sheet["Fam." + family] is Brush brush ? brush : Of("Muted");
        }

        /// <summary>
        /// The wash one symbol of a legend is drawn on, by where it sits in that legend.
        ///
        /// Position rather than a colour stored on the symbol: the legend's order is already
        /// stable, already saved and already what the palette lists, so nothing new has to be
        /// persisted, exported or reconciled when a client's workbook comes back. Eight of them,
        /// wrapping - a legend of more than eight symbols is not a key anybody reads anyway, and
        /// two symbols sharing a wash still carry different letters.
        /// </summary>
        /// <param name="at">Where the symbol sits in the legend, from zero.</param>
        public static Brush Symbol(int at)
        {
            var letter = (char)('A' + (((at % 8) + 8) % 8));

            return Sheet["Sym." + letter] is Brush brush ? brush : Of("Nav");
        }

        /// <summary>The text colour for a tone.</summary>
        /// <param name="tone">The tone.</param>
        public static Brush Ink(Tone tone) => tone switch
        {
            Tone.Good => Of("Ok"),
            Tone.Warn => Of("AmberTx"),
            Tone.Bad => Of("RedTx"),
            _ => Of("Text"),
        };

        /// <summary>The background wash for a tone.</summary>
        /// <param name="tone">The tone.</param>
        public static Brush Wash(Tone tone) => tone switch
        {
            Tone.Good => Of("OkBg"),
            Tone.Warn => Of("AmberBg"),
            Tone.Bad => Of("RedBg"),
            _ => Brushes.Transparent,
        };

        /// <summary>The border colour for a tone.</summary>
        /// <param name="tone">The tone.</param>
        public static Brush Edge(Tone tone) => tone switch
        {
            Tone.Good => Of("OkBd"),
            Tone.Warn => Of("AmberBd"),
            Tone.Bad => Of("RedBd"),
            _ => Of("Bd"),
        };

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The two palettes, as one table.
        ///
        /// Held together rather than as two methods so a key that exists in one and not the other
        /// is visible on the page: a token added to light and forgotten in dark is a control that
        /// stays white in a dark pane, and it is exactly the kind of thing that gets shipped.
        /// </summary>
        private static readonly (string Key, string Light, string Dark)[] Palette =
        {
            // The BIMCamel web brand blue, and the web's own hover direction: a hovered primary
            // darkens rather than lightening, so it does not become harder to read than at rest.
            //
            // Accent is ink and edges, so it lifts in the dark palette to stay legible on a dark
            // ground. Hover and press are the primary's plate, which is brand-coloured in both
            // themes and carries white text, so they are the same dark blue in both: lightening
            // them for the dark theme would put white on #93C5FD at 1.5:1.
            ("Accent",      "#2563EB", "#60A5FA"),
            ("AccentHover", "#1D4ED8", "#1D4ED8"),
            ("AccentPress", "#1E40AF", "#1E40AF"),
            ("AccentWash",  "#DBEAFE", "#1E3A5F"),

            ("Bg",          "#EEF1F5", "#15171B"),
            ("Pane",        "#F7F8FA", "#1C1F24"),
            ("Card",        "#FFFFFF", "#23272E"),
            ("Card2",       "#FBFCFD", "#262B32"),

            ("Text",        "#1F2329", "#E7EAEE"),
            // Faint carries every field hint, every folded summary and — worse — two warnings
            // about writes Ctrl+Z cannot undo. At #9AA3AD it was 2.56:1 on a card and 2.26:1
            // in a form well: below the large-text floor, and paler in the light theme than the
            // dark one, which is backwards. Muted moves down with it so the two tiers stay a
            // tier apart.
            ("Muted",       "#5C646F", "#9AA3AD"),
            ("Faint",       "#767E89", "#8C95A0"),

            ("Bd",          "#E1E5EA", "#333941"),
            ("BdStrong",    "#CDD3DA", "#3D444D"),

            // The button tier. A secondary button used to be Card2 on a Card - 1.02:1 - so the
            // only thing separating a press from a text box was one point of font size. It has
            // its own fill and its own edge now, and a larger radius than a field, so an action
            // and an input are different shapes before they are read.
            ("BtnBg",       "#EDF0F4", "#2F353D"),
            ("BtnBd",       "#C6CDD6", "#454D57"),
            ("BtnHover",    "#E2E7ED", "#39414A"),

            // The cap a dropdown's chevron sits in. A hairline glyph floating over the value was
            // the only thing telling a dropdown from a text box; a well with its own edge is
            // visible without being read, and it reserves the gutter so a long value truncates
            // before it reaches the arrow.
            ("Well",        "#EDF1F6", "#333A43"),

            // The keyboard ring. Two of thirteen control templates drew focus at all, and the two
            // used different geometry - one swapped a border brush, the other added a border and
            // moved the content under it.
            ("Focus",       "#2563EB", "#93C5FD"),
            // In light the input is the brightest surface on the panel, which is what makes it
            // read as "type here". In dark it was #1B1E23 inside a #15171B well - 1.07:1, an
            // input visible only by its border. It sits above both the well and the card now.
            ("Field",       "#FFFFFF", "#2B3138"),
            // Darker than Bd and darker than BdStrong, and deliberately so: an editable edge has
            // to outrank a divider. At #CDD3DA it was the same value as BdStrong, so a text box,
            // a rule and a button edge were one colour.
            ("FieldBd",     "#B4BCC7", "#4A525C"),

            // The disclosure fill. Dark #1A1D22 on a #1C1F24 pane is 1.02:1 - no surface at
            // all, so a fold on the page read as a hairline while the same fold inside a card
            // read correctly.
            ("Nav",         "#EAEDF1", "#282D34"),
            ("NavActive",   "#E6F0FA", "#1F2D3D"),
            ("Hover",       "#0F000000", "#14FFFFFF"),
            ("Press",       "#1A000000", "#24FFFFFF"),
            ("Track",       "#D8DDE3", "#2E343B"),
            ("Thumb",       "#B4BCC5", "#454C55"),

            ("OkBg",        "#E4F6EC", "#12301F"),
            ("OkBd",        "#B7E3C9", "#245638"),
            ("Ok",          "#1A8F4C", "#46C97E"),

            ("AmberBg",     "#FFF7E6", "#3A2F12"),
            ("AmberBd",     "#FFE1A8", "#5C4A1E"),
            ("AmberTx",     "#8A5A00", "#F0C66A"),

            ("RedBg",       "#FDECEC", "#3A1D1D"),
            ("RedBd",       "#F3C6C6", "#5C2B2B"),
            ("RedTx",       "#B3261E", "#F08C84"),

            ("BlueBg",      "#E0F2FE", "#10262F"),
            ("BlueBd",      "#B6E1F8", "#1F4457"),
            ("BlueTx",      "#0B6C97", "#67C8EA"),


            // The two inks for writing on a colour somebody chose. Fixed rather than theme-led,
            // because the colour under them is fixed: a wash that does not change with the theme
            // needs a letter that does not either.
            ("Ink.OnLight",  "#1F2329", "#1F2329"),
            ("Ink.OnDark",   "#F2F5F8", "#F2F5F8"),

            // Eight washes for the legend's symbols, in the order the legend lists them. Every
            // symbol in the grid was previously drawn on the same pale blue, so the only thing
            // separating a hard clash from a clearance across two hundred cells was the shape of one
            // letter. A matrix is read as a pattern — all of MEP against structure is a clearance,
            // and that should be visible without reading — and a single fill prevented that. Each
            // wash is pale enough to carry the letter at full contrast, and they are separated by
            // hue rather than lightness so the letter is never the only channel.
            //
            // Ordered so the first four separate best. Hue alone is not separation: simulated for a
            // deuteranope, the blue and the lavender were 1.2 apart in CIELAB, and they were
            // positions one and four, which on a legend of four symbols is the shipped default.
            // Blue, amber, green and pink lead; lavender, teal, lime and slate follow, at legend
            // sizes past what reads as a key.
            ("Sym.A",       "#DCEAF8", "#1E2E3F"),
            ("Sym.B",       "#FBEFD3", "#3A2F12"),
            ("Sym.C",       "#DDF2E4", "#17311F"),
            ("Sym.D",       "#FADCE8", "#38202B"),
            ("Sym.E",       "#EAE2F8", "#2A2340"),
            ("Sym.F",       "#D6F0EF", "#12312F"),
            ("Sym.G",       "#E9F2CE", "#29331A"),
            ("Sym.H",       "#E2E6EC", "#2B3138"),

            // The diagonal is not a symbol and must not be drawn as one. A discipline against
            // itself is the spine of the grid, the line every pair is mirrored about, and it was
            // drawn in the same grey that now means "this makes no test". It is deeper than any
            // symbol wash and neutral against all of them, so it reads as structure rather than as a
            // decision.
            ("Spine",       "#CBD5E3", "#33404F"),
            ("SpineBd",     "#9FB0C4", "#4A5A6D"),

            // The family hues lift in dark rather than staying put: a #0F766E teal that reads as a
            // considered green on white is a smudge on #23272E.
            ("Fam.Home",       "#57616C", "#94A0AC"),
            ("Fam.Project",    "#0F766E", "#3FB6A8"),
            ("Fam.Coordinate", "#C2410C", "#F08A4B"),
            ("Fam.Compare",    "#6D46C8", "#A98BF0"),
            ("Fam.Data",       "#0369A1", "#4FAEE8"),
            ("Fam.Sets",       "#B45309", "#E0A34A"),
            ("Fam.Batch",      "#4D7C0F", "#A9CF5C"),
            ("Fam.Automate",   "#BE185D", "#EE7FAE"),
        };

        private static readonly (string Key, string LightFrom, string LightTo, string DarkFrom, string DarkTo)[] Gradients =
        {
            ("BrandBar", "#0070C0", "#0D83DA", "#1B5E92", "#2277B8"),
        };

        private static void Repaint(ResourceDictionary sheet, bool dark)
        {
            _dark = dark;

            foreach (var (key, light, night) in Palette)
                sheet[key] = Frozen(dark ? night : light);

            foreach (var (key, lightFrom, lightTo, darkFrom, darkTo) in Gradients)
            {
                var brush = new LinearGradientBrush(
                    Parse(dark ? darkFrom : lightFrom),
                    Parse(dark ? darkTo : lightTo),
                    new Point(0, 0),
                    new Point(1, 0));

                brush.Freeze();
                sheet[key] = brush;
            }
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush(Parse(hex));
            brush.Freeze();
            return brush;
        }

        private static Color Parse(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (Exception)
            {
                return Colors.Gray;
            }
        }

        /// <summary>
        /// What Windows is set to.
        ///
        /// Read from the registry rather than from <c>SystemParameters</c> because the apps theme
        /// and the window-chrome theme are two different settings, and it is the apps one that
        /// says whether a panel should be dark.
        /// </summary>
        private static bool WindowsIsDark()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int light) return light == 0;
                }
            }
            catch (Exception)
            {
                // No registry, no permission, a stripped image: light is the safe answer, since
                // every supported Navisworks release is light by default.
            }

            return false;
        }

        private static string PrefFile() => System.IO.Path.Combine(Host.UserDirectory, FileName);

        private static SkinMode Read()
        {
            try
            {
                var file = PrefFile();
                if (!File.Exists(file)) return SkinMode.Auto;

                var text = File.ReadAllText(file).Trim();

                if (string.Equals(text, "light", StringComparison.OrdinalIgnoreCase)) return SkinMode.Light;
                if (string.Equals(text, "dark", StringComparison.OrdinalIgnoreCase)) return SkinMode.Dark;
            }
            catch (Exception)
            {
                // A preference that cannot be read is not worth an error message.
            }

            return SkinMode.Auto;
        }

        private static void Write(SkinMode mode)
        {
            try
            {
                Directory.CreateDirectory(Host.UserDirectory);

                File.WriteAllText(PrefFile(), mode == SkinMode.Light ? "light"
                                        : mode == SkinMode.Dark ? "dark"
                                        : "auto");
            }
            catch (Exception)
            {
                // A preference that cannot be saved is worth losing, not worth interrupting for.
            }
        }

        /// <summary>Whether the last repaint was the dark one. Exposed for the studio harness.</summary>
        internal static bool LastPaintedDark => _dark;

        /// <summary>Every palette key, so the harness can prove the two columns match.</summary>
        internal static IEnumerable<string> Keys
        {
            get
            {
                foreach (var (key, _, _) in Palette) yield return key;
                foreach (var (key, _, _, _, _) in Gradients) yield return key;
            }
        }

        /// <summary>A number formatted the way every screen formats numbers.</summary>
        /// <param name="value">The number.</param>
        internal static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
