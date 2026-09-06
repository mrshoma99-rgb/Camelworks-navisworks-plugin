using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Testing
{
    /// <summary>
    /// A check in memory, for everything downstream of a check read.
    ///
    /// The same shape as <see cref="FakeClashSource"/>: a small mutable model, a log of what was
    /// asked of it, and a switch for each way a check refuses. It is a fake and not a mock — a
    /// rename really renames, a deleted run really stops being read — so a test can drive the seam
    /// the way a board does and read the result back.
    ///
    /// Every capability is null by default, which is what six of the seven real checks answer, and
    /// is the case a board is most likely to get wrong. Running and naming are switched on rather
    /// than absent, because those two have behaviour worth asserting.
    /// </summary>
    public sealed class FakeCheck : ICheck, ICheckRunner, ICheckNaming
    {
        private readonly List<FakeRun> _runs = new List<FakeRun>();
        private readonly List<string> _log = new List<string>();
        private readonly List<ResultColumn> _columns = new List<ResultColumn>();

        private sealed class FakeRun
        {
            internal FakeRun(string id, string name, string? folder)
            {
                Id = id; Name = name; Folder = folder;
            }

            internal string Id { get; }
            internal string Name { get; set; }
            internal string? Folder { get; set; }
            internal long LastRunTicks { get; set; }
            internal List<Finding> Findings { get; } = new List<Finding>();
        }

        /// <summary>Build one.</summary>
        /// <param name="key">Its key, which is also the source its findings carry.</param>
        /// <param name="name">What the picker would show.</param>
        public FakeCheck(string key = "Fake", string name = "Fake check")
        {
            Key = key;
            Name = name;

            _columns.Add(new ResultColumn("Measured", "Measured", ColumnKind.Number)
            {
                RightAligned = true,
                Unit = "m",
                Number = finding => Read(finding, "Measured"),
            });

            _columns.Add(new ResultColumn("Why", "Why", ColumnKind.Text)
            {
                Text = finding => finding.Facts.TryGetValue("Why", out var why) ? why : null,
            });
        }

        // ---- What the check is -----------------------------------------------------------

        /// <inheritdoc />
        public string Key { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public string RunWord { get; set; } = "run";

        /// <inheritdoc />
        public string ResultWord { get; set; } = "result";

        /// <inheritdoc />
        public bool IsAvailable { get; set; } = true;

        /// <inheritdoc />
        public string? Unavailable { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ResultColumn> Columns => _columns;

        /// <summary>Everything that was asked of it, in order, for a test to assert on.</summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary>What the read could not do, reported on every read.</summary>
        public IList<string> Notes { get; } = new List<string>();

        /// <summary>
        /// The ids a finding may also be recognised under, or null to offer none — which is what
        /// every real check but clash does.
        /// </summary>
        public Func<Finding, IEnumerable<string>>? AliasSource { get; set; }

        /// <summary>How bad a finding is, or null to measure nothing.</summary>
        public Func<Finding, double?>? SeverityOf { get; set; }

        /// <summary>Whether this check offers running. False makes <see cref="Runner"/> null.</summary>
        public bool CanBeRun { get; set; } = true;

        /// <summary>Whether this check offers renaming. False makes <see cref="Naming"/> null.</summary>
        public bool CanBeNamed { get; set; } = true;

        /// <inheritdoc />
        public ICheckHostEcho? HostEcho { get; set; }

        /// <inheritdoc />
        public ICheckGrouping? Grouping { get; set; }

        /// <inheritdoc />
        public ICheckProperties? Properties { get; set; }

        /// <inheritdoc />
        public ICheckGeometry? Geometry { get; set; }

        /// <inheritdoc />
        public ICheckRunner? Runner => CanBeRun ? this : null;

        /// <inheritdoc />
        public ICheckNaming? Naming => CanBeNamed ? this : null;

        // ---- Building one ----------------------------------------------------------------

        /// <summary>Put a run in.</summary>
        /// <param name="id">Its id.</param>
        /// <param name="name">What it is called, or null to use the id.</param>
        /// <param name="folder">Which folder, or null for the root.</param>
        public FakeCheck With(string id, string? name = null, string? folder = null)
        {
            _runs.Add(new FakeRun(id, name ?? id, folder));

            return this;
        }

        /// <summary>
        /// Put a result in the last run added, as a finding carrying that run's id.
        /// </summary>
        /// <param name="title">One line for the board.</param>
        /// <param name="measured">The number the check measured, in metres.</param>
        /// <param name="discriminator">What tells it from another result of the same run, or null.</param>
        /// <param name="status">The status it is held at.</param>
        public FakeCheck Result(string title, double measured = 0, string? discriminator = null,
                                FindingStatus status = FindingStatus.New)
        {
            if (_runs.Count == 0) throw new InvalidOperationException("add a run first");

            var run = _runs[_runs.Count - 1];

            var finding = Finding.Create(
                Key, run.Name, title,
                new[] { Element(title) },
                discriminator ?? run.Id + "/" + title);

            finding.Status = status;
            finding.Facts["Measured"] = measured.ToString("0.###", CultureInfo.InvariantCulture);
            finding.Facts["Why"] = title;

            CheckFacts.SetRuns(finding, new[] { run.Id });

            run.Findings.Add(finding);

            return this;
        }

        /// <summary>
        /// Say that the last result added belongs to another run as well, which is the shape of one
        /// physical conflict two runs both report.
        /// </summary>
        /// <param name="runId">The other run's id.</param>
        public FakeCheck AlsoIn(string runId)
        {
            var finding = Last();

            CheckFacts.SetRuns(finding, CheckFacts.RunsOf(finding).Concat(new[] { runId }).ToList());

            return this;
        }

        /// <summary>Give the last result added a party, so a journal has one to record.</summary>
        /// <param name="who">Who it is with.</param>
        public FakeCheck AssignedTo(string who)
        {
            Last().AssignedTo = who;

            return this;
        }

        /// <summary>The findings this check would report, whichever runs they belong to.</summary>
        public IReadOnlyList<Finding> Findings => _runs.SelectMany(run => run.Findings).ToList();

        // ---- Reading ---------------------------------------------------------------------

        /// <inheritdoc />
        public IReadOnlyList<CheckRun> Runs() =>
            !IsAvailable
                ? Array.Empty<CheckRun>()
                : _runs.Select(run => new CheckRun(
                      run.Id, run.Name, "Fake", "everything against everything", run.Folder,
                      threshold: null, trades: null, resultCount: run.Findings.Count,
                      lastRunTicks: run.LastRunTicks, isOurs: true)).ToList();

        /// <inheritdoc />
        public CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress)
        {
            _log.Add("read");

            if (!IsAvailable)
                return new CheckRead(Array.Empty<CheckRun>(), Array.Empty<Finding>(),
                                     notes: new[] { Unavailable ?? "this check is not available." });

            var wanted = _runs.Where(run => runIds == null
                                            || runIds.Contains(run.Id, StringComparer.Ordinal))
                              .ToList();

            var read = new List<CheckRun>();
            var findings = new List<Finding>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var run in wanted)
            {
                // Stopping where it was asked to, rather than reading on and discarding: a cancelled
                // read reports the runs it managed, and its scope must cover only those.
                if (onProgress != null && !onProgress(read.Count + 1)) break;

                read.Add(Runs().First(one => one.Id == run.Id));

                // One finding reported by two runs is read once and scoped to both, because the
                // board draws a row per finding and the rail tallies it under each run it names.
                foreach (var finding in run.Findings)
                    if (seen.Add(finding.Id)) findings.Add(finding);
            }

            return new CheckRead(read, findings,
                                 scope: CheckFacts.ScopeOf(read.Select(run => run.Id)),
                                 notes: Notes.ToList());
        }

        /// <inheritdoc />
        public double? Severity(Finding finding) => SeverityOf?.Invoke(finding);

        /// <inheritdoc />
        public string SeverityBand(double? severity) =>
            severity == null ? "not measured"
            : severity.Value >= 2 ? "well past"
            : severity.Value >= 1 ? "past"
            : "within";

        /// <inheritdoc />
        public IEnumerable<string> Aliases(Finding finding) =>
            AliasSource?.Invoke(finding) ?? Array.Empty<string>();

        // ---- Running and naming ----------------------------------------------------------

        /// <inheritdoc />
        public bool CanRun => IsAvailable;

        /// <inheritdoc />
        public string? CannotRun => CanRun ? null : Unavailable ?? "this check is not available.";

        /// <inheritdoc />
        public bool Run(string runId, Func<int, bool>? onProgress)
        {
            _log.Add("run " + runId);

            var run = Held(runId);

            if (run == null || !CanRun) return false;

            onProgress?.Invoke(100);
            run.LastRunTicks = DateTime.UtcNow.Ticks;

            return true;
        }

        /// <inheritdoc />
        public bool Delete(IReadOnlyList<string> runIds)
        {
            _log.Add("delete");

            var went = false;

            foreach (var id in runIds ?? Array.Empty<string>())
            {
                var run = Held(id);

                if (run != null) went |= _runs.Remove(run);
            }

            return went;
        }

        /// <inheritdoc />
        public bool Compact(IReadOnlyList<string> runIds)
        {
            _log.Add("compact");

            var emptied = false;

            foreach (var id in runIds ?? Array.Empty<string>())
            {
                var run = Held(id);

                if (run == null || run.Findings.Count == 0) continue;

                run.Findings.Clear();
                emptied = true;
            }

            return emptied;
        }

        /// <inheritdoc />
        public bool Rename(string runId, string name)
        {
            _log.Add("rename " + runId);

            var run = Held(runId);

            if (run == null || string.IsNullOrWhiteSpace(name)) return false;

            run.Name = name.Trim();

            return true;
        }

        /// <inheritdoc />
        public bool Refile(string runId, string? folder)
        {
            _log.Add("refile " + runId);

            var run = Held(runId);

            if (run == null) return false;

            var wanted = (folder ?? string.Empty).Trim();

            run.Folder = wanted.Length == 0 ? null : wanted;

            return true;
        }

        /// <inheritdoc />
        public bool HasBook => false;

        // ---- Inside ----------------------------------------------------------------------

        private FakeRun? Held(string? id) =>
            id == null ? null : _runs.FirstOrDefault(run => run.Id == id);

        private Finding Last()
        {
            var finding = _runs.LastOrDefault(run => run.Findings.Count > 0)?.Findings.LastOrDefault();

            if (finding == null) throw new InvalidOperationException("add a result first");

            return finding;
        }

        private static double? Read(Finding finding, string fact) =>
            finding.Facts.TryGetValue(fact, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : (double?)null;

        /// <summary>One element of the fixture, named so two findings never share one.</summary>
        /// <param name="name">What to call it.</param>
        private static ElementKey Element(string name) =>
            ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\p\FAKE.nwc"), @"C:\p\FAKE.nwc",
                                    "L01", name);

        /// <inheritdoc />
        public override string ToString() =>
            Name + ": " + _runs.Count.ToString(CultureInfo.InvariantCulture) + " run(s)";
    }
}
