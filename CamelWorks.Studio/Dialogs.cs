using System;
using System.Collections.Generic;
using System.Windows;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The benches that are not desks, and the wizard spine, reconstructed.
    ///
    /// The wizards are a spine of steps down the left and the two benches are their own shape —
    /// neither is something the desks can show, so each gets a picture of its own. Same rule as
    /// <see cref="Panels"/>: same captions, same weights, plausible data where the real one reads
    /// the host.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>The windows, in the order the product offers them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Sheet("dlg-ids", "Bench · IDS authoring", 1000, 1180, Ids),
            Sheet("dlg-wizard", "Wizard · The spine", 640, 1000, Wizard),
            Sheet("dlg-laststep", "Wizard · The last step", 640, 1000, LastStep),
        };

        private static Sheet Sheet(string id, string title, int height, int width, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { width } };

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
                new Ui.Step("The mapping", () => Ui.Stack(), () => null),
                new Ui.Step("The write", () => Ui.Stack(
                    Ui.Commit("See what would change", "Write", "element",
                              (job, into) => 0, job => { }),
                    Ui.Line("38 elements, 6 already right and left alone.", 0.85))),
            }, null, null);

        // -----------------------------------------------------------------------------------
        // IDS authoring — the densest editor, unfolded
        // -----------------------------------------------------------------------------------

        private static UIElement Ids() => Sheets.Page(
            Ui.Eyebrow("Specifications"),
            Ui.Stack(
                Ui.Button("Every wall carries a fire rating", () => { }),   // harness: data
                Ui.Button("Every duct carries a system name", () => { }),  // harness: data
                Ui.Actions(Ui.Icon(Glyphs.Add, "Add a specification", () => { }))),

            Ui.Rule(),

            Ui.Actions(
                Ui.Primary("Save IDS...", () => { }),
                Ui.Icon(Glyphs.Save, "Keep the draft here", () => { }),
                Ui.Icon(Glyphs.Tune, "What this document is called", () => { })),

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
