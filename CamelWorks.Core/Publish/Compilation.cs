using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Publish
{
    /// <summary>How a revision is written, which decides how two of them are compared.</summary>
    public enum RevisionKind
    {
        /// <summary>There is no revision in the name.</summary>
        None = 0,

        /// <summary>A number: Rev03, R2, v11.</summary>
        Number,

        /// <summary>A letter sequence: RevA, Rev-B, RevAA.</summary>
        Letter,
    }

    /// <summary>A revision read out of a file name.</summary>
    public readonly struct Revision : IEquatable<Revision>
    {
        private readonly string? _text;

        private Revision(RevisionKind kind, string? text, long order)
        {
            Kind = kind;
            _text = text;
            Order = order;
        }

        /// <summary>How it is written.</summary>
        public RevisionKind Kind { get; }

        /// <summary>Exactly as the file name had it, so nothing is renamed behind anybody's back.</summary>
        public string Text => _text ?? string.Empty;

        /// <summary>Where it sits in its own sequence. Only comparable within one kind.</summary>
        public long Order { get; }

        /// <summary>True when the name carried no revision.</summary>
        public bool IsNone => Kind == RevisionKind.None;

        /// <summary>No revision at all.</summary>
        public static Revision None { get; } = new Revision(RevisionKind.None, null, -1);

        /// <summary>A numbered revision.</summary>
        /// <param name="text">As written.</param>
        /// <param name="value">Its number.</param>
        public static Revision Numbered(string text, long value) =>
            new Revision(RevisionKind.Number, text, value);

        /// <summary>A lettered revision.</summary>
        /// <param name="text">As written, so nothing is renamed behind anybody's back.</param>
        /// <param name="letters">Just the letters, which is what decides the order.</param>
        public static Revision Lettered(string text, string? letters = null)
        {
            var order = 0L;

            foreach (var letter in (letters ?? text ?? string.Empty).ToUpperInvariant())
            {
                if (letter < 'A' || letter > 'Z') continue;

                // Base 26 with A as one, so B beats A and AA beats Z - which is how a drawing
                // register counts once it has been round the houses more than twenty-six times.
                order = order * 26 + (letter - 'A' + 1);
            }

            return new Revision(RevisionKind.Letter, text, order);
        }

        /// <summary>
        /// Whether this one supersedes another.
        ///
        /// Only within one way of writing them. "Rev03" and "RevC" are not two points on one
        /// sequence, and a tool that ranked them would drop somebody's model on a coin toss - so
        /// two files whose revisions are written differently are both kept and the fact is
        /// reported.
        /// </summary>
        /// <param name="other">The one to compare against.</param>
        public bool Beats(Revision other)
        {
            if (Kind != other.Kind) return false;
            if (Kind == RevisionKind.None) return false;

            return Order > other.Order;
        }

        /// <summary>Whether two revisions can be ranked against each other at all.</summary>
        /// <param name="other">The one to compare against.</param>
        public bool Comparable(Revision other) =>
            Kind == other.Kind && Kind != RevisionKind.None;

        /// <inheritdoc />
        public bool Equals(Revision other) =>
            Kind == other.Kind && Order == other.Order
            && string.Equals(Text, other.Text, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Revision r && Equals(r);

        /// <inheritdoc />
        public override int GetHashCode() =>
            ((int)Kind * 397) ^ Order.GetHashCode();

        /// <summary>Equality.</summary>
        public static bool operator ==(Revision a, Revision b) => a.Equals(b);

        /// <summary>Inequality.</summary>
        public static bool operator !=(Revision a, Revision b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => IsNone ? "(no revision)" : Text;
    }

    /// <summary>One file on its way into a compilation.</summary>
    public sealed class SourceFile
    {
        /// <summary>Name one.</summary>
        /// <param name="path">Where it is.</param>
        /// <param name="exists">Whether it is there.</param>
        /// <param name="bytes">How big it is, where that is known.</param>
        /// <param name="modifiedTicks">When it last changed, where that is known.</param>
        public SourceFile(string? path, bool exists = true, long bytes = 0, long modifiedTicks = 0)
        {
            Path = (path ?? string.Empty).Trim();
            Exists = exists;
            Bytes = bytes;
            ModifiedTicks = modifiedTicks;

            var name = Compilation.NameOf(Path);

            Extension = Compilation.ExtensionOf(name);

            var split = Compilation.Split(Compilation.WithoutExtension(name));

            Base = split.Base;
            Revision = split.Revision;
        }

        /// <summary>Where it is.</summary>
        public string Path { get; }

        /// <summary>Its name with the revision and the extension taken off - what identifies it.</summary>
        public string Base { get; }

        /// <summary>Which revision it is.</summary>
        public Revision Revision { get; }

        /// <summary>Its extension, lower case, with the dot.</summary>
        public string Extension { get; }

        /// <summary>Whether it is there.</summary>
        public bool Exists { get; }

        /// <summary>How big it is.</summary>
        public long Bytes { get; }

        /// <summary>When it last changed.</summary>
        public long ModifiedTicks { get; }

        /// <summary>True when the extension is one Navisworks can take in.</summary>
        public bool IsReadable => Compilation.Readable.Contains(Extension, StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override string ToString() =>
            Compilation.NameOf(Path) + (Revision.IsNone ? string.Empty : " (" + Revision + ")");
    }

    /// <summary>What a compilation produces.</summary>
    public enum CompileMode
    {
        /// <summary>One file holding all of them, which is the weekly federation.</summary>
        Combine = 0,

        /// <summary>One file each, which is the supplier drop turned into something reviewable.</summary>
        Individual,
    }

    /// <summary>What a compilation is, saved so next week is one press.</summary>
    public sealed class CompileRecipe
    {
        /// <summary>Start one.</summary>
        /// <param name="name">What to call it.</param>
        public CompileRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Weekly federation" : name!.Trim();
        }

        /// <summary>What to call it, and what a combined file is named.</summary>
        public string Name { get; set; }

        /// <summary>What it produces.</summary>
        public CompileMode Mode { get; set; }

        /// <summary>Where the files go.</summary>
        public string? Folder { get; set; }

        /// <summary>What it writes. One of <see cref="Compilation.Writable"/>.</summary>
        public string Format { get; set; } = Compilation.Nwd;

        /// <summary>
        /// Whether a newer revision of the same file pushes the older one out.
        ///
        /// On by default, and the single most valuable thing here: a folder of supplier drops holds
        /// three revisions of half of them, and a federation built from all three is wrong in a way
        /// that looks exactly like a federation built from one.
        /// </summary>
        public bool NewestOnly { get; set; } = true;

        /// <summary>Whether the date goes in the file name, so last week's is still there.</summary>
        public bool Stamped { get; set; } = true;

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public CompileRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("name", Name)
                .Set("mode", Mode.ToString())
                .Set("format", Format)
                .Set("newestOnly", NewestOnly)
                .Set("stamped", Stamped);

            if (Folder != null) json.Set("folder", Folder);

            return json;
        }

        /// <summary>Read one back. An unreadable one comes back as the ordinary weekly one.</summary>
        /// <param name="json">What was written.</param>
        public static CompileRecipe FromJson(JsonValue? json)
        {
            var recipe = new CompileRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            if (Enum.TryParse<CompileMode>(json["mode"].AsString() ?? "Combine", out var mode))
                recipe.Mode = mode;

            var format = (json["format"].AsString() ?? string.Empty).Trim();

            // Kept as written rather than corrected here: a recipe saved when the tool could
            // write a format it no longer can should say what it was asked for, and Plan is where
            // the fact that it cannot is reported.
            recipe.Format = format.Length == 0 ? Compilation.Nwd : format.ToLowerInvariant();

            recipe.Folder = json["folder"].AsString();
            recipe.NewestOnly = json["newestOnly"].AsBool(true);
            recipe.Stamped = json["stamped"].AsBool(true);

            return recipe;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + (Mode == CompileMode.Combine ? "one " : "one each, ") + Format;
    }

    /// <summary>One file a compilation would write, and what goes in it.</summary>
    public sealed class CompileJob
    {
        internal CompileJob(string output, IReadOnlyList<SourceFile> sources)
        {
            Output = output;
            Sources = sources;
        }

        /// <summary>What comes out, as a file name.</summary>
        public string Output { get; }

        /// <summary>What goes in.</summary>
        public IReadOnlyList<SourceFile> Sources { get; }

        /// <summary>How much goes in, where the sizes are known.</summary>
        public long Bytes => Sources.Sum(s => s.Bytes);

        /// <inheritdoc />
        public override string ToString() =>
            Output + " <- " + Sources.Count.ToString("N0", CultureInfo.InvariantCulture)
            + (Sources.Count == 1 ? " file" : " files");
    }

    /// <summary>A file that lost to a newer one of itself.</summary>
    public sealed class Superseded
    {
        internal Superseded(SourceFile older, SourceFile newer)
        {
            Older = older;
            Newer = newer;
        }

        /// <summary>The one left out.</summary>
        public SourceFile Older { get; }

        /// <summary>The one that replaced it.</summary>
        public SourceFile Newer { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Compilation.NameOf(Older.Path) + " -> " + Compilation.NameOf(Newer.Path);
    }

    /// <summary>What a compilation would do, before it does any of it.</summary>
    public sealed class CompilePlan
    {
        internal CompilePlan(CompileRecipe recipe, IReadOnlyList<CompileJob> jobs,
                             IReadOnlyList<Superseded> superseded, IReadOnlyList<SourceFile> missing,
                             IReadOnlyList<SourceFile> unreadable, IReadOnlyList<string> notes,
                             string? refusal)
        {
            Recipe = recipe;
            Jobs = jobs;
            Superseded = superseded;
            Missing = missing;
            Unreadable = unreadable;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What was asked for.</summary>
        public CompileRecipe Recipe { get; }

        /// <summary>The files it would write, in order.</summary>
        public IReadOnlyList<CompileJob> Jobs { get; }

        /// <summary>Files left out because a newer revision of them is in.</summary>
        public IReadOnlyList<Superseded> Superseded { get; }

        /// <summary>Files that are not where the list says they are.</summary>
        public IReadOnlyList<SourceFile> Missing { get; }

        /// <summary>Files Navisworks would not take in.</summary>
        public IReadOnlyList<SourceFile> Unreadable { get; }

        /// <summary>Anything worth saying about how it was worked out.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is nothing to compile, when there is nothing.</summary>
        public string? Refusal { get; }

        /// <summary>How many source files are actually going in.</summary>
        public int Sources => Jobs.Sum(j => j.Sources.Count);

        /// <summary>True when there is something to write.</summary>
        public bool IsUsable => Refusal == null && Jobs.Count > 0;

        /// <summary>What it would do, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            var text = Jobs.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Jobs.Count == 1 ? " file to write, from " : " files to write, from ")
                     + Sources.ToString("N0", CultureInfo.InvariantCulture)
                     + (Sources == 1 ? " model." : " models.");

            if (Superseded.Count > 0)
                text += " " + Superseded.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Superseded.Count == 1 ? " older revision is" : " older revisions are")
                     + " left out.";

            if (Missing.Count > 0)
                text += " " + Missing.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Missing.Count == 1 ? " file is" : " files are") + " not there.";

            if (Unreadable.Count > 0)
                text += " " + Unreadable.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Unreadable.Count == 1 ? " file is" : " files are")
                     + " not something Navisworks reads.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>How one job of a run turned out.</summary>
    public sealed class CompileResult
    {
        /// <summary>Say how it went.</summary>
        /// <param name="output">Which file.</param>
        /// <param name="sources">How many went in.</param>
        /// <param name="secondsTaken">How long it took.</param>
        /// <param name="problem">What went wrong, or null.</param>
        public CompileResult(string output, int sources, double secondsTaken, string? problem = null)
        {
            Output = output ?? string.Empty;
            Sources = sources;
            SecondsTaken = secondsTaken;
            Problem = problem;
        }

        /// <summary>Which file.</summary>
        public string Output { get; }

        /// <summary>How many went in.</summary>
        public int Sources { get; }

        /// <summary>How long it took.</summary>
        public double SecondsTaken { get; }

        /// <summary>What went wrong, or null.</summary>
        public string? Problem { get; }

        /// <summary>True when the file was written.</summary>
        public bool Worked => Problem == null;

        /// <inheritdoc />
        public override string ToString() =>
            Output + (Worked
                ? " — " + SecondsTaken.ToString("0.#", CultureInfo.InvariantCulture) + "s"
                : " — FAILED: " + Problem);
    }

    /// <summary>
    /// Turning a folder of supplier drops into the file the project ships.
    ///
    /// The hard part is not the writing but knowing what goes in: a delivery folder holds three
    /// revisions of half the models, two of them superseded and none of them saying so except in the
    /// file name, and a federation built from all three is wrong in a way that looks like a
    /// federation built from one. So the revision is read out of the name, older ones lose to newer
    /// ones of the same base, and every file left out is named in the plan and again in the run log.
    ///
    /// What supersedes what: "Rev03" beats "Rev02" and "RevC" beats "RevB". "Rev03" and "RevC" beat
    /// nothing — they are not two points on one sequence, and ranking them would drop a model
    /// arbitrarily. Both are kept and the plan says why.
    ///
    /// The list comes from anywhere: files picked by hand, a folder walked, or a column of a
    /// spreadsheet, so a project that keeps its model register in Excel can keep it there.
    ///
    /// Every run writes a log beside the output: what went in, what was left out and why, and what
    /// failed. A batch that ran overnight and produced eleven files out of twelve has to be
    /// answerable in the morning.
    /// </summary>
    public static class Compilation
    {
        /// <summary>The whole-federation format.</summary>
        public const string Nwd = ".nwd";

        /// <summary>The pointer-to-the-sources format.</summary>
        public const string Nwf = ".nwf";

        /// <summary>The single-model cache format.</summary>
        public const string Nwc = ".nwc";

        /// <summary>Where saved compilations live in the project store.</summary>
        public const string StoreKey = "compilations";

        /// <summary>How many files one run will take.</summary>
        public const int MaxSources = 2000;

        /// <summary>
        /// What a compilation can write.
        ///
        /// NWD and NWF, and not NWC, because those are the two a host can be asked to save a
        /// document as. An NWC is a cache the authoring tool's own exporter produces from a native
        /// model; nothing that has a document open can write one, and offering it would be offering
        /// a button that could only ever apologise.
        /// </summary>
        public static IReadOnlyList<string> Writable { get; } = new[] { Nwd, Nwf };

        /// <summary>
        /// What Navisworks will take in.
        ///
        /// The list a person recognises rather than everything an install might have an exporter
        /// for: the point of checking is to catch the spreadsheet and the PDF somebody dropped in
        /// the delivery folder, not to adjudicate an unusual CAD format.
        /// </summary>
        public static IReadOnlyList<string> Readable { get; } = new[]
        {
            ".nwd", ".nwf", ".nwc", ".rvt", ".rfa", ".ifc", ".dwg", ".dxf", ".dgn", ".skp",
            ".3ds", ".fbx", ".sat", ".stp", ".step", ".igs", ".iges", ".obj", ".pts", ".e57",
        };

        /// <summary>The file name out of a path, whichever slash it uses.</summary>
        /// <param name="path">The path.</param>
        public static string NameOf(string? path)
        {
            var text = (path ?? string.Empty).Trim();

            var cut = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));

            return cut < 0 ? text : text.Substring(cut + 1);
        }

        /// <summary>The extension, lower case, with its dot, or empty.</summary>
        /// <param name="name">The file name.</param>
        public static string ExtensionOf(string? name)
        {
            var text = (name ?? string.Empty).Trim();
            var dot = text.LastIndexOf('.');

            return dot <= 0 ? string.Empty : text.Substring(dot).ToLowerInvariant();
        }

        /// <summary>The file name with its extension taken off.</summary>
        /// <param name="name">The file name.</param>
        public static string WithoutExtension(string? name)
        {
            var text = (name ?? string.Empty).Trim();
            var dot = text.LastIndexOf('.');

            return dot <= 0 ? text : text.Substring(0, dot);
        }

        /// <summary>
        /// Read a revision out of a stem, and give back what is left.
        ///
        /// Only the shapes a person would recognise as a revision, anchored at the end: Rev03,
        /// REV_A, -R2, _v11. A bare trailing number is deliberately not one - "Block_02" is a
        /// building, not a second issue of "Block", and guessing wrong there merges two models
        /// into one and hides half a project.
        /// </summary>
        /// <param name="stem">The file name without its extension.</param>
        public static (string Base, Revision Revision) Split(string? stem)
        {
            var text = (stem ?? string.Empty).Trim();

            if (text.Length == 0) return (string.Empty, Revision.None);

            // Cut into separator-delimited words, keeping where each one starts, because the base
            // name is whatever comes before the words the revision used.
            var words = new List<(string Word, int At)>();
            var start = -1;

            for (var i = 0; i <= text.Length; i++)
            {
                var separator = i == text.Length || text[i] == '_' || text[i] == '-'
                                || text[i] == ' ' || text[i] == '.';

                if (separator)
                {
                    if (start >= 0) words.Add((text.Substring(start, i - start), start));
                    start = -1;
                    continue;
                }

                if (start < 0) start = i;
            }

            if (words.Count < 2) return (text, Revision.None);

            var last = words[words.Count - 1];
            var joined = Joined.Match(last.Word);

            // Rev03, RevA, R2, V11 - the word and the revision written together.
            if (joined.Success)
                return Made(text, last.At, last.Word, joined.Groups[2].Value);

            if (words.Count < 3) return (text, Revision.None);

            var before = words[words.Count - 2];

            // REV_03, R_A - the word and the revision as two words. Only where the word is on its
            // own: a bare trailing number is not a revision, because "Block_02" is a building.
            if (Alone.IsMatch(before.Word) && Value.IsMatch(last.Word))
                return Made(text, before.At, before.Word + last.Word, last.Word);

            return (text, Revision.None);
        }

        private static readonly Regex Joined =
            new Regex(@"^(REV|R|V)([0-9]+|[A-Z]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Alone =
            new Regex(@"^(REV|R|V)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Value =
            new Regex(@"^([0-9]+|[A-Z]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static (string Base, Revision Revision) Made(string text, int at, string written,
                                                             string value)
        {
            var head = text.Substring(0, at).TrimEnd('_', '-', ' ', '.');

            // A file called nothing but its revision has no base to group other revisions of it
            // under, so it is left as it is rather than becoming an empty family everything joins.
            if (head.Length == 0) return (text, Revision.None);

            if (value.Length == 0) return (text, Revision.None);

            return value.All(char.IsDigit)
                ? (head, long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? Revision.Numbered(written, number)
                    : Revision.None)
                : (head, Revision.Lettered(written, value));
        }

        /// <summary>
        /// Work out what would be written.
        /// </summary>
        /// <param name="files">Everything on the list.</param>
        /// <param name="recipe">What to make of it.</param>
        /// <param name="stamp">Today, as yyyy-MM-dd, for a stamped name. Passed in so this is testable.</param>
        public static CompilePlan Plan(IEnumerable<SourceFile>? files, CompileRecipe? recipe,
                                       string? stamp = null)
        {
            var wanted = recipe ?? new CompileRecipe();
            var notes = new List<string>();

            var all = (files ?? Enumerable.Empty<SourceFile>())
                .Where(f => f != null && f.Path.Length > 0)
                .ToList();

            if (all.Count == 0)
                return Nothing(wanted, notes,
                    "There is nothing on the list. Add the models to compile - pick them, point at "
                    + "a folder, or bring in a spreadsheet of their paths.");

            if (all.Count > MaxSources)
            {
                notes.Add("The list held " + all.Count.ToString("N0", CultureInfo.InvariantCulture)
                          + " files. The first " + MaxSources.ToString("N0", CultureInfo.InvariantCulture)
                          + " are used.");

                all = all.Take(MaxSources).ToList();
            }

            var missing = all.Where(f => !f.Exists).ToList();
            var unreadable = all.Where(f => f.Exists && !f.IsReadable).ToList();
            // Read-only, because Newest hands back a new list rather than filtering this one.
            IReadOnlyList<SourceFile> usable = all.Where(f => f.Exists && f.IsReadable).ToList();

            var superseded = new List<Superseded>();

            if (wanted.NewestOnly) usable = Newest(usable, superseded, notes);

            if (usable.Count == 0)
                return new CompilePlan(wanted, Array.Empty<CompileJob>(), superseded, missing,
                    unreadable, notes,
                    "Nothing on that list can go in. "
                    + (missing.Count > 0 ? "The files are not where the list says they are. "
                                         : string.Empty)
                    + (unreadable.Count > 0 ? "Navisworks does not read those formats." : string.Empty));

            var format = Format(wanted.Format, notes);
            var jobs = new List<CompileJob>();

            if (wanted.Mode == CompileMode.Combine)
            {
                jobs.Add(new CompileJob(FileName(wanted.Name, wanted.Stamped, stamp, format), usable));
            }
            else
            {
                foreach (var file in usable)
                    jobs.Add(new CompileJob(
                        FileName(file.Base, wanted.Stamped, stamp, format),
                        new[] { file }));

                var clashing = jobs.GroupBy(j => j.Output, StringComparer.OrdinalIgnoreCase)
                                   .Where(g => g.Count() > 1)
                                   .ToList();

                if (clashing.Count > 0)
                    notes.Add(clashing.Count.ToString("N0", CultureInfo.InvariantCulture)
                              + (clashing.Count == 1 ? " output name is" : " output names are")
                              + " wanted by more than one file, so the last one written wins. Turn "
                              + "off \"newest only\" and they are all there, or rename the sources.");
            }

            return new CompilePlan(wanted, jobs, superseded, missing, unreadable, notes, null);
        }

        /// <summary>
        /// The paths in a column of a spreadsheet.
        ///
        /// Here because a project that keeps its model register in Excel already has the list, and
        /// retyping it into a plug-in is how the register and the federation start disagreeing.
        /// </summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="heading">The column holding the paths, or null for the first that looks like one.</param>
        public static IReadOnlyList<string> Paths(SheetTable? sheet, string? heading = null)
        {
            if (sheet == null || sheet.IsEmpty) return Array.Empty<string>();

            var column = heading == null ? -1 : sheet.ColumnOf(heading);

            if (column < 0) column = Likeliest(sheet);
            if (column < 0) return Array.Empty<string>();

            var paths = new List<string>();

            for (var r = 1; r < sheet.Rows.Count; r++)
            {
                var cell = sheet.Cell(r, column);

                if (string.IsNullOrWhiteSpace(cell)) continue;

                var text = cell!.Trim();

                if (ExtensionOf(NameOf(text)).Length == 0) continue;

                paths.Add(text);
            }

            return paths;
        }

        /// <summary>
        /// The run, written down beside what it produced.
        ///
        /// A batch that ran overnight and produced eleven files out of twelve is one somebody has
        /// to be able to ask a question about in the morning, and the answer is not in anybody's
        /// memory by then.
        /// </summary>
        /// <param name="plan">What was going to happen.</param>
        /// <param name="results">What happened.</param>
        /// <param name="when">When it ran, as text. Passed in so this is testable.</param>
        public static string Log(CompilePlan? plan, IEnumerable<CompileResult>? results, string? when = null)
        {
            var text = new StringBuilder();
            var done = (results ?? Enumerable.Empty<CompileResult>()).Where(r => r != null).ToList();

            text.AppendLine("CamelWorks compilation log");
            text.AppendLine("bimcamel.com");
            text.AppendLine();
            text.AppendLine("Run:      " + (when ?? "(not recorded)"));
            text.AppendLine("Name:     " + (plan?.Recipe.Name ?? "(none)"));
            text.AppendLine("Mode:     " + (plan?.Recipe.Mode.ToString() ?? "(none)"));
            text.AppendLine("Format:   " + (plan?.Recipe.Format ?? "(none)"));
            text.AppendLine();

            if (plan?.Refusal != null)
            {
                text.AppendLine("Nothing was compiled: " + plan.Refusal);
                return text.ToString();
            }

            text.AppendLine("WHAT WAS WRITTEN");

            if (done.Count == 0) text.AppendLine("  (nothing)");

            foreach (var result in done)
            {
                text.AppendLine("  " + (result.Worked ? "OK    " : "FAILED") + "  " + result.Output
                                + "  <- " + result.Sources.ToString(CultureInfo.InvariantCulture)
                                + " model(s), "
                                + result.SecondsTaken.ToString("0.#", CultureInfo.InvariantCulture) + "s");

                if (result.Problem != null) text.AppendLine("          " + result.Problem);
            }

            text.AppendLine();
            text.AppendLine("WHAT WENT IN");

            foreach (var job in plan?.Jobs ?? Array.Empty<CompileJob>())
            {
                text.AppendLine("  " + job.Output);

                foreach (var source in job.Sources) text.AppendLine("      " + source.Path);
            }

            Left(text, "LEFT OUT - AN OLDER REVISION",
                 plan?.Superseded.Select(s => s.Older.Path + "  (superseded by "
                                              + NameOf(s.Newer.Path) + ")"));

            Left(text, "LEFT OUT - NOT THERE", plan?.Missing.Select(m => m.Path));

            Left(text, "LEFT OUT - NOT A FORMAT NAVISWORKS READS", plan?.Unreadable.Select(u => u.Path));

            if (plan != null && plan.Notes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("NOTES");

                foreach (var note in plan.Notes) text.AppendLine("  " + note);
            }

            return text.ToString();
        }

        /// <summary>Every saved compilation, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<CompileRecipe> Saved(JsonValue? json)
        {
            var saved = new List<CompileRecipe>();

            if (json == null) return saved;

            foreach (var item in json.Items) saved.Add(CompileRecipe.FromJson(item));

            return saved;
        }

        /// <summary>The list with one compilation put in it, replacing any of the same name.</summary>
        /// <param name="saved">What is saved now.</param>
        /// <param name="recipe">The one to keep.</param>
        public static JsonValue Keep(IEnumerable<CompileRecipe>? saved, CompileRecipe? recipe)
        {
            var kept = (saved ?? Enumerable.Empty<CompileRecipe>()).ToList();

            if (recipe != null)
            {
                kept.RemoveAll(c => string.Equals(c.Name, recipe.Name, StringComparison.OrdinalIgnoreCase));
                kept.Add(recipe);
            }

            return JsonValue.Array(kept.Select(c => c.ToJson()));
        }

        /// <summary>What one output file is called.</summary>
        /// <param name="stem">What it is named after.</param>
        /// <param name="stamped">Whether the date goes in it.</param>
        /// <param name="stamp">Today, as yyyy-MM-dd.</param>
        /// <param name="extension">The format, with its dot.</param>
        public static string FileName(string? stem, bool stamped, string? stamp, string extension)
        {
            var name = Safe(stem);

            if (stamped && !string.IsNullOrWhiteSpace(stamp)) name += " " + stamp!.Trim();

            return name + extension;
        }

        /// <summary>
        /// The format that will actually be written, saying so when it is not the one asked for.
        /// </summary>
        /// <param name="wanted">What the recipe says.</param>
        /// <param name="notes">Where to say what happened.</param>
        private static string Format(string? wanted, IList<string> notes)
        {
            var asked = (wanted ?? string.Empty).Trim().ToLowerInvariant();

            if (Writable.Contains(asked, StringComparer.OrdinalIgnoreCase)) return asked;

            notes.Add(asked == Nwc
                ? "An NWC is a cache the authoring tool's own exporter writes from a native model - "
                  + "nothing with a document open can produce one. This is written as NWD, which is "
                  + "what a federation is saved as."
                : "\"" + asked + "\" is not a format a document can be saved as. This is written "
                  + "as NWD.");

            return Nwd;
        }

        private static IReadOnlyList<SourceFile> Newest(IReadOnlyList<SourceFile> files,
                                                        IList<Superseded> superseded,
                                                        IList<string> notes)
        {
            var kept = new List<SourceFile>();
            var split = 0;

            foreach (var family in files.GroupBy(f => f.Base + "|" + f.Extension,
                                                 StringComparer.OrdinalIgnoreCase))
            {
                var members = family.ToList();

                if (members.Count == 1) { kept.Add(members[0]); continue; }

                // Grouped by how the revision is written before anything is ranked: Rev03 and RevC
                // are not two points on one sequence, so each way of writing them keeps its own
                // winner and the fact that there is more than one is reported.
                var kinds = members.GroupBy(m => m.Revision.Kind).ToList();

                if (kinds.Count > 1) split++;

                foreach (var kind in kinds)
                {
                    var run = kind.ToList();

                    if (kind.Key == RevisionKind.None) { kept.AddRange(run); continue; }

                    var winner = run[0];

                    foreach (var member in run)
                        if (member.Revision.Beats(winner.Revision)) winner = member;

                    kept.Add(winner);

                    foreach (var member in run)
                        if (!ReferenceEquals(member, winner))
                            superseded.Add(new Superseded(member, winner));
                }
            }

            if (split > 0)
                notes.Add(split.ToString("N0", CultureInfo.InvariantCulture)
                          + (split == 1 ? " model has" : " models have")
                          + " revisions written more than one way - numbers and letters both - so "
                          + "they cannot be ranked against each other and one of each is kept.");

            return kept;
        }

        private static int Likeliest(SheetTable sheet)
        {
            var best = -1;
            var most = 0;

            for (var c = 0; c < sheet.Columns; c++)
            {
                var looks = 0;

                for (var r = 1; r < sheet.Rows.Count && r < 200; r++)
                {
                    var cell = sheet.Cell(r, c);

                    if (string.IsNullOrWhiteSpace(cell)) continue;

                    if (Readable.Contains(ExtensionOf(NameOf(cell)), StringComparer.OrdinalIgnoreCase))
                        looks++;
                }

                if (looks <= most) continue;

                most = looks;
                best = c;
            }

            return best;
        }

        private static void Left(StringBuilder text, string heading, IEnumerable<string>? lines)
        {
            var found = (lines ?? Enumerable.Empty<string>()).ToList();

            if (found.Count == 0) return;

            text.AppendLine();
            text.AppendLine(heading);

            foreach (var line in found) text.AppendLine("  " + line);
        }

        private static string Safe(string? stem)
        {
            var text = (stem ?? string.Empty).Trim();

            if (text.Length == 0) return "Federation";

            var built = new StringBuilder(text.Length);

            foreach (var letter in text)
                built.Append(Bad.IndexOf(letter) >= 0 || letter < ' ' ? '-' : letter);

            var cleaned = built.ToString().Trim('-', ' ', '.');

            return cleaned.Length == 0 ? "Federation" : cleaned;
        }

        private const string Bad = "\\/:*?\"<>|";

        private static CompilePlan Nothing(CompileRecipe recipe, IReadOnlyList<string> notes,
                                           string refusal) =>
            new CompilePlan(recipe, Array.Empty<CompileJob>(), Array.Empty<Superseded>(),
                            Array.Empty<SourceFile>(), Array.Empty<SourceFile>(), notes, refusal);
    }
}
