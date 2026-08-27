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
