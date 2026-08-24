using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Clash
{
    /// <summary>A board rebuilt from the record of a previous run, and how exact it is.</summary>
    public sealed class HydratedBoard
    {
        internal HydratedBoard(IReadOnlyList<ClashItem> items, double precisionMetres,
                               string? label, int records, int unusable)
        {
            Items = items; PrecisionMetres = precisionMetres; Label = label;
            Records = records; Unusable = unusable;
        }

        /// <summary>The results, as far as the record can describe them.</summary>
        public IReadOnlyList<ClashItem> Items { get; }

        /// <summary>
        /// How far a rebuilt clash point can be from where it really was, in metres.
        ///
        /// The record keeps the identity of a clash, not its coordinates: the position survives
        /// only as the quantised cell inside the key. Recovering the cell centre puts every result
        /// within half a cell of the truth, which is close enough to sort by level and far too
        /// coarse to measure with. Said out loud rather than left for somebody to discover.
        /// </summary>
        public double PrecisionMetres { get; }

        /// <summary>What the run was called, when the record says.</summary>
        public string? Label { get; }

        /// <summary>How many records the store held.</summary>
        public int Records { get; }

        /// <summary>How many could not be read back into a result.</summary>
        public int Unusable { get; }

        /// <summary>True when there was nothing to rebuild from.</summary>
        public bool IsEmpty => Items.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (IsEmpty) return "no previous run is recorded in this project";

            var s = Items.Count.ToString("N0", CultureInfo.InvariantCulture)
                  + " results from the last recorded run";

            if (Label != null) s += " of " + Label;

            s += ", positioned to within "
               + (PrecisionMetres * 1000).ToString("0", CultureInfo.InvariantCulture) + " mm";

            if (Unusable > 0)
                s += " - " + Unusable.ToString("N0", CultureInfo.InvariantCulture)
                   + " records could not be read";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Rebuilds a board from the sidecar when there is no clash engine to read.
    ///
    /// This is what makes CamelWorks usable on Navisworks Simulate, which is most of the seats on
    /// most jobs. Simulate has no Clash Detective, so the tests cannot be read and cannot be run —
    /// but the coordination work built on top of them is not clash detection. Triaging a board,
    /// assigning it, reporting on it and issuing BCF are all things a Simulate seat should be able
    /// to do with the run a Manage seat already made, and the project file already carries enough
    /// to reconstruct it.
    ///
    /// What comes back is honest about being a record rather than a reading: the identity, status,
    /// hand grouping and test name of every result survive exactly, and the position survives only
    /// as the cell it was quantised into. Nothing here invents a coordinate it does not have.
    /// </summary>
    public static class ClashHydration
    {
        /// <summary>What a result is called when the record does not say which test found it.</summary>
        public const string UnknownTest = "(from the last run)";

        /// <summary>Rebuild.</summary>
        /// <param name="snapshot">The record, from the project store.</param>
        /// <param name="metresPerUnit">
        /// The document's scale. The cell lives in host units, because that is what the clash
        /// engine reports and what the key was made from, so recovering metres needs it back.
        /// </param>
        /// <param name="cell">The cell size the keys were made with.</param>
        public static HydratedBoard From(ClashSnapshot? snapshot, double metresPerUnit,
                                         double cell = ClashKey.DefaultCell)
        {
            if (metresPerUnit <= 0 || double.IsNaN(metresPerUnit) || double.IsInfinity(metresPerUnit))
                throw new ArgumentOutOfRangeException(nameof(metresPerUnit), "the scale must be finite and positive");

            if (cell <= 0 || double.IsNaN(cell) || double.IsInfinity(cell))
                throw new ArgumentOutOfRangeException(nameof(cell), "the cell must be finite and positive");

            var precision = cell * metresPerUnit / 2.0;

            if (snapshot == null || snapshot.Records.Count == 0)
                return new HydratedBoard(Array.Empty<ClashItem>(), precision, null, 0, 0);

            var items = new List<ClashItem>(snapshot.Records.Count);
            var unusable = 0;

            foreach (var record in snapshot.Records)
            {
                if (record == null || record.Key.IsEmpty) { unusable++; continue; }

                var item = new ClashItem(record.Key, record.TestName ?? UnknownTest)
                {
                    Status = record.Status,

                    // Quantise rounds to the nearest cell, so the cell index times the cell size is
                    // the centre of it rather than a corner.
                    X = record.Key.CellX * cell * metresPerUnit,
                    Y = record.Key.CellY * cell * metresPerUnit,
                    Z = record.Key.CellZ * cell * metresPerUnit,
                };

                // Only a group somebody pinned by hand carries. A derived name is re-derived by the
                // rules, and carrying it would freeze the board against its own rule stack.
                if (record.GroupWasPinned && !string.IsNullOrWhiteSpace(record.GroupName))
                    item.CarriedGroup = record.GroupName;

                items.Add(item);
            }

            return new HydratedBoard(items, precision, snapshot.Label, snapshot.Records.Count, unusable);
        }
    }
}
