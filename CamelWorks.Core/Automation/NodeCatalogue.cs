using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.Core.Automation
{
    /// <summary>What travels along a wire.</summary>
    public enum PortType
    {
        /// <summary>A set of elements.</summary>
        Keys = 0,

        /// <summary>Rows and columns — a takeoff, a clash board, anything a report can hold.</summary>
        Table = 1,
    }

    /// <summary>One connection point on a node.</summary>
    public sealed class NodePort
    {
        internal NodePort(string id, string title, PortType type)
        {
            Id = id; Title = title; Type = type;
        }

        /// <summary>Stable id, used by wires.</summary>
        public string Id { get; }

        /// <summary>How it reads on the node.</summary>
        public string Title { get; }

        /// <summary>What it carries. A wire between two different types is refused.</summary>
        public PortType Type { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>What kind of control a setting needs.</summary>
    public enum SettingKind
    {
        /// <summary>Free text.</summary>
        Text = 0,

        /// <summary>A number.</summary>
        Number = 1,

        /// <summary>One of a fixed list.</summary>
        Choice = 2,
    }

    /// <summary>One thing a node needs told.</summary>
    public sealed class NodeSetting
    {
        internal NodeSetting(string id, string title, SettingKind kind, string @default,
                             IReadOnlyList<string>? choices = null)
        {
            Id = id; Title = title; Kind = kind; Default = @default; Choices = choices ?? Array.Empty<string>();
        }

        /// <summary>Stable id.</summary>
        public string Id { get; }

        /// <summary>How it reads on the node.</summary>
        public string Title { get; }

        /// <summary>What kind of control it needs.</summary>
        public SettingKind Kind { get; }

        /// <summary>What it is before anybody changes it — so a freshly dropped node already runs.</summary>
        public string Default { get; }

        /// <summary>The options, when it is a choice.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>One kind of node.</summary>
    public sealed class NodeKind
    {
        internal NodeKind(string id, string group, string title, string summary,
                          NodePort[] inputs, NodePort[] outputs, NodeSetting[] settings,
                          bool foundational = false)
        {
            Id = id; Group = group; Title = title; Summary = summary;
            Inputs = inputs; Outputs = outputs; Settings = settings;
            Foundational = foundational;
        }

        /// <summary>Stable id, written to the file.</summary>
        public string Id { get; }

        /// <summary>Which section of the add menu it appears under.</summary>
        public string Group { get; }

        /// <summary>How it reads.</summary>
        public string Title { get; }

        /// <summary>One line saying what it does.</summary>
        public string Summary { get; }

        /// <summary>Its inputs, in order.</summary>
        public IReadOnlyList<NodePort> Inputs { get; }

        /// <summary>Its outputs, in order.</summary>
        public IReadOnlyList<NodePort> Outputs { get; }

        /// <summary>What it needs told.</summary>
        public IReadOnlyList<NodeSetting> Settings { get; }

        /// <summary>
        /// True when everything after this node depends on it having worked.
        ///
        /// The steps that assemble the federation. They carry no wires - nothing downstream can be
        /// "skipped for want of an input" the way a colour node is - and yet a clash report over
        /// three models where four were meant is exactly the quietly-wrong answer that gets
        /// believed. So the runner stops on one of these rather than carrying on into a job that no
        /// longer means what it says.
        /// </summary>
        public bool Foundational { get; }

        /// <summary>A port by id, input or output.</summary>
        /// <param name="portId">The port id.</param>
        public NodePort? Port(string? portId) =>
            Inputs.Concat(Outputs).FirstOrDefault(p => string.Equals(p.Id, portId, StringComparison.Ordinal));

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Every node the canvas offers.
    ///
    /// Each one is a service the ribbon already drives. Nothing here is a second implementation, so
    /// a fix to the takeoff or the appearance planner reaches the graph and the buttons at the same
    /// time and neither front door can drift from the other.
    ///
    /// Every setting has a working default, so a node dropped on the canvas runs without being
    /// configured first.
    /// </summary>
    public static class NodeCatalogue
    {
        private static readonly NodePort[] None = Array.Empty<NodePort>();
        private static readonly NodeSetting[] NoSettings = Array.Empty<NodeSetting>();

        private static NodePort Keys(string id, string title) => new NodePort(id, title, PortType.Keys);
        private static NodePort Table(string id, string title) => new NodePort(id, title, PortType.Table);

        private static NodeSetting Text(string id, string title, string value = "") =>
            new NodeSetting(id, title, SettingKind.Text, value);

        private static NodeSetting Number(string id, string title, string value) =>
            new NodeSetting(id, title, SettingKind.Number, value);

        private static NodeSetting Choice(string id, string title, string value, params string[] choices) =>
            new NodeSetting(id, title, SettingKind.Choice, value, choices);

        /// <summary>Every node kind, in menu order.</summary>
        public static IReadOnlyList<NodeKind> All { get; } = new[]
        {
            new NodeKind("input.everything", "Pick", "Everything",
                "Every element loaded.",
                None, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("input.selection", "Pick", "Selection",
                "Whatever is selected in Navisworks when the graph runs.",
                None, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("input.set", "Pick", "Saved set",
                "The contents of a Navisworks selection or search set, by name.",
                None, new[] { Keys("out", "elements") },
                new[] { Text("name", "Set name") }),

            new NodeKind("filter.where", "Narrow", "Where",
                "Keeps only the elements whose property matches.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") },
                new[]
                {
                    Text("category", "Category", "Element"),
                    Text("property", "Property", "Category"),
                    Choice("operator", "Comparison", "Equals",
                        "Equals", "Contains", "StartsWith", "EndsWith", "WildcardMatch",
                        "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Defined"),
                    Text("value", "Value"),
                }),

            new NodeKind("set.union", "Combine", "Union",
                "Everything in either side.",
                new[] { Keys("a", "A"), Keys("b", "B") }, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("set.intersect", "Combine", "Intersect",
                "Only what is in both sides.",
                new[] { Keys("a", "A"), Keys("b", "B") }, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("set.subtract", "Combine", "Subtract",
                "A, without anything that is also in B.",
                new[] { Keys("a", "A"), Keys("b", "B") }, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("view.colour", "Look", "Colour",
                "Overrides the colour, and passes the elements on so more can be done to them.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") },
                new[] { Text("colour", "Colour (#rrggbb)", "#cc3333") }),

            new NodeKind("view.transparency", "Look", "Transparency",
                "0 is opaque, 1 is invisible.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") },
                new[] { Number("value", "Transparency", "0.7") }),

            new NodeKind("view.visibility", "Look", "Show or hide",
                "Hides or shows the elements.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") },
                new[] { Choice("state", "State", "hide", "hide", "show") }),

            new NodeKind("view.select", "Look", "Select",
                "Makes these the current selection in Navisworks.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") }, NoSettings),

            new NodeKind("data.write", "Data", "Write a property",
                "Writes into the CamelWorks tab, never into the authoring tool's own.",
                new[] { Keys("in", "elements") }, new[] { Keys("out", "elements") },
                new[] { Text("name", "Property", "Zone"), Text("value", "Value") }),

            new NodeKind("data.takeoff", "Data", "Takeoff",
                "Sums a numeric property, grouped.",
                new[] { Keys("in", "elements") }, new[] { Table("out", "table") },
                new[]
                {
                    Choice("measure", "Measure", "Volume", "Volume", "Area", "Length", "Count"),
                    Choice("group", "Group by", "Category", "Category", "Type", "Model", "Level"),
                }),

            new NodeKind("data.pivot", "Data", "Pivot",
                "Sums a measure across two axes at once - concrete by level, ductwork by system.",
                new[] { Keys("in", "elements") }, new[] { Table("out", "table") },
                new[]
                {
                    Choice("measure", "Measure", "Volume", "Volume", "Area", "Length", "Count"),
                    Choice("rows", "Rows", "Category",
                        "Category", "Type", "Model", "Level", "Discipline", "System"),
                    Choice("columns", "Columns", "Level",
                        "Category", "Type", "Model", "Level", "Discipline", "System"),
                }),

            new NodeKind("health.check", "Data", "Health check",
                "Runs the built-in model checks and hands on what they found.",
                None, new[] { Table("out", "table") }, NoSettings),

            new NodeKind("clash.board", "Data", "Clash board",
                "The board as the rules leave it, as a table.",
                None, new[] { Table("out", "table") }, NoSettings),

            new NodeKind("clash.run", "Coordinate", "Run the tests",
                "Runs the clash tests in the document, so the board that follows is of today.",
                None, None,
                new[]
                {
                    Choice("which", "Run", "Only ones that never have",
                        "Only ones that never have", "Every test"),
                }),

            new NodeKind("clash.bcf", "Coordinate", "Export BCF",
                "Writes the board as a BCF file, cameras and components included.",
                None, None,
                new[]
                {
                    Text("path", "File"),
                    Choice("version", "Version", "2.1", "2.1", "3.0"),
                    Choice("snapshots", "Pictures", "No", "No", "Yes"),
                }),

            new NodeKind("clash.viewpoints", "Coordinate", "Save a walkthrough",
                "One saved viewpoint per group on the board, framed on the group.",
                None, None,
                new[]
                {
                    Text("folder", "Folder", "Coordination"),
                    Text("pattern", "Name pattern", "{n}. {group}"),
                }),

            new NodeKind("sets.publish", "Deliver", "Publish a set",
                "Puts the elements into the host's selection sets, where any user can find them.",
                new[] { Keys("in", "elements") }, None,
                new[] { Text("name", "Set name", "CamelWorks working set") }),

            new NodeKind("report.write", "Deliver", "Report",
                "Writes the table as a PDF, spreadsheet or web page.",
                new[] { Table("in", "table") }, None,
                new[]
                {
                    Text("path", "File"),
                    Choice("format", "Format", "PDF", "PDF", "XLSX", "HTML"),
                    Text("title", "Title", "CamelWorks report"),
                }),

            new NodeKind("model.append", "Model", "Append a file",
                "Adds a file to the federation, the way File > Append does. The step that makes an "
                + "overnight job possible: a job that assembles its own federation needs no "
                + "prior manual assembly.",
                None, None,
                new[] { Text("path", "File") }, foundational: true),

            new NodeKind("model.refresh", "Model", "Refresh from disk",
                "Reloads every model whose file has changed since it was opened, so what follows is "
                + "about today's models rather than Tuesday's.",
                None, None, NoSettings, foundational: true),

            new NodeKind("model.save", "Model", "Save the federation",
                "Writes the document out. The extension decides it: .nwf keeps the links so the next "
                + "refresh still works, .nwd embeds the geometry so it opens without the source "
                + "files.",
                None, None,
                new[] { Text("path", "File") }, foundational: true),

            new NodeKind("csv.write", "Deliver", "CSV",
                "Writes the table as comma-separated text.",
                new[] { Table("in", "table") }, None,
                new[] { Text("path", "File") }),
        };

        /// <summary>A kind by id, or null when a saved graph names one this build does not have.</summary>
        /// <param name="id">The kind id.</param>
        public static NodeKind? Find(string? id) =>
            All.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.Ordinal));

        /// <summary>The groups, in menu order.</summary>
        public static IReadOnlyList<string> Groups { get; } =
            All.Select(k => k.Group).Distinct(StringComparer.Ordinal).ToList();
    }
}
