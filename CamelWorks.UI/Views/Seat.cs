using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;

namespace CamelWorks.UI.Views
{
    /// <summary>One saved set, as a screen needs to read it: what it is called and what it holds.</summary>
    /// <summary>One loaded model: what it is called, and what it is keyed by.</summary>
    public sealed class SeatModel
    {
        /// <summary>Describe one.</summary>
        /// <param name="name">What it shows as.</param>
        /// <param name="scope">The scope it is keyed by, as IModelSource.Scope gives it.</param>
        public SeatModel(string name, string scope)
        {
            Name = name ?? string.Empty;
            Scope = scope ?? string.Empty;
        }

        /// <summary>What it shows as.</summary>
        public string Name { get; }

        /// <summary>The scope it is keyed by. Not the name, and never shown.</summary>
        public string Scope { get; }
    }

    public sealed class SeatSet
    {
        /// <summary>Describe one.</summary>
        /// <param name="name">Its name.</param>
        /// <param name="folder">The folders it sits in, innermost last, or empty at the top.</param>
        /// <param name="search">True for a set the host holds as a query rather than a fixed list.</param>
        /// <param name="members">How many elements are in it.</param>
        public SeatSet(string name, string folder, bool search, int members)
        {
            Name = name ?? string.Empty;
            Folder = folder ?? string.Empty;
            IsSearch = search;
            Members = members;
        }

        /// <summary>Its name.</summary>
        public string Name { get; }

        /// <summary>The folders it sits in, innermost last, or empty at the top.</summary>
        public string Folder { get; }

        /// <summary>True for a search set — one the host re-runs rather than one it stored.</summary>
        public bool IsSearch { get; }

        /// <summary>How many elements are in it.</summary>
        public int Members { get; }
    }

    /// <summary>
    /// The seat a screen is running in: everything it needs from the host, and nothing else.
    ///
    /// Every screen in this product previously took a <see cref="Session"/>, and a Session cannot be
    /// made without Navisworks — its constructor takes an Autodesk document. So no screen could be
    /// built outside a licensed CAD application, and the only way to look at one was to install the
    /// plug-in and click through to it.
    ///
    /// That is why layout faults shipped. A column header drawn four pixels short of its own
    /// letters, a rotated name clipped by its border, a folder band that collapses to nothing and
    /// cannot be pressed to reopen it: each is visible in an image and invisible to a compiler, and
    /// each cost a seat, a licence and a person's time to find.
    ///
    /// A seat is the narrow set of questions a screen asks, not the host's whole API: what the clash
    /// engine can do, where the project is kept, what saved sets and models there are, and what is
    /// selected. Answered by Navisworks on a real seat, and by <see cref="EmptySeat"/> in the render
    /// harness, where the screen is the shipping screen drawn by WPF at the size WPF gives it.
    ///
    /// It is not a way to fake a model. Nothing here pretends a document is loaded: an empty seat
    /// reports no sets, no models and nothing selected, and every screen already has to be correct
    /// in that state, which is the state at the start of every session.
    /// </summary>
    public interface ISeat
    {
        /// <summary>The host's clash engine. Never null; ask <see cref="IClashSource.IsAvailable"/> first.</summary>
        IClashSource Clash { get; }

        /// <summary>Why the clash engine is unavailable, when it is, or null.</summary>
        string? ClashProblem { get; }

        /// <summary>Where this project's own data is kept.</summary>
        ProjectStore Store { get; }

        /// <summary>Every finding this project holds, whatever check produced it.</summary>
        FindingStore Findings { get; }

        /// <summary>Every check this seat can offer.</summary>
        CheckCatalogue Checks { get; }

        /// <summary>What CamelWorks derived about this project, with any overrides applied.</summary>
        ProjectProfile Profile { get; }

