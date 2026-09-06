using System;
using System.Collections.Generic;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// A held finding, as the <see cref="ClashItem"/> the rules pipeline and the Coordinate tabs
    /// consume.
    ///
    /// The board used to read the host twice: once through the check, into findings, and once
    /// directly, into items. Two reads of one document disagree the moment anything is decided
    /// between them, and only one of them carried what somebody had recorded. So the register is
    /// the spine for state and identity, and the item is derived from it.
    ///
    /// What an item holds that a finding does not is supplied here rather than looked up: the
    /// level, the disciplines and the model display names come from the seat, and the projection
    /// takes them as lookups so it stays a pure function and can be asserted against a host read.
    ///
    /// Nothing here resolves an element key. Everything the item needs about a participant is
    /// either in the key itself — the model scope — or was written onto the finding by the producer
    /// while it was holding the element. Resolving a key builds an index of every geometry leaf in
    /// the federation, and a projection over eighteen thousand findings is the worst place in the
    /// product to pay for one.
    /// </summary>
    public sealed class ClashProjection
    {
        private readonly double _metresPerUnit;
        private readonly double _cell;

        /// <summary>Set up a projection for one document.</summary>
        /// <param name="metresPerUnit">
        /// How many metres one model unit is. A finding's point is in metres and the item's key is
        /// quantised in model units, which is the only place the two spines differ, so the scale
        /// is undone once here and nowhere else.
        /// </param>
        /// <param name="cell">The cell size the keys are made with.</param>
        public ClashProjection(double metresPerUnit, double cell = ClashKey.DefaultCell)
        {
            _metresPerUnit = metresPerUnit > 0 && !double.IsNaN(metresPerUnit)
                             && !double.IsInfinity(metresPerUnit)
                ? metresPerUnit : 1.0;

            _cell = cell > 0 && !double.IsNaN(cell) && !double.IsInfinity(cell)
                ? cell : ClashKey.DefaultCell;
        }

        /// <summary>The display name of the model an element is in. Null answers nothing.</summary>
        public Func<ElementKey, string?>? ModelName { get; set; }

        /// <summary>The discipline an element belongs to. Null answers nothing.</summary>
        public Func<ElementKey, string?>? Discipline { get; set; }

        /// <summary>The level a height in metres falls on. Null answers nothing.</summary>
        public Func<double, string?>? Level { get; set; }

        /// <summary>The folder a host test sits in, by the host's id for it. Null answers nothing.</summary>
        public Func<string?, string?>? Folder { get; set; }

        /// <summary>
        /// One finding as an item, or null when it is not a clash between two resolved elements.
        ///
        /// Null rather than an item with an empty key: a result the adapter could not resolve both
        /// sides of has no clash key at all, and the rules, the grouping and the carry-over all
        /// index on one.
        /// </summary>
        /// <param name="finding">The held finding.</param>
        public ClashItem? Item(Finding? finding)
        {
            if (finding == null || finding.Elements.Count != 2) return null;

            var a = finding.Elements[0];
            var b = finding.Elements[1];

            if (a.IsEmpty || b.IsEmpty) return null;

            // Metres on the finding, model units in the key. Divided rather than multiplied by the
            // reciprocal, and left alone on a metric document, so a metric seat quantises the same
            // number the host read quantised rather than one a rounding trip away from it.
            var x = finding.X ?? 0;
            var y = finding.Y ?? 0;
            var z = finding.Z ?? 0;

            var item = new ClashItem(ClashKey.Create(a, b, InUnits(x), InUnits(y), InUnits(z), _cell),
                                     finding.Rule)
            {
                FindingId = finding.Id,
                Name = finding.Title,
                TestFolder = Folder?.Invoke(Fact(finding, ClashFindings.HostTest)),
                X = x,
                Y = y,
                Z = z,
                Status = finding.Status,
                AssignedTo = finding.AssignedTo,
                TopicGuid = finding.TopicGuid,
                Distance = ClashFindings.DistanceMetres(finding),
                SuppressedBy = Fact(finding, ClashFindings.SuppressedBy),
                CategoryA = Fact(finding, ClashFindings.CategoryA),
                CategoryB = Fact(finding, ClashFindings.CategoryB),
                ModelA = ModelName?.Invoke(a),
                ModelB = ModelName?.Invoke(b),
                DisciplineA = Discipline?.Invoke(a),
                DisciplineB = Discipline?.Invoke(b),
                Level = Level?.Invoke(z),
            };

            // Only a group somebody made by hand carries. A derived name is re-derived by the rule
            // stack, and carrying it would freeze the board against its own rules.
            if (Fact(finding, ClashFindings.BoardGroupPinned) != null)
                item.CarriedGroup = Fact(finding, ClashFindings.BoardGroup);

            // What the host last said, as the board's two host columns. Absent on a seat that has
            // never read the host, which is the honest answer and what the board already draws.
            var status = Fact(finding, ClashFindings.HostStatus);
            var assignee = Fact(finding, ClashFindings.HostAssignee);

            if (status != null) item.Properties["Host/Status"] = status;
            if (assignee != null) item.Properties["Host/AssignedTo"] = assignee;

            foreach (var comment in finding.Comments)
                if (comment != null)
                    item.Comments.Add(new ClashComment(comment.Author, comment.WhenTicks,
                                                       comment.Body) { HostId = comment.HostId });

            if (finding.Priority != null || finding.Stage != null || finding.DueDayNumber != null)
                item.Review = new ClashAside(finding.TopicGuid ?? finding.Id)
                {
                    TestId = Fact(finding, ClashFindings.HostTest),
                    ResultId = Fact(finding, ClashFindings.HostId),
                    Priority = finding.Priority,
                    Stage = finding.Stage,
                    DueTicks = finding.DueDayNumber != null
                        ? Finding.DateOf(finding.DueDayNumber.Value).Ticks : 0,
                };

            return item;
        }

        /// <summary>
        /// Every finding that projects into an item, in the order they were given.
        /// </summary>
        /// <param name="findings">The held findings. Anything that is not a resolved pair is left out.</param>
        public IReadOnlyList<ClashItem> Items(IEnumerable<Finding>? findings)
        {
            var items = new List<ClashItem>();

            foreach (var finding in findings ?? Array.Empty<Finding>())
            {
                var item = Item(finding);

                if (item != null) items.Add(item);
            }

            return items;
        }

        /// <summary>A length in metres, back in model units.</summary>
        /// <param name="metres">The length.</param>
        private double InUnits(double metres) =>
            _metresPerUnit == 1.0 ? metres : metres / _metresPerUnit;

        /// <summary>A fact, or null when the finding does not carry one worth reading.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="name">The fact.</param>
        private static string? Fact(Finding finding, string name) =>
            finding.Facts.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)
                ? value : null;
    }
}
