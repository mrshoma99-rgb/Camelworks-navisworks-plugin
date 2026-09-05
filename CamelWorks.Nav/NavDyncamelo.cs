using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CamelWorks.Nav
{
    /// <summary>
    /// What one node did during a graph run.
    /// </summary>
    public sealed class GraphNodeReport
    {
        /// <summary>Name it.</summary>
        /// <param name="name">The node's name, as the editor shows it.</param>
        /// <param name="state">Idle, Executed, Warning or Error, in Dyncamelo's words.</param>
        /// <param name="message">What it had to say, or empty.</param>
        public GraphNodeReport(string name, string state, string message)
        {
            Name = name;
            State = state;
            Message = message;
        }

        /// <summary>The node's name.</summary>
        public string Name { get; }

        /// <summary>Idle, Executed, Warning or Error.</summary>
        public string State { get; }

        /// <summary>The diagnostics it produced, or empty.</summary>
        public string Message { get; }

        /// <summary>Whether the node ended in the Error state.</summary>
        public bool Failed => string.Equals(State, "Error", StringComparison.Ordinal);

        /// <summary>Whether the node ended in the Warning state.</summary>
        public bool Warned => string.Equals(State, "Warning", StringComparison.Ordinal);
    }

    /// <summary>
    /// What a whole graph run came to.
    /// </summary>
    public sealed class GraphRun
    {
        /// <summary>A run that happened.</summary>
        /// <param name="graphName">The graph's own name, or its file name.</param>
        /// <param name="nodes">A report per node.</param>
        /// <param name="executed">How many nodes actually executed.</param>
        /// <param name="elapsed">How long it took.</param>
        /// <param name="cancelled">Whether the engine stopped early.</param>
        public GraphRun(string graphName, IReadOnlyList<GraphNodeReport> nodes, int executed,
                        TimeSpan elapsed, bool cancelled)
        {
            GraphName = graphName;
            Nodes = nodes;
            Executed = executed;
            Elapsed = elapsed;
            Cancelled = cancelled;
        }

        /// <summary>A run that never started, and why.</summary>
        /// <param name="problem">What stopped it, in a sentence.</param>
        public GraphRun(string problem)
        {
            GraphName = string.Empty;
            Nodes = Array.Empty<GraphNodeReport>();
            Problem = problem;
        }

        /// <summary>The graph's name.</summary>
        public string GraphName { get; }

        /// <summary>A report per node.</summary>
        public IReadOnlyList<GraphNodeReport> Nodes { get; }

        /// <summary>How many nodes executed.</summary>
        public int Executed { get; }

        /// <summary>How long the run took.</summary>
        public TimeSpan Elapsed { get; }

        /// <summary>Whether the engine stopped early.</summary>
        public bool Cancelled { get; }

        /// <summary>What stopped the run before it began, or null when it ran.</summary>
        public string? Problem { get; }

        /// <summary>How many nodes ended in the Error state.</summary>
        public int Errors => Nodes.Count(n => n.Failed);

        /// <summary>How many nodes ended in the Warning state.</summary>
        public int Warnings => Nodes.Count(n => n.Warned);

        /// <summary>The run, in one line.</summary>
        public string Describe() =>
            Problem != null
                ? Problem
                : (Cancelled ? "Stopped after " : "Ran ")
                  + Executed.ToString(CultureInfo.InvariantCulture) + " of "
                  + Nodes.Count.ToString(CultureInfo.InvariantCulture) + " nodes in "
                  + Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                  + (Errors > 0 ? ", " + Errors.ToString(CultureInfo.InvariantCulture) + " with errors" : string.Empty)
                  + (Warnings > 0 ? ", " + Warnings.ToString(CultureInfo.InvariantCulture) + " with warnings" : string.Empty)
                  + ".";
    }

    /// <summary>
    /// Runs a Dyncamelo graph without opening the Dyncamelo editor.
    ///
    /// A bridge rather than a reference. Dyncamelo ships as its own Navisworks bundle beside this
    /// one: its assemblies are not in CamelWorks' folder and are not on the probing path. A
    /// compile-time reference would make CamelWorks fail to load on a machine where Dyncamelo is
    /// absent, which is the same mistake as referencing the clash assembly on Simulate. So this
    /// loads by path and calls by reflection, as <see cref="NavClash"/> reaches Clash Detective, and
    /// every screen that uses it degrades to a message when the answer is null.
    ///
    /// It calls <c>DyncameloHost.Registry</c> and <c>DyncameloHost.CreateEvaluationContext()</c>
    /// rather than the engine directly, which are the two things the editor itself runs on. The
    /// registry is where the Navisworks node library and the third-party packs are loaded, and the
    /// context is where the host document service is published; rebuilding either here would make a
    /// graph behave differently depending on which window ran it.
    ///
    /// A Navisworks plug-in is compiled per release, so the bundle has a folder per year. The right
    /// one is chosen from the API version this process loaded (v21 is 2024, v22 2025, v23 2026, v24
    /// 2027) rather than from anything written down.
    /// </summary>
    public static class NavDyncamelo
    {
        private const string HostType = "Dyncamelo.App.DyncameloHost";
        private const string SerializerType = "Dyncamelo.Core.Serialization.GraphSerializer";
        private const string EngineType = "Dyncamelo.Core.Execution.GraphEngine";

        private static bool _looked;
        private static Assembly? _app;
        private static string? _why;

        /// <summary>The file extension a Dyncamelo graph is saved as.</summary>
        public const string Extension = ".dyc";

        /// <summary>Whether Dyncamelo is installed and its assemblies loaded.</summary>
        public static bool IsAvailable => Load() != null;

        /// <summary>
        /// Why Dyncamelo could not be reached, or null when it can. Worth showing: "not installed"
        /// and "installed, but the build for this Navisworks year is missing" are different
        /// problems with different fixes, and a screen that says only "unavailable" sends somebody
        /// to reinstall for no reason.
        /// </summary>
        public static string? Unavailable
        {
            get
            {
                Load();
                return _why;
            }
        }

        /// <summary>The folder the installed Dyncamelo bundle's assemblies live in, or null.</summary>
        public static string? Where { get; private set; }

        /// <summary>
        /// Run a graph file against the open document.
        ///
        /// Everything is caught: a graph is somebody else's file, its nodes are somebody else's
        /// code, and neither is worth taking Navisworks down for.
        /// </summary>
        /// <param name="path">The .dyc file to run.</param>
        public static GraphRun Run(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new GraphRun("There is no file at " + (path ?? "(nothing)") + ".");

            var app = Load();

            if (app == null) return new GraphRun(_why ?? "Dyncamelo is not available.");

            try
            {
                var host = app.GetType(HostType)
                    ?? throw new InvalidOperationException("Dyncamelo's host type has moved.");

                var registry = host.GetProperty("Registry", BindingFlags.Public | BindingFlags.Static)
                                   ?.GetValue(null)
                    ?? throw new InvalidOperationException("Dyncamelo's node registry is unreachable.");

                var context = host.GetMethod("CreateEvaluationContext",
                                             BindingFlags.Public | BindingFlags.Static)
                                  ?.Invoke(null, null);

                var core = registry.GetType().Assembly;

                var serializer = Activator.CreateInstance(
                    core.GetType(SerializerType)
                    ?? throw new InvalidOperationException("Dyncamelo's graph reader has moved."),
                    registry);

                var graph = serializer!.GetType()
                    .GetMethod("Deserialize", new[] { typeof(string) })!
                    .Invoke(serializer, new object[] { File.ReadAllText(path!) })
                    ?? throw new InvalidOperationException("The graph could not be read.");

                var engine = Activator.CreateInstance(
                    core.GetType(EngineType)
                    ?? throw new InvalidOperationException("Dyncamelo's engine has moved."))!;

                var run = engine.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .First(m => m.Name == "Run" && m.GetParameters().Length == 2)
                    .Invoke(engine, new[] { graph, context });

                return Read(graph, run, Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception error)
            {
                // TargetInvocationException hides the interesting half behind reflection's own
                // wrapper, which is never the sentence somebody needs.
                var real = error is TargetInvocationException wrapped && wrapped.InnerException != null
                    ? wrapped.InnerException
                    : error;

                return new GraphRun("The graph could not be run: " + real.Message);
            }
        }

        /// <summary>Turn Dyncamelo's own objects into this product's plain ones.</summary>
        private static GraphRun Read(object graph, object? run, string fallbackName)
        {
            var nodes = new List<GraphNodeReport>();

            if (graph.GetType().GetProperty("Nodes")?.GetValue(graph) is IEnumerable list)
            {
                foreach (var node in list)
                {
                    if (node == null) continue;

                    var type = node.GetType();

                    nodes.Add(new GraphNodeReport(
                        type.GetProperty("Name")?.GetValue(node) as string ?? "(unnamed)",
                        type.GetProperty("State")?.GetValue(node)?.ToString() ?? "Idle",
                        type.GetProperty("StateMessage")?.GetValue(node) as string ?? string.Empty));
                }
            }

            var name = graph.GetType().GetProperty("Name")?.GetValue(graph) as string;

            if (string.IsNullOrWhiteSpace(name)) name = fallbackName;

            var executed = 0;
            var elapsed = TimeSpan.Zero;
            var cancelled = false;

            if (run != null)
            {
                var type = run.GetType();

                if (type.GetProperty("ExecutedNodes")?.GetValue(run) is ICollection done)
                    executed = done.Count;

                if (type.GetProperty("Elapsed")?.GetValue(run) is TimeSpan took) elapsed = took;

                if (type.GetProperty("Cancelled")?.GetValue(run) is bool stopped) cancelled = stopped;
            }

            return new GraphRun(name!, nodes, executed, elapsed, cancelled);
        }

        /// <summary>
        /// Find and load Dyncamelo.App once per session.
        ///
        /// Already-loaded first: if the user has opened the editor this session, its assemblies are
        /// in the domain and loading them again from disk would give a second copy of every type,
        /// so the registry built by one would be rejected by the other.
        /// </summary>
        private static Assembly? Load()
        {
            if (_looked) return _app;

            _looked = true;

            _app = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Dyncamelo.App",
                                                   StringComparison.OrdinalIgnoreCase));

            if (_app != null)
            {
                Where = Path.GetDirectoryName(_app.Location);
                return _app;
            }

            var folder = BundleFolder();

            if (folder == null) return null;

            try
            {
                Where = folder;

                // Dyncamelo.App pulls in Core, Nodes, Navisworks, the UI and Nodify. They sit
                // beside it, but the CLR will not probe a folder outside the application base, so
                // an assembly it asks for later has to be handed to it by name.
                AppDomain.CurrentDomain.AssemblyResolve += Beside;

                _app = Assembly.LoadFrom(Path.Combine(folder, "Dyncamelo.App.dll"));
            }
            catch (Exception error)
            {
                _why = "Dyncamelo is installed but would not load: " + error.Message;
                _app = null;
            }

            return _app;
        }

        /// <summary>Resolve a Dyncamelo dependency out of the same bundle folder.</summary>
        private static Assembly? Beside(object? sender, ResolveEventArgs args)
        {
            if (Where == null) return null;

            var name = new AssemblyName(args.Name).Name;
            var file = Path.Combine(Where, name + ".dll");

            try
            {
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            }
            catch (Exception)
            {
                // A dependency that will not load is Dyncamelo's problem to report, not a reason
                // for this resolver to throw inside somebody else's assembly load.
                return null;
            }
        }

        /// <summary>
        /// The installed bundle's folder for the Navisworks this process is actually running.
        /// </summary>
        private static string? BundleFolder()
        {
            string bundle;

            try
            {
                bundle = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "ApplicationPlugins", "Dyncamelo.bundle");
            }
            catch (Exception)
            {
                _why = "This machine's application-data folder could not be read.";
                return null;
            }

            if (!Directory.Exists(bundle))
            {
                _why = "Dyncamelo is not installed. It ships with CamelWorks, so reinstalling "
                       + "CamelWorks puts it back — or install it on its own from bimcamel.com.";

                return null;
            }

            var year = Year();
            var folder = Path.Combine(bundle, year.ToString(CultureInfo.InvariantCulture));

            if (!File.Exists(Path.Combine(folder, "Dyncamelo.App.dll")))
            {
                _why = "Dyncamelo is installed, but not the build for Navisworks "
                       + year.ToString(CultureInfo.InvariantCulture) + ". A plug-in has to be "
                       + "compiled against the release it runs in, so the bundle carries a folder "
                       + "per year and this one has no Dyncamelo in it.";

                return null;
            }

            return folder;
        }

        /// <summary>
        /// The Navisworks year this process is, read off the API assembly it loaded.
        ///
        /// The major version is the API version, and Autodesk has moved it once per release:
        /// v21 is 2024, v22 2025, v23 2026, v24 2027. Read rather than compiled in, because a
        /// CamelWorks built for one year and dropped in another year's folder by hand would
        /// otherwise go looking for the wrong Dyncamelo and report a confusing reason.
        /// </summary>
        private static int Year()
        {
            try
            {
                var api = typeof(Autodesk.Navisworks.Api.Application).Assembly.GetName().Version;

                return api == null ? 2024 : 2003 + api.Major;
            }
            catch (Exception)
            {
                return 2024;
            }
        }
    }
}
