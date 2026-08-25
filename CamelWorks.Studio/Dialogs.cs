using System;
using System.Collections.Generic;
using System.Windows;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The dialogue windows, reconstructed.
    ///
    /// These were the one part of the product nobody had ever looked at. The panes have been
    /// rendered here since the harness was built; the fourteen windows were added during a sprint
    /// that closed a capability gap, went straight into the product, and were reviewed by reading
    /// their layout code - which is how all fourteen ended up with ninety-eight buttons of exactly
    /// one weight and not a single one marked as the action the window is for.
    ///
    /// Same rule as <see cref="Panels"/>: same cards in the same order, same captions, same button
    /// weights, plausible data where the real one reads the host. Where a reconstruction and its
    /// window disagree the reconstruction is wrong, and fixing it is part of changing the window.
    ///
    /// They are wider than the panes because they are windows: a dialogue is sized to the work,
    /// which was the whole reason for having them.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>The windows, in the order the ribbon offers them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Sheet("dlg-review", "Window · Review a finding", 720, 620, Review),
            Sheet("dlg-clashxml", "Window · Clash XML", 940, 760, ClashXml),
            Sheet("dlg-ids", "Window · IDS authoring", 1000, 1120, Ids),
            Sheet("dlg-datalink", "Wizard · External Data", 640, 900, DataLink),
            Sheet("dlg-laststep", "Wizard · The last step", 620, 900, LastStep),
        };

        private static Sheet Sheet(string id, string title, int height, int width, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { width } };

        // -----------------------------------------------------------------------------------
        // Review a finding — the walk through a clash board, one group at a time
        // -----------------------------------------------------------------------------------

        private static UIElement Review() => Sheets.Page(
            Ui.Line("7 of 42 - Level 04 riser vs structural frame", 1, true),
            Ui.Line("12 clashes - Structures - 12.480, 4.100, 15.750 m", 0.85),
            Ui.Line("Move the duct 340 mm east", 0.85),

            Ui.Across(
                Ui.Icon(Glyphs.Backward, "Previous finding (P)", () => { }),
                Ui.Icon(Glyphs.Onward, "Next finding (N)", () => { }),
                Ui.Icon(Glyphs.Boxed, "Section box round this one (B)", () => { })),

            Ui.Field("Where this one stands",
                Ui.Segmented(new[] { "Active", "Reviewed", "Approved", "Resolved" }, "Reviewed", null)),

            Ui.Field("What to do", Ui.Stack(
                Ui.Across(
                    Ui.Choice(new[] { "Move", "Reroute", "Resize", "Accept" }, "Move", null),
                    Ui.Choice(new[] { "east", "west", "north", "south", "up", "down" }, "east", null),
                    Ui.Text("340", null, 90)),
                Ui.Actions(Ui.Primary("Record", () => { })))),

            Ui.Line("Recorded.", 0.75),

            Ui.Note("Keys: N and P move, 1 reviewed, 2 approved, 3 resolved, 0 active, "
                    + "B boxes, A annotates. Nothing is written until this window closes."));

        // -----------------------------------------------------------------------------------
        // Clash XML — read a board in, write one out, against a named profile
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The write leg, which is the one the switch opens on.
        ///
        /// The point of the picture is what is folded. Which results go in and how the file is
        /// written are options, and the profile that remembers them is a convenience, so all three
        /// are shut. What is open is the board, the file and the one button the tool exists for -
        /// where the loudest thing used to be a button that saves a settings file.
        /// </summary>
        private static UIElement ClashXml() =>
            // In the real frame, because the frame is most of what somebody sees when a tool opens
            // and every picture before this one left it out.
            ToolWindow.Frame(Tools.Find("clashxml"), Sheets.Page(
            Ui.Segmented(new[] { "Write one", "Read one back" }, "Write one", null),

            Ui.Eyebrow("The tests"),
            Ui.Line("Six tests on this board, 764 results between them.", 1, true),
            Ui.Table(Tests(), ("Test", 0), ("Clashes", 90), ("In", 60)),

            Ui.Section("What goes in", "which results, and whose", false,
                Ui.Small("Untick the ones the receiving tool has no use for."),
                Ui.Actions(
                    Ui.Quiet("Tick every test", () => { }),
                    Ui.Quiet("Untick every test", () => { }))),

            Ui.Section("How it is written", "folders, elements, units", false,
                Ui.Check("Keep the folders as folders", true, null),
                Ui.Check("Write each result's two elements", true, null)),

            Ui.Section("This profile", "keep these settings for next time", false,
                Ui.Field("Called", Ui.Text("Weekly issue to the contractor", null, 300)),
                Ui.Actions(
                    Ui.Quiet("Keep", () => { }),
                    Ui.Danger("Forget", () => { }),
                    Ui.Quiet("Save the profile to a file...", () => { }),
                    Ui.Quiet("Load a profile from a file...", () => { }))),

            Ui.Rule(),

            Ui.Field("File", Ui.Text(@"D:\Projects\Riverside\Clash\week-31.xml", null, 380)),
            Ui.Actions(Ui.Runner("Show me what would go in", Ui.Stack(), _ => { }))));

        private static IEnumerable<TableRow> Tests()
        {
            yield return new TableRow(null, "Structure vs Mechanical", "412", "yes");
            yield return new TableRow(null, "Structure vs Electrical", "88", "yes");
            yield return new TableRow(null, "Mechanical vs Electrical", "203", "yes");
            yield return new TableRow(null, "Architecture vs Mechanical", "61", "no");
        }

        // -----------------------------------------------------------------------------------
        // External Data — the first tool to become a wizard
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Step two of five, so the rail shows a step behind, the step itself, and three ahead.
        ///
        /// The point of the picture is the rail and what is missing from the screen: there is no
        /// column mapping here and no write button, because neither means anything until the file
        /// and the model have been read. The window this replaced had all five on screen at once.
        /// </summary>
        private static UIElement DataLink() => Sheets.Page(
            Ui.Steps(new[]
            {
                new Ui.Step("The file", () => Ui.Stack(
                    Ui.PathField("Spreadsheet", Ui.Text(@"D:\Projects\Riverside\Asset register.xlsx",
                                                        null, 340), PathKind.Open),
                    Ui.Form(Ui.Field("Worksheet", Ui.Choice(new[] { "Assets", "Rooms" }, "Assets", null))),
                    Ui.Line("412 rows, 9 columns.", 0.85)), () => null),

                new Ui.Step("The model", () => Ui.Stack(
                    Ui.Form(Ui.Field("Scope",
                        Ui.Choice(new[] { "What is selected", "Everything loaded" },
                                  "Everything loaded", null))),
                    Ui.Actions(Ui.Primary("Read the model", () => { })),
                    Ui.Line("41,880 elements read.", 0.85))),

                new Ui.Step("Matching", () => Ui.Stack()),
                new Ui.Step("Columns", () => Ui.Stack()),
                new Ui.Step("Write", () => Ui.Stack()),
            }, null, null));

        // -----------------------------------------------------------------------------------
        // The last step — the one branch of Ui.Steps nothing has ever drawn
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The end of a wizard whose last step carries its own two-stage commit.
        ///
        /// Worth a picture of its own because it is the branch that is easiest to get wrong: on the
        /// last step Next has nothing to advance to, and where the step already holds the action -
        /// as five of the six do - there is no finish button either, so the row underneath is Back
        /// and nothing else. A wizard that ends on a dead button that says "Finish" and does
        /// nothing is worse than one that ends on no button at all.
        /// </summary>
        private static UIElement LastStep() =>
            ToolWindow.Frame(Tools.Find("matrix"), Sheets.Page(
                Ui.Steps(new[]
                {
                    // Answered, so the wizard opens past them - which is what puts this
                    // picture on the step it is about.
                    new Ui.Step("The sheet", () => Ui.Stack(), () => null),
                    new Ui.Step("The matrix", () => Ui.Stack(), () => null),
                    new Ui.Step("The tests", () => Ui.Stack(
                        Ui.Commit("Show me the tests it would make", "Create", "test",
                                  (job, into) => 0, job => { }),
                        Ui.Line("38 tests, 6 already there and left alone.", 0.85))),
                }, null, null)));

        // -----------------------------------------------------------------------------------
        // IDS authoring — the densest window in the product
        // -----------------------------------------------------------------------------------

        private static UIElement Ids() => Sheets.Page(
            Ui.Eyebrow("Specifications"),
            Ui.Stack(
                Ui.Button("Every wall carries a fire rating", () => { }),   // harness: data
                Ui.Button("Every duct carries a system name", () => { }),  // harness: data
                Ui.Actions(Ui.Quiet("Add a specification", () => { }))),

            Ui.Rule(),

            Ui.Actions(
                Ui.Primary("Save as an .ids file...", () => { }),
                Ui.Quiet("Keep the draft here", () => { })),

            // Folded, and under the action, because what the file is called is set once and read
            // never. It used to be the first thing on the screen.
            Ui.Section("This document", "what the file says it is", false,
                Ui.Form(
                    Ui.Field("Called", Ui.Text("Riverside employer's information requirements", null, 340)),
                    Ui.Pair(Ui.Field("Version", Ui.Text("1.2", null, 90)),
                            Ui.Field("Author", Ui.Text("coordination@bimcamel.com", null, 220))),
                    Ui.Field("About", Ui.Text("What every model must carry before it is accepted.", null, 340)))),

            Ui.Rule(),

            Ui.Eyebrow("The specification"),
            Ui.Form(
                Ui.Field("Called", Ui.Text("Every wall carries a fire rating", null, 340)),
                Ui.Pair(Ui.Field("Reference", Ui.Text("EIR-04", null, 120)),
                        Ui.Field("IFC versions", Ui.Text("IFC4", null, 120))),
                Ui.Field("Instruction", Ui.Text("Ask the architect before the next issue.", null, 340)),
                Ui.Pair(Ui.Field("At least this many match", Ui.Text("1", null, 90)),
                        Ui.Field("At most", Ui.Text("", "no limit", null, 90)))),

            Ui.Eyebrow("It applies to"),
            Ui.Section("Entity", () => "IFCWALL", false,
                Ui.Field("This row is a", Ui.Choice(new[] { "Entity", "Property", "Classification" }, "Entity", null)),
                Ui.Form(Ui.Field("Entity", Ui.Text("IFCWALL", null, 200)))),

            Ui.Eyebrow("Those elements must have"),
            Ui.Section("Property", () => "Pset_WallCommon / FireRating is set", false,
                Ui.Field("This row is a", Ui.Choice(new[] { "Entity", "Property", "Classification" }, "Property", null)),
                Ui.Form(
                    Ui.Pair(Ui.Field("Property set", Ui.Text("Pset_WallCommon", null, 200)),
                            Ui.Field("Property", Ui.Text("FireRating", null, 200))),
                    Ui.Field("It is", Ui.Choice(new[] { "required", "optional", "prohibited" }, "required", null)),
                    Ui.Actions(Ui.Icon(Glyphs.Bin, "Remove this row", () => { })))),

            Ui.Actions(Ui.Quiet("Add a row", () => { })),

            Ui.Rule(),
            Ui.Eyebrow("What it comes to"),
            Ui.Line("2 specifications, 3 rows. 41,880 elements would be checked.", 0.85));
    }
}
