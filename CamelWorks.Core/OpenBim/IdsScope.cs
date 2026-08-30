using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>What a scope is named by — the vocabulary a Navisworks model actually has.</summary>
    public enum IdsScopeKind
    {
        /// <summary>
        /// A saved set the document holds, search or selection.
        ///
        /// One kind for both on purpose: the host resolves a name to members and the difference
        /// between "a query re-run" and "a list recalled" is the host's business, not the rule's.
        /// A rule that says "the level 3 ductwork" should keep meaning that when somebody converts
        /// the set from one to the other.
        /// </summary>
        Set = 0,

        /// <summary>
        /// Whatever is selected in the host when the check runs.
        ///
        /// Saveable, but it cannot mean the same thing twice, and the editor says so: it is for
        /// the check somebody runs now on what is in front of them, not for the rule set the
        /// project keeps.
        /// </summary>
        Selection = 1,
    }

    /// <summary>
    /// Which elements a rule is about, said in the model's own terms rather than IFC's.
    ///
    /// <b>Why this exists.</b> An IDS says who a rule applies to with an entity facet — "every
    /// IfcWall" — and a Navisworks federation is a pile of NWCs from Revit, Tekla and Civil 3D
    /// where most elements have no IFC class at all. On that model every specification matches
    /// nothing and the whole check reports "not applicable", which is useless and, worse, reads
    /// as green.
    ///
    /// The requirements half already works there: a property facet is property matching and the
    /// projection already bridges it. Only applicability was IFC-bound. So this is the missing
    /// half — a rule can say "the elements in the Level 3 Ductwork set" and everything else about
    /// IDS works unchanged.
    ///
    /// <b>It does not go in the .ids file.</b> The published schema's applicability is a closed
    /// sequence of six facet elements with no extension point, so a scope written into one would
    /// make the file invalid for every other tool. It travels in a CamelWorks overlay beside the
    /// IDS instead, and the portable file stays portable.
    /// </summary>
    public sealed class IdsScope : IEquatable<IdsScope>
    {
        private IdsScope(IdsScopeKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        /// <summary>What names it.</summary>
        public IdsScopeKind Kind { get; }

        /// <summary>The set's name, or empty for the current selection.</summary>
        public string Name { get; }

        /// <summary>The scope naming a saved set.</summary>
        /// <param name="name">The set's name, as the document holds it.</param>
        public static IdsScope? Of(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : new IdsScope(IdsScopeKind.Set, name!.Trim());

        /// <summary>The scope naming whatever is selected when the check runs.</summary>
        public static IdsScope Selected { get; } =
            new IdsScope(IdsScopeKind.Selection, string.Empty);

        /// <summary>
        /// How this reads on the element it was stamped on, and in a rule.
        ///
        /// The kind is in the text rather than compared separately, so a set that somebody named
        /// "selection" cannot be mistaken for the current selection.
        /// </summary>
        public string Token => Kind == IdsScopeKind.Selection ? "@selection" : "set:" + Name;

        /// <summary>How it reads in a report.</summary>
        public string Describe() =>
            Kind == IdsScopeKind.Selection
                ? "what is selected"
                : "the \"" + Name + "\" set";

        /// <summary>Read a token back, or null when it names nothing.</summary>
        /// <param name="token">What <see cref="Token"/> wrote.</param>
        public static IdsScope? FromToken(string? token)
        {
            var text = (token ?? string.Empty).Trim();

            if (text.Length == 0) return null;
            if (string.Equals(text, "@selection", StringComparison.OrdinalIgnoreCase)) return Selected;

            return text.StartsWith("set:", StringComparison.OrdinalIgnoreCase)
                ? Of(text.Substring(4))
                : Of(text);
        }

        /// <inheritdoc />
        public bool Equals(IdsScope? other) =>
            other != null && Kind == other.Kind
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as IdsScope);

        /// <inheritdoc />
        public override int GetHashCode() =>
            ((int)Kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>
    /// Matches on which of the model's own groupings an element is in.
    ///
    /// <b>Applicability only.</b> Written as a requirement it would assert "this element must be
    /// in that set", which is a statement about how somebody organised their sets rather than
    /// about the building — and the sets are usually made by the same person the rule is checking.
    /// A check that grades a model on its own filing is worth nothing, so
    /// <see cref="IdsScopeSpecification.IsScoped"/> reads this off applicability and nothing reads
    /// it off requirements.
    /// </summary>
    public sealed class IdsScopeFacet : IdsFacet
    {
        /// <summary>Create the facet.</summary>
        /// <param name="scope">Which grouping.</param>
        public IdsScopeFacet(IdsScope scope) =>
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));

        /// <summary>Which grouping.</summary>
        public IdsScope Scope { get; }

        /// <inheritdoc />
        public override string Describe() => "in " + Scope.Describe();

        /// <inheritdoc />
        public override bool Matches(IdsElement element) =>
            element != null && element.Scopes.Contains(Scope.Token, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Always true.
        ///
        /// Whether an element is in a set is a fact the projection resolved for every element it
        /// offered, so there is no such thing as "the data was missing" here — only in or out.
        /// Saying otherwise would report a wrong-value failure for an element that is simply
        /// somewhere else.
        /// </summary>
        public override bool DataIsPresent(IdsElement element) => true;
    }

    /// <summary>Reading the scope off a specification, for the parts that must know.</summary>
    public static class IdsScopeSpecification
    {
        /// <summary>The scopes a specification is limited to, in the order written. Empty when none.</summary>
        /// <param name="specification">The specification.</param>
        public static IReadOnlyList<IdsScope> ScopesOf(IdsSpecification? specification) =>
            specification == null
                ? Array.Empty<IdsScope>()
                : specification.Applicability.OfType<IdsScopeFacet>().Select(f => f.Scope).ToList();

        /// <summary>Whether a specification is limited to the model's own groupings.</summary>
        /// <param name="specification">The specification.</param>
        public static bool IsScoped(IdsSpecification? specification) => ScopesOf(specification).Count > 0;

        /// <summary>
        /// Why a specification cannot be written into a .ids file, or null when it can.
        ///
        /// The published schema's applicability is a closed sequence of six facet elements with no
        /// extension point, so there is nowhere for a scope to go. Saying which rules cannot
        /// travel, and why, is the whole of the honesty here: the alternative is an invalid file
        /// or a silent drop.
        /// </summary>
        /// <param name="specification">The specification.</param>
        public static string? WhyNotPortable(IdsSpecification? specification)
        {
            var scopes = ScopesOf(specification);

            if (scopes.Count == 0) return null;

            return "it applies to " + string.Join(" and ", scopes.Select(s => s.Describe()))
                   + ", which is this model's own grouping and has no place in an IDS file";
        }

        /// <summary>
        /// The specifications a .ids file can carry, and the reason for each one it cannot.
        /// </summary>
        /// <param name="document">The rule set, or null.</param>
        public static IdsPortable Split(IdsDocument? document)
        {
            var portable = new List<IdsSpecification>();
            var staying = new List<IdsStayedBehind>();

            foreach (var specification in document?.Specifications ?? Enumerable.Empty<IdsSpecification>())
            {
                if (specification == null) continue;

                var why = WhyNotPortable(specification);

                if (why == null) portable.Add(specification);
                else staying.Add(new IdsStayedBehind(specification, why));
            }

            return new IdsPortable(portable, staying);
        }

    }

    /// <summary>One rule that could not travel, and why.</summary>
    public sealed class IdsStayedBehind
    {
        /// <summary>Name it.</summary>
        /// <param name="specification">The rule.</param>
        /// <param name="why">Why it could not go.</param>
        public IdsStayedBehind(IdsSpecification specification, string why)
        {
            Specification = specification ?? throw new ArgumentNullException(nameof(specification));
            Why = why ?? string.Empty;
        }

        /// <summary>The rule.</summary>
        public IdsSpecification Specification { get; }

        /// <summary>Why it could not go.</summary>
        public string Why { get; }

        /// <inheritdoc />
        public override string ToString() => "\"" + Specification.Name + "\" - " + Why;
    }

    /// <summary>
    /// A rule set split into what a .ids file can carry and what has to stay here.
    ///
    /// The whole of the honesty about the format: the alternative to naming what stayed is an
    /// invalid file or a silent drop, and both are worse than a rule the client does not get.
    /// </summary>
    public sealed class IdsPortable
    {
        internal IdsPortable(IReadOnlyList<IdsSpecification> portable,
                             IReadOnlyList<IdsStayedBehind> staying)
        {
            Portable = portable;
            Staying = staying;
        }

        /// <summary>What the file can carry.</summary>
        public IReadOnlyList<IdsSpecification> Portable { get; }

        /// <summary>What has to stay, each with its reason.</summary>
        public IReadOnlyList<IdsStayedBehind> Staying { get; }

        /// <summary>Whether every rule can travel.</summary>
        public bool IsWhole => Staying.Count == 0;

        /// <summary>How the split reads in one line, or null when nothing stayed.</summary>
        public string? Explain()
        {
            if (Staying.Count == 0) return null;

            var count = Staying.Count.ToString(CultureInfo.InvariantCulture);

            return count + (Staying.Count == 1 ? " rule stayed here: " : " rules stayed here: ")
                   + string.Join("; ", Staying.Take(3).Select(s => s.ToString()))
                   + (Staying.Count > 3
                       ? "; and " + (Staying.Count - 3).ToString(CultureInfo.InvariantCulture) + " more."
                       : ".")
                   + " They are still in this project and still run here; the file carries the rest.";
        }

        /// <inheritdoc />
        public override string ToString() => Explain() ?? "every rule can travel.";
    }
}
