using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Clash
{
    /// <summary>One returned pack: who sent it, and what was in it.</summary>
    public sealed class ExchangePack
    {
        /// <summary>Take a file as one party's return.</summary>
        /// <param name="party">Who sent it back.</param>
        /// <param name="read">The file, read.</param>
        /// <param name="fileName">What the file was called, for the readout.</param>
        public ExchangePack(string? party, ClashPackRead? read, string? fileName = null)
        {
            Party = string.IsNullOrWhiteSpace(party) ? "Somebody" : party!.Trim();
            Read = read;
            FileName = fileName;
        }

        /// <summary>Who sent it back.</summary>
        public string Party { get; }

        /// <summary>The file, read.</summary>
        public ClashPackRead? Read { get; }

        /// <summary>What the file was called.</summary>
        public string? FileName { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem => Read?.Problem ?? "The file could not be read.";

        /// <summary>How many results it carried.</summary>
        public int Results => Read?.Results.Count ?? 0;

        /// <inheritdoc />
        public override string ToString() => Party + " - " + (FileName ?? "unnamed pack");
    }

    /// <summary>What one party said about one clash.</summary>
    public sealed class ExchangeSay
    {
        internal ExchangeSay(string party, ClashPackResult result, FindingStatus current)
        {
            Party = party;
            Result = result;
            Incoming = result.Status;
            Current = current;
        }

        /// <summary>Who said it.</summary>
        public string Party { get; }

        /// <summary>The result as their file carried it.</summary>
        public ClashPackResult Result { get; }

        /// <summary>Where their file puts it.</summary>
        public FindingStatus Incoming { get; }

        /// <summary>Where the board has it.</summary>
        public FindingStatus Current { get; }

        /// <summary>Who their file says is responsible, or null.</summary>
        public string? AssignedTo =>
            string.IsNullOrWhiteSpace(Result.AssignedTo) ? null : Result.AssignedTo!.Trim();

        /// <summary>Their comments on it.</summary>
        public IReadOnlyList<ClashComment> Comments => Result.Comments;

        /// <summary>True when taking this would move the clash.</summary>
        public bool Changes => Incoming != Current || AssignedTo != null || Comments.Count > 0;

        /// <summary>How the answer reads.</summary>
        public string Explain()
        {
            var said = Party + " returned " + Incoming;

            if (AssignedTo != null) said += ", assigned to " + AssignedTo;

            if (Comments.Count > 0)
                said += ", " + Measure.Count(Comments.Count)
                      + (Comments.Count == 1 ? " comment" : " comments");

            return said + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>One clash, and everything that came back about it.</summary>
    public sealed class ExchangeRow
    {
        private readonly List<ExchangeSay> _answers = new List<ExchangeSay>();

        internal ExchangeRow(ClashItem item)
        {
            Item = item;
            Current = item.Status;
            Take = true;
        }

        /// <summary>The clash on the board.</summary>
        public ClashItem Item { get; }

        /// <summary>Where the board has it.</summary>
        public FindingStatus Current { get; }

        /// <summary>What each party said, in the order the packs were read.</summary>
        public IReadOnlyList<ExchangeSay> Answers => _answers;

        /// <summary>Whether this row is applied.</summary>
        public bool Take { get; set; }

        /// <summary>
        /// Whose answer is taken when the parties disagree, or null for none yet.
        ///
        /// A disputed row starts with nobody chosen and is not applied. Picking one silently is
        /// how a clash somebody closed re-opens itself a week later with nobody able to say who
        /// did it.
        /// </summary>
        public string? Chosen { get; set; }

        /// <summary>True when two parties answered this differently.</summary>
        public bool IsDisputed =>
            _answers.Select(one => one.Incoming).Distinct().Count() > 1
            || _answers.Select(one => one.AssignedTo)
                       .Where(one => one != null)
                       .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

        /// <summary>The answer that would be applied, or null when there is none to apply.</summary>
        public ExchangeSay? Settled
        {
            get
            {
                if (_answers.Count == 0) return null;

                if (Chosen != null)
                    return _answers.FirstOrDefault(
                        one => string.Equals(one.Party, Chosen, StringComparison.OrdinalIgnoreCase));

                return IsDisputed ? null : _answers[0];
            }
        }

        /// <summary>Where the clash would end up.</summary>
        public FindingStatus Would
        {
            get
            {
                var settled = Settled;

                if (settled == null) return Current;

                // The lattice, not the file. An explicit re-open is honoured downwards; anything
                // else is two edits neither author knew about, and promote-only is what stops a
                // stale "Open" undoing a real "Approved".
                return BcfResponse.Merge(Current, settled.Incoming,
                                         settled.Incoming == FindingStatus.Active
                                         && Current > FindingStatus.Active
                                         && settled.Result.Comments.Count > 0);
            }
        }

        /// <summary>True when applying this row would change the board.</summary>
        public bool Changes
        {
            get
            {
                var settled = Settled;

                if (settled == null) return false;

                return Would != Current
                       || settled.AssignedTo != null
                       || settled.Comments.Count > 0;
            }
        }

        /// <summary>True when the row is ready to be applied.</summary>
        public bool IsReady => Take && Settled != null;

        /// <summary>How the row reads in one line.</summary>
        public string Explain()
        {
            if (_answers.Count == 0) return "No pack answered this clash.";

            if (IsDisputed && Chosen == null)
                return "Conflict. " + string.Join(" ", _answers.Select(one => one.Explain()));

            var settled = Settled;

            if (settled == null) return "Nothing to apply.";

            return Would == Current
                ? settled.Party + " confirms " + Current + "."
                : settled.Party + " changes it from " + Current + " to " + Would + ".";
        }

        internal void Add(ExchangeSay say) => _answers.Add(say);

        /// <inheritdoc />
        public override string ToString() => Item.Name ?? Item.TestName;
    }

    /// <summary>What several returned packs would do to the board.</summary>
    public sealed class ExchangeReading
    {
        internal ExchangeReading(IReadOnlyList<ExchangePack> packs, IReadOnlyList<ExchangeRow> rows,
                                 IReadOnlyList<ClashPackResult> strangers,
                                 IReadOnlyList<BcfTopic> raised)
        {
            Packs = packs; Rows = rows; Strangers = strangers; Raised = raised;
        }

        /// <summary>The packs that were read.</summary>
        public IReadOnlyList<ExchangePack> Packs { get; }

        /// <summary>One row per clash anybody answered.</summary>
        public IReadOnlyList<ExchangeRow> Rows { get; }

        /// <summary>Results the packs carried that this board has no clash for.</summary>
        public IReadOnlyList<ClashPackResult> Strangers { get; }

        /// <summary>Topics that are not clashes at all - what a reviewer raised in their own tool.</summary>
        public IReadOnlyList<BcfTopic> Raised { get; }

        /// <summary>How many rows two parties answered differently.</summary>
        public int Disputed => Rows.Count(one => one.IsDisputed);

        /// <summary>How many rows still need somebody to pick a side.</summary>
        public int Undecided => Rows.Count(one => one.IsDisputed && one.Chosen == null);

        /// <summary>How many rows would move the board as they stand.</summary>
        public int Changes => Rows.Count(one => one.IsReady && one.Changes);

        /// <summary>How many comments came back.</summary>
        public int Comments => Rows.Sum(one => one.Answers.Sum(say => say.Comments.Count));

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Rows.Count == 0 && Strangers.Count == 0)
                return "These packs answer no clash on this board.";

            var parts = new List<string>
            {
                Measure.Count(Rows.Count) + (Rows.Count == 1 ? " clash returned" : " clashes returned"),
            };

            if (Disputed > 0) parts.Add(Measure.Count(Disputed) + " in conflict");
            if (Comments > 0) parts.Add(Measure.Count(Comments) + " comments");
            if (Strangers.Count > 0)
                parts.Add(Measure.Count(Strangers.Count) + " matching no clash on this board");
            if (Raised.Count > 0)
                parts.Add(Measure.Count(Raised.Count) + " raised as issues");

            return string.Join(", ", parts) + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>What applying the reading did.</summary>
    public sealed class ExchangeApplied
    {
        internal ExchangeApplied(int moved, int assigned, int comments, int held)
        {
            Moved = moved; Assigned = assigned; Comments = comments; Held = held;
        }

        /// <summary>How many clashes changed status.</summary>
        public int Moved { get; }

        /// <summary>How many were assigned to somebody.</summary>
        public int Assigned { get; }

        /// <summary>How many comments were written onto clashes.</summary>
        public int Comments { get; }

        /// <summary>How many rows were left where they were, disputed or untaken.</summary>
        public int Held { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Moved == 0 && Assigned == 0 && Comments == 0)
                return Held == 0
                    ? "Nothing to apply."
                    : "Nothing applied; " + Measure.Count(Held) + " left unchanged.";

            var parts = new List<string>();

            if (Moved > 0) parts.Add(Measure.Count(Moved) + " moved");
            if (Assigned > 0) parts.Add(Measure.Count(Assigned) + " assigned");
            if (Comments > 0) parts.Add(Measure.Count(Comments) + " comments written");

            var line = string.Join(", ", parts);

            if (Held > 0) line += "; " + Measure.Count(Held) + " left unchanged";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Several stakeholders' packs, read together and applied once.
    ///
    /// The other half of an issue going out. Six packs come back on Thursday, each holding the
    /// clashes that party was sent and a handful of them answered. Opening six files by hand and
    /// retyping the answers is why the answers stop coming back.
    ///
    /// Two parties answering the same clash differently is the case this exists for. They were
    /// sent the same pack at the same time and neither saw the other's answer, so the row is
    /// marked disputed, both answers are shown with the names on them, and nothing is applied
    /// until somebody picks a side.
    ///
    /// Nothing is applied by reading. <see cref="Read"/> works out what would happen so it can be
    /// looked at first; <see cref="Apply"/> is the separate press.
    /// </summary>
    public static class ClashExchange
    {
        /// <summary>Hold the returned packs up against the board.</summary>
        /// <param name="packs">The packs, one per party.</param>
        /// <param name="board">Every clash on the board.</param>
        public static ExchangeReading Read(IReadOnlyList<ExchangePack>? packs,
                                           IEnumerable<ClashItem>? board)
        {
            var all = (packs ?? Array.Empty<ExchangePack>()).Where(one => one != null).ToList();

            var here = new Dictionary<ClashKey, ClashItem>();

            foreach (var item in board ?? Array.Empty<ClashItem>())
                if (item != null) here[item.Key] = item;

            var rows = new List<ExchangeRow>();
            var byKey = new Dictionary<ClashKey, ExchangeRow>();
            var strangers = new List<ClashPackResult>();
            var raised = new List<BcfTopic>();

            foreach (var pack in all)
            {
                if (pack.Read == null) continue;

                foreach (var result in pack.Read.Results)
                {
                    if (result?.Item == null) { strangers.Add(result!); continue; }

                    if (!here.TryGetValue(result.Item.Key, out var mine))
                    {
                        // The pack names elements this federation does not have. Reported by
                        // count rather than dropped: a party working against last month's models
                        // is a thing the coordinator has to be told about.
                        strangers.Add(result);
                        continue;
                    }

                    if (!byKey.TryGetValue(mine.Key, out var row))
                    {
                        row = new ExchangeRow(mine);
                        byKey[mine.Key] = row;
                        rows.Add(row);
                    }

                    row.Add(new ExchangeSay(pack.Party, result, mine.Status));
                }

                // Topics that answer no clash at all - what a reviewer raised in their own tool
                // while they were in there. They belong in the issue register, not nowhere.
                foreach (var topic in pack.Read.Topics)
                    if (topic != null && !Answers(topic, pack.Read)) raised.Add(topic);
            }

            // A disputed row starts with nobody chosen and untaken, so applying the reading as it
            // arrives cannot settle an argument by accident.
            foreach (var row in rows)
                if (row.IsDisputed) row.Take = false;

            return new ExchangeReading(all, rows, strangers, raised);
        }

        /// <summary>Write the taken rows onto the board.</summary>
        /// <param name="reading">What was read.</param>
        /// <param name="whenTicks">When this is happening, for the comments. Zero means now.</param>
        public static ExchangeApplied Apply(ExchangeReading? reading, long whenTicks = 0)
        {
            if (reading == null) return new ExchangeApplied(0, 0, 0, 0);

            var when = whenTicks > 0 ? whenTicks : DateTime.UtcNow.Ticks;

            var moved = 0;
            var assigned = 0;
            var comments = 0;
            var held = 0;

            foreach (var row in reading.Rows)
            {
                var settled = row.Settled;

                if (!row.Take || settled == null) { held++; continue; }

                var would = row.Would;

                if (would != row.Item.Status)
                {
                    row.Item.Status = would;
                    moved++;
                }

                if (settled.AssignedTo != null
                    && !string.Equals(row.Item.AssignedTo, settled.AssignedTo,
                                      StringComparison.OrdinalIgnoreCase))
                {
                    row.Item.AssignedTo = settled.AssignedTo;
                    assigned++;
                }

                // Every comment from every party, not only the one whose answer won. A comment is
                // somebody's reasoning, and dropping the losing side's is throwing away the thing
                // that would have settled the argument.
                foreach (var say in row.Answers)
                {
                    foreach (var comment in say.Comments)
                    {
                        if (comment == null) continue;

                        if (row.Item.Comments.Any(one => Same(one, comment))) continue;

                        row.Item.Comments.Add(comment);
                        comments++;
                    }
                }

                _ = when;
            }

            return new ExchangeApplied(moved, assigned, comments, held);
        }

        /// <summary>Whether a topic is one of the clash results this file carried.</summary>
        /// <param name="topic">The topic.</param>
        /// <param name="read">The file it came from.</param>
        private static bool Answers(BcfTopic topic, ClashPackRead read) =>
            read.Results.Any(one => string.Equals(one.FileGuid, topic.Guid,
                                                  StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether two comments are the same one, so a pack read twice does not double them.</summary>
        /// <param name="held">What the clash already carries.</param>
        /// <param name="arriving">What came back.</param>
        private static bool Same(ClashComment held, ClashComment arriving) =>
            string.Equals(held.Author, arriving.Author, StringComparison.OrdinalIgnoreCase)
            && string.Equals(held.Body, arriving.Body, StringComparison.Ordinal)
            && held.WhenTicks == arriving.WhenTicks;
    }
}
