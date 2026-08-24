using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CamelWorks.Core.Measurement;

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
        private readonly string? _category;
        private readonly string? _name;

        /// <summary>Create a reference.</summary>
        /// <param name="category">The property tab, or empty for any.</param>
        /// <param name="name">The property name.</param>
        public PropertyRef(string category, string name)
        {
            _category = category;
            _name = name;
        }

        /// <summary>
        /// The property tab, or empty when the formula did not say.
        ///
        /// Read through a field rather than held in an auto-property, because a struct has a
        /// default value nobody constructs: `default(PropertyRef)` runs no constructor, so an
        /// auto-property normalised in one would still be null - and the whole point of this type
        /// promising a string is that a caller may say `.Name.Length` without a guard. That is not
        /// hypothetical; it is two crashes, both of them a screen leaving a reference unset.
        /// </summary>
        public string Category => _category ?? string.Empty;

        /// <summary>The property name, or empty when there is none.</summary>
        public string Name => _name ?? string.Empty;

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
        /// <param name="lookup">
        /// Reads a code table: table name, value, and null when that table does not list it.
        /// Supplied by the caller rather than reached for, so this stays an expression evaluator
        /// with no idea what a project is. Null means <c>lookup()</c> has nothing to read and says
        /// so rather than silently passing the value through — passing an unlisted code through is
        /// exactly what a code table exists to stop.
        /// </param>
        public FormulaResult Evaluate(Func<string, string, string?> property,
                                      Func<string, string, string?>? lookup = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (Problem != null) return new FormulaResult(null, Problem, Array.Empty<PropertyRef>());

            var missing = new List<PropertyRef>();

            try
            {
                var value = _root.Evaluate(property, missing, lookup);

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

        private enum Kind { Property, Number, Text, Operator, Open, Close, Name, Comma }

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
                if (c == ',') { tokens.Add(new Token(Kind.Comma, ",")); at++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = at;
                    while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;

                    tokens.Add(new Token(Kind.Name, text.Substring(start, at - start)));
                    continue;
                }

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

                case Kind.Name:
                {
                    at++;

                    if (at >= tokens.Count || tokens[at].Kind != Kind.Open)
                        throw new FormatException("\"" + token.Text + "\" is not a property - a property "
                            + "goes in braces, like {Element/Mark} - and it is not a function either, "
                            + "because nothing follows it in brackets.");

                    at++;

                    var arguments = new List<Node>();

                    if (at < tokens.Count && tokens[at].Kind != Kind.Close)
                    {
                        arguments.Add(ParseJoin(tokens, ref at));

                        while (at < tokens.Count && tokens[at].Kind == Kind.Comma)
                        {
                            at++;
                            arguments.Add(ParseJoin(tokens, ref at));
                        }
                    }

                    if (at >= tokens.Count || tokens[at].Kind != Kind.Close)
                        throw new FormatException("A ( was opened and never closed.");

                    at++;

                    return CallNode.Of(token.Text, arguments);
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
            internal abstract Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup);

            internal abstract string Describe();
        }

        private sealed class NumberNode : Node
        {
            private readonly double _value;

            internal NumberNode(double value) => _value = value;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup) => new Slot(_value);

            internal override string Describe() => _value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private sealed class TextNode : Node
        {
            private readonly string _value;

            internal TextNode(string value) => _value = value;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup) => new Slot(_value);

            internal override string Describe() => "\"" + _value + "\"";
        }

        private sealed class PropertyNode : Node
        {
            private readonly PropertyRef _reference;

            internal PropertyNode(PropertyRef reference) => _reference = reference;

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup)
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
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup) =>
                new Slot(_left.Evaluate(property, missing, lookup).ToText()
                         + _right.Evaluate(property, missing, lookup).ToText());

            internal override string Describe() => _left.Describe() + " & " + _right.Describe();
        }

        /// <summary>
        /// A named function over one or more values.
        ///
        /// Four of them, and each earns its place by being something a property genuinely needs
        /// doing to it before it means anything: a pattern pulled out of a name, a number moved
        /// between units, a value looked up in a table, and a level read off an ancestor.
        ///
        /// Everything else stays out. A formula language that grows a function per request ends up
        /// as a programming language nobody documents, and the moment a person needs a loop they
        /// needed a spreadsheet instead.
        /// </summary>
        private sealed class CallNode : Node
        {
            private readonly string _name;
            private readonly IReadOnlyList<Node> _arguments;

            private CallNode(string name, IReadOnlyList<Node> arguments)
            {
                _name = name;
                _arguments = arguments;
            }

            /// <summary>The functions a formula may call, and how many arguments each takes.</summary>
            internal static IReadOnlyList<string> Names { get; } =
                new[] { "regex", "unit", "lookup", "upper", "lower", "trim" };

            internal static Node Of(string name, IReadOnlyList<Node> arguments)
            {
                var known = name.ToLowerInvariant();

                if (!Names.Contains(known))
                    throw new FormatException("\"" + name + "\" is not a function this formula knows. "
                        + "The ones it does: " + string.Join(", ", Names) + ".");

                var wanted = Arity(known);

                if (arguments.Count < wanted.Least || arguments.Count > wanted.Most)
                    throw new FormatException("\"" + name + "\" takes "
                        + (wanted.Least == wanted.Most
                            ? wanted.Least.ToString(CultureInfo.InvariantCulture)
                              + (wanted.Least == 1 ? " argument" : " arguments")
                            : wanted.Least.ToString(CultureInfo.InvariantCulture) + " or "
                              + wanted.Most.ToString(CultureInfo.InvariantCulture) + " arguments")
                        + ", not " + arguments.Count.ToString(CultureInfo.InvariantCulture) + ".");

                return new CallNode(known, arguments);
            }

            private static (int Least, int Most) Arity(string name) => name switch
            {
                "regex" => (2, 3),
                "unit" => (3, 3),
                "lookup" => (2, 2),
                _ => (1, 1),
            };

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup)
            {
                var values = _arguments.Select(a => a.Evaluate(property, missing, lookup)).ToList();
                var first = values[0].ToText();

                switch (_name)
                {
                    case "upper": return new Slot(first.ToUpperInvariant());
                    case "lower": return new Slot(first.ToLowerInvariant());
                    case "trim": return new Slot(first.Trim());

                    case "regex":
                    {
                        var pattern = values[1].ToText();

                        Regex expression;

                        try
                        {
                            // A timeout, because a pattern typed by a person runs against every
                            // element in a federation and catastrophic backtracking would look
                            // exactly like a hang.
                            expression = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
                        }
                        catch (ArgumentException error)
                        {
                            throw new FormatException("\"" + pattern + "\" is not a pattern: " + error.Message);
                        }

                        try
                        {
                            if (values.Count == 3)
                                return new Slot(expression.Replace(first, values[2].ToText()));

                            var match = expression.Match(first);

                            // The first group when the pattern has one, the whole match when it does
                            // not: "what did the brackets catch" is what somebody writing a pattern
                            // over a name is asking.
                            if (!match.Success) return new Slot(string.Empty);

                            return new Slot(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value);
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            throw new FormatException("\"" + pattern + "\" takes too long to match. "
                                + "A pattern with nested repeats can take longer than the rest of the "
                                + "job put together.");
                        }
                    }

                    case "unit":
                    {
                        var from = Measure.MetresPer(values[1].ToText());
                        var to = Measure.MetresPer(values[2].ToText());

                        if (from == null)
                            throw new FormatException("\"" + values[1].ToText() + "\" is not a unit. "
                                + Measure.UnitWords);

                        if (to == null)
                            throw new FormatException("\"" + values[2].ToText() + "\" is not a unit. "
                                + Measure.UnitWords);

                        return new Slot(values[0].AsNumber() * from.Value / to.Value);
                    }

                    default:
                    {
                        // lookup: the value's row in a named table, or nothing when the table does
                        // not list it. Nothing rather than the value itself, because passing an
                        // unlisted code through is exactly what a code table exists to stop.
                        if (lookup == null)
                            throw new FormatException("lookup() needs a code table, and this screen "
                                + "has none to read. Project > Profile is where they live.");

                        return new Slot(lookup(values[1].ToText(), first) ?? string.Empty);
                    }
                }
            }

            internal override string Describe() =>
                _name + "(" + string.Join(", ", _arguments.Select(a => a.Describe())) + ")";
        }

        private sealed class MathNode : Node
        {
            private readonly Node _left;
            private readonly Node _right;
            private readonly char _op;

            internal MathNode(Node left, Node right, char op) { _left = left; _right = right; _op = op; }

            internal override Slot Evaluate(Func<string, string, string?> property,
                                            ICollection<PropertyRef> missing,
                                            Func<string, string, string?>? lookup)
            {
                var left = _left.Evaluate(property, missing, lookup);
                var right = _right.Evaluate(property, missing, lookup);

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
