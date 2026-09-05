using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>How a field gets its value on each line.</summary>
    public enum MatrixFill
    {
        /// <summary>Typed on the line, one line at a time.</summary>
        ByHand = 0,

        /// <summary>Counted down the axis from a starting value.</summary>
        Series = 1,

        /// <summary>The same value on every line.</summary>
        Same = 2,
    }

    /// <summary>What a series counts in.</summary>
    public enum MatrixSteps
    {
        /// <summary>1, 2, 3 — or 10, 20, 30 at a step of ten.</summary>
        Numbers = 0,

        /// <summary>A, B, C, and on past Z as AA, AB.</summary>
        Letters = 1,
    }

    /// <summary>
    /// One field a line carries, and how it is filled in.
    ///
    /// A matrix line used to hold exactly three things a person could type — a name, a number and
    /// a discipline code — and every job that files its tests by anything else had nowhere to put
    /// it. A sub-discipline code, a phase, a package, a zone: all of them appear in the test names
    /// the project already writes, and none of them had a field, so the naming pattern could not
    /// reach them and a read could not recover them.
    ///
    /// The four the board has always had are fields like any other. They keep writing to the
    /// properties the rest of the product reads, so nothing outside this file changes, and in
    /// return they gain what a custom field has: a title of the project's own, a fill rule, and a
    /// series. They cannot be deleted, because the board's headers, its diff and its export all
    /// name them.
    ///
    /// The fill rule is the point of the type. Numbering forty rows by hand is forty edits, and
    /// changing where the numbering starts is forty more — which is the cost that stops anybody
    /// renumbering a matrix once it is drawn.
    /// </summary>
    public sealed class MatrixField
    {
        /// <summary>The line's name. Always present, never deleted.</summary>
        public const string NameKey = "name";

        /// <summary>The line's number.</summary>
        public const string NumberKey = "number";

        /// <summary>The line's discipline code.</summary>
        public const string CodeKey = "code";

        /// <summary>The folder the line is filed in.</summary>
        public const string FolderKey = "folder";

        /// <summary>The four the board has always carried, which cannot be removed.</summary>
        public static IReadOnlyList<string> BuiltIn { get; } =
            new[] { NameKey, NumberKey, CodeKey, FolderKey };

        /// <summary>Describe a field.</summary>
        /// <param name="key">Its key, which is what a token names.</param>
        /// <param name="title">What it is called on screen.</param>
        public MatrixField(string key, string? title = null)
        {
            Key = Clean(key);

            if (Key.Length == 0) throw new ArgumentException("a field needs a key", nameof(key));

            Title = string.IsNullOrWhiteSpace(title) ? Titled(Key) : title!.Trim();
        }

        /// <summary>
        /// The key, which is what a naming token is built from and what a saved board stores.
        ///
        /// Letters and digits only, first character a letter, and compared without case. A token
        /// is written into a pattern as <c>{rowPhase}</c>, so a key with a space or a brace in it
        /// would produce a token that cannot be parsed back.
        /// </summary>
        public string Key { get; }

        /// <summary>What it is called on screen. The project's word, not ours.</summary>
        public string Title { get; set; }

        /// <summary>Whether this is one of the four the board cannot do without.</summary>
        public bool IsBuiltIn => BuiltIn.Contains(Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>How each line's value is arrived at.</summary>
        public MatrixFill Fill { get; set; } = MatrixFill.ByHand;

        /// <summary>What a series counts in.</summary>
        public MatrixSteps Steps { get; set; } = MatrixSteps.Numbers;

        /// <summary>What the first line in a series gets. "10", or "A".</summary>
        public string Start { get; set; } = "1";

        /// <summary>How much each line adds to the one before it.</summary>
        public int Step { get; set; } = 1;

        /// <summary>
        /// The width a number is padded to with leading zeros, or 0 for none.
        ///
        /// A job that writes ST-01 rather than ST-1 sorts correctly in every tool that sorts by
        /// text, which is most of them, so the padding is part of the value rather than of how it
        /// is drawn.
        /// </summary>
        public int Pad { get; set; }

        /// <summary>The value every line gets when <see cref="Fill"/> is <see cref="MatrixFill.Same"/>.</summary>
        public string Same { get; set; } = string.Empty;

        /// <summary>
        /// Whether a series begins again at <see cref="Start"/> in each folder.
        ///
        /// A flat list of disciplines wants one run down the axis. A board grouped into packages
        /// usually wants the opposite — the architectural rows numbered 01, 02, 03 and the
        /// structural rows numbered 01, 02, 03 — because the folder is already carrying what
        /// tells them apart.
        /// </summary>
        public bool PerFolder { get; set; }

        /// <summary>The token that reads this field off a row.</summary>
        public string RowToken => "{row" + Upper(Key) + "}";

        /// <summary>The token that reads this field off a column.</summary>
        public string ColumnToken => "{column" + Upper(Key) + "}";

        /// <summary>Whether a key is one a token can be built from.</summary>
        /// <param name="key">The key to check.</param>
        public static bool IsUsable(string? key)
        {
            var said = (key ?? string.Empty).Trim();

            return said.Length > 0 && char.IsLetter(said[0])
                   && said.All(one => char.IsLetterOrDigit(one));
        }

        /// <summary>The value the given position in a series takes.</summary>
        /// <param name="at">How many lines have come before it, from zero.</param>
        public string At(int at)
        {
            if (Fill == MatrixFill.Same) return Same ?? string.Empty;
            if (Fill != MatrixFill.Series || at < 0) return string.Empty;

            return Steps == MatrixSteps.Letters ? Lettered(at) : Numbered(at);
        }

        private string Numbered(int at)
        {
            var from = long.TryParse((Start ?? string.Empty).Trim(), NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out var held)
                ? held
                : 1;

            var value = from + (long)at * Step;
            var said = Math.Abs(value).ToString(CultureInfo.InvariantCulture);

            if (Pad > 0 && said.Length < Pad) said = said.PadLeft(Pad, '0');

            return value < 0 ? "-" + said : said;
        }

        private string Lettered(int at)
        {
            // Counted in the spreadsheet's own alphabet: A to Z, then AA. A start nobody typed
            // begins at A rather than refusing, since an empty letter series has no useful other
            // reading.
            var from = Ordinal((Start ?? string.Empty).Trim());

            if (from <= 0) from = 1;

            var value = from + (long)at * Step;

            return value < 1 ? string.Empty : Alphabetic(value);
        }

        /// <summary>Where a letter sequence falls, from one. Zero when it is not letters.</summary>
        /// <param name="said">The letters.</param>
        internal static long Ordinal(string? said)
        {
            var text = (said ?? string.Empty).Trim();

            if (text.Length == 0 || !text.All(char.IsLetter)) return 0;

            long held = 0;

            foreach (var one in text.ToUpperInvariant())
                held = held * 26 + (one - 'A' + 1);

            return held;
        }

        /// <summary>The letters at a position, from one.</summary>
        /// <param name="value">The position.</param>
        internal static string Alphabetic(long value)
        {
            var held = new StringBuilder();

            while (value > 0)
            {
                // Bijective base 26: there is no zero digit, so Z rolls to AA rather than to BA.
                var digit = (int)((value - 1) % 26);

                held.Insert(0, (char)('A' + digit));
                value = (value - 1) / 26;
            }

            return held.ToString();
        }

        /// <summary>A copy of this field.</summary>
        public MatrixField Copy() => new MatrixField(Key, Title)
        {
            Fill = Fill,
            Steps = Steps,
            Start = Start,
            Step = Step,
            Pad = Pad,
            Same = Same,
            PerFolder = PerFolder,
        };

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("key", Key).Set("title", Title);

            if (Fill != MatrixFill.ByHand)
                json.Set("fill", Fill.ToString().ToLowerInvariant());

            if (Fill == MatrixFill.Series)
            {
                if (Steps != MatrixSteps.Numbers)
                    json.Set("steps", Steps.ToString().ToLowerInvariant());

                json.Set("start", Start ?? string.Empty);

                if (Step != 1) json.Set("step", (long)Step);
                if (Pad > 0) json.Set("pad", (long)Pad);
                if (PerFolder) json.Set("perFolder", true);
            }

            if (Fill == MatrixFill.Same) json.Set("same", Same ?? string.Empty);

            return json;
        }

        /// <summary>Read one back, or null when there is no usable key.</summary>
        /// <param name="json">What was written.</param>
        public static MatrixField? FromJson(JsonValue? json)
        {
            if (json == null) return null;

            var key = json["key"].AsString();

            if (!IsUsable(key)) return null;

            var field = new MatrixField(key!, json["title"].AsString());

            field.Fill = (json["fill"].AsString() ?? string.Empty).ToLowerInvariant() switch
            {
                "series" => MatrixFill.Series,
                "same" => MatrixFill.Same,
                _ => MatrixFill.ByHand,
            };

            field.Steps = (json["steps"].AsString() ?? string.Empty).ToLowerInvariant() == "letters"
                ? MatrixSteps.Letters
                : MatrixSteps.Numbers;

            field.Start = json["start"].AsString() ?? "1";
            field.Step = (int)json["step"].AsDouble(1);
            field.Pad = (int)json["pad"].AsDouble(0);
            field.Same = json["same"].AsString() ?? string.Empty;
            field.PerFolder = json["perFolder"].AsBool(false);

            if (field.Step == 0) field.Step = 1;

            return field;
        }

        /// <summary>A key reduced to what a token can carry.</summary>
        private static string Clean(string? said)
        {
            var text = (said ?? string.Empty).Trim();
            var held = new StringBuilder();

            foreach (var one in text)
                if (char.IsLetterOrDigit(one))
                    held.Append(one);

            var made = held.ToString();

            // A key has to start with a letter for its token to read as a word.
            while (made.Length > 0 && !char.IsLetter(made[0])) made = made.Substring(1);

            return made;
        }

        /// <summary>A key as a title, for a field nobody has named.</summary>
        private static string Titled(string key) =>
            key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

        /// <summary>A key with its first letter raised, for building a token.</summary>
        private static string Upper(string key) =>
            key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

        /// <inheritdoc />
        public override string ToString() => Title + " {" + Key + "}";
    }
}
