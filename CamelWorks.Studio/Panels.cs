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
            Sheet("zones", "Data · Levels & zones", 1000, Zones),
            Sheet("viewpoints", "Sets · Viewpoints", 1300, Viewpoints),
            Sheet("batch", "Batch · Jobs", 900, Batch),
        };

        private static Sheet Sheet(string id, string title, int height, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { 470 } };

        // -----------------------------------------------------------------------------------
        // Project
        // -----------------------------------------------------------------------------------

        private static UIElement Health() => Sheets.Page(
            Ui.Marked(Glyphs.Model, "Models", "What arrived, and whether it is the same as last week.",
                Ui.Actions(Ui.Button("Check what arrived", () => { })),
                Ui.Done("Six models read. Two are newer than the last check.")),

            Ui.Marked(Glyphs.Tick, "Built-in checks", "Six checks over geometry, placement and data. Nothing to configure first.",
                Score("Elements with no level", "1,204 of 41,880", Tone.Warn),
                Score("Duplicate GUIDs", "none", Tone.Good),
                Score("Elements outside the site box", "3", Tone.Warn),
                Ui.Actions(Ui.Primary("Run the checks", () => { }))),

            Ui.Marked(Glyphs.Clash, "The federation", "Whether the models are where each other thinks they are.",
                Score("Shared origin agrees", "6 of 6 models", Tone.Good),
                Score("Units agree", "6 of 6 models", Tone.Good),
                Score("Overlapping extents", "MEP r4 sits 12 m north of the rest", Tone.Bad),
                Ui.Actions(Ui.Button("Check this model", () => { }))),

            Ui.Marked(Glyphs.Table, "Codes", "Needs a code table",
                Ui.Empty("No code tables on this project.",
                         "Profile takes one pasted straight out of the spreadsheet your client sent. "
                         + "Every other check here runs without one.",
                         "Go to Profile", () => { })),

            Ui.Marked(Glyphs.Report, "Client requirements", "An IDS, if the client sent one.",
                Ui.Form(Ui.Field("IDS file", Ui.Text(string.Empty, "nothing chosen", null, 220))),
                Ui.Actions(Ui.Button("Check against an IDS", () => { }))));

        private static UIElement Score(string what, string value, Tone tone)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = tone == Tone.Good ? Glyphs.Tick : tone == Tone.Bad ? Glyphs.Error : Glyphs.Warn;
            var key = tone == Tone.Good ? "Ok" : tone == Tone.Bad ? "RedTx" : "AmberTx";

            row.Children.Add(Ui.Glyph(glyph, 15, key, new Thickness(0, 0, 9, 0)));

            var label = Ui.Line(what);
            label.VerticalAlignment = VerticalAlignment.Center;
            Sheets.Add(row, 1, label);

            var right = Ui.Line(value, 0.75);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(10, 0, 0, 0);
            Sheets.Add(row, 2, right);
            return row;
        }

        private static UIElement Setup() => Sheets.Page(
            Ui.Marked(Glyphs.Tune, "What CamelWorks worked out", null,
                Ui.Field("Project name", Ui.Text("Riverside — Block B", null, 220), "derived from the models"),
                Ui.Pair(
                    Ui.Field("Units", Ui.Choice(new[] { "Metres", "Millimetres", "Feet" }, "Metres", null), "derived"),
                    Ui.Field("Discipline from", Ui.Choice(new[] { "File name", "A property" }, "File name", null), "yours")),
                Ui.Field("Level property", Ui.Text("Element / Level", null, 200), "derived from the models"),
                Ui.Actions(Ui.Primary("Save overrides", () => { }), Ui.Quiet("Reset to derived", () => { }))),

            Ui.Split("Project file", "D:\\Projects\\Riverside\\Riverside.cwproj"),

            Ui.Marked(Glyphs.Find, "Find moved files",
                "Models whose source is no longer where the federation says it is.",
                Ui.Actions(Ui.Primary("Look for missing sources", () => { })),
                Ui.Note("Two models point at a folder that no longer exists. Nothing has been changed yet.")),

            Ui.Marked(Glyphs.Save, "Model writes",
                "What CamelWorks has stamped onto elements on this project.",
                Ui.Table(Writes(), ("Property", 170), ("Elements", 0), ("Last written", 110)),
                Ui.Actions(Ui.Primary("Count what CamelWorks has written", () => { }),
                           Ui.Danger("Remove them from the model", () => { }))));

        private static IEnumerable<TableRow> Writes()
        {
            yield return new TableRow(null, "CamelWorks / Level", "41,880", "Yesterday");
            yield return new TableRow(null, "CamelWorks / Grid", "38,102", "Yesterday");
            yield return new TableRow(null, "CamelWorks / Change", "1,284", "12 minutes ago");
        }

        private static UIElement Profile() => Sheets.Page(
            Ui.Marked(Glyphs.Model, "Responsible parties",
                "Who gets what. Used by the board's assign picker and the report's per-party split.",
                Ui.Table(Parties(), ("Party", 150), ("Covers", 0), ("Issues", 70)),
                Ui.Form(Ui.Pair(
                    Ui.Field("Add a party", Ui.Text(string.Empty, "Mechanical contractor", null, 160)),
                    Ui.Field("Covers", Ui.Text(string.Empty, "MEP*", null, 120)))),
                Ui.Actions(Ui.Primary("Save", () => { }))),

            Ui.Marked(Glyphs.Table, "Code tables",
                "Paste one straight out of the spreadsheet the client sent.",
                Ui.Form(
                    Ui.Field("Table name", Ui.Text("Uniclass", null, 180)),
                    Ui.Field("Values", Ui.Text(string.Empty, "one per line, code then description", null, 300))),
                Ui.Actions(Ui.Primary("Save this table", () => { }))));

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
            Ui.Marked(Glyphs.Clash, "The board",
                "Every clash the tests found, grouped into the issues somebody actually fixes.",
                Ui.Actions(Ui.Primary("Load the board", () => { }),
                           Ui.Quiet("Undo the last board edit", () => { })),
                Ui.Eyebrow("This run"),
                Ui.Across(
                    Ui.Figure("1,284", "from the engine"),
                    Ui.Figure("96", "groups"),
                    Ui.Figure("13", "unassigned", Tone.Warn),
                    Ui.Figure("412", "suppressed")),
                Ui.Rule(),
                Ui.Eyebrow("Since the last run"),
                Ui.Across(
                    Ui.Figure("22", "new", Tone.Warn),
                    Ui.Figure("68", "still there"),
                    Ui.Figure("31", "gone", Tone.Good),
                    Ui.Figure("6", "came back", Tone.Bad)),
                Ui.Problem("6 clashes that a previous run recorded as resolved are back. Something that "
                           + "was fixed has been undone, or a model went backwards — either way it is the "
                           + "first thing to look at this week.")),

            Ui.Marked(Glyphs.Filter, "The board", null,
                Ui.Form(Ui.Pair(
                    Ui.Field("Show", Ui.Choice(new[] { "Everything", "New this week", "Came back", "Unassigned" }, "Everything", null)),
                    Ui.Field("Find", Ui.Text(string.Empty, "level, party, element…", null, 140)))),
                Ui.Table(Board(), ("Issue", 190), ("Party", 0), ("Clashes", 62)),
                Ui.Tally(5, 96, "issue")),

            Ui.Section("Raise an issue", "For what the clash engine cannot see", false,
                Ui.Small("It joins the board and behaves like everything else on it."),
                Ui.Form(
                    Ui.Field("What is wrong", Ui.Text(string.Empty, "Riser has no access panel", null, 240)),
                    Ui.Pair(
                        Ui.Field("Party", Ui.Choice(new[] { "Architect", "Structures", "Mechanical" }, "Architect", null)),
                        Ui.Field("Priority", Ui.Segmented(new[] { "Low", "Med", "High" }, "Med", null)))),
                Ui.Actions(Ui.Primary("Raise it against the selection", () => { }))),

            Ui.Section("Freeze", "Not frozen — the board follows every run", false,
                Ui.Small("A frozen board stops moving when the models do, so a week's issue numbers stay "
                         + "the numbers everybody was sent."),
                Ui.Actions(Ui.Button("Freeze what is on the board", () => { }),
                           Ui.Quiet("Change notice", () => { }))),

            Ui.Section("Headroom", "Not checked on this model", false,
                Ui.Small("Clear height under everything the services hang from, reported as a list of "
                         + "places rather than as a number."),
                Ui.Actions(Ui.Button("Check clear height", () => { }))));

        private static IEnumerable<TableRow> Board()
        {
            yield return new TableRow(null, "Duct through transfer beam", "Mechanical", "34") { Tone = Tone.Bad };
            yield return new TableRow(null, "Sprinkler main at Level 04 core", "Mechanical", "18") { Tone = Tone.Warn };
            yield return new TableRow(null, "Column clash with riser wall", "Structures", "12");
            yield return new TableRow(null, "Door swing into pipework", "Architect", "9");
            yield return new TableRow(null, "Cable tray under beam soffit", "Nobody yet", "7") { Tone = Tone.Warn };
        }

        private static UIElement Tests() => Sheets.Page(
            Ui.Marked(Glyphs.Table, "Build a matrix",
                "Every discipline against every other, as clash tests, in one go.",
                Ui.Form(
                    Ui.Field("Group by", Ui.Choice(new[] { "Discipline", "Model", "Level" }, "Discipline", null)),
                    Ui.Pair(
                        Ui.Field("Tolerance", Ui.Text("0.01", null, 80)),
                        Ui.Field("Type", Ui.Segmented(new[] { "Hard", "Clearance" }, "Hard", null))),
                    Ui.Field("Put them in", Ui.Text("Week 33", null, 160))),
                Ui.Actions(Ui.Primary("Create the ticked ones", () => { }))),

            Ui.Marked(Glyphs.Clash, "Tests in this document", null,
                Ui.Table(TestRows(), ("Test", 180), ("Last run", 0), ("Clashes", 66)),
                Ui.Actions(Ui.Primary("Run them", () => { }), Ui.Quiet("Show me", () => { }))));

        private static IEnumerable<TableRow> TestRows()
        {
            yield return new TableRow(null, "ARC vs STR", "Yesterday", "218");
            yield return new TableRow(null, "ARC vs MEP", "Yesterday", "604");
            yield return new TableRow(null, "STR vs MEP", "Yesterday", "462");
            yield return new TableRow(null, "MEP vs MEP", "Never", "—") { Tone = Tone.Warn };
        }

        private static UIElement Rules() => Sheets.Page(
            Ui.Info("Rules run in order: suppress and flag, then group, then assign. Nothing is applied "
                    + "until you press the button, and the board says what each rule did."),

            Ui.Marked(Glyphs.Filter, "Suppress and flag", "First match wins.",
                Rule("Under 5 mm", "suppressed", Tone.Plain),
                Rule("Insulation against structure", "suppressed", Tone.Plain),
                Rule("Anything on the transfer level", "flagged", Tone.Warn),
                Ui.Actions(Ui.Quiet("+ Add a rule", () => { }))),

            Ui.Numbered(2, "Group", "Applied in order.",
                Rule("Within 5 m of each other", "one issue", Tone.Plain),
                Rule("Same pair of models", "one issue", Tone.Plain),
                Ui.Actions(Ui.Quiet("+ Add a step", () => { }))),

            Ui.Numbered(3, "Assign", "First match wins.",
                Rule("MEP-M* against anything", "Mechanical", Tone.Plain),
                Rule("STR* against ARC*", "Structures", Tone.Plain),
                Ui.Actions(Ui.Quiet("+ Add an assignment", () => { }))),

            Ui.Actions(Ui.Primary("Save rules", () => { }), Ui.Button("Let the rules decide this again", () => { })));

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
            Ui.Marked(Glyphs.Report, "Report", "One file, from a built-in template.",
                Ui.Form(
                    Ui.Field("Format", Ui.Segmented(new[] { "PDF", "XLSX", "HTML" }, "PDF", null)),
                    Ui.Pair(
                        Ui.Field("Contents", Ui.Choice(new[] { "Everything", "New only", "Per party" }, "Everything", null)),
                        Ui.Field("For", Ui.Choice(new[] { "The team", "The client", "Site" }, "The team", null))),
                    Ui.Field("Write to", Ui.Text("D:\\Projects\\Riverside\\Reports", null, 260)),
                    Ui.Field("Name it", Ui.Text("{project} — week {week}", null, 220),
                             "The name carries the project, the week and the party.")),
                Ui.Actions(Ui.Primary("Write the report", () => { }), Ui.Button("Write a worksheet", () => { }))),

            Ui.Marked(Glyphs.Camera, "Pictures", "A key plan and a view per issue.",
                Ui.Toggle("Put a rendered view on every issue", true, null),
                Ui.Toggle("Put a key plan on the cover", true, null),
                Ui.Small("Rendering moves the camera once per issue and puts it back at the end.")));

        private static UIElement Bcf() => Sheets.Page(
            Ui.Marked(Glyphs.Onward, "Export", "BCF 2.1 or 3.0, with a camera on every topic.",
                Ui.Form(
                    Ui.Pair(
                        Ui.Field("Version", Ui.Segmented(new[] { "2.1", "3.0" }, "2.1", null)),
                        Ui.Field("Topics", Ui.Choice(new[] { "Everything", "New only", "One party" }, "Everything", null))),
                    Ui.Field("File", Ui.Text("D:\\Projects\\Riverside\\Riverside-w33.bcf", null, 280))),
                Ui.Toggle("Put a rendered view on every topic", true, null),
                Ui.Actions(Ui.Primary("Export the board", () => { }))),

            Ui.Marked(Glyphs.Model, "Import", "Read a response back, and see what it would change first.",
                Ui.Actions(Ui.Primary("Read a BCF file", () => { }), Ui.Button("Read a sheet back", () => { })),
                Ui.Note("Nothing is applied until the conflicts below have been looked at.")));

        // -----------------------------------------------------------------------------------
        // Compare
        // -----------------------------------------------------------------------------------

        private static UIElement CompareRun() => Sheets.Page(
            Ui.Marked(Glyphs.Compare, "Compare against", "Pick a baseline and see what changed.",
                Ui.Form(Ui.Field("Baseline", Ui.Choice(
                    new[] { "Week 32 — Friday issue", "Week 30", "Week 28" }, "Week 32 — Friday issue", null))),
                Ui.Section("What counts as a change",
                           "moves over 0.001 model units · standard properties compared · GUID, tree path or shape",
                           false,
                           Ui.Small("Every default here works on a raw model."),
                           Ui.Form(
                               Ui.Pair(
                                   Ui.Field("Move over", Ui.Text("0.001", null, 80)),
                                   Ui.Field("Properties", Ui.Choice(new[] { "Standard list", "Every property", "No properties" }, "Standard list", null))),
                               Ui.Field("Match on", Ui.Choice(new[] { "GUID, tree path or shape", "GUID or tree path", "GUID only" }, "GUID, tree path or shape", null))),
                           Ui.Toggle("Read the actual triangles", false, null)),
                Ui.Actions(Ui.Primary("Compare", () => { }))),

            Ui.Marked(Glyphs.Save, "Keep it", "A baseline is a snapshot beside the document, not a copy of it.",
                Ui.Actions(Ui.Primary("Take a baseline", () => { }))),

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

            Ui.Actions(Ui.Button("Stamp it into the properties", () => { }),
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
            Ui.Marked(Glyphs.Save, "Record one now",
                "Kept beside the document, so a job handed over arrives with its history.",
                Ui.Form(Ui.Field("Call it", Ui.Text("Week 33 — Friday issue", null, 240))),
                Ui.Actions(Ui.Primary("Take a baseline", () => { }))),

            Ui.Marked(Glyphs.Clock, "Recorded", null,
                Ui.Table(BaselineRows(), ("Baseline", 172), ("Elements", 0), ("Taken", 96)),
                Ui.Actions(Ui.Button("Compare against this one", () => { }),
                           Ui.Danger("Delete the selected one", () => { }))));

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
            Ui.Marked(Glyphs.Table, "Properties",
                "Every property the elements carry, folded to one row each.",
                Ui.Form(Ui.Field("Scope", Ui.Segmented(new[] { "Whole model", "Selection", "A saved set" }, "Whole model", null))),
                Ui.Actions(Ui.Primary("Read properties", () => { }))),

            Ui.Marked(Glyphs.Table, "What the elements carry", null,
                Ui.Table(Props(), ("Property", 175), ("Value", 0), ("Elements", 74))),

            Ui.Section("Write a property", "Nothing staged", false,
                Ui.Form(
                    Ui.Pair(
                        Ui.Field("Tab", Ui.Text("CamelWorks", null, 110)),
                        Ui.Field("Property", Ui.Text("Zone", null, 110))),
                    Ui.Field("Value", Ui.Text(string.Empty, "one value for every element in scope", null, 240))),
                Ui.Actions(Ui.Primary("Show me what would change", () => { }),
                           Ui.Button("Write it", () => { }))));

        private static IEnumerable<TableRow> Props()
        {
            yield return new TableRow(null, "Element / Level", "Level 00 … Level 06", "41,880");
            yield return new TableRow(null, "Element / Category", "Walls, Doors, Ducts …", "41,880");
            yield return new TableRow(null, "Revit Type / Fire Rating", "varies", "12,044");
            yield return new TableRow(null, "IFC / GlobalId", "unique per element", "41,880");
            yield return new TableRow(null, "CamelWorks / Zone", "not set", "0") { Tone = Tone.Warn };
        }

        private static UIElement Zones() => Sheets.Page(
            Ui.Marked(Glyphs.Layers, "Levels & zones",
                "Grids where the federation has them, the models' own levels where they do not, and a "
                + "height histogram where neither — and it says which.",
                Ui.Actions(Ui.Primary("Derive levels", () => { })),
                Ui.Done("Seven levels from the models' own level property.")),

            Ui.Marked(Glyphs.Table, "What it found", null,
                Ui.Table(Levels(), ("Level", 130), ("Height", 0), ("Elements", 78)),
                Ui.Actions(Ui.Button("Stamp the level onto every element", () => { }),
                           Ui.Button("Show me the grid", () => { }))));

        private static IEnumerable<TableRow> Levels()
        {
            yield return new TableRow(null, "Level 00", "+0.000", "8,204");
            yield return new TableRow(null, "Level 01", "+4.200", "7,918");
            yield return new TableRow(null, "Level 02", "+8.100", "7,660");
            yield return new TableRow(null, "Level +3.600", "+3.600", "412") { Tone = Tone.Warn };
        }

        private static UIElement Viewpoints() => Sheets.Page(
            Ui.Marked(Glyphs.Camera, "Saved viewpoints", null,
                Ui.Table(Views(), ("Viewpoint", 190), ("Folder", 0), ("Saved", 90)),
                Ui.Actions(Ui.Primary("Save this view", () => { }), Ui.Quiet("Select them", () => { }))),

            Ui.Section("Rename in bulk", "{n} — {name}, starting at 1", false,
                Ui.Form(Ui.Pair(
                    Ui.Field("Pattern", Ui.Text("{n} — {name}", null, 150)),
                    Ui.Field("Start at", Ui.Text("1", null, 60)))),
                Ui.Actions(Ui.Primary("Rename all", () => { }))),

            Ui.Section("File them into a folder", "Not set", false,
                Ui.Form(Ui.Field("Folder", Ui.Text(string.Empty, "Week 33", null, 180))),
                Ui.Actions(Ui.Primary("Move them", () => { }))),

            Ui.Section("A viewpoint per issue", "One per group on the board", false,
                Ui.Form(Ui.Pair(
                    Ui.Field("Folder", Ui.Text("Week 33 issues", null, 150)),
                    Ui.Field("Name pattern", Ui.Text("{n} — {name}", null, 150)))),
                Ui.Actions(Ui.Primary("Save one per group on the board", () => { }))),

            Ui.Section("Render every viewpoint to an image", "Not run", false,
                Ui.Form(Ui.Field("Write to", Ui.Text("D:\\Projects\\Riverside\\Views", null, 250))),
                Ui.Actions(Ui.Primary("Render them all", () => { }))));

        private static IEnumerable<TableRow> Views()
        {
            yield return new TableRow(null, "01 — Transfer level looking north", "Week 33 issues", "Yesterday");
            yield return new TableRow(null, "02 — Riser at core", "Week 33 issues", "Yesterday");
            yield return new TableRow(null, "Site approach", "—", "3 weeks ago");
        }

        private static UIElement Batch() => Sheets.Page(
            Ui.Marked(Glyphs.Play, "Jobs", null,
                Ui.Table(Jobs(), ("Job", 180), ("Steps", 60), ("Last run", 0)),
                Ui.Tally(2, 2, "job"),
                Ui.Actions(Ui.Primary("Run \"Friday issue\"", () => { }),
                           Ui.Quiet("Open on the canvas", () => { }),
                           Ui.Danger("Delete the selected one", () => { }))),

            Ui.Marked(Glyphs.Clock, "History", null,
                Ui.Table(Runs(), ("Job", 150), ("Outcome", 0), ("When", 96)),
                Ui.Tally(3, 18, "run")),

            Ui.Marked(Glyphs.Add, "Job from this document",
                "Nine models are open. This makes the job you already do by hand every Monday.",
                Ui.Actions(Ui.Button("Make one from what is open", () => { }))));

        private static IEnumerable<TableRow> Jobs()
        {
            yield return new TableRow(null, "Friday issue", "6", "Yesterday");
            yield return new TableRow(null, "Nightly health check", "3", "This morning");
        }

        private static IEnumerable<TableRow> Runs()
        {
            yield return new TableRow(null, "Nightly health check", "Finished — 3 steps", "This morning") { Tone = Tone.Good };
            yield return new TableRow(null, "Friday issue", "Finished — 6 steps", "Yesterday") { Tone = Tone.Good };
            yield return new TableRow(null, "Friday issue", "Stopped at step 4 — no clash engine", "Last Friday") { Tone = Tone.Bad };
        }
    }
}
