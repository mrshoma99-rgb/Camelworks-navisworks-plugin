using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// How a pack of findings is written as BCF.
    ///
    /// The viewpoint settings are the clash writer's, generalised: they framed a clash point and
    /// coloured the two elements of a pair, and they now frame the finding's point and colour
    /// <see cref="Finding.AllElements"/>. Every setting a coordinator had over a clash pack — the
    /// camera direction, the colouring, the section box, the rendered picture — is the same
    /// setting over any check's pack.
    ///
    /// There is no model-units setting. A finding's point and every length fact are metres at full
    /// precision, whichever check produced it and whether it was read from a document or a file,
    /// so a camera in metres needs no conversion.
    /// </summary>
    public sealed class FindingBcfOptions
    {
        /// <summary>Which schema to write.</summary>
        public BcfVersion Version { get; set; } = BcfVersion.V21;

        /// <summary>Which document this came from, for the project record and the descriptions.</summary>
        public string? SourceFile { get; set; }

        /// <summary>The project's name, for <c>project.bcfp</c>.</summary>
        public string? ProjectName { get; set; }

        /// <summary>When the file was written, in ticks. Zero means now.</summary>
        public long WhenTicks { get; set; }

        /// <summary>Who is writing it. Every topic needs an author and BCF will not invent one.</summary>
        public string Author { get; set; } = "CamelWorks";

        /// <summary>
        /// Whether the group each finding is filed under travels as a label.
        ///
        /// A reviewer who is going to work through the file wants the folders that took an
        /// afternoon to make; a reviewer who wants it in their own tool usually wants it flat.
        /// </summary>
        public bool KeepGroups { get; set; } = true;

        /// <summary>
        /// Which way each topic's camera looks at its finding.
        ///
        /// A reviewer works through a pack faster when every topic faces the same way, and which
        /// way that is depends on the job: a plan for an opening in a slab, an elevation for a
        /// riser.
        /// </summary>
        public BcfView View { get; set; } = BcfView.Standing;

        /// <summary>
        /// Whether the finding's elements are coloured in the viewpoint.
        ///
        /// On by default: red for the first element and amber for the rest, so a reviewer opening
        /// a clash can see which side is which without reading the description. Off for a
        /// receiving tool that treats a colouring as a permanent override on the model.
        /// </summary>
        public bool Highlight { get; set; } = true;

        /// <summary>
        /// Whether the viewpoint carries a section box around the finding's point.
        ///
        /// A camera on a breach inside a plant room shows the wall in front of it. The box is the
        /// point plus <see cref="SectionMetres"/> each way.
        /// </summary>
        public bool Section { get; set; }

        /// <summary>How far the section box reaches from the point, in metres.</summary>
        public double SectionMetres { get; set; } = 2.0;

        /// <summary>
        /// Renders one finding as a PNG, or returns null.
        ///
        /// A callback rather than an image, because rendering needs the host. This assembly has no
        /// Navisworks in it and is tested on a machine with no graphics, so the picture is
        /// requested rather than taken here.
        ///
        /// Worth requesting because every coordination tool shows its issue list as thumbnails. A
        /// pack with no snapshots arrives as rows of grey boxes and a reviewer has to open each
        /// one to find out what it is, which on four hundred findings decides whether the file is
        /// worked through or closed.
        ///
        /// Null is an ordinary answer: a document that cannot render off-screen, a file source
        /// with no model behind it, or a reviewer who asked for no pictures.
        /// </summary>
        public Func<Finding, byte[]?>? Snapshot { get; set; }

        /// <summary>
        /// The group a finding is filed under in this pack, when that is not the group the
        /// finding itself carries.
        ///
        /// A pack is written from a board, and a board's folders are made by the grouping rules
        /// rather than by the host: the finding's own group is what the host filed it under.
        /// Null answers nothing and the finding's own group travels.
        /// </summary>
        public Func<Finding, string?>? GroupOf { get; set; }

        /// <summary>
        /// Where an element sits in the writing document, so the reading seat can reach it
        /// without searching for it.
        ///
        /// An element key is a hash, and the one route from a hash back to the element is to key
        /// every element in the federation until one matches. The writing seat has the model open
        /// and can spare the reader that, so what it knows travels in the sidecar. Null answers
        /// nothing and the reader falls back to the key.
        /// </summary>
        public Func<ElementKey, ElementLocator>? LocatorOf { get; set; }

        /// <summary>
        /// The IFC GlobalId for an element, so a receiving tool can select it.
        ///
        /// Null answers nothing, and the element's own key is tried instead — which is a GlobalId
        /// exactly when the federation was built from IFC.
        /// </summary>
        public Func<ElementKey, string?>? IfcGuidOf { get; set; }

        /// <summary>
        /// The defaults for one check, by its key.
        ///
        /// Only the framing differs, and only where the check's own shape makes a different
        /// framing the right one: a clearance breach is an area somebody walks under, so it is
        /// sent boxed; a fall hazard is an opening in a floor, which reads from above and nowhere
        /// else. Every other check takes the standing view a clash is sent in.
        /// </summary>
        /// <param name="checkKey">The check's key, as <see cref="FindingSource"/> spells it.</param>
        public static FindingBcfOptions For(string? checkKey)
        {
            var options = new FindingBcfOptions();

            switch ((checkKey ?? string.Empty).Trim())
            {
                case FindingSource.Headroom:
                    options.Section = true;
                    options.SectionMetres = 3.0;
                    break;

                case FindingSource.FallHazard:
                    options.View = BcfView.Plan;
                    break;
            }

            return options;
        }
    }

    /// <summary>A written pack, in memory.</summary>
    public sealed class FindingBcfDocument
    {
        internal FindingBcfDocument(byte[] bytes, int findings, int issues, int snapshots,
                                    int blind, int partial, IReadOnlyList<string> warnings)
        {
            Bytes = bytes; Findings = findings; Issues = issues; Snapshots = snapshots;
            Blind = blind; Partial = partial; Warnings = warnings;
        }

        /// <summary>The .bcfzip.</summary>
        public byte[] Bytes { get; }

        /// <summary>How many findings went into it, one topic each.</summary>
        public int Findings { get; }

        /// <summary>How many issues went into it, one topic each.</summary>
        public int Issues { get; }

        /// <summary>How many topics carry a picture.</summary>
        public int Snapshots { get; }

        /// <summary>
        /// Findings whose elements can be found by no other tool.
        ///
        /// A component is resolvable only when it carries an IFC GlobalId, because that is the one
        /// id another tool looks up. A federation assembled from RVT and DWG usually carries none,
        /// and a pack full of such topics opens in the receiving tool without complaint and
        /// selects nothing — so this is counted and said out loud rather than left to be
        /// discovered by the person the file was sent to.
        /// </summary>
        public int Blind { get; }

        /// <summary>Findings where some of the elements can be found, but not all.</summary>
        public int Partial { get; }

        /// <summary>Findings whose elements another tool can select.</summary>
        public int Resolvable => Findings - Blind - Partial;

        /// <summary>Anything worth saying about what was written.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Findings == 0 && Issues == 0
                ? "Nothing to write."
                : Measure.Count(Findings) + (Findings == 1 ? " finding" : " findings")
                  + (Issues == 0
                      ? "."
                      : " and " + Measure.Count(Issues) + (Issues == 1 ? " issue." : " issues."));

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// One topic's row in the sidecar: what BCF has no field for.
    ///
    /// The identity a finding is rebuilt from and the producer's own numbers. A file that has been
    /// through another tool comes back without any of this, and the topics still read.
    /// </summary>
    public sealed class FindingRecord
    {
        internal FindingRecord(string topicGuid, string? findingId, string? source, string? rule,
                               string? discriminator, IReadOnlyList<ElementLocator> elements,
                               double? x, double? y, double? z,
                               IReadOnlyDictionary<string, string?> facts)
        {
            TopicGuid = topicGuid; FindingId = findingId; Source = source; Rule = rule;
            Discriminator = discriminator; Located = elements; X = x; Y = y; Z = z; Facts = facts;
        }

        /// <summary>The topic this row is about.</summary>
        public string TopicGuid { get; }

        /// <summary>The finding's derived id, as the writing seat held it.</summary>
        public string? FindingId { get; }

        /// <summary>Which check produced it.</summary>
        public string? Source { get; }

        /// <summary>The rule or run that found it.</summary>
        public string? Rule { get; }

        /// <summary>What made it distinct from another finding by the same rule on the same elements.</summary>
        public string? Discriminator { get; }

        /// <summary>The elements it points at, and where the writing document had each.</summary>
        public IReadOnlyList<ElementLocator> Located { get; }

        /// <summary>The elements it points at.</summary>
        public IReadOnlyList<ElementKey> Elements =>
            Located.Select(one => one.Key).ToList();

        /// <summary>Where it is, in metres, or null when the producer gave no point.</summary>
        public double? X { get; }

        /// <inheritdoc cref="X"/>
        public double? Y { get; }

        /// <inheritdoc cref="X"/>
        public double? Z { get; }

        /// <summary>The producer's own numbers, verbatim.</summary>
        public IReadOnlyDictionary<string, string?> Facts { get; }

        /// <inheritdoc />
        public override string ToString() => (Source ?? "?") + " " + (Rule ?? "?");
    }

    /// <summary>A pack, read.</summary>
    public sealed class FindingPackRead
    {
        internal FindingPackRead(BcfVersion version, IReadOnlyList<BcfTopic> topics,
                                 IReadOnlyDictionary<string, FindingRecord> records,
                                 IReadOnlyList<string> warnings, string? problem)
        {
            Version = version; Topics = topics; Records = records;
            Warnings = warnings; Problem = problem;
        }

        /// <summary>Which schema the file was written to.</summary>
        public BcfVersion Version { get; }

        /// <summary>Every topic the file carried.</summary>
        public IReadOnlyList<BcfTopic> Topics { get; }

        /// <summary>The sidecar rows, by topic guid. Empty for a file written by another tool.</summary>
        public IReadOnlyDictionary<string, FindingRecord> Records { get; }

        /// <summary>Anything worth saying about what was read.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem { get; }

        /// <summary>True when the file gave up something.</summary>
        public bool IsUsable => Problem == null && Topics.Count > 0;

        /// <summary>The row for one topic, or null when the file carried none.</summary>
        /// <param name="topicGuid">The topic's guid.</param>
        public FindingRecord? Record(string? topicGuid) =>
            topicGuid != null && Records.TryGetValue(topicGuid, out var found) ? found : null;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Problem != null) return Problem;
            if (Topics.Count == 0) return "That file holds no topics.";

            var line = Measure.Count(Topics.Count)
                     + (Topics.Count == 1 ? " topic" : " topics");

            if (Records.Count == 0) return line + ", written by another tool.";

            return line + ", " + Measure.Count(Records.Count) + " of them ours.";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
