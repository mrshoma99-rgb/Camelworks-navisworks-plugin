using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// Every remaining screen, reconstructed one by one.
    ///
    /// Sheets.cs holds the pages that prove the vocabulary; this holds the panels themselves. The
    /// distinction matters: a widget set can be faultless and still compose into a screen nobody
    /// can use, and until every panel had been looked at on its own the only thing that had really
    /// been reviewed was the parts bin.
    ///
    /// Each of these mirrors the composition of the real screen — same cards in the same order,
    /// same field labels, same button captions, same table columns — with plausible project data
    /// where the real one reads the host. Where a reconstruction and its screen drift apart the
    /// reconstruction is wrong, and fixing it is part of changing the screen.
    /// </summary>
    public static class Panels
    {
        /// <summary>The panels, in the order the pane's switcher offers them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Sheet("health", "Project · Health", 1500, Health),
            Sheet("setup", "Project · Setup", 900, Setup),
            Sheet("profile", "Project · Profile", 1100, Profile),
            Sheet("triage", "Coordinate · Triage", 1700, Triage),
            Sheet("tests", "Coordinate · Tests", 1100, Tests),
            Sheet("rules", "Coordinate · Rules", 1200, Rules),
            Sheet("report", "Coordinate · Report", 1000, Report),
            Sheet("bcf", "Coordinate · BCF", 1100, Bcf),
            Sheet("compare", "Compare · Run", 1100, CompareRun),
            Sheet("changes", "Compare · Changes", 1500, Changes),
            Sheet("baselines", "Compare · Baselines", 900, Baselines),
            Sheet("data", "Data · Properties", 1100, Properties),
            Sheet("zones", "Data · Levels", 1000, Zones),
            Sheet("viewpoints", "Sets · Viewpoints", 1300, Viewpoints),
            Sheet("batch", "Batch · Jobs", 900, Batch),
        };

        private static Sheet Sheet(string id, string title, int height, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { 470 } };

        // -----------------------------------------------------------------------------------
        // Project
        // -----------------------------------------------------------------------------------

        private static UIElement Health() => Sheets.Page(
            Ui.Marked(Glyphs.Tick, "Is this fit to coordinate against?",
                "Placement, geometry, what has arrived and what it is coded as, in one pass over "
                + "the models.",
                Ui.Actions(Ui.Primary("Check this federation", () => { })),
                Ui.Section("Which property carries the code",
                    "Uniclass, read from a property of the same name", false,
                    Ui.Form(
                        Ui.Field("Table", Ui.Choice(new[] { "Uniclass", "COBie type" }, "Uniclass", null)),
                        Ui.Field("Property", Ui.Text("Uniclass", null, 240),
                                 "\"Category / Name\", or a bare name for any category"))),

                Verdict("Not fit to coordinate against \u2014 2 problems to settle first.", "RedTx"),
                Ui.Across(
                    Ui.Figure("2", "to settle", Tone.Bad),
                    Ui.Figure("3", "worth a look", Tone.Warn),
                    Ui.Figure("4", "clean")),
                Ui.Small("41,880 elements across 6 models, read once."),

                Ui.Finding(true, "Placement", "1 model has been moved by the federation since the last sweep",
                    "Nobody is told when this happens, and every clash from that moment on is in the wrong place.",
                    "Run the clash tests again once it is back.",
                    "MEP r4.nwc"),
                Ui.Across(Ui.Button("Put MEP r4 back where it was", () => { })),

                Ui.Finding(true, "Codes", "412 elements carry a value Uniclass does not list",
                    "Either the table is missing a row or the model is carrying a value nobody agreed "
                    + "to, and the difference matters: one is a five-second edit and the other is a "
                    + "conversation.",
                    null,
                    "Pr_20_93_52, Ss_25_10_30_25, EF_25_10"),
                Ui.Finding(false, "Geometry", "1,204 elements sit exactly on the project origin while the rest of the model does not",
                    null,
                    "Check the source file's shared coordinates before running clashes: everything "
                    + "here will clash with everything else here.",
                    "ARCH r7 (4 more)"),
                Ui.Finding(false, "The federation", "2 models have changed since this file was saved",
                    "The federation is showing what was published when it was saved, not what is on disk now.",
                    "Refresh, then run this again.",
                    "STR r3.nwc, MEP r4.nwc"),
                Ui.Finding(false, "Placement", "1 model carries a transform",
                    "That is legitimate - it is how a survey-grid model is brought onto a project grid - "
                    + "and it is worth knowing which ones, because a transform is also the thing nobody "
                    + "remembers applying.",
                    null,
                    "SITE r1.nwc"),

                Ui.Section("What is loaded", "6 models in this federation", false,
                    Ui.Table(new[]
                    {
                        new TableRow("a", "ARCH r7.nwc", "Architecture", "14 Aug 2026 09:12", @"P:\Riverside\ARCH r7.nwc"),
                        new TableRow("b", "STR r3.nwc", "Structure", "changed since this file was saved", @"P:\Riverside\STR r3.nwc") { Tone = Tone.Warn },
                        new TableRow("c", "MEP r4.nwc", "Services", "changed since this file was saved", @"P:\Riverside\MEP r4.nwc") { Tone = Tone.Warn },
                    }, ("Model", 150), ("Discipline", 100), ("Source", 190), ("Path", 0)))),

            Ui.Marked(Glyphs.Report, "The client's own rules",
                "Answer an IDS — the openBIM way of writing information requirements down — against "
                + "this federation. Point it at the file and press the button; there is nothing else "
                + "to fill in.",
                Ui.PathField("IDS file", Ui.Text(string.Empty, "nothing chosen", null, 260),
                             PathKind.Open, "IDS|*.ids|All files|*.*"),
                Ui.Actions(Ui.Primary("Check against an IDS", () => { })),
                Ui.Section("Where the IFC class is read from", "wherever the readers put it", false,
                    Ui.Field("IFC class from", Ui.Text(string.Empty, "nothing", null, 200),
                             "leave it empty unless a run tells you to fill it in"),
                    Ui.Small("IDS is written about IFC classes, and a federation only carries those "
                             + "where a model came from IFC — so CamelWorks looks in the places the "
                             + "readers put them and believes a value only if it reads like a class "
                             + "name."))));

        /// <summary>The one line the whole panel exists to say.</summary>
        private static UIElement Verdict(string text, string key)
        {
            var headline = Ui.Heading(text);
            headline.Tint(TextBlock.ForegroundProperty, key);
            return headline;
        }

        private static UIElement Setup() => Sheets.Page(
            Ui.Marked(Glyphs.Tune, "What CamelWorks worked out", null,
                Ui.Field("Project name", Ui.Text("Riverside — Block B", null, 220), "derived from the models"),
                Ui.Pair(
                    Ui.Field("Units", Ui.Choice(new[] { "Metres", "Millimetres", "Feet" }, "Metres", null), "derived"),
                    Ui.Field("Discipline from", Ui.Choice(new[] { "File name", "A property" }, "File name", null), "yours")),
                Ui.Field("Level property", Ui.Text("Element / Level", null, 200), "derived from the models"),
                Ui.Actions(Ui.Primary("Save overrides", () => { }), Ui.Quiet("Reset them all to derived", () => { }))),

            Ui.Split("Project file", @"D:\Projects\Riverside\Riverside.cwproj"));

        private static UIElement Profile() => Sheets.Page(
            Ui.Small(@"Kept beside the document in D:\Projects\Riverside\Riverside.cwproj"),

            Ui.Marked(Glyphs.Onward, "Who a clash can be given to",
                "Comma separated. Derived from the disciplines in the loaded models until you say "
                + "otherwise.",
                Ui.Form(
                    Ui.Field("Parties", Ui.Text("Architect, Structures, Mechanical, Electrical", null, 380)),
                    Ui.Field("Priorities", Ui.Text("Low, Medium, High", null, 380))),
                Ui.Actions(Ui.Button("Save", () => { })),
                Ui.Small("Leave these alone and the board still works: the assignee is free text and "
                         + "the priorities are the built-in three. Filling them in turns two "
                         + "free-text boxes into pickers, which is worth about a minute a week.")),

            Ui.Marked(Glyphs.Table, "Code tables",
                "The values a coded property is allowed to hold, and the ones it is not.",
                Ui.Table(Parties(), ("Table", 150), ("Covers", 0), ("Rows", 70)),
                Ui.Section("Paste one in", "code, description, parent - one per line", false,
                    Ui.Form(
                        Ui.Field("Name", Ui.Text("Uniclass", null, 180)),
                        Ui.Field("Rows", Ui.Text(string.Empty, "Pr_20_93_52, Concrete beam", null, 420),
                                 "commas or tabs between them, and a line starting with # is a comment")),
                    Ui.Actions(Ui.Button("Save this table", () => { })),
                    Ui.Small("Paste the column straight out of the spreadsheet your client sent."))),

            Ui.Marked(Glyphs.Bin, "Take back what CamelWorks wrote",
                "Everything the product has put into the model itself, and the way to remove it.",
                Ui.Actions(Ui.Primary("Count what CamelWorks has written", () => { })),
                Ui.Small("Ctrl+Z does not undo a property write - Navisworks does not put custom "
                         + "property writes on its undo stack. What CamelWorks wrote is recorded in "
                         + "the project journal, and removing it here is the way back.")));

        private static IEnumerable<TableRow> Parties()
        {
            yield return new TableRow(null, "Architect", "ARC*", "31");
            yield return new TableRow(null, "Structures", "STR*", "18");
            yield return new TableRow(null, "Mechanical", "MEP-M*", "34");
            yield return new TableRow(null, "Nobody yet", "—", "13") { Tone = Tone.Warn };
        }

        // -----------------------------------------------------------------------------------
        // Coordinate
        // -----------------------------------------------------------------------------------

        private static UIElement Triage() => Sheets.Page(
            Ui.Actions(Ui.Primary("Load the board", () => { }),
                       Ui.Quiet("Undo the last board edit", () => { })),

            Ui.Across(
                Ui.Figure("22", "new", Tone.Warn),
                Ui.Figure("6", "came back", Tone.Bad),
                Ui.Figure("31", "gone", Tone.Good),
                Ui.Figure("13", "unassigned", Tone.Warn)),

            Ui.Problem("6 clashes that a previous run recorded as resolved are back. Something that "
                       + "was fixed has been undone, or a model went backwards — either way it is the "
                       + "first thing to look at this week."),

            Ui.Eyebrow("Who owes what"),
            Ui.Line("Mechanical 52   Architect 21   Structures 14   Nobody yet 13", 0.75),

            Ui.Section("How this board was made", "1,284 results in, 96 issues out", false,
                Ui.Across(
                    Ui.Figure("1,284", "from the engine"),
                    Ui.Figure("412", "suppressed"),
                    Ui.Figure("96", "grouped into"),
                    Ui.Figure("68", "still there")),
                Ui.Line("1,284 results, 412 suppressed, 96 groups.", 0.8),
                Ui.Line("Rules: Ignore insulation touches, Same-model pairs, Level from grid", 0.7)),

            Ui.Form(Ui.Pair(
                Ui.Field("Show", Ui.Choice(new[] { "Everything", "Unassigned", "New or back again", "Mine" }, "Everything", null)),
                Ui.Field("Find", Ui.Text(string.Empty, "level, party, element…", null, 140)))),

            Ui.Table(Board(), ("Issue", 190), ("Party", 0), ("Clashes", 62)),
            Ui.Tally(5, 96, "issue"),

            Ui.Rule(),

            Ui.Section("Raise an issue", "For what the clash engine cannot see", false,
                Ui.Small("It joins the board and behaves like everything else on it."),
                Ui.Form(
                    Ui.Field("What is wrong", Ui.Text(string.Empty, "Riser has no access panel", null, 240)),
                    Ui.Pair(
                        Ui.Field("Party", Ui.Choice(new[] { "Architect", "Structures", "Mechanical" }, "Architect", null)),
                        Ui.Field("Priority", Ui.Segmented(new[] { "Low", "Med", "High" }, "Med", null)))),
                Ui.Actions(Ui.Primary("Raise it against the selection", () => { }))),

            Ui.Marked(Glyphs.Save, "Freeze",
                "Sign-off and fabrication release are the same record, and both come with a change notice",
                Ui.Form(
                    Ui.Field("What is being frozen", Ui.Text(string.Empty, "Level 04 services", null, 220)),
                    Ui.Pair(
                        Ui.Field("Signed by", Ui.Text(string.Empty, null, 180)),
                        Ui.Field("Release reference", Ui.Text(string.Empty, null, 140)))),
                Ui.Actions(Ui.Button("Freeze what is on the board", () => { }),
                           Ui.Quiet("Change notice", () => { }))));

        private static IEnumerable<TableRow> Board()
        {
            yield return new TableRow(null, "Duct through transfer beam", "Mechanical", "34") { Tone = Tone.Bad };
            yield return new TableRow(null, "Sprinkler main at Level 04 core", "Mechanical", "18") { Tone = Tone.Warn };
            yield return new TableRow(null, "Column clash with riser wall", "Structures", "12");
            yield return new TableRow(null, "Door swing into pipework", "Architect", "9");
            yield return new TableRow(null, "Cable tray under beam soffit", "Nobody yet", "7") { Tone = Tone.Warn };
        }

        private static UIElement Tests() => Sheets.Page(
            Ui.Across(
                Ui.Pill("4 tests"),
                Ui.Pill("1,284 results"),
                Ui.Pill("1 never run", Tone.Warn)),

            Ui.Table(TestRows(), ("Test", 180), ("Last run", 0), ("Clashes", 66)),
            Ui.Actions(Ui.Primary("Run all 4 tests", () => { }),
                       Ui.Button("Run only the 1 that never have", () => { })),

            Ui.Rule(),

            Ui.Marked(Glyphs.Clash, "Build a matrix",
                "Two lists, every pairing between them, made in the engine in one go. Nothing has to "
                + "be set up first: the disciplines come from what CamelWorks already worked out about "
                + "the models that are loaded.",
                Ui.Form(Ui.Pair(
                    Ui.Field("Rows", Ui.Choice(new[] { "By discipline", "By model" }, "By discipline", null)),
                    Ui.Field("Columns", Ui.Choice(new[] { "By discipline", "By model" }, "By discipline", null)))),
                Ui.Actions(Ui.Primary("Show me the grid", () => { })),
                Ui.Section("How the tests are made", "Named {A} v {B}, at 0.010 m", false,
                    Ui.Form(
                        Ui.Field("Name pattern", Ui.Text("{A} v {B}", null, 180)),
                        Ui.Field("Tolerance (m)", Ui.Text("0.010", null, 80)),
                        Ui.Field("Put them in folder", Ui.Text("Week 33", null, 160))),
                    Ui.Check("Include each side against itself", false, null))),

            Ui.Marked(Glyphs.Layers, "Clear height",
                "The check a clash test cannot do: a duct 1.9 m above a corridor clashes with nothing.",
                Ui.Form(Ui.Field("Minimum clear height (m)", Ui.Text("2.100", null, 80))),
                Ui.Actions(Ui.Primary("Check clear height", () => { })),
                Ui.Section("What counts as a floor", "Floors and slabs, under everything else", false,
                    Ui.Form(
                        Ui.Field("Surfaces", Ui.Choice(new[] { "Floors and slabs", "Current selection", "Everything" }, "Floors and slabs", null)),
                        Ui.Field("Look above at", Ui.Choice(new[] { "Everything else", "Current selection", "Everything" }, "Everything else", null))))));

        private static IEnumerable<TableRow> TestRows()
        {
            yield return new TableRow(null, "ARC vs STR", "Yesterday", "218");
            yield return new TableRow(null, "ARC vs MEP", "Yesterday", "604");
            yield return new TableRow(null, "STR vs MEP", "Yesterday", "462");
            yield return new TableRow(null, "MEP vs MEP", "Never", "—") { Tone = Tone.Warn };
        }

        private static UIElement Rules() => Sheets.Page(
            Ui.Numbered(1, "Suppress and flag", "Applied to every result before anything else.",
                Rule("Under 5 mm", "suppressed", Tone.Plain),
                Rule("Insulation against structure", "suppressed", Tone.Plain),
                Rule("Anything on the transfer level", "flagged", Tone.Warn),
                Ui.Across(Ui.Quiet("Add", () => { }))),

            Ui.Numbered(2, "Group", "Outermost rule first. Empty means the built-in stack.",
                Rule("Within 5 m of each other", "one issue", Tone.Plain),
                Rule("Same pair of models", "one issue", Tone.Plain),
                Ui.Across(Ui.Quiet("Add", () => { }))),

            Ui.Numbered(3, "Assign", "First match wins.",
                Rule("MEP-M* against anything", "Mechanical", Tone.Plain),
                Rule("STR* against ARC*", "Structures", Tone.Plain),
                Ui.Across(Ui.Quiet("Add", () => { }))),

            Ui.Actions(Ui.Primary("Try these on the board", () => { }),
                       Ui.Button("Save rules", () => { })),

            Ui.Across(
                Ui.Figure("1,284", "from the engine"),
                Ui.Figure("412", "suppressed", Tone.Warn),
                Ui.Figure("96", "issues"),
                Ui.Figure("13", "unassigned", Tone.Warn)),

            Ui.Line("1,284 results, 412 suppressed, 96 groups.", 0.8),
            Ui.Line("Rules: Under 5 mm, Insulation against structure, Within 5 m of each other", 0.7),
            Ui.Small("Nothing has been saved. This is what the board would look like if it were."),

            Ui.Small("With nothing here the board still works: it groups by model pair, then level, "
                     + "then proximity, and assigns nothing. These rules narrow that; they do not "
                     + "switch it on."));

        private static UIElement Rule(string when, string then, Tone tone)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = Ui.Line(when);
            left.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(left);

            var pill = Ui.Pill(then, tone);
            Sheets.Add(row, 1, pill);
            Sheets.Add(row, 2, Ui.Icon(Glyphs.Bin, "Remove this rule", () => { }));
            return row;
        }

        private static UIElement Report() => Sheets.Page(
            Ui.Marked(Glyphs.Report, "Write a report", null,
                Ui.Form(
                    Ui.Field("Contents", Ui.Choice(
                        new[] { "Full report", "Field pack - one issue per page", "Penetration schedule" },
                        "Full report", null, 280)),
                    Ui.Small("The whole board, laid out for a desk: every issue with its clashes, "
                             + "party and priority."),
                    Ui.Pair(
                        Ui.Field("For", Ui.Choice(new[] { "Everyone", "Only the unassigned", "Mechanical" }, "Everyone", null, 180)),
                        Ui.Field("Format", Ui.Choice(new[] { "PDF", "XLSX", "HTML" }, "PDF", null, 120)))),
                Ui.Actions(Ui.Primary("Write the report", () => { })),
                Ui.Small("Each issue takes the next revision letter and its cover says which one it "
                         + "supersedes. A PDF in an inbox that looks exactly like last week's is the "
                         + "most common reason a trade builds from the wrong information."),
                Ui.Section("Where it goes, and what else is in it",
                    "Beside the document, with a key plan", false,
                    Ui.Form(
                        Ui.Field("Write to", Ui.Text(string.Empty, "beside the document", null, 260),
                                 "blank writes it beside the document"),
                        Ui.Field("Name it", Ui.Text("{project} - {date} - {party}", null, 260),
                                 "carries {project}, {date}, {party}, {rev}")),
                    Ui.Check("Only what the last run had not seen", false, null),
                    Ui.Check("A plan of the site with every issue marked", true, null),
                    Ui.Check("One file per party, not one file", false, null),
                    Ui.Check("A picture of each group", false, null),
                    Ui.Small("A picture moves your view while it runs and puts it back afterwards. "
                             + "On a board of two hundred groups that is two hundred renders, so it "
                             + "is off until you ask for it."))));

        private static UIElement Bcf() => Sheets.Page(
            Ui.Marked(Glyphs.Onward, "Export", "BCF 2.1 or 3.0, with a camera on every topic.",
                Ui.Form(
                    Ui.Pair(
                        Ui.Field("Version", Ui.Segmented(new[] { "2.1", "3.0" }, "2.1", null)),
                        Ui.Field("Topics", Ui.Choice(new[] { "Everything", "New only", "One party" }, "Everything", null))),
                    Ui.Field("File", Ui.Text("D:\\Projects\\Riverside\\Riverside-w33.bcf", null, 280))),
                Ui.Toggle("Put a rendered view on every topic", true, null),
                Ui.Actions(Ui.Primary("Send the board", () => { }))),

            Ui.Marked(Glyphs.Model, "Import", "Read a response back, and see what it would change first.",
                Ui.Form(Ui.Field("File", Ui.Text(string.Empty, "a .bcf or the .xlsx you sent out", null, 280))),
                Ui.Actions(Ui.Primary("Read it", () => { })),
                Ui.Note("Nothing is applied until the conflicts below have been looked at.")));

        // -----------------------------------------------------------------------------------
        // Compare
        // -----------------------------------------------------------------------------------

        private static UIElement CompareRun() => Sheets.Page(
            Ui.Form(Ui.Field("Compare against", Ui.Choice(
                new[] { "Week 32 — Friday issue", "Week 30", "Week 28" }, "Week 32 — Friday issue", null, 320),
                "the most recent is picked for you")),

            Ui.Actions(Ui.Primary("Compare", () => { })),

            Ui.Section("What counts as a change",
                "moves over 0.001 model units · standard properties compared · GUID, tree path or shape",
                false,
                Ui.Form(
                    Ui.Pair(
                        Ui.Field("Move over", Ui.Text("0.001", null, 80),
                                 "model units. Below this an element has not moved"),
                        Ui.Field("Properties", Ui.Choice(new[] { "Standard list", "Every property", "No properties" }, "Standard list", null))),
                    Ui.Field("Match on", Ui.Choice(new[] { "GUID, tree path or shape", "GUID or tree path", "GUID only" }, "GUID, tree path or shape", null))),
                Ui.Toggle("Read the actual triangles", false, null)),

            Ui.Section("Record a new baseline", "the model as it stands right now", false,
                Ui.Form(Ui.Field("Call it", Ui.Text("2026-08-24", null, 220))),
                Ui.Actions(Ui.Button("Take a baseline", () => { }))),

            Ui.Marked(Glyphs.Model, "Which model was held to be which", null,
                Ui.Table(Pairing(), ("Baseline", 150), ("Now", 0), ("Matched on", 96))));

        private static IEnumerable<TableRow> Pairing()
        {
            yield return new TableRow(null, "ARC-Block-B-r6", "ARC-Block-B-r7", "same stem");
            yield return new TableRow(null, "STR-Block-B-r2", "STR-Block-B-r2", "same path");
            yield return new TableRow(null, "MEP-Block-B-r3", "MEP-Block-B-r4", "same stem");
            yield return new TableRow(null, "FAC-Block-B-r1", "—", "nothing") { Tone = Tone.Warn };
        }

        private static UIElement Changes() => Sheets.Page(
            Ui.Eyebrow("Week 32 — Friday issue  →  now"),

            Ui.Across(
                Ui.Figure("612", "added", Tone.Good),
                Ui.Figure("208", "removed", Tone.Bad),
                Ui.Figure("1,044", "moved", Tone.Warn),
                Ui.Figure("96", "changed")),

            Ui.Actions(Ui.Primary("Paint it onto the model", () => { }),
                       Ui.Button("Take the colours off", () => { })),

            Ui.Actions(Ui.Button("Bring the old revision in", () => { })),
            Ui.Small("208 removed elements are in the baseline and not in this document. "
                     + "CamelWorks appends it, hidden, and takes it out again afterwards."),

            Ui.Across(
                Ui.Field("Show", Ui.Choice(new[] { "Everything", "Added", "Removed", "Moved", "Changed" }, "Everything", null)),
                Ui.Field("Model", Ui.Choice(new[] { "All models", "ARC r7", "STR r2", "MEP r4" }, "All models", null)),
                Ui.Field("Find", Ui.Text(string.Empty, "element, type…", null, 130))),

            Ui.Table(ChangeRows(), ("Element", 168), ("Model", 74), ("What changed", 0)),
            Ui.Tally(5, 1960, "change"),

            Ui.Rule(),

            Ui.Actions(Ui.Button("Stamp 1,960 changes into the properties", () => { }),
                       Ui.Button("Raise them on the board", () => { }),
                       Ui.Button("Save the report", () => { }),
                       Ui.Button("Make a viewpoint for each place", () => { })));

        private static IEnumerable<TableRow> ChangeRows()
        {
            yield return new TableRow(null, "Basic Wall: 200 blockwork", "ARC r7", "Moved 340 mm east") { Tone = Tone.Warn };
            yield return new TableRow(null, "Rectangular Duct 400x250", "MEP r4", "Added") { Tone = Tone.Good };
            yield return new TableRow(null, "Pipe Type: chilled flow", "MEP r4", "Removed") { Tone = Tone.Bad };
            yield return new TableRow(null, "Structural Column UC254", "STR r2", "Type changed");
            yield return new TableRow(null, "Floor: 150 RC slab", "STR r2", "Resized") { Tone = Tone.Warn };
        }

        private static UIElement Baselines() => Sheets.Page(
            Ui.Small("Give it a name you will recognise in three months — an issue number, a "
                     + "milestone, a date."),
            Ui.Form(Ui.Field("Call it", Ui.Text("Week 33 — Friday issue", null, 240))),
            Ui.Actions(Ui.Primary("Take a baseline", () => { })),

            Ui.Rule(),

            Ui.Eyebrow("Recorded"),
            Ui.Small(@"D:\Projects\Riverside\.camelworks\baselines"),
            Ui.Table(BaselineRows(), ("Baseline", 172), ("Elements", 0), ("Taken", 96)),
            Ui.Actions(Ui.Button("Take a baseline", () => { }),
                       Ui.Danger("Delete the selected one", () => { })));

        private static IEnumerable<TableRow> BaselineRows()
        {
            yield return new TableRow(null, "Week 32 — Friday issue", "128,400", "6 days ago");
            yield return new TableRow(null, "Week 30", "126,918", "3 weeks ago");
            yield return new TableRow(null, "Week 28", "119,204", "5 weeks ago");
        }

        // -----------------------------------------------------------------------------------
        // Data and Sets
        // -----------------------------------------------------------------------------------

        private static UIElement Properties() => Sheets.Page(
            Ui.Form(Ui.Field("Scope", Ui.Segmented(new[] { "Selection", "Everything loaded" }, "Selection", null))),
            Ui.Actions(Ui.Primary("Read properties", () => { })),

            Ui.Line("41,880 elements, 62 properties", 1, true),
            Ui.Table(Props(), ("Property", 175), ("Value", 0), ("Elements", 74)),

            Ui.Marked(Glyphs.Save, "Write to the elements",
                "Into a tab of its own called \"CamelWorks\", so it is never mixed with the "
                + "authoring tool's data. Nothing is written until you have seen the diff, element "
                + "by element.",
                Ui.Form(
                    Ui.Field("The value comes from", Ui.Choice(new[]
                    {
                        "a value you type", "a formula over other properties",
                        "a code table", "a spreadsheet, out and back",
                    }, "a value you type", null, 260)),
                    Ui.Field("If a value is already there",
                             Ui.Choice(new[] { "leave it alone", "replace it" }, "leave it alone", null))),
                Ui.Form(
                    Ui.Field("Name", Ui.Text("Zone", null, 150)),
                    Ui.Field("Value", Ui.Text("Level 04 — East", null, 200))),
                Ui.Actions(Ui.Primary("Show me what would change", () => { })),
                Ui.Across(
                    Ui.Figure("11,208", "to add", Tone.Good),
                    Ui.Figure("402", "already set", Tone.Warn),
                    Ui.Figure("0", "would empty"),
                    Ui.Figure("1,940", "already right")),
                Ui.Actions(Ui.Primary("Write 11,208 elements", () => { })),
                Ui.Line("402 elements will be left as they are because they already hold something.", 0.7),
                Ui.Small("Ctrl+Z will not undo a write. Navisworks does not put property writes on "
                         + "its undo stack, so CamelWorks records what it changed in the project "
                         + "file instead of pretending otherwise.")),

            Ui.Marked(Glyphs.Tune, "Rename or remove a column",
                "Only columns in CamelWorks' own tab. Renaming what the authoring tool wrote would "
                + "make the model disagree with the model it came from.",
                Ui.Form(Ui.Pair(
                    Ui.Field("Column", Ui.Text("Zone", null, 150),
                             "as it is spelled in the CamelWorks tab"),
                    Ui.Field("New name", Ui.Text("Zone code", null, 150)))),
                Ui.Actions(Ui.Primary("Count it", () => { })),
                Ui.Line("11,208 elements carry it, out of 41,880 elements in scope.", 1, true),
                Ui.Across(
                    Ui.Button("Rename it", () => { }),
                    Ui.Danger("Remove it from 11,208 elements", () => { }))));

        private static IEnumerable<TableRow> Props()
        {
            yield return new TableRow(null, "Element / Level", "Level 00 … Level 06", "41,880");
            yield return new TableRow(null, "Element / Category", "Walls, Doors, Ducts …", "41,880");
            yield return new TableRow(null, "Revit Type / Fire Rating", "varies", "12,044");
            yield return new TableRow(null, "IFC / GlobalId", "unique per element", "41,880");
            yield return new TableRow(null, "CamelWorks / Zone", "not set", "0") { Tone = Tone.Warn };
        }

        private static UIElement Zones() => Sheets.Page(
            Ui.Actions(Ui.Primary("Derive levels", () => { })),

            Ui.Line("Seven levels, from the models' own level property.", 1, true),
            Ui.Line("A grid would have been used had the federation carried one.", 0.75),

            Ui.Table(Levels(), ("Level", 130), ("Height", 0), ("Elements", 78)),

            Ui.Field("Worked out from", Ui.Choice(new[]
            {
                "The models' own level property (7)",
                "The coordination grid (9)",
                "A histogram of element heights (6)",
            }, "The models' own level property (7)", null, 260)),

            Ui.Rule(),

            Ui.Actions(Ui.Button("Stamp the level onto every element", () => { })),
            Ui.Small("Writes Level into the CamelWorks tab, so clash grouping, reports and set "
                     + "rules can all use it — including on models that never had a level property "
                     + "to begin with."));

        private static IEnumerable<TableRow> Levels()
        {
            yield return new TableRow(null, "Level 00", "+0.000", "8,204");
            yield return new TableRow(null, "Level 01", "+4.200", "7,918");
            yield return new TableRow(null, "Level 02", "+8.100", "7,660");
            yield return new TableRow(null, "Level +3.600", "+3.600", "412") { Tone = Tone.Warn };
        }

        private static UIElement Viewpoints() => Sheets.Page(
            Ui.Actions(Ui.Primary("Save this view", () => { })),

            Ui.Table(Views(), ("Viewpoint", 190), ("Folder", 0), ("Saved", 90)),
            Ui.Small("Tick the ones to act on. With nothing ticked, everything below acts on all "
                     + "of them — except deleting, which never does."),

            Ui.Section("Rename them", "{n}. {name}, numbered from 1", false,
                Ui.Form(Ui.Pair(
                    Ui.Field("Pattern", Ui.Text("{n}. {name}", null, 200),
                             "{n} the number, {name} the current name, {folder} the folder"),
                    Ui.Field("Start at", Ui.Text("1", null, 60)))),
                Ui.Actions(Ui.Button("Rename them", () => { }))),

            Ui.Section("File them into a folder", "Coordination", false,
                Ui.Form(Ui.Field("Into folder", Ui.Text("Coordination", null, 180),
                                 "made if it is not there; one already in it is left where it is")),
                Ui.Actions(Ui.Button("Move them", () => { }))),

            Ui.Section("Render them to images", "1920 x 1080, numbered in order", false,
                Ui.Form(Ui.Pair(
                    Ui.Field("Write into", Ui.Text(string.Empty, "your documents", null, 250)),
                    Ui.Field("Size", Ui.Choice(new[] { "1280 x 720", "1920 x 1080", "2560 x 1440" }, "1920 x 1080", null)))),
                Ui.Actions(Ui.Button("Render them", () => { }))),

            Ui.Section("Copy overrides onto the rest", "from 01. Transfer level looking north", false,
                Ui.Form(Ui.Field("Take them from", Ui.Choice(
                    new[] { "01. Transfer level looking north", "02. Riser at core" },
                    "01. Transfer level looking north", null, 260))),
                Ui.Actions(Ui.Button("Copy them across", () => { }))),

            Ui.Section("Delete them", "asks once more first", false,
                Ui.Actions(Ui.Danger("Delete the ticked ones", () => { }))),

            Ui.Rule(),

            Ui.Marked(Glyphs.Camera, "A viewpoint per issue",
                "The walkthrough a coordination meeting is actually run from: one saved view per "
                + "group on the board, framed on the group.",
                Ui.Form(Ui.Pair(
                    Ui.Field("Folder", Ui.Text("Coordination", null, 180)),
                    Ui.Field("Name pattern", Ui.Text("{n}. {group}", null, 200),
                             "{n} number, {group} group, {who} party, {count} clashes"))),
                Ui.Actions(Ui.Primary("Save one per group on the board", () => { }))));

        private static IEnumerable<TableRow> Views()
        {
            yield return new TableRow(null, "01 — Transfer level looking north", "Week 33 issues", "Yesterday");
            yield return new TableRow(null, "02 — Riser at core", "Week 33 issues", "Yesterday");
            yield return new TableRow(null, "Site approach", "—", "3 weeks ago");
        }

        private static UIElement Batch() => Sheets.Page(
            Ui.Table(Jobs(), ("Job", 180), ("Steps", 60), ("Last run", 0)),
            Ui.Tally(2, 2, "job"),

            Ui.Actions(Ui.Primary("Run the selected job", () => { })),
            Ui.Danger("Delete the selected job", () => { }),
            Ui.Small("A job runs on the model that is open now, using whatever is selected now. "
                     + "Nothing in it is scheduled or unattended: Navisworks has to be running, "
                     + "and so do you."),

            Ui.Rule(),

            Ui.Marked(Glyphs.Add, "Job from this document",
                "Nine models are open. This makes the job you already do by hand every Monday.",
                Ui.Actions(Ui.Primary("Make a job from what is open", () => { }))),

            Ui.Marked(Glyphs.Clock, "What has run", null,
                Ui.Table(Runs(), ("Run", 0), ("When", 110)),
                Ui.Tally(3, 18, "run")));

        private static IEnumerable<TableRow> Jobs()
        {
            yield return new TableRow(null, "Friday issue", "6", "Yesterday");
            yield return new TableRow(null, "Nightly health check", "3", "This morning");
        }

        private static IEnumerable<TableRow> Runs()
        {
            yield return new TableRow(null, "Nightly health check — finished, 3 steps", "This morning") { Tone = Tone.Good };
            yield return new TableRow(null, "Friday issue — finished, 6 steps", "Yesterday") { Tone = Tone.Good };
            yield return new TableRow(null, "Friday issue — stopped at step 4, no clash engine", "Last Friday") { Tone = Tone.Bad };
        }
    }
}
