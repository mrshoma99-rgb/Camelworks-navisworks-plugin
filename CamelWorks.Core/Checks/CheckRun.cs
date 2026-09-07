using System;
using System.Collections.Generic;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// One run of one check, as the rail draws it: a clash test, a saved clearance run, an IDS
    /// specification, an audit rule, a revision pair.
    ///
    /// The check writes the wording. <see cref="Between"/> and <see cref="Threshold"/> are
    /// sentences the check composes out of its own settings, because the board has no way to word
    /// "Mechanical v Structure" for one check and "Corridors, 2.1 m" for another without knowing
    /// what each check's settings mean.
    /// </summary>
    public sealed class CheckRun
    {
        /// <summary>Describe a run.</summary>
        /// <param name="id">The check's own id for it, stable across a read.</param>
        /// <param name="name">What it is called.</param>
        /// <param name="kind">The rail's hint word: "Hard clash", "Clearance", "IDS specification".</param>
        /// <param name="between">What it covers, worded by the check.</param>
        /// <param name="folder">The folder it is filed in, or null for the root.</param>
        /// <param name="threshold">The threshold, worded, or null when it has none.</param>
        /// <param name="trades">The discipline chips on the rail. Empty is legitimate.</param>
        /// <param name="resultCount">How many results it holds.</param>
        /// <param name="lastRunTicks">When it last ran, as UTC ticks, or zero if it never has.</param>
        /// <param name="isOurs">True when this product created it, so it may be renamed and re-run.</param>
        public CheckRun(string id, string name, string kind, string between, string? folder = null,
                        string? threshold = null, IReadOnlyList<string>? trades = null,
                        int resultCount = 0, long lastRunTicks = 0, bool isOurs = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));

            Id = id;
            Name = name ?? string.Empty;
            Kind = kind ?? string.Empty;
            Between = between ?? string.Empty;
            Folder = folder;
            Threshold = threshold;
            Trades = trades ?? Array.Empty<string>();
            ResultCount = resultCount;
            LastRunTicks = lastRunTicks;
            IsOurs = isOurs;
        }

        /// <summary>The check's own id for this run.</summary>
        public string Id { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; }

        /// <summary>The folder it is filed in, or null for the root.</summary>
        public string? Folder { get; }

        /// <summary>The rail's hint word for what kind of run this is.</summary>
        public string Kind { get; }

        /// <summary>What it covers, as one sentence the check wrote.</summary>
        public string Between { get; }

        /// <summary>The threshold, worded — "25 mm", "2.1 m", "grade C". Null when there is none.</summary>
        public string? Threshold { get; }

        /// <summary>The disciplines it touches, for the rail's chips.</summary>
        public IReadOnlyList<string> Trades { get; }

        /// <summary>How many results it holds.</summary>
        public int ResultCount { get; }

        /// <summary>
        /// When it last ran, as UTC ticks, or zero for a run saved and never run.
        ///
        /// The two are different empty states: a run that has never been run has nothing to show,
        /// and a run that found nothing found nothing.
        /// </summary>
        public long LastRunTicks { get; }

        /// <summary>
        /// True when this product created the run, so it may be renamed and re-run.
        ///
        /// False for a host clash test read back, an IDS specification, an audit rule — things the
        /// board can read and report on but does not own.
        /// </summary>
        public bool IsOurs { get; }

        /// <inheritdoc />
        public override string ToString() => Name + " (" + Kind + ")";
    }
}
