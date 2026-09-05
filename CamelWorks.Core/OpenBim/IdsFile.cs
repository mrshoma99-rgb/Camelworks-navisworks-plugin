using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// A rule set as a file, in either of the two kinds, and one reader that takes both.
    ///
    /// An IDS is an open standard and the reason to write one is that the client's tool, the
    /// architect's tool and ours all read it. A rule that says who it is about in this model's own
    /// terms has no place in that standard — applicability is a closed sequence of six IFC facets
    /// with no extension point — so a project that writes those rules can either send a file
    /// everyone reads and leave them out, or send everything and be read only here. That is a choice
    /// about who the file is for and belongs to the sender.
    ///
    /// A BIMCamel rule set is a zip holding two things: <c>rules.ids</c>, a real IDS carrying every
    /// portable rule, and <c>camelworks-rules.json</c>, carrying the rest. Somebody without
    /// CamelWorks can rename it, unzip it and open the IDS inside, so the degradation path is a real
    /// file. Nothing is hidden and nothing claims to be a standard it is not.
    ///
    /// A scoped rule names a set, and a set is something a model has or has not. The rule travels;
    /// whether it applies to another model depends on that model having a set of that name, and a
    /// run says so rather than reporting the rule as matching nothing.
    /// </summary>
    public static class IdsFile
    {
        /// <summary>What a rule set written for CamelWorks alone is called.</summary>
        public const string Extension = ".bcids";

        /// <summary>What an IDS file is called.</summary>
        public const string IdsExtension = ".ids";

        /// <summary>Where the portable half sits inside the archive.</summary>
        public const string RulesEntry = "rules.ids";

        /// <summary>Where the rules no IDS can carry sit inside the archive.</summary>
        public const string ExtraEntry = "camelworks-rules.json";

        /// <summary>The filter for a file dialog, taking either kind.</summary>
        public const string Filter =
            "Rule sets|*.ids;*.bcids|IDS|*.ids|BIMCamel rules|*.bcids|All files|*.*";

        /// <summary>How many rules an archive may carry before it is refused.</summary>
        public const int MaxRules = 5000;

        /// <summary>
        /// Write everything, in the kind only CamelWorks reads.
        ///
        /// The portable half is written by the ordinary IDS writer, so what is inside the archive
        /// is the same file the other button produces and is valid against the published schema.
        /// </summary>
        /// <param name="document">The rule set.</param>
        /// <param name="whenTicks">When it is being written. Zero leaves the date out.</param>
        public static IdsPackResult Write(IdsDocument? document, long whenTicks = 0)
        {
            if (document == null || document.Specifications.Count == 0)
                return new IdsPackResult(null, 0, 0, "There is nothing to write.");

            var split = IdsScopeSpecification.Split(document);

            string? portable = null;
            var warnings = new List<string>();

            if (split.Portable.Count > 0)
            {
                var only = new IdsDocument(document.Title)
                {
                    Version = document.Version,
                    Author = document.Author,
                    Description = document.Description,
                };

                foreach (var specification in split.Portable) only.Specifications.Add(specification);

                var written = IdsWriter.Write(only, whenTicks);

                if (written.Problem != null)
                    return new IdsPackResult(null, 0, 0, written.Problem);

                portable = written.Text;
                warnings.AddRange(written.Warnings);
            }

            byte[] bytes;

            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    if (portable != null) Put(zip, RulesEntry, portable);

                    Put(zip, ExtraEntry, JsonValue.Object()
                        .Set("version", IdsJson.Version)
                        .Set("title", document.Title)
                        .Set("rules", IdsJson.WriteAll(split.Staying.Select(s => s.Specification)))
                        .ToJson());
                }

                bytes = buffer.ToArray();
            }

            return new IdsPackResult(bytes, split.Portable.Count, split.Staying.Count, null)
            {
                Warnings = warnings,
            };
        }

        /// <summary>
        /// Read either kind, told apart by what the bytes are rather than by what they are called.
        ///
        /// By content and not by extension, because a file renamed on the way through an email is
        /// still the file it was, and a reader that trusts a name refuses a good rule set.
        /// </summary>
        /// <param name="bytes">The file.</param>
        public static IdsReadResult Read(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return Nothing("There is nothing in that file.");

            if (!IsArchive(bytes))
            {
                using var stream = new MemoryStream(bytes, writable: false);

                return IdsReader.Read(stream);
            }

            try
            {
                using var buffer = new MemoryStream(bytes, writable: false);
                using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

                return Unpack(zip);
            }
            catch (InvalidDataException)
            {
                return Nothing("That file begins like a zip and is not one, so nothing could be "
                               + "read out of it.");
            }
        }

        /// <summary>Whether these bytes are a zip - which is to say, a BIMCamel rule set.</summary>
        /// <param name="bytes">The start of a file.</param>
        public static bool IsArchive(byte[]? bytes) =>
            bytes != null && bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B;

        /// <summary>Whether a name reads as either kind of rule set.</summary>
        /// <param name="path">The file name or path.</param>
        public static bool Names(string? path)
        {
            var extension = Path.GetExtension(path ?? string.Empty);

            return extension.Equals(IdsExtension, StringComparison.OrdinalIgnoreCase)
                || extension.Equals(Extension, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------------------------

        private static IdsReadResult Unpack(ZipArchive zip)
        {
            var warnings = new List<string>();
            IdsDocument? document = null;

            var rules = zip.GetEntry(RulesEntry);

            if (rules != null)
            {
                using var stream = rules.Open();
                using var copy = new MemoryStream();

                stream.CopyTo(copy);
                copy.Position = 0;

                var read = IdsReader.Read(copy);

                warnings.AddRange(read.Warnings);
                document = read.Document;
            }

            var extra = zip.GetEntry(ExtraEntry);

            if (extra == null && document == null)
                return Nothing("That archive holds neither an IDS nor any CamelWorks rules, so "
                               + "there is nothing in it to check against.");

            if (extra != null)
            {
                string text;

                using (var stream = extra.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    text = reader.ReadToEnd();

                var json = JsonReader.Parse(text);
                var scoped = IdsJson.ReadAll(json?["rules"]);

                document ??= new IdsDocument(json?["title"].AsString() ?? "Rules");

                foreach (var specification in scoped)
                {
                    if (document.Specifications.Count >= MaxRules)
                    {
                        warnings.Add("That archive names more than "
                                     + MaxRules + " rules; the rest were not read.");
                        break;
                    }

                    document.Specifications.Add(specification);
                }

                var claimed = json?["rules"].Items.Count() ?? 0;

                if (claimed > scoped.Count)
                    warnings.Add((claimed - scoped.Count)
                        + " of the CamelWorks rules in that archive could not be read and were "
                        + "left out. The rest are here.");
            }

            return document == null || document.Specifications.Count == 0
                ? Nothing("That archive carried no rules this version can read.")
                : new IdsReadResult(document, warnings);
        }

        private static void Put(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            writer.Write(text);
        }

        private static IdsReadResult Nothing(string why) =>
            new IdsReadResult(null, new[] { why });
    }

    /// <summary>What writing a BIMCamel rule set produced.</summary>
    public sealed class IdsPackResult
    {
        internal IdsPackResult(byte[]? bytes, int portable, int scoped, string? problem)
        {
            Bytes = bytes;
            Portable = portable;
            Scoped = scoped;
            Problem = problem;
        }

        /// <summary>The archive, or null when there is none.</summary>
        public byte[]? Bytes { get; }

        /// <summary>How many rules an IDS file could have carried on its own.</summary>
        public int Portable { get; }

        /// <summary>How many rules only this format can carry.</summary>
        public int Scoped { get; }

        /// <summary>Why nothing was written, or null.</summary>
        public string? Problem { get; }

        /// <summary>Anything the writing had to decide, in a sentence each.</summary>
        public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();

        /// <summary>True when there is a file.</summary>
        public bool IsUsable => Problem == null && Bytes != null && Bytes.Length > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Problem != null) return Problem;

            var all = Portable + Scoped;

            return all + (all == 1 ? " rule" : " rules")
                   + (Scoped > 0
                       ? ", " + Scoped + " of them about a set in this model."
                       : ", every one of them an ordinary IDS rule.");
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
