using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Automation;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Report;
using CamelWorks.Core.Sets;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// What a graph node actually does, in Navisworks.
    ///
    /// Every method here is a call into a service the ribbon also calls. That is the point of the
    /// two front doors: the graph is not a second implementation of anything, so a node and the
    /// button beside it cannot come to disagree.
    /// </summary>
    public sealed class NavGraphHost : IGraphHost
    {
        private readonly Session _session;
        private IReadOnlyList<ElementKey>? _everything;

        /// <summary>Create a host over one session.</summary>
        /// <param name="session">The document to work on.</param>
        public NavGraphHost(Session session) =>
            _session = session ?? throw new ArgumentNullException(nameof(session));

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Everything() =>
            _everything ??= _session.Model.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Selection() => _session.Model.SelectedKeys();

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Set(string name) =>
            _session.Model.Traverse(TraversalScope.Set(name)).Select(i => i.Key).ToList();

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Where(IReadOnlyList<ElementKey> within, SetCondition condition)
        {
            var found = _session.Search.Run(DnfCompiler.Compile(SetExpression.Where(condition)));

            // Narrowed rather than replaced: the node's contract is "keep the ones that match", and
            // a host search always answers about the whole model.
            var matched = new HashSet<ElementKey>(found.Keys);
            return within.Where(matched.Contains).ToList();
        }

        /// <inheritdoc />
        public void Colour(IReadOnlyList<ElementKey> keys, Colour colour) => _session.View.SetColour(keys, colour);

        /// <inheritdoc />
        public void Transparency(IReadOnlyList<ElementKey> keys, double value) =>
            _session.View.SetTransparency(keys, value);

        /// <inheritdoc />
        public void Visible(IReadOnlyList<ElementKey> keys, bool visible) => _session.View.SetVisible(keys, visible);

        /// <inheritdoc />
        public void Select(IReadOnlyList<ElementKey> keys) => _session.Model.Select(keys);

        /// <inheritdoc />
        public int Write(IReadOnlyList<ElementKey> keys, string property, string? value)
        {
            if (string.IsNullOrWhiteSpace(property)) return 0;

            using (var write = new NavWriteTransaction(_session.Model, "CamelWorks: " + property))
            {
                foreach (var key in keys) write.SetProperty(key, NavWriteTransaction.TabName, property, value);

                return write.Commit();
            }
        }

        /// <inheritdoc />
        public TableBlock Takeoff(IReadOnlyList<ElementKey> keys, string measure, string group)
        {
            var lines = new List<TakeoffLine>();

            foreach (var key in keys)
            {
                var item = _session.Model.Resolve(key);
                if (item == null) continue;

                lines.Add(new TakeoffLine(item.DisplayName, GroupOf(item, group),
                    measure == "Count" ? "1" : Measure(item, measure)));
            }

            var result = Core.Data.Takeoff.Sum(lines);
            var table = new TableBlock("Group", "Count", "Total", "Unreadable");

            foreach (var line in result.Groups.OrderByDescending(g => g.Count))
                table.Row(line.Name,
                    line.Count.ToString(CultureInfo.InvariantCulture),
                    line.Total?.ToString() ?? (line.MixedKinds ? "(mixed units)" : string.Empty),
                    line.Unreadable > 0 ? line.Unreadable.ToString(CultureInfo.InvariantCulture) : string.Empty);

            return table;
        }

        /// <inheritdoc />
        public TableBlock ClashBoard()
        {
            var notes = new List<string>();
            var board = CoordinateViews.Build(_session, Job.Silent(), notes);

            if (board == null)
            {
                // An empty table rather than an exception: a graph whose clash node found nothing
                // should write an empty report and say so, not stop the whole run.
                var empty = new TableBlock("Group", "Clashes");
                return empty;
            }

            var table = new TableBlock("Group", "Clashes", "Assigned to", "Priority", "Level", "Models");

            foreach (var group in board.Groups)
                table.Row(group.Name,
                    group.Items.Count.ToString(CultureInfo.InvariantCulture),
                    group.AssignedTo ?? "unassigned",
                    group.Priority ?? string.Empty,
                    group.Items.FirstOrDefault()?.Level ?? string.Empty,
                    group.Items.FirstOrDefault()?.ModelPair ?? string.Empty);

            return table;
        }

        /// <inheritdoc />
        public void Report(string path, string format, string title, TableBlock table)
        {
            var document = new ReportDocument(string.IsNullOrWhiteSpace(title) ? "CamelWorks report" : title)
            {
                Subtitle = _session.Profile.ProjectName,
            };

            document.Fact("Project", _session.Profile.ProjectName);
            document.Fact("Author", _session.Profile.Author);
            document.Fact("Date", DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            document.Fact("Rows", table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture));
            document.Add(table);

            switch (format)
            {
                case "XLSX":
                    File.WriteAllBytes(path, XlsxWriter.Write("Report", table));
                    break;

                case "HTML":
                    File.WriteAllText(path, HtmlReportWriter.Write(document));
                    break;

                default:
                {
                    using (var stream = File.Create(path)) PdfReportWriter.Write(stream, document);
                    break;
                }
            }
        }

        /// <inheritdoc />
        public void Csv(string path, TableBlock table)
        {
            var text = new StringBuilder();

            text.AppendLine(string.Join(",", table.Headers.Select(Escape)));

            foreach (var row in table.Rows)
                text.AppendLine(string.Join(",", row.Select(cell => Escape(cell ?? string.Empty))));

            // UTF-8 with a byte order mark, because the one thing every CSV consumer on Windows
            // does badly is guess the encoding, and Excel guesses ANSI without it.
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        }

        /// <inheritdoc />
        public TableBlock Pivot(IReadOnlyList<ElementKey> keys, string measure, string rows, string columns)
        {
            var lines = new List<PivotLine>();

            foreach (var key in keys)
            {
                var item = _session.Model.Resolve(key);
                if (item == null) continue;

                lines.Add(new PivotLine(item.DisplayName,
                    AxisOf(item, rows), AxisOf(item, columns),
                    measure == "Count" ? "1" : Measure(item, measure)));
            }

            var pivot = TakeoffPivot.Of(lines);

            var headers = new List<string> { rows };
            headers.AddRange(pivot.Columns);
            headers.Add("Total");

            var table = new TableBlock(headers.ToArray());

            foreach (var row in pivot.Rows)
            {
                var cells = new List<string> { row };

                foreach (var column in pivot.Columns) cells.Add(Cell(pivot.Cell(row, column)));

                cells.Add(Cell(pivot.RowTotal(row)));
                table.Row(cells.ToArray());
            }

            var totals = new List<string> { "All" };

            foreach (var column in pivot.Columns) totals.Add(Cell(pivot.ColumnTotal(column)));

            totals.Add(Cell(pivot.Grand));
            table.Row(totals.ToArray());

            return table;
        }

        /// <inheritdoc />
        public TableBlock Health()
        {
            var elements = new List<HealthElement>();
            var scale = _session.MetresPerUnit;

            foreach (var item in _session.Model.Traverse(TraversalScope.WholeDocument))
            {
                var bounds = item.Bounds;

                elements.Add(new HealthElement(item.Key.ToString(), item.Model.DisplayName)
                {
                    Name = item.DisplayName,
                    Category = item.Category,
                    X = bounds.CentreX * scale,
                    Y = bounds.CentreY * scale,
                    Z = bounds.CentreZ * scale,
                    SizeX = bounds.SizeX * scale,
                    SizeY = bounds.SizeY * scale,
                    SizeZ = bounds.SizeZ * scale,
                    PropertyCount = item.Properties().Any() ? 1 : 0,
                });
            }

            var report = ModelHealth.Check(elements);
            var table = new TableBlock("Finding", "What to do", "Examples");

            foreach (var finding in report.Findings)
                table.Row(finding.Summary, finding.Fix, string.Join(", ", finding.Examples));

            return table;
        }

        /// <inheritdoc />
        public int RunClashTests(bool all)
        {
            if (!_session.Clash.IsAvailable || !_session.Clash.CanEdit) return 0;

            var ran = 0;

            foreach (var test in _session.Clash.Tests())
            {
                if (!all && test.LastRunTicks > 0) continue;
                if (_session.Clash.Run(test.Id)) ran++;
            }

            return ran;
        }

        /// <inheritdoc />
        public void Bcf(string path, string version, bool snapshots)
        {
            var notes = new List<string>();
            var board = CoordinateViews.Build(_session, Job.Silent(), notes);

            if (board == null) return;

            var topics = CoordinateViews.TopicsOf(_session, board, snapshots, Job.Silent());

            using (var stream = File.Create(path))
            {
                BcfWriter.Write(stream, topics,
                    version == "3.0" ? BcfVersion.V30 : BcfVersion.V21,
                    new BcfProject(_session.Profile.ProjectName, _session.Profile.ProjectName));
            }
        }

        /// <inheritdoc />
        public int Walkthrough(string folder, string pattern)
        {
            var notes = new List<string>();
            var board = CoordinateViews.Build(_session, Job.Silent(), notes);

            if (board == null) return 0;

            var into = (folder ?? string.Empty).Trim();
            var template = string.IsNullOrWhiteSpace(pattern) ? "{n}. {group}" : pattern;
            var saved = 0;
            var n = 1;

            // Borrowed once and put back at the end, exactly as the Viewpoints tab does it. A graph
            // that ran overnight and left the camera on the last clash of two hundred would be a
            // small betrayal somebody notices first thing in the morning.
            using (_session.View.BorrowCamera())
            {
                foreach (var group in board.Groups)
                {
                    var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();

                    if (points.Count == 0) { n++; continue; }

                    _session.View.FrameBox(CoordinateViews.BoxOf(points));

                    _session.Viewpoints.SaveCurrent(
                        template
                            .Replace("{n}", n.ToString("00", CultureInfo.InvariantCulture))
                            .Replace("{group}", group.Name)
                            .Replace("{who}", group.AssignedTo ?? "unassigned")
                            .Replace("{count}", group.Items.Count.ToString(CultureInfo.InvariantCulture)),
                        into.Length == 0 ? null : into);

                    saved++;
                    n++;
                }
            }

            return saved;
        }

        /// <inheritdoc />
        public void PublishSet(string name, IReadOnlyList<ElementKey> keys)
        {
            if (keys.Count == 0) return;

            _session.Search.PublishKeys(name, keys);
        }

        /// <summary>How one pivot cell reads: its total, or why there is not one.</summary>
        private static string Cell(TakeoffGroup? group)
        {
            if (group == null || group.Count == 0) return string.Empty;
            if (group.MixedKinds) return "(mixed units)";

            return group.Total?.ToString()
                   ?? group.Count.ToString("N0", CultureInfo.InvariantCulture) + " elements";
        }

        /// <summary>Which bucket an element falls into on one pivot axis.</summary>
        private string AxisOf(IModelItem item, string axis) => axis switch
        {
            "Type" => item.TypeName ?? PivotLine.None,
            "Model" => item.Model.DisplayName,
            "Discipline" => _session.Profile.DisciplineOf(item.Model.Scope) ?? PivotLine.None,
            "System" => item.Property("Element", "System Name")
                        ?? item.Property("Element", "System")
                        ?? item.Property("Item", "System")
                        ?? PivotLine.None,
            "Level" => item.Property(NavWriteTransaction.TabName, "Level")
                       ?? item.Property("Element", "Level")
                       ?? item.Property("Item", "Level")
                       ?? PivotLine.None,
            _ => item.Category ?? PivotLine.None,
        };

        private static string Escape(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"") + "\"";

        private static string? Measure(IModelItem item, string measure) =>
            item.Property("Element", measure)
            ?? item.Property("Item", measure)
            ?? item.Property("Revit Material Takeoff", measure)
            ?? item.Property(NavWriteTransaction.TabName, measure);

        private static string GroupOf(IModelItem item, string group) => group switch
        {
            "Type" => item.TypeName ?? "(no type)",
            "Model" => item.Model.DisplayName,
            "Level" => item.Property(NavWriteTransaction.TabName, "Level")
                       ?? item.Property("Element", "Level")
                       ?? item.Property("Item", "Level")
                       ?? "(no level)",
            _ => item.Category ?? "(no category)",
        };
    }
}