        /// <summary>
        /// How many metres one model unit is.
        ///
        /// Every threshold in this product is stated in metres and the host reports geometry in
        /// whatever unit the document was authored in, so a screen that states one has to be able
        /// to convert at the boundary.
        /// </summary>
        double MetresPerUnit { get; }

        /// <summary>
        /// Colour, transparency, visibility, camera and the section box.
        ///
        /// The document itself is deliberately not here. A host document is a Navisworks type, and
        /// putting one on this interface would drag Navisworks into the render harness, which is
        /// what this interface exists to keep out. What a screen actually asks a document for is
        /// its models, its sets, its selection and a traversal, and those are the members above.
        /// </summary>
        IViewSession View { get; }

        /// <summary>Write a line into the project's history.</summary>
        /// <param name="kind">What sort of act it was.</param>
        /// <param name="summary">One line saying what happened.</param>
        /// <param name="detail">More, when there is more.</param>
        void Record(string kind, string summary, string? detail = null);

        /// <summary>Every saved set the document holds, in the order it lists them.</summary>
        IReadOnlyList<SeatSet> Sets();

        /// <summary>The names of the models loaded, in the order they were appended.</summary>
        IReadOnlyList<string> Models();

        /// <summary>
        /// The models loaded, with the scope each is known by as well as the name it shows.
        ///
        /// Both, because they are not the same string and the difference is load-bearing: a
        /// model's scope is a hash of the file it came from, and a clash test's whole-models side
        /// is written in scopes. A screen that offers names and hands them on as scopes builds a
        /// test the host then cannot resolve, which is what the Create tab did.
        /// </summary>
        IReadOnlyList<SeatModel> LoadedModels();

        /// <summary>The keys of whatever is selected in the host right now.</summary>
        IReadOnlyList<ElementKey> Selected();

        /// <summary>
        /// The keys of everything drawn in the document: not hidden, and carrying geometry.
        ///
        /// Streamed rather than listed, because a federated model is millions of items and the
        /// screen that asks for this reports progress while it reads. A list would have to be
        /// built in full before the first number could be shown.
        /// </summary>
        IEnumerable<ElementKey> Visible();
    }

    /// <summary>
    /// A seat with nothing in it: no clash engine, no sets, no models, nothing selected.
    ///
    /// A real state rather than a stub. It is what a screen faces before a document is open, on a
    /// Navisworks edition with no clash engine, and in the render harness. The harness renders this
    /// seat because it is the state least often checked.
    /// </summary>
    public sealed class EmptySeat : ISeat
    {
        private readonly ProjectStore _store;
        private readonly IClashSource _clash;
        private readonly FakeDocument _document = new FakeDocument();

        /// <summary>Take a seat with nothing in it.</summary>
        /// <param name="store">Where to keep things. A scratch store is fine.</param>
        /// <param name="clash">The clash engine to answer with. <see cref="Host.WithoutClash"/>
        /// hands out one that says it is unavailable, which is the usual choice here.</param>
        public EmptySeat(ProjectStore store, IClashSource clash)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clash = clash ?? throw new ArgumentNullException(nameof(clash));

            Findings = FindingStore.Open(PhysicalFileSystem.Instance, store);
            Profile = ProjectProfile.Derive(null, store.Path);
            View = new FakeViewSession(_document);
            Checks = new CheckCatalogue();

            // Registered on the empty seat too, so the picker on a seat with no document lists the
            // checks and says why each is unavailable rather than leaving them out.
            var runs = CheckRunStore.Read(store);

            Checks.Add(new ClearanceCheck(Findings.Findings, runs, _document, project: store));
            Checks.Add(new FallCheck(Findings.Findings, runs, _document, _document, project: store));
        }

        /// <inheritdoc />
        public FindingStore Findings { get; }

        /// <inheritdoc />
        public CheckCatalogue Checks { get; }

        /// <inheritdoc />
        public ProjectProfile Profile { get; }

        /// <summary>One metre per unit: an empty seat has no document to have been authored in one.</summary>
        public double MetresPerUnit => 1.0;

