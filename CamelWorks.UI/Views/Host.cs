using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.Navisworks.Api;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// One document, with everything a screen needs hanging off it.
    ///
    /// Screens ask <see cref="Host.Current"/> for this and use what they need. Building it is
    /// cheap — the expensive part, the element index inside <see cref="NavDocument"/>, is built on
    /// first use and kept — so a screen never has to decide whether to cache anything itself.
    /// </summary>
    public sealed class Session
    {
        internal Session(Document document, NavDocument model, ProjectStore store, ProjectProfile profile,
                         IClashSource clash, string? clashProblem)
        {
            Document = document;
            Model = model;
            Store = store;
            Profile = profile;
            Clash = clash;
            ClashProblem = clashProblem;
            View = new NavViewSession(model);
            Viewpoints = new NavViewpointStore(model);
            Search = new NavSearch(model);
        }

        /// <summary>The host document.</summary>
        public Document Document { get; }

        /// <summary>The document behind the seam.</summary>
        public NavDocument Model { get; }

        /// <summary>Runs compiled set expressions and publishes native sets.</summary>
        public NavSearch Search { get; }

        /// <summary>Colour, transparency, visibility, camera and the section box.</summary>
        public IViewSession View { get; }

        /// <summary>Saved viewpoints.</summary>
        public IViewpointStore Viewpoints { get; }

        /// <summary>
        /// The clash engine, or a stand-in that reports itself unavailable.
        ///
        /// Never null. Navisworks Simulate has no clash engine at all, and a screen that has to
        /// null-check before every use would eventually forget once.
        /// </summary>
        public IClashSource Clash { get; }

        /// <summary>Why clash is unavailable, or null when it is available.</summary>
        public string? ClashProblem { get; }

        /// <summary>The project file.</summary>
        public ProjectStore Store { get; }

        /// <summary>What CamelWorks derived about this project, with any overrides applied.</summary>
        public ProjectProfile Profile { get; }

        /// <summary>
        /// How many metres one model unit is.
        ///
        /// Every threshold in this product is stated in metres — a 10 mm clash tolerance, a 5 m
        /// grouping distance — and the host reports geometry in whatever unit the document was
        /// authored in. Multiplying at the boundary is the only place this can be got right once;
        /// leaving it to each rule is how a millimetre model ends up grouping everything within
        /// five millimetres and reporting one group per clash.
        /// </summary>
        public double MetresPerUnit => NavUnits.MetresPerUnit(Document.Units);

        /// <summary>The saved document path, or null when it has never been saved.</summary>
        public string? SavedPath =>
            string.IsNullOrWhiteSpace(Document.CurrentFileName) ? null : Document.CurrentFileName;

        /// <summary>
        /// Whether this document is a published one - an NWD.
        ///
        /// The single most useful fact about an open document that nothing used to say. An NWD is
        /// a snapshot: its clash results are read-only, its properties cannot be saved back into
        /// it, and every write in this product lands somewhere the file will never carry. That is
        /// not a fault - publishing is what an NWD is for - but a coordinator who spends an
        /// afternoon marking one up and only finds out at the end has lost the afternoon.
        ///
        /// Read off the file name rather than asked of the host, because the host has no property
        /// that says it and the extension is the thing that decides it.
        /// </summary>
        public bool IsPublished
        {
            get
            {
                try
                {
                    var path = SavedPath;

                    return path != null && System.IO.Path.GetExtension(path)
                        .Equals(".nwd", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    // An unsaved or unnameable document is not a published one.
                    return false;
                }
            }
        }

        /// <summary>Now, as UTC ticks.</summary>
        public long NowTicks => DateTime.UtcNow.Ticks;

        /// <summary>
        /// Who we are to the project file.
        ///
        /// The signed-in user, because the lease is advisory and its whole job is to put a name on
        /// a screen: "Ahmed has this open" is actionable in a way that a machine id is not.
        /// </summary>
        public string Owner => Environment.UserName;

        /// <summary>Who holds the right to save project settings, without taking it.</summary>
        public LeaseResult LookAtLease() => Store.LookAtLease(Owner, NowTicks);

        /// <summary>Take or renew that right.</summary>
        /// <param name="force">Take it from a live holder — what the "Take it over" button does.</param>
        public LeaseResult Claim(bool force = false) => Store.TakeLease(Owner, NowTicks, force);

        /// <summary>Record something in the activity log and save the project file.</summary>
        /// <param name="kind">One of <see cref="ActivityKind"/>.</param>
        /// <param name="summary">One line, in the past tense.</param>
        /// <param name="detail">Anything worth keeping that does not fit on the line.</param>
        public void Record(string kind, string summary, string? detail = null)
        {
            // Claim on the way to a write, not on the way to a screen. This is the moment the
            // project file is about to change, which is the only moment a lease is about.
            Claim();
            Store.Record(kind, NowTicks, summary, detail);
        }


        /// <inheritdoc />
        public override string ToString() => Profile.ProjectName + " — " + Model.Models.Count + " models";
    }

    /// <summary>
    /// The way in to Navisworks, and the only place in the UI that talks to it directly.
    ///
    /// <b>Nothing here throws when there is no model.</b> <see cref="Current"/> is null, screens
    /// say so, and no screen is left deciding what an absent document means.
    /// </summary>
    public static class Host
    {
        private static Session? _session;
        private static string? _key;
        private static bool _resolverRegistered;

        private static readonly List<Action> _onChange = new List<Action>();

        /// <summary>What to say when there is no model open.</summary>
        public const string NoModel = "Open a model first. Every screen in CamelWorks works on a raw "
                                      + "model with nothing configured, but it does need one.";

        /// <summary>
        /// The current session, or null when nothing is open.
        ///
        /// Rebuilt when the document changes underneath — a different file, or models added or
        /// removed. Comparing a cheap key each time beats subscribing to host events, which fire on
        /// the host's thread at moments a dock pane is not necessarily ready for.
        /// </summary>
        public static Session? Current
        {
            get
            {
                Ensure();

                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;

                if (document == null || document.IsClear)
                {
                    if (_key != null) Changed();

                    _session = null;
                    _key = null;
                    return null;
                }

                var key = document.CurrentFileName + "|" + document.Models.Count + "|" + document.FileName;

                if (_session != null && string.Equals(_key, key, StringComparison.Ordinal)) return _session;

                // A different document, or models added or removed. Everything a screen cached
                // about the last one describes a model that is no longer open.
                if (_key != null) Changed();

                var model = new NavDocument(document);
                var saved = SavedPathOf(document);

                var store = ProjectStore.Open(PhysicalFileSystem.Instance, saved, UserDirectory);

                // The host's units seed the project's, once. After that the project keeps its own:
                // switching the display to millimetres to read a dimension must not reissue every
                // report in different units from last week's.
                var profile = ProjectProfile.Derive(model.Models, saved, Environment.UserName,
                                                    document.Units.ToString());
                profile.ApplyOverrides(store.Section(ProjectStore.ProfileSection));

                var clash = LoadClash(model, out var problem);

                _session = new Session(document, model, store, profile, clash, problem);
                _key = key;
                return _session;
            }
        }

        /// <summary>Drop the cached session, so the next access rebuilds it.</summary>
        public static void Forget()
        {
            if (_key != null) Changed();

            _session = null;
            _key = null;
        }

        /// <summary>
        /// Run this whenever the open document changes.
        ///
        /// <b>What this is for.</b> Several screens keep what they read in a static, on purpose:
        /// re-reading eighteen thousand clash results because somebody glanced at another tab
        /// would make the glance cost more than the work. But a static outlives the document it
        /// describes, and nothing used to tell it so. Open a second federation and the clash board
        /// still showed the first one's results, the clearance check still showed its findings,
        /// and the pickers still offered its sets - all of it wrong, and none of it saying so.
        ///
        /// Registering here is what makes a cache safe to keep. The call goes in a static
        /// constructor, so a screen nobody has opened has nothing to forget and never registers.
        /// </summary>
        /// <param name="forget">Throw away what was cached about the last document.</param>
        public static void WhenDocumentChanges(Action forget)
        {
            if (forget == null) return;

            lock (_onChange)
            {
                if (!_onChange.Contains(forget)) _onChange.Add(forget);
            }
        }

        /// <summary>Tell every cache that what it holds is about a document nobody has open.</summary>
        private static void Changed()
        {
            List<Action> forgetters;

            lock (_onChange) forgetters = new List<Action>(_onChange);

            foreach (var forget in forgetters)
            {
                try
                {
                    forget();
                }
                catch (Exception)
                {
                    // One screen's cache refusing to clear must not stop the others clearing, and
                    // must not take down the document switch that triggered it.
                }
            }
        }

        /// <summary>Where CamelWorks keeps things for a document that has never been saved.</summary>
        public static string UserDirectory =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamelWorks");

        /// <summary>
        /// Make side-by-side DLLs loadable.
        ///
        /// The CLR probes the application's folder, which for a plug-in is the Navisworks
        /// installation — not the folder the plug-in was installed into. Without this,
        /// CamelWorks.Core.dll sitting right beside CamelWorks.UI.dll is invisible.
        /// </summary>
        public static void Ensure()
        {
            if (_resolverRegistered) return;
            _resolverRegistered = true;

            var folder = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(folder)) return;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var file = System.IO.Path.Combine(folder!, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
        }

        private static string? SavedPathOf(Document document) =>
            string.IsNullOrWhiteSpace(document.CurrentFileName) ? null : document.CurrentFileName;

        /// <summary>
        /// Load the clash adapter without taking a hard dependency on it.
        ///
        /// <c>Autodesk.Navisworks.Clash.dll</c> ships with Manage and not with Simulate, so
        /// CamelWorks.Nav.Clash cannot be referenced from here: a type from a missing assembly is
        /// fine until the JIT touches the method that uses it, at which point it is a
        /// FileNotFoundException naming a DLL the user has never heard of, in the middle of
        /// whatever they were doing. Loading it by name means a Simulate user simply gets a screen
        /// that says the clash engine is not in their edition.
        /// </summary>
        private static IClashSource LoadClash(NavDocument model, out string? problem)
        {
            problem = null;

            try
            {
                var assembly = Assembly.Load("CamelWorks.Nav.Clash");
                var type = assembly.GetType("CamelWorks.Nav.Clash.NavClashSource", throwOnError: false);

                if (type == null)
                {
                    problem = "The CamelWorks clash adapter is installed but does not contain the expected "
                              + "type. This build is inconsistent; reinstall it.";
                    return new NoClash();
                }

                if (Activator.CreateInstance(type, model) is IClashSource source && source.IsAvailable)
                    return source;

                problem = "This document has no clash tests, or this Navisworks edition has no clash engine.";
                return new NoClash();
            }
            catch (Exception e) when (e is FileNotFoundException || e is FileLoadException
                                      || e is BadImageFormatException || e is TypeLoadException
                                      || e is TargetInvocationException || e is MissingMethodException)
            {
                problem = "Clash Detective is not available in this Navisworks edition. Clash tests, the "
                          + "coordination board and BCF export from clashes all need Navisworks Manage; "
                          + "everything else in CamelWorks works on Simulate.";

                return new NoClash();
            }
        }

        /// <summary>Stands in for the clash engine when there is not one. Answers, rather than throwing.</summary>
        private sealed class NoClash : IClashSource
        {
            public bool IsAvailable => false;

            public System.Collections.Generic.IReadOnlyList<ClashTestInfo> Tests() =>
                Array.Empty<ClashTestInfo>();

            public System.Collections.Generic.IReadOnlyList<ClashResultInfo> Results(string testId) =>
                Array.Empty<ClashResultInfo>();

            public bool CanEdit => false;

            public ClashTestInfo? Create(ClashTestSpec spec) => null;

            public bool Run(string testId) => false;

            public string? LastProblem =>
                "There is no clash engine in this Navisworks edition, so tests cannot be made or run.";

            public bool SetStatus(string testId,
                System.Collections.Generic.IReadOnlyList<string> resultIds, string status) => false;

            public bool Assign(string testId,
                System.Collections.Generic.IReadOnlyList<string> resultIds, string assignee) => false;

            public bool Comment(string testId,
                System.Collections.Generic.IReadOnlyList<string> resultIds, string body, string author) => false;

            public bool Regroup(string testId,
                System.Collections.Generic.IReadOnlyList<string> resultIds, string groupName) => false;

            public bool RenameResults(string testId, string prefix) => false;

            public bool RenameTest(string testId, string name) => false;

            public bool DeleteTests(System.Collections.Generic.IReadOnlyList<string> testIds) => false;

            public bool MoveTests(System.Collections.Generic.IReadOnlyList<string> testIds, string? folder) => false;

            public bool RenamePicked(string testId,
                System.Collections.Generic.IReadOnlyList<string> resultIds, string prefix) => false;

            public bool CompactTests(System.Collections.Generic.IReadOnlyList<string> testIds) => false;

            public ClashTestSides? Sides(string testId) => null;

            public bool ApplyResultView(string testId, string resultId) => false;

            public byte[]? ResultSnapshot(string testId, string resultId, int width, int height) => null;
        }
    }
}
