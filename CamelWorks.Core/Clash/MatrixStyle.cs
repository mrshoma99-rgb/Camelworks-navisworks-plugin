using System;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>Whether a field of a line is drawn, and who decides.</summary>
    public enum MatrixField
    {
        /// <summary>
        /// Drawn when it says something the rest of the header does not.
        ///
        /// The default, and right nearly always: rows numbered 01, 02, 03 down the list are
        /// already numbered by the gutter beside them, and a code that is the front of its own
        /// name is three letters twice. Neither is worth a column until it differs.
        /// </summary>
        Auto,

        /// <summary>Always drawn, whatever it says.</summary>
        Shown,

        /// <summary>Never drawn, whatever it says.</summary>
        Hidden,
    }

    /// <summary>
    /// How a matrix is drawn: which half is worked in, what the parts are coloured, and which of a
    /// line's fields appear beside its name.
    ///
    /// Kept with the matrix rather than with the seat. A practice draws its matrices one way — the
    /// lower triangle, clearances in green, no code column because their set names carry the code —
    /// and that is a property of the matrix they send out rather than of the machine it was last
    /// opened on. Saved with the grid, it travels with it.
    ///
    /// Everything here is about drawing. None of it changes which tests are planned or built: a pair
    /// is written to both halves whichever half it was entered in, and a hidden number is still the
    /// number the test is named after.
    /// </summary>
    public sealed class MatrixStyle
    {
        /// <summary>The half of a mirrored matrix that is worked in.</summary>
        public bool UpperIsLive { get; set; } = true;

        /// <summary>
        /// Whether the dead half echoes what the live half says.
        ///
        /// On, a row can be read all the way across - half of what a discipline is checked
        /// against lives on the other axis. Off, the dead triangle is one empty shape the eye
        /// steps over. Both are defensible and people want different ones.
        /// </summary>
        public bool Echo { get; set; } = true;

        /// <summary>Whether the diagonal carries a ground of its own.</summary>
        public bool Spine { get; set; } = true;

        /// <summary>The diagonal's colour as <c>#RRGGBB</c>, or empty for the drawing's own.</summary>
        public string SpineColour { get; set; } = string.Empty;

        /// <summary>Whether a pair ruled out carries a ground of its own.</summary>
        public string RuledColour { get; set; } = string.Empty;

        /// <summary>Whether each line's number is drawn beside its name.</summary>
        public MatrixField Numbers { get; set; } = MatrixField.Auto;

        /// <summary>Whether each line's discipline code is drawn beside its name.</summary>
        public MatrixField Codes { get; set; } = MatrixField.Auto;

        /// <summary>Whether a line pointing at nothing is marked in the header.</summary>
        public MatrixField Warnings { get; set; } = MatrixField.Auto;

        /// <summary>
        /// Whether a field is drawn, given what the lines themselves say about it.
        /// </summary>
        /// <param name="field">What the style says.</param>
        /// <param name="worthIt">Whether the lines make it worth a column of its own.</param>
        public static bool Drawn(MatrixField field, bool worthIt) => field switch
        {
            MatrixField.Shown => true,
            MatrixField.Hidden => false,
            _ => worthIt,
        };

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("upper", UpperIsLive)
                .Set("echo", Echo)
                .Set("spine", Spine)
                .Set("spineColour", SpineColour)
                .Set("ruledColour", RuledColour)
                .Set("numbers", (double)(int)Numbers)
                .Set("codes", (double)(int)Codes)
                .Set("warnings", (double)(int)Warnings);

        /// <summary>Read one back, or the ordinary one when there is nothing stored.</summary>
        /// <param name="json">What was written.</param>
        public static MatrixStyle FromJson(JsonValue? json)
        {
            var style = new MatrixStyle();

            if (json == null || json.Kind != JsonKind.Object) return style;

            style.UpperIsLive = json["upper"].AsBool(true);
            style.Echo = json["echo"].AsBool(true);
            style.Spine = json["spine"].AsBool(true);
            style.SpineColour = json["spineColour"].AsString() ?? string.Empty;
            style.RuledColour = json["ruledColour"].AsString() ?? string.Empty;
            style.Numbers = Fielded(json["numbers"]);
            style.Codes = Fielded(json["codes"]);
            style.Warnings = Fielded(json["warnings"]);

            return style;
        }

        /// <summary>One stored field setting, or Auto for anything unrecognised.</summary>
        /// <param name="json">What was written for it.</param>
        private static MatrixField Fielded(JsonValue? json)
        {
            var at = (int)(json?.AsDouble() ?? 0);

            return at == (int)MatrixField.Shown ? MatrixField.Shown
                : at == (int)MatrixField.Hidden ? MatrixField.Hidden
                : MatrixField.Auto;
        }

        /// <inheritdoc />
        public override string ToString() =>
            (UpperIsLive ? "upper" : "lower") + " half"
            + (Echo ? ", echoed" : string.Empty)
            + (Spine ? ", spine" : string.Empty);
    }
}
