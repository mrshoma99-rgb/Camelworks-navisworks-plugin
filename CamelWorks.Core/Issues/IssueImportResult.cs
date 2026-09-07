using System.Collections.Generic;

namespace CamelWorks.Core.Issues
{
    /// <summary>What an import did, in the numbers somebody checks it by.</summary>
    public sealed class IssueImportResult
    {
        internal IssueImportResult(int added, int updated, int comments, IReadOnlyList<string> notes)
        {
            Added = added; Updated = updated; Comments = comments; Notes = notes;
        }

        /// <summary>Topics that became new issues.</summary>
        public int Added { get; }

        /// <summary>Topics that matched an issue already here, and refreshed it.</summary>
        public int Updated { get; }

        /// <summary>Comments brought in on top of what was already said.</summary>
        public int Comments { get; }

        /// <summary>Anything worth saying about what came in.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Added == 0 && Updated == 0
                ? "Nothing came in."
                : Added + (Added == 1 ? " issue added" : " issues added") + ", "
                  + Updated + " updated, " + Comments
                  + (Comments == 1 ? " comment" : " comments") + ".";

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
