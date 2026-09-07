using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Clash results read out of a file rather than out of the document: a baseline a Manage seat
    /// exported, or a markup somebody sent back.
    ///
    /// The same check with five capabilities missing. A file is not a document: there is nothing to
    /// echo a decision into, no host groups to nest in, nothing to re-run, no test to rename and no
    /// element properties to read. Each of those is a null capability rather than a branch, which is
    /// the whole reason the seat without a clash engine can still take part in a review.
    ///
    /// What it does keep is <see cref="ICheckGeometry"/>, because a file written by this product
    /// carries, per element, the host handle and the path down the tree — so the elements can be
    /// found without searching for them.
    ///
    /// A file is a partial view of a run and never a run: its read marks nothing absent.
    ///
    /// What closes a finding a file raised: a host read that adopts it, or a person. Nothing else
    /// can. Its run fact names the file's own test name, which is no host test id, so no host
    /// read's scope ever covers it and no read can mark it absent. That is the intended answer
    /// rather than a gap — a seat with no clash engine cannot know that a clash it was sent has
    /// been resolved, and marking one absent because the document that is open does not report it
    /// would close, on no evidence, the findings of every test the file did not carry. A file-born
    /// finding the document does report is claimed by <see cref="Adopt"/> on the first host read
    /// and is the document's finding from then on.
    /// </summary>
    public sealed class FileCheck : ICheck
    {
        private readonly ClashPackRead _read;
        private readonly string _path;
        private readonly double _cell;

        // The file's own item behind each finding, kept so a markup save writes back what was read
        // and so the elements can be located without a search.
        private readonly Dictionary<string, ClashItem> _items =
            new Dictionary<string, ClashItem>(StringComparer.Ordinal);

        private readonly List<CheckRun> _runs = new List<CheckRun>();
        private readonly List<Finding> _findings = new List<Finding>();
        private readonly List<string> _notes = new List<string>();

        private int _unkeyed;

        /// <summary>Read a clash file as a check.</summary>
        /// <param name="read">The file, as <see cref="ClashFile.Read"/> returned it.</param>
        /// <param name="path">Where the file came from, for the run's own wording.</param>
        /// <param name="cell">The cell size the clash keys are quantised to.</param>
        public FileCheck(ClashPackRead read, string? path = null,
                         double cell = ClashKey.DefaultCell)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _path = path ?? string.Empty;
            _cell = cell;

            Geometry = new Sight(this);

            Take();
        }

        /// <inheritdoc />
        public string Key => FindingSource.Clash;

        /// <summary>What the picker shows. The same check; the file is where it read from.</summary>
        public string Name => "Clash detection";

        /// <inheritdoc />
        public string RunWord => "test";

        /// <inheritdoc />
        public string ResultWord => "clash";

        /// <summary>The same symbol the document leg draws. The same check; the file is where it read from.</summary>
        public string Symbol => CheckSymbols.Clash;

        /// <inheritdoc />
        public bool IsAvailable => _read.IsUsable;

        /// <inheritdoc />
        public string? Unavailable => IsAvailable ? null : _read.Explain();

        /// <summary>
        /// The same three columns the document leg declares.
        ///
        /// The same columns and not a copy of them: a file board and a document board show one
        /// register slice between them, and two declarations of "Worst" would be two definitions
        /// of what the figure in that cell means. A file carries no tolerance of its own, so the
        /// depth is toned against clash's own default, which is what this board has always done
        /// on a file.
        /// </summary>
        public IReadOnlyList<ResultColumn> Columns { get; } =
            ClashColumns.Declare(_ => null);

        /// <summary>Nowhere to echo a decision to. A file is not a document.</summary>
        public ICheckHostEcho? HostEcho => null;

        /// <summary>No host groups to nest into.</summary>
        public ICheckGrouping? Grouping => null;

        /// <summary>Nothing to run: the results were found somewhere else.</summary>
        public ICheckRunner? Runner => null;

        /// <summary>The test names are the file's, not ours to change.</summary>
        public ICheckNaming? Naming => null;

        /// <summary>No elements in this document to read properties off.</summary>
        public ICheckProperties? Properties => null;

        /// <inheritdoc />
        public ICheckGeometry? Geometry { get; }

        /// <summary>How many results the file carried no CamelWorks key for.</summary>
        public int Unkeyed => _unkeyed;

        /// <summary>Where the file came from.</summary>
        public string Path => _path;

        /// <summary>
        /// The file's own item behind a finding, or null when the file carried no key for it.
        ///
        /// A markup writes back what was read, item by item, so the item has to survive the read.
        /// A result with no item cannot land back on the model and is counted in
        /// <see cref="Unkeyed"/> rather than written into a markup of nothing.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public ClashItem? Item(Finding? finding)
        {
            if (finding == null) return null;

            if (_items.TryGetValue(finding.Id, out var item)) return item;

            return finding.TopicGuid != null && _items.TryGetValue(finding.TopicGuid, out var same)
                ? same
                : null;
        }

        /// <summary>
        /// No measure: a file carries no tolerance to hold a distance against.
        ///
        /// The depth column still tones, against clash's own default rather than against the
        /// run's — a file records the results and not the settings they were found under, so
        /// there is a figure to draw and nothing to normalise it by.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public double? Severity(Finding finding)
        {
            _ = finding;

            return null;
        }

        /// <summary>
        /// What a band is called. The same words the document leg uses, so one register slice
        /// read from both does not band under two vocabularies.
        /// </summary>
        /// <param name="severity">The severity, which for a file is always null.</param>
        public string SeverityBand(double? severity) =>
            severity == null ? "no measure"
            : severity.Value >= 4 ? "far past tolerance"
            : severity.Value >= 1 ? "past tolerance"
            : "within tolerance";

        /// <summary>No aliases: a file read is matched by topic, not by neighbouring cell.</summary>
        /// <param name="finding">The finding.</param>
        public IEnumerable<string> Aliases(Finding finding)
        {
            _ = finding;

            return Array.Empty<string>();
        }

        /// <inheritdoc />
        public IReadOnlyList<CheckRun> Runs() => _runs;

        /// <summary>
        /// What the file held.
        ///
        /// Read once, when the file was opened, and handed out again here: re-reading it would mean
        /// re-parsing the archive, and a markup being edited on the board would lose the edits.
        ///
        /// The scope marks nothing absent. A file is one party's partial view of a run — the tests
        /// they were sent, the results they were asked about — so a result it does not carry is not
        /// a result that has gone.
        /// </summary>
        /// <param name="runIds">Which tests, or null for every test the file carried.</param>
        /// <param name="onProgress">Called with how many results have been read; false stops it.</param>
        public CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress)
        {
            var wanted = runIds == null
                ? null
                : new HashSet<string>(runIds, StringComparer.OrdinalIgnoreCase);

            var runs = wanted == null
                ? _runs
                : _runs.Where(run => wanted.Contains(run.Id)).ToList();

            var found = new List<Finding>();
            var seen = 0;

            foreach (var finding in _findings)
            {
                if (wanted != null && !CheckFacts.RunsOf(finding).Any(wanted.Contains)) continue;

                if (onProgress != null && !onProgress(++seen)) break;

                found.Add(finding);
            }

            return new CheckRead(runs, found, _ => false, _notes);
        }

        /// <summary>Take the file apart once, into runs and findings.</summary>
        private void Take()
        {
            foreach (var warning in _read.Warnings) _notes.Add(warning);

            if (!_read.IsUsable)
            {
                _notes.Add(_read.Explain());
                return;
            }

            var byTest = new Dictionary<string, List<ClashResultInfo>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var folders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var test in _read.Tests)
            {
                if (folders.ContainsKey(test.Name)) continue;

                folders[test.Name] = test.Folder;
                order.Add(test.Name);
                byTest[test.Name] = new List<ClashResultInfo>();
            }

            foreach (var result in _read.Results)
            {
                if (!byTest.TryGetValue(result.Test, out var held))
                {
                    held = new List<ClashResultInfo>();
                    byTest[result.Test] = held;
                    folders[result.Test] = null;
                    order.Add(result.Test);
                }

                if (result.Item == null) _unkeyed++;

                held.Add(Described(result));
            }

            foreach (var name in order)
            {
                var results = byTest[name];

                // The file's test name stands in for the host's id. A file carries no host ids —
                // they are the open document's, and this board may not have that document open —
                // so the name is the only stable handle a markup and its baseline agree on.
                _runs.Add(new CheckRun(name, name, MatrixNaming.Worded(MatrixDiff.Kinded(null)),
                                       "two selections this test does not name",
                                       folders.TryGetValue(name, out var folder) ? folder : null,
                                       null, Array.Empty<string>(), results.Count, 0,
                                       isOurs: false));

                // No scale: a file's points and distances are metres already, which is the unit
                // the producer now writes on both legs.
                var found = ClashFindings.ForTest(name, name, results, _cell);

                foreach (var finding in found)
                {
                    CheckFacts.SetRuns(finding, new[] { name });

                    // Read out of a file and not out of the document that is open, so nothing here
                    // is in the host: a decision on one of these is held in the register and
                    // written back into the file, which is what a markup is.
                    finding.HostHasIt = false;
                }

                _findings.AddRange(found);
            }

            var items = new Dictionary<string, ClashItem>(StringComparer.Ordinal);
            var byGuid = new Dictionary<string, ClashItem>(StringComparer.Ordinal);

            foreach (var result in _read.Results)
                if (result.Item != null)
                    byGuid[Handle(result)] = result.Item;

            // The topic guid each item is filed under is claimed once, and the findings are
            // walked in id order so two findings on one guid answer the same way twice. A file
            // another tool re-authored can carry one guid on two topics.
            var claimed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var finding in _findings.OrderBy(one => one.Id, StringComparer.Ordinal))
            {
                if (!finding.Facts.TryGetValue(ClashFindings.HostId, out var id)
                    || id == null || !byGuid.TryGetValue(id, out var item))
                    continue;

                items[finding.Id] = item;

                // The topic the file actually carried, rather than the one re-derived from the
                // key. The two are normally equal; they differ for the second of two findings on
                // one clash, which goes out under a topic of its own, and a markup saved here has
                // to go home under the name it arrived with.
                if (!string.IsNullOrEmpty(item.TopicGuid)) finding.TopicGuid = item.TopicGuid;

                // What the writing seat resolved per document and this one cannot: which model
                // and which discipline each side is. The facets and the same-model test read them.
                Carried(finding, ClashFindings.ModelA, item.ModelA);
                Carried(finding, ClashFindings.ModelB, item.ModelB);
                Carried(finding, ClashFindings.DisciplineA, item.DisciplineA);
                Carried(finding, ClashFindings.DisciplineB, item.DisciplineB);

                // Under the topic guid as well, because a caller that has folded this read onto
                // the findings already held is holding those and not these, and the item is what
                // says where in the model the elements are.
                if (!string.IsNullOrEmpty(finding.TopicGuid) && claimed.Add(finding.TopicGuid!))
                    items[finding.TopicGuid!] = item;
            }

            foreach (var pair in items) _items[pair.Key] = pair.Value;
        }

        /// <summary>Write a fact the file carried, and leave what is there when it carried none.</summary>
        /// <param name="finding">The finding raised from the file.</param>
        /// <param name="fact">The fact's name.</param>
        /// <param name="value">What the file said, or null.</param>
        private static void Carried(Finding finding, string fact, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) finding.Facts[fact] = value!;
        }

        /// <summary>One file result, in the shape the clash producer reads.</summary>
        /// <param name="result">The result the file carried.</param>
        private static ClashResultInfo Described(ClashPackResult result)
        {
            var last = result.Comments.Count > 0
                ? result.Comments[result.Comments.Count - 1]
                : null;

            return new ClashResultInfo(
                Handle(result),
                result.Item?.Key.A ?? default,
                result.Item?.Key.B ?? default,
                result.X, result.Y, result.Z,
                FindingStatusLattice.ToName(result.Status),
                result.AssignedTo)
            {
                Name = result.Name,
                GroupName = result.Group,

                // The participants' display names, as the producing seat wrote them. These used
                // to read result.Item.ModelA, which is the model file the element is in and not
                // the element: the two element-name facts came home holding model names.
                NameA = Said(result, ClashFindings.NameA),
                NameB = Said(result, ClashFindings.NameB),
                CategoryA = result.Item?.CategoryA,
                CategoryB = result.Item?.CategoryB,
                CommentCount = result.Comments.Count,
                LastComment = last?.Body,
                Comments = result.Comments,
                Distance = result.Item?.Distance,
            };
        }

        /// <summary>One fact the file carried on a result, or null when it carried none.</summary>
        /// <param name="result">The result the file carried.</param>
        /// <param name="fact">The fact's name.</param>
        private static string? Said(ClashPackResult result, string fact) =>
            result.Item != null
            && result.Item.Properties.TryGetValue(fact, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        /// <summary>What stands in for a host result id: the file's own guid, or where it sits.</summary>
        /// <param name="result">The result the file carried.</param>
        private static string Handle(ClashPackResult result) =>
            result.FileGuid ?? result.Test + "/" + result.Name;

        /// <summary>
        /// Fold a file read's findings onto the ones already held for the same clashes.
        ///
        /// A file carries no host test id, so a finding raised from a file is hashed under the
        /// file's test name and a finding read from the document under the host's id for that test:
        /// the two ids differ for one physical clash and the register, which merges by id, would
        /// hold both. What the two do agree on is the topic guid, which is derived from the two
        /// elements and the cell they meet in, so that is what the fold matches on.
        ///
        /// The held finding keeps its own identity and takes the file's status, assignee, group and
        /// remarks. Only findings with no held match are returned, for the caller to observe as
        /// new.
        ///
        /// A held finding is claimed once. A topic guid is derived from the element pair and the
        /// cell, not from the run, so two clash tests reporting one physical conflict produce two
        /// findings with different ids and the same topic guid. Without a claim both would fold
        /// onto the one held finding, and the second would be neither drawn as folded nor returned
        /// as unheld — dropped from the read altogether. The second is returned as unheld instead
        /// and observed as its own finding: collapsing duplicates is a separate decision and not
        /// one a fold may take by accident.
        /// </summary>
        /// <param name="register">The register.</param>
        /// <param name="found">What the file read produced.</param>
        /// <param name="folded">How many landed on a finding that was already held.</param>
        public static IReadOnlyList<Finding> Fold(FindingRegister? register,
                                                  IReadOnlyList<Finding>? found,
                                                  out int folded)
        {
            folded = 0;

            var fresh = found ?? Array.Empty<Finding>();

            if (register == null) return fresh;

            var claims = new Claims(register.From(FindingSource.Clash), null);

            var landed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var one in Ordered(fresh))
            {
                var held = claims.Claim(one.TopicGuid);

                if (held == null || string.Equals(held.Id, one.Id, StringComparison.Ordinal))
                    continue;

                Onto(held, one);
                landed.Add(one.Id);
                folded++;
            }

            // In the order the read produced them, not the order the claims were made in: the
            // caller observes these and the board draws that read.
            return fresh.Where(one => !landed.Contains(one.Id)).ToList();
        }

        /// <summary>
        /// Take over the findings a file read raised, when the document reports the same clashes.
        ///
        /// The other direction of <see cref="Fold"/>, and the one the register needs after a board
        /// read from a file has written findings into it: those findings are hashed under the
        /// file's test name and the document's are hashed under the host's id for that test, so
        /// without this the register would hold two findings for one clash and the board would
        /// draw the host's while the decisions sat on the other.
        ///
        /// The host's finding wins the identity, because it is the one the board draws and the one
        /// the echo can write. It takes the file-born finding's status, assignee, group, dates and
        /// remarks, and the file-born finding is then removed rather than left as a duplicate.
        ///
        /// A held finding the document reported is marked as being in the host and is left alone:
        /// this only ever claims findings a file raised.
        ///
        /// A file-born finding is claimed once, for the reason <see cref="Fold"/> states. A second
        /// fresh finding whose topic another has already claimed is left alone and observed as its
        /// own finding, so one decision is never copied onto two rows.
        ///
        /// Removing the file-born finding orphans anything that named it by id, which is what an
        /// issue's attachment is. The ids that moved are returned so the caller can re-point them
        /// under the save door that writes issues and findings together.
        /// </summary>
        /// <param name="register">The register.</param>
        /// <param name="found">What the host read produced.</param>
        /// <param name="adopted">How many file-born findings were taken over.</param>
        /// <returns>The id each adopted finding had, against the id it has now.</returns>
        public static IReadOnlyDictionary<string, string> Adopt(FindingRegister? register,
                                                                IReadOnlyList<Finding>? found,
                                                                out int adopted)
        {
            adopted = 0;

            var moved = new Dictionary<string, string>(StringComparer.Ordinal);

            if (register == null || found == null) return moved;

            // Raised by a file read and by nothing since. A finding the document reported is
            // marked by its producer as being in the host, and this must never claim one of
            // those: what it claims it removes, and the document's findings are what the board
            // draws and what the echo can write.
            var claims = new Claims(register.From(FindingSource.Clash), held => !held.HostHasIt);

            if (claims.IsEmpty) return moved;

            foreach (var fresh in Ordered(found))
            {
                var held = claims.Claim(fresh.TopicGuid);

                if (held == null || string.Equals(held.Id, fresh.Id, StringComparison.Ordinal))
                    continue;

                Onto(fresh, held);

                if (held.DueDayNumber != null) fresh.DueDayNumber = held.DueDayNumber;
                if (held.Priority != null) fresh.Priority = held.Priority;
                if (held.Stage != null) fresh.Stage = held.Stage;

                foreach (var label in held.Labels)
                    if (!fresh.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                        fresh.Labels.Add(label);

                register.Remove(held.Id);
                moved[held.Id] = fresh.Id;
                adopted++;
            }

            return moved;
        }

        /// <summary>
        /// The findings to match, in one order.
        ///
        /// By id, ordinal. Which of two findings sharing a topic guid claims the held one decides
        /// where a status and a comment trail land, and dictionary order would make that answer
        /// depend on how the register happened to enumerate.
        /// </summary>
        /// <param name="findings">The findings a read produced.</param>
        private static IEnumerable<Finding> Ordered(IEnumerable<Finding> findings) =>
            findings.OrderBy(one => one.Id, StringComparer.Ordinal);

        /// <summary>
        /// Held findings by topic guid, each claimable once.
        ///
        /// The topic guid is the one id a file and a document agree on for one clash, and it is
        /// not unique: it is derived from the element pair and the cell, so two runs reporting one
        /// physical conflict share it. Both directions of the fold match through this, so the
        /// claim is written once rather than twice.
        /// </summary>
        private sealed class Claims
        {
            private readonly Dictionary<string, List<Finding>> _byTopic =
                new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Index the held findings this fold may claim.</summary>
            /// <param name="held">The findings the register holds.</param>
            /// <param name="keep">Which of them may be claimed, or null for all of them.</param>
            internal Claims(IEnumerable<Finding> held, Func<Finding, bool>? keep)
            {
                foreach (var one in held)
                {
                    if (keep != null && !keep(one)) continue;

                    var topic = one.TopicGuid;

                    if (string.IsNullOrEmpty(topic)) continue;

                    if (!_byTopic.TryGetValue(topic!, out var under))
                    {
                        under = new List<Finding>();
                        _byTopic[topic!] = under;
                    }

                    under.Add(one);
                }

                foreach (var under in _byTopic.Values)
                    under.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            }

            /// <summary>True when there is nothing to claim.</summary>
            internal bool IsEmpty => _byTopic.Count == 0;

            /// <summary>
            /// The first unclaimed held finding under this topic, or null when there is none left.
            /// </summary>
            /// <param name="topic">The topic guid a fresh finding carries.</param>
            internal Finding? Claim(string? topic)
            {
                if (string.IsNullOrEmpty(topic)
                    || !_byTopic.TryGetValue(topic!, out var under)) return null;

                foreach (var one in under)
                    if (_claimed.Add(one.Id)) return one;

                return null;
            }
        }

        /// <summary>What a file says about a clash, onto the finding already held for it.</summary>
        /// <param name="held">The finding the register holds.</param>
        /// <param name="fresh">The finding the file produced.</param>
        private static void Onto(Finding held, Finding fresh)
        {
            // Promoted and never demoted, as a run's own report is: what somebody decided here
            // outranks what a file that left last week still says.
            held.Status = FindingStatusLattice.Merge(held.Status, fresh.Status);

            if (!string.IsNullOrWhiteSpace(fresh.AssignedTo)) held.AssignedTo = fresh.AssignedTo;
            if (!string.IsNullOrWhiteSpace(fresh.Group)) held.Group = fresh.Group;

            foreach (var comment in fresh.Comments)
                if (!held.Comments.Any(one => one.Repeats(comment)))
                    held.Comments.Add(comment);
        }

        /// <summary>Selecting what a file names, for a board with no clash engine behind it.</summary>
        private sealed class Sight : ICheckGeometry
        {
            private readonly FileCheck _check;

            internal Sight(FileCheck check) => _check = check;

            /// <summary>
            /// Nothing to select: there is no engine holding these results.
            ///
            /// Null rather than zero, which is the difference between "selected none of them" and
            /// "cannot select"; the caller falls back to <see cref="Locate"/>.
            /// </summary>
            /// <param name="findings">The findings.</param>
            /// <param name="add">True to add to the selection rather than replace it.</param>
            public int? Select(IReadOnlyList<Finding> findings, bool add)
            {
                _ = findings;
                _ = add;

                return null;
            }

            /// <summary>
            /// Where each finding's elements are, as the file recorded them.
            ///
            /// A file written by this product carries the host's own handle and the path down the
            /// tree per element, so the document can be asked for the element rather than searched
            /// for it. An older file carries neither and the key is what is left, which still works
            /// for an instance GUID and still declines to index the federation behind a press.
            /// </summary>
            /// <param name="findings">The findings.</param>
            public IReadOnlyList<ElementLocator> Locate(IReadOnlyList<Finding> findings)
            {
                var found = new List<ElementLocator>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                void Add(ElementKey key, string? pathId, string? tree)
                {
                    if (key.IsEmpty || !seen.Add(key.ToString())) return;

                    found.Add(new ElementLocator(key, pathId, tree));
                }

                foreach (var finding in findings ?? Array.Empty<Finding>())
                {
                    var item = _check.Item(finding);

                    if (finding.Elements.Count > 0)
                        Add(finding.Elements[0], item?.PathIdA, item?.TreeA);

                    if (finding.Elements.Count > 1)
                        Add(finding.Elements[1], item?.PathIdB, item?.TreeB);
                }

                return found;
            }

            /// <summary>No viewpoint: a file's results were seen in somebody else's document.</summary>
            /// <param name="finding">The finding.</param>
            public bool ApplyView(Finding finding)
            {
                _ = finding;

                return false;
            }

            /// <summary>No picture: there is no engine to render one.</summary>
            /// <param name="finding">The finding.</param>
            /// <param name="width">Image width in pixels.</param>
            /// <param name="height">Image height in pixels.</param>
            public byte[]? Snapshot(Finding finding, int width, int height)
            {
                _ = finding;
                _ = width;
                _ = height;

                return null;
            }
        }
    }
}
