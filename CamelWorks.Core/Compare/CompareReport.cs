using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// A comparison as something to send somebody.
    ///
    /// The scoreboard is the smallest part of it. Four numbers can be produced by any tool; what
    /// makes a change report worth issuing is that it states which model was held to be which, on
    /// what evidence, at what tolerance, watching which properties, and what it is consequently
    /// unable to report. All of that is on the front page, so the discussion is about the building
    /// rather than about the tool.
    /// </summary>
    public static class CompareReport
    {
        /// <summary>How many change rows one printed report carries before it stops being one.</summary>
        public const int PrintCap = 400;

        /// <summary>Build the report document.</summary>
        /// <param name="result">What the comparison found.</param>
        /// <param name="projectName">The project, for the cover.</param>
        public static ReportDocument Build(CompareResult? result, string? projectName = null)
        {
            var document = new ReportDocument("Model compare");

            if (result == null || result.IsEmpty)
            {
                document.Subtitle = "Nothing was compared";
                document.Paragraph("No baseline was available, or nothing in it could be paired with "
                                   + "anything in the current document.");
                return document;
            }

            document.Subtitle = result.Between();

            if (!string.IsNullOrWhiteSpace(projectName)) document.Fact("Project", projectName!.Trim());

            document.Fact("Baseline", Side(result.BaselineLabel, result.BaselineTakenUtc));
            document.Fact("Current", Side(result.CurrentLabel, result.CurrentTakenUtc));
            document.Fact("Models", result.Pairing.Explain());
            document.Fact("Elements compared", ComparePolicy.Count(result.Total));
            document.Fact("Settings", result.Options.Describe());

            Scoreboard(document, result);
            ByModel(document, result);
            ByKind(document, result);
            Evidence(document, result);
            Notes(document, result);
            Changes(document, result);

            return document;
        }

        private static void Scoreboard(ReportDocument document, CompareResult result)
        {
            var legend = new LegendBlock("What changed");

            foreach (var row in CompareLayers.Legend(result))
                legend.Entry(row.Label, (row.Colour?.ToString()) ?? "#B0B0B0", row.Count);

            document.Add(legend);

            document.Paragraph(result.IsQuiet
                ? "Nothing differs between the two. Every element that could be paired was paired, "
                  + "and matched exactly."
                : result.Explain() + ".");
        }

        private static void ByModel(ReportDocument document, CompareResult result)
        {
            if (result.Models.Count == 0) return;

            document.Heading("By model", 2);

            var table = new TableBlock("Model", "Added", "Removed", "Moved", "Changed", "Unchanged",
                                       "Paired on");

            foreach (var model in result.Models)
            {
                table.Row(model.Title,
                          ComparePolicy.Count(model.Added),
                          ComparePolicy.Count(model.Removed),
                          ComparePolicy.Count(model.Moved),
                          ComparePolicy.Count(model.Changed),
                          ComparePolicy.Count(model.Unchanged),
                          ModelPairing.Show(model.Strength) + " — " + model.Reason);
            }

            document.Add(table);
        }

        private static void ByKind(ReportDocument document, CompareResult result)
        {
            if (result.ByKind.Count == 0) return;

            document.Heading("By kind of change", 2);

            document.Paragraph("An element can appear on more than one line: one that was moved and "
                               + "also had its system name corrected is counted under both.");

            var table = new TableBlock("Change", "Elements");

            foreach (var flag in ElementChange.Flags)
            {
                if (!result.ByKind.TryGetValue(flag, out var count) || count == 0) continue;

                table.Row(Sentence(ElementChange.Names(flag)[0]), ComparePolicy.Count(count));
            }

            document.Add(table);
        }

        private static void Evidence(ReportDocument document, CompareResult result)
        {
            if (result.MatchesByRung.Count == 0) return;

            document.Heading("What the matching rested on", 2);

            var table = new TableBlock("Matched on", "Elements", "How much it can be relied on");

            foreach (var rung in new[] { KeyRung.InstanceGuid, KeyRung.TreePath, KeyRung.Geometry })
            {
                if (!result.MatchesByRung.TryGetValue(rung, out var count) || count == 0) continue;

                table.Row(Rung(rung), ComparePolicy.Count(count), Trust(rung));
            }

            document.Add(table);
        }

        private static void Notes(ReportDocument document, CompareResult result)
        {
            if (result.Notes.Count == 0) return;

            var disclosure = new DisclosureBlock("What this comparison cannot tell you");

            foreach (var note in result.Notes) disclosure.Line(note);

            document.Add(disclosure);
        }

        private static void Changes(ReportDocument document, CompareResult result)
        {
            var printed = 0;

            foreach (var bucket in new[]
                     {
                         ChangeBucket.Added, ChangeBucket.Removed,
                         ChangeBucket.Changed, ChangeBucket.Moved,
                     })
            {
                var rows = result.In(bucket);
                if (rows.Count == 0) continue;

                document.Heading(ElementChange.Show(bucket) + " — "
                                 + ComparePolicy.Count(result.CountIn(bucket)), 2);

                var table = new TableBlock("Element", "Type", "Model", "What changed", "Where", "Matched on");

                foreach (var change in rows)
                {
                    if (printed >= PrintCap)
                    {
                        document.Add(table);
                        document.Paragraph("The list stops at " + ComparePolicy.Count(PrintCap)
                                           + " rows. Export the spreadsheet for every one.");
                        return;
                    }

                    var print = change.Print;

                    table.Row(print.Name.Length > 0 ? print.Name : "(unnamed)",
                              print.Type,
                              change.Model,
                              change.Summary(),
                              print.ShowCentre(),
                              change.MatchedOn.HasValue ? Rung(change.MatchedOn.Value) : "not matched");

                    printed++;
                }

                document.Add(table);
            }
        }

        /// <summary>
        /// The workbook: the scoreboard, every change row, the per-model tally and the notes.
        ///
        /// Uncapped on purpose, unlike the printed report. This is the one somebody filters,
        /// sorts and sends on, and a spreadsheet that quietly stops at four hundred rows is worse
        /// than no spreadsheet — it looks complete.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        public static IReadOnlyList<XlsxSheet> Workbook(CompareResult? result)
        {
            var summary = new XlsxSheet("Summary");
            var changes = new XlsxSheet("Changes");
            var models = new XlsxSheet("By model");
            var notes = new XlsxSheet("Notes");

            if (result == null)
            {
                summary.Row("Nothing was compared");
                return new[] { summary };
            }

            summary.Row("Model compare");
            summary.Row("Between", result.Between());
            summary.Row("Baseline", Side(result.BaselineLabel, result.BaselineTakenUtc));
            summary.Row("Current", Side(result.CurrentLabel, result.CurrentTakenUtc));
            summary.Row("Settings", result.Options.Describe());
            summary.Row("Models", result.Pairing.Explain());
            summary.Row(string.Empty);
            summary.Row("Change", "Elements");

            foreach (var row in CompareLayers.Legend(result))
                summary.Row(row.Label, row.Count.ToString(CultureInfo.InvariantCulture));

            if (result.Skipped > 0)
                summary.Row("Not compared", result.Skipped.ToString(CultureInfo.InvariantCulture));

            changes.Row("Change", "Element", "Type", "Category", "Model", "What changed",
                        "X", "Y", "Z", "Size before", "Size after", "Moved by", "Matched on",
                        "Property", "Was", "Now");

            foreach (var change in result.Changes)
            {
                var print = change.Print;

                // One row per element, then one row per property underneath it. A wide row with
                // every property concatenated into one cell is unfilterable, and filtering is the
                // only reason anybody opens this in a spreadsheet.
                changes.Row(ElementChange.Show(change.Bucket),
                            print.Name, print.Type, print.Category, change.Model, change.Summary(),
                            Number(print.CentreX), Number(print.CentreY), Number(print.CentreZ),
                            change.Before?.ShowSize(), change.After?.ShowSize(),
                            change.Distance > 0 ? Number(change.Distance) : null,
                            change.MatchedOn.HasValue ? Rung(change.MatchedOn.Value) : "not matched",
                            null, null, null);

                foreach (var value in change.Properties)
                {
                    changes.Row(ElementChange.Show(change.Bucket),
                                print.Name, print.Type, print.Category, change.Model, "property",
                                null, null, null, null, null, null, null,
                                value.Property.ToString(), value.Before, value.After);
                }
            }

            models.Row("Model", "Added", "Removed", "Moved", "Changed", "Unchanged", "Paired on", "Why");

            foreach (var model in result.Models)
            {
                models.Row(model.Title,
                           model.Added.ToString(CultureInfo.InvariantCulture),
                           model.Removed.ToString(CultureInfo.InvariantCulture),
                           model.Moved.ToString(CultureInfo.InvariantCulture),
                           model.Changed.ToString(CultureInfo.InvariantCulture),
                           model.Unchanged.ToString(CultureInfo.InvariantCulture),
                           ModelPairing.Show(model.Strength),
                           model.Reason);
            }

            notes.Row("What this comparison cannot tell you");
            foreach (var note in result.Notes) notes.Row(note);

            if (result.Notes.Count == 0) notes.Row("Nothing — the baseline could answer every question asked of it.");

            return new[] { summary, changes, models, notes };
        }

        /// <summary>
        /// A change list as CSV, for whoever wants it in something other than a spreadsheet.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        public static string Csv(CompareResult? result)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append("Change,Element,Type,Category,Model,What changed,X,Y,Z,Moved by,Matched on\n");

            if (result == null) return sb.ToString();

            foreach (var change in result.Changes)
            {
                var print = change.Print;

                sb.Append(Cell(ElementChange.Show(change.Bucket))).Append(',')
                  .Append(Cell(print.Name)).Append(',')
                  .Append(Cell(print.Type)).Append(',')
                  .Append(Cell(print.Category)).Append(',')
                  .Append(Cell(change.Model)).Append(',')
                  .Append(Cell(change.Summary())).Append(',')
                  .Append(Cell(Number(print.CentreX))).Append(',')
                  .Append(Cell(Number(print.CentreY))).Append(',')
                  .Append(Cell(Number(print.CentreZ))).Append(',')
                  .Append(Cell(change.Distance > 0 ? Number(change.Distance) : string.Empty)).Append(',')
                  .Append(Cell(change.MatchedOn.HasValue ? Rung(change.MatchedOn.Value) : "not matched"))
                  .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>How a rung reads in a report.</summary>
        /// <param name="rung">The rung.</param>
        public static string Rung(KeyRung rung) => rung switch
        {
            KeyRung.InstanceGuid => "instance GUID",
            KeyRung.TreePath => "tree path",
            _ => "category, type and size",
        };

        private static string Trust(KeyRung rung) => rung switch
        {
            KeyRung.InstanceGuid => "The element said who it was. Nothing stronger exists.",
            KeyRung.TreePath => "Reliable while the model keeps its structure; a re-organised tree breaks it.",
            _ => "A proposal. Two identical parts cannot be told apart this way.",
        };

        private static string Side(string label, DateTimeOffset? at)
        {
            var when = at.HasValue
                ? at.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
                : null;

            if (label.Length == 0) return when ?? "unlabelled";
            return when == null ? label : label + " (" + when + ")";
        }

        private static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Cell(string? value)
        {
            var v = value ?? string.Empty;

            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? v
                : "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
