using System;
using System.Globalization;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>What kind of value a declared column holds.</summary>
    public enum ColumnKind
    {
        /// <summary>Words. Sorted as text, never banded.</summary>
        Text = 0,

        /// <summary>A number, in metres where it is a length. Sorted, banded and range filtered.</summary>
        Number = 1,

        /// <summary>One of the finding's elements, named by <see cref="ResultColumn.ElementIndex"/>.</summary>
        Element = 2,
    }

    /// <summary>
    /// A per-result column a check declares, beyond the standing ones.
    ///
    /// The column carries typed accessors rather than the name of a fact to parse.
    /// <see cref="Finding.Facts"/> is for display and export: the strings there are written with a
    /// display format, so they are already quantised away from what the check measured, they merge
    /// additively and never expire, and nothing in them says what unit they are in. A board that
    /// banded or filtered on one of them would eventually band a foot-unit model against a
    /// millimetre threshold and be silently wrong. So <see cref="Number"/> and <see cref="Text"/>
    /// are what the board reads, and <see cref="Key"/> is only where the value exports under.
    ///
    /// Every length a check hands over is metres at full precision, host-read or file-read, so a
    /// column's number means the same thing whichever leg produced the finding.
    /// </summary>
    public sealed class ResultColumn
    {
        /// <summary>Declare a column.</summary>
        /// <param name="key">The <see cref="Finding.Facts"/> key it exports under.</param>
        /// <param name="title">The column heading.</param>
        /// <param name="kind">What kind of value it holds.</param>
        public ResultColumn(string key, string title, ColumnKind kind)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));

            Key = key;
            Title = string.IsNullOrWhiteSpace(title) ? key : title;
            Kind = kind;
        }

        /// <summary>
        /// The <see cref="Finding.Facts"/> key this column exports under. Display and spreadsheet
        /// only: the board reads <see cref="Number"/> or <see cref="Text"/>.
        /// </summary>
        public string Key { get; }

        /// <summary>The column heading.</summary>
        public string Title { get; }

        /// <summary>The unit the numbers are shown in, when saying so helps. Null when it does not.</summary>
        public string? Unit { get; set; }

        /// <summary>What kind of value it holds.</summary>
        public ColumnKind Kind { get; }

        /// <summary>How wide to draw it.</summary>
        public double Width { get; set; }

        /// <summary>Whether the value is drawn against the right edge, as numbers are.</summary>
        public bool RightAligned { get; set; }

        /// <summary>
        /// For <see cref="ColumnKind.Element"/>: which of <see cref="Finding.Elements"/> it draws.
        /// </summary>
        public int ElementIndex { get; set; }

        /// <summary>
        /// The value, typed, and in metres where it is a length. Sorting, range filtering and
        /// banding all read this. Null means the column has no number and the board reads
        /// <see cref="Text"/>.
        /// </summary>
        public Func<Finding, double?>? Number { get; set; }

        /// <summary>The value as words, where there is no number. Null means read <see cref="Number"/>.</summary>
        public Func<Finding, string?>? Text { get; set; }

        /// <summary>How a number is written for this column, given the project's units.</summary>
        public Func<double, string>? Format { get; set; }

        /// <summary>
        /// Which band a value falls in, for the range filter offered on a numeric column. Null
        /// means the column sorts but does not band.
        /// </summary>
        public Func<double, string>? Bucket { get; set; }

        /// <summary>Worse than this run's threshold. Null means the column is never toned.</summary>
        public Func<Finding, bool>? IsBad { get; set; }

        /// <summary>
        /// This column's value for one finding, as the board draws it.
        ///
        /// A column with no <see cref="Format"/> writes its number invariantly, because the
        /// project's unit system is not this type's to choose: a check that wants feet says so in
        /// its own <see cref="Format"/>.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public string? Show(Finding? finding)
        {
            if (finding == null) return null;

            var text = Text?.Invoke(finding);

            if (text != null) return text;

            var number = Number?.Invoke(finding);

            if (number == null) return null;

            return Format != null
                ? Format(number.Value)
                : number.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override string ToString() => Title + " (" + Kind + ")";
    }
}
