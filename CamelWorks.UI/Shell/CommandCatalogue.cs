using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One ribbon command.</summary>
    public sealed class RibbonCommand
    {
        internal RibbonCommand(string id, string title, string? toolId,
                               string tooltip, string[] synonyms, string[] symptoms)
        {
            Id = id; Title = title; ToolId = toolId;
            Tooltip = tooltip; Synonyms = synonyms; Symptoms = symptoms;
        }

        /// <summary>The host command id, matching the ribbon XAML.</summary>
        public string Id { get; }

        /// <summary>How it reads on the button.</summary>
        public string Title { get; }

        /// <summary>
        /// Which tool it opens.
        ///
        /// One button, one tool. It used to be a "workspace/tab" route, and twenty-one of the
        /// twenty-seven buttons routed into the same pane - three of them onto a tab another
        /// button already opened, so two different buttons did the identical thing.
        /// </summary>
        public string? ToolId { get; }

        /// <summary>The tool this opens, or null when there is no such tool.</summary>
        public Tool? Tool => Tools.Find(ToolId);

        /// <summary>The tooltip.</summary>
        public string Tooltip { get; }

        /// <summary>Other words for the same thing, so search finds it under the name the user knows.</summary>
        public IReadOnlyList<string> Synonyms { get; }

        /// <summary>
        /// The problem this solves, in the words somebody would use while having it.
        ///
        /// The reason Find a Tool is worth building. Nobody looking for the appearance manager
        /// types "appearance manager" — they type "why is this thing hidden", and a search that
        /// only matches command names answers nothing.
        /// </summary>
        public IReadOnlyList<string> Symptoms { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Every command in the product, as data.
    ///
    /// One list, because the ribbon handler, the Find a Tool search and the keyboard shortcut map
    /// all need the same set and would otherwise each carry their own copy. A command missing from
    /// one of three hand-maintained lists is invisible in exactly one place, which is the hardest
    /// kind of gap to notice.
    /// </summary>
    public static class CommandCatalogue
    {
        /// <summary>All thirty-nine, in ribbon order.</summary>
        public static IReadOnlyList<RibbonCommand> All { get; } = new[]
        {
            // Panel - Project
            Cmd("ID_CW_HealthCheck", "Health Check", "health",
                "Whether this federation is fit to coordinate against, before anybody "
                + "spends a week finding out it was not.",
                new[] { "audit", "model check", "quality", "scorecard", "validate" },
                new[] { "is this model any good", "something is wrong with the federation",
                        "elements in the wrong place", "model loaded twice", "duplicate elements" }),

            Cmd("ID_CW_ProjectSettings", "Project Settings", "project",
                "What CamelWorks worked out about this job, where each answer came from, "
                + "and an override on every line. Nothing here is required.",
                new[] { "setup", "profile", "configure", "options", "preferences", "template" },
                new[] { "where do I set this up", "do I have to configure it first",
                        "set this up the same way on the next job", "what did it assume" }),

            Cmd("ID_CW_FixLinks", "Fix Broken Links", "links",
                "The source files a loaded model can no longer see, and where they went.",
                new[] { "missing model", "relink", "repath", "broken reference" },
                new[] { "model did not load", "file not found",
                        "the NWF points at the wrong folder" }),

            Cmd("ID_CW_Help", "Help", "help",
                "The guide, what this cannot do, the shortcuts, a sample project and the "
                + "diagnostics.",
                new[] { "documentation", "support", "diagnostics", "about", "version",
                        "find a tool" },
                new[] { "how do I do this", "something is broken", "what version am I on",
                        "where is that tool" }),

            // Panel - Clash
            Cmd("ID_CW_Board", "Clash Board", "triage",
                "Every result the tests found, worked down to the issues somebody fixes - "
                + "and your decisions survive the next run.",
                new[] { "clashes", "board", "issues", "results", "grouping", "triage" },
                new[] { "too many clashes", "thousands of results", "the same clash forty times",
                        "one pipe through forty joists", "where do I even start",
                        "did I lose last week's work" }),

            Cmd("ID_CW_Review", "Review", "review",
                "One issue at a time in front of the geometry, both hands on the keyboard.",
                new[] { "walk the clashes", "go through results", "sign off" },
                new[] { "I need to go through these one at a time" }),

            Cmd("ID_CW_ClashTests", "Clash Tests", "tests",
                "What gets tested against what, and how close is too close. The matrix "
                + "builds a whole set at once, including from a spreadsheet somebody sent "
                + "you.",
                new[] { "matrix", "run clashes", "test setup", "batch clash", "clash matrix" },
                new[] { "setting up clash tests takes all morning",
                        "which tests have I already run", "they sent me their clash matrix" }),

            Cmd("ID_CW_ClashRules", "Clash Rules", "rules",
                "Suppress what never matters, group what is one problem, assign what is "
                + "left - previewed before it touches the board.",
                new[] { "filters", "ignore", "suppress", "auto-assign", "grouping rules" },
                new[] { "the same false positives every week", "insulation touching a wall",
                        "I keep ignoring the same things" }),

            Cmd("ID_CW_Clearance", "Clearance", "headroom",
                "The check no clash test can do: whether the clear height is still there "
                + "where nothing is touching.",
                new[] { "headroom", "ceiling height", "soffit", "head height", "clear height" },
                new[] { "not enough headroom", "the ceiling is too low under that duct",
                        "will a person fit under that" }),

            // Panel - Issues
            Cmd("ID_CW_ClashReport", "Clash Report", "clashreport",
                "The board as a document somebody without Navisworks can read - and the "
                + "penetration schedule the structural engineer wants instead.",
                new[] { "export report", "issue list", "pdf", "spreadsheet", "penetrations",
                        "sleeves" },
                new[] { "the client wants a report", "I need this in a spreadsheet",
                        "structures want a list of holes" }),

            Cmd("ID_CW_Bcf", "BCF", "bcf",
                "Issues out to any tool that speaks BCF, and their answers back in - 2.1 or "
                + "3.0.",
                new[] { "bcf", "openbim", "issue exchange", "solibri", "bimcollab", "revizto" },
                new[] { "send these to the architect", "they use a different tool",
                        "they sent a bcfzip back" }),

            Cmd("ID_CW_Merge", "Merge Markups", "merge",
                "Several reviewers answered the same board. Where they disagree is shown, "
                + "not silently won by whoever came last.",
                new[] { "combine", "reconcile", "incoming", "conflicts" },
                new[] { "two people reviewed the same clashes", "whose version wins" }),

            Cmd("ID_CW_ClashXml", "Clash XML", "clashxml",
                "The interchange format other coordination tools speak, in or out, against "
                + "a saved profile.",
                new[] { "xml", "interchange", "import results", "iconstruct" },
                new[] { "they sent an xml of their tests", "get these results into their tool" }),

            // Panel - Revisions
            Cmd("ID_CW_Compare", "Compare Revisions", "compare",
                "What changed against a baseline: added, removed, moved, retyped - and how "
                + "it knows.",
                new[] { "diff", "difference", "revision", "version", "what changed",
                        "model compare" },
                new[] { "what changed since last week", "which model did they update",
                        "the architect moved something and did not say", "did anything move" }),

            Cmd("ID_CW_Changes", "Changes", "changes",
                "The change list painted onto the model, so a revision is walked rather "
                + "than read.",
                new[] { "change list", "walk changes", "review revision" },
                new[] { "show me what moved", "I cannot tell what is different" }),

            Cmd("ID_CW_Baseline", "Baseline", "baseline",
                "Record what the model is right now, so next week has something to compare "
                + "against - or freeze a scope that has been agreed.",
                new[] { "snapshot", "record", "mark", "freeze", "capture", "agreed scope" },
                new[] { "save what it looks like now", "I want to compare against this later",
                        "we agreed this scope today" }),

            Cmd("ID_CW_ChangeNotice", "Change Notice", "notice",
                "What has changed since a scope was frozen, written the way a variation has "
                + "to be written.",
                new[] { "variation", "change order", "claim", "scope change" },
                new[] { "they changed it after we agreed", "I need to raise a variation" }),

            // Panel - Data
            Cmd("ID_CW_DataManager", "Data Manager", "data",
                "Every property the elements carry, in one grid - and the pen to fix what "
                + "is missing, by hand, formula, code table or spreadsheet.",
                new[] { "properties", "parameters", "attributes", "edit data", "excel",
                        "round trip" },
                new[] { "I need to see every property at once", "bulk edit properties",
                        "get this into Excel", "the QS sent a spreadsheet back" }),

            Cmd("ID_CW_Zones", "Levels and Zones", "levels",
                "What level and what zone every element is on, worked out from whatever the "
                + "model already carries - and it says which source it used.",
                new[] { "storeys", "floors", "levels", "zoning", "grids" },
                new[] { "the model has no levels", "clashes are not grouped by floor",
                        "which floor is this on" }),

            Cmd("ID_CW_Takeoff", "Takeoff", "takeoff",
                "The quantities the model carries, summed how a bill wants them, with "
                + "whatever could not be read reported rather than dropped.",
                new[] { "quantities", "measure", "counts", "schedule", "qto", "bill" },
                new[] { "how much of this is there", "I need quantities out of the model" }),

            Cmd("ID_CW_Wbs", "Work Breakdown", "wbs",
                "The selection tree's own shape written onto the elements as properties, so "
                + "a schedule can group by a breakdown the model already implies.",
                new[] { "wbs", "work breakdown", "structure", "hierarchy", "packages" },
                new[] { "I need a WBS on these", "group the schedule by package",
                        "the tree knows this but the properties do not" }),

            Cmd("ID_CW_Ids", "IDS", "ids",
                "What every model must carry, written as the open standard - and the "
                + "federation checked against it.",
                new[] { "ids", "information delivery", "requirements", "eir",
                        "employer information" },
                new[] { "do their models meet our requirements",
                        "check the models against the EIR" }),

            // Panel - Sets & Views
            Cmd("ID_CW_Sets", "Set Builder", "sets",
                "The selections everything else works on, said in plain rows - and "
                + "generated in bulk from a property list or a spreadsheet.",
                new[] { "search sets", "selection sets", "filters", "find items", "library" },
                new[] { "the search only does AND", "I need everything except this",
                        "building the same set over and over", "I need a set per room" }),

            Cmd("ID_CW_Appearance", "Appearance", "appearance",
                "What is coloured, what is hidden, and which layer did it - stacked, and "
                + "every layer takes back off.",
                new[] { "colours", "override", "hide", "isolate", "transparency", "layers" },
                new[] { "why is this thing hidden", "why is this pink", "unhide all lost my work",
                        "I cannot tell what is overridden" }),

            Cmd("ID_CW_Viewpoints", "Viewpoints", "viewpoints",
                "Rename, renumber, re-folder and re-camera viewpoints in bulk instead of "
                + "one at a time down the tree.",
                new[] { "views", "saved viewpoints", "renumber", "rename" },
                new[] { "two hundred viewpoints named Viewpoint", "renaming these one at a time" }),

            Cmd("ID_CW_SectionBox", "Section Box", "sectionbox",
                "Boxes whatever is selected. With nothing selected it switches sectioning "
                + "off and back on without losing the box.",
                new[] { "section", "clip", "cut", "crop" },
                new[] { "I cannot see inside", "turning sectioning off loses my box" }),

            // Panel - Deliver
            Cmd("ID_CW_ExportGlb", "Export GLB", "exportglb",
                "The geometry as one file a browser opens, with nothing installed and no "
                + "licence at the other end.",
                new[] { "gltf", "glb", "web viewer", "3d", "share the model", "geometry" },
                new[] { "put this on the web", "send them the geometry",
                        "they cannot open an nwd" }),

            Cmd("ID_CW_ExportData", "Export Data", "exportsheet",
                "The properties you name, as a spreadsheet or a CSV, one row per element.",
                new[] { "csv", "xlsx", "spreadsheet", "excel", "properties", "schedule" },
                new[] { "they want it in Excel", "send them the properties",
                        "I need this as a schedule" }),

            Cmd("ID_CW_ExportDrawing", "Export Drawing", "exportdxf",
                "The geometry as a DXF every CAD package opens, one layer per discipline.",
                new[] { "dxf", "dwg", "autocad", "drawing", "cad", "2d" },
                new[] { "they want a DWG", "send it to the CAD team",
                        "they do not have Navisworks" }),

            Cmd("ID_CW_ExportIfc", "Export IFC", "ifc",
                "The model as IFC, with the mapping and the classification it should carry.",
                new[] { "ifc", "openbim export", "ifc4", "ifc2x3" },
                new[] { "they want IFC", "export to IFC" }),

            Cmd("ID_CW_Extract", "Extract", "extract",
                "A level, a zone, a room or a view cut out of the federation as something "
                + "somebody else can open.",
                new[] { "cut out", "subset", "split", "issue a zone" },
                new[] { "send them just level 3", "they only need the plant room" }),

            Cmd("ID_CW_Compile", "Compile", "compile",
                "A folder of supplier drops appended into one deliverable, newest revision "
                + "of each, with a log of what went in.",
                new[] { "append", "federate", "combine models", "deliverable", "issue" },
                new[] { "build this week's federated model", "which revision went into that NWD" }),

            // Panel - Site
            Cmd("ID_CW_SetOut", "Setting Out", "setout",
                "The file an engineer loads into a total station and walks the floor with, "
                + "in site coordinates.",
                new[] { "survey", "total station", "coordinates", "points", "site" },
                new[] { "the site engineer needs these points",
                        "give me setting out for the hangers" }),

            Cmd("ID_CW_Dimensions", "Dimensions", "dimensions",
                "How long that run is, how far it sits off the slab, whether the hangers "
                + "are at centres - for every element at once.",
                new[] { "measure", "lengths", "spacing", "centres", "offsets" },
                new[] { "are these at 600 centres", "how far off the slab is that" }),

            Cmd("ID_CW_Weight", "Centre of Gravity", "weight",
                "Where the weight of a module actually sits, so the hook goes in the right "
                + "place.",
                new[] { "weight", "mass", "lifting", "cog", "crane" },
                new[] { "where do we lift this from", "how heavy is that module" }),

            Cmd("ID_CW_Labels", "Site Labels", "labels",
                "A sheet of stickers with a code on each and the identifier underneath, for "
                + "somebody to put on the work.",
                new[] { "qr", "barcode", "stickers", "tags", "labels" },
                new[] { "we need to tag these on site", "print labels for the spools" }),

            Cmd("ID_CW_Paperwork", "Documents and Photos", "paperwork",
                "The data sheets, site photos and document-system addresses the model "
                + "already points at - found, checked, opened.",
                new[] { "attachments", "links", "documents", "photos", "datasheets" },
                new[] { "where is the datasheet for that", "are these document links still alive" }),

            // Panel - Automate
            Cmd("ID_CW_Batch", "Batch", "batch",
                "A saved list of jobs run in order over the document that is open, in one "
                + "press.",
                new[] { "automation", "scheduled", "overnight", "unattended", "jobs" },
                new[] { "doing the same export every week", "this takes an hour every Friday" }),

            Cmd("ID_CW_Graph", "Graph Editor", "graph",
                "The same jobs as a graph, for when a list of steps is not enough.",
                new[] { "nodes", "visual scripting", "dyncamelo", "workflow" },
                new[] { "I want to chain these together" }),
        };

        /// <summary>Look one up by its host command id.</summary>
        public static RibbonCommand? Find(string? commandId) =>
            All.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Find a Tool: match a query against names, synonyms and symptom phrasing.
        ///
        /// Symptoms carry the most weight after an exact name match, because they are what people
        /// actually type. A search that only matched command names would answer "why is this thing
        /// hidden" with nothing at all, which is precisely when somebody needs it.
        /// </summary>
        public static IReadOnlyList<RibbonCommand> Search(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return All;

            var needle = query!.Trim();

            return All
                .Select(c => new { Command = c, Score = Score(c, needle) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Command.Title, StringComparer.Ordinal)
                .Select(x => x.Command)
                .ToList();
        }

        private static int Score(RibbonCommand command, string needle)
        {
            if (Has(command.Title, needle)) return command.Title.Length == needle.Length ? 100 : 80;
            if (command.Symptoms.Any(s => Has(s, needle))) return 60;
            if (command.Synonyms.Any(s => Has(s, needle))) return 50;
            if (Has(command.Tooltip, needle)) return 20;

            // Every word matching somewhere is still a match — "hidden elements" should find the
            // appearance manager even though neither word alone is one of its synonyms.
            var words = needle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1 && words.All(w => Score(command, w) > 0)) return 10;

            return 0;
        }

        private static bool Has(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static RibbonCommand Cmd(string id, string title, string toolId, string tooltip,
                                         string[] synonyms, string[] symptoms) =>
            new RibbonCommand(id, title, toolId, tooltip, synonyms, symptoms);
    }
}
