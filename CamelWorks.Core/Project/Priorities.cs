namespace CamelWorks.Core.Project
{
    /// <summary>
    /// The priority words a project starts with.
    ///
    /// The default only. <see cref="ProjectProfile.Priorities"/> is the one priority vocabulary a
    /// project uses, and it is seeded from here: a finding and an issue raised over three findings
    /// must not offer different words, so there is one configurable list rather than a hard-coded
    /// list per record type.
    ///
    /// A project that overrides the list keeps its override. A record already carrying a word that
    /// is no longer in the list is drawn as it was written and offered until somebody changes it,
    /// because rewriting somebody's judgement to fit a later setting would be the setting editing
    /// the record.
    /// </summary>
    public static class Priorities
    {
        /// <summary>The four words, most urgent first.</summary>
        public static readonly string[] All = { "Critical", "High", "Normal", "Low" };

        /// <summary>The four words as the profile stores a list, comma separated.</summary>
        public static string Default => string.Join(", ", All);
    }
}
