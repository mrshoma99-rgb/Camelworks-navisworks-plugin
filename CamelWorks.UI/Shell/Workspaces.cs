using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.UI.Shell
{
    /// <summary>One tab inside a workspace.</summary>
    public sealed class WorkspaceTab
    {
        internal WorkspaceTab(string id, string title, string summary)
        {
            Id = id; Title = title; Summary = summary;
        }

        /// <summary>Stable id, used by ribbon routing.</summary>
        public string Id { get; }

        /// <summary>How it reads on the tab strip.</summary>
        public string Title { get; }

        /// <summary>One line saying what the tab is for, shown before anything has been run.</summary>
        public string Summary { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>One entry on the workspace switcher.</summary>
    public sealed class Workspace
    {
        internal Workspace(string id, string title, string paneId, bool hasTabStrip, params WorkspaceTab[] tabs)
        {
            Id = id; Title = title; PaneId = paneId; HasTabStrip = hasTabStrip; Tabs = tabs;
        }

        /// <summary>Stable id.</summary>
        public string Id { get; }

        /// <summary>How it reads on the switcher.</summary>
        public string Title { get; }

        /// <summary>Which pane it lives in.</summary>
        public string PaneId { get; }

        /// <summary>Its tabs, in order.</summary>
        public IReadOnlyList<WorkspaceTab> Tabs { get; }

        /// <summary>
        /// Whether the workspace shows a tab strip.
        ///
        /// Home and the graph canvas are single screens rather than tabbed ones, and a strip with
        /// one tab in it is a control that does nothing while taking a row of vertical space —
        /// which matters at the 340 DIP height the panes have to stay usable at.
        /// </summary>
        public bool HasTabStrip { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// The navigation model: two panes, eight switcher entries, twenty-one tabs.
    ///
    /// Held as data rather than as markup because three separate things have to agree on it — the
    /// ribbon's routing targets, the shell's switcher, and the command catalogue behind Find a
    /// Tool. Written three times, they would disagree within a week, and the symptom would be a
    /// ribbon button that opens the pane on the wrong tab.
    /// </summary>
    public static class Workspaces
    {
        /// <summary>The main pane's id.</summary>
        public const string MainPane = "CamelWorks";

        /// <summary>The graph editor's pane id. Separate because the canvas needs full width.</summary>
        public const string AutomatePane = "CamelWorks.Automate";

        /// <summary>Every workspace, in switcher order.</summary>
        public static IReadOnlyList<Workspace> All { get; } = new[]
        {
            new Workspace("Home", "Home", MainPane, hasTabStrip: false,
                new WorkspaceTab("This week", "This week",
                    "The weekly cycle as the front door: reconcile, regroup, report — and when each "
                    + "of those last happened.")),

            new Workspace("Project", "Project", MainPane, hasTabStrip: true,
                new WorkspaceTab("Health", "Health",
                    "One scorecard over models, sets and data. Runs its built-in rules on a raw "
                    + "model with nothing configured."),
                new WorkspaceTab("Setup", "Setup",
                    "What CamelWorks already derived, with an override on each line. Skippable."),
                new WorkspaceTab("Profile", "Profile",
                    "Responsible parties, priorities and code tables. Created lazily; nothing blocks "
                    + "on it."),
                new WorkspaceTab("Export", "Export IFC",
                    "The IFC exporter, full pane.")),

            new Workspace("Coordinate", "Coordinate", MainPane, hasTabStrip: true,
                new WorkspaceTab("Triage", "Triage",
                    "Every clash the tests found, grouped into the issues somebody actually fixes."),
                new WorkspaceTab("Tests", "Tests",
                    "What each test last did, and two ways to make more: a clash matrix, and the "
                    + "clear-height check no clash test can do."),
                new WorkspaceTab("Rules", "Rules",
                    "Suppress and flag, then group, then assign — one pipeline, previewed before it "
                    + "is applied."),
                new WorkspaceTab("Report", "Report",
                    "PDF, XLSX or HTML from a built-in template. Nothing to configure first."),
                new WorkspaceTab("BCF", "BCF",
                    "Export and import, BCF 2.1 and 3.0, with a conflict preview on the way in.")),

            new Workspace("Compare", "Compare", MainPane, hasTabStrip: true,
                new WorkspaceTab("Run", "Run",
                    "Pick a baseline and see what changed. With no baseline yet, one button makes "
                    + "one — that is the whole of the setup."),
                new WorkspaceTab("Changes", "Changes",
                    "The change list, filterable, with what each element was and is. Paint it onto "
                    + "the model, stamp it into the properties, or send it out as a report."),
                new WorkspaceTab("Baselines", "Baselines",
                    "What has been recorded, when, and how much of it. Kept beside the document so "
                    + "a job handed over arrives with its history.")),

            new Workspace("Data", "Data", MainPane, hasTabStrip: true,
                new WorkspaceTab("Data", "Data",
                    "Browse and edit properties in one place, including calculated columns."),
                new WorkspaceTab("Zones", "Levels",
                    "Derives levels from whatever the model has — grids, then a height histogram — "
                    + "and says which source it used."),
                new WorkspaceTab("Takeoff", "Takeoff",
                    "Sums the numeric properties it finds, grouped, and reports whatever it could "
                    + "not read rather than dropping it.")),

            new Workspace("Sets", "Sets & Views", MainPane, hasTabStrip: true,
                new WorkspaceTab("Sets", "Sets",
                    "Boolean set building — AND, OR, NOT and references to other sets — compiled "
                    + "down to native search sets."),
                new WorkspaceTab("Appearance", "Appearance",
                    "The layers system: what is hidden, what is overridden, by which layer, and "
                    + "what each layer is doing."),
                new WorkspaceTab("Viewpoints", "Viewpoints",
                    "Bulk rename, renumber and re-folder; batch render; copy overrides between "
                    + "viewpoints.")),

            // No strip: it held one tab, so it was a row of chrome that said "Jobs" above a screen
            // already titled Jobs, and offered nowhere else to go.
            new Workspace("Batch", "Batch", MainPane, hasTabStrip: false,
                new WorkspaceTab("Jobs", "Jobs",
                    "Saved jobs and what each one last did. One click turns this document into "
                    + "another.")),

            new Workspace("Automate", "Automate", AutomatePane, hasTabStrip: false,
                new WorkspaceTab("Canvas", "Canvas",
                    "The graph canvas. In its own pane because it needs the full width.")),
        };

        /// <summary>Find a workspace by id.</summary>
        public static Workspace? Find(string? workspaceId) =>
            All.FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Find a tab by its "workspace/tab" route.</summary>
        public static (Workspace? Workspace, WorkspaceTab? Tab) Route(string? route)
        {
            if (string.IsNullOrWhiteSpace(route)) return (null, null);

            var parts = route!.Split('/');
            var workspace = Find(parts[0]);
            if (workspace == null) return (null, null);

            if (parts.Length < 2) return (workspace, workspace.Tabs.FirstOrDefault());

            var tab = workspace.Tabs.FirstOrDefault(
                t => string.Equals(t.Id, parts[1], StringComparison.OrdinalIgnoreCase));

            return (workspace, tab ?? workspace.Tabs.FirstOrDefault());
        }

        /// <summary>The switcher entries in the main pane.</summary>
        public static IReadOnlyList<Workspace> Switcher =>
            All.Where(w => w.PaneId == MainPane).ToList();

        /// <summary>
        /// How many tabs the tabbed workspaces have between them.
        ///
        /// Asserted by a test against the number the spec states, so the two cannot drift apart
        /// quietly — a tab added here and not there is a tab nobody documented.
        /// </summary>
        public static int TabCount => All.Where(w => w.HasTabStrip).Sum(w => w.Tabs.Count);
    }
}