        /// <summary>An in-memory view session, so a screen that colours or frames something answers.</summary>
        public IViewSession View { get; }

        /// <inheritdoc />
        public IClashSource Clash => _clash;

        /// <inheritdoc />
        public string? ClashProblem => _clash.IsAvailable ? null : "No document is open.";

        /// <inheritdoc />
        public ProjectStore Store => _store;

        /// <inheritdoc />
        public void Record(string kind, string summary, string? detail = null) { }

        /// <inheritdoc />
        public IReadOnlyList<SeatSet> Sets() => Array.Empty<SeatSet>();

        /// <inheritdoc />
        public IReadOnlyList<string> Models() => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyList<SeatModel> LoadedModels() => Array.Empty<SeatModel>();

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Selected() => Array.Empty<ElementKey>();

        /// <inheritdoc />
        public IEnumerable<ElementKey> Visible() => Array.Empty<ElementKey>();
    }

    /// <summary>
    /// What a screen asks a seat for, asked once here rather than on every panel.
    ///
    /// Three of the check panels had their own copy of "ask the document, and answer nothing when
    /// it will not", each with its own comment saying why. A picker that cannot be filled is a
    /// picker with nothing in it, never a screen that refuses to open — and that decision belongs
    /// in one place, so a fourth panel gets it without being told.
    /// </summary>
    public static class Seated
    {
        /// <summary>
        /// Whatever the document answers, or nothing when it will not.
        /// </summary>
        /// <typeparam name="T">What is being read.</typeparam>
        /// <param name="supply">Where it comes from.</param>
        public static IReadOnlyList<T> Offer<T>(Func<IReadOnlyList<T>> supply)
        {
            if (supply == null) throw new ArgumentNullException(nameof(supply));

            try
            {
                return supply();
            }
            catch (Exception)
            {
                return Array.Empty<T>();
            }
        }

        /// <summary>Every saved set this document holds, or none when it will not say.</summary>
        /// <param name="seat">The seat.</param>
        public static IReadOnlyList<SeatSet> Sets(this ISeat seat) =>
            Offer(() => seat.Sets());

        /// <summary>Every saved set's name, once each, in order.</summary>
        /// <param name="seat">The seat.</param>
        public static IReadOnlyList<string> SetNames(this ISeat seat) =>
            Ui.Listed(Sets(seat).Select(one => one.Name));

        /// <summary>Every loaded model's name, once each, in order.</summary>
        /// <param name="seat">The seat.</param>
        public static IReadOnlyList<string> ModelNames(this ISeat seat) =>
            Ui.Listed(Offer(() => seat.Models()));

        /// <summary>Every loaded model, with the scope each is keyed by.</summary>
        /// <param name="seat">The seat.</param>
        public static IReadOnlyList<SeatModel> Loaded(this ISeat seat) =>
            Offer(() => seat.LoadedModels());

        /// <summary>
        /// The folders a check's tests are filed in, once each, in order.
        ///
        /// Off the tests rather than off a list somebody maintains: a clash folder exists because
        /// a test is in it, and one nothing is in is one the document has forgotten.
        /// </summary>
        /// <param name="check">The check.</param>
        public static IReadOnlyList<string> Folders(ICheck? check)
        {
            if (check == null) return Array.Empty<string>();

            // Off the tests, which is all a project-file check has to go on.
            var used = Offer(() => check.Runs()).Select(one => one.Folder);

            // And, where the check is the document's own clash data, the folders the document
            // holds with nothing in them. The comment that used to stand here said a folder
            // nothing is in is one the document has forgotten. It is not: it is the folder
            // somebody made in Navisworks a moment ago and is trying to file into, and leaving
            // it off the list is why they could not.
            var held = check is ClashCheck clash
                ? Offer(() => clash.Folders())
                : Array.Empty<string>();

            return Ui.Listed(used.Concat(held));
        }
    }
}
