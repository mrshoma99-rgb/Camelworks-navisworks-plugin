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
using CamelWorks.Core.Search;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
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
            // The Manage board, drawn by the tab that ships it. This page used to be a table
            // written here, because the board is the clash engine's results and a seat with no
            // document has no engine — so the one page whose faults have all been in the layout
            // code was the one page no render could show. ClashManageViews.Drawn takes the
            // engine, and what is drawn is the tab, read, measured by WPF.
            Page("app-clash-manage", "Clash Manager · Manage", 980, () => Managing(false)),

            // The same board with the rail's filter panel open: the disciplines, the folders and
            // the naming tokens this read actually holds, drawn as a card over the test list.
            Page("app-clash-finder", "Clash Manager · Manage, the filter panel open", 980,
                 () => Managing(true)),
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
                 () => Deck("clash", "exchange", new Border
                 {
                     Height = 500,
                     Child = ClashExchangeViews.Drawn(Filed(), "Riverside", Sent(),
                                                      importing: true, returned: Returned()),
                 })),

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

            // The form that makes one clash test, as the Create tab's panel holds it: the two
            // sides, the kind and the tolerance, with both sides unset, which is the state it
            // opens in and the one that has to say what to do next.
            // Not in the render's default list; named on dispatch.
            Page("app-clash-new", "Clash Manager · A new clash test", 620, NewClash),
        };

        /// <summary>The new-clash-test form, as it opens.</summary>
        private static UIElement NewClash()
        {
            var seat = Seated();

            // Built here rather than looked for on the seat. The harness seat has no clash
            // engine, so it registers no clash check and this page failed on every render with
            // "sequence contains no elements" — the one page whose whole point is to be looked at
            // was the one nobody could see.
            var clash = new ClashCheck(seat.Clash, seat.MetresPerUnit, seat.ClashProblem);

            var body = new Border
            {
                Padding = new Thickness(18, 16, 18, 16),
                Child = ClashTestViews.Drawn(seat, clash, "Clash 1"),
            };

            body.Tint(Border.BackgroundProperty, "Bg");

            return body;
        }

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

            var heights = new ClearanceSettings
            {
                Surfaces = "Level 03 Floors",
                Targets = ClearanceSettings.NotTheSurfaces,
                MinimumMetres = 2.1,
                ToleranceMetres = 0.05,
                DetectSurfaces = true,
                WorstPerPatch = true,
            };

            // Two storeys, so the render shows what a list-valued scope looks like rather than
            // the one-value case the panel used to be limited to.
            heights.Levels.Add("Level 03");
            heights.Levels.Add("Level 04");

            heights.Write(measured);

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
                LevelName = "Level 03",
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
        /// Clash Manager - Manage, drawn by the tab itself over a seat and a clash engine.
        ///
        /// The engine is a fixture and everything else is the seat: the project file, the
        /// findings, the naming and the roster are read exactly as they are on a licensed seat,
        /// and the read is the one the tab's own "Read the results" runs.
        /// </summary>
        /// <param name="panel">Whether the rail's filter panel is open.</param>
        private static UIElement Managing(bool panel)
        {
            var seat = Seated();
            var engine = Engined();

            Decided(seat, engine);

            return Deck("clash", "manage", ClashManageViews.Drawn(seat, engine, panel));
        }

        /// <summary>
        /// The decisions this project has already made about its clashes.
        ///
        /// A row's status comes from the project's own register rather than from the host: a read
        /// adopts only the two host statuses that are a human judgement, Reviewed and Approved,
        /// because New, Active and Resolved are the engine's own bookkeeping. So a board read into
        /// an empty register has two of its five status segments at nought whatever the engine
        /// says, and those two are exactly the ones somebody sets by working the board.
        ///
        /// This is that work: the register takes the read's own findings and a spread of decisions
        /// over them, so the strip, the row colours and the rail's counts all have something to
        /// count.
        /// </summary>
        /// <param name="seat">The seat whose register to write.</param>
        /// <param name="engine">The clash engine the board is about to read.</param>
        private static void Decided(ISeat seat, IClashSource engine)
        {
            var read = new ClashCheck(engine, seat.MetresPerUnit).Read(null, null);
            var register = seat.Findings.Findings;

            // A fixed moment, so two renders of this page date the findings the same way.
            var when = new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Utc).Ticks;

            register.Observe(FindingSource.Clash, read.Findings, when,
                             origin: seat.Store.Path, scope: read.Scope);

            // By id, so the same clashes carry the same decision on every render. The register is
            // a dictionary and its enumeration order changes on any remove.
            var held = register.Present.OrderBy(one => one.Id, StringComparer.Ordinal).ToList();

            for (var at = 0; at < held.Count; at++)
                if (at % 5 == 1) held[at].Status = FindingStatus.Active;
                else if (at % 5 == 4) held[at].Status = FindingStatus.Resolved;

            seat.Findings.Save();
        }

        /// <summary>A search of one condition.</summary>
        /// <param name="term">The condition.</param>
        private static ResultQuery Search(QueryTerm term) =>
            new ResultQuery().With(new ResultQuery(term));

        /// <summary>A search of two conditions, both of which have to hold.</summary>
        /// <param name="first">One condition.</param>
        /// <param name="second">The other.</param>
        private static ResultQuery Group(QueryTerm first, QueryTerm second) =>
            Search(first).With(new ResultQuery(second));

        /// <summary>
        /// The Filter tab with a project's rules on it.
        ///
        /// The cards are written into the scratch project on the way in, as
        /// <see cref="Creating"/> writes its tests: the store is shared by every page, so a page
        /// that needs data puts it there itself rather than relying on the order the harness draws
        /// in. Nothing is saved, so the empty Filter page still opens on an empty project.
        ///
        /// What is written is what the list has to draw: all six actions, a name long enough to be
        /// trimmed, and a rule turned off. The bands go in too, for the Settings tab that owns
        /// them.
        /// </summary>
        private static UIElement Ruled()
        {
            var seat = Seated();
            var rules = new ClashRuleSet();

            var ducts = new ResultQuery();

            ducts.Inside.Add(new ResultQuery(
                new QueryTerm(ResultFields.Category, QueryOp.Is)
                {
                    Where = QueryWhere.FirstElement,
                    Text = "Ducts",
                }));

            // A bracket whose two conditions are joined by "or", so the harness draws the chip
            // and the bar the sketch calls for.
            var framing = new ResultQuery();

            framing.With(new ResultQuery(
                new QueryTerm(ResultFields.Category, QueryOp.Is)
                {
                    Where = QueryWhere.SecondElement,
                    Text = "Structural Framing",
                }));

            framing.With(new ResultQuery(
                new QueryTerm(ResultFields.Category, QueryOp.Is)
                {
                    Where = QueryWhere.SecondElement,
                    Text = "Structural Columns",
                }), QueryJoin.Any);

            ducts.With(framing);

            rules.Cards.Add(new ClashCard("Ducts through structural framing", ducts,
                                          ClashActionKind.Assign) { Value = "Mechanical" });

            // Searches of every shape the editor draws: one condition, a stack of two, a bracket,
            // an element property read from an ancestor, and a number range.
            rules.Cards.Add(new ClashCard("Insulation grazing insulation",
                Search(new QueryTerm(ResultFields.Column(ClashFindings.Distance), QueryOp.AtMost)
                {
                    Number = 0.001,
                }),
                ClashActionKind.Suppress));

            var sleeves = new ResultQuery();

            sleeves.Inside.Add(new ResultQuery(
                new QueryTerm(ResultFields.Category, QueryOp.Is)
                {
                    Where = QueryWhere.AnyElement,
                    Text = "Sleeves",
                }));

            sleeves.Inside.Add(new ResultQuery(
                new QueryTerm(ResultFields.Property("Element", "Mark"), QueryOp.StartsWith)
                {
                    Where = QueryWhere.AnyElement,
                    Text = "REV21",
                    Ancestors = 1,
                }));

            rules.Cards.Add(new ClashCard(
                "Sleeves already coordinated against the structural openings schedule REV21",
                sleeves, ClashActionKind.Suppress));

            rules.Cards.Add(new ClashCard("Touching, not passing through",
                Search(new QueryTerm(ResultFields.Column(ClashFindings.Distance), QueryOp.Between)
                {
                    Number = 0,
                    Number2 = 0.005,
                }),
                ClashActionKind.Flag));

            rules.Cards.Add(new ClashCard("Cable tray not yet grouped",
                Group(new QueryTerm(ResultFields.Category, QueryOp.Is)
                      {
                          Where = QueryWhere.AnyElement,
                          Text = "Cable Tray",
                      },
                      new QueryTerm(ResultFields.Group, QueryOp.IsEmpty)),
                ClashActionKind.Group) { Value = "Electrical containment" });

            rules.Cards.Add(new ClashCard("Sprinkler branches already agreed",
                Group(new QueryTerm(ResultFields.Property("Element", "System Name"), QueryOp.Contains)
                      {
                          Where = QueryWhere.AnyElement,
                          Text = "SPR",
                      },
                      new QueryTerm(ResultFields.Status, QueryOp.Is) { Text = "Active" }),
                ClashActionKind.Status) { Value = "Approved" });

            rules.Cards.Add(new ClashCard("Duplicated by the coordination model",
                Search(new QueryTerm(ResultFields.Model, QueryOp.Same)
                {
                    Where = QueryWhere.BetweenElements,
                }),
                ClashActionKind.Remove) { IsEnabled = false });

            // The bands live on the Settings tab now, and are written here so that tab has a
            // stack to draw.
            rules.Grouping.Add(new GroupingSpec("level"));
            rules.Grouping.Add(new GroupingSpec("proximity") { A = 2.5 });
            rules.Grouping.Add(new GroupingSpec("property") { Name = "System Name" });

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

            for (var at = 0; at < BoardSize; at++) made.Add(Boarded(at));

            return ClashPipeline.Run(made).Groups;
        }

        /// <summary>How many clashes the board holds. Enough that the tests have sixteen each.</summary>
        private const int BoardSize = 96;

        /// <summary>Who the board's clashes are assigned to.</summary>
        private static readonly string[] SentParties =
            { "Mechanical", "Structure", "Electrical", "Architecture" };

        /// <summary>
        /// The six tests the board's clashes came from, as the host holds them.
        ///
        /// Each side is a saved set, because that is where the host records what a test is
        /// between: the rail states the two disciplines and the filter panel's chips are the
        /// values it found, and a test built out of an ad-hoc selection records neither. Two
        /// clearances among four hard tests, three folders counting the root, and four
        /// disciplines over the six.
        /// </summary>
        private static readonly (string Name, string? Folder, string A, string B,
                                 MatrixTest Kind, double Tolerance)[] SentShapes =
        {
            ("L02 MEP v STR", "Weekly", "Mechanical", "Structure", MatrixTest.Hard, 0.025),
            ("L03 MEP v STR", "Weekly", "Mechanical", "Structure", MatrixTest.Hard, 0.025),
            ("L04 MEP v ARC", "Weekly", "Mechanical", "Architecture", MatrixTest.Clearance, 0.05),
            ("L02 ARC v STR", "Handover", "Architecture", "Structure", MatrixTest.Hard, 0.025),
            ("Basement services", "Handover", "Electrical", "Structure", MatrixTest.Hard, 0.01),
            ("Roof plant", null, "Mechanical", "Electrical", MatrixTest.Clearance, 0.075),
        };

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
            var shape = SentShapes[at % SentShapes.Length];

            var item = ClashFixture(at, shape.Name, SentLevels[at % SentLevels.Length]);

            item.TestFolder = shape.Folder;
            item.AssignedTo = SentParties[at % SentParties.Length];
            item.Status = (FindingStatus)(at % 5);

            return item;
        }

        /// <summary>
        /// A clash engine holding the board's own clashes, for the Manage page.
        ///
        /// The Manage board is what the engine reports, so a page of it needs an engine. These are
        /// the clashes <see cref="Boarded"/> makes, which is what the exchange packs are written
        /// from, so the two pages describe one project rather than two.
        ///
        /// What is set here is what the board draws: six tests of two kinds in three folders,
        /// between four disciplines, sixteen results each over the host's five status words and
        /// four parties, part of one test in two named groups with the rest of it loose, a comment
        /// trail on one result, and a run date on every test so the header line can carry one. A
        /// board of one test, one status and no groups draws none of that.
        /// </summary>
        private static IClashSource Engined()
        {
            var engine = new FakeClashSource();
            var ids = new List<string>();

            for (var test = 0; test < SentShapes.Length; test++)
            {
                var shape = SentShapes[test];

                // Made rather than declared, because a test made from a spec is the only one the
                // fake records two sides for, and the two sides are what the rail says a test is
                // between and what the filter panel's discipline chips are read from.
                var made = engine.Create(new ClashTestSpec(
                    shape.Name, ClashSide.Set(shape.A), ClashSide.Set(shape.B),
                    shape.Tolerance, shape.Folder, shape.Kind));

                if (made == null) continue;

                var id = made.Value.Id;

                ids.Add(id);

                var results = 0;

                for (var at = test; at < BoardSize; at += SentShapes.Length)
                {
                    var item = Boarded(at);

                    engine.Finding(item.Key.A, item.Key.B, item.X, item.Y, item.Z,
                                   Hosted(item.Status));

                    var result = "r" + (++results).ToString(CultureInfo.InvariantCulture);

                    engine.Assign(id, new[] { result }, item.AssignedTo ?? string.Empty);
                }

                // Dated, so the board's header line says when the test last ran rather than that
                // it never has.
                engine.Run(id);
            }

            if (ids.Count == 0) return engine;

            // On the first test only, and over part of it: the board bands grouped results under
            // a row of their own, and the loose ones sit beside them.
            engine.Regroup(ids[0], new[] { "r1", "r2", "r3", "r4", "r5" },
                           "Corridor C - duct against beams");

            engine.Regroup(ids[0], new[] { "r6", "r7", "r8" },
                           "Riser 2 - pipework against the slab edge");

            engine.Comment(ids[0], new[] { "r1" },
                           "Re-routed above the beam; waiting on the structural opening.",
                           "Mechanical");

            return engine;
        }

        /// <summary>
        /// The host's word for a status.
        ///
        /// The five words the host parses are spelled the same as the five members of
        /// <see cref="FindingStatus"/>, but they are the host's vocabulary rather than this
        /// product's, and the engine refuses anything it does not know.
        /// </summary>
        /// <param name="status">The status.</param>
        private static string Hosted(FindingStatus status) => status switch
        {
            FindingStatus.Active => "Active",
            FindingStatus.Reviewed => "Reviewed",
            FindingStatus.Approved => "Approved",
            FindingStatus.Resolved => "Resolved",
            _ => "New",
        };

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
