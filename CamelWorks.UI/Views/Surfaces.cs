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
                case "notice": return OnModel(_ => FreezeViews.NoticeScreen());
                case "sidebyside": return OnModel(SideBySideViews.Surface);
                case "history": return OnModel(HistoryWindow.Surface);
                case "project": return ProjectViews.Settings();

                // ---- Coordination ----
                case "tests": return CoordinateViews.Tests();
                case "manage": return OnModel(ClashManageViews.Manage);
                case "issuelist": return OnModel(IssueViews.Register);
                case "issuein": return OnModel(IssueViews.Import);
                case "issueout": return OnModel(IssueViews.Export);
                case "matrix": return OnModel(MatrixWindow.Surface);
                case "headroom": return OnModel(ClearanceViews.Check);
                case "rules": return CoordinateViews.Rules();
                case "clashreport": return CoordinateViews.Report();
                case "bcf": return CoordinateViews.Bcf();
                case "clashxml": return OnModel(ClashExportWindow.Surface);
                case "penetrations": return OnModel(_ => CoordinateViews.PenetrationsReport());
                case "merge": return CoordinateViews.Merge();
                case "freeze": return OnModel(_ => FreezeViews.FreezeScreen());

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
    }
}
