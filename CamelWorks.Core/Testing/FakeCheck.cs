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
    ///
    /// Findings are built fresh on every read, from what the fixture holds. A real check builds one
    /// from whatever its engine reported this time, and its ids have to come back the same; a fake
    /// that handed the same objects out twice would be the only check at this seam whose identity
    /// rule was never exercised.
    /// </summary>
    public sealed class FakeCheck : ICheck, ICheckRunner, ICheckNaming
    {
        private readonly List<FakeRun> _runs = new List<FakeRun>();
        private readonly List<string> _log = new List<string>();
        private readonly List<ResultColumn> _columns = new List<ResultColumn>();

        private sealed class FakeRun
        {
            internal FakeRun(string check, string id, string name, string? folder)
            {
                Check = check; Id = id; Name = name; Folder = folder;
            }

            internal string Check { get; }
            internal string Id { get; }
            internal string Name { get; set; }
            internal string? Folder { get; set; }
            internal long LastRunTicks { get; set; }
            internal List<FakeResult> Results { get; } = new List<FakeResult>();
        }

        /// <summary>
        /// One result as the fixture holds it: what it is, not the object a read handed out.
        ///
        /// A finding is built from this on every read, as a real check builds one from whatever its
        /// engine reports. Holding one object and handing it back would make the fake the one check
        /// whose findings are reference-identical between runs, and the identity rule — the same
        /// source, rule, title, elements and discriminator derive the same id — would never be
        /// exercised at this seam.
        /// </summary>
        private sealed class FakeResult
        {
            internal FakeResult(FakeRun run, string title, double measured, string? discriminator)
            {
                Run = run;
                Title = title;
                Measured = measured;
                Discriminator = discriminator ?? run.Id + "/" + title;
                Runs.Add(run.Id);
            }

            internal FakeRun Run { get; }
            internal string Title { get; }
            internal double Measured { get; }
            internal string? Discriminator { get; }
            internal FindingStatus Status { get; set; } = FindingStatus.New;
            internal string? AssignedTo { get; set; }
            internal List<string> Runs { get; } = new List<string>();

            /// <summary>A finding for this result, built fresh.</summary>
            internal Finding Build()
            {
                var finding = Finding.Create(
                    Run.Check, Run.Name, Title, new[] { Element(Title) }, Discriminator);

                finding.Status = Status;
                finding.AssignedTo = AssignedTo;
                finding.Facts["Measured"] = Measured.ToString("0.###", CultureInfo.InvariantCulture);
                finding.Facts["Why"] = Title;

                CheckFacts.SetRuns(finding, Runs);

                return finding;
            }
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

        /// <summary>Settable, so a test can drive a surface that draws one symbol per kind.</summary>
        public string Symbol { get; set; } = CheckSymbols.Check;

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
            _runs.Add(new FakeRun(Key, id, name ?? id, folder));

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

            run.Results.Add(new FakeResult(run, title, measured, discriminator) { Status = status });

            return this;
        }

        /// <summary>
        /// Say that the last result added belongs to another run as well, which is the shape of one
        /// physical conflict two runs both report.
        /// </summary>
        /// <param name="runId">The other run's id.</param>
        public FakeCheck AlsoIn(string runId)
        {
            var result = Last();

            if (!result.Runs.Contains(runId, StringComparer.Ordinal)) result.Runs.Add(runId);

            return this;
        }

        /// <summary>Give the last result added a party, so a journal has one to record.</summary>
        /// <param name="who">Who it is with.</param>
        public FakeCheck AssignedTo(string who)
        {
            Last().AssignedTo = who;

            return this;
        }

        /// <summary>
        /// The findings this check would report, whichever runs they belong to, built fresh on
        /// every call as a read builds them.
        /// </summary>
        public IReadOnlyList<Finding> Findings =>
            _runs.SelectMany(run => run.Results).Select(result => result.Build()).ToList();

        // ---- Reading ---------------------------------------------------------------------

        /// <inheritdoc />
        public IReadOnlyList<CheckRun> Runs() =>
            !IsAvailable
                ? Array.Empty<CheckRun>()
                : _runs.Select(run => new CheckRun(
                      run.Id, run.Name, "Fake", "everything against everything", run.Folder,
                      threshold: null, trades: null, resultCount: ResultsNaming(run.Id).Count,
                      lastRunTicks: run.LastRunTicks, isOurs: true)).ToList();

        /// <summary>Every result naming a run, wherever it was put in.</summary>
        /// <param name="runId">The run id.</param>
        private List<FakeResult> ResultsNaming(string runId) =>
            _runs.SelectMany(run => run.Results)
                 .Where(result => result.Runs.Contains(runId, StringComparer.Ordinal))
                 .ToList();

        /// <summary>
        /// Read the runs asked for, building each result's finding fresh.
        /// </summary>
        /// <param name="runIds">Which runs, or null for every run.</param>
        /// <param name="onProgress">
        /// Called with the number of runs started, which is this check's own meaning for the
        /// figure: <see cref="ICheck.Read"/> leaves what it counts to the check, and returning
        /// false stops the read between runs.
        /// </param>
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

                // Every result naming this run, including one put in under another run: a read of a
                // run reports all of that run's results, and one left out is one reported gone.
                // A result naming two runs is built once and scoped to both, because the board
                // draws a row per finding and the rail tallies it under each run it names.
                foreach (var result in ResultsNaming(run.Id))
                {
                    var finding = result.Build();

                    if (seen.Add(finding.Id)) findings.Add(finding);
                }
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

                if (run == null || run.Results.Count == 0) continue;

                run.Results.Clear();
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

        private FakeResult Last()
        {
            var result = _runs.LastOrDefault(run => run.Results.Count > 0)?.Results.LastOrDefault();

            if (result == null) throw new InvalidOperationException("add a result first");

            return result;
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
