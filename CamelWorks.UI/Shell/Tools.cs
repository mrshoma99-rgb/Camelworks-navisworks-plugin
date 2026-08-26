using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>What shape a tool takes when it opens.</summary>
    public enum ToolShape
    {
        /// <summary>
        /// A dockable panel, because the work is long and happens beside the model.
        ///
        /// Reserved for the few tools somebody sits in for an hour with the 3D view next to them:
        /// working a clash board down, editing properties, building sets. A panel is the right
        /// answer only when the model has to stay visible the whole time.
        /// </summary>
        Panel = 0,

        /// <summary>
        /// Its own window: a task with a beginning and an end.
        ///
        /// Most tools are this. Somebody opens it to do one thing, does it, and closes it. It gets
        /// the width the work needs rather than the width a dock pane happens to be.
        /// </summary>
        Window,

        /// <summary>
        /// Its own window, taken in order, because each decision depends on the last.
        ///
        /// Only where the dependency is real - you cannot map a spreadsheet's columns before you
        /// have read the spreadsheet. Where the decisions are independent, a wizard is a form with
        /// extra clicks.
        /// </summary>
        Wizard,

        /// <summary>No surface at all. It happens and it is done.</summary>
        Act,

        /// <summary>A menu of choices under the button.</summary>
        Menu,
    }

    /// <summary>
    /// One tool: what it is for, the single thing it does, and how it shows itself.
    ///
    /// <b>Every tool has exactly one main action.</b> The field is not optional and not a
    /// description - it is the caption on the one filled button that tool exists to offer. If a
    /// tool cannot name it in three or four words, that is the tool telling you it is two tools.
    /// </summary>
    public sealed class Tool
    {
        internal Tool(string id, string title, string purpose, string? mainAction,
                      ToolShape shape, string glyph)
        {
            Id = id;
            Title = title;
            Purpose = purpose;
            MainAction = mainAction;
            Shape = shape;
            Glyph = glyph;
        }

        /// <summary>What the code calls it.</summary>
        public string Id { get; }

        /// <summary>What the button and the title bar call it.</summary>
        public string Title { get; }

        /// <summary>What it is trying to do, in one sentence, shown under the title.</summary>
        public string Purpose { get; }

        /// <summary>
        /// The caption on its one filled button, or null for a tool with no surface.
        ///
        /// Everything else the tool offers is arranged round this and is quieter than it.
        /// </summary>
        public string? MainAction { get; }

        /// <summary>How it opens.</summary>
        public ToolShape Shape { get; }

        /// <summary>Its mark on the ribbon and in its own title bar.</summary>
        public string Glyph { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Title + (MainAction == null ? string.Empty : " — " + MainAction);
    }

    /// <summary>
    /// Every tool CamelWorks has, and what each one is for.
    ///
    /// <b>This replaces a workspace switcher and a tab strip.</b> The old model was one dock pane
    /// with seven workspaces and eighteen tabs, and twenty-one of the twenty-seven ribbon buttons
    /// opened that same pane on a different tab - three of them on tabs another button already
    /// opened, so pressing either landed you in the identical place. A ribbon of buttons that all
    /// open the same panel is a menu wearing a ribbon's clothes, and the tab strip underneath it
    /// meant every tool looked like the same form with different fields.
    ///
    /// So: a tool opens its own surface, sized and shaped to what it does. Six of them are
    /// panels, because their work genuinely happens beside the model for an hour at a time.
    /// Everything else is a window or a wizard, opens, does its one job, and closes.
    ///
    /// <b>The test each entry has to pass.</b> Name the one action in three or four words. A tool
    /// that cannot is two tools - which is how a window holding weight, dimensions, setting out,
    /// paperwork and labels behind five collapsible sections came to be five entries here.
    /// </summary>
    public static class Tools
    {
        /// <summary>Every tool, in ribbon order.</summary>
        public static IReadOnlyList<Tool> All { get; } = new[]
        {
            // ---- Project ----------------------------------------------------------------------

            Of("health", "Health Check", ToolShape.Window, Glyph.Tick,
               "Say whether this federation is fit to coordinate against, before anybody spends a "
               + "week coordinating against it.",
               "Check this federation"),

            Of("project", "Project Settings", ToolShape.Window, Glyph.Tune,
               "Everything CamelWorks worked out about this job on its own, where each answer came "
               + "from, and an override on every line. Nothing here is required.",
               "Save overrides"),


            Of("history", "History", ToolShape.Window, Glyph.Clock,
               "What has happened on this project and who did it, newest first, so a question "
               + "about a decision has an answer that is not somebody's memory.",
               "Copy the log"),

            Of("links", "Fix Broken Links", ToolShape.Window, Glyph.Folder,
               "Find the source files a loaded model can no longer see, and point it at where they "
               + "went.",
               "Bring in"),

            // ---- Coordinate -------------------------------------------------------------------

            Of("triage", "Clash Board", ToolShape.Panel, Glyph.Clash,
               "Turn every clash the tests found into the handful of issues somebody actually "
               + "fixes, and give each one to whoever fixes it.",
               "Load the board"),

            Of("review", "Review", ToolShape.Window, Glyph.Camera,
               "Walk the findings one at a time over the model, deciding each in front of the "
               + "geometry rather than off a list.",
               "Record"),

            Of("tests", "Clash Tests", ToolShape.Window, Glyph.Play,
               "Say what gets tested against what, and how close is too close.",
               "Run them"),

            Of("matrix", "Clash Matrix", ToolShape.Wizard, Glyph.Table,
               "Build a whole test set at once from a grid of one discipline against another, "
               + "instead of naming forty tests by hand.",
               "Read the matrix"),

            Of("headroom", "Clearance", ToolShape.Window, Glyph.Layers,
               "Find where the clear height under the services is not there - which no clash test "
               + "can find, because nothing is touching.",
               "Check clear height"),

            Of("rules", "Clash Rules", ToolShape.Window, Glyph.Filter,
               "Suppress what does not matter, group what is one issue, and assign what is left - "
               + "one pipeline, previewed before any of it is applied.",
               "Try these on the board"),

            Of("clashreport", "Clash Report", ToolShape.Window, Glyph.Report,
               "Get the board out as a document somebody who does not have Navisworks can read.",
               "Write the report"),

            Of("bcf", "BCF", ToolShape.Window, Glyph.Onward,
               "Exchange issues with every other tool that speaks BCF, in 2.1 or 3.0.",
               "Send the board"),

            Of("merge", "Merge Markups", ToolShape.Window, Glyph.Compare,
               "Fold several reviewers' responses back into one board, showing where two of them "
               + "disagree rather than letting the last one win.",
               "Bring them together"),

            Of("clashxml", "Clash XML", ToolShape.Window, Glyph.Clash,
               "Read a board out of the format other coordination tools speak, or write one back "
               + "into it, against a saved profile.",
               "Show me what would go in"),

            // ---- Compare ----------------------------------------------------------------------


            Of("compare", "Compare", ToolShape.Window, Glyph.Compare,
               "Say what changed between the model in front of you and a baseline - moved, "
               + "resized, added, removed, retyped.",
               "Run the comparison"),

            Of("changes", "Changes", ToolShape.Panel, Glyph.Model,
               "The change list, filtered and painted onto the model, so a revision can be walked "
               + "rather than read.",
               "Paint it onto the model"),

            Of("freeze", "Freeze", ToolShape.Window, Glyph.Save,
               "Record that a scope was agreed and what it was at the moment of agreeing, so a "
               + "later argument about what changed has a fixed point to argue from.",
               "Freeze what is on the board"),

            Of("notice", "Change Notice", ToolShape.Window, Glyph.Compare,
               "What has changed since a scope was frozen, written the way a variation has to be "
               + "written.",
               "Write the change notice"),

            Of("baseline", "Baseline", ToolShape.Act, Glyph.Save,
               "Record what the model is right now, so a comparison next week has something to "
               + "compare against.",
               null),

            // ---- Data -------------------------------------------------------------------------

            Of("data", "Data Manager", ToolShape.Panel, Glyph.Table,
               "Browse every property the elements carry, and write the ones that are missing.",
               "Read properties"),

            Of("excel", "Excel Round-trip", ToolShape.Window, Glyph.Table,
               "Take the properties out to a workbook, let somebody who does not have Navisworks "
               + "edit them, and read the edits back in.",
               "Write a sheet"),

            Of("datalink", "External Data", ToolShape.Wizard, Glyph.Folder,
               "Join a register, a schedule or an asset list onto the model by a key column, and "
               + "write it back either way.",
               "Read the file"),

            Of("levels", "Levels and Zones", ToolShape.Window, Glyph.Layers,
               "Work out what level and what zone every element is on, from whatever the model "
               + "already carries.",
               "Derive levels"),

            Of("takeoff", "Takeoff", ToolShape.Window, Glyph.Table,
               "Sum the quantities the model carries, grouped how a bill wants them, and say what "
               + "could not be read rather than dropping it.",
               "Sum it"),

            Of("report", "Report Builder", ToolShape.Wizard, Glyph.Report,
               "Build a report of model data on a template it proposes from the model itself.",
               "Read the model"),


            Of("wbs", "Work Breakdown", ToolShape.Window, Glyph.Model,
               "Write the selection tree's own shape onto the elements as properties, so a "
               + "schedule can group by a breakdown the model already implies.",
               "Show me what it would write"),

            Of("ids", "IDS", ToolShape.Window, Glyph.Tick,
               "Write down what every model must carry, as the open standard for saying so, and "
               + "check the federation against it.",
               "Save as an .ids file"),

            // ---- Sets and views ----------------------------------------------------------------

            Of("sets", "Set Builder", ToolShape.Panel, Glyph.Filter,
               "Build the selections every other tool works on, out of properties and other sets.",
               "Publish as a Navisworks set"),

            Of("schedule", "Sets from a Schedule", ToolShape.Wizard, Glyph.Table,
               "Turn a spreadsheet of rooms, assets or conditions into that many sets at once, "
               + "rather than building them one at a time.",
               "Read the schedule"),


            Of("appearance", "Appearance", ToolShape.Panel, Glyph.Layers,
               "Control what is visible and what colour it is, as layers that stack and can be "
               + "taken back off.",
               "Apply the stack"),

            Of("viewpoints", "Viewpoints", ToolShape.Window, Glyph.Camera,
               "Rename, renumber, re-folder and render viewpoints in bulk, instead of one at a "
               + "time down the tree.",
               "Read what is selected"),

            Of("restructure", "Organise Sets", ToolShape.Wizard, Glyph.Model,
               "Reshape the set library into the folders the properties imply, rather than the "
               + "order they were made in.",
               "Read the model"),

            Of("sectionbox", "Section Box", ToolShape.Act, Glyph.Empty,
               "Box whatever is selected, or switch sectioning off when nothing is.",
               null),

            // ---- Publish and site ---------------------------------------------------------------

            Of("extract", "Extract", ToolShape.Wizard, Glyph.Model,
               "Cut a piece out of the federation - a level, a zone, a room, a view - as a set, a "
               + "drawing or a model somebody else can open.",
               "Read it"),

            Of("compile", "Compile", ToolShape.Wizard, Glyph.Save,
               "Append a list of models into one deliverable, newest revision of each, with a log "
               + "of what went in.",
               "Compile"),

            Of("ifc", "Export IFC", ToolShape.Window, Glyph.Onward,
               "Write the model out as IFC, with the mapping and the classification it should "
               + "carry.",
               "Open the IFC exporter"),

            // One window used to hold all five of these behind five collapsible sections, which
            // is five tools sharing a title bar and no main action between them. They read the
            // same selection, so they still share the geometry once it is read - but each one
            // opens on its own and offers the single thing it is for.

            Of("weight", "Centre of Gravity", ToolShape.Window, Glyph.Layers,
               "Work out where the weight of a module actually sits, so the hook goes in the "
               + "right place.",
               "Work out where it sits"),

            Of("dimensions", "Dimensions", ToolShape.Window, Glyph.Ruler,
               "Answer how long that run is, how far it sits off the slab and whether those "
               + "hangers are at centres - for every element at once, on a schedule.",
               "Measure it"),

            Of("setout", "Setting Out", ToolShape.Window, Glyph.Camera,
               "Produce the file an engineer loads into a total station and walks the floor with, "
               + "in site coordinates.",
               "Write the file"),

            Of("paperwork", "Documents and Photos", ToolShape.Window, Glyph.Folder,
               "Find the data sheets, the site photos and the document-system addresses the model "
               + "already points at.",
               "Find what is attached"),

            Of("labels", "Site Labels", ToolShape.Window, Glyph.Table,
               "Print a sheet of stickers with a code on each and the identifier underneath, for "
               + "somebody to put on the work.",
               "Make the labels"),

            Of("batch", "Batch", ToolShape.Window, Glyph.Clock,
               "Run a saved sequence of steps over the document that is open, in one press.",
               "Run the selected job"),

            Of("graph", "Graph Editor", ToolShape.Panel, Glyph.Play,
               "Build that sequence as a graph, when a list of steps is not enough.",
               "Run the graph"),

            // ---- Everywhere -----------------------------------------------------------------------

            Of("export", "Export", ToolShape.Menu, Glyph.Onward,
               "Geometry as a file a browser opens, or the properties as a spreadsheet.",
               null),

            Of("help", "Help", ToolShape.Window, Glyph.Empty,
               "The guide, what this cannot do, the shortcuts, a sample project and the "
               + "diagnostics.",
               null),
        };

        /// <summary>The tools that live in a dock pane, because their work happens beside the model.</summary>
        public static IReadOnlyList<Tool> Panels =>
            All.Where(t => t.Shape == ToolShape.Panel).ToList();

        /// <summary>Find one by id.</summary>
        /// <param name="id">The tool's id.</param>
        public static Tool? Find(string? id) =>
            id == null ? null : All.FirstOrDefault(
                t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        private static Tool Of(string id, string title, ToolShape shape, string glyph,
                               string purpose, string? mainAction) =>
            new Tool(id, title, purpose, mainAction, shape, glyph);

        /// <summary>The glyph names, kept here so this file does not depend on the view layer.</summary>
        private static class Glyph
        {
            internal const string Tick = "tick";
            internal const string Tune = "tune";
            internal const string Folder = "folder";
            internal const string Clash = "clash";
            internal const string Camera = "camera";
            internal const string Play = "play";
            internal const string Table = "table";
            internal const string Layers = "layers";
            internal const string Filter = "filter";
            internal const string Report = "report";
            internal const string Onward = "onward";
            internal const string Compare = "compare";
            internal const string Model = "model";
            internal const string Save = "save";
            internal const string Clock = "clock";
            internal const string Ruler = "ruler";
            internal const string Empty = "empty";
        }
    }
}
