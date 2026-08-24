using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Project;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Help: what this is, what it will not do, and what state it is in right now.
    ///
    /// A window rather than a tab, and the one modal in the product that is not a file dialog. It
    /// has to work when the panel does not — "the pane will not open" is exactly when somebody
    /// needs the diagnostics — so it depends on as little as possible and shows what it can.
    ///
    /// <b>The limitations section is not an apology, it is documentation.</b> Every line in it is a
    /// thing that surprises somebody once and then costs them an afternoon: Ctrl+Z not undoing a
    /// property write, clash tools missing on Simulate, the temporary override layer being
    /// unreadable. Stating them where they can be found beats explaining them one support email at
    /// a time.
    /// </summary>
    public static class HelpWindow
    {
        /// <summary>Show the window.</summary>
        public static void Show()
        {
            var body = Ui.Stack(
                Ui.Card("CamelWorks",
                    "Coordination, data and delivery tools for Navisworks, from bimcamel.com.",
                    Ui.Line("Free to use, including at a commercial firm and on commercial projects. "
                            + "No seat count, no licence key.", 0.85),
                    Ui.Line("Not free to sell: you may not sell CamelWorks, or sell a product or "
                            + "service whose value comes mainly from it. Using it to do paid work for "
                            + "your own clients is using it, not selling it, and is expressly allowed.", 0.85),
                    Ui.Line("Apache 2.0 with the Commons Clause. Source-available, not open source. "
                            + "The full terms and a plain-words summary are in LICENSE; the icons and "
                            + "anything else shipped alongside are listed in NOTICE.", 0.85)),

                Ui.Card("How it is meant to be used", null,
                    Ui.Line("Open a model and press a button. Nothing has to be configured first — every "
                            + "screen derives what it needs and tells you what it derived, and Setup exists "
                            + "to correct a guess rather than to be filled in before you start.", 0.85),
                    Ui.Line("The weekly cycle is the front door: bring in the new models, run the rules "
                            + "over what that broke, send out the result.", 0.85),
                    Ui.Line("Find a tool, top right of the panel, searches what things do as well as what "
                            + "they are called. \"Why is this thing hidden\" finds Appearance.", 0.85),
                    Ui.Line("Compare reads bounding boxes by default and can read the actual triangles "
                            + "instead — the only way to see an element remodelled inside its own "
                            + "envelope. It costs a geometry pass per element, so it is a tick box on "
                            + "both the baseline and the run: a signature on one side compares against "
                            + "nothing, and Compare says so before you spend the time.", 0.85)),

                Ui.Card("What it will not do",
                    "Each of these is a real limit, not a missing feature list.",
                    Bullet("Ctrl+Z does not undo a property write. Navisworks does not put custom property "
                           + "writes on its undo stack. CamelWorks records what it changed in the project "
                           + "file instead of pretending otherwise."),
                    Bullet("Clash tools need Navisworks Manage. Simulate has no clash engine, so the board, "
                           + "the clash rules and BCF-from-clashes are unavailable there. Everything else works."),
                    Bullet("The temporary override layer cannot be read back. Navisworks offers no way to ask "
                           + "what is on it, so the Appearance stack works on the permanent layer and says so "
                           + "rather than quietly reporting half the truth."),
                    Bullet("Nothing runs unattended. A job runs when you run it, in the Navisworks you have "
                           + "open. There is no scheduler and no headless mode."),
                    Bullet("A set built from a rule that needs more than one search is saved as a fixed "
                           + "selection set, because a Navisworks search set holds one search. CamelWorks says "
                           + "which you got rather than letting you find out later."),
                    Bullet("Showing a removed element needs the old revision to still be a file. "
                           + "Compare will open it for you — the baseline records which file each "
                           + "model was loaded from — and the removed elements then paint red beside "
                           + "the current ones. What cannot be recovered is a model issued by "
                           + "overwriting one file in place: those bytes are gone, and Compare says so "
                           + "rather than looking for them. Issuing each revision into its own folder "
                           + "is what makes the difference."),
                    Bullet("A square duct turned about its own run is invisible to any comparison "
                           + "here. Shape comes from the bounding box, which decides moved, resized "
                           + "and a quarter turn about an axis; a rotation that leaves the box "
                           + "identical leaves nothing to see. Reading the actual triangles does not "
                           + "help with this one either — the triangles move with the element."),
                    Bullet("Comparing two different files needs somebody to say which model is which. "
                           + "CamelWorks pairs them by path, then by name, then by name with the revision "
                           + "marker taken off, and says on every row which rule paired it and why. Where "
                           + "two candidates fit equally well it pairs neither and asks, because guessing "
                           + "reports a whole discipline as rebuilt."),
                    Bullet("There is one icon set, drawn for a light ribbon. On a dark Navisworks theme "
                           + "the glyphs are darker than ideal; a theme-aware second set is a design "
                           + "job rather than something to guess at.")),

                Shortcuts(),
                Diagnostics(),
                SelfTestCard(),
                Bundle(),
                Sample(),
                Updates());

            var window = new Window
            {
                Title = "CamelWorks",
                Width = 720,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(14),
                    Content = body,
                },
            };

            window.ShowDialog();
        }

        private static UIElement Bullet(string text) => Ui.Line("- " + text, 0.85);

        /// <summary>
        /// The keys, and only the keys that exist.
        ///
        /// A shortcuts list that includes one the product does not have is worse than no list: it
        /// sends somebody pressing a key that does nothing and then doubting the rest of the page.
        /// Review mode is where the keys are, because that is the one screen somebody works through
        /// a hundred items on without wanting to reach for the mouse.
        /// </summary>
        private static UIElement Shortcuts() => Ui.Card("Keys",
            "In Review mode, opened from the ribbon while the board is loaded.",
            Key("N  or  →", "the next issue"),
            Key("P  or  ←", "the one before"),
            Key("1", "mark it reviewed"),
            Key("2", "mark it approved"),
            Key("3", "mark it resolved"),
            Key("0", "put it back to active"),
            Key("B", "section box around this issue, on and off"),
            Key("A", "write a note on it"),
            Ui.Note("Everywhere else, the product is buttons. Navisworks owns the keyboard while its "
                    + "own windows have focus, and a plug-in that claimed Ctrl+F or F2 for itself would "
                    + "take it away from something the host already does."));

        private static UIElement Key(string keys, string what)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            row.Children.Add(new TextBlock
            {
                Text = keys,
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
                Width = 110,
            });

            row.Children.Add(Ui.Line(what, 0.85));
            return row;
        }

        /// <summary>
        /// The contract suite, on the machine where it matters.
        ///
        /// A thousand tests run on every push and none of them run where the product actually
        /// breaks. What breaks in the field is not the logic - that is the assembly CI built - it
        /// is the environment: a locale whose decimal separator is a comma, a temp folder nobody
        /// can write to, a compression stack a security agent has replaced. Those are invisible to
        /// CI by construction and they are what a support email is about.
        /// </summary>
        private static UIElement SelfTestCard()
        {
            var output = Ui.Stack();

            var run = Ui.Button("Run the self-test", () =>
            {
                output.Children.Clear();

                var report = SelfTest.Run(Scratch());

                output.Children.Add(Ui.Line(report.Explain(), 1, true));

                foreach (var result in report.Results)
                    output.Children.Add(Ui.Line(
                        (result.Passed ? "ok    " : "FAIL  ") + result.Area + " / " + result.Name
                        + " - " + result.Detail,
                        result.Passed ? 0.75 : 1));

                output.Children.Add(report.IsClean
                    ? Ui.Line("Nothing is wrong with this installation that these checks can see.", 0.85)
                    : Ui.Problem("Quote the failing lines above in a bug report - they say which part "
                                 + "of the platform this build is not getting on with."));
            });

            return Ui.Card("Run the self-test",
                "Ten checks against the things that differ from machine to machine rather than "
                + "against the logic. Takes a moment and needs no model open.",
                Ui.Across(run),
                output);
        }

        /// <summary>
        /// Everything worth quoting, as one file.
        ///
        /// <b>It writes a file rather than sending anything.</b> Nothing in CamelWorks phones home,
        /// and a support bundle that uploaded itself would be the one place it did. What goes in it
        /// is listed on the card before the button is pressed, because a bundle whose contents are a
        /// surprise is one nobody at a firm with a security policy is allowed to send.
        /// </summary>
        private static UIElement Bundle()
        {
            var path = Ui.Text(Suggested("camelworks-support.txt"), null, 360);
            var status = Ui.Stack();

            var write = Ui.Button("Write the bundle", () =>
            {
                status.Children.Clear();

                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    status.Children.Add(Ui.Problem("Say where to write it."));
                    return;
                }

                try
                {
                    System.IO.File.WriteAllText(file, BundleText(), new System.Text.UTF8Encoding(true));

                    status.Children.Add(Ui.Line("Written to " + file + ".", 1, true));
                    status.Children.Add(Ui.Line("Open it and read it before you send it.", 0.7));
                }
                catch (Exception e)
                {
                    status.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                }
            });

            return Ui.Card("Copy a support bundle",
                "One text file with everything a bug report needs.",
                Ui.Line("What goes in it: the build number, this machine's Navisworks version and "
                        + "culture, the diagnostics above, the self-test result, and the last twenty "
                        + "lines of this project's activity log.", 0.85),
                Ui.Line("What does not: any part of your model, any property value, any element, and "
                        + "the contents of no file. Model and project file paths are in it, because "
                        + "half the faults worth reporting are about where a file is.", 0.85),
                Ui.Note("It writes a file and sends nothing. Nothing in CamelWorks contacts a server, "
                        + "and a support bundle that uploaded itself would be the one place it did."),
                Ui.Field("Write to", path),
                Ui.Across(write),
                status);
        }

        /// <summary>The bundle's contents, assembled the same way every time.</summary>
        private static string BundleText()
        {
            var lines = new List<string>
            {
                "CamelWorks support bundle",
                "=========================",
                string.Empty,
                "Build            " + Ui.Release,
                "Navisworks       " + NavisworksVersion(),
                "Installed in     " + Folder(Assembly.GetExecutingAssembly()),
                "Culture          " + CultureInfo.CurrentCulture.Name
                                    + " (numbers read as \"" + (1234.5).ToString("0.#") + "\")",
                "OS               " + Environment.OSVersion,
                ".NET             " + Environment.Version,
                string.Empty,
            };

            var session = Host.Current;

            if (session == null)
            {
                lines.Add("Document         nothing open");
            }
            else
            {
                lines.Add("Document         " + (session.SavedPath ?? "never saved"));
                lines.Add("Models           " + session.Model.Models.Count.ToString(CultureInfo.InvariantCulture));
                lines.Add("Units            " + session.Document.Units);
                lines.Add("Project file     " + (session.Store.Path ?? "memory only"));
                lines.Add("Stored on        " + session.Store.Substrate);
                lines.Add("Project state    " + session.Store.Outcome.ToString().ToLowerInvariant()
                                              + (session.Store.UsedBackup ? ", recovered from backup" : string.Empty));

                lines.Add("Clash engine     " + (session.Clash.IsAvailable
                    ? session.Clash.Tests().Count.ToString(CultureInfo.InvariantCulture) + " tests"
                    : session.ClashProblem ?? "unavailable"));
            }

            lines.Add(string.Empty);
            lines.Add(SelfTest.Run(Scratch()).ToText());

            if (session != null)
            {
                lines.Add(string.Empty);
                lines.Add("Recent activity");
                lines.Add("---------------");

                // Summaries only, and only twenty. An activity line says "took off volume by
                // category: 412 elements" - what was done, never what it was done to.
                foreach (var entry in session.Store.Activity.Entries.Take(20))
                    lines.Add(ActivityLog.Ago(entry.WhenTicks, session.NowTicks) + "  " + entry.Summary);
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// The sample project, written where somebody can open it.
        /// </summary>
        private static UIElement Sample()
        {
            var path = Ui.Text(Suggested(SampleProject.FileName), null, 360);
            var status = Ui.Stack();

            var write = Ui.Button("Write the sample project", () =>
            {
                status.Children.Clear();

                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    status.Children.Add(Ui.Problem("Say where to write it."));
                    return;
                }

                try
                {
                    System.IO.File.WriteAllText(file, SampleProject.Write(DateTime.UtcNow.Ticks));

                    // The note beside it, so the file is not an unexplained blob of JSON in
                    // somebody's Documents folder a fortnight later.
                    var note = System.IO.Path.ChangeExtension(file, ".txt");
                    System.IO.File.WriteAllText(note, SampleProject.ReadMe());

                    status.Children.Add(Ui.Line("Written to " + file + ", with a note beside it.", 1, true));
                }
                catch (Exception e)
                {
                    status.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                }
            });

            var body = Ui.Stack();

            body.Children.Add(Ui.Line("Every screen works with nothing set up, which is the point of "
                + "the product and also the reason nobody ever sees what a set-up project looks like. "
                + "This is one, on a made-up job.", 0.85));

            foreach (var line in SampleProject.Explains) body.Children.Add(Ui.Line("- " + line, 0.8));

            body.Children.Add(Ui.Field("Write to", path));
            body.Children.Add(Ui.Across(write));
            body.Children.Add(status);

            return Ui.Card("A worked example project", "For reading, not for working in.", body);
        }

        /// <summary>
        /// Which build this is, and where a newer one lives.
        ///
        /// <b>It does not check.</b> A plug-in that reaches the internet on startup is one a firm's
        /// security team has to have an opinion about, and the opinion is usually no. The version is
        /// here and the address is here; looking is a decision somebody makes.
        /// </summary>
        private static UIElement Updates() => Ui.Card("Updates",
            "There is no update check, and there is no telemetry.",
            Ui.Field("This build", Ui.Line(Ui.Release, 0.85)),
            Ui.Field("Newer ones", Ui.Line("bimcamel.com", 0.85)),
            Ui.Line("Nothing in CamelWorks contacts a server - not to check a version, not to count "
                    + "an install, not to report a crash. That is a deliberate limit and it has a "
                    + "cost: you find out about a new build by looking.", 0.85),
            Ui.Line("A newer build reads a project file this one wrote. An older build shown a file "
                    + "from a newer one opens it read-only and says so, rather than writing a version "
                    + "back over settings it does not understand.", 0.85));

        /// <summary>A path in the user's own folder, so the box is never empty.</summary>
        private static string Suggested(string file)
        {
            try
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                return string.IsNullOrEmpty(folder) ? file : System.IO.Path.Combine(folder, file);
            }
            catch (Exception)
            {
                return file;
            }
        }

        /// <summary>Somewhere the self-test may write, or null when there is nowhere.</summary>
        private static string? Scratch()
        {
            try { return System.IO.Path.GetTempPath(); }
            catch (Exception) { return null; }
        }

        private static UIElement Diagnostics()
        {
            var rows = Ui.Stack();

            void Say(string label, string value) => rows.Children.Add(Ui.Field(label, Ui.Line(value, 0.85)));

            var assembly = Assembly.GetExecutingAssembly();

            Say("Build", assembly.GetName().Version?.ToString() ?? "unknown");
            Say("Installed in", Folder(assembly));
            Say("Navisworks", NavisworksVersion());

            var session = Host.Current;

            if (session == null)
            {
                Say("Document", "nothing open");
                return Ui.Card("Diagnostics", "What to quote in a bug report.", rows);
            }

            Say("Document", session.SavedPath ?? "never saved");
            Say("Models", session.Model.Models.Count.ToString(CultureInfo.InvariantCulture));
            Say("Units", session.Document.Units + " (1 unit = "
                         + NavUnits.MetresPerUnit(session.Document.Units)
                             .ToString("0.######", CultureInfo.InvariantCulture) + " m)");

            Say("Project file", session.Store.Path ?? "memory only");
            Say("Stored on", session.Store.Substrate.ToString());
            Say("Project file state", session.Store.Outcome.ToString().ToLowerInvariant()
                                      + (session.Store.UsedBackup ? ", recovered from backup" : string.Empty));

            Say("Clash engine", session.Clash.IsAvailable
                ? session.Clash.Tests().Count.ToString(CultureInfo.InvariantCulture) + " tests"
                : session.ClashProblem ?? "unavailable");

            return Ui.Card("Diagnostics", "What to quote in a bug report.", rows);
        }

        private static string Folder(Assembly assembly)
        {
            try
            {
                return System.IO.Path.GetDirectoryName(assembly.Location) ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string NavisworksVersion()
        {
            try
            {
                var api = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Autodesk.Navisworks.Api");

                var version = api?.GetName().Version;

                // The API's major version is not the year: 21 is 2024, and each year since adds one.
                // Worth translating, because the number people need in a bug report is the year.
                return version == null
                    ? "unknown"
                    : "API v" + version.Major.ToString(CultureInfo.InvariantCulture)
                      + " (" + (version.Major + 2003).ToString(CultureInfo.InvariantCulture) + ")";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
