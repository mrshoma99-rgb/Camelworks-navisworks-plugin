using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Report
{
    /// <summary>What was issued last time, so this time can say what it supersedes.</summary>
    public sealed class ReportIssue
    {
        /// <summary>Create a record of an issue.</summary>
        /// <param name="revision">Its revision.</param>
        /// <param name="issuedUtc">When it went out.</param>
        /// <param name="file">What it was called.</param>
        public ReportIssue(string revision, DateTimeOffset issuedUtc, string? file = null)
        {
            Revision = string.IsNullOrWhiteSpace(revision) ? Deliverable.FirstRevision : revision.Trim();
            IssuedUtc = issuedUtc;
            File = file;
        }

        /// <summary>Its revision.</summary>
        public string Revision { get; }

        /// <summary>When it went out.</summary>
        public DateTimeOffset IssuedUtc { get; }

        /// <summary>What it was called.</summary>
        public string? File { get; }

        /// <summary>The line a cover page carries when there is something to supersede.</summary>
        public string Supersedes =>
            "Supersedes revision " + Revision + ", issued "
            + IssuedUtc.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            + (string.IsNullOrWhiteSpace(File) ? "." : " as " + File + ".");

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("rev", JsonValue.String(Revision))
                .Set("issued", JsonValue.Number(IssuedUtc.ToUnixTimeSeconds()));

            if (!string.IsNullOrWhiteSpace(File)) json.Set("file", JsonValue.String(File));

            return json;
        }

        /// <summary>Read one back, or null.</summary>
        /// <param name="json">What was written.</param>
        public static ReportIssue? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var revision = json["rev"].AsString();
            if (revision == null) return null;

            return new ReportIssue(revision,
                DateTimeOffset.FromUnixTimeSeconds(json["issued"].AsLong()),
                json["file"].AsString());
        }

        /// <inheritdoc />
        public override string ToString() => Revision + " - " + IssuedUtc.UtcDateTime.ToString("yyyy-MM-dd",
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What a report is called, and which one it replaces.
    ///
    /// The most common cause of a trade building from the wrong information is a PDF in an inbox
    /// that looks exactly like last week's. Everything else in this product exists to get the right
    /// facts into that file; a filename identical to the last four makes all of it worthless at the
    /// last step, and the fix is one token and one line.
    ///
    /// Revisions run A, B, C … Z, AA, AB — the sequence every drawing register in the industry
    /// already uses — or 01, 02, 03 when the previous one was numeric, because a project that
    /// numbers its revisions will not thank a tool for switching to letters halfway through.
    /// </summary>
    public static class Deliverable
    {
        /// <summary>What the first issue is called.</summary>
        public const string FirstRevision = "A";

        /// <summary>The party a report is for.</summary>
        public const string PartyToken = "{Party}";

        /// <summary>Its revision.</summary>
        public const string RevisionToken = "{Rev}";

        /// <summary>The date it was issued, as yyyy-MM-dd.</summary>
        public const string DateToken = "{Date}";

        /// <summary>The project's name.</summary>
        public const string ProjectToken = "{Project}";

        /// <summary>What a name looks like when nobody has said otherwise.</summary>
        public const string DefaultTemplate = "{Project} - clash report - {Party} - Rev {Rev} - {Date}";

        /// <summary>The tokens, for a settings screen to list.</summary>
        public static IReadOnlyList<string> Tokens { get; } =
            new[] { ProjectToken, PartyToken, RevisionToken, DateToken };

        /// <summary>
        /// Fill a name template in, and make what comes out safe to write to disk.
        /// </summary>
        /// <param name="template">The template. Null or blank falls back to <see cref="DefaultTemplate"/>.</param>
        /// <param name="project">The project's name.</param>
        /// <param name="party">Who the report is for.</param>
        /// <param name="revision">Its revision.</param>
        /// <param name="issuedUtc">When it is going out.</param>
        public static string Name(string? template, string? project, string? party, string revision,
                                  DateTimeOffset issuedUtc)
        {
            var text = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template!;

            text = text.Replace(ProjectToken, project ?? string.Empty)
                       .Replace(PartyToken, party ?? string.Empty)
                       .Replace(RevisionToken, revision ?? FirstRevision)
                       .Replace(DateToken, issuedUtc.UtcDateTime.ToString("yyyy-MM-dd",
                                                                         CultureInfo.InvariantCulture));

            return Safe(text);
        }

        /// <summary>
        /// The revision after this one.
        ///
        /// Letters by default; digits when the previous one was digits, keeping its width so 09
        /// becomes 10 rather than 010. Z rolls to AA, which is what a drawing register does.
        /// </summary>
        /// <param name="previous">The previous revision, or null for a first issue.</param>
        public static string NextRevision(string? previous)
        {
            if (string.IsNullOrWhiteSpace(previous)) return FirstRevision;

            var text = previous!.Trim();

            // Trailing digits win, so P1 becomes P2 and 09 becomes 10 — the two conventions a
            // drawing register actually uses, and neither of them wants a letter appended.
            var digits = text.Length;
            while (digits > 0 && char.IsDigit(text[digits - 1])) digits--;

            if (digits < text.Length)
            {
                var prefix = text.Substring(0, digits);
                var number = text.Substring(digits);

                // A number longer than a long is not a revision, and rolling it over would be a
                // worse answer than leaving it where it is.
                if (number.Length > 18) return text;

                var next = (long.Parse(number, CultureInfo.InvariantCulture) + 1)
                    .ToString(CultureInfo.InvariantCulture);

                return prefix + (next.Length < number.Length ? next.PadLeft(number.Length, '0') : next);
            }

            if (!text.All(c => c >= 'A' && c <= 'Z') && !text.All(c => c >= 'a' && c <= 'z'))
                return text + "1";

            var upper = text.ToUpperInvariant();
            var letters = upper.ToCharArray();
            var at = letters.Length - 1;

            while (at >= 0)
            {
                if (letters[at] != 'Z') { letters[at]++; break; }

                letters[at] = 'A';
                at--;
            }

            var rolled = new string(letters);
            if (at < 0) rolled = "A" + rolled;

            return text[0] >= 'a' ? rolled.ToLowerInvariant() : rolled;
        }

        /// <summary>Every issue this project has made, keyed by who it went to.</summary>
        /// <param name="json">What was written.</param>
        public static IReadOnlyDictionary<string, ReportIssue> Read(JsonValue? json)
        {
            var issues = new Dictionary<string, ReportIssue>(StringComparer.OrdinalIgnoreCase);

            if (json == null || json.Kind != JsonKind.Object) return issues;

            foreach (var party in json.Keys)
            {
                var issue = ReportIssue.FromJson(json[party]);
                if (issue != null) issues[party] = issue;
            }

            return issues;
        }

        /// <summary>Write the register back.</summary>
        /// <param name="issues">Every issue, keyed by who it went to.</param>
        public static JsonValue Write(IReadOnlyDictionary<string, ReportIssue> issues)
        {
            var json = JsonValue.Object();

            foreach (var entry in issues.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                json.Set(entry.Key, entry.Value.ToJson());

            return json;
        }

        /// <summary>
        /// A name that can be written to disk on Windows.
        ///
        /// The characters a path may not contain become hyphens, runs of whitespace collapse, and
        /// a trailing dot or space — which Windows silently strips, leaving a file nobody can find
        /// by the name they were told — is removed here where it can be seen.
        /// </summary>
        /// <param name="text">The filled-in name.</param>
        public static string Safe(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "report";

            var clean = new char[text!.Length];
            var at = 0;
            var space = false;

            foreach (var c in text)
            {
                var ok = c == '/' || c == '\\' || c == ':' || c == '*' || c == '?'
                         || c == '"' || c == '<' || c == '>' || c == '|' ? '-' : c;

                if (char.IsControl(ok)) ok = ' ';

                if (ok == ' ')
                {
                    if (space || at == 0) continue;
                    space = true;
                }
                else
                {
                    space = false;
                }

                clean[at++] = ok;
            }

            var name = new string(clean, 0, at).TrimEnd(' ', '.');

            return name.Length == 0 ? "report" : name;
        }
    }
}
