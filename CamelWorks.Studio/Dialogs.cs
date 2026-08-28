using System;
using System.Collections.Generic;
using System.Windows;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The two windows that are not desks, and the wizard spine, reconstructed.
    ///
    /// Review is a floating palette over the 3D view and the wizards are a spine of steps down
    /// the left — both shapes the desks cannot show, so each gets a picture of its own. Same rule
    /// as <see cref="Panels"/>: same captions, same weights, plausible data where the real one
    /// reads the host.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>The windows, in the order the product offers them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Sheet("dlg-review", "Window · Review a finding", 720, 620, Review),
            Sheet("dlg-clashxml", "Bench · Clash XML", 760, 1180, ClashXml),
            Sheet("dlg-ids", "Bench · IDS authoring", 1000, 1180, Ids),
            Sheet("dlg-wizard", "Wizard · The spine", 640, 1000, Wizard),
            Sheet("dlg-laststep", "Wizard · The last step", 640, 1000, LastStep),
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
        // Clash XML — the two-leg editor on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement ClashXml()
        {
            var setup = Ui.Stack(
                Bench.Group(string.Empty,
                    Ui.Segmented(new[] { "Write one", "Read one back" }, "Write one", null)),
                Bench.Group("This profile",
                    Ui.Field("Called", Ui.Text("Weekly issue to the contractor", null, 200)),
                    Ui.Across(
                        Ui.Quiet("Keep", () => { }),
                        Ui.Danger("Forget", () => { })),
                    Ui.Across(
                        Ui.Quiet("Save...", () => { }),
                        Ui.Quiet("Load...", () => { }))),
                Bench.Group("How it is written",
                    Ui.Check("Keep the folders as folders", true, null),
                    Ui.Check("Write each result's two elements", true, null)),
                Bench.Group("The file",
                    Ui.PathField("File", Ui.Text(@"D:\Projects\Riverside\Clash\week-31.xml",
                                                 null, 200), PathKind.Save)));

            var stage = Ui.Stack(
                Ui.Eyebrow("The tests"),
                Ui.Line("Six tests on this board, 764 results between them.", 1, true),
                Ui.Table(Tests(), ("Test", 0), ("Clashes", 90), ("In", 60)),
                Ui.Across(
                    Ui.Quiet("Tick every test", () => { }),
                    Ui.Quiet("Untick every test", () => { })),
                Ui.Commit("Show me what would go in", "Write", "result",
                          (job, into) => 0, job => { }));

            return Bench.Surface(setup, null, stage);
        }

        private static IEnumerable<TableRow> Tests()
        {
            yield return new TableRow(null, "Structure vs Mechanical", "412", "yes");
            yield return new TableRow(null, "Structure vs Electrical", "88", "yes");
            yield return new TableRow(null, "Mechanical vs Electrical", "203", "yes");
            yield return new TableRow(null, "Architecture vs Mechanical", "61", "no");
        }

        // -----------------------------------------------------------------------------------
        // The wizard spine — a step behind, the step itself, three ahead
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Step two of five, so the spine shows the whole job with one step answered.
        ///
        /// The point of the picture is what is missing from the stage: there is no column mapping
        /// here and no write button, because neither means anything until the file and the model
        /// have been read.
        /// </summary>
        private static UIElement Wizard() =>
            Ui.Steps(new[]
            {
                new Ui.Step("The file", () => Ui.Stack(
                    Ui.PathField("Spreadsheet", Ui.Text(@"D:\Projects\Riverside\Asset register.xlsx",
                                                        null, 340), PathKind.Open),
                    Ui.Field("Worksheet", Ui.Choice(new[] { "Assets", "Rooms" }, "Assets", null)),
                    Ui.Line("412 rows, 9 columns.", 0.85)), () => null),

                new Ui.Step("The model", () => Ui.Stack(
                    Ui.Field("Scope",
                        Ui.Choice(new[] { "What is selected", "Everything loaded" },
                                  "Everything loaded", null)),
                    Ui.Actions(Ui.Primary("Read the model", () => { })),
                    Ui.Line("41,880 elements read.", 0.85))),

                new Ui.Step("Matching", () => Ui.Stack()),
                new Ui.Step("Columns", () => Ui.Stack()),
                new Ui.Step("Write", () => Ui.Stack()),
            }, null, null);

        // -----------------------------------------------------------------------------------
        // The last step — the branch where the step carries its own commit
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The end of a wizard whose last step carries its own two-stage commit.
        ///
        /// Worth a picture of its own because it is the branch that is easiest to get wrong: on
        /// the last step Next has nothing to advance to, and where the step already holds the
        /// action there is no finish button either — the spine's foot is Back and nothing else.
        /// A wizard that ends on a dead button saying "Finish" is worse than one that ends on no
        /// button at all.
        /// </summary>
        private static UIElement LastStep() =>
            Ui.Steps(new[]
            {
                // Answered, so the wizard opens past them - which is what puts this
                // picture on the step it is about.
                new Ui.Step("The sheet", () => Ui.Stack(), () => null),
                new Ui.Step("The matrix", () => Ui.Stack(), () => null),
                new Ui.Step("The tests", () => Ui.Stack(
                    Ui.Commit("Preview", "Create", "test",
                              (job, into) => 0, job => { }),
                    Ui.Line("38 tests, 6 already there and left alone.", 0.85))),
            }, null, null);

        // -----------------------------------------------------------------------------------
        // IDS authoring — the densest editor, unfolded
        // -----------------------------------------------------------------------------------

        private static UIElement Ids() => Sheets.Page(
            Ui.Eyebrow("Specifications"),
            Ui.Stack(
                Ui.Button("Every wall carries a fire rating", () => { }),   // harness: data
                Ui.Button("Every duct carries a system name", () => { }),  // harness: data
                Ui.Actions(Ui.Quiet("Add a specification", () => { }))),

            Ui.Rule(),

            Ui.Actions(
                Ui.Primary("Save IDS...", () => { }),
                Ui.Quiet("Keep the draft here", () => { })),

            Ui.Eyebrow("This document"),
            Ui.Field("Called", Ui.Text("Riverside employer's information requirements", null, 340)),
            Ui.Pair(Ui.Field("Version", Ui.Text("1.2", null, 90)),
                    Ui.Field("Author", Ui.Text("coordination@bimcamel.com", null, 220))),
            Ui.Field("About", Ui.Text("What every model must carry before it is accepted.", null, 340)),

            Ui.Rule(),

            Ui.Eyebrow("The specification"),
            Ui.Field("Called", Ui.Text("Every wall carries a fire rating", null, 340)),
            Ui.Pair(Ui.Field("Reference", Ui.Text("EIR-04", null, 120)),
                    Ui.Field("IFC versions", Ui.Text("IFC4", null, 120))),
            Ui.Field("Instruction", Ui.Text("Ask the architect before the next issue.", null, 340)),
            Ui.Pair(Ui.Field("At least this many match", Ui.Text("1", null, 90)),
                    Ui.Field("At most", Ui.Text("", "no limit", null, 90))),

            Ui.Eyebrow("It applies to"),
            Ui.Field("This row is a", Ui.Choice(new[] { "Entity", "Property", "Classification" }, "Entity", null)),
            Ui.Field("Entity", Ui.Text("IFCWALL", null, 200)),
            Ui.Rule(),

            Ui.Eyebrow("Those elements must have"),
            Ui.Field("This row is a", Ui.Choice(new[] { "Entity", "Property", "Classification" }, "Property", null)),
            Ui.Pair(Ui.Field("Property set", Ui.Text("Pset_WallCommon", null, 200)),
                    Ui.Field("Property", Ui.Text("FireRating", null, 200))),
            Ui.Field("It is", Ui.Choice(new[] { "required", "optional", "prohibited" }, "required", null)),
            Ui.Actions(Ui.Icon(Glyphs.Bin, "Remove this row", () => { })),

            Ui.Actions(Ui.Quiet("Add a row", () => { })),

            Ui.Rule(),
            Ui.Eyebrow("What it comes to"),
            Ui.Line("2 specifications, 3 rows. 41,880 elements would be checked.", 0.85));
    }
}
