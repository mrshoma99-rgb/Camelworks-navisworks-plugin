namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The symbol keys a check may declare through <see cref="ICheck.Symbol"/>.
    ///
    /// Keys and not shapes. Core is netstandard2.0 and draws nothing, so a check names what its
    /// symbol depicts and the surface resolves that name to whatever it draws with — the same
    /// split <see cref="ColumnKind"/> uses to declare a column's type without naming a control.
    ///
    /// The list is open. A check may return a key that is not here; a surface that does not know
    /// the key draws its fallback symbol, so an unrecognised key costs the symbol and never the
    /// row.
    /// </summary>
    public static class CheckSymbols
    {
        /// <summary>Two solids meeting. Clash detection.</summary>
        public const string Clash = "clash";

        /// <summary>A measured vertical gap under a surface. Clearance and headroom.</summary>
        public const string Clearance = "clearance";

        /// <summary>A gap in a horizontal surface. Fall hazard and floor openings.</summary>
        public const string Opening = "opening";

        /// <summary>A list checked off. The fallback, and what an unworded check gets.</summary>
        public const string Check = "check";
    }
}
