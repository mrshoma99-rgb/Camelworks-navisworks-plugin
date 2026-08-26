using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The board as the file everyone else reads, and the saved decision about what goes in it.
    ///
    /// A report is a deliverable; this is a baseline. It is what a reviewer opens in their own tool,
    /// works through and sends back, and it is the only clash format the industry has agreed on.
    ///
    /// A window rather than a card because the question is a grid: twenty tests, each with a tick,
    /// a count and a date it last went out, beside a set of filters. That is the shape of the
    /// decision, and it is the shape a coordinator recognises from every other tool that asks it.
    /// </summary>
    public static class ClashExportWindow
    {
        /// <summary>Where the profiles live in the project file.</summary>
        public const string Key = "exportProfiles";

        /// <summary>What a profile file is called on disk.</summary>
        public const string Extension = ".cwexport";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Editor(session).View;

        private sealed class Editor
        {
            private readonly Session _session;

            private readonly List<ExportProfile> _profiles = new List<ExportProfile>();

            private const string WriteOne = "Write one";
            private const string ReadOne = "Read one back";

            private readonly StackPanel _tests = Ui.Stack();
            private readonly StackPanel _filters = Ui.Stack();
            private readonly StackPanel _match = Ui.Stack();
            private readonly StackPanel _kept = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();
            private readonly StackPanel _back = Ui.Stack();

            private readonly TextBox _file = Ui.Text(string.Empty, "where to write it", null, 240);
            private readonly TextBox _incoming = Ui.Text(string.Empty, "a file to read", null, 240);
            private readonly TextBox _name = Ui.Text("Everything", null, 200);

            private ExportProfile _profile = new ExportProfile();
            private ComboBox? _picker;

            internal Editor(Session session)
            {
                _session = session;

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // The board
            // -----------------------------------------------------------------------------------

            private IReadOnlyList<ClashGroup> Board() =>
                CoordinateViews.LastBoard?.Groups ?? Array.Empty<ClashGroup>();

            private IReadOnlyList<ClashItem> Results() =>
                Board().SelectMany(g => g.Items).ToList();

            /// <summary>Every party the board mentions, so the filter is a list rather than a box.</summary>
            private IReadOnlyList<string> Parties() =>
                Board().Select(g => g.AssignedTo)
                       .Where(p => !string.IsNullOrWhiteSpace(p))
                       .Select(p => p!.Trim())
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                       .ToList();

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Two legs, one on screen at a time, each built to the surface anatomy.
            ///
            /// <b>Not a wizard.</b> Writing a file out and reading a reviewer's file back are the
            /// same trip a fortnight apart, not two steps of one task. The switch at the top of
            /// each setup column says which one you are on, and swapping it swaps the whole
            /// surface — the two legs share nothing but the switch's caption.
            ///
            /// The write leg's primary is the two-stage Commit, so it leads the stage and the
            /// setup column has no foot; the read leg's Runner is an ordinary foot action.
            /// </summary>
            private UIElement Build()
            {
                var writeSetup = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Segmented(new[] { WriteOne, ReadOne }, WriteOne, Leg)),
                    Bench.Group("The tests", _match, _tests),
                    Bench.Group("What goes in", _filters),
                    Bench.Group("How it is written", Shape()),
                    Bench.Group("This profile", Profiles()),
                    Bench.Group("The file",
                        Ui.PathField("File", _file, PathKind.Save, "Clash XML|*.xml",
                                     "The format Navisworks and every reviewing tool reads.")));

                var writeStage = Ui.Stack(
                    _kept,
                    Ui.Commit("Show me what would go in", "Write", "result", Rehearse, Publish),
                    _output);

                _write = Bench.Surface(writeSetup, null, writeStage);

                var readSetup = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Segmented(new[] { WriteOne, ReadOne }, ReadOne, Leg)),
                    Bench.Group("The file",
                        Ui.PathField("File", _incoming, PathKind.Open, "Clash XML|*.xml",
                                     "A reviewer's file, returned.")));

                // The Runner clears its own panel on the way in, so the idle sentence seeded here
                // lasts exactly until the first read lands where it stood.
                _back.Children.Add(Bench.Ready(
                    "Reads the returned file and says what came back.",
                    "Results carrying a CamelWorks key land on the elements they left from; "
                    + "nothing on the board changes."));

                _read = Bench.Surface(readSetup,
                                      Ui.Runner("Read it", _back, Receive),
                                      Ui.Stack(_back));

                DrawFilters();
                DrawTests();

                _half.Content = _write;
                return _half;
            }

            /// <summary>The holder the leg switch swaps whole surfaces in and out of.</summary>
            private readonly ContentControl _half = new ContentControl();

            private UIElement? _write;
            private UIElement? _read;

            /// <summary>Put the chosen leg's surface on show, detaching the other first.</summary>
            private void Leg(string leg)
            {
                // Detached before the other goes in, or the half being left is still claimed
                // by this holder when it is handed back to it.
                _half.Content = null;
                _half.Content = leg == ReadOne ? _read : _write;
            }

            private UIElement Profiles()
            {
                var names = _profiles.Select(p => p.Name).ToList();

                if (names.Count == 0) names.Add(_profile.Name);

                _picker = Ui.Choice(names, _profile.Name, Switch, 220);

                return Ui.Stack(
                    Ui.Field("Saved", _picker),
                    Ui.Field("Called", _name),
                    Ui.Across(
                        Ui.Quiet("Keep", Keep),
                        Ui.Danger("Forget", Forget),
                        Ui.Quiet("Save the profile to a file...", ToFile),
                        Ui.Quiet("Load a profile from a file...", FromFile)),
                    _note);
            }

            private UIElement Shape()
            {
                var units = Ui.Choice(new[] { "m", "mm", "cm", "ft", "in" }, _profile.Units,
                                      pick => _profile.Units = pick, 100);

                return Ui.Stack(
                    Ui.Check("Keep the folders as folders", _profile.KeepGroups,
                             on => _profile.KeepGroups = on),
                    Ui.Check("Write each result's two elements", _profile.IncludeObjects,
                             on => _profile.IncludeObjects = on),
                    Ui.Field("Model units", units,
                             "Declares what the numbers already are; converts nothing."));
            }

            // -----------------------------------------------------------------------------------
            // Filters
            // -----------------------------------------------------------------------------------

            private void DrawFilters()
            {
                _filters.Children.Clear();

                _filters.Children.Add(Ui.Line("Statuses", 0.8, true));

                foreach (var status in new[]
                {
                    FindingStatus.New, FindingStatus.Active, FindingStatus.Reviewed,
                    FindingStatus.Approved, FindingStatus.Resolved,
                })
                {
                    var here = status;

                    _filters.Children.Add(Ui.Check(here.ToString(), _profile.Statuses.Contains(here),
                        on =>
                        {
                            if (on) _profile.Statuses.Add(here); else _profile.Statuses.Remove(here);
                            ShowKept();
                        }));
                }

                _filters.Children.Add(Ui.Small("Nothing ticked means every status."));

                var parties = Parties();

                _filters.Children.Add(Ui.Line("Parties", 0.8, true));

                if (parties.Count == 0)
                {
                    _filters.Children.Add(Ui.Small("Nothing on the board is assigned yet."));
                }
                else
                {
                    foreach (var party in parties)
                    {
                        var here = party;

                        _filters.Children.Add(Ui.Check(here, _profile.Parties.Contains(here),
                            on =>
                            {
                                if (on) _profile.Parties.Add(here); else _profile.Parties.Remove(here);
                                ShowKept();
                            }));
                    }

                    _filters.Children.Add(Ui.Small("An unassigned result goes into every party's file."));
                }

                ShowKept();
            }

            // -----------------------------------------------------------------------------------
            // The tests
            // -----------------------------------------------------------------------------------

            private void DrawTests()
            {
                _tests.Children.Clear();
                _match.Children.Clear();

                var source = _session.Clash;

                if (!source.IsAvailable)
                {
                    _match.Children.Add(Ui.Problem(_session.ClashProblem
                        ?? "Clash Detective is not available in this Navisworks edition."));
                    return;
                }

                var refs = source.Tests()
                                 .Select(t => new ClashTestRef(t.Name, t.Id, t.ResultCount))
                                 .ToList();

                var match = _profile.Match(refs);

                _match.Children.Add(Ui.Line(match.Explain(), 1, true));

                foreach (var renamed in match.Renamed.Take(6))
                    _match.Children.Add(Ui.Note(renamed.ToString()
                        + " - still chosen, since it is the same test. A name usually changes "
                        + "because a scope did, so it is worth a look."));

                foreach (var gone in match.Missing.Take(6))
                    _match.Children.Add(Ui.Note("\"" + gone.Name + "\" is not in this document. Kept "
                        + "in the profile, in case it is a document somebody opened without running."));

                // Built by hand rather than ToDictionary: two tests may share a name, and the
                // second one would throw rather than shrug.
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var one in refs) counts[one.Name] = one.Results;

                foreach (var test in _profile.Tests)
                {
                    var here = test;

                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                    row.Children.Add(Ui.Check(string.Empty, here.Chosen, on =>
                    {
                        here.Chosen = on;
                        ShowKept();
                    }));

                    row.Children.Add(Ui.Line(here.Name, 1, true));

                    row.Children.Add(counts.TryGetValue(here.Name, out var many)
                        ? Ui.Pill(Ui.Count(many, "result"), Tone.Plain)
                        : Ui.Pill("not in this document", Tone.Warn));

                    row.Children.Add(Ui.Line("last sent " + ExportProfile.When(here), 0.65));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 8, 0);

                    _tests.Children.Add(row);
                }

                if (_profile.Tests.Count == 0)
                    _tests.Children.Add(Ui.Empty("No clash tests in this document.",
                        "Build a matrix or make a test in Clash Detective, then come back."));
                else
                    _tests.Children.Add(Ui.Across(
                        Ui.Quiet("Tick every test", () => Tick(true)),
                        Ui.Quiet("Untick every test", () => Tick(false))));

                ShowKept();
            }

            private void Tick(bool on)
            {
                foreach (var test in _profile.Tests) test.Chosen = on;

                DrawTests();
            }

            /// <summary>
            /// The one-line answer to "what is actually going", kept under the filters.
            ///
            /// Recomputed on every tick rather than at the press, because the number is the whole
            /// point of the filters and finding it out afterwards is finding it out too late.
            /// </summary>
            private void ShowKept()
            {
                _kept.Children.Clear();

                var results = Results();

                if (results.Count == 0)
                {
                    _kept.Children.Add(Ui.Note("The board has not been built yet. Open Clash Triage "
                        + "and run it, then everything here has something to count."));
                    return;
                }

                _kept.Children.Add(Ui.Line(_profile.Choose(results, Board()).Explain(), 0.8));
            }

            // -----------------------------------------------------------------------------------
            // Writing
            // -----------------------------------------------------------------------------------

            private int Rehearse(Job job, Panel into)
            {
                var results = Results();

                if (results.Count == 0)
                {
                    into.Children.Add(Ui.Problem("The board has not been built yet. Open Clash Triage "
                        + "and run it first - this writes what the board holds, not what the host "
                        + "happens to have open."));
                    return 0;
                }

                job.Say("Working out what would go in...");

                var picked = _profile.Choose(results, Board());

                into.Children.Add(Ui.Line(picked.Explain(), 1, true));

                if (!picked.IsUsable) return 0;

                var written = ClashXml.Write(picked.Items, picked.Groups, _profile.Options());

                foreach (var warning in written.Warnings) into.Children.Add(Ui.Note(warning));

                var rows = written.Tests
                    .Select(t => new TableRow(t, t.Name, t.Kind.ToString(),
                                              t.Total.ToString("N0", CultureInfo.InvariantCulture),
                                              t.Groups.ToString("N0", CultureInfo.InvariantCulture)))
                    .ToList();

                var going = Ui.Table(rows,
                    ("Test", 280), ("As", 100), ("Results", 90), ("Folders", 80));

                into.Children.Add(Ui.Picking(going,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(going, picked), say, "test"))));

                return picked.Items.Count;
            }

            private void Publish(Job job)
            {
                var path = _file.Text.Trim();

                if (path.Length == 0)
                {
                    _output.Children.Add(Ui.Problem("Say where to write it."));
                    return;
                }

                var picked = _profile.Choose(Results(), Board());

                if (!picked.IsUsable)
                {
                    _output.Children.Add(Ui.Problem(picked.Explain()));
                    return;
                }

                job.Say("Writing the file...");

                var written = ClashXml.Write(picked.Items, picked.Groups,
                                             _profile.Options(_session.SavedPath, _session.NowTicks));

                try
                {
                    File.WriteAllText(path, written.Text);
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _output.Children.Add(Ui.Done("Wrote " + Ui.Count(written.Results, "result")
                    + " across " + Ui.Count(written.Tests.Count, "test") + " to " + path + "."));

                // The date each test last went out, which is what turns a list of tests into a work
                // list: which went last Friday, and which have never gone at all.
                _profile.Stamp(written.Tests.Select(t => t.Name), _session.NowTicks);

                Keep();
                DrawTests();

                _session.Record(ActivityKind.Exchange,
                    "wrote " + Ui.Count(written.Results, "clash result") + " to " + path);
            }

            // -----------------------------------------------------------------------------------
            // Reading one back
            // -----------------------------------------------------------------------------------

            private void Receive(Job job)
            {
                _back.Children.Clear();

                var path = _incoming.Text.Trim();

                if (path.Length == 0)
                {
                    _back.Children.Add(Ui.Problem("Say which file to read."));
                    return;
                }

                if (!File.Exists(path))
                {
                    _back.Children.Add(Ui.Problem("There is no file at " + path + "."));
                    return;
                }

                job.Say("Reading the file...");

                string text;

                try
                {
                    text = File.ReadAllText(path);
                }
                catch (Exception e)
                {
                    _back.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                var read = ClashXml.Read(text);

                if (read.Problem != null)
                {
                    _back.Children.Add(Ui.Problem(read.Problem));
                    return;
                }

                _back.Children.Add(Ui.Line(read.Explain(), 1, true));

                foreach (var warning in read.Warnings) _back.Children.Add(Ui.Note(warning));

                var rows = read.Tests
                    .Select(t => new TableRow(t, t.Name, t.Kind.ToString(),
                                              t.Total.ToString("N0", CultureInfo.InvariantCulture),
                                              Counted(t, FindingStatus.Resolved)))
                    .ToList();

                var came = Ui.Table(rows,
                    ("Test", 280), ("As", 100), ("Results", 90), ("Resolved", 90));

                _back.Children.Add(Ui.Picking(came,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(came, picked), say, "test"))));

                // Read, not applied. Carrying a reviewer's statuses onto the board is the board's
                // decision to make, on the board, where the before and after are both visible.
                _back.Children.Add(Ui.Note("Nothing on the board has changed. This says what came "
                    + "back; taking it on is a decision the board makes, where what it would change "
                    + "is visible next to what it would change from."));
            }

            private static string Counted(ClashXmlTest test, FindingStatus status) =>
                test.Counts.TryGetValue(status, out var many)
                    ? many.ToString("N0", CultureInfo.InvariantCulture)
                    : "0";

            // -----------------------------------------------------------------------------------
            // Profiles in the project file, and on disk
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                var stored = _session.Store.Section(ProjectStore.ClashSection)[Key];

                foreach (var item in stored.Items) _profiles.Add(ExportProfile.FromJson(item));

                // Zero setup: a project that has never exported opens on a profile that sends
                // everything, which is what a first export is.
                _profile = _profiles.Count > 0 ? _profiles[0] : new ExportProfile();

                _name.Text = _profile.Name;
            }

            private void Switch(string name)
            {
                var found = _profiles.FirstOrDefault(
                    p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                if (found == null) return;

                _profile = found;
                _name.Text = found.Name;

                DrawFilters();
                DrawTests();
            }

            private void Keep()
            {
                _note.Children.Clear();

                var named = _name.Text.Trim();

                if (named.Length > 0) _profile.Name = named;

                if (!_profiles.Contains(_profile)) _profiles.Add(_profile);

                Write();

                _note.Children.Add(_session.Store.Save()
                    ? Ui.Done("Kept as \"" + _profile.Name + "\".")
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));

                Repick();
            }

            private void Forget()
            {
                _note.Children.Clear();

                _profiles.Remove(_profile);
                _profile = _profiles.Count > 0 ? _profiles[0] : new ExportProfile();
                _name.Text = _profile.Name;

                Write();
                _session.Store.Save();

                Repick();
                DrawFilters();
                DrawTests();
            }

            private void Write()
            {
                _session.Store.Section(ProjectStore.ClashSection)
                        .Set(Key, JsonValue.Array(_profiles.Select(p => p.ToJson())));
            }

            /// <summary>Refill the picker, since its list is the profiles and they have changed.</summary>
            private void Repick()
            {
                var picker = _picker;
                if (picker == null) return;

                picker.Items.Clear();

                foreach (var profile in _profiles) picker.Items.Add(profile.Name);

                if (_profiles.Count == 0) picker.Items.Add(_profile.Name);

                picker.SelectedItem = _profile.Name;
            }

            /// <summary>
            /// The profile as a file, so it travels between projects.
            ///
            /// The project file is the right home for it - it goes with the job - but a company
            /// that runs the same weekly issue on nine jobs wants to write it once.
            /// </summary>
            private void ToFile()
            {
                _note.Children.Clear();

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the export profile",
                    Filter = "CamelWorks export profile|*" + Extension,
                    FileName = _profile.Name + Extension,
                    AddExtension = true,
                    DefaultExt = Extension,
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    File.WriteAllText(dialog.FileName, _profile.ToJson().ToJson());
                    _note.Children.Add(Ui.Done("Wrote " + dialog.FileName));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                }
            }

            private void FromFile()
            {
                _note.Children.Clear();

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Open an export profile",
                    Filter = "CamelWorks export profile|*" + Extension + "|All files|*.*",
                    CheckFileExists = true,
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    _profile = ExportProfile.FromJson(JsonReader.Parse(File.ReadAllText(dialog.FileName)));
                }
                catch (Exception e)
                {
                    _note.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                _profiles.Add(_profile);
                _name.Text = _profile.Name;

                _note.Children.Add(Ui.Done("Loaded \"" + _profile.Name + "\". Keep it to hold it "
                    + "in this project."));

                Repick();
                DrawFilters();
                DrawTests();
            }
        }
    }
}
