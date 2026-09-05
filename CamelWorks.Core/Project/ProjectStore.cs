using System;
using System.Globalization;
using System.IO;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// The project file: where it is, how the load went, and the sections that live in it.
    ///
    /// <b>Opening never fails.</b> A missing file is a first run, a damaged one falls back to its
    /// backup, a read-only folder degrades to memory, and a file from a newer build opens read-only
    /// and says so. Every one of those states still yields a usable store, because the alternative
    /// is a product that refuses to start over a settings file the user has never seen and did not
    /// ask for.
    ///
    /// Nothing here is created on open. The file appears the first time something is saved, which
    /// is what keeps a plain "open a model and press a button" session from littering the network
    /// drive with sidecars nobody wanted.
    /// </summary>
    public sealed class ProjectStore
    {
        /// <summary>The highest format version this build understands.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The folder the sidecar lives in, beside the document.</summary>
        public const string SidecarFolder = ".camelworks";

        /// <summary>The sidecar's extension.</summary>
        public const string Extension = ".cwproj";

        /// <summary>The file name used when the document has never been saved.</summary>
        public const string UnsavedName = "unsaved" + Extension;

        /// <summary>
        /// The advisory lease's extension: the same name as the project file, next to it.
        ///
        /// Beside rather than inside, because a lease has to be readable and writable by somebody
        /// who is not allowed to write the project file, and because deleting a stuck one must not
        /// mean deleting the settings.
        /// </summary>
        public const string LeaseExtension = ".cwlock";

        /// <summary>Section holding profile overrides.</summary>
        public const string ProfileSection = "profile";

        /// <summary>Section holding the appearance layer stack.</summary>
        public const string LayersSection = "layers";

        /// <summary>Section holding saved clash rules.</summary>
        public const string ClashSection = "clash";

        /// <summary>Section holding saved set definitions.</summary>
        public const string SetsSection = "sets";

        /// <summary>Section holding batch jobs and graphs.</summary>
        public const string JobsSection = "jobs";

        /// <summary>Section holding the activity log.</summary>
        public const string ActivitySection = "activity";

        /// <summary>What the last health check saw: the model manifest it compares against.</summary>
        public const string ModelsSection = "models";

        /// <summary>What each tool was last told, so it does not ask again.</summary>
        public const string ToolsSection = "tools";

        /// <summary>Saved model-data reports, and what else the data tools keep for the job.</summary>
        public const string DataSection = "data";

        /// <summary>The issue register: everything raised on this project, however it was found.</summary>
        public const string IssuesSection = "issues";

        private readonly IFileSystem _fs;
        private readonly VersionedDocument _document;

        private ProjectStore(IFileSystem fs, VersionedDocument document, string? path, string where,
                             bool usedBackup)
        {
            _fs = fs;
            _document = document;
            Path = path;
            Where = where;
            UsedBackup = usedBackup;
            Substrate = StorageSubstrate.Classify(path);
            Activity = ActivityLog.FromJson(document.Root[ActivitySection]);
        }

        /// <summary>Where the file is, or null when this store only exists in memory.</summary>
        public string? Path { get; }

        /// <summary>Where it is, in a sentence the pane can show.</summary>
        public string Where { get; }

        /// <summary>True when nothing will be persisted — the store still works for this session.</summary>
        public bool IsMemoryOnly => Path == null;

        /// <summary>True when the file came from a newer build and must not be written over.</summary>
        public bool IsReadOnly => _document.IsReadOnly;

        /// <summary>Why it is read-only, or null.</summary>
        public string? ReadOnlyReason => _document.ReadOnlyReason;

        /// <summary>How the load ended.</summary>
        public LoadOutcome Outcome => _document.Outcome;

        /// <summary>What the file is stored on, and therefore whether a lease means anything.</summary>
        public SubstrateKind Substrate { get; }

        /// <summary>True when the main file was unreadable and its backup was used instead.</summary>
        public bool UsedBackup { get; }

        /// <summary>What has happened on this project.</summary>
        public ActivityLog Activity { get; }

        /// <summary>Why the last <see cref="Save"/> did not write, or null when it did.</summary>
        public string? LastSaveProblem { get; private set; }

        /// <summary>Where the lease file is, or null when nothing is written to disk.</summary>
        public string? LeasePath
        {
            get
            {
                var path = Path;

                return path == null || !path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : path.Substring(0, path.Length - Extension.Length) + LeaseExtension;
            }
        }

        /// <summary>
        /// How the last attempt on the lease went, or null when nothing has asked yet.
        ///
        /// Null is the ordinary state for most of a session. Nothing takes the lease on open: a
        /// person who loads a model to look at it should not be writing lock files into somebody
        /// else's project folder.
        /// </summary>
        public LeaseResult? Lease { get; private set; }

        /// <summary>
        /// Whether settings may be saved.
        ///
        /// Only <see cref="LeaseOutcome.HeldByAnother"/> stops a save. A substrate that cannot
        /// carry a lease at all is not somebody else holding one, and refusing there would mean
        /// CamelWorks saved nothing on OneDrive - which is where a great many project folders
        /// live. There the answer is the warning on <see cref="ConcurrencyNote"/>, not a refusal.
        /// </summary>
        public bool MaySaveSettings => Lease == null || Lease.Outcome != LeaseOutcome.HeldByAnother;

        /// <summary>A named section, created on first use.</summary>
        /// <param name="name">One of the section constants on this class.</param>
        public JsonValue Section(string name) => _document.Section(name);

        /// <summary>
        /// Open the store for a document.
        /// </summary>
        /// <param name="fs">File access.</param>
        /// <param name="documentPath">The saved document's full path, or null when it has never been saved.</param>
        /// <param name="userDirectory">A per-user folder to fall back to. Null means memory only.</param>
        public static ProjectStore Open(IFileSystem fs, string? documentPath, string? userDirectory)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));

            var (path, where) = Locate(documentPath, userDirectory);

            if (path == null)
                return new ProjectStore(fs, VersionedDocument.Create(SchemaVersion), null, where, false);

            string? text = null;
            var usedBackup = false;

            try
            {
                text = AtomicFile.ReadWithFallback(fs, path, out usedBackup);
            }
            catch (IOException)
            {
                // An unreachable share or a locked file. Reported through Where rather than thrown:
                // the session continues, unsaved, which is strictly better than not starting.
                return new ProjectStore(fs, VersionedDocument.Create(SchemaVersion), null,
                    "could not be read (" + path + ") — this session will not be saved", false);
            }
            catch (UnauthorizedAccessException)
            {
                return new ProjectStore(fs, VersionedDocument.Create(SchemaVersion), null,
                    "no permission to read (" + path + ") — this session will not be saved", false);
            }

            return new ProjectStore(fs, VersionedDocument.Load(text, SchemaVersion), path, where, usedBackup);
        }

        /// <summary>
        /// Where the sidecar for a document belongs.
        /// </summary>
        /// <param name="documentPath">The saved document's full path, or null.</param>
        /// <param name="userDirectory">A per-user folder to fall back to, or null.</param>
        public static (string? Path, string Where) Locate(string? documentPath, string? userDirectory)
        {
            if (!string.IsNullOrWhiteSpace(documentPath))
            {
                var directory = SafeDirectoryName(documentPath!);
                var stem = SafeStem(documentPath!);

                if (directory != null && stem != null)
                {
                    var folder = PathText.Join(directory, SidecarFolder);
                    return (PathText.Join(folder, stem + Extension), "beside the document, in " + SidecarFolder);
                }
            }

            if (!string.IsNullOrWhiteSpace(userDirectory))
                return (PathText.Join(userDirectory!, UnsavedName),
                        "in your own CamelWorks folder, because this document has never been saved");

            return (null, "in memory only — nothing will be kept when Navisworks closes");
        }

        /// <summary>
        /// Write the file, creating its folder if needed.
        ///
        /// Returns false rather than throwing, and leaves the reason on
        /// <see cref="LastSaveProblem"/>: a failed save is something a pane reports on a line, not
        /// something every caller wraps in a try.
        /// </summary>
        public bool Save()
        {
            LastSaveProblem = null;

            if (IsMemoryOnly)
            {
                LastSaveProblem = "there is nowhere to save to — " + Where;
                return false;
            }

            if (IsReadOnly)
            {
                LastSaveProblem = ReadOnlyReason;
                return false;
            }

            if (!MaySaveSettings)
            {
                LastSaveProblem = Refusal();
                return false;
            }

            _document.Root.Set(ActivitySection, Activity.ToJson());

            try
            {
                var folder = SafeDirectoryName(Path!);
                if (folder != null) _fs.CreateDirectory(folder);

                AtomicFile.Write(_fs, Path!, _document.Save());
                return true;
            }
            catch (IOException e)
            {
                LastSaveProblem = "could not write " + Path + ": " + e.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                LastSaveProblem = "no permission to write " + Path;
                return false;
            }
        }

        /// <summary>Record something that happened and save. Returns whether the save landed.</summary>
        /// <param name="kind">One of <see cref="ActivityKind"/>.</param>
        /// <param name="whenTicks">When, as UTC ticks.</param>
        /// <param name="summary">One line, in the past tense.</param>
        /// <param name="detail">Anything worth keeping that does not fit on the line.</param>
        public bool Record(string kind, long whenTicks, string summary, string? detail = null)
        {
            Activity.Record(new Activity(kind, whenTicks, summary, detail));
            return Save();
        }

        /// <summary>
        /// Take, renew, or find out who holds the lease.
        ///
        /// Called before an edit that will be saved, not on open. Returns a result rather than a
        /// bool because the three answers a user can act on are different: you have it, somebody
        /// else has it until roughly a time, or this folder cannot carry a lease at all.
        ///
        /// Nothing here throws. A lease is advisory: a folder that will not give up its lease file
        /// is a reason to carry on unlocked, not a reason to stop somebody working.
        /// </summary>
        /// <param name="owner">Our stable writer id, usually the signed-in user.</param>
        /// <param name="nowTicks">Current UTC ticks, supplied so this is testable.</param>
        /// <param name="force">Take it even from a live holder — what the "Take it over" button does.</param>
        public LeaseResult TakeLease(string owner, long nowTicks, bool force = false) =>
            Evaluate(owner, nowTicks, claim: true, force: force);

        /// <summary>
        /// Find out who holds the lease without taking it.
        ///
        /// What a screen does when it opens. Claiming on open would mean that reading the clash
        /// board for two minutes locked a colleague out of project settings for ten, which is the
        /// failure mode that makes people delete lock files by hand and stop trusting them.
        /// </summary>
        /// <param name="owner">Our stable writer id.</param>
        /// <param name="nowTicks">Current UTC ticks.</param>
        public LeaseResult LookAtLease(string owner, long nowTicks) =>
            Evaluate(owner, nowTicks, claim: false, force: false);

        private LeaseResult Evaluate(string owner, long nowTicks, bool claim, bool force)
        {
            if (string.IsNullOrWhiteSpace(owner)) owner = "someone";

            var file = LeasePath;

            if (file == null)
            {
                // Nothing is written to disk, so nobody else can be holding it.
                Lease = new LeaseResult(LeaseOutcome.Acquired, owner, 0, null);
                return Lease;
            }

            string? held = null;

            if (!force)
            {
                try
                {
                    held = _fs.ReadAllText(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            var result = ProjectLease.TryAcquire(held, owner, nowTicks, Substrate);

            if (claim && result.MayWrite) Put(file, ProjectLease.Write(owner, result.ExpiresTicks));

            Lease = result;
            return result;
        }

        /// <summary>
        /// Give the lease up, so the next person does not wait out its deadline.
        ///
        /// Safe to call when we never had it: <see cref="ProjectLease.Release"/> leaves somebody
        /// else's lease alone, so a stale close cannot evict whoever took over.
        /// </summary>
        /// <param name="owner">The same id the lease was taken under.</param>
        public void ReleaseLease(string owner)
        {
            var file = LeasePath;

            if (file != null && Lease != null && Lease.MayWrite)
            {
                try
                {
                    if (ProjectLease.Release(_fs.ReadAllText(file), owner) == null) _fs.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            Lease = null;
        }

        /// <summary>
        /// Why a save was refused, said in terms of what is actually different.
        ///
        /// "Somebody else is editing settings" is not something a person can act on. "They changed
        /// the clash matrix and the party list; your rule edits are untouched" is — it says
        /// whether to wait, redo the work, or carry on.
        /// </summary>
        private string Refusal()
        {
            var reason = Lease?.Reason ?? "another user is editing project settings.";

            try
            {
                var theirs = _fs.ReadAllText(Path!);

                if (theirs != null && JsonReader.TryParse(theirs, out var json))
                {
                    var changed = SectionDiff.ChangedSections(_document.Root, json);

                    if (changed.Count > 0)
                        return reason + " " + SectionDiff.Describe(changed, Lease?.Holder);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return reason;
        }

        /// <summary>Write the lease file, and say nothing when the folder will not have it.</summary>
        private void Put(string file, string text)
        {
            try
            {
                var folder = SafeDirectoryName(file);
                if (folder != null) _fs.CreateDirectory(folder);

                _fs.WriteAllText(file, text);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// What to say about concurrency, given where the file landed. Empty when there is nothing
        /// worth saying.
        /// </summary>
        public string ConcurrencyNote => Substrate switch
        {
            SubstrateKind.SyncRoot =>
                "This project file is in a synced folder. CamelWorks will not take a lock here, "
                + "because a sync client does not merge — it keeps both copies and renames one. "
                + "Your own edits are safe; two people editing at once will produce a conflicted copy.",
            SubstrateKind.NetworkShare =>
                "This project file is on a network share. Locking works; only speed will differ.",
            _ => string.Empty,
        };

        private static string? SafeDirectoryName(string path) => PathText.Directory(path);

        private static string? SafeStem(string path) => PathText.Stem(path);

        /// <inheritdoc />
        public override string ToString() =>
            (Path ?? "(memory)") + " — " + Outcome.ToString().ToLowerInvariant()
            + ", schema " + _document.SchemaVersion.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Path arithmetic that behaves the same wherever it runs.
    ///
    /// <c>System.IO.Path</c> does not: on Linux a backslash is an ordinary character, so
    /// <c>GetDirectoryName(@"C:\Jobs\Site.nwf")</c> returns nothing at all. CamelWorks.Core is
    /// netstandard2.0 precisely so it can be tested on a Linux CI job, which means every Windows
    /// path in a test would take a different route through the code than the same path takes in
    /// the product — and the CI would go green on logic that had never been exercised.
    ///
    /// So this treats both separators as separators, everywhere.
    /// </summary>
    internal static class PathText
    {
        private static readonly char[] Separators = { '/', '\\' };

        /// <summary>The folder part, or null when there is none.</summary>
        internal static string? Directory(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var at = path!.LastIndexOfAny(Separators);

            if (at < 0) return null;
            return at == 0 ? path.Substring(0, 1) : path.Substring(0, at);
        }

        /// <summary>The file name without its folder or extension, or null.</summary>
        internal static string? Stem(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var name = path!;

            var at = name.LastIndexOfAny(Separators);
            if (at >= 0) name = name.Substring(at + 1);

            var dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);

            return name.Length == 0 ? null : name;
        }

        /// <summary>Join a folder and a name, keeping whichever separator the folder already uses.</summary>
        internal static string Join(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder)) return name;

            var last = folder[folder.Length - 1];
            if (last == '/' || last == '\\') return folder + name;

            // A Windows product, so a path with no separator in it yet gets a backslash.
            var separator = folder.IndexOf('/') >= 0 && folder.IndexOf('\\') < 0 ? '/' : '\\';
            return folder + separator + name;
        }
    }
}
