using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Clash
{
    /// <summary>One service through one piece of structure.</summary>
    public sealed class PenetrationLine
    {
        /// <summary>Create a line.</summary>
        /// <param name="group">The group it belongs to, which is how it reads on the board.</param>
        /// <param name="service">The service, as the model names it.</param>
        /// <param name="host">The structure it goes through.</param>
        public PenetrationLine(string group, string service, string host)
        {
            Group = group ?? string.Empty;
            Service = string.IsNullOrWhiteSpace(service) ? "(unnamed)" : service.Trim();
            Host = string.IsNullOrWhiteSpace(host) ? "(unnamed)" : host.Trim();
        }

        /// <summary>The group it belongs to.</summary>
        public string Group { get; }

        /// <summary>The service, as the model names it.</summary>
        public string Service { get; }

        /// <summary>The service's size, as the model states it. Never derived.</summary>
        public string? Size { get; set; }

        /// <summary>
        /// Which system the service belongs to.
        ///
        /// Named <c>SystemName</c> and not <c>System</c>: a property that shadows the root
        /// namespace inside its own class is a trap for whoever edits the file next.
        /// </summary>
        public string? SystemName { get; set; }

        /// <summary>The structure it goes through.</summary>
        public string Host { get; }

        /// <summary>The host's type, as the model states it.</summary>
        public string? HostType { get; set; }

        /// <summary>Which level it is on.</summary>
        public string? Level { get; set; }

        /// <summary>Who is responsible.</summary>
        public string? Party { get; set; }

        /// <summary>Where it is, X, in metres.</summary>
        public double X { get; set; }

        /// <summary>Where it is, Y, in metres.</summary>
        public double Y { get; set; }

        /// <summary>Where it is, Z, in metres.</summary>
        public double Z { get; set; }

        /// <summary>Where the schedule stands on this one.</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <inheritdoc />
        public override string ToString() => Service + " through " + Host;
    }

    /// <summary>Every penetration on the board, as a schedule.</summary>
    public sealed class PenetrationSchedule
    {
        internal PenetrationSchedule(IReadOnlyList<PenetrationLine> lines, int considered)
        {
            Lines = lines;
            Considered = considered;
        }

        /// <summary>One per penetration, level then service.</summary>
        public IReadOnlyList<PenetrationLine> Lines { get; }

        /// <summary>How many board results were looked at to find them.</summary>
        public int Considered { get; }

        /// <summary>True when nothing on the board reads as a service through structure.</summary>
        public bool IsEmpty => Lines.Count == 0;

        /// <summary>The schedule, as a report or a spreadsheet prints it.</summary>
        /// <param name="units">How this project writes a length.</param>
        public TableBlock Table(Measure? units = null)
        {
            var measure = units ?? Measure.Metric;
            var axis = " (" + measure.LengthUnit + ")";

            var table = new TableBlock("Level", "Service", "Size", "System", "Through", "Host type",
                                       "X" + axis, "Y" + axis, "Z" + axis, "Party", "Status");

            foreach (var line in Lines)
                table.Row(line.Level ?? string.Empty,
                          line.Service,
                          line.Size ?? Measure.Nothing,
                          line.SystemName ?? Measure.Nothing,
                          line.Host,
                          line.HostType ?? Measure.Nothing,
                          measure.Bare(line.X), measure.Bare(line.Y), measure.Bare(line.Z),
                          line.Party ?? PartyLedger.Nobody,
                          FindingStatusLattice.ToName(line.Status));

            return table;
        }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsEmpty)
                return "nothing on the board reads as a service through structure, out of "
                     + Measure.Count(Considered) + " looked at";

            var levels = Lines.Select(l => l.Level ?? string.Empty)
                              .Distinct(StringComparer.OrdinalIgnoreCase).Count();

            var sized = Lines.Count(l => !string.IsNullOrWhiteSpace(l.Size));

            var s = Measure.Count(Lines.Count) + " penetrations across " + Measure.Count(levels)
                  + (levels == 1 ? " level" : " levels");

            if (sized < Lines.Count)
                s += " - " + Measure.Count(Lines.Count - sized) + " with no size on the service";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Services through structure, pulled out of the board as a schedule.
    ///
    /// On a concrete frame the pour date is the one immovable date on the programme, and the sleeve
    /// schedule is what has to be right before it. It is normally produced by a person reading a
    /// clash board and retyping it into a spreadsheet — which is the same list, one transcription
    /// error later.
    ///
    /// <b>What it deliberately does not do.</b> No derived opening size, no inference of host
    /// thickness, no request-to-approved-to-cast state machine. Every one of those is a number this
    /// tool would be guessing, on a drawing somebody cuts concrete from. The schedule carries what
    /// the model actually says — the service, its stated size and system, the host and its type,
    /// where it is, and one status — and leaves the engineering to the engineer.
    /// </summary>
    public static class Penetrations
    {
        private static readonly string[] ServiceWords =
        {
            "duct", "pipe", "conduit", "cable", "tray", "sprinkler", "trunking", "busbar",
        };

        // "Structural Framing" is the category a beam arrives in from every Revit export on earth,
        // so the word that matters is "framing" and not "beam".
        private static readonly string[] StructureWords =
        {
            "wall", "slab", "floor", "beam", "column", "foundation", "footing", "roof", "deck",
            "framing", "truss", "joist",
        };

        /// <summary>True when a category or type reads as something that runs through a building.</summary>
        /// <param name="category">The element's category.</param>
        /// <param name="type">The element's type name.</param>
        public static bool IsService(string? category, string? type = null) =>
            Says(category, ServiceWords) || Says(type, ServiceWords);

        /// <summary>True when a category or type reads as something a service would have to go through.</summary>
        /// <param name="category">The element's category.</param>
        /// <param name="type">The element's type name.</param>
        public static bool IsStructure(string? category, string? type = null) =>
            Says(category, StructureWords) || Says(type, StructureWords);

        /// <summary>
        /// Which way round a clash reads, or null when it is not a penetration at all.
        ///
        /// A duct through a wall is a penetration; a duct through a duct is a clash, and a wall
        /// through a wall is a modelling error. Only the mixed pair qualifies, and the answer says
        /// which side is the service so the caller does not have to work it out twice.
        /// </summary>
        /// <param name="categoryA">The first element's category.</param>
        /// <param name="typeA">The first element's type name.</param>
        /// <param name="categoryB">The second element's category.</param>
        /// <param name="typeB">The second element's type name.</param>
        /// <returns>True when A is the service, false when B is, null when neither reading holds.</returns>
        public static bool? ServiceIsA(string? categoryA, string? typeA, string? categoryB, string? typeB)
        {
            var aService = IsService(categoryA, typeA);
            var bService = IsService(categoryB, typeB);
            var aStructure = IsStructure(categoryA, typeA);
            var bStructure = IsStructure(categoryB, typeB);

            if (aService && bStructure && !aStructure) return true;
            if (bService && aStructure && !bStructure) return false;

            return null;
        }

        /// <summary>Order the lines the way a schedule is read: by level, then by service.</summary>
        /// <param name="lines">The lines, in any order.</param>
        /// <param name="considered">How many board results were looked at to find them.</param>
        public static PenetrationSchedule Of(IEnumerable<PenetrationLine>? lines, int considered = 0)
        {
            var ordered = (lines ?? Array.Empty<PenetrationLine>())
                .Where(l => l != null)
                .OrderBy(l => string.IsNullOrWhiteSpace(l.Level) ? 1 : 0)
                .ThenBy(l => l.Level ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Service, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Host, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PenetrationSchedule(ordered, Math.Max(considered, ordered.Count));
        }

        private static bool Says(string? text, string[] words)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            foreach (var word in words)
                if (text!.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }
    }
}
