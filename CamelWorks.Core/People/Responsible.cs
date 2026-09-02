using System;
using System.Globalization;

namespace CamelWorks.Core.People
{
    /// <summary>What kind of thing a piece of work is assigned to.</summary>
    public enum ResponsibleKind
    {
        /// <summary>One named person.</summary>
        Person = 0,

        /// <summary>Everybody who holds a role, whichever company they are at.</summary>
        Role = 1,

        /// <summary>Everybody at a company.</summary>
        Company = 2,

        /// <summary>Everybody on a team.</summary>
        Team = 3,
    }

    /// <summary>
    /// Who a clash is assigned to: a person, or a whole role, company or team.
    ///
    /// <b>Neither "party" nor "assignee".</b> "Party" already means an organisation everywhere
    /// else in this product - the ledger, the key plan and the BCF merge all use it that way -
    /// and this is the wider thing a company is only one kind of. "Assignee" is a name the host
    /// and WPF already use, and a Core type that shadows one of theirs has to be written out in
    /// full at every use in the UI.
    ///
    /// <b>Why not just a name.</b> A coordinator assigns two hundred clashes on a Friday and
    /// does not know, and should not have to know, which architect will pick each one up. What
    /// they know is that it is architecture's - the ARC team's, or the fire engineer's, or
    /// Acme's. Writing that as a person's name means guessing, and writing it as a bare string
    /// means the board cannot tell "Sam Reed" the person from "Structures" the team, so it can
    /// neither draw them differently nor narrow to one of them.
    ///
    /// <b>Held as a string, because that is what the host has.</b> Navisworks' assignee field is
    /// one string per result, and this has to survive a round trip through it and through every
    /// export - so the kind rides in front of the name as a prefix. A name with no prefix is a
    /// person, which is what every assignment written before this existed was, so old boards read
    /// back unchanged.
    /// </summary>
    public readonly struct Responsible : IEquatable<Responsible>
    {
        /// <summary>Name a party.</summary>
        /// <param name="kind">What kind of thing it is.</param>
        /// <param name="name">What it is called.</param>
        public Responsible(ResponsibleKind kind, string name)
        {
            Kind = kind;
            _name = (name ?? string.Empty).Trim();
        }

        /// <summary>What kind of thing it is.</summary>
        public ResponsibleKind Kind { get; }

        /// <summary>
        /// What it is called.
        ///
        /// Read through the backing field so <c>default(Responsible)</c> - which is how "nobody" is
        /// spelled everywhere - answers with an empty string rather than null. A struct that
        /// promises a string its own default would not give is a null reference waiting for the
        /// first row nobody has assigned.
        /// </summary>
        public string Name => _name ?? string.Empty;

        private readonly string? _name;

        /// <summary>Whether this names nobody at all.</summary>
        public bool IsEmpty => Name.Length == 0;

        /// <summary>One person, by name.</summary>
        /// <param name="name">Their name.</param>
        public static Responsible Person(string name) => new Responsible(ResponsibleKind.Person, name);

        /// <summary>
        /// What goes into the host's assignee field, and into every export.
        ///
        /// A person is written bare. That keeps every assignment made before parties existed
        /// readable, keeps the native clash window showing a name rather than a token, and means
        /// the common case costs nothing.
        /// </summary>
        public string Written =>
            IsEmpty ? string.Empty
            : Kind == ResponsibleKind.Person ? Name
            : Prefix(Kind) + Name;

        /// <summary>
        /// Read one back out of a field that has been through the host and back.
        ///
        /// Anything that is not one of the three prefixes is a person, including a name that
        /// happens to contain a colon - the prefixes are matched whole, at the front, and
        /// nothing else is treated as a marker.
        /// </summary>
        /// <param name="written">The field as it was stored.</param>
        public static Responsible Read(string? written)
        {
            var said = (written ?? string.Empty).Trim();

            if (said.Length == 0) return default;

            foreach (ResponsibleKind kind in new[] { ResponsibleKind.Role, ResponsibleKind.Company, ResponsibleKind.Team })
            {
                var prefix = Prefix(kind);

                if (said.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new Responsible(kind, said.Substring(prefix.Length));
            }

            return Person(said);
        }

        /// <summary>What the front of a written party looks like for a kind.</summary>
        /// <param name="kind">The kind.</param>
        public static string Prefix(ResponsibleKind kind) =>
            kind == ResponsibleKind.Role ? "role:"
            : kind == ResponsibleKind.Company ? "company:"
            : kind == ResponsibleKind.Team ? "team:"
            : string.Empty;

        /// <summary>What this kind is called on screen.</summary>
        public string Kinded =>
            Kind == ResponsibleKind.Role ? "role"
            : Kind == ResponsibleKind.Company ? "company"
            : Kind == ResponsibleKind.Team ? "team"
            : "person";

        /// <inheritdoc />
        public bool Equals(Responsible other) =>
            Kind == other.Kind
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Responsible other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            unchecked(((int)Kind * 397)
                      ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name ?? string.Empty));

        /// <inheritdoc />
        public override string ToString() =>
            IsEmpty ? "nobody" : Name + " (" + Kinded + ")";

        /// <summary>Whether two parties name the same thing.</summary>
        /// <param name="left">One.</param>
        /// <param name="right">The other.</param>
        public static bool operator ==(Responsible left, Responsible right) => left.Equals(right);

        /// <summary>Whether two parties name different things.</summary>
        /// <param name="left">One.</param>
        /// <param name="right">The other.</param>
        public static bool operator !=(Responsible left, Responsible right) => !left.Equals(right);

        /// <summary>A stable spelling for a file, which is the written form.</summary>
        public string ToJsonString() => Written;

        /// <summary>How many kinds there are, for anything that wants to walk them.</summary>
        public static int Kinds => 4;

        /// <summary>One kind by its number, for anything that walks them.</summary>
        /// <param name="at">Which one, from zero.</param>
        public static ResponsibleKind KindAt(int at) =>
            at <= 0 ? ResponsibleKind.Person
            : at == 1 ? ResponsibleKind.Role
            : at == 2 ? ResponsibleKind.Company
            : ResponsibleKind.Team;

        /// <summary>A number for a kind, so it can be written to a file.</summary>
        /// <param name="kind">The kind.</param>
        public static string Numbered(ResponsibleKind kind) =>
            ((int)kind).ToString(CultureInfo.InvariantCulture);
    }
}
