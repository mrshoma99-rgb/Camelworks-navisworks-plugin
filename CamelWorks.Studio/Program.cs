using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The design harness: CamelWorks' own controls, in a plain window, with no Navisworks.
    ///
    /// <code>
    /// dotnet run --project CamelWorks.Studio                          look at them
    /// dotnet run --project CamelWorks.Studio -- --shoot out\shots     write them to PNG
    /// </code>
    ///
    /// The second form draws pages and exits, which is what the ui-shots workflow runs. It takes
    /// <c>--only &lt;ids&gt;</c> to name which, because a Windows runner bills at twice the rate
    /// and a render is nearly always asked for to settle one screen.
    /// </summary>
    public static class Program
    {
        /// <summary>The widths worth looking at: the pane's minimum, its default, and docked wide.</summary>
        private static readonly int[] Widths = { 470, 640, 900 };

        /// <summary>
        /// Every page: the ones that prove the vocabulary, then the panels themselves.
        ///
        /// The distinction is the point. A widget set can be faultless and still compose into a
        /// screen nobody can use, so reviewing the parts bin is not reviewing the product.
        /// </summary>
        internal static IReadOnlyList<Sheet> Everything { get; } =
            Sheets.All.Concat(Panels.All).Concat(Dialogs.All).ToList();

        /// <summary>
        /// Which pages a shoot draws when it is not told, and the word that means all of them.
        ///
        /// A render is asked for to settle one screen. Every page in both themes is thirty images
        /// from a Windows runner billed at twice the rate, twenty-eight of which nobody opens. Named
        /// pages are two.
        /// </summary>
        private const string Only = "app-clash-create,app-clash-settings-team,app-clash-settings-units,app-clash-report,app-clash-manage,app-clash-rules-set";

        /// <summary>Run the harness.</summary>
        /// <param name="args">
        /// Pass <c>--shoot &lt;folder&gt;</c> to write PNGs instead of opening a window, and
        /// <c>--only &lt;id,id&gt;</c> to name which pages - or <c>all</c> for every one of them.
        /// </param>
        /// <returns>Zero on success.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            // Software rendering, always. A screenshot taken on a build agent and one taken on a
            // workstation with a GPU must be the same picture, or comparing them proves nothing.
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Skin.ApplyTo(application.Resources);

            var shoot = Array.IndexOf(args, "--shoot");

            if (shoot >= 0)
            {
                var folder = shoot + 1 < args.Length ? args[shoot + 1] : "shots";
                var picked = Array.IndexOf(args, "--only");
                var only = picked >= 0 && picked + 1 < args.Length ? args[picked + 1] : Only;

                return Shoot(folder, only);
            }

            return application.Run(new StudioWindow());
        }

        /// <summary>
        /// Render the named sheets at every width and write them out.
        ///
        /// This is what makes the harness worth having on a build agent rather than only on a desk:
        /// the images come back from the machine that compiled the change, so a layout can be
        /// reviewed by whoever wrote it without a Navisworks licence.
        ///
        /// Light palette only, and only the named pages. Every page in both themes was thirty images
        /// a run, and a run happens because one screen needs looking at; the other twenty-eight cost
        /// Windows minutes at twice the rate and are not opened. The dark palette is a skin over the
        /// same layout, and no fault reported against this product has been visible in one and not
        /// the other.
        /// </summary>
        /// <param name="folder">Where to write the PNGs.</param>
        /// <param name="only">Which pages, by id, comma-separated - or <c>all</c> for every one.</param>
        private static int Shoot(string folder, string only)
        {
            Directory.CreateDirectory(folder);

            // Nothing named means nothing drawn, not everything. A push carries no dispatch input,
            // so the workflow hands this an empty string, and an empty list read as "no filter" drew
            // all nineteen pages off a Windows runner for a request that wanted two.
            if (string.IsNullOrWhiteSpace(only)) only = Only;

            var wanted = only.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(one => one.Trim())
                             .Where(one => one.Length > 0)
                             .ToList();

            var every = wanted.Any(one => string.Equals(one, "all", StringComparison.OrdinalIgnoreCase));

            var pages = every
                ? Everything
                : Everything.Where(sheet => wanted.Any(
                      one => string.Equals(one, sheet.Id, StringComparison.OrdinalIgnoreCase))).ToList();

            // Checked before anything is drawn. Sixteen pictures of Windows' default controls look
            // enough like a deliberate design to be reviewed as one.
            _ = Skin.Sheet;

            if (Skin.Problem != null)
            {
                Log("SKIN NOT LOADED: " + Skin.Problem);
                Log("Refusing to render: the pictures would be of Windows' controls, not this one's.");
                File.WriteAllLines(System.IO.Path.Combine(folder, "_log.txt"), _log);
                return 2;
            }

            Log("skin loaded: " + Skin.Sheet.MergedDictionaries[0].Count.ToString(CultureInfo.InvariantCulture)
                + " styles and resources");

            // A name that matched nothing is a typo, and a typo must not look like a clean run
            // that happened to draw nothing: the branch would be force-pushed empty and whoever
            // asked for the picture would go looking for a fault in the screen instead.
            foreach (var one in wanted)
                if (!every && !Everything.Any(sheet => string.Equals(one, sheet.Id,
                                                                     StringComparison.OrdinalIgnoreCase)))
                    Log("NO SUCH PAGE: " + one);

            if (pages.Count == 0)
            {
                Log("nothing to draw: --only named " + only);
                File.WriteAllLines(System.IO.Path.Combine(folder, "_log.txt"), _log);
                return 3;
            }

            Log("drawing " + pages.Count.ToString(CultureInfo.InvariantCulture) + " of "
                + Everything.Count.ToString(CultureInfo.InvariantCulture) + " pages, light");

            Skin.Mode = SkinMode.Light;

            var written = 0;

            foreach (var sheet in pages)
            {
                foreach (var width in sheet.Widths ?? Widths)
                {
                    var name = sheet.Id + "-" + width.ToString(CultureInfo.InvariantCulture) + ".png";
                    var path = System.IO.Path.Combine(folder, name);

                    try
                    {
                        Capture(sheet, width, path);
                        written++;
                        Log("wrote " + name);
                    }
                    catch (Exception e)
                    {
                        // One sheet that cannot be rendered must not cost the others.
                        Log("FAILED " + name + ": " + e);
                    }
                }
            }

            Log(written.ToString(CultureInfo.InvariantCulture) + " images in " + folder);

            try
            {
                File.WriteAllLines(System.IO.Path.Combine(folder, "_log.txt"), _log);
            }
            catch (Exception e)
            {
                Console.WriteLine("could not write the log: " + e.Message);
            }

            return written > 0 ? 0 : 1;
        }

        /// <summary>
        /// Lay one sheet out off-screen and write it to a file.
        ///
        /// It goes through a real <see cref="Window"/> rather than straight to a
        /// <see cref="RenderTargetBitmap"/> because parts of WPF only behave once there is a
        /// presentation source — a ComboBox popup, a ListView's generated containers, anything
        /// that measures against a real DPI. A window positioned off the desktop gives all of that
        /// without anything appearing on screen.
        /// </summary>
        /// <param name="sheet">What to draw.</param>
        /// <param name="width">How wide the pane is.</param>
        /// <param name="path">Where to write it.</param>
        private static void Capture(Sheet sheet, int width, string path)
        {
            // Assigned below and captured by the closure, not by value: a page that photographs
            // itself mid-run needs the frame it is going to be put in, and that frame cannot exist
            // until the page it holds does.
            Border? host = null;
            var taken = false;

            void Shoot()
            {
                if (taken || host == null) return;
                taken = true;
                Snap(host, width, path);
            }

            var content = sheet.Builder(Shoot);

            host = new Border { Width = width, Child = content };
            host.SetResourceReference(Border.BackgroundProperty, "Pane");

            var window = new Window
            {
                Width = width,
                Height = sheet.Height,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Left = -32000,
                Top = -32000,
                Content = host,
            };

            Skin.Apply(window);

            // The window is never shown.
            //
            // It was, on the theory that parts of WPF only behave once there is a presentation
            // source. The shoot's own log disproved it: only the first page of a run gets a real
            // window - the app shuts down when that one closes, so every later Show is a no-op -
            // and the first page is the one that comes back wrong, every time, while the pages that
            // never got a window come back correct with their tables, rows and combo boxes intact.
            //
            // What a shown window costs is its size. A build agent's desktop is smaller than these
            // pages: the window came up at 1044 px for a 1180 px sheet, the page measured itself
            // against that, and the measurement survived being handed a bigger slot afterwards. The
            // picture was the size asked for with the page laid out for a narrower one inside it.
            //
            // The Window object stays, unshown, so the page inherits its resources during the
            // passes below; Snap puts the skin on the page itself before it cuts it loose.

            // Two layout passes with a pump between them. The first generates the item containers
            // a ListView only creates once it has been measured; the second lays out what that
            // generation added. One pass gives tables with headers and no rows.
            window.UpdateLayout();
            Pump();
            window.UpdateLayout();
            Pump();

            // A page with something to set in motion does it now, laid out and pumped, and takes
            // its own picture from inside whatever it started.
            if (sheet.Drive != null)
            {
                sheet.Drive(host);
                Pump();
            }

            // Anything that did not photograph itself gets taken here, settled. Snap takes the
            // page off the window first, so nothing the window did to it is still on it.
            Shoot();
        }

        /// <summary>Write one laid-out element to a PNG.</summary>
        /// <param name="host">The frame around the page.</param>
        /// <param name="width">How wide the pane is.</param>
        /// <param name="path">Where to write it.</param>
        private static void Snap(Border host, int width, string path)
        {
            // Off the window before anything is measured.
            //
            // While the page is the window's content the window owns its arrangement, and a
            // window on a build agent is clamped to the agent's display: 1030 by 774 where the
            // sheet asked for 1180 by 980. The measure and arrange below set the size the picture
            // is taken at, but UpdateLayout runs whatever window pass is still outstanding, and
            // that pass arranges the page back into the client area — which leaves the layout
            // clip an undersized slot comes with sitting on the page itself, so the bitmap is the
            // size asked for with the page cut off inside it. That is what the first page of every
            // shoot has been getting: manage cut at x=1030 and y=774, header columns and footer
            // buttons sliced, transparent pixels beyond. Detached, the page is its own layout root
            // and these are the only measure and arrange it gets.
            if (host.Parent is Window frame) frame.Content = null;

            // The skin goes onto the page itself, whether or not it was still on a window when it
            // got here. Styles are resolved by walking up from the element, and off the window that
            // walk ends at the page: the second and third pages of a shoot came back in Windows'
            // default controls, which looks enough like a deliberate design to be reviewed as one.
            // Merging twice is a no-op, so this is unconditional rather than guarded on the parent.
            Skin.Apply(host);

            // Detaching is not enough on its own. Measure and arrange are skipped for a subtree
            // that is not dirty, so the elements inside the page keep the layout clip the window's
            // clamped arrange put on them and the picture comes back the size asked for with the
            // page cut off inside it. Every element is marked dirty here so the passes below reach
            // all of them.
            Dirty(host);

            // Measured, laid out, and measured again, because laying it out realises rows a
            // virtualising list did not have before and those rows change how tall the page is.
            // The second pass settles it; the third is the guard.
            var height = 1;

            for (var pass = 0; pass < 3; pass++)
            {
                host.Measure(new Size(width, double.PositiveInfinity));

                var wanted = (int)Math.Ceiling(Math.Max(host.DesiredSize.Height, 1));

                host.Arrange(new Rect(0, 0, width, wanted));
                host.UpdateLayout();

                if (wanted == height) break;

                height = wanted;
            }

            // What the page came out as. A page whose desired width is under the width asked for
            // was laid out for a narrower slot than the picture is taken at, which is the fault
            // this harness has had twice; the number is in the log so the next one is one read
            // rather than another run.
            Log("  " + System.IO.Path.GetFileName(path) + ": desired "
                + host.DesiredSize.Width.ToString("0", CultureInfo.InvariantCulture) + "x"
                + host.DesiredSize.Height.ToString("0", CultureInfo.InvariantCulture)
                + " into " + width.ToString(CultureInfo.InvariantCulture) + "x"
                + height.ToString(CultureInfo.InvariantCulture));

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var file = File.Create(path)) encoder.Save(file);
        }

        /// <summary>
        /// Mark every element of a page for measure and arrange.
        ///
        /// Depth first over the visual tree, because a clip sits on whichever element was given
        /// less room than it asked for and that is rarely the root.
        /// </summary>
        /// <param name="element">The page, or one element of it.</param>
        private static void Dirty(DependencyObject element)
        {
            if (element is UIElement one)
            {
                // The clip as well as the passes. A clip set while the page was inside a clamped
                // window is not undone by arranging it in a bigger slot.
                one.Clip = null;
                one.InvalidateMeasure();
                one.InvalidateArrange();
            }

            var children = VisualTreeHelper.GetChildrenCount(element);

            for (var at = 0; at < children; at++) Dirty(VisualTreeHelper.GetChild(element, at));
        }

        private static readonly List<string> _log = new List<string>();

        internal static void Log(string line)
        {
            _log.Add(line);
            Console.WriteLine(line);
        }

        /// <summary>Let everything queued at or above background priority happen.</summary>
        internal static void Pump() =>
            Application.Current?.Dispatcher.Invoke(
                new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
}
