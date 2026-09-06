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

        // IsDisputed and Settled are read six times over in the course of drawing one row —
        // Settled reads IsDisputed, Would and Changes each read Settled, Explain reads all three.
        // Cached, and thrown away by the two things that can change the answer.
        private bool? _disputed;
        private ExchangeSay? _settled;
        private bool _settledKnown;

        private void Restate()
        {
            _disputed = null;
            _settled = null;
            _settledKnown = false;
        }

        /// <summary>
        /// Whose answer is taken when the parties disagree, or null for none yet.
        ///
        /// A disputed row starts with nobody chosen and is not applied. Picking one silently is
        /// how a clash somebody closed re-opens itself a week later with nobody able to say who
        /// did it.
        /// </summary>
        public string? Chosen
        {
            get => _chosen;
            set { _chosen = value; Restate(); }
        }

        private string? _chosen;

        /// <summary>
        /// What the coordinator decided, in their own words.
        ///
        /// Three parties disagreeing is ordinary, and the resolution is usually a note and an
        /// action rather than a winner: "Structure is right, mechanical to re-route, agreed on
        /// Thursday". Kept on the row and carried onto the finding, because a decision with no
        /// reasoning is one the same three people argue about again next week.
        /// </summary>
        public string? Note { get; set; }

        /// <summary>How many parties answered this clash.</summary>
        public int Parties => _answers.Count;

        /// <summary>
        /// True when more than two parties answered and they do not agree.
        ///
        /// Told apart from a two-sided dispute because the screen has to offer more than a choice
        /// between two names, and because the losing answers are worth keeping in front of the
        /// person deciding.
        /// </summary>
        public bool IsThreeWay => IsDisputed && _answers.Count > 2;

        /// <summary>
        /// The answers that were not taken, so the screen can keep them readable.
        ///
        /// A resolution that hides what the other parties said is one nobody can check.
        /// </summary>
        public IReadOnlyList<ExchangeSay> Overruled
        {
            get
            {
                var settled = Settled;

                return settled == null
                    ? Array.Empty<ExchangeSay>()
                    : _answers.Where(one => !ReferenceEquals(one, settled)).ToList();
            }
        }

        /// <summary>True when two parties answered this differently.</summary>
        public bool IsDisputed => _disputed ??= Disagrees();

        private bool Disagrees()
        {
            if (_answers.Count < 2) return false;

            var status = _answers[0].Incoming;
            var party = _answers.Select(one => one.AssignedTo).FirstOrDefault(one => one != null);

            foreach (var say in _answers)
            {
                if (say.Incoming != status) return true;

                if (say.AssignedTo != null
                    && !string.Equals(say.AssignedTo, party, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>The answer that would be applied, or null when there is none to apply.</summary>
        public ExchangeSay? Settled
        {
            get
            {
                if (_settledKnown) return _settled;

                _settled = Decided();
                _settledKnown = true;

                return _settled;
            }
        }

        private ExchangeSay? Decided()
        {
            if (_answers.Count == 0) return null;

            if (Chosen != null)
                return _answers.FirstOrDefault(
                    one => string.Equals(one.Party, Chosen, StringComparison.OrdinalIgnoreCase));

            return IsDisputed ? null : _answers[0];
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

        internal void Add(ExchangeSay say)
        {
            _answers.Add(say);
            Restate();
        }

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

        /// <summary>How many rows more than two parties answered differently.</summary>
        public int ThreeWay => Rows.Count(one => one.IsThreeWay);

        /// <summary>How many rows still need somebody to pick a side.</summary>
        public int Undecided => Rows.Count(one => one.IsDisputed && one.Chosen == null);

        /// <summary>How many rows would move the board as they stand.</summary>
        public int Changes => Rows.Count(one => one.IsReady && one.Changes);

        /// <summary>How many comments came back.</summary>
        public int Comments => Rows.Sum(one => one.Answers.Sum(say => say.Comments.Count));

        /// <summary>
        /// Every status word the packs used, where each lands, and how many topics said it.
        ///
        /// BCF does not fix its status list, so a trade returning forty topics marked "Rejected"
        /// is ordinary. The screen shows this before Apply: the mapping was applied silently, and
        /// a coordinator had no way to see that forty answers had become Active.
        /// </summary>
        public IReadOnlyList<ClashStatusWord> StatusWords => _words ??= ClashStatusMap.Tally(
            Packs.Where(pack => pack?.Read != null)
                 .SelectMany(pack => pack.Read!.Topics)
                 .Where(topic => topic != null)
                 .Select(topic => topic.TopicStatus));

        /// <summary>The words the conventional vocabulary does not hold.</summary>
        public IReadOnlyList<ClashStatusWord> Guessed =>
            StatusWords.Where(word => word.IsGuess).ToList();

        private IReadOnlyList<ClashStatusWord>? _words;

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
        internal ExchangeApplied(int moved, int assigned, int comments, int held, int missed = 0)
        {
            Moved = moved; Assigned = assigned; Comments = comments; Held = held; Missed = missed;
        }

        /// <summary>How many clashes changed status.</summary>
        public int Moved { get; }

        /// <summary>How many were assigned to somebody.</summary>
        public int Assigned { get; }

        /// <summary>How many comments were written onto clashes.</summary>
        public int Comments { get; }

        /// <summary>How many rows were left where they were, disputed or untaken.</summary>
        public int Held { get; }

        /// <summary>
        /// How many taken rows named a clash the register does not hold.
        ///
        /// Reported rather than swallowed: the answer went nowhere, and a screen that said
        /// "14 moved" over a register that took none of them is the failure this counts.
        /// </summary>
        public int Missed { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Moved == 0 && Assigned == 0 && Comments == 0 && Held == 0 && Missed == 0)
                return "Nothing to apply.";

            if (Moved == 0 && Assigned == 0 && Comments == 0)
                return "Nothing applied"
                     + (Held > 0 ? "; " + Measure.Count(Held) + " left unchanged" : string.Empty)
                     + (Missed > 0
                         ? "; " + Measure.Count(Missed) + " named a clash the register does not hold"
                         : string.Empty)
                     + ".";

            var parts = new List<string>();

            if (Moved > 0) parts.Add(Measure.Count(Moved) + " moved");
            if (Assigned > 0) parts.Add(Measure.Count(Assigned) + " assigned");
            if (Comments > 0) parts.Add(Measure.Count(Comments) + " comments written");

            var line = string.Join(", ", parts);

            if (Held > 0) line += "; " + Measure.Count(Held) + " left unchanged";
            if (Missed > 0) line += "; " + Measure.Count(Missed) + " named a clash the register "
                                  + "does not hold";

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
    /// looked at first; <c>Apply</c> is the separate press.
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
                //
                // The pack's own result GUIDs, indexed once: asking each topic whether any result
                // carried its GUID was a scan of the results per topic, and a pack holds one of
                // each.
                var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var result in pack.Read.Results)
                    if (result?.FileGuid != null) answered.Add(result.FileGuid);

                foreach (var topic in pack.Read.Topics)
                    if (topic != null && !answered.Contains(topic.Guid)) raised.Add(topic);
            }

            // A disputed row starts with nobody chosen and untaken, so applying the reading as it
            // arrives cannot settle an argument by accident.
            foreach (var row in rows)
                if (row.IsDisputed) row.Take = false;

            return new ExchangeReading(all, rows, strangers, raised);
        }

        /// <summary>
        /// Write the taken rows onto the board only.
        ///
        /// The live board is a projection, so this alone does not outlive the tab. Callers with a
        /// register pass it; this overload is for tests and for a board with no project file.
        /// </summary>
        /// <param name="reading">What was read.</param>
        /// <param name="whenTicks">When this is happening, for the comments. Zero means now.</param>
        public static ExchangeApplied Apply(ExchangeReading? reading, long whenTicks = 0) =>
            Apply(reading, null, whenTicks);

        /// <summary>
        /// Write the taken rows onto the board and into the register.
        ///
        /// The register is where a decision lives. Writing only onto the <see cref="ClashItem"/>s
        /// meant six packs read, disputes settled by hand and "14 moved, 9 assigned, 31 comments
        /// written" reported — and the assignees and the comments gone when the tab was left,
        /// because the Manage tab keeps findings and had never seen those items.
        ///
        /// Each field written is held (<see cref="Finding.Hold"/>), which is what makes it survive
        /// the next read of the host and what offers it to the host writer. A field written and not
        /// held is reverted by the next press of "Read the results".
        ///
        /// A row reaches its finding by the topic GUID the key derives, which is how a markup
        /// coming back from another tool finds its way home. Every exact match is made before any
        /// neighbouring cell is tried, and a finding is claimed at most once: a duct that crosses
        /// one beam twice is two clashes, and a neighbour match that took the finding another row
        /// needed exactly would write one party's answer onto the other conflict. A row that
        /// reaches nothing lands on the board and is counted in
        /// <see cref="ExchangeApplied.Missed"/> rather than reported as applied.
        /// </summary>
        /// <param name="reading">What was read.</param>
        /// <param name="register">The register to write into, or null for the board alone.</param>
        /// <param name="whenTicks">When this is happening, for the comments. Zero means now.</param>
        public static ExchangeApplied Apply(ExchangeReading? reading, FindingRegister? register,
                                            long whenTicks = 0)
        {
            if (reading == null) return new ExchangeApplied(0, 0, 0, 0);

            var when = whenTicks > 0 ? whenTicks : DateTime.UtcNow.Ticks;
            var taking = reading.Rows.Where(row => row.Take && row.Settled != null).ToList();
            var held = reading.Rows.Count - taking.Count;

            var matcher = register == null ? null : new Matcher(register);
            var found = new Dictionary<ExchangeRow, Finding>();

            if (matcher != null)
            {
                foreach (var row in taking)
                {
                    var exact = matcher.Exact(row.Item);

                    if (exact != null) found[row] = exact;
                }

                foreach (var row in taking)
                {
                    if (found.ContainsKey(row)) continue;

                    var near = matcher.Near(row.Item);

                    if (near != null) found[row] = near;
                }
            }

            var moved = 0;
            var assigned = 0;
            var comments = 0;
            var missed = 0;

            foreach (var row in taking)
            {
                var settled = row.Settled!;
                var would = row.Would;

                found.TryGetValue(row, out var finding);

                if (matcher != null && finding == null) missed++;

                // The board is a projection, so it takes the write whether or not the register
                // could be reached: an answer nobody can see on screen is worse than one that is
                // only on screen, and Missed says which happened.
                var boardMoved = would != row.Item.Status;

                if (boardMoved) row.Item.Status = would;

                var boardAssigned = settled.AssignedTo != null
                    && !string.Equals(row.Item.AssignedTo, settled.AssignedTo,
                                      StringComparison.OrdinalIgnoreCase);

                if (boardAssigned) row.Item.AssignedTo = settled.AssignedTo;

                if (finding != null)
                {
                    var landed = FindingStatusLattice.Merge(finding.Status, would);

                    if (landed != finding.Status)
                    {
                        finding.Status = landed;
                        finding.Hold(FindingField.Status);
                        moved++;
                    }

                    if (settled.AssignedTo != null
                        && !string.Equals(finding.AssignedTo, settled.AssignedTo,
                                          StringComparison.OrdinalIgnoreCase))
                    {
                        finding.AssignedTo = settled.AssignedTo;
                        finding.Hold(FindingField.AssignedTo);
                        assigned++;
                    }

                    // What the reviewer set beyond the status: a due date, a priority, a retitle
                    // and their own labels. These are on the returned result and were being read
                    // by the Manage tab's sync and dropped by this one.
                    Reviewed(finding, settled.Result);

                    // What the coordinator decided, in their own words, alongside what the parties
                    // said. Three parties disagreeing is ordinary and the resolution is usually a
                    // note rather than a winner.
                    if (!string.IsNullOrWhiteSpace(row.Note))
                    {
                        var decided = new FindingComment("The coordinator", when, row.Note!.Trim());

                        if (!finding.Comments.Any(one => one.Repeats(decided)))
                        {
                            finding.Comments.Add(decided);
                            finding.Hold(FindingField.Comments);
                            comments++;
                        }
                    }
                }
                else if (matcher == null)
                {
                    // No register at all: the board is the only record there is, so it is what the
                    // counts describe. A register that simply does not hold this clash is a miss,
                    // and a miss is never reported as applied.
                    if (boardMoved) moved++;
                    if (boardAssigned) assigned++;
                }

                // Every comment from every party, not only the one whose answer won. A comment is
                // somebody's reasoning, and dropping the losing side's is throwing away the thing
                // that would have settled the argument.
                foreach (var say in row.Answers)
                {
                    foreach (var comment in say.Comments)
                    {
                        if (comment == null) continue;

                        var already = row.Item.Comments.Any(one => Same(one, comment));

                        if (!already) row.Item.Comments.Add(comment);

                        if (finding == null)
                        {
                            if (!already && matcher == null) comments++;
                            continue;
                        }

                        var arriving = new FindingComment(
                            comment.Author,
                            comment.WhenTicks > 0 ? comment.WhenTicks : when,
                            comment.Body);

                        if (finding.Comments.Any(one => one.Repeats(arriving))) continue;

                        finding.Comments.Add(arriving);
                        finding.Hold(FindingField.Comments);
                        comments++;
                    }
                }
            }

            return new ExchangeApplied(moved, assigned, comments, held, missed);
        }

        /// <summary>
        /// Carry the reviewer's own fields onto the finding.
        ///
        /// The same fields the Manage tab's sync applies through <see cref="ClashAside"/>. Each is
        /// held, so it reaches the host on the next write rather than being reverted by the next
        /// read.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="result">The returned result.</param>
        private static void Reviewed(Finding finding, ClashPackResult result)
        {
            if (result.DueTicks > 0)
                finding.DueDayNumber =
                    Finding.DayNumberFor(new DateTime(result.DueTicks, DateTimeKind.Utc));

            if (!string.IsNullOrWhiteSpace(result.Priority)) finding.Priority = result.Priority;
            if (!string.IsNullOrWhiteSpace(result.Stage)) finding.Stage = result.Stage;

            foreach (var label in result.Labels)
                if (!string.IsNullOrWhiteSpace(label)
                    && !finding.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    finding.Labels.Add(label);
        }

        /// <summary>
        /// The way from a board clash back to the finding that records it.
        ///
        /// Findings are indexed under both the topic GUID they carry and the one their own
        /// elements and cell derive, because the two are not always the same: a pack synced on the
        /// Manage tab first stamps the returning file's GUID onto the finding, and a finding
        /// indexed only under that is unreachable from the board.
        ///
        /// A finding is claimed once. Findings this run reported come before ones it did not, and
        /// a finding whose rule is the row's test comes before one that is not, so two clashes on
        /// one pair land the way a person would sort them.
        /// </summary>
        private sealed class Matcher
        {
            private readonly Dictionary<string, List<Finding>> _byGuid =
                new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);

            internal Matcher(FindingRegister register)
            {
                foreach (var finding in register.From(FindingSource.Clash))
                {
                    if (!string.IsNullOrEmpty(finding.TopicGuid)) Under(finding.TopicGuid!, finding);

                    var derived = Derived(finding);

                    if (derived != null && !string.Equals(derived, finding.TopicGuid,
                                                          StringComparison.OrdinalIgnoreCase))
                        Under(derived, finding);
                }
            }

            /// <summary>The finding for this clash's own cell, or null.</summary>
            /// <param name="item">The clash on the board.</param>
            internal Finding? Exact(ClashItem item) =>
                Claim(ClashBcf.TopicGuidFor(item.Key), item.TestName);

            /// <summary>The finding for a cell beside this clash's, or null.</summary>
            /// <param name="item">The clash on the board.</param>
            internal Finding? Near(ClashItem item)
            {
                foreach (var guid in ClashBcf.NearbyTopicGuidsFor(item.Key))
                {
                    var found = Claim(guid, item.TestName);

                    if (found != null) return found;
                }

                return null;
            }

            private void Under(string guid, Finding finding)
            {
                if (!_byGuid.TryGetValue(guid, out var held))
                {
                    held = new List<Finding>();
                    _byGuid[guid] = held;
                }

                if (!held.Contains(finding)) held.Add(finding);
            }

            private Finding? Claim(string guid, string? testName)
            {
                if (!_byGuid.TryGetValue(guid, out var held)) return null;

                Finding? best = null;
                var bestRank = int.MinValue;

                foreach (var finding in held)
                {
                    if (_claimed.Contains(finding.Id)) continue;

                    var rank = (finding.IsPresent ? 2 : 0)
                             + (testName != null && string.Equals(finding.Rule, testName,
                                                                  StringComparison.OrdinalIgnoreCase)
                                 ? 1 : 0);

                    // Ordinal by id breaks a tie, so the same register answers the same way twice.
                    if (best != null
                        && (rank < bestRank
                            || (rank == bestRank
                                && string.CompareOrdinal(finding.Id, best.Id) >= 0)))
                        continue;

                    best = finding;
                    bestRank = rank;
                }

                if (best != null) _claimed.Add(best.Id);

                return best;
            }

            /// <summary>
            /// The topic GUID a finding's own elements and cell derive, or null when it does not
            /// carry the two elements and the cell a clash key is made of.
            /// </summary>
            /// <param name="finding">The finding.</param>
            private static string? Derived(Finding finding)
            {
                if (finding.Elements.Count != 2 || string.IsNullOrEmpty(finding.Discriminator))
                    return null;

                var a = finding.Elements[0].ToString();
                var b = finding.Elements[1].ToString();

                // Canonical order, as ClashKey.Create writes it: the finding's element list is in
                // the order the host reported the pair.
                if (string.CompareOrdinal(a, b) > 0) { var swap = a; a = b; b = swap; }

                return ClashKey.TryParse(a + "|" + b + "|" + finding.Discriminator, out var key)
                    ? ClashBcf.TopicGuidFor(key)
                    : null;
            }
        }

        /// <summary>Whether two comments are the same one, so a pack read twice does not double them.</summary>
        /// <param name="held">What the clash already carries.</param>
        /// <param name="arriving">What came back.</param>
        private static bool Same(ClashComment held, ClashComment arriving) =>
            string.Equals(held.Author, arriving.Author, StringComparison.OrdinalIgnoreCase)
            && string.Equals(held.Body, arriving.Body, StringComparison.Ordinal)
            && held.WhenTicks == arriving.WhenTicks;
    }
}
