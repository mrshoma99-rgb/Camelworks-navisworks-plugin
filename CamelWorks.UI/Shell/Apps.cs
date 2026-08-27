using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One tab of an app: how it reads on the strip, and the tool it shows.</summary>
    public sealed class AppTab
    {
        internal AppTab(string title, string toolId)
        {
            Title = title;
            ToolId = toolId;
        }

        /// <summary>How the tab reads on the strip — short, because twelve apps share one voice.</summary>
        public string Title { get; }

        /// <summary>The tool the tab shows.</summary>
        public string ToolId { get; }
    }

    /// <summary>
    /// One app of the suite: a name, a job, and its tabs.
    /// </summary>
    public sealed class App
    {
        internal App(string id, string title, string job, string family, params AppTab[] tabs)
        {
            Id = id;
            Title = title;
            Job = job;
            Family = family;
            Tabs = tabs;
        }

        /// <summary>What the code calls it.</summary>
        public string Id { get; }

        /// <summary>What the ribbon button and the title bar call it.</summary>
        public string Title { get; }

        /// <summary>The job, in one sentence, for the hub's directory and the button's tooltip.</summary>
        public string Job { get; }

        /// <summary>The skin family its accent is drawn in, such as <c>Coordinate</c>.</summary>
        public string Family { get; }

        /// <summary>The modes, left to right on the strip.</summary>
        public IReadOnlyList<AppTab> Tabs { get; }

        /// <inheritdoc />
        public override string ToString() => Title + " — " + Job;
    }

    /// <summary>
    /// The suite: eleven apps, flat, each a self-contained product.
    ///
    /// The fourth shape of the front end and the first one modelled on a collection the client
    /// named — the DiRoots plug-in suites, whose law is a flat row of named apps, each opening on
    /// its data with its modes as tabs across the top and its one action bottom-right.
    /// `CAMELWORKS_DESIGN.md` is the law this file implements.
    ///
    /// Tools keep their identity in <see cref="Tools"/>; an app decides where a tool appears,
    /// never what it is. Review, Section Box, Help and the hub live outside the table: the
    /// palette, the act, the lifeboat and the front door.
    /// </summary>
    public static class Apps
    {
        /// <summary>The apps, in ribbon order.</summary>
        public static IReadOnlyList<App> All { get; } = new[]
        {
            new App("clash", "Clash Manager",
                "Find what fights, and decide it.", "Coordinate",
                new AppTab("Tests", "tests"),
                new AppTab("Matrix", "matrix"),
                new AppTab("Clearance", "headroom"),
                new AppTab("Rules", "rules"),
                new AppTab("Board", "triage"),
                new AppTab("Freeze", "freeze")),

            new App("issues", "Issue Link",
                "Issues out to the world, and answers back in.", "Coordinate",
                new AppTab("Report", "clashreport"),
                new AppTab("BCF", "bcf"),
                new AppTab("Clash XML", "clashxml"),
                new AppTab("Merge", "merge"),
                new AppTab("Penetrations", "penetrations")),

            new App("audit", "Model Audit",
                "Whether this federation is fit to coordinate against.", "Project",
                new AppTab("Health", "health"),
                new AppTab("IDS", "ids"),
                new AppTab("Models", "models"),
                new AppTab("Broken Links", "links"),
                new AppTab("Project", "project")),

            new App("track", "Compare Track",
                "What changed, against what, since when.", "Compare",
                new AppTab("Compare", "compare"),
                new AppTab("Changes", "changes"),
                new AppTab("Change Notice", "notice"),
                new AppTab("History", "history")),

            new App("data", "Data Manager",
                "What the elements carry, and the pen to fix it.", "Data",
                new AppTab("Properties", "data"),
                new AppTab("Write", "write"),
                new AppTab("Excel", "excel"),
                new AppTab("External Data", "datalink"),
                new AppTab("Levels & Zones", "levels"),
                new AppTab("Breakdown", "wbs")),

            new App("quantify", "Quantify",
                "Numbers out of the model, the way a bill wants them.", "Data",
                new AppTab("Takeoff", "takeoff"),
                new AppTab("Report Builder", "report"),
                new AppTab("Data Export", "exportsheet")),

            new App("sets", "Sets Manager",
                "The selections everything else works on.", "Sets",
                new AppTab("Builder", "sets"),
                new AppTab("From a Schedule", "schedule"),
                new AppTab("Generate Library", "setlib"),
                new AppTab("Organise", "restructure")),

            new App("scenes", "Scene Kit",
                "What is visible, and what colour it is.", "Sets",
                new AppTab("Appearance", "appearance"),
                new AppTab("Viewpoints", "viewpoints")),

            new App("exports", "Export Kit",
                "The model as a file somebody else opens.", "Batch",
                new AppTab("IFC", "ifc"),
                new AppTab("GLB", "exportglb"),
                new AppTab("Drawing", "exportdxf"),
                new AppTab("Extract", "extract"),
                new AppTab("Compile", "compile")),

            new App("site", "Site Pack",
                "The model reaching the people building it.", "Batch",
                new AppTab("Setting Out", "setout"),
                new AppTab("Dimensions", "dimensions"),
                new AppTab("Weight", "weight"),
                new AppTab("Labels", "labels"),
                new AppTab("Documents", "paperwork")),

            new App("automate", "Automate",
                "Doing all of it again next Friday, in one press.", "Automate",
                new AppTab("Jobs", "batch"),
                new AppTab("Canvas", "graph")),
        };

        /// <summary>Find an app by id.</summary>
        /// <param name="appId">The app's id.</param>
        public static App? Find(string? appId) =>
            appId == null ? null : All.FirstOrDefault(
                a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The app a tool sits in, or null for the four that sit in none — Review's palette,
        /// the two acts, and Help.
        /// </summary>
        /// <param name="toolId">The tool's id.</param>
        public static App? Of(string? toolId)
        {
            if (toolId == null) return null;

            return All.FirstOrDefault(a => a.Tabs.Any(
                t => string.Equals(t.ToolId, toolId, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
