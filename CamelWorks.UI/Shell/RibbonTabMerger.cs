using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// Merges the BIMCamel ribbon tabs of every installed BIMCamel plug-in into one.
    ///
    /// CamelWorks, Dyncamelo and the BIMCamel IFC exporter all declare the same ribbon tab id
    /// (<c>ID_Tab_BIMCamel</c>) in their <c>RibbonLayout</c> markup, but Navisworks composes each
    /// plug-in's layout independently and does not merge tabs that share an id. With three BIMCamel
    /// tools installed the user gets three tabs all called BIMCamel.
    ///
    /// So each plug-in carries an identical copy of this helper and each installs it. It is
    /// idempotent: whichever runs last does the merge, and with a single tool installed it finds one
    /// tab and does nothing. Keep this file in step with the copies in the Dyncamelo and exporter
    /// repositories. It is duplicated rather than shared because a shared assembly would have to be
    /// installed and versioned by all three.
    ///
    /// Reflection is used because the ribbon object model is AdWindows and the per-year Navisworks
    /// NuGet reference packages do not include <c>AdWindows.dll</c>. The host has it loaded, so this
    /// reaches it through <see cref="AppDomain.CurrentDomain"/>. The shape touched — RibbonControl →
    /// Tabs → Panels, all observable collections — is stable across 2024–2027.
    ///
    /// Two ways of finding the ribbon: <c>ComponentManager.Ribbon</c> is the AutoCAD-era static and
    /// is often unpopulated in Navisworks, so a visual-tree walk over every live presentation source
    /// is the fallback.
    ///
    /// Runs on <c>Application.Idle</c>, so it executes on the UI thread after the ribbon exists, and
    /// keeps watching cheaply (guarded by a tab-count check) so a ribbon rebuild from a workspace or
    /// user-profile load is re-merged. Any failure unhooks and leaves the ribbon alone: the worst
    /// case is the several-tabs behaviour it set out to fix.
    /// </summary>
    internal static class RibbonTabMerger
    {
        private const string TabId = "ID_Tab_BIMCamel";
        private const string TabTitle = "BIMCamel";

        private static bool _installed;
        private static int _lastCount = -1;

        /// <summary>Starts watching for duplicate tabs. Safe to call repeatedly.</summary>
        public static void Install()
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            System.Windows.Forms.Application.Idle += OnIdle;
        }

        private static void OnIdle(object? sender, EventArgs e)
        {
            try
            {
                Merge();
            }
            catch (Exception)
            {
                // The reflection surface changed (a future Navisworks). Stop trying for this
                // session rather than throwing on every idle tick for the rest of the day.
                System.Windows.Forms.Application.Idle -= OnIdle;
            }
        }

        private static void Merge()
        {
            var tabs = FindTabsViaComponentManager() ?? FindTabsViaPresentationSources();

            if (tabs == null)
            {
                return;
            }

            if (tabs.Count == _lastCount)
            {
                return;   // nothing new composed since the last look
            }

            _lastCount = tabs.Count;

            object? primary = null;
            var duplicates = new List<object>();

            foreach (var tab in tabs)
            {
                if (tab == null || !IsBimCamelTab(tab))
                {
                    continue;
                }

                if (primary == null)
                {
                    primary = tab;
                }
                else
                {
                    duplicates.Add(tab);
                }
            }

            if (primary == null || duplicates.Count == 0)
            {
                return;
            }

            if (!(GetProp<object>(primary, "Panels") is IList primaryPanels))
            {
                return;
            }

            foreach (var duplicate in duplicates)
            {
                if (GetProp<object>(duplicate, "Panels") is IList panels)
                {
                    foreach (var panel in panels.Cast<object>().ToList())
                    {
                        panels.Remove(panel);
                        primaryPanels.Add(panel);
                    }
                }

                tabs.Remove(duplicate);
            }

            _lastCount = tabs.Count;
        }

        /// <summary>
        /// A BIMCamel tab, by exact id, by a host-namespaced id that still ends with the shared
        /// id, or — last resort — by its visible title.
        /// </summary>
        private static bool IsBimCamelTab(object tab)
        {
            var id = GetProp<string>(tab, "Id");

            if (id != null
                && (id.Equals(TabId, StringComparison.Ordinal)
                    || id.EndsWith(TabId, StringComparison.Ordinal)))
            {
                return true;
            }

            var title = GetProp<string>(tab, "Title");

            return title != null && title.Equals(TabTitle, StringComparison.Ordinal);
        }

        /// <summary>The AutoCAD-style static registry. Often unpopulated in Navisworks.</summary>
        private static IList? FindTabsViaComponentManager()
        {
            var adWindows = AdWindowsAssembly();
            var componentManager = adWindows?.GetType("Autodesk.Windows.ComponentManager");

            var ribbon = componentManager
                ?.GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            return ribbon == null ? null : GetProp<object>(ribbon, "Tabs") as IList;
        }

        /// <summary>
        /// Walks every live WPF visual tree on this thread looking for the RibbonControl — the
        /// Navisworks ribbon is AdWindows WPF hosted in the main frame.
        /// </summary>
        private static IList? FindTabsViaPresentationSources()
        {
            var ribbonType = AdWindowsAssembly()?.GetType("Autodesk.Windows.RibbonControl");

            if (ribbonType == null)
            {
                return null;
            }

            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (!(source.RootVisual is DependencyObject root))
                {
                    continue;
                }

                var ribbon = FindDescendantOfType(root, ribbonType);

                if (ribbon != null)
                {
                    return GetProp<object>(ribbon, "Tabs") as IList;
                }
            }

            return null;
        }

        /// <summary>Breadth-first, budget-capped visual-tree search. The ribbon sits near the top.</summary>
        private static object? FindDescendantOfType(DependencyObject root, Type type)
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            var visited = 0;

            while (queue.Count > 0 && visited < 5000)
            {
                var node = queue.Dequeue();
                visited++;

                if (type.IsInstanceOfType(node))
                {
                    return node;
                }

                var children = VisualTreeHelper.GetChildrenCount(node);

                for (var i = 0; i < children; i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
                }
            }

            return null;
        }

        private static Assembly? AdWindowsAssembly() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "AdWindows",
                                                   StringComparison.OrdinalIgnoreCase));

        private static T? GetProp<T>(object obj, string name)
            where T : class =>
            obj.GetType().GetProperty(name)?.GetValue(obj) as T;
    }

    /// <summary>
    /// Loaded when Navisworks starts, which is what lets the tab merge happen before the user
    /// clicks anything.
    ///
    /// An <c>EventWatcherPlugin</c> rather than the command handler because event watchers are not
    /// lazy: a command handler does not load until its button is pressed, and by then the user has
    /// already seen three tabs. This is also the earliest safe place to start the merge — the
    /// ribbon composes during startup, and the merger waits on Idle for it.
    /// </summary>
    [Autodesk.Navisworks.Api.Plugins.Plugin("CamelWorks.Startup", "CamelWorks",
        DisplayName = "CamelWorks startup")]
    public class CamelWorksStartup : Autodesk.Navisworks.Api.Plugins.EventWatcherPlugin
    {
        /// <inheritdoc />
        public override void OnLoaded() => RibbonTabMerger.Install();

        /// <inheritdoc />
        public override void OnUnloading()
        {
        }
    }
}
