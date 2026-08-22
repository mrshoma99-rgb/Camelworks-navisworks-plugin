using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>What somebody decided should happen. Five verbs, because there are five.</summary>
    public enum ResolutionAction
    {
        /// <summary>Nobody has said. The honest default, and the one a report prints as such.</summary>
        NotStated = 0,

        /// <summary>Something moves, in a stated direction, by a stated amount.</summary>
        Move = 1,

        /// <summary>Something gets smaller. A 400 duct becomes a 300.</summary>
        Resize = 2,

        /// <summary>Something takes a different path entirely.</summary>
        Reroute = 3,

        /// <summary>Nothing moves yet, because a decision is outstanding.</summary>
        Hold = 4,

        /// <summary>This element stays. The clash is the other trade's to fix.</summary>
        OtherTradeMoves = 5,
    }

    /// <summary>Which way, along the project's own axes. Z reads as up and down, because it is.</summary>
    public enum ResolutionAxis
    {
        /// <summary>No direction stated.</summary>
        None = 0,

        /// <summary>Along +X.</summary>
        XPlus = 1,

        /// <summary>Along -X.</summary>
        XMinus = 2,

        /// <summary>Along +Y.</summary>
        YPlus = 3,

        /// <summary>Along -Y.</summary>
        YMinus = 4,

        /// <summary>Up.</summary>
        ZPlus = 5,

        /// <summary>Down.</summary>
        ZMinus = 6,
    }

    /// <summary>
    /// What to do about a clash, in a shape a machine can carry and a person can act on.
    ///
    /// A coordination report that names a problem and stops is a report the reader has to solve
    /// again. Every board in the industry has a free-text comment box for this, and free text is
    /// why the instruction never reaches the model: "lower duct" cannot be drawn as an arrow,
    /// cannot be checked against the next revision, and means three different things to the three
    /// people who read it.
    ///
    /// So it is typed: a verb, the element it applies to, a direction and an amount. That is
    /// enough for one bold line under the group image, one arrow in the view, and one column in
    /// the field pack — from a single record that somebody set in review mode in about four
    /// seconds.
    ///
    /// It is deliberately not a workflow. There is no request, no approval, no state machine:
    /// a decision was made and this is what it was.
    /// </summary>
    public sealed class Resolution
    {
        /// <summary>What a group with no instruction prints.</summary>
        public const string Unstated = "Resolution not stated";

        /// <summary>Create an instruction.</summary>
        /// <param name="action">The verb.</param>
        /// <param name="subject">What it applies to, as the board names it. Null means the group as a whole.</param>
        /// <param name="axis">Which way, when the verb takes a direction.</param>
        /// <param name="amount">How far, in metres.</param>
        /// <param name="note">Anything the four typed fields cannot hold.</param>
        /// <param name="subjectKey">
        /// The element the arrow starts at, as <c>ElementKey</c> writes itself. Kept apart from
        /// <paramref name="subject"/> because one of them is for a person to read and the other is
        /// for the view to draw, and a report line reading "Move 3f2a9c1e/44/17 150 mm down" helps
        /// nobody.
        /// </param>
        public Resolution(ResolutionAction action, string? subject = null,
                          ResolutionAxis axis = ResolutionAxis.None, double? amount = null,
                          string? note = null, string? subjectKey = null)
        {
            Action = action;
            Subject = Trimmed(subject);
            Axis = axis;
            Amount = amount.HasValue && IsReal(amount.Value) ? amount : null;
            Note = Trimmed(note);
            SubjectKey = Trimmed(subjectKey);
        }

        /// <summary>The verb.</summary>
        public ResolutionAction Action { get; }

        /// <summary>What it applies to, or null for the group as a whole.</summary>
        public string? Subject { get; }

        /// <summary>Which way.</summary>
        public ResolutionAxis Axis { get; }

        /// <summary>How far, in metres.</summary>
        public double? Amount { get; }

        /// <summary>Anything the typed fields cannot hold.</summary>
        public string? Note { get; }

        /// <summary>The element the arrow starts at, as <c>ElementKey</c> writes itself.</summary>
        public string? SubjectKey { get; }

        /// <summary>Nothing said. What every group starts with.</summary>
        public static Resolution None { get; } = new Resolution(ResolutionAction.NotStated);

        /// <summary>True when somebody actually decided something.</summary>
        public bool IsStated => Action != ResolutionAction.NotStated;

        /// <summary>The verbs, in the order a picker should offer them.</summary>
        public static IReadOnlyList<string> Actions { get; } = new[]
        {
            "Not stated", "Move", "Resize", "Reroute", "Hold", "Other trade moves",
        };

        /// <summary>The directions, in the order a picker should offer them.</summary>
        public static IReadOnlyList<string> Directions { get; } = new[]
        {
            "—", "+X", "-X", "+Y", "-Y", "up", "down",
        };

        /// <summary>
        /// The unit vector to draw, or null when there is no direction to draw.
        ///
        /// A direction without an amount still draws: an arrow saying "that way" is worth having,
        /// and the caller scales it to whatever reads at the view's zoom.
        /// </summary>
        public (double X, double Y, double Z)? Direction
        {
            get
            {
                switch (Axis)
                {
                    case ResolutionAxis.XPlus: return (1, 0, 0);
                    case ResolutionAxis.XMinus: return (-1, 0, 0);
                    case ResolutionAxis.YPlus: return (0, 1, 0);
                    case ResolutionAxis.YMinus: return (0, -1, 0);
                    case ResolutionAxis.ZPlus: return (0, 0, 1);
                    case ResolutionAxis.ZMinus: return (0, 0, -1);
                    default: return null;
                }
            }
        }

        /// <summary>
        /// The one line that goes under the group image.
        ///
        /// Written as an instruction to a person, not as a record dump: "Move the 400 duct 150 mm
        /// down" is something a foreman can act on, and "Move / 400 duct / ZMinus / 0.15" is not.
        /// </summary>
        /// <param name="units">How this project writes a length.</param>
        public string Describe(Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            if (!IsStated) return Unstated;

            var what = Subject ?? "this element";
            var far = Amount.HasValue ? measure.Length(Amount.Value) : null;
            var way = Way();

            string line;

            switch (Action)
            {
                case ResolutionAction.Move:
                    line = "Move " + what
                         + (far == null ? string.Empty : " " + far)
                         + (way == null ? string.Empty : " " + way);
                    break;

                case ResolutionAction.Resize:
                    line = "Resize " + what + (far == null ? string.Empty : " by " + far);
                    break;

                case ResolutionAction.Reroute:
                    line = "Reroute " + what;
                    break;

                case ResolutionAction.Hold:
                    line = "Hold " + what + " - no change until this is decided";
                    break;

                default:
                    line = what + " stays - the other trade moves";
                    break;
            }

            if (Note != null) line += " (" + Note + ")";

            return line;
        }

        /// <summary>How the direction reads in a sentence, or null when none was stated.</summary>
        private string? Way()
        {
            switch (Axis)
            {
                case ResolutionAxis.XPlus: return "in +X";
                case ResolutionAxis.XMinus: return "in -X";
                case ResolutionAxis.YPlus: return "in +Y";
                case ResolutionAxis.YMinus: return "in -Y";
                case ResolutionAxis.ZPlus: return "up";
                case ResolutionAxis.ZMinus: return "down";
                default: return null;
            }
        }

        /// <summary>Read a verb from what a picker or a saved file says. Anything unknown is "not stated".</summary>
        /// <param name="word">The written form.</param>
        public static ResolutionAction ActionOf(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return ResolutionAction.NotStated;

            var text = word!.Trim().Replace(" ", string.Empty);

            return Enum.TryParse<ResolutionAction>(text, ignoreCase: true, out var action)
                ? action
                : ResolutionAction.NotStated;
        }

        /// <summary>Read a direction from what a picker or a saved file says.</summary>
        /// <param name="word">The written form.</param>
        public static ResolutionAxis AxisOf(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return ResolutionAxis.None;

            switch (word!.Trim().ToLowerInvariant())
            {
                case "+x": case "xplus": return ResolutionAxis.XPlus;
                case "-x": case "xminus": return ResolutionAxis.XMinus;
                case "+y": case "yplus": return ResolutionAxis.YPlus;
                case "-y": case "yminus": return ResolutionAxis.YMinus;
                case "+z": case "up": case "zplus": return ResolutionAxis.ZPlus;
                case "-z": case "down": case "zminus": return ResolutionAxis.ZMinus;
                default: return ResolutionAxis.None;
            }
        }

        /// <summary>How the verb reads in a picker.</summary>
        public string ActionWord => Actions[(int)Action];

        /// <summary>How the direction reads in a picker.</summary>
        public string DirectionWord => Directions[(int)Axis];

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("action", Action.ToString());

            if (Subject != null) json.Set("subject", JsonValue.String(Subject));
            if (Axis != ResolutionAxis.None) json.Set("axis", JsonValue.String(Axis.ToString()));
            if (Amount.HasValue) json.Set("amount", JsonValue.Number(Amount.Value));
            if (Note != null) json.Set("note", JsonValue.String(Note));
            if (SubjectKey != null) json.Set("at", JsonValue.String(SubjectKey));

            return json;
        }

        /// <summary>Read one back. Anything unreadable is <see cref="None"/> rather than an error.</summary>
        /// <param name="json">What was written.</param>
        public static Resolution FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return None;

            var action = ActionOf(json["action"].AsString());
            if (action == ResolutionAction.NotStated) return None;

            var amount = json["amount"];

            return new Resolution(
                action,
                json["subject"].AsString(),
                AxisOf(json["axis"].AsString()),
                amount.Kind == JsonKind.Number ? amount.AsDouble() : (double?)null,
                json["note"].AsString(),
                json["at"].AsString());
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        private static string? Trimmed(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        private static bool IsReal(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
