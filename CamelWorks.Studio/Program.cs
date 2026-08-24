using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// </summary>
    public static class Program
    {
        /// <summary>The widths worth looking at: the pane's minimum, its default, and docked wide.</summary>
        private static readonly int[] Widths = { 470, 640, 900 };

        /// <summary>Run the harness.</summary>
        /// <param name="args">Pass <c>--shoot &lt;folder&gt;</c> to write PNGs instead of opening a window.</param>
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
                return Shoot(folder);
            }

            return application.Run(new StudioWindow());
        }

        /// <summary>
        /// Render every sheet, in both themes, at every width, and write them out.
        ///
        /// This is the part that makes the harness worth having on a build agent rather than only
        /// on a desk: the pictures come back from the machine that compiled the change, so a
        /// layout can be reviewed by whoever wrote it without a Navisworks licence anywhere.
        /// </summary>
        /// <param name="folder">Where to write the PNGs.</param>
        private static int Shoot(string folder)
        {
            Directory.CreateDirectory(folder);

            var written = 0;

            foreach (var dark in new[] { false, true })
            {
                Skin.Mode = dark ? SkinMode.Dark : SkinMode.Light;
                var suffix = dark ? "dark" : "light";

                foreach (var sheet in Sheets.All)
                {
                    foreach (var width in sheet.Widths ?? Widths)
                    {
                        var name = sheet.Id + "-" + width.ToString(CultureInfo.InvariantCulture) + "-" + suffix + ".png";
                        var path = System.IO.Path.Combine(folder, name);

                        try
                        {
                            Capture(sheet, width, path);
                            written++;
                            Log("wrote " + name);
                        }
                        catch (Exception e)
                        {
                            // One sheet that cannot be rendered must not cost the other fourteen.
                            Log("FAILED " + name + ": " + e);
                        }
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

            // A build agent is not guaranteed a desktop to put a window on. Showing one is worth
            // trying, because parts of WPF only behave once there is a presentation source, but it
            // is not worth failing over: measure and arrange work either way.
            var shown = false;

            try
            {
                window.Show();
                shown = true;
            }
            catch (Exception e)
            {
                Log("no window (" + e.GetType().Name + "), laying out headless");
            }

            // Two layout passes with a pump between them. The first generates the item containers
            // a ListView only creates once it has been measured; the second lays out what that
            // generation added. One pass gives tables with headers and no rows.
            window.UpdateLayout();
            Pump();
            window.UpdateLayout();
            Pump();

            // A page that takes its own picture has done so by now; one that does not gets taken
            // here, settled.
            Shoot();

            if (shown) window.Close();
        }

        /// <summary>Write one laid-out element to a PNG.</summary>
        /// <param name="host">The frame around the page.</param>
        /// <param name="width">How wide the pane is.</param>
        /// <param name="path">Where to write it.</param>
        private static void Snap(Border host, int width, string path)
        {
            host.Measure(new Size(width, double.PositiveInfinity));

            var height = (int)Math.Ceiling(Math.Max(host.ActualHeight, Math.Max(host.DesiredSize.Height, 1)));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var file = File.Create(path)) encoder.Save(file);
        }

        private static readonly List<string> _log = new List<string>();

        private static void Log(string line)
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
