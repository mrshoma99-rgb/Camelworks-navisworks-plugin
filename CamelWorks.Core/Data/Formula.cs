using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CamelWorks.Core.Data
{
    /// <summary>What one evaluation produced.</summary>
    public sealed class FormulaResult
    {
        internal FormulaResult(string? value, string? problem, IReadOnlyList<PropertyRef> missing)
        {
            Value = value; Problem = problem; Missing = missing;
        }

        /// <summary>The value, or null when there is not one.</summary>
        public string? Value { get; }

        /// <summary>
        /// Why there is no value, or null when there is one.
        ///
        /// Separate from a missing property: a formula that could not be worked out because the
        /// element does not carry something is an ordinary state of a federation, and one that
        /// divides by zero is a formula somebody needs to fix. Reporting both as "no value" would
        /// bury the second in the first.
        /// </summary>
        public string? Problem { get; }

        /// <summary>Which properties the element did not carry.</summary>
        public IReadOnlyList<PropertyRef> Missing { get; }

        /// <summary>True when there is a value to write.</summary>
        public bool HasValue => Value != null;

        /// <inheritdoc />
        public override string ToString() => Value ?? Problem ?? "(nothing)";
    }

    /// <summary>A property a formula reads, as it was written.</summary>
    public readonly struct PropertyRef : IEquatable<PropertyRef>
    {
        /// <summary>Create a reference.</summary>
        /// <param name="category">The property tab, or empty for any.</param>
        /// <param name="name">The property name.</param>
        public PropertyRef(string category, string name)
        {
            Category = category ?? string.Empty;
            Name = name ?? string.Empty;
        }

        /// <summary>The property tab, or empty when the formula did not say.</summary>
        public string Category { get; }

        /// <summary>The property name.</summary>
        public string Name { get; }

        /// <inheritdoc />
        public bool Equals(PropertyRef other) =>
            string.Equals(Category, other.Category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PropertyRef other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            (StringComparer.OrdinalIgnoreCase.GetHashCode(Category) * 397)
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

        /// <summary>Equality.</summary>
        public static bool operator ==(PropertyRef a, PropertyRef b) => a.Equals(b);

        /// <summary>Inequality.</summary>
        public static bool operator !=(PropertyRef a, PropertyRef b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            "{" + (Category.Length == 0 ? Name : Category + "/" + Name) + "}";
    }

    /// <summary>
    /// A small language for making one property out of the others.
    ///
    /// This is how a work breakdown code, a sort key, a door reference or a derived quantity
    /// actually gets built: not by a mapping table, but by sticking two properties together with a
    /// dash in the middle, or multiplying a length by a width. Doing it by hand is an export to
    /// Excel, a column of formulas, and an import — which is a round trip nobody repeats when the
    /// model changes.
    ///
    /// Deliberately small, and deliberately strict about two things:
    ///
    /// <list type="bullet">
    /// <item><b><c>+</c> adds and <c>&amp;</c> joins.</b> One operator that did both would have to
    /// guess, and the guess is wrong exactly when the values look like numbers but are not — a door
    /// reference of "12" and a level of "3" becoming 15 rather than "12-3".</item>
    /// <item><b>A missing property produces nothing, never zero.</b> An element that does not carry
    /// a width has no area, and writing zero would put a number into a schedule that somebody adds
    /// up. The element is skipped and counted, which is the same rule the takeoff follows.</item>
    /// </list>
    ///
    /// What it is not: a spreadsheet. No functions, no conditionals, no ranges. Anything that needs
    /// those needs a real one, and pretending otherwise produces a language nobody can debug from
    /// a single-line text box.
    /// </summary>
    public sealed class Formula
    {
        private readonly Node _root;

        private Formula(Node root, IReadOnlyList<PropertyRef> references)
        {
            _root = root;
            References = references;
        }

        /// <summary>Every property this formula reads.</summary>
        public IReadOnlyList<PropertyRef> References { get; }

        /// <summary>Why the text could not be read as a formula, or null when it could.</summary>
        public string? Problem { get; private set; }

        /// <summary>True when there is a formula to evaluate.</summary>
        public bool IsUsable => Problem == null;

        /// <summary>
        /// Read a formula. Never throws: a formula somebody is halfway through typing is the normal
        /// state of the box it is typed into.
        /// </summary>
        /// <param name="text">The formula.</param>
        public static Formula Parse(string? text)
        {
            var references = new List<PropertyRef>();

            if (string.IsNullOrWhiteSpace(text))
                return Broken("There is no formula yet.", references);

            try
            {
                var tokens = Scan(text!, references);
                var at = 0;
                var root = ParseJoin(tokens, ref at);

                if (at < tokens.Count)
                    return Broken("There is something after the end of the formula: "
                                  + tokens[at].Text, references);

                return new Formula(root, references);
            }
            catch (FormatException e)
            {
                return Broken(e.Message, references);
            }
        }

        /// <summary>
        /// Work the formula out for one element.
        /// </summary>
        /// <param name="property">
        /// Reads one property: category, name, and null when the element does not carry it. An
        /// empty category means "any tab", which is what a formula that only names a property says.
        /// </param>
        public FormulaResult Evaluate(Func<string, string, string?> property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (Problem != null) return new FormulaResult(null, Problem, Array.Empty<PropertyRef>());

            var missing = new List<PropertyRef>();

            try
            {
                var value = _root.Evaluate(property, missing);

                return missing.Count > 0
                    ? new FormulaResult(null, null, missing)
                    : new FormulaResult(value.ToText(), null, missing);
            }
            catch (FormatException e)
            {
                return new FormulaResult(null, e.Message, missing);
            }
        }

        /// <inheritdoc />
        public override string ToString() => Problem ?? _root.Describe();

        private static Formula Broken(string problem, IReadOnlyList<PropertyRef> references) =>
            new Formula(new TextNode(string.Empty), references) { Problem = problem };

        // -------------------------------------------------------------------------------
        // Scanning
        // -------------------------------------------------------------------------------

        private enum Kind { Property, Number, Text, Operator, Open, Close }

        private readonly struct Token
        {
            internal Token(Kind kind, string text, PropertyRef reference = default, double number = 0)
            {
                Kind = kind; Text = text; Reference = reference; Number = number;
            }

            internal Kind Kind { get; }
            internal string Text { get; }
            internal PropertyRef Reference { get; }
            internal double Number { get; }
        }

        private static List<Token> Scan(string text, ICollection<PropertyRef> references)
        {
            var tokens = new List<Token>();
            var at = 0;

            while (at < text.Length)
            {
                var c = text[at];

                if (char.IsWhiteSpace(c)) { at++; continue; }

                if (c == '{')
                {
                    var close = text.IndexOf('}', at);
                    if (close < 0) throw new FormatException("A { was opened and never closed.");

                    var inside = text.Substring(at + 1, close - at - 1).Trim();
                    if (inside.Length == 0) throw new FormatException("An empty {} names no property.");

                    var slash = inside.LastIndexOf('/');

                    var reference = slash < 0
                        ? new PropertyRef(string.Empty, inside)
                        : new PropertyRef(inside.Substring(0, slash).Trim(), inside.Substring(slash + 1).Trim());

                    if (reference.Name.Length == 0)
                        throw new FormatException("A property reference has a tab but no name.");

                    if (!references.Contains(reference)) references.Add(reference);

                    tokens.Add(new Token(Kind.Property, inside, reference));
                    at = close + 1;
                    continue;
                }

                if (c == '"')
                {
                    var close = text.IndexOf('"', at + 1);
                    if (close < 0) throw new FormatException("A quote was opened and never closed.");

                    tokens.Add(new Token(Kind.Text, text.Substring(at + 1, close - at - 1)));
                    at = close + 1;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && at + 1 < text.Length && char.IsDigit(text[at + 1])))
                {
                    var start = at;
                    while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;

                    var literal = text.Substring(start, at - start);

                    if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        throw new FormatException("\"" + literal + "\" is not a number.");

                    tokens.Add(new Token(Kind.Number, literal, default, number));
                    continue;
                }

                if (c == '(') { tokens.Add(new Token(Kind.Open, "(")); at++; continue; }
                if (c == ')') { tokens.Add(new Token(Kind.Close, ")")); at++; continue; }

                if (c == '+' || c == '-' || c == '*' || c == '/' || c == '&')
                {
                    tokens.Add(new Token(Kind.Operator, c.ToString()));
                    at++;
                    continue;
                }

                throw new FormatException("\"" + c + "\" is not something a formula can contain.");
            }

            if (tokens.Count == 0) throw new FormatException("There is no formula yet.");

            return tokens;
        }

        // -------------------------------------------------------------------------------
        // Parsing: join, then add, then multiply, then atoms
        // -------------------------------------------------------------------------------

        private static Node ParseJoin(List<Token> tokens, ref int at)
        {
            var left = ParseAdd(tokens, ref at);

            while (at < tokens.Count && tokens[at].Kind == Kind.Operator && tokens[at].Text == "&")
            {
                at++;
                left = new JoinNode(left, ParseAdd(tokens, ref at));
            }

            return left;
        }

        private static Node ParseAdd(List<Token> tokens, ref int at)
        {
            var left = ParseMultiply(tokens, ref at);

            while (at < tokens.Count && tokens[at].Kind == Kind.Operator
                   && (tokens[at].Text == "+" || tokens[at].Text == "-"))
            {
                var op = tokens[at].Text;
                at++;
                left = new MathNode(left, ParseMultiply(tokens, ref at), op[0]);
            }

            return left;
        }

        private static Node ParseMultiply(List<Token> tokens, ref int at)
        {
            var left = ParseAtom(tokens, ref at);

            while (at < tokens.Count && tokens[at].Kind == Kind.Operator
                   && (tokens[at].Text == "*" || tokens[at].Text == "/"))
            {
                var op = tokens[at].Text;
                at++;
                left = new MathNode(left, ParseAtom(tokens, ref at), op[0]);
            }

            return left;
        }

        private static Node ParseAtom(List<Token> tokens, ref int at)
        {
            if (at >= tokens.Count) throw new FormatException("The formula stops in the middle.");

            var token = tokens[at];

            switch (token.Kind)
            {
                case Kind.Property: at++; return new PropertyNode(token.Reference);
                case Kind.Number: at++; return new NumberNode(token.Number);
                case Kind.Text: at++; return new TextNode(token.Text);

                case Kind.Open:
                {
                    at++;
                    var inner = ParseJoin(tokens, ref at);

                    if (at >= tokens.Count || tokens[at].Kind != Kind.Close)
                        throw new FormatException("A ( was opened and never closed.");

                    at++;
                    return inner;
                }

                case Kind.Operator when token.Text == "-":
                {
                    at++;
                    return new MathNode(new NumberNode(0), ParseAtom(tokens, ref at), '-');
                }

                default:
                    throw new FormatException("\"" + token.Text + "\" cannot start a value.");
            }
        }

        // -------------------------------------------------------------------------------
        // The tree
        // -------------------------------------------------------------------------------

        /// <summary>A value while a formula is being worked out: a number, or text.</summary>
        private readonly struct Slot
        {
            internal Slot(double number) { Number = number; Text = null; }
            internal Slot(string text) { Number = 0; Text = text; }

            internal double Number { get; }
            internal string? Text { get; }

            internal bool IsNumber => Text == null;

            internal double AsNumber()
            {
                if (Text == null) return Number;

                if (double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                throw new FormatException("\"" + Text + "\" is not a number, so it cannot be added or multiplied. "
                                          + "Use & to join it to something instead.");
            }

            internal string ToText() =>
                Text ?? Number.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private abstract class Node
        {
            internal abstract Slot Evaluate(Func<string, string, string?> property, ICollection<PropertyRef> missing);

            internal abstract string Describe();
        }

        private sealed class NumberNode : Node
        {
            private readonly double _value;

            internal NumberNode(double value) => _value = value;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing) => new Slot(_value);

            internal override string Describe() => _value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private sealed class TextNode : Node
        {
            private readonly string _value;

            internal TextNode(string value) => _value = value;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing) => new Slot(_value);

            internal override string Describe() => "\"" + _value + "\"";
        }

        private sealed class PropertyNode : Node
        {
            private readonly PropertyRef _reference;

            internal PropertyNode(PropertyRef reference) => _reference = reference;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing)
            {
                var value = property(_reference.Category, _reference.Name);

                // Missing, not zero and not empty. An element that does not carry a width has no
                // area, and a zero would put a number into a schedule somebody adds up.
                if (string.IsNullOrWhiteSpace(value))
                {
                    missing.Add(_reference);
                    return new Slot(string.Empty);
                }

                return new Slot(value!.Trim());
            }

            internal override string Describe() => _reference.ToString();
        }

        private sealed class JoinNode : Node
        {
            private readonly Node _left;
            private readonly Node _right;

            internal JoinNode(Node left, Node right) { _left = left; _right = right; }

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing) =>
                new Slot(_left.Evaluate(property, missing).ToText()
                         + _right.Evaluate(property, missing).ToText());

            internal override string Describe() => _left.Describe() + " & " + _right.Describe();
        }

        private sealed class MathNode : Node
        {
            private readonly Node _left;
            private readonly Node _right;
            private readonly char _op;

            internal MathNode(Node left, Node right, char op) { _left = left; _right = right; _op = op; }

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing)
            {
                var left = _left.Evaluate(property, missing);
                var right = _right.Evaluate(property, missing);

                // Stop before parsing: a missing value is already the answer, and turning "" into a
                // number error would report the wrong problem.
                if (missing.Count > 0) return new Slot(0);

                var a = left.AsNumber();
                var b = right.AsNumber();

                switch (_op)
                {
                    case '+': return new Slot(a + b);
                    case '-': return new Slot(a - b);
                    case '*': return new Slot(a * b);

                    default:
                        if (b == 0) throw new FormatException("This formula divides by zero.");
                        return new Slot(a / b);
                }
            }

            internal override string Describe() =>
                "(" + _left.Describe() + " " + _op + " " + _right.Describe() + ")";
        }
    }
}
