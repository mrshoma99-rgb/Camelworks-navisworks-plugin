using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Clash
{
    /// <summary>One authored comment on a clash result.</summary>
    public sealed class ClashComment
    {
        /// <summary>Create a comment.</summary>
        /// <param name="author">Who wrote it.</param>
        /// <param name="whenTicks">When, as UTC ticks. Zero when the source did not say.</param>
        /// <param name="body">What they wrote.</param>
        public ClashComment(string author, long whenTicks, string body)
        {
            Author = string.IsNullOrWhiteSpace(author) ? "(unknown)" : author.Trim();
            WhenTicks = whenTicks;
            Body = body ?? string.Empty;
        }

        /// <summary>Who wrote it.</summary>
        public string Author { get; }

        /// <summary>When, as UTC ticks. Zero when the source did not say.</summary>
        public long WhenTicks { get; }

        /// <summary>What they wrote.</summary>
        public string Body { get; }

        /// <summary>
        /// What the host calls this comment, or zero when it did not come from one.
        ///
        /// Navisworks gives every comment an id and makes every other field read-only, so an
        /// existing remark can only be changed by rebuilding the whole trail without it, or with
        /// something else in its place. The id is how the rebuild knows which one that is.
        /// </summary>
        public long HostId { get; set; }

        /// <inheritdoc />
        public override string ToString() => Author + ": " + Body;
    }

    /// <summary>
    /// One clash result as the rules see it.
    ///
    /// Flattened on purpose. A rule needs a level, a grid reference, a system name and a model
    /// name; making it walk back to the host for each would put every rule on the STA thread and
    /// make the pipeline untestable. The adapter resolves these once, and the rules are then pure
    /// functions over values — which is why the whole pipeline runs on a Linux CI job.
    /// </summary>
    public sealed class ClashItem
    {
        /// <summary>Create an item.</summary>
        public ClashItem(ClashKey key, string testName)
        {
            Key = key;
            TestName = testName ?? throw new ArgumentNullException(nameof(testName));
        }

        /// <summary>Stable identity.</summary>
        public ClashKey Key { get; }

        /// <summary>The clash test that produced it.</summary>
        public string TestName { get; }

        /// <summary>Test folder, when the matrix nests tests.</summary>
        public string? TestFolder { get; set; }

        /// <summary>
        /// What the engine calls this result, or null.
        ///
        /// The name a coordinator says out loud - "Clash117", or whatever they renumbered it to.
        /// Carried so that a marker exported into somebody else's software arrives under that
        /// name in their selection tree, which is the only way a person on the other end can say
        /// which one they are looking at back to the person who sent it.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Where this result stands, after carry-over has run.
        ///
        /// Defaults to <see cref="FindingStatus.New"/>, which is the truth for a result no previous
        /// run knew about. <see cref="ClashCarryOver"/> overwrites it for the ones a previous run
        /// did know about — that, and nothing else, is what stops every re-export presenting last
        /// month's reviewed clashes as brand new.
        /// </summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>
        /// The hand-made group this result was in last run, or null.
        ///
        /// <b>Only hand-made groups carry.</b> A derived group must re-derive, because the stack is
        /// the source of truth for it — carrying a derived name would freeze the board against its
        /// own rules, so adding a Grid rule would change nothing. A hand-made group has no other
        /// memory, so it carries or it is lost.
        /// </summary>
        public string? CarriedGroup { get; set; }

        /// <summary>
        /// Who this result is assigned to, or null.
        ///
        /// Carried for the review round: the file a reviewer marks up travels with its
        /// assignments, and the sync puts them back into the host's own assigned-to field.
        /// </summary>
        public string? AssignedTo { get; set; }

        /// <summary>
        /// The authored comment trail, oldest first.
        ///
        /// A list and not a single string, because the whole point of the review round is three
        /// reviewers writing onto one file — and a format that keeps only the last comment turns
        /// a conversation into whoever saved last.
        /// </summary>
        public IList<ClashComment> Comments { get; } = new List<ClashComment>();

        /// <summary>Level name, from the zoning pass. Null when the model has no usable levels.</summary>
        public string? Level { get; set; }

        /// <summary>Nearest grid intersection, e.g. "C4". Null when the model has no grids.</summary>
        public string? Grid { get; set; }

        /// <summary>Zone name, when zones were derived.</summary>
        public string? Zone { get; set; }

        /// <summary>
        /// How the document that wrote this file finds element A again, without searching for it.
        ///
        /// <b>Why a file carries this at all.</b> The element keys in this file are hashes, and a
        /// hash cannot be turned back into the thing it names: the only way from a key to an
        /// element is to hash every element in the federation until one matches, which on a real
        /// job is the indexing pass. That is a search the writing seat never needed to do - it had
        /// both elements in its hand when the clash was read - so it writes down where they were
        /// instead, and the reading seat looks them up rather than hunting for them.
        ///
        /// Opaque here on purpose. This is the host's own handle for an element and Core neither
        /// builds nor reads it; the adapter that wrote it is the only thing that understands it,
        /// which is what lets the encoding change without a new file format.
        ///
        /// Null is ordinary: a file written before this existed, or one written from a file rather
        /// than a model. <see cref="TreeA"/> is the fallback, and the keys are the fallback to that.
        /// </summary>
        public string? PathIdA { get; set; }

        /// <summary>How the writing document finds element B again. See <see cref="PathIdA"/>.</summary>
        public string? PathIdB { get; set; }

        /// <summary>
        /// Element A's path down the selection tree, as the names a person would read.
        ///
        /// The fallback for <see cref="PathIdA"/>, and the reason both are written rather than
        /// one: a native handle is exact and fast where it resolves, and a path of display names
        /// is something any document with the same tree can walk, including one this handle was
        /// not made against. Walking it is a descent - depth times the children at each step - and
        /// not a search of the whole model.
        /// </summary>
        public string? TreeA { get; set; }

        /// <summary>Element B's path down the selection tree. See <see cref="TreeA"/>.</summary>
        public string? TreeB { get; set; }

        /// <summary>Display name of the model owning item A.</summary>
        public string? ModelA { get; set; }

        /// <summary>Display name of the model owning item B.</summary>
        public string? ModelB { get; set; }

        /// <summary>Discipline of item A, when derivable.</summary>
        public string? DisciplineA { get; set; }

        /// <summary>Discipline of item B, when derivable.</summary>
        public string? DisciplineB { get; set; }

        /// <summary>System name of item A — the MEP system, where there is one.</summary>
        public string? SystemA { get; set; }

        /// <summary>System name of item B.</summary>
        public string? SystemB { get; set; }

        /// <summary>
        /// What a reviewer set that the host has no field for - priority, due date, stage, tags.
        ///
        /// Carried on the item so it goes back out on the next export. Without it a reviewer's
        /// priority is quietly reset every time the file makes a round trip, which is a worse
        /// failure than never having carried it: they set it, saw it accepted, and it went.
        /// </summary>
        public ClashAside? Review { get; set; }

        /// <summary>
        /// The BCF topic this came back as, when it was read from a file.
        ///
        /// Kept so a re-saved markup carries the same topic identity it arrived with, rather than
        /// a freshly derived one. The two are normally equal - the derivation is a function of the
        /// element pair - but a topic whose elements were recovered differently, or one somebody's
        /// own tool raised, would otherwise come home under a name the model has never seen.
        /// </summary>
        public string? TopicGuid { get; set; }

        /// <summary>
        /// The first element's IFC GlobalId, when it has one.
        ///
        /// Carried on the item because it is the one thing a receiving tool needs in order to
        /// select the element, and because finding it costs a property read the export would
        /// otherwise pay again for every clash the same duct appears in.
        /// </summary>
        public string? IfcGuidA { get; set; }

        /// <summary>The second element's IFC GlobalId, when it has one.</summary>
        public string? IfcGuidB { get; set; }

        /// <summary>Category of item A.</summary>
        public string? CategoryA { get; set; }

        /// <summary>Category of item B.</summary>
        public string? CategoryB { get; set; }

        /// <summary>Clash point, model units.</summary>
        public double X { get; set; }

        /// <summary>Clash point, model units.</summary>
        public double Y { get; set; }

        /// <summary>Clash point, model units.</summary>
        public double Z { get; set; }

        /// <summary>Overlap volume in cubic model units, where the engine reported one.</summary>
        public double? OverlapVolume { get; set; }

        /// <summary>Signed distance the engine reported. Negative is penetration; positive is a clearance miss.</summary>
        public double? Distance { get; set; }

        /// <summary>
        /// Angle between the two elements' dominant axes, in degrees, 0 to 90. Null when either
        /// element has no dominant axis — a slab has none, and a rule about crossing angle should
        /// not silently treat that as zero.
        /// </summary>
        public double? CrossingAngleDegrees { get; set; }

        /// <summary>Named sets item A belongs to.</summary>
        public ISet<string> SetsA { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Named sets item B belongs to.</summary>
        public ISet<string> SetsB { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Arbitrary properties a rule can match on, keyed "Category/Name".</summary>
        public IDictionary<string, string?> Properties { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when both participants are in the same model — usually a modelling artefact.</summary>
        public bool IsSameModel =>
            ModelA != null && string.Equals(ModelA, ModelB, StringComparison.OrdinalIgnoreCase);

        /// <summary>The two model names in a stable order, for pair grouping.</summary>
        public string ModelPair
        {
            get
            {
                var a = ModelA ?? "?";
                var b = ModelB ?? "?";
                return string.CompareOrdinal(a, b) <= 0 ? a + " v " + b : b + " v " + a;
            }
        }

        /// <summary>The two disciplines in a stable order.</summary>
        public string DisciplinePair
        {
            get
            {
                var a = DisciplineA ?? "?";
                var b = DisciplineB ?? "?";
                return string.CompareOrdinal(a, b) <= 0 ? a + " v " + b : b + " v " + a;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            TestName + " " + (Level ?? "?") + "/" + (Grid ?? "?") + " @"
            + X.ToString("0.##", CultureInfo.InvariantCulture) + ","
            + Y.ToString("0.##", CultureInfo.InvariantCulture) + ","
            + Z.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
