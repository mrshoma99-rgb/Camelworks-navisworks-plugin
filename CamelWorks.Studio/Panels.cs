using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The apps, reconstructed.
    ///
    /// One page per app worth looking at, each drawn as the app window draws it — the title
    /// strip with the app's accent, the flat tab strip read from the real <see cref="Apps"/>
    /// registry, and a flagship tab built to the anatomy in <c>CAMELWORKS_DESIGN.md</c>: the
    /// data filling the window, the options on the right, the one action bottom-right.
    ///
    /// Same rule as ever: same captions, same weights, plausible data where the real surface
    /// reads the host. Where a reconstruction and its surface disagree the reconstruction is
    /// wrong, and fixing it is part of changing the surface.
    /// </summary>
    public static class Panels
    {
        /// <summary>Every desk page, in ribbon order.</summary>
        /// <summary>Every app page, in ribbon order.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Page("app-clash-manage", "Clash Manager · Manage", 980, () => Coordination(false)),
            Page("app-clash-finder", "Clash Manager · Manage, the filter panel open", 980,
                 () => Coordination(true)),
            Page("app-issue-register", "Issue Manager · Issues", 780, Issues),
            Page("app-audit", "Model Audit · Health", 780, Federation),
            Page("app-data", "Data Manager · Properties", 780, Data),
            Page("app-scenes-appearance", "Scene Kit · Appearance", 780, Appearance),
            Page("app-scenes-viewpoints", "Scene Kit · Viewpoints", 780, Scenes),
            Page("app-automate-jobs", "Automate · Jobs", 780, Deliver),

            // The one page here that is the shipping screen rather than a drawing of it. Every
            // other page composes the real controls into an arrangement written here, which catches
            // a wrong colour or a clipped word but not a fault in the layout code itself — and the
            // matrix's faults have all been in the layout code: a header sized without its own
            // padding, a rotated name given a box longer than the space it has, a folder band
            // collapsed to no height. None of those appear in an arrangement written by hand.
            //
            // MatrixWindow.Surface takes a seat, so this is the tab itself, measured by WPF at the
            // width WPF gives it, in the font it is read in.
            Page("app-clash-matrix", "Clash Manager · Matrix", 820,
                 () => MatrixWindow.Surface(Seated())),

            // Drawn from the fielded board: it is the same grid plus a scheme, so one page
            // covers the matrix and the two things the fields add to it — a number counted down
            // the axis, and the mark on the row that has left the counting.
            Page("app-clash-matrix-drawn", "Clash Manager · Matrix, drawn", 880,
                 () => MatrixWindow.Surface(Seated(), Fielded())),

            // The fields window, and the state that matters in it: a built-in field counted down
            // the axis, a custom field of the project's own, and a line held by hand. A board with
            // every field on "by hand" would draw the window with none of its controls, which is
            // the one shape that proves nothing.
            Page("app-clash-matrix-fields", "Clash Manager · Fields", 620,
                 () => MatrixFields.Drawn(Fielded())),

            // The window that says which tests become a matrix and what their names mean. Drawn
            // as a panel rather than shown as a window, because a WPF window needs a screen to
            // exist on and this harness has none - so until this was split out, every dialogue
            // in the product was checked by a coordinator on a licensed seat and described to me
            // in words.
            Page("app-clash-matrix-read", "Clash Manager · Read from the model", 700,
                 () => MatrixReadWindow.Draw(Named(), "{rowCode}-{rowNumber} {row} vs "
                                                      + "{columnCode}-{columnNumber} {column}",
                                             null, null)),

            // The matrix in the state a real one is used in: more lines than fit, so the grid
            // scrolls and the frozen bands have to track it. A page tall enough to show every
            // row proves nothing about the bands, which is why the drift between them and the
            // cells was reported off a seat rather than off a picture.
            Page("app-clash-matrix-scrolled", "Clash Manager · Matrix, scrolled", 520,
                 () => MatrixWindow.Surface(Seated(), Drawn(pointed: true))),

            // The panel beside the grid, with a line picked, which is the only state it draws
            // anything in. Every field of a line is typed here and a field of the project's own
            // is made here, and until this page existed none of it had been photographed.
            Page("app-clash-matrix-line", "Clash Manager · Matrix, a line picked", 880,
                 () => MatrixWindow.Surface(Seated(), Fielded(), MatrixWindow.Showing.Symbols,
                                            null, "ARC_Internal Partitions")),

            // The levels a project declares, and what each covers. Drawn with a building in it:
            // an empty plan draws the one line saying there are none, which is the state that
            // proves nothing about the editor.
            // The Exchange tab, in the state it is used in: a board of a few hundred clashes,
            // a filter narrowing them and a split that writes a pack per trade. Drawn from the
            // project file rather than a document, so the table and the filter chips can be
            // looked at rather than described from a licensed seat.
            Page("app-clash-exchange", "Clash Manager · Exchange", 560,
                 () => new Border
                 {
                     // Bounded, because the tab is. The surface is a two-pane bench whose body row
                     // is a star, and a star row measured against an unbounded height draws the
                     // whole screen again below itself.
                     Height = 500,
                     Child = ClashExchangeViews.Drawn(Filed(), "Riverside", Sent()),
                 }),

            // The other half of the round with nothing chosen, which is the state it opens in and
            // the one an empty screen has to explain rather than merely be blank in. Short,
            // because that state is one card.
            Page("app-clash-exchange-import", "Clash Manager · Exchange, import", 360,
                 () => new Border
                 {
                     Height = 300,
                     Child = ClashExchangeViews.Drawn(Filed(), "Riverside", Sent(),
                                                      importing: true),
                 }),

            // The import mode in the state it is used in: two parties' packs read against the
            // board, one clash answered differently by each. The conflict row, the verb that
            // resolves it and the primary that applies the rest are all drawn only in this state,
            // and none of them had been photographed.
            Page("app-clash-exchange-import-read", "Clash Manager · Exchange, packs read", 560,
                 () => new Border
                 {
                     Height = 500,
                     Child = ClashExchangeViews.Drawn(Filed(), "Riverside", Sent(),
                                                      importing: true, returned: Returned()),
                 }),

            // The Filter tab as a fresh project has it: three sections with nothing in them, the
            // composer beside them, and the preview blocked because a preview reads results and
            // this seat has no model. The empty states were written for this and had never been
            // drawn.
            Page("app-clash-rules", "Clash Manager · Filter", 720,
                 () => Deck("clash", "rules", CoordinateViews.Rules(Seated()))),

            // The same tab with rules on it, which is the state every list, every order control
            // and every trim on this screen exists for.
            Page("app-clash-rules-set", "Clash Manager · Filter, rules written", 860, Ruled),

            // The Report tab. A report is written from the board and this seat has none, so what
            // the page draws is the rest of the screen: the document picker, the audience, the
            // marker settings and the revision line, none of which had been looked at.
            Page("app-clash-report", "Clash Manager · Report", 620,
                 () => Deck("clash", "clashreport", CoordinateViews.Report(Seated()))),

            // The Settings tab, on each of the three sections that has data behind it. The whole
            // tab each time: the rail says which section is open and the foot says whether
            // anything is unsaved, and neither is visible in a picture of the pane alone.
            Page("app-clash-settings", "Clash Manager · Settings, levels", 760,
                 () => Deck("clash", "clashsettings",
                            ClashSettingsViews.Drawn(Seated(), ClashSettingsViews.Part.Levels,
                                                     Storeyed()))),

            Page("app-clash-settings-naming", "Clash Manager · Settings, test naming", 760,
                 () => Deck("clash", "clashsettings",
                            ClashSettingsViews.Drawn(Seated(), ClashSettingsViews.Part.Naming,
                                                     null, Booked(), Testing()))),

            Page("app-clash-settings-units", "Clash Manager · Settings, units and distances", 760,
                 () => Deck("clash", "clashsettings",
                            ClashSettingsViews.Drawn(Seated(), ClashSettingsViews.Part.Units))),

            // The naming, drawn on each of its panes. This is the surface that decides what the
            // manager can band, filter, sort and renumber by, and every one of those reads a
            // test's name through a pattern somebody wrote here.
            Page("app-clash-naming-tests", "Clash Manager · Naming, tests", 720,
                 () => ClashNaming.Drawn(Booked(), Testing(), NamingPane.Tests)),

            Page("app-clash-naming-patterns", "Clash Manager · Naming, patterns", 720,
                 () => ClashNaming.Drawn(Booked(), Testing(), NamingPane.Patterns)),

            // Renumbering with the plan on screen: what each test is called, what it would
            // become, and the ones the naming refuses with the reason beside them.
            Page("app-clash-naming-write", "Clash Manager · Naming, renumber", 720,
                 () => ClashNaming.Drawn(Booked(), Testing(), NamingPane.Write)),

            // The results view was the one that could not be photographed: it counts what the
            // document holds and this seat holds none, so the heat scale, the number in each
            // cell and the difference between a nought and a pair no test covers were drawing
            // nobody had ever seen. It is handed the same list a real read hands back, and the
            // tab counts it through MatrixTally exactly as a licensed seat does.
            Page("app-clash-matrix-results", "Clash Manager · Matrix, results", 880,
                 () =>
                 {
                     var board = Drawn(pointed: true);

                     return MatrixWindow.Surface(Seated(), board, MatrixWindow.Showing.Results,
                                                 Ran(board));
                 }),

            // The same board, turned over to the priority view. A view that cannot be
            // photographed is a view whose drawing faults are found on a licensed seat and
            // described to me in words, which is how every layout fault on this tab has been
            // found so far. The results view is not here: it counts what a document holds, and
            // this seat holds none.
            Page("app-clash-matrix-priority", "Clash Manager · Matrix, priority", 880,
                 () => MatrixWindow.Surface(Seated(), Drawn(), MatrixWindow.Showing.Priority)),

            // The detected-surface window, opened from a clearance test's settings panel. The
            // whole point of the page is the sentence the detected option is offered under and the
            // list under it: detection cannot tell a floor from a desk top, and a screen that says
            // "detected walkable surfaces" and stops there reads as though it could.
            // Not in the render's default list; named on dispatch.
            Page("app-clash-clearance-surfaces", "Clash Manager · Detected walkable surfaces", 700,
                 Walkable),

            // The Create tab, with a clearance test picked: the list of every kind on the left,
            // that test's settings on the right, and the Run the seat cannot press with its
            // reason. The shipping screen rather than a drawing of it, which is what Create(ISeat)
            // buys. Not in the render's default list; named on dispatch.
            Page("app-clash-create", "Clash Manager · Create, a clearance test picked", 760,
                 () => Creating(FindingSource.Headroom)),

            // The Manage board's check picker, opened. A menu lives in a popup and a popup needs
            // a screen to open on, so the greyed entry and its reason — the state this control
            // exists for — had never been drawn.
            Page("app-clash-picker", "Clash Manager · The check picker", 420, Picker),

            // The same tab with a fall-hazard test picked. The nine settings this check is
            // configured by used to live in two windows off an action row, so they could only be
            // looked at on a licensed seat; they are the panel now and this page draws them.
            // Not in the render's default list; named on dispatch.
            Page("app-clash-create-fall", "Clash Manager · Create, a fall-hazard test picked",
                 760, () => Creating(FindingSource.FallHazard)),

            // The same tab with nothing saved, which is the state a project starts in: the list
            // says what is missing and carries the verb that fixes it, and the panel says the
            // settings belong to a test.
            // Not in the render's default list; named on dispatch.
            Page("app-clash-create-empty", "Clash Manager · Create, no tests yet", 620,
                 () => Creating(null)),
        };

        /// <summary>
        /// The Create tab, with a test of one kind picked.
        ///
        /// The tests are saved into the scratch project first, because the settings belong to a
        /// test and the panel says so rather than drawing controls over nothing.
        /// </summary>
        /// <param name="checkKey">
        /// Which kind's test to pick, or null for a project holding no test at all — which is the
        /// state a project starts in and the one the empty states are written for.
        /// </param>
        private static UIElement Creating(string? checkKey)
        {
            var seat = Seated();

            if (checkKey == null)
            {
                // The scratch project is shared by every page and this one has to show none, so
                // it empties the project rather than taking a store of its own: the pages that
                // need tests write them again on the way in, whatever order the harness draws in.
                foreach (var one in seat.Checks.All)
                    one.Runner?.Delete(one.Runs().Select(run => run.Id).ToList());

                return Deck("clash", "create", CheckCreateViews.Drawn(seat, null, null));
            }

            var clearance = seat.Checks.All.OfType<ClearanceCheck>().First();
            var fall = seat.Checks.All.OfType<FallCheck>().First();

            var measured = new CheckRunSpec(clearance.Key, "l3-corridors", "Level 3 corridors")
            {
                Folder = "Weekly",
            };

            new ClearanceSettings
            {
                Surfaces = "Level 03 Floors",
                Targets = ClearanceSettings.NotTheSurfaces,
                MinimumMetres = 2.1,
                ToleranceMetres = 0.05,
                Zone = "Level 03",
                WorstPerPatch = true,
            }.Write(measured);

            clearance.Put(measured);

            var swept = new CheckRunSpec(fall.Key, "l3-openings", "Level 3 openings")
            {
                Folder = "Weekly",
            };

            new FallSettings
            {
                Floors = "Level 03 Floors",
                Handrails = "Level 03 Handrails",
                Obstructions = FallSettings.Nothing,
                LevelMetres = 10.5,
                BandMetres = 1.0,
                CellMetres = 0.1,
                LimitMetres = 0.2,
                HandrailToleranceMetres = 0.3,
                MinPassageMetres = 0.5,
            }.Write(swept);

            fall.Put(swept);

            var picked = string.Equals(checkKey, FindingSource.Headroom, StringComparison.Ordinal)
                ? measured
                : swept;

            return Deck("clash", "create",
                        CheckCreateViews.Drawn(seat, picked.CheckKey, picked.Id));
        }


        /// <summary>
        /// The check picker's own menu, with an unavailable check in it.
        ///
        /// The state worth drawing is the greyed line: a Simulate seat has no clash engine, and
        /// the picker lists the check with its reason rather than leaving it out, so the seat can
        /// say why the check is not there.
        /// </summary>
        private static UIElement Picker()
        {
            var seat = Seated();

            // The board's own list: this document's clash data first, then whatever else the
            // session registered. On a seat with no document neither is available, which is the
            // state the reasons are written for.
            var offered = new List<ICheck>
            {
                new ClashCheck(Host.WithoutClash(), 1.0,
                               "This Navisworks edition has no clash engine."),
            };

            offered.AddRange(seat.Checks.All);

            return Ui.Stack(
                CheckPicker.Draw(offered, offered[0], seat.Findings.Findings, _ => { }),
                new Border { Height = 10 },
                Parts.Drawn(CheckPicker.Entries(offered, offered[0],
                                                seat.Findings.Findings, _ => { })));
        }

        /// <summary>
        /// The Filter tab with a project's rules on it.
        ///
        /// The rules are written into the scratch project on the way in, as
        /// <see cref="Creating"/> writes its tests: the store is shared by every page, so a page
        /// that needs data puts it there itself rather than relying on the order the harness draws
        /// in. Nothing is saved, so the empty Filter page still opens on an empty project.
        ///
        /// What is written is what the three lists have to draw: both filter actions, a name long
        /// enough to be trimmed, a grouping step that carries a number and one that carries a
        /// property name, and assignments with and without a priority.
        /// </summary>
        private static UIElement Ruled()
        {
            var seat = Seated();
            var rules = new ClashRuleSet();

            rules.Filters.Add(new FilterSpec(
                "Insulation grazing insulation",
                new PredicateSpec("maxVolume") { A = 0.001 }, suppress: true));

            rules.Filters.Add(new FilterSpec(
                "Sleeves already coordinated against the structural openings schedule REV21",
                new PredicateSpec("bothInSet") { Name = "Sleeves REV21" }, suppress: true));

            rules.Filters.Add(new FilterSpec(
                "Touching, not passing through",
                new PredicateSpec("distance") { A = 0, B = 0.005 }, suppress: false));

            rules.Filters.Add(new FilterSpec(
                "Services through structural framing",
                new PredicateSpec("category") { Name = "Structural Framing" }, suppress: false));

            rules.Grouping.Add(new GroupingSpec("level"));
            rules.Grouping.Add(new GroupingSpec("proximity") { A = 2.5 });
            rules.Grouping.Add(new GroupingSpec("property") { Name = "System Name" });

            rules.Assigns.Add(new AssignSpec(
                new PredicateSpec("eitherInSet") { Name = "WSC_DUCTS_REV21_ISSUED" },
                "Mechanical", "High"));

            rules.Assigns.Add(new AssignSpec(
                new PredicateSpec("category") { Name = "Cable Tray" }, "Electrical"));

            rules.Assigns.Add(new AssignSpec(
                new PredicateSpec("propertyContains") { Name = "System Name", Value = "SPR" },
                "Fire", "Normal"));

            seat.Store.Section(ProjectStore.ClashSection).Set(ClashKeys.Rules, rules.ToJson());

            return Deck("clash", "rules", CoordinateViews.Rules(seat));
        }

        /// <summary>
        /// A seat with nothing in it, and a scratch project beside the images.
        ///
        /// Empty on purpose. This is the state a screen is in before a document is open, and the one
        /// least often checked, so the harness draws it.
        /// </summary>

        /// <summary>A project file in the temp folder, for the screens that keep settings in one.</summary>
        private static ProjectStore Filed() =>
            ProjectStore.Open(PhysicalFileSystem.Instance,
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots", "shots.nwf"),
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots"));

        /// <summary>
        /// A board of the size and shape a real issue goes out from.
        ///
        /// Four trades, four statuses and three storeys, because that is what the Exchange tab's
        /// filter, split and table are all about: a screen drawn from six clashes on one level
        /// shows none of them doing anything.
        /// </summary>
        private static IReadOnlyList<ClashGroup> Sent()
        {
            var made = new List<ClashItem>();

            for (var at = 0; at < 96; at++) made.Add(Boarded(at));

            return ClashPipeline.Run(made).Groups;
        }

        /// <summary>Who the board's clashes are assigned to.</summary>
        private static readonly string[] SentParties =
            { "Mechanical", "Structure", "Electrical", "Architecture" };

        /// <summary>The tests the board's clashes came from.</summary>
        private static readonly string[] SentTests =
            { "L02 MEP v STR", "L03 MEP v STR", "L04 MEP v ARC", "L02 ARC v STR",
              "Basement services", "Roof plant" };

        /// <summary>The folder each test is filed under, or null for one at the root.</summary>
        private static readonly string?[] SentFolders =
            { "Weekly", "Weekly", "Weekly", "Handover", "Handover", null };

        /// <summary>The storeys the board's clashes stand on.</summary>
        private static readonly string[] SentLevels = { "L02", "L03", "L04" };

        /// <summary>
        /// One clash of the board, as the board has it.
        ///
        /// Made here rather than inside <see cref="Sent"/> because the returned packs are written
        /// from the same clashes: a pack whose keys are derived any other way matches nothing on
        /// the board and the import mode draws an empty reading.
        /// </summary>
        /// <param name="at">Which one.</param>
        private static ClashItem Boarded(int at)
        {
            var item = ClashFixture(at, SentTests[at % SentTests.Length],
                                    SentLevels[at % SentLevels.Length]);

            item.TestFolder = SentFolders[at % SentTests.Length];
            item.AssignedTo = SentParties[at % SentParties.Length];
            item.Status = (FindingStatus)(at % 5);

            return item;
        }

        /// <summary>
        /// Two returned packs on disk, for the import page.
        ///
        /// Written rather than fabricated: the tab takes paths, reads the archives and matches
        /// what it finds against the board, so a fixture that skipped the writer would photograph
        /// a screen the product cannot produce.
        ///
        /// Both parties answer clash 007, and differently, which is the state the conflict row,
        /// the verb that resolves it and the enabled primary are all drawn in.
        /// </summary>
        private static IReadOnlyList<string> Returned()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                "camelworks-shots", "returned");

            System.IO.Directory.CreateDirectory(folder);

            // Every answer differs from the status the board has, so each row is a row that
            // changes something and the "only what changes" filter has the same list under it.
            return new[]
            {
                Answered(folder, "Mechanical", new[]
                {
                    (7, FindingStatus.Resolved),
                    (13, FindingStatus.Active),
                    (25, FindingStatus.Approved),
                    (31, FindingStatus.Reviewed),
                }),
                Answered(folder, "Structure", new[]
                {
                    (7, FindingStatus.Active),
                    (18, FindingStatus.Resolved),
                    (44, FindingStatus.Active),
                }),
            };
        }

        /// <summary>
        /// One party's pack, written to the temp folder.
        ///
        /// The file's name is the party: the tab reads the party off the file name unless somebody
        /// renames it, which is what a coordinator sees when the packs arrive by mail.
        /// </summary>
        /// <param name="folder">Where to write it.</param>
        /// <param name="party">Who sent it back.</param>
        /// <param name="answers">Which of the board's clashes they answered, and how.</param>
        private static string Answered(string folder, string party,
                                       IReadOnlyList<(int At, FindingStatus Said)> answers)
        {
            // A fixed moment, so two renders of this page produce the same file.
            var when = new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Utc).Ticks;

            var findings = new List<Finding>();

            foreach (var answer in answers)
            {
                var item = Boarded(answer.At);

                var result = new ClashResultInfo(
                    answer.At.ToString(CultureInfo.InvariantCulture),
                    item.Key.A, item.Key.B, item.X, item.Y, item.Z, null, null)
                {
                    Name = item.Name,
                };

                // Through the product's own projection, so the topic guid and the sidecar identity
                // are the ones a real pack carries and the return leg lands on the right clash.
                var finding = ClashFindings.From(item.TestName, result);

                finding.Status = answer.Said;
                finding.AssignedTo = party;
                finding.Comments.Add(new FindingComment(party, when, Remarked(answer.Said)));

                findings.Add(finding);
            }

            var path = System.IO.Path.Combine(folder, party + ClashFile.Extension);

            var written = FindingBcf.Write(findings, null, new FindingBcfOptions
            {
                ProjectName = "Riverside",
                Author = party,
                WhenTicks = when,
            });

            System.IO.File.WriteAllBytes(path, written.Bytes);

            return path;
        }

        /// <summary>What a party writes on a clash when they answer it.</summary>
        /// <param name="said">The status they returned.</param>
        private static string Remarked(FindingStatus said) => said switch
        {
            FindingStatus.Resolved => "Re-routed above the beam, re-issued this morning.",
            FindingStatus.Approved => "Within the agreed tolerance, no change needed.",
            FindingStatus.Reviewed => "Looked at; waiting on the structural opening.",
            _ => "Still ours, not started.",
        };

        /// <summary>One clash, far enough from the last that the grouping keeps them apart.</summary>
        /// <param name="at">Which one, for its name and its position.</param>
        /// <param name="test">Which test it came from.</param>
        /// <param name="level">Which level it stands on.</param>
        private static ClashItem ClashFixture(int at, string test, string level)
        {
            var scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");
            var other = ElementKey.ScopeOf(@"C:\p\MEP.nwc");

            var said = at.ToString("000", CultureInfo.InvariantCulture);

            var a = ElementKey.FromTreePath(scope, @"C:\p\ARCH.nwc", level, "Wall " + said);
            var b = ElementKey.FromTreePath(other, @"C:\p\MEP.nwc", level, "Duct " + said);

            return new ClashItem(ClashKey.Create(a, b, at * 3.0, at * 2.0, at % 3 * 4.0), test)
            {
                Name = "Clash " + said,
                Level = level,
                ModelA = "ARCH",
                ModelB = "MEP",
                X = at * 3.0,
                Y = at * 2.0,
                Z = at % 3 * 4.0,
            };
        }

        private static ISeat Seated() => new EmptySeat(
            ProjectStore.Open(PhysicalFileSystem.Instance,
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots", "shots.nwf"),
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots")),
            Host.WithoutClash());

        /// <summary>
        /// A matrix of the size and shape a real one is, so the grid can be looked at.
        ///
        /// Every image of this tab was previously the empty start screen. A seat with no document
        /// has no board, so the grid — the whole of the tab, and where every layout fault reported
        /// against it has been — was never captured. The clipped column names, the row band that ran
        /// out of names and the dead strip beside the last column were all described in words from a
        /// licensed seat and fixed by reasoning rather than by looking.
        ///
        /// So the shapes that break it are here deliberately: names long enough to have to be turned
        /// and long enough to be cut off if the band is a pixel short; enough disciplines that the
        /// cells reach their smallest size; folders on both axes, one of them holding a run; codes
        /// that are not the front of their names, so the code gutter is drawn; and a number that
        /// disagrees with its position, so the number gutter is drawn too.
        /// </summary>
        /// <summary>
        /// A board whose fields exercise the window: a counted number, a phase the same on every
        /// line, a sub-discipline code typed by hand, and one row that has left the numbering.
        /// </summary>
        private static MatrixBoard Fielded()
        {
            var board = Drawn(pointed: true);

            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "10";
            number.Step = 10;
            number.Pad = 2;

            board.Scheme.Add("phase", "Phase");

            var phase = board.Scheme.Field("phase")!;

            phase.Fill = MatrixFill.Same;
            phase.Same = "P2";

            board.Scheme.Add("sub", "Sub-discipline");

            // A package every test name is built from and nobody wants as a fourth gutter down
            // the side of the board. It is the state the placement ticks exist for, so the card
            // that shows it is drawn on every render.
            board.Scheme.Add("package", "Package");

            var package = board.Scheme.Field("package")!;

            package.Fill = MatrixFill.Same;
            package.Same = "PK1";
            package.Shown = MatrixShown.Panel;

            board.ApplyScheme();

            if (board.Rows.Count > 2) board.Write(board.Rows[2], MatrixField.NumberKey, "99", byHand: true);

            return board;
        }

        private static MatrixBoard Drawn() => Drawn(pointed: false);

        /// <summary>
        /// The board every matrix page is drawn from.
        /// </summary>
        /// <param name="pointed">
        /// True to give every line the saved set it stands for.
        ///
        /// Off for the two pages that photograph the drawing, because a board being typed has
        /// nothing behind its lines yet and the marks that say so are part of what those pages
        /// are for. On for the results page, where it is not decoration: what a test is between
        /// is how a count finds its cell, so a board pointing at nothing counts onto nothing.
        /// </param>
        private static MatrixBoard Drawn(bool pointed)
        {
            var board = new MatrixBoard();

            var named = new[]
            {
                ("ARC", "ARC_General Elements", "Architecture"),
                ("ARC", "ARC_Internal Partitions", "Architecture"),
                ("ARC", "ARC_Ceilings and Soffits", "Architecture"),
                ("STR", "STR_Structural Frame", "Structure"),
                ("STR", "STR_Slabs and Decks", "Structure"),
                ("STR", "STR_Foundations", "Structure"),
                ("FGT", "FGT_Equipment", string.Empty),
                ("FGT", "FGT_Insulation", string.Empty),
                ("WSC", "WSC_Pipes", "Wet services"),
                ("WSC", "WSC_Ducts", "Wet services"),
                ("WSC", "WSC_Equipment", "Wet services"),
                ("WSC", "WSC_Insulation", "Wet services"),
                ("AUX", "AUX_General Purpose Pipes", string.Empty),
                ("AUX", "AUX_Equipment", string.Empty),
                ("AUX", "AUX_Insulation", string.Empty),
                ("EIC", "EIC_Cable Trays and Supports", "Electrical"),
                ("EIC", "EIC_Containment", "Electrical"),
                ("EIC", "EIC_Lighting", "Electrical"),
                (string.Empty, string.Empty, string.Empty),
                (string.Empty, string.Empty, string.Empty),
            };

            for (var at = 0; at < named.Length; at++)
            {
                var (code, name, folder) = named[at];

                // The numbers skip one, so they stop agreeing with the position in the list and
                // the gutter that prints them is drawn - which is a column of the header the
                // empty screen never had to lay out.
                var row = board.AddPair(new MatrixLine(
                    name,
                    source: pointed && name.Length > 0
                        ? MatrixSource.SelectionSet(name)
                        : null)
                {
                    Code = code,
                    Number = (at + (at > 8 ? 2 : 1)).ToString(CultureInfo.InvariantCulture),
                });

                if (folder.Length > 0) board.File(row, folder);
            }

            // Coloured, because a folder that can carry a colour and is never drawn with one is
            // a feature no picture has ever checked. Two of the four, so the render shows both a
            // folder that has been given one and folders that have not.
            board.Paint("Architecture", "#B3E5FC");
            board.Paint("Wet services", "#C8E6C9");

            // A standing rule, applied first so its cells are the rule's and carry the corner
            // that says so. Filled by hand afterwards and the rule would have nothing left to
            // write - which is a real board's order of events, not this page's.
            board.AddRule(new MatrixRule("Architecture", "Wet services", "B"));
            board.ApplyRules();

            // Enough decided cells that the sheet is read as a pattern rather than as a grid of
            // blanks, and a diagonal that has to be drawn as one.
            var marks = new[] { "A", "B", "C", "D" };

            for (var down = 0; down < board.Rows.Count; down++)
                for (var across = 0; across < board.Columns.Count; across++)
                {
                    if (down == across) continue;
                    if (((down * 7) + (across * 3)) % 5 == 0) continue;

                    // What the rule wrote is left to the rule, so the render shows both kinds of
                    // cell side by side.
                    if (board.At(board.Rows[down], board.Columns[across]) != null) continue;

                    board.Set(board.Rows[down], board.Columns[across],
                              marks[((down * 3) + across) % marks.Length]);
                }

            // Ranked in a block rather than at random, so the picture says whether a run of
            // ranks down a discipline reads as one thing - which is the question the view
            // exists to answer.
            for (var down = 0; down < board.Rows.Count; down++)
                for (var across = 0; across < board.Columns.Count; across++)
                {
                    if (board.At(board.Rows[down], board.Columns[across]) == null) continue;

                    var rank = down < 3 ? MatrixBoard.High
                        : down < 6 ? MatrixBoard.Medium
                        : across < 4 ? MatrixBoard.Low
                        : MatrixBoard.NotSet;

                    board.Rank(board.Rows[down], board.Columns[across], rank);
                }

            return board;
        }

        /// <summary>
        /// The board's tests as a run of them hands them back, so the results view has something
        /// to count.
        ///
        /// Not a tally written by hand. These are the product's own types - the shape of a test
        /// and the results under it - so what the page photographs is
        /// <see cref="MatrixTally"/> doing its own job: finding the cell from what the test is
        /// between, and counting a result as open or not from the word the host wrote on it.
        ///
        /// A spread on purpose. Some pairs come back heavy, some light, some entirely approved -
        /// which is the nought that has to be told from the pairs left blank because no test
        /// covers them at all, and a third of them are left uncovered so that both are on the
        /// picture.
        /// </summary>
        /// <param name="board">The pointed board.</param>
        private static IReadOnlyList<ClashTestRead> Ran(MatrixBoard board)
        {
            var read = new List<ClashTestRead>();
            var at = 0;

            foreach (var cell in board.Cells())
            {
                at++;

                // Diagonal cells included. Cells() only yields written ones, and a written
                // diagonal is a real test in the product - a duct against its own discipline -
                // so a harness that skipped them could not draw one.
                //
                // A third of the pairs have no test at all, so the view has to draw "nobody has
                // checked this" beside "checked, nothing left".
                if (at % 3 == 0) continue;

                // Nought open on one in five, which is the pair somebody has finished with.
                var found = ((at * 37) % 240) + 1;
                var open = at % 5 == 0 ? 0 : ((at * 17) % found) + 1;

                var results = new List<ClashResultInfo>();

                for (var one = 0; one < found; one++)
                    results.Add(new ClashResultInfo(
                        one.ToString(CultureInfo.InvariantCulture), default, default,
                        0, 0, 0,
                        one < open ? (one % 2 == 0 ? "New" : "Active") : "Approved", null));

                var shape = new ClashTestSides(cell.Row.Name, cell.Column.Name,
                                               Array.Empty<string>(), Array.Empty<string>(),
                                               "Hard", 0.01);

                read.Add(new ClashTestRead(
                    new ClashTestInfo(at.ToString(CultureInfo.InvariantCulture),
                                      cell.Row.Name + " vs " + cell.Column.Name, null,
                                      results.Count, 0),
                    shape, results));
            }

            return read;
        }

        /// <summary>
        /// A document's tests as a read of them hands them back, named three different ways.
        ///
        /// Three conventions on one job, because that is what two years of set-up looks like: a
        /// batch named to the standard, a batch from before it, and a handful somebody typed. A
        /// fixture where every name fits the first pattern would photograph a screen that never
        /// happens.
        /// </summary>
        /// <summary>
        /// A building's storeys as a coordinator would type them: a ground floor at zero, a taller
        /// plant level over it, then equal storeys up.
        ///
        /// Uneven on purpose. A block of identical storeys draws every span the same and would not
        /// show that the span is worked out from the next level up rather than typed.
        /// </summary>
        private static LevelPlan Storeyed()
        {
            var plan = new LevelPlan();

            plan.Add("Ground floor", 0);
            plan.Add("Plant", 5.4);
            plan.Add("Level 1", 9.6);
            plan.Add("Level 2", 13.2);
            plan.Add("Roof", 16.8);

            return plan;
        }

        /// <summary>The naming window's tests: the same list, as id and name.</summary>
        private static IReadOnlyList<(string Id, string Name)> Testing() =>
            Named().Select(one => (one.Test.Id, one.Test.Name)).ToList();

        /// <summary>
        /// A project's naming as it stands after a job has been running a while: two conventions,
        /// a field of the project's own, most tests bound and a few on nothing.
        ///
        /// The unbound ones are the state the window exists for. A book where every test reads
        /// cleanly draws none of the refusals, none of the proposal, and none of the count that
        /// says how many are left.
        /// </summary>
        private static NamingBook Booked()
        {
            var book = new NamingBook();

            book.Scheme.Add("phase", "Phase");

            book.Patterned(new[]
            {
                "{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column}",
                "{row} | {column}",
            });

            foreach (var test in Named())
            {
                var at = book.Fitting(test.Test.Name);

                if (at > 0) book.Bind(test.Test.Id, at);
            }

            return book;
        }

        private static IReadOnlyList<ClashTestRead> Named()
        {
            var read = new List<ClashTestRead>();

            var sides = new (string Code, string Number, string Name)[]
            {
                ("ARC", "01", "General Elements"),
                ("ARC", "02", "Internal Partitions"),
                ("STR", "03", "Structural Frame"),
                ("STR", "04", "Slabs and Decks"),
                ("WSC", "05", "Pipes"),
                ("WSC", "06", "Ducts"),
                ("EIC", "07", "Cable Trays"),
            };

            var at = 0;

            for (var one = 0; one < sides.Length; one++)
            for (var other = one + 1; other < sides.Length; other++)
            {
                at++;

                var a = sides[one];
                var b = sides[other];

                // Most of them to the standard, a run of older ones on the other separator, and
                // two nobody named to any rule at all.
                var name = at % 7 == 0
                    ? "Coordination check " + at.ToString(CultureInfo.InvariantCulture)
                    : at % 3 == 0
                        ? a.Name + " | " + b.Name
                        : a.Code + "-" + a.Number + " " + a.Name + " vs "
                          + b.Code + "-" + b.Number + " " + b.Name;

                read.Add(new ClashTestRead(
                    new ClashTestInfo(at.ToString(CultureInfo.InvariantCulture), name, null, 0, 0),
                    new ClashTestSides(Setted(a.Code, a.Name), Setted(b.Code, b.Name),
                                       Array.Empty<string>(), Array.Empty<string>(),
                                       at % 4 == 0 ? "Clearance" : "Hard",
                                       at % 4 == 0 ? 0.025 : 0.010),
                    Array.Empty<ClashResultInfo>()));
            }

            return read;
        }

        /// <summary>
        /// A saved set named the way a real one is: a code, a name in capitals, and the revision
        /// somebody issued it at - which is precisely what a matrix read from the sets used to be
        /// named after, and why reading the test names is worth the window.
        /// </summary>
        /// <param name="code">The discipline code.</param>
        /// <param name="name">The discipline's name.</param>
        private static string Setted(string code, string name) =>
            string.Join("_", new[]
            {
                code, name.Replace(" ", "_").ToUpperInvariant(), "REV21", "ISSUED",
            });

        /// <summary>
        /// The detected walkable surfaces, as the window off a clearance test's settings panel
        /// draws them.
        ///
        /// Drawn rather than shown: a window needs a screen to open on and this harness has none,
        /// so until this page existed nothing about this window had been looked at away from a
        /// licensed seat.
        /// </summary>
        private static UIElement Walkable()
        {
            var rows = new[]
            {
                new TableRow(null, "Level 03", "412.0 m²", "9.600 m", string.Empty),
                new TableRow(null, "Level 03", "88.4 m²", "9.600 m", string.Empty),
                new TableRow(null, "Level 03", "12.6 m²", "10.050 m", string.Empty),
                new TableRow(null, "Level 03", "1.4 m²", "9.900 m", "dropped") { Tone = Tone.Warn },
                new TableRow(null, "Level 03", "0.9 m²", "10.700 m", "dropped") { Tone = Tone.Warn },
                new TableRow(null, "Level 02", "398.2 m²", "6.000 m", string.Empty),
            };

            var table = Ui.Table(rows, ("Level", 0), ("Area", 90), ("Top", 90), ("In the check", 90));

            var stairs = Ui.Check("Leave stairs out", false, null);

            stairs.ToolTip = "Detection makes one surface per tread, so the flight above reads as "
                             + "low headroom over the treads below it.";

            // The same order the screen builds: the one orientation sentence, the settings, the
            // button, then what it found.
            var detected = Bench.Group("Detected walkable surfaces",
                Ui.Small("Detection reads triangles and keeps every up-facing surface over "
                         + "0.05 m², which is a floor, and is also a desk top, a worktop, a window "
                         + "sill and the top of a wide duct: read the list and drop what is not a "
                         + "floor."),
                Ui.Field("Smallest surface kept",
                         Ui.Choice(new[] { "0.05 m²", "0.25 m²", "1.0 m²", "4.0 m²", "10.0 m²" },
                                   "0.05 m²", null, 160)),
                Ui.Check("Prefer elements whose category names a floor", false, null),
                stairs,
                Ui.Runner("Find the surfaces", Ui.Stack(), _ => { }, primary: false),
                Ui.Line("412 surfaces, 8,940 m², 31 elements refused", 1, true),
                Ui.Small("31 elements would not give triangles; their bounding-box tops were used "
                         + "instead."),
                table,
                Ui.Picking(table,
                    new Ui.OnPicked("Drop these {n}", (picked, panel) => { }),
                    new Ui.OnPicked("Include these {n}", (picked, panel) => { })),
                Ui.Small("410 surfaces in the check, 2 dropped"));

            // No footer: this is a window off the settings panel, not a tab, so it has no Run of
            // its own. Edits here apply as they are made, as they do in every configure window.
            return Deck("clash", "create", Bench.Surface(null, null, detected));
        }

        private static Sheet Page(string id, string title, int height, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { 1180 } };

        // -----------------------------------------------------------------------------------
        // The desk chrome
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// An app window's content: title strip, tab strip, and the tab's surface.
        ///
        /// The strip is read from the shipping registry rather than restated, so a tab renamed or
        /// moved changes here by itself — the one honest way to keep a picture true.
        /// </summary>
        private static UIElement Deck(string appId, string toolId, UIElement bench)
        {
            var app = Apps.Find(appId)!;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header(app));

            var strip = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var tab in app.Tabs)
            {
                var current = string.Equals(tab.ToolId, toolId, StringComparison.OrdinalIgnoreCase);

                var label = new TextBlock
                {
                    Text = tab.Title,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(12, 8, 12, 7),
                };

                label.SetResourceReference(TextBlock.ForegroundProperty, current ? "Text" : "Muted");

                var mark = new Border { Height = 2, Background = Brushes.Transparent };
                if (current) mark.SetResourceReference(Border.BackgroundProperty, "Fam." + app.Family);

                var face = new Grid();
                face.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                face.Children.Add(label);
                Grid.SetRow(mark, 1);
                face.Children.Add(mark);

                var button = new Button { Content = face, Padding = new Thickness(0) };
                button.Wearing("RailRow");
                strip.Children.Add(button);
            }

            var tabs = new Border
            {
                Padding = new Thickness(10, 0, 10, 0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = strip,
            };

            tabs.SetResourceReference(Border.BackgroundProperty, "Pane");
            tabs.SetResourceReference(Border.BorderBrushProperty, "Bd");
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            Grid.SetRow(bench, 2);
            root.Children.Add(bench);

            var ground = new Border { Child = root };
            ground.SetResourceReference(Border.BackgroundProperty, "Bg");
            return ground;
        }

        private static UIElement Header(App app)
        {
            var titles = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 10, 16, 9),
            };

            var name = new TextBlock
            {
                Text = app.Title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var suite = new TextBlock
            {
                Text = "CamelWorks",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            suite.Wearing("Hint");
            titles.Children.Add(suite);

            var bar = new Border { Child = titles, BorderThickness = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Nav");
            bar.SetResourceReference(Border.BorderBrushProperty, "Bd");

            var accent = new Border { Height = 2 };
            accent.SetResourceReference(Border.BackgroundProperty, "Fam." + app.Family);

            var header = new Grid();
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Children.Add(bar);
            Grid.SetRow(accent, 1);
            header.Children.Add(accent);

            return header;
        }

        // -----------------------------------------------------------------------------------
        // Federation — Health Check on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Federation()
        {
            var setup = Ui.Stack(
                Bench.Group("The rules",
                    Ui.Field("Rules from", Ui.Choice(
                        new[] { "This project's rules", "An IDS file" }, "An IDS file", null, 200),
                        "this project's own, or a supplied file"),
                    Ui.PathField("An .ids file", Ui.Text(string.Empty, "none — built-in rules only",
                                                         null, 200), PathKind.Open),
                    Ui.Field("Check over", Ui.Choice(
                        new[] { "Everything loaded", "What is selected", "Level 3 Ductwork" },
                        "Everything loaded", null, 200),
                        "the whole federation, or one part of it"),
                    Ui.Runner("Check the rules", Ui.Stack(), _ => { }, primary: false),
                    Ui.Quiet("Write IDS...", () => { })));

            var stage = Ui.Stack(
                Bench.Verdict("Watch — two models disagree about where the site is", Tone.Warn),
                Ui.Across(
                    Ui.Figure("9", "models"),
                    Ui.Figure("2", "adrift", Tone.Warn),
                    Ui.Figure("41,880", "elements"),
                    Ui.Figure("96%", "coded", Tone.Good)),
                Ui.Finding(true, "Placement", "STR-Block-B sits 340 mm east of where it arrived",
                           "Ask structures whether the move was meant before anything is coordinated against it."),
                Ui.Finding(false, "Codes", "412 elements carry a value Uniclass does not list",
                           "Most are one system with a typo in its family; the table below names them."),
                Ui.Eyebrow("Sets"),
                Ui.Finding(true, "Sets", "\"Level 3 Ductwork\" returns nothing. It held 412 elements "
                           + "when it was last read.",
                           "clash test \"MEP vs Structure\" and rule \"Ducts carry a system name\" "
                           + "are pointed at this set, and now cover nothing. A check over nothing "
                           + "does not fail — it passes.",
                           "Open the set and see what it matches now. A set is a saved query, so "
                           + "what changed is usually a property the model stopped carrying.",
                           "clash test \"MEP vs Structure\"; rule \"Ducts carry a system name\""));

            return Deck("audit", "health",
                Bench.Surface(setup, Ui.Runner("Check this federation", Ui.Stack(), _ => { }), stage));
        }

        // -----------------------------------------------------------------------------------
        // Coordination — the clash results bench
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Clash Manager ▸ Manage: the test finder, the board, and the bar for what is picked.
        ///
        /// Reconstructed from <c>ClashManageViews</c>, which needs a live document and cannot run
        /// here. Everything structural is the real thing — <see cref="Parts"/> draws the chips,
        /// the bands and the bar, and the columns are the surface's own widths — so what this
        /// picture gets wrong is the data and never the design.
        /// </summary>
        private static UIElement Coordination(bool panel)
        {
            var rail = Rail(panel);
            var board = Board();

            var foot = new DockPanel { LastChildFill = false };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var runner = Ui.Runner("Refresh", Ui.Stack(), _ => { }, primary: false);
            runner.Margin = new Thickness(0, 0, 10, 0);
            runner.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(runner);

            right.Children.Add(Parts.MenuButton("Export", Glyphs.Onward, new[]
            {
                Parts.Entry.Barred("Sync into the document", Glyphs.Save,
                    "This is a published NWD — its clash results are read-only"),
                Parts.Entry.Saying("Save as a clash file (BCF)", Glyphs.Round,
                                   "The offline review round — out and back", null),
                Parts.Entry.Saying("Write a report", Glyphs.Report,
                                   "One document per party, revision-lettered", null),
            }));

            // The caption the product declares. Its counted form — "Save 14 changes into the
            // document" — is built at run time from what the document refused, and a picture
            // cannot claim a caption no panel contains.
            var save = Ui.Primary("Save changes", () => { });
            save.Margin = new Thickness(8, 0, 0, 0);
            save.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(save);

            DockPanel.SetDock(right, Dock.Right);
            foot.Children.Add(right);

            return Deck("clash", "manage", Bench.Split(null, rail, board, foot, 372));
        }

        // -----------------------------------------------------------------------------------
        // Region 1 — the test finder
        // -----------------------------------------------------------------------------------

        private static UIElement Rail(bool panel)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tools = new StackPanel { Orientation = Orientation.Horizontal };

            tools.Children.Add(Parts.MoreMenu(Glyphs.Tune, "Sort: Most new",
                new[] { Parts.Entry.Act("Most new", null, null) }));

            // Accented, because a filter is in force and the chips that used to say so are gone.
            var narrow = Ui.Icon(Glyphs.Filter, "Narrowed to Mechanical. Press to change it.",
                                 () => { });

            narrow.Content = Ui.Glyph(Glyphs.Filter, 15, "Accent");
            tools.Children.Add(narrow);

            var top = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tools, Dock.Right);
            top.Children.Add(tools);
            top.Children.Add(Ui.Text(string.Empty, "Search tests by name or discipline", null, 150));

            var head = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = Ui.Stack(top),
            };

            head.Tint(Border.BorderBrushProperty, "Bd");
            grid.Children.Add(head);

            var rows = Ui.Stack(RailLabels());

            rows.Children.Add(RailRow("L03 — Mech v Struct", "25 mm", true, 18, 42, 12, 31, 65));
            rows.Children.Add(RailRow("L03 — Elec v Struct", "25 mm", false, 6, 11, 4, 8, 13));
            rows.Children.Add(RailRow("L04 — Mech v Arch", "10 mm", false, 24, 61, 30, 4, 8));
            rows.Children.Add(RailRow("Risers — All v All", "25 mm", false, 0, 0, 0, 40, 56));
            rows.Children.Add(RailRow("B1 — Drainage v Struct", "50 mm · run Mon 08:40", false, 3, 7, 2, 1, 3));
            rows.Children.Add(RailRow("L05 — Fire v Struct", "25 mm · run Mon 08:41", false, 0, 4, 9, 11, 20));

            var list = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = rows,
            };

            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            // Over the rows, not above them. Inside the head it pushed the test list down by its
            // own height, and the rail's column headings stopped lining up with the board's.
            if (panel)
            {
                var over = Narrowing();

                if (over is FrameworkElement floating)
                {
                    floating.Margin = new Thickness(10, 0, 10, 0);
                    floating.VerticalAlignment = VerticalAlignment.Top;
                    floating.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                Grid.SetRow(over, 1);
                Panel.SetZIndex(over, 2);
                grid.Children.Add(over);
            }

            var verbs = new StackPanel { Orientation = Orientation.Horizontal };
            verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run all the tests again", () => { }));

            verbs.Children.Add(Parts.MoreMenu(Glyphs.More, "Everything else you can do to tests",
                new[] { Parts.Entry.Act("Rename this test", Glyphs.Pencil, null) }, up: true));

            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(verbs, Dock.Right);
            bar.Children.Add(verbs);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 5, 0),
            };

            dot.Tint(System.Windows.Shapes.Shape.FillProperty, "RedTx");

            // Names what is queued rather than a bare figure. "14 waiting" said nothing about
            // what was waiting or on what, and the surface itself now says it in the export menu.
            var held = Ui.Link("14 changes the document has not taken", () => { });  // harness: data
            held.Margin = new Thickness(0);
            held.VerticalAlignment = VerticalAlignment.Center;

            DockPanel.SetDock(dot, Dock.Right);
            DockPanel.SetDock(held, Dock.Right);
            bar.Children.Add(held);
            bar.Children.Add(dot);

            var say = new TextBlock
            {
                Text = "3 of 6 tests · 168 results, 143 loose",           // harness: data
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            bar.Children.Add(say.Wearing("Hint"));

            var foot = new Border
            {
                Padding = new Thickness(14, 8, 14, 8),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = bar,
            };

            foot.Tint(Border.BackgroundProperty, "Pane");
            foot.Tint(Border.BorderBrushProperty, "Bd");

            Grid.SetRow(foot, 2);
            grid.Children.Add(foot);

            var pane = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
            pane.Tint(Border.BackgroundProperty, "Card");
            pane.Tint(Border.BorderBrushProperty, "Bd");

            return pane;
        }

        /// <summary>The rail's filter panel, which is where a filter is made and taken off.</summary>
        private static UIElement Narrowing()
        {
            // Every facet the product offers, at the width a real job produces. Reviewers read
            // four chips off this panel and reported that multi-select filtering did not exist.
            var body = Ui.Stack();

            void Facet(string named, string[] values, string? chosen)
            {
                body.Children.Add(Ui.Eyebrow(named));

                var wrap = new WrapPanel();

                foreach (var value in values)
                    wrap.Children.Add(Parts.Pick(
                        value, string.Equals(value, chosen, StringComparison.Ordinal), () => { }));

                body.Children.Add(wrap);
            }

            Facet("Assigned to", new[] { "J. Okonkwo", "R. Patel", "S. Aziz", "M. Lindqvist",
                                         "Unassigned" }, "J. Okonkwo");
            Facet("Discipline", new[] { "Mechanical", "Structures", "Electrical", "Architecture",
                                        "Fire", "Public health" }, "Mechanical");
            Facet("Level", new[] { "B1", "L01", "L02", "L03", "L04", "Roof" }, null);
            Facet("Folder", new[] { "Week 33", "Week 32", "Handover" }, null);
            Facet("Zone", new[] { "Core A", "Core B", "Podium" }, null);
            Facet("System", new[] { "CHW Supply", "CHW Return", "Small Power", "Wet Riser" }, null);
            Facet("Since last run", new[] { "New", "Regressed", "Unchanged" }, null);

            body.Children.Add(Ui.Check("Only tests with something open", false, null));
            body.Children.Add(Ui.Link("Clear all", () => { }));

            var panel = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = body,
            };

            panel.Tint(Border.BackgroundProperty, "Card");
            panel.Tint(Border.BorderBrushProperty, "Bd");

            panel.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.18,
            };

            return panel;
        }

        private static UIElement RailLabels()
        {
            var row = new DockPanel { LastChildFill = true };
            var figures = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var label in new[] { "New", "Active", "Rev.", "Appr.", "Res." })
            {
                var head = new TextBlock
                {
                    Text = label,
                    MinWidth = 26,
                    Margin = new Thickness(10, 0, 0, 0),
                    TextAlignment = TextAlignment.Right,
                };

                figures.Children.Add(head.Wearing("Hint"));
            }

            DockPanel.SetDock(figures, Dock.Right);
            row.Children.Add(figures);
            row.Children.Add(new TextBlock { Text = "Test" }.Wearing("Hint"));

            var band = new Border
            {
                Padding = new Thickness(27, 5, 14, 5),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            };

            band.Tint(Border.BackgroundProperty, "Card2");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        private static UIElement RailRow(string name, string hint, bool on,
                                         int fresh, int active, int reviewed, int approved,
                                         int closed)
        {
            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            var under = new TextBlock
            {
                Text = hint,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 1, 0, 0),
            };

            var row = new DockPanel { LastChildFill = true };

            var band = Parts.CountBand(new[] { fresh, active, reviewed, approved, closed },
                                       fresh + active == 0);

            DockPanel.SetDock(band, Dock.Right);
            row.Children.Add(band);
            row.Children.Add(Ui.Stack(title.Wearing("Body"), under.Wearing("Hint")));

            var tone = new Border
            {
                Padding = new Thickness(12, 8, 14, 8),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Child = row,
            };

            tone.BorderBrush = on ? Skin.Of("Accent")
                : fresh > 0 ? Skin.Of("RedTx")
                : fresh + active > 0 ? Skin.Of("AmberTx")
                : Skin.Of("Ok");

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tone };
            line.Tint(Border.BorderBrushProperty, "Bd");

            if (on) line.Tint(Border.BackgroundProperty, "NavActive");

            return line;
        }

        // -----------------------------------------------------------------------------------
        // Region 2 and 3 — the board, and the bar for what is picked
        // -----------------------------------------------------------------------------------

        private static UIElement Board()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Border
            {
                Padding = new Thickness(16, 10, 16, 0),
                Child = Ui.Stack(BoardTitle(), BoardStanding(), BoardStrip()),
            };

            grid.Children.Add(head);

            var body = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 10, 16, 16),
                Content = BoardTable(),
            };

            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var bar = Parts.SelectionBar(3, "row", () => { },
                Parts.InlineChip(null, new[] { "New", "Active" }, _ => { }, Tone.Plain, "Set status"),
                Parts.InlineChip(null, new[] { "Mechanical" }, _ => { }, Tone.Plain, "Assign to"),
                Parts.Verb("Group", Glyphs.Group, true, () => { }),
                Parts.Verb("Raise as issues", Glyphs.Issue, false, () => { }),
                Parts.MoreMenu(Glyphs.More, "Everything else for the picked rows",
                    new[] { Parts.Entry.Act("Focus all of them", Glyphs.Focus, null) }, up: true));

            Grid.SetRow(bar, 2);
            grid.Children.Add(bar);

            var ground = new Border { Child = grid };
            ground.Tint(Border.BackgroundProperty, "Bg");

            return ground;
        }

        private static UIElement BoardTitle()
        {
            var row = new DockPanel { LastChildFill = true };

            var verbs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run this test again", () => { }));
            verbs.Children.Add(Ui.Icon(Glyphs.Read, "Walk the board one clash at a time", () => { }));
            verbs.Children.Add(Ui.Icon(Glyphs.Columns, "Which columns the board shows", () => { }));

            var layers = Parts.MoreMenu(Glyphs.Layers,
                "Showing sharing an item with Pipe CHW-31 and Wall PC-200. Press to change it.",
                new[]
                {
                    Parts.Entry.Heading("Show: Both"),
                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Sharing an item with Pipe CHW-31 and Wall PC-200"),
                    Parts.Entry.Act("Every clash again, not just these", Glyphs.Close, null),
                });

            verbs.Children.Add(layers);

            verbs.Children.Add(Ui.Icon(Glyphs.Tune, "Settings", () => { }));

            DockPanel.SetDock(verbs, Dock.Right);
            row.Children.Add(verbs);

            var said = new TextBlock
            {
                Text = "Showing 18 of 168",                         // harness: data
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            var head = new TextBlock
            {
                Text = "L03 — Mech v Struct",                       // harness: data
                VerticalAlignment = VerticalAlignment.Bottom,
            };

            var words = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(head, Dock.Left);
            words.Children.Add(head.Wearing("Head"));
            words.Children.Add(said.Wearing("Hint"));

            row.Children.Add(words);

            return row;
        }

        /// <summary>
        /// What the carry-over did, in one line under the title.
        ///
        /// It runs on every board build and never said so, which is why reviewers read these
        /// screens as a product with no memory across runs.
        /// </summary>
        private static UIElement BoardStanding()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            row.Children.Add(Ui.Small(
                "150 results kept their statuses from the last run, 18 new, "
                + "3 are back after being resolved, 9 gone from the model."));   // harness: data

            return row;
        }

        private static UIElement BoardStrip()
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal };

            var tabs = new (string Label, int Count, string Key, bool On)[]
            {
                ("all", 168, "Text", false),
                ("new", 18, "RedTx", true),
                ("active", 42, "Text", false),
                ("reviewed", 12, "AmberTx", false),
                ("approved", 31, "Ok", false),
                ("resolved", 65, "Ok", false),
            };

            for (var i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];

                var figure = new TextBlock
                {
                    Text = tab.Count.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };

                figure.Tint(TextBlock.ForegroundProperty, tab.Key);

                var label = new TextBlock
                {
                    Text = tab.Label,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 0, 0),
                };

                label.Tint(TextBlock.ForegroundProperty, tab.On ? "Text" : "Faint");

                var words = new StackPanel { Orientation = Orientation.Horizontal };
                words.Children.Add(figure.Wearing("Body"));
                words.Children.Add(label);

                var segment = new Border
                {
                    Padding = new Thickness(8, 6, 8, tab.On ? 4 : 6),
                    BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, tab.On ? 2 : 0),
                    Child = words,
                };

                segment.Tint(Border.BorderBrushProperty, tab.On ? "Accent" : "Bd");
                if (tab.On) segment.Tint(Border.BackgroundProperty, "AccentWash");

                strip.Children.Add(segment);
            }

            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = strip,
            };

            pill.Tint(Border.BackgroundProperty, "Card");
            pill.Tint(Border.BorderBrushProperty, "Bd");
            pill.SetValue(UIElement.ClipToBoundsProperty, true);

            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };

            var find = Ui.Text(string.Empty, "Search", null, 128);
            find.MaxWidth = 128;
            find.VerticalAlignment = VerticalAlignment.Center;
            find.Margin = new Thickness(0, 0, 6, 0);

            DockPanel.SetDock(find, Dock.Right);
            row.Children.Add(find);
            row.Children.Add(pill);

            return row;
        }

        /// <summary>The nine columns, in the order the surface builds them.</summary>
        private static Grid Cells()
        {
            var grid = new Grid();

            void Fixed(double width) =>
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            void Flexible(double share, double least) =>
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(share, GridUnitType.Star),
                    MinWidth = least,
                });

            // No Clashes column. A group's member count is beside its name, and the width goes
            // to the two items and the property column.
            Fixed(26);
            Flexible(2, 110);
            Flexible(1, 74);
            Flexible(1, 74);
            Fixed(60);
            Flexible(1, 76);    // one property column, as somebody's own column would be
            Fixed(104);
            Fixed(104);
            Fixed(120);

            return grid;
        }

        private static void Put(Grid grid, UIElement cell, int column)
        {
            if (cell is FrameworkElement element)
            {
                element.Margin = new Thickness(0, 0, 8, 0);
                element.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        /// <summary>A column header: pressable to sort, with a handle to resize it.</summary>
        /// <param name="column">Which column, which is also what it says.</param>
        /// <param name="right">True for a figure, whose label sits over its digits.</param>
        /// <param name="on">True when the board is sorted by it.</param>
        /// <param name="unit">
        /// What the figures under it are in, for the tooltip. A declared column states its unit
        /// there rather than in the heading, which has no room for it.
        /// </param>
        private static UIElement Head(string column, bool right, bool on, string? unit = null)
        {
            var label = new TextBlock
            {
                Text = column,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            label.Wearing("Hint");
            if (on) label.Tint(TextBlock.ForegroundProperty, "Accent");

            var words = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var mark = on
                ? Ui.Glyph(Glyphs.Down, 10, "Accent",
                           right ? new Thickness(0, 0, 3, 0) : new Thickness(4, 0, 0, 0))
                : null;

            if (right && mark != null) words.Children.Add(mark);
            words.Children.Add(label);
            if (!right && mark != null) words.Children.Add(mark);

            return new Border
            {
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = (on ? "Sorted by " + column + ". Press again to turn it round."
                              : "Sort by " + column)
                          + (string.IsNullOrWhiteSpace(unit)
                                 ? string.Empty
                                 : " Figures are in " + unit + "."),
                Child = words,
            };
        }

        private static TextBlock Hint(string text) =>
            new TextBlock
            {
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            }.Wearing("Hint");

        private static TextBlock Right(TextBlock cell)
        {
            cell.TextAlignment = TextAlignment.Right;
            return cell;
        }

        private static UIElement BoardTable()
        {
            var body = Ui.Stack(TableHead());

            // Assigned to people, not to disciplines. TeamRoster holds people, roles, companies
            // and teams, and a fixture that wrote "Mechanical" in the assignee column had three
            // reviewers report that assignment to a person was missing.
            body.Children.Add(GroupRow("Corridor C — duct against beams", 7, "-48 mm", true, true));
            body.Children.Add(ClashRow("Clash 121", "Duct 400×250", "Beam 610UB", "-48 mm",
                                       "New", Tone.Bad, "R. Patel · Structures", "CHW Supply",
                                       true, false));
            body.Children.Add(ClashRow("Clash 122", "Duct 600×300", "Beam 460UB", "-31 mm",
                                       "Active", Tone.Plain, "J. Okonkwo · Mechanical",
                                       "CHW Supply", true, false));
            body.Children.Add(ClashRow("Clash 123", "Duct 600×300", "Beam 460UB", "-18 mm",
                                       "Approved", Tone.Good, "J. Okonkwo · Mechanical",
                                       "CHW Supply", true, false));
            body.Children.Add(GroupRow("Riser 2 — pipework against slab edge", 5, "-22 mm", false, false));

            body.Children.Add(Parts.ViewBand(Glyphs.Clash, "In no group", "143 results",
                                             "of 143 — tick two or more to group them, or run "
                                             + "the rules on the Coordinate tab", () => { }));

            body.Children.Add(ClashRow("Clash 208", "Pipe CHW-31", "Wall PC-200", "-52 mm",
                                       "New", Tone.Bad, string.Empty, "CHW Return", false, true));
            body.Children.Add(ClashRow("Clash 209", "Tray CT-9", "Bracing BR-3", "-14 mm",
                                       "Reviewed", Tone.Warn, "S. Aziz · Electrical",
                                       "Small Power", false, true));
            body.Children.Add(ClashRow("Clash 210", "Sprinkler SP-88", "Slab L03", "-9 mm",
                                       "Resolved", Tone.Good, "M. Lindqvist · Fire", "Wet Riser",
                                       false, false));

            var card = new Border
            {
                MinWidth = 772,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = body,
            };

            card.Tint(Border.BackgroundProperty, "Card");
            card.Tint(Border.BorderBrushProperty, "Bd");
            card.SetValue(UIElement.ClipToBoundsProperty, true);

            var sideways = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = card,
            };

            card.SetBinding(FrameworkElement.WidthProperty,
                            new System.Windows.Data.Binding("ViewportWidth") { Source = sideways });

            return sideways;
        }

        private static UIElement TableHead()
        {
            var grid = Cells();

            var all = new CheckBox
            {
                IsThreeState = true,
                IsChecked = null,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Put(grid, all, 0);
            Put(grid, Head("Group or result", false, false), 1);
            Put(grid, Head("Item 1", false, false), 2);
            Put(grid, Head("Item 2", false, false), 3);
            Put(grid, Head("Worst", true, true, "mm"), 4);
            Put(grid, Head("System Name", false, false), 5);
            Put(grid, Head("Status", false, false), 6);
            Put(grid, Head("Assigned", false, false), 7);

            var band = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };

            band.Tint(Border.BackgroundProperty, "Card2");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        private static UIElement GroupRow(string name, int count, string worst, bool open, bool made)
        {
            var grid = Cells();

            Put(grid, new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center }, 0);

            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(Ui.Glyph(open ? Glyphs.Down : Glyphs.Right, 13, "Muted",
                                        new Thickness(0, 0, 7, 0)));

            words.Children.Add(Ui.Glyph(Glyphs.Group, 15, "Accent", new Thickness(0, 0, 7, 0)));

            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            words.Children.Add(title.Wearing("Body"));

            if (made) words.Children.Add(Parts.Note("grouped by you"));

            // The member count, at the right of the name's own cell. The figure alone, with the
            // word on its tooltip: spelt out it left the group's name showing one letter.
            var many = new TextBlock
            {
                Text = count.ToString(CultureInfo.InvariantCulture),  // harness: data
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = Ui.Count(count, "clash") + " in this group",
            };

            var named = new DockPanel { LastChildFill = true };

            DockPanel.SetDock(many, Dock.Right);
            named.Children.Add(many.Wearing("Hint"));
            named.Children.Add(words);

            Put(grid, named, 1);
            Put(grid, Right(Hint("—")), 2);
            Put(grid, Right(Hint("—")), 3);
            Put(grid, Figure(worst, true), 4);

            Put(grid, Hint("—"), 5);
            Put(grid, Parts.InlineChip("New", new[] { "New" }, _ => { }, Tone.Bad, "Set status"), 6);
            Put(grid, Parts.InlineChip("Structures", new[] { "Structures" }, _ => { }, Tone.Plain, "Assign"), 7);
            var groupVerbs = Verbs();
            Put(grid, groupVerbs, 8);
            groupVerbs.Margin = new Thickness(0);

            var tone = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = Skin.Of("RedTx"),
                Child = grid,
            };

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tone };
            line.Tint(Border.BorderBrushProperty, "Bd");

            return line;
        }

        private static UIElement ClashRow(string name, string a, string b, string worst,
                                          string status, Tone tone, string who, string system,
                                          bool inside, bool picked)
        {
            var grid = Cells();

            Put(grid, new CheckBox { IsChecked = picked, VerticalAlignment = VerticalAlignment.Center }, 0);

            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(inside ? 22 : 0, 0, 8, 0),
            };

            title.Wearing("Mono");
            title.Tint(TextBlock.ForegroundProperty, "Faint");
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            Put(grid, Item(a), 2);
            Put(grid, Item(b), 3);

            Put(grid, Figure(worst, worst.StartsWith("-4", StringComparison.Ordinal)
                                    || worst.StartsWith("-5", StringComparison.Ordinal)), 4);

            Put(grid, Item(system), 5);
            Put(grid, Parts.InlineChip(status, new[] { status }, _ => { }, tone, "Set status"), 6);
            Put(grid, Parts.InlineChip(who, new[] { "Mechanical" }, _ => { }, Tone.Plain, "Assign"), 7);

            var verbs = Verbs();
            verbs.Visibility = picked ? Visibility.Visible : Visibility.Hidden;
            Put(grid, verbs, 8);
            verbs.Margin = new Thickness(0);

            var edge = status == "New" ? Skin.Of("RedTx")
                : status == "Reviewed" ? Skin.Of("AmberTx")
                : status == "Resolved" ? Skin.Of("Ok")
                : Brushes.Transparent;

            var toned = new Border
            {
                Padding = new Thickness(12, inside ? 7 : 9, 12, inside ? 7 : 9),
                BorderThickness = new Thickness(inside ? 0 : 3, 0, 0, 0),
                BorderBrush = inside ? Brushes.Transparent : edge,
                Child = grid,
            };

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = toned };
            line.Tint(Border.BorderBrushProperty, "Bd");

            if (picked) line.Tint(Border.BackgroundProperty, "NavActive");
            else if (inside) line.Tint(Border.BackgroundProperty, "Card2");

            return line;
        }

        private static TextBlock Item(string name) =>
            new TextBlock
            {
                Text = name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            }.Wearing("Lbl");

        private static FrameworkElement Verbs()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            bar.Children.Add(Ui.Icon(Glyphs.Focus, "Focus this clash", () => { }));
            bar.Children.Add(Ui.Icon(Glyphs.Eye, "Isolate this clash", () => { }));
            bar.Children.Add(Ui.Icon(Glyphs.Comment, "Comment", () => { }));

            bar.Children.Add(Parts.MoreMenu(Glyphs.More, "Everything else for this clash",
                new[]
                {
                    Parts.Entry.Act("Snapshot", Glyphs.Camera, null),
                    Parts.Entry.Rule(),
                    Parts.Entry.Act("Find other clashes with these items", Glyphs.Find, null),
                }));

            foreach (var verb in bar.Children.OfType<FrameworkElement>())
                verb.Margin = new Thickness(2, 2, 2, 2);

            return bar;
        }

        private static TextBlock Figure(string text, bool bad)
        {
            var cell = new TextBlock { Text = text, TextAlignment = TextAlignment.Right };
            cell.Wearing("Body");
            cell.Tint(TextBlock.ForegroundProperty, bad ? "RedTx" : "Muted");
            return cell;
        }

        // -----------------------------------------------------------------------------------
        // Data — the Data Manager's shape, at desk width
        // -----------------------------------------------------------------------------------

        private static UIElement Data()
        {
            var setup = Ui.Stack(
                Bench.Group(string.Empty,
                    Ui.Field("Scope", Ui.Choice(new[] { "Current selection", "Everything loaded" },
                                                "Current selection", null))),
                Bench.Group("Write something",
                    Ui.Small("Read first; every writer previews as a diff before it touches the model.")));

            var stage = Ui.Stack(
                Ui.Across(
                    Ui.Figure("232", "elements"),
                    Ui.Figure("41", "properties"),
                    Ui.Figure("3", "part-filled", Tone.Warn)),
                Ui.Table(PropertyRows(), ("Property", 180), ("Tab", 100), ("Carried by", 110), ("Looks like", 0)),
                Ui.Eyebrow("A typed value"),
                Ui.Commit("Show me what it would write", "Write", "element",
                          (job, into) => 0, job => { }));

            return Deck("data", "data",
                Bench.Surface(setup, Ui.Runner("Read properties", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> PropertyRows()
        {
            yield return new TableRow(null, "Level", "Element", "232 of 232", "Level 04");
            yield return new TableRow(null, "System Name", "Element", "198 of 232", "CHW-F") { Tone = Tone.Warn };
            yield return new TableRow(null, "Uniclass", "CamelWorks", "232 of 232", "Ss_60_40_15");
        }

        // -----------------------------------------------------------------------------------
        // Scenes — Viewpoints on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Scenes()
        {
            var setup = Ui.Stack(
                Bench.Group("Rename",
                    Ui.Field("Pattern", Ui.Text("{folder} {n:00} {name}", null, 180)),
                    Ui.Field("Start numbering at", Ui.Text("1", null, 60)),
                    Ui.Runner("Rename them", Ui.Stack(), _ => { }, primary: false)),
                Bench.Group("Render to images",
                    Ui.PathField("Into folder", Ui.Text(string.Empty, null, 180), PathKind.Folder),
                    Ui.Runner("Render them", Ui.Stack(), _ => { }, primary: false)));

            var stage = Ui.Stack(
                Ui.Across(Ui.Pill("218 viewpoints"), Ui.Pill("6 folders")),
                Ui.Table(ViewRows(), ("Viewpoint", 240), ("Folder", 140), ("Camera", 0)));

            return Deck("scenes", "viewpoints",
                Bench.Surface(setup, Ui.Runner("Save this view", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> ViewRows()
        {
            yield return new TableRow(null, "Viewpoint", "Clash review", "12.4, 8.1, 15.7");
            yield return new TableRow(null, "Viewpoint (2)", "Clash review", "10.0, 3.5, 15.7");
            yield return new TableRow(null, "L04 riser", "Week 32", "9.2, 4.4, 16.0");
        }

        // -----------------------------------------------------------------------------------
        // Deliver — Batch on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Deliver()
        {
            var setup = Ui.Stack(
                Bench.Group("Job from this document",
                    Ui.Line("9 models loaded, all with a file to re-append:", 0.8),
                    Ui.Line("ARC-Riverside-Block-B-r7.nwc", 0.6),
                    Ui.Line("STR-Riverside-Block-B-r4.nwc", 0.6),
                    Ui.Field("Call it", Ui.Text("Weekly coordination", null, 200),
                             "The Monday morning job, from what is open."),
                    Ui.Across(Ui.Button("Job from this...", () => { }))));

            var stage = Ui.Stack(
                Ui.Table(JobRows(), ("Job", 170), ("Size", 90), ("What it does", 0)),
                Ui.Danger("Delete", () => { }),
                Ui.Small("A job runs on the model that is open now, using whatever is selected now."),
                Ui.Rule(),
                Ui.Eyebrow("What has run"),
                Ui.Table(RunRows(), ("Run", 0), ("When", 110)));

            return Deck("automate", "batch",
                Bench.Surface(setup, Ui.Runner("Run the selected job", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> JobRows()
        {
            yield return new TableRow(null, "Weekly coordination", "12 nodes", "Append, Refresh, Run tests, Report");
            yield return new TableRow(null, "Friday NWD", "4 nodes", "Append, Save NWD");
        }

        private static IEnumerable<TableRow> RunRows()
        {
            yield return new TableRow(null, "ran \"Weekly coordination\": 12 steps, all ran", "Yesterday");
            yield return new TableRow(null, "ran \"Friday NWD\": stopped at step 2 — file locked", "Friday") { Tone = Tone.Bad };
        }

        // -----------------------------------------------------------------------------------
        // The pane shapes — the same anatomy, stacked at 470
        // -----------------------------------------------------------------------------------

        private static UIElement Issues()
        {
            var setup = Ui.Stack(
                Bench.Group("Where it stands",
                    Ui.Split("Open", "24"),
                    Ui.Split("Overdue", "3", Tone.Bad),
                    Ui.Split("Closed", "71", Tone.Good)),
                Bench.Group("Narrow it",
                    Ui.Field("Assigned to", Ui.Choice(new[] { "Anyone", "Mechanical", "Structures" },
                                                      "Anyone", null)),
                    Ui.Field("Raised by", Ui.Choice(new[] { "Any source", "Raised", "Clash",
                                                            "Clearance", "Imported" },
                                                    "Any source", null)),
                    Ui.Field("Find", Ui.Text(string.Empty, "title, number or assignee", null, 160))));

            var stage = Ui.Stack(
                Ui.Eyebrow("24 issues of 95 on this project"),
                Ui.Table(IssueRows(), ("Number", 80), ("Title", 220), ("Status", 90),
                         ("Assigned", 110), ("Due", 100), ("Raised by", 0)));

            return Deck("issuemgr", "issuelist",
                Bench.Surface(setup, Ui.Actions(Ui.Primary("New issue", () => { })), stage));
        }

        private static IEnumerable<TableRow> IssueRows()
        {
            yield return new TableRow(null, "CW-14", "L04 riser has no opening", "Open",
                                      "Structures", "4 Mar 2026", "Raised") { Tone = Tone.Bad };
            yield return new TableRow(null, "CW-15", "9 clashes in L03 MEP v STR", "Open",
                                      "Mechanical", "11 Mar 2026", "Clash");
            yield return new TableRow(null, "CW-16", "Clear height 1.85 m under the CHW main",
                                      "Answered", "Mechanical", "—", "Clearance") { Tone = Tone.Warn };
            yield return new TableRow(null, "CW-17", "Duct through beam", "Closed",
                                      "Structures", "—", "Imported") { Tone = Tone.Good };
        }

        private static UIElement Appearance()
        {
            var setup = Ui.Stack(
                Bench.Group("Add a layer",
                    Ui.Field("Name", Ui.Text(string.Empty, "Level 04 only", null, 160)),
                    Ui.Field("Covers", Ui.Choice(new[] { "The selection", "A saved set", "A search" },
                                                 "The selection", null)),
                    Ui.Field("Visibility", Ui.Choice(new[] { "Leave it", "Hide it", "Show it" },
                                                     "Leave it", null)),
                    Ui.Field("Colour (#rrggbb)", Ui.Text(string.Empty, "#c8102e", null, 110)),
                    Ui.Field("Transparency (0-1)", Ui.Text(string.Empty, "0.85", null, 90)),
                    Ui.Across(Ui.Button("Add layer", () => { }))));

            var stage = Ui.Stack(
                Ui.Eyebrow("Top of the list wins"),
                Layer("Clashing elements", "Red, opaque", Color.FromRgb(0xC8, 0x10, 0x2E)),
                Layer("Level 04 only", "Everything else 85% transparent", Color.FromRgb(0x9A, 0xA3, 0xAD)),
                Layer("Temporary works", "Hidden", Color.FromRgb(0xB4, 0xBC, 0xC5)),
                Ui.Across(
                    Ui.Button("Save the stack", () => { }),
                    Ui.Runner("Put the model back", Ui.Stack(), _ => { }, primary: false)),
                Ui.Rule(),
                Ui.Eyebrow("What else is going on"),
                Ui.Small("Everything hidden or overridden that no layer above accounts for."),
                Ui.Runner("Scan the model", Ui.Stack(), _ => { }, primary: false));

            return Deck("scenes", "appearance",
                Bench.Surface(setup, Ui.Runner("Apply the stack", Ui.Stack(), _ => { }), stage));
        }

        private static UIElement Layer(string name, string what, Color paint)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The literal colour the layer writes, not a Tone: a preview of the model, not of
            // the pane.
            var swatch = new Border
            {
                Width = 10,
                Height = 34,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(paint),
                Margin = new Thickness(0, 0, 10, 0),
            };

            grid.Children.Add(swatch);

            var text = Ui.Stack(Ui.Line(name, 1, true), Ui.Small(what));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            buttons.Children.Add(Ui.Icon(Glyphs.Up, "Move up", () => { }));
            buttons.Children.Add(Ui.Icon(Glyphs.Down, "Move down", () => { }));
            buttons.Children.Add(Ui.Icon(Glyphs.Bin, "Remove this layer", () => { }));

            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            var frame = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 6, 0, 6),
                Child = grid,
            };

            frame.SetResourceReference(Border.BorderBrushProperty, "Bd");
            return frame;
        }

    }
}
