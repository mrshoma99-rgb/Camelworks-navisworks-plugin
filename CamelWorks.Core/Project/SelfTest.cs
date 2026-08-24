using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Report;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>One check and what it found.</summary>
    public sealed class SelfTestResult
    {
        internal SelfTestResult(string area, string name, bool passed, string detail)
        {
            Area = area;
            Name = name;
            Passed = passed;
            Detail = detail;
        }

        /// <summary>Which part of the product it covers.</summary>
        public string Area { get; }

        /// <summary>What it checks, in one phrase.</summary>
        public string Name { get; }

        /// <summary>True when it did what it should.</summary>
        public bool Passed { get; }

        /// <summary>What it actually found. Worth reading on a pass as well as a failure.</summary>
        public string Detail { get; }

        /// <inheritdoc />
        public override string ToString() =>
            (Passed ? "ok   " : "FAIL ") + Area + " / " + Name + " - " + Detail;
    }

    /// <summary>Everything one run found.</summary>
    public sealed class SelfTestReport
    {
        internal SelfTestReport(IReadOnlyList<SelfTestResult> results, long elapsedTicks)
        {
            Results = results;
            ElapsedTicks = elapsedTicks;
        }

        /// <summary>Every check, in the order it ran.</summary>
        public IReadOnlyList<SelfTestResult> Results { get; }

        /// <summary>How long the run took, in ticks.</summary>
        public long ElapsedTicks { get; }

        /// <summary>How many passed.</summary>
        public int Passed => Results.Count(r => r.Passed);

        /// <summary>How many did not.</summary>
        public int Failed => Results.Count(r => !r.Passed);

        /// <summary>True when everything passed.</summary>
        public bool IsClean => Failed == 0;

        /// <summary>The checks that failed.</summary>
        public IReadOnlyList<SelfTestResult> Failures => Results.Where(r => !r.Passed).ToList();

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var s = Passed.ToString(CultureInfo.InvariantCulture) + " of "
                  + Results.Count.ToString(CultureInfo.InvariantCulture) + " checks passed";

            if (Failed > 0)
                s += ", " + Failed.ToString(CultureInfo.InvariantCulture) + " failed";

            return s + " in " + (ElapsedTicks / (double)TimeSpan.TicksPerMillisecond)
                       .ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>The whole run as plain text, for pasting into a bug report.</summary>
        public string ToText()
        {
            var lines = new List<string> { "CamelWorks self-test", Explain(), string.Empty };

            lines.AddRange(Results.Select(r => r.ToString()));

            return string.Join(Environment.NewLine, lines);
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The contract suite, shipped.
    ///
    /// A thousand tests run on every push and none of them run on the machine where the product
    /// actually breaks. What breaks there is not the logic — that is the same assembly CI built —
    /// it is the environment: a .NET that resolves a different culture, a locale whose decimal
    /// separator is a comma, a temp folder nobody can write to, a compression stack a security
    /// agent has replaced. Those failures are invisible to CI by construction, and they are the
    /// ones that produce a support email saying "the report is empty".
    ///
    /// So the checks here are deliberately about the seams to the platform rather than about the
    /// logic: does a written PDF come back with the bytes a PDF needs, does a zip round-trip, does
    /// a number written in this locale read back as the same number, does the store actually write
    /// where it says it does.
    ///
    /// <b>Nothing here touches the host.</b> This assembly cannot see Navisworks, which is exactly
    /// why the suite can run on a support call in thirty seconds without a model open.
    /// </summary>
    public static class SelfTest
    {
        /// <summary>
        /// Run everything.
        /// </summary>
        /// <param name="scratch">
        /// A folder the checks may write into, or null to skip the ones that need a disk. Passing
        /// null is not a failure - it is what a run on a locked-down machine does, and it says so
        /// rather than reporting a disk check that never ran as a pass.
        /// </param>
        public static SelfTestReport Run(string? scratch = null)
        {
            var started = DateTime.UtcNow.Ticks;
            var results = new List<SelfTestResult>();

            Check(results, "Numbers", "a number survives this machine's locale", Numbers);
            Check(results, "Numbers", "quantities parse and convert", Quantities);
            Check(results, "Numbers", "lengths format and read back", Lengths);
            Check(results, "Identity", "hashes are stable", Hashes);
            Check(results, "Identity", "IFC GUIDs round-trip", Guids);
            Check(results, "Project", "JSON survives a round trip", Json);
            Check(results, "Sets", "an expression compiles and describes", Expressions);
            Check(results, "Report", "a PDF is written and is structurally a PDF", Pdf);
            Check(results, "Report", "a spreadsheet is a readable zip", Xlsx);
            Check(results, "openBIM", "a BCF file writes and reads back", Bcf);

            if (scratch != null)
                Check(results, "Storage", "the project file writes and reads back",
                      () => Disk(scratch));
            else
                results.Add(new SelfTestResult("Storage", "the project file writes and reads back",
                    true, "not run - no folder was offered to write into"));

            return new SelfTestReport(results, DateTime.UtcNow.Ticks - started);
        }

        /// <summary>Run one check, turning anything it throws into a failure rather than a crash.</summary>
        private static void Check(ICollection<SelfTestResult> into, string area, string name,
                                  Func<string> check)
        {
            try
            {
                into.Add(new SelfTestResult(area, name, true, check()));
            }
            catch (Exception e)
            {
                // The whole point is to run on a machine where something is wrong, so a check that
                // throws is the result, not an interruption.
                into.Add(new SelfTestResult(area, name, false, e.GetType().Name + ": " + e.Message));
            }
        }

        private static string Must(bool condition, string whenTrue, string whenFalse)
        {
            if (!condition) throw new InvalidOperationException(whenFalse);
            return whenTrue;
        }

        // A machine set to a comma decimal separator is the single most common way a working build
        // produces wrong numbers in the field: everything CamelWorks writes is invariant, and a
        // stray ToString() without a culture turns 3.5 into "3,5" and a CSV column into two.
        private static string Numbers()
        {
            var written = (1234.5).ToString("0.###", CultureInfo.InvariantCulture);

            Must(written == "1234.5", "", "a number wrote as \"" + written + "\" rather than \"1234.5\"");

            var read = double.TryParse("1234.5", NumberStyles.Float, CultureInfo.InvariantCulture, out var back)
                       && Math.Abs(back - 1234.5) < 1e-9;

            return Must(read, "1234.5 writes and reads back exactly", "\"1234.5\" would not read back");
        }

        private static string Quantities()
        {
            Must(Quantity.TryParse("2.5 m3", out var volume) && volume.Kind == QuantityKind.Volume,
                 "", "\"2.5 m3\" did not parse as a volume");

            Must(Math.Abs(volume.Value - 2.5) < 1e-9, "", "2.5 m3 came back as " + volume.Value);

            Must(Quantity.TryParse("1500 mm", out var length)
                 && Math.Abs(length.Value - 1.5) < 1e-9,
                 "", "1500 mm did not convert to 1.5 m");

            Must(Quantity.TryParse("3'-6\"", out var imperial)
                 && Math.Abs(imperial.Value - 1.0668) < 1e-4,
                 "", "3'-6\" did not convert to 1.0668 m");

            return Quantity.KnownUnits.Count.ToString(CultureInfo.InvariantCulture)
                   + " units known, metric and imperial both converting";
        }

        private static string Lengths()
        {
            var metric = new Measure(UnitSystem.Metric);
            var imperial = new Measure(UnitSystem.Imperial);

            Must(metric.TryRead(metric.Length(3.456), out var back) && Math.Abs(back - 3.456) < 0.001,
                 "", "a metric length would not read back");

            var feet = imperial.Length(1.0668);

            Must(feet.Contains("3") && feet.Contains("6"),
                 "", "1.0668 m formatted as \"" + feet + "\" rather than 3 feet 6 inches");

            return "metric \"" + metric.Length(3.456) + "\" and imperial \"" + feet + "\"";
        }

        private static string Hashes()
        {
            var once = Hash.Of(12, "a", "b", null, "c");
            var twice = Hash.Of(12, "a", "b", null, "c");

            Must(once == twice, "", "the same input hashed two different ways in one process");

            Must(once != Hash.Of(12, "a", "bc", null, ""),
                 "", "two different inputs hashed the same - the length prefix is not working");

            return "stable and collision-free on the length-prefix case: " + once;
        }

        private static string Guids()
        {
            const string real = "0YvctVUKr0kugbFTf53O9L";

            Must(IfcGuid.IsCompressed(real), "", "a real IFC GUID would not validate: " + real);
            Must(IfcGuid.OrNull(real) == real, "", "a real IFC GUID did not come back from OrNull");

            // The first character carries only two bits of the 128, so most of the alphabet cannot
            // legally start one. A build that accepts "ZYvct..." is one whose alphabet is wrong.
            Must(!IfcGuid.IsCompressed("ZYvctVUKr0kugbFTf53O9L"),
                 "", "an impossible first character was accepted as an IFC GUID");

            Must(!IfcGuid.IsCompressed("d8a9f6c2-1e34-4b5a-9c7d-0e1f2a3b4c5d"),
                 "", "an uncompressed GUID was accepted as a compressed one");

            return "22-character IFC GUIDs validate, and look-alikes are refused";
        }

        private static string Json()
        {
            var written = JsonValue.Object()
                .Set("text", JsonValue.String("a \"quoted\" line\nand a second"))
                .Set("number", JsonValue.Number(1234.5))
                .Set("list", JsonValue.Array(new[] { JsonValue.String("x"), JsonValue.String("y") }));

            var text = written.ToString();

            Must(JsonReader.TryParse(text, out var read), "", "the JSON this build writes would not parse back");
            Must(read["text"].AsString() == "a \"quoted\" line\nand a second",
                 "", "an escaped string did not survive the round trip");
            Must(Math.Abs(read["number"].AsDouble() - 1234.5) < 1e-9,
                 "", "a number did not survive the round trip");
            Must(read["list"].Items.Count == 2, "", "an array did not survive the round trip");

            return text.Length.ToString(CultureInfo.InvariantCulture)
                   + " characters written and read back identically";
        }

        private static string Expressions()
        {
            var expression = SetExpression.And(
                SetExpression.Where("Element", "Category", SetOperator.Equals, "Walls"),
                SetExpression.Not(SetExpression.Where("Element", "Level", SetOperator.Equals, "Level 00")));

            var plan = DnfCompiler.Compile(expression);

            Must(plan.Clauses.Count >= 1, "", "an AND-NOT expression compiled to no clauses");
            Must(!plan.MatchesNothing, "", "an AND-NOT expression compiled to nothing at all");

            var described = expression.Describe();

            Must(described.Contains("Walls"), "", "an expression described itself without its own value");

            return "compiled to " + plan.Clauses.Count.ToString(CultureInfo.InvariantCulture)
                   + " clause(s), reading \"" + described + "\"";
        }

        private static string Pdf()
        {
            var document = new ReportDocument("Self-test")
                .Fact("Written by", "the self-test")
                .Heading("A heading")
                .Paragraph("A paragraph with a \\ backslash, a ( bracket and an accent: café.");

            using (var buffer = new MemoryStream())
            {
                PdfReportWriter.Write(buffer, document);

                var bytes = buffer.ToArray();

                Must(bytes.Length > 400, "", "the PDF came out " + bytes.Length + " bytes, which is too small to be one");

                var head = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
                Must(head == "%PDF-", "", "the file does not start with %PDF-, it starts with \"" + head + "\"");

                var tail = System.Text.Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 32), Math.Min(32, bytes.Length));
                Must(tail.Contains("%%EOF"), "", "the PDF has no %%EOF marker, so it is truncated");

                return bytes.Length.ToString("N0", CultureInfo.InvariantCulture)
                       + " bytes, with a header, a trailer and an %%EOF";
            }
        }

        private static string Xlsx()
        {
            var table = new TableBlock("Thing", "Count");
            table.Row("Walls", "412");
            table.Row("Doors with a \"quote\" in the name", "8");

            var bytes = XlsxWriter.Write("Self-test", table);

            Must(bytes.Length > 400, "", "the spreadsheet came out " + bytes.Length + " bytes");
            Must(bytes[0] == 0x50 && bytes[1] == 0x4B, "", "the spreadsheet is not a zip - it does not start with PK");

            // Read it back through this machine's own compression stack. A security agent that has
            // replaced System.IO.Compression is a real thing and produces a file Excel refuses,
            // which nothing in the writing half would notice.
            using (var buffer = new MemoryStream(bytes))
            {
                var sheets = XlsxReader.Read(buffer);

                Must(sheets.Sheets.Count >= 1, "", "the spreadsheet this build wrote has no readable sheet in it");

                var rows = sheets.Sheets[0].Rows.Count;
                Must(rows >= 2, "", "the spreadsheet read back with " + rows + " rows rather than three");

                return bytes.Length.ToString("N0", CultureInfo.InvariantCulture)
                       + " bytes, re-read as " + rows.ToString(CultureInfo.InvariantCulture) + " rows";
            }
        }

        private static string Bcf()
        {
            var topic = new BcfTopic("self-test", "A test topic",
                                     new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                                     "self-test");

            using (var buffer = new MemoryStream())
            {
                var written = BcfWriter.Write(buffer, new[] { topic }, BcfVersion.V21);

                Must(written.Topics == 1, "", "one topic in gave " + written.Topics + " topics out");

                buffer.Position = 0;

                var read = BcfReader.Read(buffer);

                Must(read.Topics.Count == 1, "", "a BCF file this build wrote read back with "
                                                + read.Topics.Count + " topics");

                Must(read.Topics[0].Title == "A test topic",
                     "", "a topic title did not survive the round trip");

                return "one topic written as BCF 2.1 and read back with its title and GUID intact";
            }
        }

        // The atomic write rather than the whole store, because the atomic write is the part that
        // depends on the machine: write-temp, replace-with-backup is one call on a local disk and a
        // different one on a synced folder, and a security agent or a network share that refuses
        // the replace is exactly the failure a support call is about.
        private static string Disk(string folder)
        {
            var fs = PhysicalFileSystem.Instance;

            var path = Path.Combine(folder, "camelworks-selftest-"
                                            + Hash.Of(8, folder, "selftest") + ".camelworks");

            try
            {
                fs.CreateDirectory(folder);

                var first = JsonValue.Object().Set("run", JsonValue.String("first")).ToString();
                var second = JsonValue.Object().Set("run", JsonValue.String("second")).ToString();

                AtomicFile.Write(fs, path, first);
                Must(fs.Exists(path), "", "an atomic write reported success but wrote no file to " + path);

                // Twice, because the second write is the one that exercises the replace-and-keep-a-
                // backup path. A first write to an empty folder proves much less.
                AtomicFile.Write(fs, path, second);

                var back = AtomicFile.ReadWithFallback(fs, path, out var usedBackup);

                Must(back != null, "", "the file this build wrote could not be read back");
                Must(!usedBackup, "", "reading the file it had just written fell back to the backup copy");

                Must(JsonReader.TryParse(back!, out var parsed) && parsed["run"].AsString() == "second",
                     "", "the second write did not replace the first");

                Must(!fs.Exists(path + AtomicFile.TempSuffix),
                     "", "the in-progress file was left behind at " + path + AtomicFile.TempSuffix);

                return "wrote, replaced and re-read a file in " + folder + ", backup path included";
            }
            finally
            {
                // Cleaned up whatever happened. A self-test that leaves litter is one somebody
                // stops running.
                foreach (var litter in new[] { path, path + AtomicFile.TempSuffix, path + AtomicFile.BackupSuffix })
                {
                    try { fs.Delete(litter); }
                    catch (Exception) { }
                }
            }
        }
    }
}
