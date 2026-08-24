using System;
using System.Collections.Generic;
using System.Windows;
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
            Sheet("dlg-clashxml", "Window · Clash XML", 900, 760, ClashXml),
            Sheet("dlg-ids", "Window · IDS authoring", 1000, 1120, Ids),
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

        private static UIElement ClashXml() => Sheets.Page(
            Ui.Marked(Glyphs.Clash, "Clash XML",
                "Read a board out of the format every other coordination tool speaks, or write "
                + "one back into it.",
                Ui.Field("The file", Ui.Text(@"D:\Projects\Riverside\Clash\week-31.xml", null, 380)),
                Ui.Actions(
                    Ui.Primary("Save to a file...", () => { }),
                    Ui.Button("Load from a file...", () => { }))),

            Ui.Marked(Glyphs.Filter, "What goes in it",
                "Six tests on this board. Untick the ones the receiving tool has no use for.",
                Ui.Table(Tests(), ("Test", 0), ("Clashes", 90), ("In", 60)),
                Ui.Actions(
                    Ui.Quiet("Tick every test", () => { }),
                    Ui.Quiet("Untick every test", () => { }))),

            Ui.Marked(Glyphs.Save, "Profiles",
                "A profile is which tests, which columns and which units - kept so the next issue "
                + "goes out the same way.",
                Ui.Field("Called", Ui.Text("Weekly issue to the contractor", null, 300)),
                Ui.Actions(
                    Ui.Quiet("Keep", () => { }),
                    Ui.Danger("Forget", () => { }))));

        private static IEnumerable<TableRow> Tests()
        {
            yield return new TableRow(null, "Structure vs Mechanical", "412", "yes");
            yield return new TableRow(null, "Structure vs Electrical", "88", "yes");
            yield return new TableRow(null, "Mechanical vs Electrical", "203", "yes");
            yield return new TableRow(null, "Architecture vs Mechanical", "61", "no");
        }

        // -----------------------------------------------------------------------------------
        // IDS authoring — the densest window in the product
        // -----------------------------------------------------------------------------------

        private static UIElement Ids() => Sheets.Page(
            Ui.Eyebrow("This document"),
            Ui.Form(
                Ui.Field("Called", Ui.Text("Riverside employer's information requirements", null, 340)),
                Ui.Pair(Ui.Field("Version", Ui.Text("1.2", null, 90)),
                        Ui.Field("Author", Ui.Text("coordination@bimcamel.com", null, 220))),
                Ui.Field("About", Ui.Text("What every model must carry before it is accepted.", null, 340))),

            Ui.Eyebrow("Specifications"),
            Ui.Stack(
                Ui.Button("Every wall carries a fire rating", () => { }),
                Ui.Button("Every duct carries a system name", () => { }),
                Ui.Actions(Ui.Quiet("Add a specification", () => { }))),

            Ui.Eyebrow("Write it"),
            Ui.Actions(
                Ui.Primary("Save as an .ids file...", () => { }),
                Ui.Quiet("Keep the draft here", () => { })),

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
