using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Clash
{
    /// <summary>How the file of tests is written.</summary>
    public sealed class MatrixTestsXmlOptions
    {
        /// <summary>What the batch of tests is called inside the file.</summary>
        public string Batch { get; set; } = "CamelWorks matrix";

        /// <summary>
        /// The units the tolerances are written in, as the header declares them.
        ///
        /// Metres, because that is what the board holds and what the host's own exports use.
        /// A file that declared millimetres and wrote metres would import twenty-five tests at a
        /// thousandth of the tolerance somebody agreed, and nothing would say so.
        /// </summary>
        public string Units { get; set; } = "m";

        /// <summary>Which document this matrix belongs to, for the header.</summary>
        public string? SourceFile { get; set; }

        /// <summary>
        /// What a saved set's name is prefixed with to become a selection locator.
        ///
        /// <b>Held as a setting rather than fixed in the code.</b> This is the one part of the
        /// format that is the host's private business: the path it uses to name a saved set
        /// inside its own tree. It is what every clash-test file in the wild carries, and it is
        /// not published - so a host year that spells it differently would otherwise mean a new
        /// build rather than a changed line.
        /// </summary>
        public string SetPrefix { get; set; } = "lcop_selection_set_tree/";

        /// <summary>Whether each test names the folder the matrix would file it in.</summary>
        public bool KeepFolders { get; set; } = true;
    }

    /// <summary>The file, and what could not be written into it.</summary>
    public sealed class MatrixTestsXmlDocument
    {
        internal MatrixTestsXmlDocument(string text, int written, IReadOnlyList<string> warnings)
        {
            Text = text; Written = written; Warnings = warnings;
        }

        /// <summary>The file.</summary>
        public string Text { get; }

        /// <summary>How many tests it carries.</summary>
        public int Written { get; }

        /// <summary>What was left out, and why.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Whether there is anything in it.</summary>
        public bool IsEmpty => Written == 0;

        /// <inheritdoc />
        public override string ToString() =>
            Written.ToString(CultureInfo.InvariantCulture) + " tests";
    }

    /// <summary>
    /// The matrix as a file Navisworks can make the tests from.
    ///
    /// <b>The build button needs the seat the matrix was drawn on.</b> A matrix is argued over at
    /// a kick-off, on a laptop, by somebody who is not the person who will run it - and the only
    /// way it became tests was pressing a button inside the model it was going to run against.
    /// So the coordinator drew the board, sent a spreadsheet, and the person with the seat built
    /// two hundred tests by hand from a picture of them.
    ///
    /// Clash Detective imports its own tests from XML, and that file is exactly what a matrix
    /// already knows: a name per pair, two sides, a kind and a tolerance. Written out, the board
    /// travels - and the person with the seat presses Import rather than New two hundred times.
    ///
    /// <b>Tests, not results.</b> The other writer in this codebase records what came back; this
    /// one records what should be run and carries no results at all, which is what the host
    /// expects from a file it is asked to create tests from.
    ///
    /// <b>Only the sides the format can name.</b> A side that is a saved set is a locator the
    /// host resolves by name. A side that is whole models, or a selection captured by element,
    /// is not something this file can address - so those are left out and named in the warnings
    /// rather than written as something that would import as an empty test.
    /// </summary>
    public static class MatrixTestsXml
    {
        /// <summary>Write the file.</summary>
        /// <param name="plan">The tests the matrix asks for.</param>
        /// <param name="options">Units, names and how a set is addressed.</param>
        public static MatrixTestsXmlDocument Write(MatrixPlan? plan,
                                                   MatrixTestsXmlOptions? options = null)
        {
            var how = options ?? new MatrixTestsXmlOptions();
            var warnings = new List<string>();
            var written = 0;

            var s = new StringBuilder();

            s.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            s.Append("<exchange units=\"").Append(Xml.Escape(how.Units)).Append('"');

            if (!string.IsNullOrWhiteSpace(how.SourceFile))
                s.Append(" filename=\"").Append(Xml.Escape(how.SourceFile)).Append('"');

            s.Append(">\n");
            s.Append("  <batchtest name=\"").Append(Xml.Escape(how.Batch))
             .Append("\" internal_name=\"clashtests\" units=\"")
             .Append(Xml.Escape(how.Units)).Append("\">\n");
            s.Append("    <clashtests>\n");

            // Named once each. Two cells of a matrix can plan the same test - the same pair at
            // the same tolerance reached from two rows pointed at one set - and a file carrying
            // it twice imports two tests that will for ever report the same clashes.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var test in plan?.Tests ?? Array.Empty<MatrixTestPlan>())
            {
                if (test == null) continue;

                var left = Locator(test.Row.Source, how);
                var right = Locator(test.Column.Source, how);

                if (left == null || right == null)
                {
                    warnings.Add(test.Name + " - "
                                 + (left == null ? test.Row.Titled : test.Column.Titled)
                                 + " is "
                                 + (left == null ? test.Row.Source.Kinded()
                                                 : test.Column.Source.Kinded()).ToLowerInvariant()
                                 + ", which this file cannot name. Only saved sets can be.");
                    continue;
                }

                if (!seen.Add(test.Name))
                {
                    warnings.Add(test.Name + " - two cells plan the same test, so it is written "
                                 + "once.");
                    continue;
                }

                s.Append("      <clashtest name=\"").Append(Xml.Escape(test.Name))
                 .Append("\" test_type=\"").Append(Word(test.Symbol.Test))
                 .Append("\" status=\"new\" tolerance=\"")
                 .Append(test.Symbol.Tolerance.ToString("0.000000", CultureInfo.InvariantCulture))
                 .Append("\" merge_composites=\"1\">\n");

                if (how.KeepFolders && !string.IsNullOrWhiteSpace(test.Row.Folder))
                    s.Append("        <clashtestfolder>").Append(Xml.Escape(test.Row.Folder))
                     .Append("</clashtestfolder>\n");

                // No linkage and no rules, said outright rather than left out: a test the host
                // makes carries both, and an importer that meets neither has to decide what they
                // were - which is a decision nobody wrote down.
                s.Append("        <linkage mode=\"none\" />\n");

                Side(s, "left", left);
                Side(s, "right", right);

                s.Append("        <rules />\n");
                s.Append("      </clashtest>\n");

                written++;
            }

            s.Append("    </clashtests>\n");
            s.Append("  </batchtest>\n");
            s.Append("</exchange>\n");

            foreach (var skipped in plan?.Skipped ?? Array.Empty<MatrixSkip>())
                warnings.Add(skipped.Cell + " - " + skipped.Why);

            return new MatrixTestsXmlDocument(s.ToString(), written, warnings);
        }

        private static void Side(StringBuilder s, string which, string locator)
        {
            s.Append("        <").Append(which).Append(">\n");
            s.Append("          <clashselection selfintersect=\"0\">\n");
            s.Append("            <locator>").Append(Xml.Escape(locator)).Append("</locator>\n");
            s.Append("          </clashselection>\n");
            s.Append("        </").Append(which).Append(">\n");
        }

        /// <summary>
        /// How the host names one side, or null when this file cannot name it.
        ///
        /// A saved set is addressed by its path in the host's own tree, folders and all - which
        /// is why a set inside a folder has to carry that folder here: two sets called "Pipes" in
        /// two different folders are two different sets, and a locator naming only the leaf would
        /// resolve to whichever the host found first.
        /// </summary>
        /// <param name="source">What the row or column points at.</param>
        /// <param name="how">The options, which carry the prefix.</param>
        private static string? Locator(MatrixSource? source, MatrixTestsXmlOptions how)
        {
            if (source == null || !source.IsSet) return null;

            var trail = string.IsNullOrWhiteSpace(source.Folder)
                ? source.Name
                : source.Folder.Trim('/') + "/" + source.Name;

            return (how.SetPrefix ?? string.Empty) + trail;
        }

        /// <summary>What the host calls each kind of test.</summary>
        /// <param name="kind">The kind.</param>
        private static string Word(MatrixTest kind) => kind switch
        {
            MatrixTest.Clearance => "clearance",
            MatrixTest.Duplicate => "duplicates",
            MatrixTest.HardConservative => "hard_conservative",
            _ => "hard",
        };
    }
}
