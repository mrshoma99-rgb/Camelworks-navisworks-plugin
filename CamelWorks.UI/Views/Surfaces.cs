using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Every tool surface, by tool id — the one dispatch the desks bench from.
    ///
    /// A surface is a factory, not a window: the desk decides where it appears. Tools that need a
    /// live document get one through <see cref="OnModel"/>, which turns "no model" into a stated
    /// empty surface rather than a message box — a desk is somewhere you can stand before the
    /// model is open, and its rail should not slap your hand for looking.
    /// </summary>
    public static class Surfaces
    {
        /// <summary>
        /// Build the surface for a tool.
        /// </summary>
        /// <param name="toolId">Which tool.</param>
        public static UIElement Build(string toolId)
        {
            switch ((toolId ?? string.Empty).ToLowerInvariant())
            {
                // ---- Federation ----
                case "models": return OnModel(ModelsWindow.Surface);
                case "links": return OnModel(FixLinksWindow.Surface);
                case "compile": return OnModel(CompileWindow.Surface);
                case "health": return ProjectViews.Health();
                case "ids": return OnModel(IdsWindow.Surface);
                case "compare": return CompareViews.Run();
                case "changes": return CompareViews.Changes();
                case "notice": return OnModel(_ => CoordinateViews.NoticeScreen());
                case "history": return OnModel(HistoryWindow.Surface);
                case "project": return ProjectViews.Settings();

                // ---- Coordination ----
                case "triage": return CoordinateViews.Triage();
                case "tests": return CoordinateViews.Tests();
                case "matrix": return OnModel(MatrixWindow.Surface);
                case "headroom": return CoordinateViews.ClearHeight();
                case "rules": return CoordinateViews.Rules();
                case "clashreport": return CoordinateViews.Report();
                case "bcf": return CoordinateViews.Bcf();
                case "clashxml": return OnModel(ClashExportWindow.Surface);
                case "penetrations": return OnModel(_ => CoordinateViews.PenetrationsReport());
                case "merge": return CoordinateViews.Merge();
                case "freeze": return OnModel(_ => CoordinateViews.FreezeScreen());

                // ---- Data ----
                case "data": return DataViews.Manager();
                case "write": return OnModel(_ => DataViews.WriteProperties());
                case "excel": return DataViews.Excel();
                case "datalink": return OnModel(DataLinkWindow.Surface);
                case "levels": return DataViews.Zones();
                case "wbs": return OnModel(DataViews.BreakdownStamp);
                case "takeoff": return DataViews.Takeoff();
                case "report": return OnModel(ReportBuilderWindow.Surface);
                case "exportsheet": return OnModel(s => ExportWindow.Surface(s, "sheet"));

                // ---- Scenes ----
                case "sets": return SetsViews.Builder();
                case "appearance": return SetsViews.Appearance();
                case "schedule": return OnModel(ScheduleWindow.Surface);
                case "setlib": return OnModel(SetLibraryWindow.Surface);
                case "restructure": return OnModel(RestructureWindow.Surface);
                case "viewpoints": return SetsViews.Viewpoints();

                // ---- Deliver ----
                case "extract": return OnModel(ExtractWindow.Surface);
                case "ifc": return ProjectViews.ExportIfc();
                case "exportglb": return OnModel(s => ExportWindow.Surface(s, "glb"));
                case "exportdxf": return OnModel(s => ExportWindow.Surface(s, "dxf"));
                case "setout":
                case "dimensions":
                case "weight":
                case "labels":
                case "paperwork": return OnModel(s => SiteWindow.Surface(s, toolId!));
                case "batch": return BatchViews.Jobs();
                case "graph": return AutomateView.Build();

                default:
                    return Ui.Stack(Ui.Problem(
                        "\"" + toolId + "\" has no surface here. This is a routing mistake in this "
                        + "build, not anything you did."));
            }
        }

        /// <summary>
        /// A surface that needs a live document, or the stated reason there is none.
        /// </summary>
        /// <param name="build">Builds the surface from the session.</param>
        private static UIElement OnModel(Func<Session, UIElement> build)
        {
            var session = Host.Current;

            if (session == null)
                return Ui.Stack(Ui.Empty("No model is open.", Host.NoModel));

            return build(session);
        }
        /// <summary>
        /// The app's status strip: where its workflow stands, in numbers, with the way to each.
        ///
        /// This is what makes a window an app rather than a binder of tools: Clash Manager says
        /// on every tab when the tests last ran and what the board holds; Issue Link says what
        /// was last issued and to whom; Model Audit says what is loaded and what is broken;
        /// Compare Track says which baseline the week is measured against. Every figure is
        /// clickable and lands on the tab that owns it. Null for the apps whose state is the
        /// surface itself.
        ///
        /// Everything here is a cheap read — store, index, host registers — never a traversal:
        /// the strip refreshes on every tab switch and after every run, and a strip that costs a
        /// model walk would tax the product for the sake of its chrome.
        /// </summary>
        /// <param name="appId">Which app.</param>
        /// <param name="openTab">Open one of the app's tabs by tool id.</param>
        public static UIElement? Status(string appId, Action<string> openTab)
        {
            try
            {
                switch ((appId ?? string.Empty).ToLowerInvariant())
                {
                    case "clash": return ClashStatus(openTab);
                    case "issues": return IssuesStatus(openTab);
                    case "audit": return AuditStatus(openTab);
                    case "track": return TrackStatus(openTab);
                    case "sets": return SetsStatus(openTab);
                    case "scenes": return ScenesStatus(openTab);
                    case "data": return DataStatus(openTab);
                    case "quantify": return QuantifyStatus(openTab);
                    case "exports": return ExportsStatus(openTab);
                    case "automate": return AutomateStatus(openTab);
                    default: return null;
                }
            }
            catch (Exception)
            {
                // The strip is chrome. Chrome that can take an app down is not worth having.
                return null;
            }
        }

        private static UIElement? ClashStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            if (session.Clash.IsAvailable)
            {
                var tests = session.Clash.Tests();
                var newest = tests.Count == 0 ? 0 : tests.Max(t => t.LastRunTicks);

                row.Add(Chip(Skin.Num(tests.Count), "tests", Tone.Plain,
                                  () => openTab("tests"), "What gets tested against what"));
                row.Add(Chip(Skin.Num(tests.Sum(t => t.ResultCount)), "results", Tone.Plain,
                                  () => openTab("tests"), "Raw results across every test"));
                row.Add(Chip(newest > 0
                        ? Core.Project.ActivityLog.Ago(newest, session.NowTicks) : "never",
                        "last run", newest > 0 ? Tone.Plain : Tone.Warn,
                        () => openTab("tests"), "When any test last ran"));
            }

            var board = CoordinateViews.LastBoard;

            if (board == null)
            {
                row.Add(Chip("—", "board not loaded", Tone.Warn,
                                  () => openTab("triage"), "Load the board to work the results"));
            }
            else
            {
                row.Add(Chip(Skin.Num(board.Groups.Count), "on the board", Tone.Plain,
                                  () => openTab("triage"), "Issues after the rules"));

                var delta = CoordinateViews.LastDelta;

                if (delta != null)
                {
                    if (delta.New.Count > 0)
                        row.Add(Chip(Skin.Num(delta.New.Count), "new", Tone.Bad,
                                          () => openTab("triage"), "New since the last run"));
                    if (delta.Regressed.Count > 0)
                        row.Add(Chip(Skin.Num(delta.Regressed.Count), "back again", Tone.Bad,
                                          () => openTab("triage"), "Resolved once, clashing again"));
                    if (delta.Resolved.Count > 0)
                        row.Add(Chip(Skin.Num(delta.Resolved.Count), "resolved", Tone.Good,
                                          () => openTab("triage"), "Gone since the last run"));
                }
            }

            return Strip(row);
        }

        private static UIElement? IssuesStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            var board = CoordinateViews.LastBoard;

            row.Add(board == null
                ? Chip("—", "board not loaded", Tone.Warn,
                            () => Ui.Told(Shell.AppWindow.Show("clash", "triage")),
                            "Everything here issues the board. Load it in Clash Manager first")
                : Chip(Skin.Num(board.Groups.Count), "issues to send", Tone.Plain,
                            () => Ui.Told(Shell.AppWindow.Show("clash", "triage")),
                            "What is on the board in Clash Manager now"));

            var report = session.Store.Activity.Latest(Core.Project.ActivityKind.Report);

            row.Add(Chip(
                report == null ? "never" : Core.Project.ActivityLog.Ago(report.WhenTicks, session.NowTicks),
                "last report", report == null ? Tone.Warn : Tone.Plain,
                () => openTab("clashreport"),
                report?.Summary ?? "No report has been written on this project"));

            var exchange = session.Store.Activity.Latest(Core.Project.ActivityKind.Exchange);

            row.Add(Chip(
                exchange == null ? "never" : Core.Project.ActivityLog.Ago(exchange.WhenTicks, session.NowTicks),
                "last exchange", Tone.Plain,
                () => openTab("bcf"),
                exchange?.Summary ?? "Nothing has gone out or come back as BCF or XML"));

            return Strip(row);
        }

        private static UIElement? AuditStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            var models = session.Model.Models;

            row.Add(Chip(Skin.Num(models.Count), "models loaded", Tone.Plain,
                              () => openTab("models"), "The federation as it stands"));

            var broken = 0;

            foreach (var model in models)
            {
                if (string.IsNullOrWhiteSpace(model.SourcePath)) continue;

                try
                {
                    if (!System.IO.File.Exists(model.SourcePath)) broken++;
                }
                catch (Exception)
                {
                    // Unknown is not broken; an unreachable share should not paint the strip red.
                }
            }

            if (broken > 0)
                row.Add(Chip(Skin.Num(broken), "sources missing", Tone.Bad,
                                  () => openTab("links"),
                                  "Models showing a cached copy of a file that has moved"));

            var checked_ = session.Store.Activity.Latest(Core.Project.ActivityKind.Reconcile);

            row.Add(Chip(
                checked_ == null ? "never" : Core.Project.ActivityLog.Ago(checked_.WhenTicks, session.NowTicks),
                "last checked", checked_ == null ? Tone.Warn : Tone.Plain,
                () => openTab("health"),
                checked_?.Summary ?? "No health check has run on this project"));

            return Strip(row);
        }

        private static UIElement? DataStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            // The pen's own history, from the activity log: writing properties is the one
            // irreversible thing the suite does, so when it last happened is the first figure a
            // returning coordinator wants — and "never" is worth a colour, because a project
            // where the pen has never touched the model is usually one where it should have.
            var write = session.Store.Activity.Latest(Core.Project.ActivityKind.Write);

            row.Add(Chip(
                write == null ? "never" : Core.Project.ActivityLog.Ago(write.WhenTicks, session.NowTicks),
                "last write", write == null ? Tone.Warn : Tone.Plain,
                () => openTab("write"),
                write?.Summary ?? "No property has been written onto this model"));

            // The Excel round trip's out leg, found by its summary because the Export kind is
            // shared with geometry exports. Its detail is the path, which is what the person
            // waiting for the QS's edits actually wants to see.
            var sheet = session.Store.Activity.Entries.FirstOrDefault(a =>
                string.Equals(a.Kind, Core.Project.ActivityKind.Export, StringComparison.Ordinal)
                && a.Summary.StartsWith("wrote properties to a spreadsheet", StringComparison.Ordinal));

            row.Add(Chip(
                sheet == null ? "never" : Core.Project.ActivityLog.Ago(sheet.WhenTicks, session.NowTicks),
                "last sheet out", Tone.Plain,
                () => openTab("excel"),
                sheet?.Detail ?? sheet?.Summary ?? "No property sheet has gone out for editing"));

            var links = Core.Data.DataLink.Saved(
                session.Store.Section(Core.Project.ProjectStore.DataSection)[DataLinkWindow.Key]).Count;

            if (links > 0)
                row.Add(Chip(Skin.Num(links), "saved links", Tone.Plain,
                                  () => openTab("datalink"),
                                  "Spreadsheet joins saved to run again on a newer drop"));

            return Strip(row);
        }

        private static UIElement? QuantifyStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            // Both takeoff records land under the Report kind, told apart by their summaries -
            // "took off ..." for the sum, "wrote a takeoff spreadsheet" for the file.
            var takeoff = session.Store.Activity.Entries.FirstOrDefault(a =>
                string.Equals(a.Kind, Core.Project.ActivityKind.Report, StringComparison.Ordinal)
                && a.Summary.StartsWith("took off", StringComparison.Ordinal));

            row.Add(Chip(
                takeoff == null ? "never" : Core.Project.ActivityLog.Ago(takeoff.WhenTicks, session.NowTicks),
                "last takeoff", takeoff == null ? Tone.Warn : Tone.Plain,
                () => openTab("takeoff"),
                takeoff?.Summary ?? "No quantities have been summed on this project"));

            var reports = Core.Data.DataReport.Saved(
                session.Store.Section(Core.Project.ProjectStore.DataSection)[ReportBuilderWindow.Key]).Count;

            row.Add(Chip(reports > 0 ? Skin.Num(reports) : "none", "saved reports", Tone.Plain,
                              () => openTab("report"),
                              reports > 0
                                  ? "Report shapes saved with the project, ready to run again "
                                    + "next revision"
                                  : "Read the model on Report Builder and it proposes one"));

            var written = session.Store.Activity.Entries.FirstOrDefault(a =>
                string.Equals(a.Kind, Core.Project.ActivityKind.Export, StringComparison.Ordinal)
                && a.Summary.StartsWith("Wrote the report", StringComparison.Ordinal));

            row.Add(Chip(
                written == null ? "never" : Core.Project.ActivityLog.Ago(written.WhenTicks, session.NowTicks),
                "last report written", Tone.Plain,
                () => openTab("report"),
                written?.Detail ?? written?.Summary
                    ?? "No data report has been written out on this project"));

            return Strip(row);
        }

        private static UIElement? TrackStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            var latest = CompareViews.StoreFor(session).Latest();

            row.Add(latest == null
                ? Chip("none", "baseline", Tone.Warn, () => openTab("compare"),
                            "Take a baseline so next week has something to compare against")
                : Chip(latest.Label.Length > 0 ? latest.Label : "unnamed", "baseline",
                            Tone.Plain, () => openTab("compare"), latest.Describe()));

            var last = CompareViews.Last;

            if (last != null)
                row.Add(Chip(Skin.Num(last.Changes.Count), "changes found", Tone.Plain,
                                  () => openTab("changes"), "The last comparison's answer"));

            return Strip(row);
        }

        private static UIElement? SetsStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            // Two counts, because they are two different facts: the project's rules travel with
            // the .cw file and re-run anywhere; the document's sets are what every other tool's
            // "which set" question is answered from.
            var library = Core.Project.SetLibrary.FromJson(
                session.Store.Section(Core.Project.ProjectStore.SetsSection)["items"]);

            row.Add(Chip(Skin.Num(library.Sets.Count), "rules in the project",
                         library.Sets.Count > 0 ? Tone.Plain : Tone.Warn,
                         () => openTab("sets"),
                         "Saved rules, stored in the project file so the next person has them too"));

            row.Add(Chip(Skin.Num(session.Model.Sets().Count), "sets in the document", Tone.Plain,
                         () => openTab("restructure"),
                         "The Navisworks selection tree as it stands — what Appearance, Takeoff "
                         + "and Extract pick from"));

            return Strip(row);
        }

        private static UIElement? ScenesStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            var layers = Core.Project.LayerStackJson.Read(
                session.Store.Section(Core.Project.ProjectStore.LayersSection)["stack"]).Count;

            row.Add(Chip(layers > 0 ? Skin.Num(layers) : "none", "layers saved",
                         Tone.Plain, () => openTab("appearance"),
                         layers > 0
                             ? "The saved appearance stack. What the model shows also depends on "
                               + "overrides the scan on the Appearance tab uncovers"
                             : "No saved stack: whatever the model looks like, CamelWorks did not "
                               + "decide it. The Appearance tab's scan says who did"));

            row.Add(Chip(Skin.Num(session.Viewpoints.All().Count), "viewpoints", Tone.Plain,
                         () => openTab("viewpoints"),
                         "Saved viewpoints in the document, ready to rename, file or render"));

            return Strip(row);
        }

        private static UIElement? ExportsStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            // One kind covers every file the suite writes out, Site Pack's included - which is
            // honest for a chip that answers "did anything leave this project this week".
            var export = session.Store.Activity.Latest(Core.Project.ActivityKind.Export);

            row.Add(Chip(
                export == null ? "never" : Core.Project.ActivityLog.Ago(export.WhenTicks, session.NowTicks),
                "last export", export == null ? Tone.Warn : Tone.Plain,
                () => openTab("exportglb"),
                export?.Summary ?? "Nothing has been exported on this project"));

            var data = session.Store.Section(Core.Project.ProjectStore.DataSection);

            var extracts = Core.Publish.Extract.Saved(data[ExtractWindow.Key]).Count;

            if (extracts > 0)
                row.Add(Chip(Skin.Num(extracts), "saved extracts", Tone.Plain,
                             () => openTab("extract"),
                             "Rules that cut a named piece out of whatever is loaded today"));

            var compilations = Core.Publish.Compilation.Saved(data[CompileWindow.Key]).Count;

            if (compilations > 0)
                row.Add(Chip(Skin.Num(compilations), "saved compilations", Tone.Plain,
                             () => openTab("compile"),
                             "The deliverables Compile rebuilds from next week's folder"));

            return Strip(row);
        }

        private static UIElement? AutomateStatus(Action<string> openTab)
        {
            var session = Host.Current;
            if (session == null) return null;

            var row = new List<UIElement?>();

            var jobs = session.Store.Section(Core.Project.ProjectStore.JobsSection)["items"];
            var count = jobs.Kind == Core.Store.JsonKind.Array ? jobs.Items.Count() : 0;

            row.Add(count == 0
                ? Chip("none", "jobs saved", Tone.Warn, () => openTab("graph"),
                       "Build one on the canvas and press Save job")
                : Chip(Skin.Num(count), "jobs saved", Tone.Plain, () => openTab("batch"),
                       "In the project file, so whoever opens the model next has them too"));

            var ran = session.Store.Activity.Latest(Core.Project.ActivityKind.Job);

            row.Add(Chip(
                ran == null ? "never" : Core.Project.ActivityLog.Ago(ran.WhenTicks, session.NowTicks),
                "last run", ran == null ? Tone.Warn : Tone.Plain,
                () => openTab("batch"),
                ran?.Summary ?? "No job has run on this project"));

            return Strip(row);
        }

        /// <summary>The chips in one row, or null when there is nothing worth a strip.</summary>
        private static UIElement? Strip(IReadOnlyList<UIElement?> chips)
        {
            var real = chips.Where(c => c != null).Cast<UIElement>().ToList();
            if (real.Count == 0) return null;

            return Ui.Across(real.ToArray());
        }

        /// <summary>One strip figure with the sentence behind it as a tooltip.</summary>
        private static UIElement Chip(string value, string label, Tone tone, Action onClick, string tip)
        {
            var figure = Ui.Figure(value, label, tone, onClick);

            if (figure is FrameworkElement fe) fe.ToolTip = tip;

            return figure;
        }

    }
}
