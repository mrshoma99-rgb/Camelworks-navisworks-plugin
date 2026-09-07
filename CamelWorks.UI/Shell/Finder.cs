using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// Find a tool: every tool in the product, matched by name, by other names for the same
    /// thing, and by the problem somebody has while needing it.
    ///
    /// The ribbon slimmed to the desks and the daily tools, so search is how the other thirty
    /// reach a first-time hand — and symptoms carry the most weight after an exact name match,
    /// because they are what people actually type. Nobody looking for the appearance manager
    /// types "appearance manager"; they type "why is this thing hidden", and a search that only
    /// matches names answers nothing.
    /// </summary>
    public static class Finder
    {
        /// <summary>
        /// Other names and symptom phrasings, by tool id.
        ///
        /// Every tool has an entry, held complete by the ribbon check — a tool missing from here
        /// is a tool search cannot find under any words but its own.
        /// </summary>
        private static readonly Dictionary<string, (string[] Synonyms, string[] Symptoms)> Words =
            new Dictionary<string, (string[], string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["health"] = (new[] { "audit", "model check", "quality", "scorecard", "validate" },
                new[] { "is this model any good", "something is wrong with the federation",
                        "elements in the wrong place", "model loaded twice", "duplicate elements" }),

            ["project"] = (new[] { "setup", "profile", "configure", "options", "preferences", "template" },
                new[] { "where do I set this up", "do I have to configure it first",
                        "set this up the same way on the next job", "what did it assume" }),

            ["models"] = (new[] { "manifest", "register", "revisions", "loaded models", "intake" },
                new[] { "which revision is this against", "what is actually loaded",
                        "did the architect republish" }),

            ["history"] = (new[] { "activity", "log", "audit trail", "who did what" },
                new[] { "who changed this", "what happened on this project" }),

            ["links"] = (new[] { "missing model", "relink", "repath", "broken reference" },
                new[] { "model did not load", "file not found",
                        "the NWF points at the wrong folder" }),

            ["issuelist"] = (new[] { "issues", "issue register", "snags", "punch list", "raise",
                                     "assign", "due date", "open issues" },
                new[] { "what is still open", "who owes me an answer", "where do I even start",
                        "track what we agreed", "the client sent issues back" }),

            ["issuein"] = (new[] { "import bcf", "read bcf", "issues in", "bcfzip" },
                new[] { "they sent a BCF file", "load their issues" }),

            ["issueout"] = (new[] { "export bcf", "send issues", "issues out" },
                new[] { "send them the issues", "write a BCF file" }),

            ["manage"] = (new[] { "clash results", "run clashes", "test setup", "batch clash",
                                  "status", "assign", "comment", "tolerance" },
                new[] { "setting up clash tests takes all morning",
                        "which tests have I already run",
                        "who owns this clash", "mark these reviewed" }),

            ["matrix"] = (new[] { "clash matrix", "discipline grid", "test matrix" },
                new[] { "they sent me their clash matrix", "forty tests by hand" }),

            ["create"] = (new[] { "new test", "create tests", "checks", "headroom",
                                  "ceiling height", "soffit", "head height", "clear height",
                                  "clearance", "fall hazard", "fall", "opening",
                                  "hole in the slab", "handrail", "guardrail", "edge protection",
                                  "void" },
                new[] { "not enough headroom", "the ceiling is too low under that duct",
                        "will a person fit under that", "is that edge railed",
                        "where are the holes in this floor" }),

            ["rules"] = (new[] { "filters", "ignore", "suppress", "auto-assign", "grouping rules" },
                new[] { "the same false positives every week", "insulation touching a wall",
                        "I keep ignoring the same things" }),

            ["clashreport"] = (new[] { "export report", "issue list", "pdf", "spreadsheet" },
                new[] { "the client wants a report", "I need this in a spreadsheet" }),

            ["exchange"] = (new[] { "bcf", "bcfzip", "openbim", "issue exchange", "solibri",
                                    "bimcollab", "revizto", "combine", "reconcile", "incoming",
                                    "conflicts", "export profile" },
                new[] { "send these to the architect", "they use a different tool",
                        "they sent a bcfzip back", "two people reviewed the same clashes",
                        "whose version wins", "a file per trade" }),

            ["penetrations"] = (new[] { "sleeves", "holes", "openings", "builderswork", "penetration schedule" },
                new[] { "structures want a list of holes", "the engineer asked for the sleeves" }),

            ["compare"] = (new[] { "diff", "difference", "revision", "version", "what changed", "model compare" },
                new[] { "what changed since last week", "which model did they update",
                        "the architect moved something and did not say", "did anything move" }),

            ["changes"] = (new[] { "change list", "walk changes", "review revision" },
                new[] { "show me what moved", "I cannot tell what is different" }),

            ["freeze"] = (new[] { "agreed scope", "sign-off", "fixed point", "lock the board" },
                new[] { "we agreed this scope today", "record what was agreed" }),

            ["sidebyside"] = (new[] { "compare two models", "two sets", "supplier model", "difference",
                                      "what is in one and not the other", "side by side" },
                new[] { "they sent the model back, what did they change",
                        "these two sets should be the same",
                        "compare this model against that one" }),

            ["notice"] = (new[] { "variation", "change order", "claim", "scope change" },
                new[] { "they changed it after we agreed", "I need to raise a variation" }),

            ["baseline"] = (new[] { "snapshot", "record", "mark", "capture" },
                new[] { "save what it looks like now", "I want to compare against this later" }),

            ["data"] = (new[] { "properties", "parameters", "attributes", "edit data", "grid" },
                new[] { "I need to see every property at once", "bulk edit properties" }),

            ["excel"] = (new[] { "excel", "round trip", "workbook", "spreadsheet out and back" },
                new[] { "get this into Excel", "the QS sent a spreadsheet back" }),

            ["datalink"] = (new[] { "external data", "join", "key column", "asset register", "cobie" },
                new[] { "join the room schedule onto the model", "facilities sent an asset list" }),

            ["levels"] = (new[] { "storeys", "floors", "levels", "zoning", "grids" },
                new[] { "the model has no levels", "clashes are not grouped by floor",
                        "which floor is this on" }),

            ["takeoff"] = (new[] { "quantities", "measure", "counts", "schedule", "qto", "bill" },
                new[] { "how much of this is there", "I need quantities out of the model" }),

            ["report"] = (new[] { "report builder", "data report", "schedule report" },
                new[] { "a report of what the model carries", "a document from the properties" }),

            ["write"] = (new[] { "stamp", "set property", "formula", "code table", "bulk write" },
                new[] { "put this value on every element", "fill in the missing property" }),

            ["wbs"] = (new[] { "wbs", "work breakdown", "structure", "hierarchy", "packages" },
                new[] { "I need a WBS on these", "group the schedule by package",
                        "the tree knows this but the properties do not" }),

            ["ids"] = (new[] { "ids", "information delivery", "requirements", "eir", "employer information" },
                new[] { "do their models meet our requirements",
                        "check the models against the EIR" }),

            ["sets"] = (new[] { "search sets", "selection sets", "filters", "find items", "library" },
                new[] { "the search only does AND", "I need everything except this",
                        "building the same set over and over" }),

            ["schedule"] = (new[] { "sets from spreadsheet", "room list", "bulk sets" },
                new[] { "I need a set per room", "turn this sheet into sets" }),

            ["setlib"] = (new[] { "set library", "generate sets", "set tree", "per level per discipline" },
                new[] { "a set per level per discipline", "an hour of clicking sets" }),

            ["restructure"] = (new[] { "organise sets", "re-folder sets", "tidy the library" },
                new[] { "the set library is a mess", "fold the sets into folders" }),

            ["appearance"] = (new[] { "colours", "override", "hide", "isolate", "transparency", "layers" },
                new[] { "why is this thing hidden", "why is this pink", "unhide all lost my work",
                        "I cannot tell what is overridden" }),

            ["viewpoints"] = (new[] { "views", "saved viewpoints", "renumber", "rename" },
                new[] { "two hundred viewpoints named Viewpoint", "renaming these one at a time" }),

            ["sectionbox"] = (new[] { "section", "clip", "cut", "crop" },
                new[] { "I cannot see inside", "turning sectioning off loses my box" }),

            ["extract"] = (new[] { "cut out", "subset", "split", "issue a zone" },
                new[] { "send them just level 3", "they only need the plant room" }),

            ["compile"] = (new[] { "append", "federate", "combine models", "deliverable", "issue" },
                new[] { "build this week's federated model", "which revision went into that NWD" }),

            ["ifc"] = (new[] { "ifc", "openbim export", "ifc4", "ifc2x3" },
                new[] { "they want IFC", "export to IFC" }),

            ["exportglb"] = (new[] { "gltf", "glb", "web viewer", "3d", "share the model", "geometry" },
                new[] { "put this on the web", "send them the geometry", "they cannot open an nwd" }),

            ["exportsheet"] = (new[] { "csv", "xlsx", "spreadsheet", "excel", "properties", "schedule" },
                new[] { "they want it in Excel", "send them the properties",
                        "I need this as a schedule" }),

            ["exportdxf"] = (new[] { "dxf", "dwg", "autocad", "drawing", "cad", "2d" },
                new[] { "they want a DWG", "send it to the CAD team", "they do not have Navisworks" }),

            ["setout"] = (new[] { "survey", "total station", "coordinates", "points", "site" },
                new[] { "the site engineer needs these points",
                        "give me setting out for the hangers" }),

            ["dimensions"] = (new[] { "measure", "lengths", "spacing", "centres", "offsets" },
                new[] { "are these at 600 centres", "how far off the slab is that" }),

            ["weight"] = (new[] { "weight", "mass", "lifting", "cog", "crane" },
                new[] { "where do we lift this from", "how heavy is that module" }),

            ["labels"] = (new[] { "qr", "barcode", "stickers", "tags", "labels" },
                new[] { "we need to tag these on site", "print labels for the spools" }),

            ["paperwork"] = (new[] { "attachments", "links", "documents", "photos", "datasheets" },
                new[] { "where is the datasheet for that", "are these document links still alive" }),

            ["batch"] = (new[] { "automation", "scheduled", "overnight", "unattended", "jobs" },
                new[] { "doing the same export every week", "this takes an hour every Friday" }),

            ["graph"] = (new[] { "nodes", "visual scripting", "dyncamelo", "workflow" },
                new[] { "I want to chain these together" }),

            ["help"] = (new[] { "documentation", "support", "diagnostics", "about", "version",
                                "find a tool" },
                new[] { "how do I do this", "something is broken", "what version am I on",
                        "where is that tool" }),
        };

        /// <summary>Which tool ids the table knows, so a check can hold it complete.</summary>
        public static IEnumerable<string> Known => Words.Keys;

        /// <summary>
        /// Match a query against every tool's name, synonyms and symptom phrasing.
        /// </summary>
        /// <param name="query">What was typed.</param>
        public static IReadOnlyList<Tool> Search(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<Tool>();

            var needle = query!.Trim();

            return Tools.All
                .Select(t => new { Tool = t, Score = Score(t, needle) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Tool.Title, StringComparer.Ordinal)
                .Select(x => x.Tool)
                .ToList();
        }

        private static int Score(Tool tool, string needle)
        {
            if (Has(tool.Title, needle)) return tool.Title.Length == needle.Length ? 100 : 80;

            if (Words.TryGetValue(tool.Id, out var words))
            {
                if (words.Symptoms.Any(s => Has(s, needle))) return 60;
                if (words.Synonyms.Any(s => Has(s, needle))) return 50;
            }

            if (Has(tool.Purpose, needle)) return 20;

            // Every word matching somewhere is still a match — "hidden elements" should find the
            // appearance manager even though neither word alone is one of its synonyms.
            var parts = needle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(w => Score(tool, w) > 0)) return 10;

            return 0;
        }

        private static bool Has(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
