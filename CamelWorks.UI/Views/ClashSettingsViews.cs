using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.People;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Measurement;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The settings the whole Clash Manager reads, in one tab.
    ///
    /// Five subjects had no home: the storeys, the roster, the model-to-discipline mapping, the
    /// test naming, and the project's units and grouping distances. Each was a window or a panel
    /// hanging off the Manage board, and each is read by more than one tab — banding and the
    /// clearance scope read the storeys, assignment and the per-trade export split read the
    /// roster, the matrix and the exports read the disciplines. A setting one tab owns stays on
    /// that tab: the matrix's grid and fields, Manage's columns and banding, Exchange's export
    /// profiles, and each test's own settings on Create.
    ///
    /// A rail of sections with a pane beside it rather than one scroll. The five subjects are
    /// unrelated to each other, so stacked they are two thousand pixels of scroll with no index;
    /// the rail names all five at once, which is what "where does this live" needs answering
    /// with. It is the same shape <see cref="Ui.Configure"/> gives a settings window, so the tab
    /// reads as the product rather than as a new pattern.
    ///
    /// Edits are held and written by the one action at the foot. The renumber pane is the
    /// exception and says so: it rewrites the names of the tests in the document, not the project
    /// file, so it carries its own action beside the plan it would apply.
    /// </summary>
    public static class ClashSettingsViews
    {
        /// <summary>Which section of the tab is open.</summary>
        public enum Part
        {
            /// <summary>Lengths, and the two distances the board groups by.</summary>
            Units = 0,

            /// <summary>The storeys the project declares.</summary>
            Levels = 1,

            /// <summary>Which discipline each loaded model is.</summary>
            Disciplines = 2,

            /// <summary>The people on the job.</summary>
            Team = 3,

            /// <summary>The fields, the patterns, and which pattern reads which test.</summary>
            Naming = 4,
        }

        /// <summary>The tab.</summary>
        /// <param name="seat">The seat this is running in.</param>
        public static UIElement Settings(ISeat seat) => Drawn(seat, Part.Units);

        /// <summary>
        /// The tab opened on one section, with the project data supplied, for the render harness.
        ///
        /// The empty seat's project file holds no storeys, no naming and no tests, so without
        /// these the only state the harness could draw is the empty one.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="on">Which section to open on.</param>
        /// <param name="plan">The storeys, or null to read the project's.</param>
        /// <param name="book">The naming, or null to read the project's.</param>
        /// <param name="tests">The tests, or null to read the document's.</param>
        public static UIElement Drawn(ISeat seat, Part on, LevelPlan? plan = null,
                                      NamingBook? book = null,
                                      IReadOnlyList<(string Id, string Name)>? tests = null) =>
            new Screen(seat, plan, book, tests).Build(on);

        /// <summary>
        /// A section's title, its one line of orientation, and the rest of the explanation behind
        /// a press.
        ///
        /// One heading size and weight, sentence case. The longer text is the material that used
        /// to sit as standing prose between the last field and the action.
        /// </summary>
        /// <param name="title">The section's name.</param>
        /// <param name="says">One sentence. Not a paragraph.</param>
        /// <param name="help">Everything else, shown only when asked for.</param>
        internal static UIElement Head(string title, string says, string? help)
        {
            var held = Ui.Stack();
            var shown = Ui.Stack();

            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 2) };

            if (help != null)
            {
                var open = false;

                var press = Ui.Icon(Glyphs.Info, "What these settings do", () =>
                {
                    open = !open;
                    shown.Children.Clear();

                    if (!open) return;

                    var note = Ui.Note(help);

                    if (note is FrameworkElement wide) wide.MaxWidth = TextWidth;

                    shown.Children.Add(note);
                });

                press.Margin = new Thickness(6, 0, 0, 0);

                DockPanel.SetDock(press, Dock.Right);
                line.Children.Add(press);
            }

            var head = Ui.Heading(title);

            head.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(head);

            held.Children.Add(line);

            var sub = Ui.Sub(says);

            sub.MaxWidth = TextWidth;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            sub.Margin = new Thickness(0, 0, 0, 10);

            held.Children.Add(sub);
            held.Children.Add(shown);

            return held;
        }

        /// <summary>How wide a column of prose is allowed to run: about 75 characters.</summary>
        internal const double TextWidth = 560;

        /// <summary>
        /// How wide a line of fields is allowed to run: two fields of about 200 and the gap.
        ///
        /// A field stretched to the width of the tab is a box a hundred characters long holding
        /// a number of metres.
        /// </summary>
        internal const double FieldWidth = 440;

        /// <summary>What a discipline reads as when a model has none.</summary>
        private const string NoDiscipline = "Not set";

        /// <summary>Where the roster is kept inside the project's profile section.</summary>
        private const string RosterKey = "roster";

        /// <summary>The tab, and the five sections behind its rail.</summary>
        private sealed class Screen
        {
            private readonly ISeat _seat;
            private readonly LevelPlan? _given;
            private readonly NamingBook? _bookGiven;
            private readonly IReadOnlyList<(string Id, string Name)>? _testsGiven;

            // Held because the rail replaces what is in them: the pane on every section change,
            // the save state on every edit.
            private readonly ContentControl _pane = new ContentControl();
            private readonly StackPanel _state = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            private readonly List<System.Windows.Controls.Button> _tabs =
                new List<System.Windows.Controls.Button>();

            // The project's own data, read once and edited in place. Written by Keep, which is the
            // tab's one action.
            private LevelPlan? _plan;
            private NamingBook? _book;
            private TeamRoster? _roster;

            // Profile overrides typed here but not yet written. Held apart from the profile itself
            // so a section that was never opened cannot rewrite a key it did not touch.
            private readonly Dictionary<string, string?> _changed =
                new Dictionary<string, string?>(StringComparer.Ordinal);

            private bool _dirty;
            private Part _on = Part.Units;

            internal Screen(ISeat seat, LevelPlan? plan = null, NamingBook? book = null,
                            IReadOnlyList<(string Id, string Name)>? tests = null)
            {
                _seat = seat;
                _given = plan;
                _bookGiven = book;
                _testsGiven = tests;
            }

            /// <summary>Build the tab, open on one section.</summary>
            /// <param name="on">Which section.</param>
            internal UIElement Build(Part on)
            {
                _on = on;

                var rail = new StackPanel { Margin = new Thickness(8, 10, 8, 10) };

                foreach (var part in new[]
                         {
                             Part.Units, Part.Levels, Part.Disciplines, Part.Team, Part.Naming,
                         })
                {
                    var here = part;

                    var row = new System.Windows.Controls.Button
                    {
                        Content = Titled(here),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 7, 10, 7),
                        Margin = new Thickness(0, 0, 0, 2),
                    };

                    row.Click += (s, e) => Ui.Safely(() => Show(here));

                    _tabs.Add(row);
                    rail.Children.Add(row);
                }

                var side = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = rail,
                    },
                };

                side.Tint(Border.BackgroundProperty, "Pane");
                side.Tint(Border.BorderBrushProperty, "Bd");

                var body = new Grid();

                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                body.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                body.Children.Add(side);

                var right = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(18, 14, 18, 16),
                    Content = Ui.Loose(_pane),
                };

                Grid.SetColumn(right, 1);
                body.Children.Add(right);

                Show(on);

                return Bench.Surface(null, Foot(), body, 0, scrollStage: false);
            }

            /// <summary>How a section reads on the rail.</summary>
            /// <param name="part">The section.</param>
            private static string Titled(Part part) => part switch
            {
                Part.Levels => "Levels",
                Part.Disciplines => "Disciplines",
                Part.Team => "Team",
                Part.Naming => "Test naming",
                _ => "Units and distances",
            };

            /// <summary>Draw one section, and mark it on the rail.</summary>
            /// <param name="part">The section.</param>
            private void Show(Part part)
            {
                _on = part;

                for (var at = 0; at < _tabs.Count; at++)
                    _tabs[at].Wearing(at == (int)part ? "RailRowOn" : "RailRow");

                try
                {
                    _pane.Content = part switch
                    {
                        Part.Levels => LevelsPart(),
                        Part.Disciplines => DisciplinesPart(),
                        Part.Team => TeamPart(),
                        Part.Naming => NamingPart(),
                        _ => UnitsPart(),
                    };
                }
                catch (Exception e)
                {
                    _pane.Content = Ui.Problem("This section could not be opened: "
                                               + Ui.Describe(e));
                }
            }

            // ---------------------------------------------------------------------------------
            // The action, and what it writes
            // ---------------------------------------------------------------------------------

            /// <summary>The foot: what is unsaved, and the one action that writes it.</summary>
            private UIElement Foot()
            {
                Said();

                return Ui.Across(Ui.Loose(_state),
                                 Ui.Primary("Save the settings", () => Ui.Safely(Keep)));
            }

            /// <summary>Note that something was edited, and say so beside the action.</summary>
            private void Touched()
            {
                _dirty = true;
                Said();
            }

            /// <summary>What the foot says about the state of the project file.</summary>
            private void Said()
            {
                _state.Children.Clear();

                if (!_dirty) return;

                var line = Ui.Small("Not saved yet");

                line.VerticalAlignment = VerticalAlignment.Center;
                line.Margin = new Thickness(0, 0, 12, 0);

                _state.Children.Add(line);
            }

            /// <summary>
            /// Write everything this tab holds to the project file, in one call.
            ///
            /// Keys are set one at a time rather than by rewriting the section, because the
            /// profile section also carries the storeys and the roster and a whole-section write
            /// would take them out.
            /// </summary>
            private void Keep()
            {
                var profile = _seat.Store.Section(ProjectStore.ProfileSection);

                foreach (var one in _changed)
                    profile.Set(one.Key, JsonValue.String(
                        string.IsNullOrWhiteSpace(one.Value) ? null : one.Value!.Trim()));

                if (_plan != null) profile.Set(LevelPlan.Key, _plan.ToJson() ?? JsonValue.Array());

                if (_roster != null)
                {
                    // Kept beside the person as well, which is where the roster is read from on
                    // the next job.
                    _roster.Keep();
                    profile.Set(RosterKey, _roster.ToJson());
                }

                if (_book != null)
                    _seat.Store.Section(ProjectStore.ClashSection)
                         .Set(NamingBook.Key, _book.ToJson() ?? JsonValue.Object());

                _state.Children.Clear();

                if (_seat.Store.Save())
                {
                    _dirty = false;

                    var line = Ui.Small("Saved to " + _seat.Store.Where);

                    line.VerticalAlignment = VerticalAlignment.Center;
                    line.Margin = new Thickness(0, 0, 12, 0);
                    line.MaxWidth = 320;
                    line.TextTrimming = TextTrimming.CharacterEllipsis;
                    line.ToolTip = _seat.Store.Where;

                    _state.Children.Add(line);

                    return;
                }

                _state.Children.Add(Ui.Problem(
                    _seat.Store.LastSaveProblem ?? "The project file could not be written."));
            }

            // ---------------------------------------------------------------------------------
            // Units and distances
            // ---------------------------------------------------------------------------------

            /// <summary>Lengths, and the two distances the board groups clashes by.</summary>
            private UIElement UnitsPart()
            {
                var held = Ui.Stack();

                held.Children.Add(Head(
                    "Units and distances",
                    "How lengths are written on this project, and how far apart two clashes can "
                    + "be and still be reported as one.",
                    "Lengths are held in metres and converted once where they are printed, so "
                    + "this changes reports and labels rather than any stored value. Grouping "
                    + "distance is how far apart two clashes from the same test can be and still "
                    + "be one problem. Same-conflict distance is how close two different tests' "
                    + "hits have to be to count as the same conflict found twice."));

                var lengths = Read(ProfileKeys.Units);
                var fraction = Read(ProfileKeys.InchFraction);
                var grouping = Read(ProfileKeys.ClashProximity);
                var duplicate = Read(ProfileKeys.DuplicateBand);

                var imperial = Measure.Of(lengths).System == UnitSystem.Imperial;

                var units = Ui.Field("Lengths in",
                    Ui.Choice(Measure.Choices, lengths ?? Measure.Choices[0], pick =>
                    {
                        Set(ProfileKeys.Units, pick);

                        // The inch fraction is only a control at all in feet and inches, so the
                        // section is drawn again rather than leaving a dead box on a metric job.
                        Show(Part.Units);
                    }, 200));

                var inches = imperial
                    ? Ui.Field("Round inches to",
                        Ui.Choice(
                            Measure.Denominators
                                   .Select(d => d.ToString(CultureInfo.InvariantCulture))
                                   .ToList(),
                            fraction ?? "16",
                            pick => Set(ProfileKeys.InchFraction, pick), 200))
                    : null;

                held.Children.Add(Rowed(units, inches));

                held.Children.Add(Rowed(
                    Ui.Field("Grouping distance (m)",
                             Ui.Text(grouping, typed => Set(ProfileKeys.ClashProximity, typed),
                                     120)),
                    Ui.Field("Same-conflict distance (m)",
                             Ui.Text(duplicate, typed => Set(ProfileKeys.DuplicateBand, typed),
                                     120))));

                return held;
            }

            // ---------------------------------------------------------------------------------
            // Levels
            // ---------------------------------------------------------------------------------

            /// <summary>The storeys, drawn by the editor that owns them.</summary>
            private UIElement LevelsPart() =>
                ClashLevels.Panel(Plan, _pane, () => { Touched(); return true; });

            /// <summary>The storeys this project declares, read once and edited in place.</summary>
            private LevelPlan Plan => _plan ??= _given ?? LevelPlan.FromJson(
                _seat.Store.Section(ProjectStore.ProfileSection)[LevelPlan.Key]);

            // ---------------------------------------------------------------------------------
            // Disciplines
            // ---------------------------------------------------------------------------------

            /// <summary>
            /// Which discipline each loaded model is.
            ///
            /// Read from the file name where it can be, overridden here where it cannot. The
            /// matrix's two sides, the board's grouping and the export split all read this, so a
            /// model on the wrong discipline is wrong in three places at once.
            /// </summary>
            private UIElement DisciplinesPart()
            {
                var held = Ui.Stack();

                held.Children.Add(Head(
                    "Disciplines",
                    "Which discipline each loaded model belongs to.",
                    "CamelWorks reads a discipline off each model's file name where the name says "
                    + "one. What is set here overrides that. The matrix builds its two sides from "
                    + "these, the board groups by them, and Exchange splits an export by them."));

                var models = _seat.Profile.Settings
                    .Where(one => one.Key.StartsWith(ProfileKeys.DisciplinePrefix,
                                                     StringComparison.Ordinal))
                    .ToList();

                if (models.Count == 0)
                {
                    held.Children.Add(Ui.Empty(
                        "No models loaded",
                        "A discipline belongs to a model. Append the federation in Navisworks "
                        + "and every model appears here."));

                    return held;
                }

                var offered = new List<string> { NoDiscipline };

                offered.AddRange(Disciplines.All);

                var rows = models.Select(one => new TableRow(one,
                    one.Title,
                    string.IsNullOrWhiteSpace(Read(one.Key)) ? NoDiscipline : Read(one.Key)!))
                    .ToList();

                var table = Ui.Table(rows, ("Model", 0), ("Discipline", 180));

                // A federation is twenty models and a discipline is set on most of them at once:
                // a chooser per row is twenty menus to open. The chooser beside the verb is read
                // by it, so a whole selection is set in one press.
                var wanted = NoDiscipline;

                var pick = Ui.Choice(offered, NoDiscipline, chosen => wanted = chosen, 180);

                pick.VerticalAlignment = VerticalAlignment.Center;

                held.Children.Add(Ui.Picking(table, Ui.Field("Set to", pick),
                    new Ui.OnPicked("Set discipline on {n}", (picked, said) =>
                    {
                        var settings = picked.OfType<ProfileSetting>().ToList();

                        said.Children.Clear();

                        if (settings.Count == 0) return;

                        foreach (var setting in settings)
                            Set(setting.Key,
                                string.Equals(wanted, NoDiscipline, StringComparison.Ordinal)
                                    ? null
                                    : wanted);

                        Show(Part.Disciplines);
                    })));

                return held;
            }

            // ---------------------------------------------------------------------------------
            // Team
            // ---------------------------------------------------------------------------------

            /// <summary>The roster, as a table edited in place.</summary>
            private UIElement TeamPart()
            {
                var held = Ui.Stack();

                held.Children.Add(Head(
                    "Team",
                    "Every person on the job. A clash can be given to one of them, or to "
                    + "a whole role, company or team.",
                    "The roster is kept beside you as well as in this project, so the same people "
                    + "are there the next time a model is opened and whoever opens this job gets "
                    + "the team with it. Roles, companies and teams are not maintained lists: "
                    + "they are whatever the people on the roster say they are, so a role "
                    + "no member holds stops being one."));

                held.Children.Add(TeamWindow.Panel(Roster, Touched));

                return held;
            }

            /// <summary>The roster, read once and edited in place.</summary>
            private TeamRoster Roster => _roster ??= TeamWindow.Read(_seat);

            // ---------------------------------------------------------------------------------
            // Test naming
            // ---------------------------------------------------------------------------------

            /// <summary>The fields, the patterns, which pattern reads which test, and renumber.</summary>
            private UIElement NamingPart() =>
                ClashNaming.Section(Book, Tests(), Rename, Opening(), _pane, Touched);

            /// <summary>The project's naming, read once and edited in place.</summary>
            private NamingBook Book => _book ??= _bookGiven ?? NamingBook.FromJson(
                _seat.Store.Section(ProjectStore.ClashSection)[NamingBook.Key]);

            /// <summary>
            /// Which naming pane to open on.
            ///
            /// Patterns until there is one, because the Tests pane can show nothing to assign
            /// before a pattern exists.
            /// </summary>
            private NamingPane Opening() =>
                Book.Patterns.Count == 0 ? NamingPane.Patterns : NamingPane.Tests;

            /// <summary>The document's clash tests, as the naming holds them.</summary>
            private IReadOnlyList<(string Id, string Name)> Tests()
            {
                if (_testsGiven != null) return _testsGiven;

                if (!_seat.Clash.IsAvailable) return Array.Empty<(string, string)>();

                return _seat.Clash.Tests().Select(one => (one.Id, one.Name)).ToList();
            }

            /// <summary>Rename one test in the document, and say whether the host took it.</summary>
            /// <param name="id">The test's host id.</param>
            /// <param name="name">What to call it.</param>
            private bool Rename(string id, string name) =>
                _seat.Clash.IsAvailable && _seat.Clash.CanEdit
                && _seat.Clash.RenameTest(id, name);

            // ---------------------------------------------------------------------------------
            // Profile values
            // ---------------------------------------------------------------------------------

            /// <summary>What a profile setting reads as now, including an edit not yet saved.</summary>
            /// <param name="key">The setting's key.</param>
            private string? Read(string key) =>
                _changed.TryGetValue(key, out var held) ? held : _seat.Profile.Find(key)?.Value;

            /// <summary>Record an override, to be written by the tab's one action.</summary>
            /// <param name="key">The setting's key.</param>
            /// <param name="value">What to set it to, or null to go back to the derived value.</param>
            private void Set(string key, string? value)
            {
                _changed[key] = value;

                // Applied to the profile at once as well, so anything else on this screen reading
                // the profile agrees with the box that was just typed in.
                var setting = _seat.Profile.Find(key);

                if (setting != null) setting.Override = value;

                Touched();
            }

            /// <summary>
            /// One or two short fields on a line, capped so a wide tab does not stretch them.
            /// </summary>
            /// <param name="left">The first field.</param>
            /// <param name="right">The second, or null for a single field on the line.</param>
            private static UIElement Rowed(UIElement left, UIElement? right)
            {
                var row = right == null ? Ui.Stack(left) : Ui.Pair(left, right);

                row.MaxWidth = FieldWidth;
                row.HorizontalAlignment = HorizontalAlignment.Left;
                row.Margin = new Thickness(0, 0, 0, 10);

                return row;
            }
        }
    }
}
