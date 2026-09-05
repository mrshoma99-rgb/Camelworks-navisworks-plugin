using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Field
{
    /// <summary>Where the thing printed on a label comes from.</summary>
    public enum LabelSource
    {
        /// <summary>The element's mark, and its name where it carries no mark. Needs nothing set up.</summary>
        Mark = 0,

        /// <summary>A property named on the recipe.</summary>
        Property,

        /// <summary>The identity CamelWorks knows the element by. Long, opaque, and always there.</summary>
        Key,
    }

    /// <summary>One label.</summary>
    public sealed class SiteLabel
    {
        internal SiteLabel(ElementKey key, string identity, string encoded, string heading,
                           IReadOnlyList<string> lines, QrSymbol? symbol)
        {
            Key = key;
            Identity = identity;
            Encoded = encoded;
            Heading = heading;
            Lines = lines;
            Symbol = symbol;
        }

        /// <summary>Which element it is for.</summary>
        public ElementKey Key { get; }

        /// <summary>What identifies the element, before any prefix.</summary>
        public string Identity { get; }

        /// <summary>What the symbol actually says.</summary>
        public string Encoded { get; }

        /// <summary>The big line on the label.</summary>
        public string Heading { get; }

        /// <summary>The small lines under it.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>The symbol, or null where the text would not fit one.</summary>
        public QrSymbol? Symbol { get; }

        /// <inheritdoc />
        public override string ToString() => Heading + " — " + Encoded;
    }

    /// <summary>What goes on the labels.</summary>
    public sealed class LabelRecipe
    {
        /// <summary>How many labels one run will make.</summary>
        public const int DefaultCeiling = 2000;

        /// <summary>Start one. It works as it stands.</summary>
        /// <param name="name">What to call it.</param>
        public LabelRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Labels" : name!.Trim();
        }

        /// <summary>What to call it.</summary>
        public string Name { get; set; }

        /// <summary>Where the identity comes from.</summary>
        public LabelSource Source { get; set; } = LabelSource.Mark;

        /// <summary>Which property, when the identity comes from one.</summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Which category that property is in. Empty matches any.</summary>
        public string PropertyCategory { get; set; } = string.Empty;

        /// <summary>
        /// What goes in front of the identity in the symbol.
        ///
        /// Empty by default, so the code says exactly what is printed under it and a scan on any
        /// phone shows the mark. Set it to an address and every code becomes a link into whatever
        /// the project already uses to hold documents.
        /// </summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>How much of a code can be lost before it stops reading.</summary>
        public QrLevel Level { get; set; } = QrLevel.High;

        /// <summary>How wide one label is, in millimetres.</summary>
        public double LabelWidth { get; set; } = 62;

        /// <summary>How tall one label is, in millimetres.</summary>
        public double LabelHeight { get; set; } = 32;

        /// <summary>How wide the symbol on it is, in millimetres.</summary>
        public double SymbolWidth { get; set; } = 26;

        /// <summary>Whether the name goes under the mark.</summary>
        public bool ShowName { get; set; } = true;

        /// <summary>How many labels before it stops.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public LabelRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("name", Name)
                .Set("source", Source.ToString())
                .Set("property", PropertyName)
                .Set("category", PropertyCategory)
                .Set("prefix", Prefix)
                .Set("level", Level.ToString())
                .Set("width", LabelWidth)
                .Set("height", LabelHeight)
                .Set("symbol", SymbolWidth)
                .Set("showName", ShowName)
                .Set("ceiling", Ceiling);

        /// <summary>Read one back. An unreadable one comes back working.</summary>
        /// <param name="json">What was written.</param>
        public static LabelRecipe FromJson(JsonValue? json)
        {
            var recipe = new LabelRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            if (Enum.TryParse<LabelSource>(json["source"].AsString() ?? string.Empty, out var source))
                recipe.Source = source;

            if (Enum.TryParse<QrLevel>(json["level"].AsString() ?? string.Empty, out var level))
                recipe.Level = level;

            recipe.PropertyName = (json["property"].AsString() ?? string.Empty).Trim();
            recipe.PropertyCategory = (json["category"].AsString() ?? string.Empty).Trim();
            recipe.Prefix = (json["prefix"].AsString() ?? string.Empty).Trim();
            recipe.ShowName = json["showName"].AsBool(true);

            recipe.LabelWidth = Sane(json["width"].AsDouble(), 20, 300, 62);
            recipe.LabelHeight = Sane(json["height"].AsDouble(), 12, 300, 32);
            recipe.SymbolWidth = Sane(json["symbol"].AsDouble(), 8, 200, 26);

            var ceiling = (int)json["ceiling"].AsLong();

            recipe.Ceiling = ceiling > 0 ? ceiling : DefaultCeiling;

            // A symbol wider than the label it sits on prints off the edge of the sticker, which
            // nobody notices until a whole sheet has gone through.
            var room = Math.Min(recipe.LabelWidth, recipe.LabelHeight) - 4;

            if (recipe.SymbolWidth > room) recipe.SymbolWidth = Math.Max(8, room);

            return recipe;
        }

        private static double Sane(double value, double least, double most, double fallback) =>
            value >= least && value <= most ? value : fallback;

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + LabelSheet.Word(Source)
            + (Prefix.Length == 0 ? string.Empty : ", behind " + Prefix);
    }

    /// <summary>The labels, and what could not be made.</summary>
    public sealed class LabelRun
    {
        internal LabelRun(LabelRecipe recipe, IReadOnlyList<SiteLabel> labels, int elements,
                          int nameless, int oversize, int dropped,
                          IReadOnlyList<string> repeated, IReadOnlyList<string> notes,
                          string? refusal)
        {
            Recipe = recipe;
            Labels = labels;
            Elements = elements;
            Nameless = nameless;
            Oversize = oversize;
            Dropped = dropped;
            Repeated = repeated;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What was asked for.</summary>
        public LabelRecipe Recipe { get; }

        /// <summary>The labels.</summary>
        public IReadOnlyList<SiteLabel> Labels { get; }

        /// <summary>How many elements were looked at.</summary>
        public int Elements { get; }

        /// <summary>How many carried nothing to identify them by.</summary>
        public int Nameless { get; }

        /// <summary>How many carried more than the biggest symbol holds.</summary>
        public int Oversize { get; }

        /// <summary>How many did not fit under the ceiling.</summary>
        public int Dropped { get; }

        /// <summary>Identities that turned up on more than one element.</summary>
        public IReadOnlyList<string> Repeated { get; }

        /// <summary>Anything worth saying about the run.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there are no labels, when there are none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is a sheet worth printing.</summary>
        public bool IsUsable => Refusal == null && Labels.Count > 0;

        /// <summary>What it made, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            var text = Labels.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Labels.Count == 1 ? " label off " : " labels off ")
                     + Elements.ToString("N0", CultureInfo.InvariantCulture)
                     + (Elements == 1 ? " element." : " elements.");

            if (Nameless > 0)
                text += " " + Nameless.ToString("N0", CultureInfo.InvariantCulture)
                     + (Nameless == 1 ? " element carries" : " elements carry")
                     + " nothing to identify " + (Nameless == 1 ? "it" : "them")
                     + " by, so " + (Nameless == 1 ? "it has" : "they have") + " no label.";

            if (Oversize > 0)
                text += " " + Oversize.ToString("N0", CultureInfo.InvariantCulture)
                     + " would not fit in any symbol and " + (Oversize == 1 ? "was" : "were")
                     + " left off.";

            if (Dropped > 0)
                text += " " + Dropped.ToString("N0", CultureInfo.InvariantCulture)
                     + " more did not fit under the ceiling.";

            if (Repeated.Count > 0)
                text += " " + Repeated.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Repeated.Count == 1 ? " identity is" : " identities are")
                     + " on more than one element, so scanning "
                     + (Repeated.Count == 1 ? "that label" : "those labels")
                     + " cannot say which: " + string.Join(", ", Repeated.Take(5))
                     + (Repeated.Count > 5 ? " and others." : ".");

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The model, as labels somebody can put on the work.
    ///
    /// A label is the one thing in this product that ends up in somebody's hand. It carries a code a
    /// phone reads and the same identifier printed underneath in characters, because the code will
    /// be scuffed, or wet, or in a riser with no light, and somebody will have to type it.
    ///
    /// It does not read codes back. Scanning one and having Navisworks fly to that element needs a
    /// camera and a person standing in front of the work, which is a phone application rather than a
    /// plug-in. What is here is the half this end can do properly: make the codes, print them, and
    /// keep an index of what was printed so whatever does the scanning has something to look up.
    /// </summary>
    public static class LabelSheet
    {
        /// <summary>Where saved label runs live in the project store.</summary>
        public const string StoreKey = "labels";

        /// <summary>Make the labels.</summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">What goes on them.</param>
        /// <param name="identity">
        /// What a property-sourced identity reads off an element. Null when the caller has no
        /// properties to hand, which makes a property-sourced run refuse rather than come back empty.
        /// </param>
        public static LabelRun Make(IEnumerable<FieldElement>? elements, LabelRecipe? recipe,
                                    Func<FieldElement, string?>? identity = null)
        {
            var wanted = recipe ?? new LabelRecipe();
            var notes = new List<string>();

            var all = (elements ?? Enumerable.Empty<FieldElement>()).Where(e => e != null).ToList();

            if (all.Count == 0)
                return Nothing(wanted, notes,
                    "There is nothing selected to label. Select what is going on site and read it "
                    + "again.");

            if (wanted.Source == LabelSource.Property && identity == null)
                return Nothing(wanted, notes,
                    "The labels are set to come off a property, but no properties have been read. "
                    + "Read the selection first, or take the identity off the mark instead.");

            var ceiling = wanted.Ceiling > 0 ? wanted.Ceiling : LabelRecipe.DefaultCeiling;
            var labels = new List<SiteLabel>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nameless = 0;
            var oversize = 0;
            var dropped = 0;

            foreach (var element in all)
            {
                var found = Identify(element, wanted, identity);

                if (string.IsNullOrWhiteSpace(found)) { nameless++; continue; }

                var text = found!.Trim();

                if (labels.Count >= ceiling) { dropped++; continue; }

                var encoded = wanted.Prefix + text;
                var symbol = QrCode.Encode(encoded, wanted.Level);

                if (symbol == null) { oversize++; continue; }

                seen[text] = seen.TryGetValue(text, out var already) ? already + 1 : 1;

                var lines = new List<string>();

                if (wanted.ShowName && !string.Equals(element.Name, text, StringComparison.Ordinal))
                    lines.Add(element.Name);

                if (wanted.Prefix.Length > 0) lines.Add(text);

                labels.Add(new SiteLabel(element.Key, text, encoded, text, lines, symbol));
            }

            var repeated = seen.Where(p => p.Value > 1).Select(p => p.Key)
                               .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

            if (labels.Count == 0)
                return Nothing(wanted, notes,
                    "No label came out. Nothing in that selection carries "
                    + Word(wanted.Source) + " to print.");

            if (wanted.Prefix.Length == 0)
                notes.Add("These codes say exactly what is printed under them. To make each one a "
                          + "link into a document system, put its address in front.");

            return new LabelRun(wanted, labels, all.Count, nameless, oversize, dropped,
                                repeated, notes, null);
        }

        /// <summary>Where a label's identity comes from, in words.</summary>
        /// <param name="source">The source.</param>
        public static string Word(LabelSource source) => source switch
        {
            LabelSource.Mark => "the mark, or the name where there is no mark",
            LabelSource.Property => "a property",
            LabelSource.Key => "the identity CamelWorks knows it by",
            _ => source.ToString(),
        };

        /// <summary>
        /// The sheet, as a page that prints.
        ///
        /// Its own small page rather than the report engine's, because a sheet of labels is a grid
        /// measured in millimetres that has to line up with die-cut stock, and a report is a column
        /// of blocks that reflows. The one thing that must not happen to a label sheet is reflow.
        /// </summary>
        /// <param name="run">What was made.</param>
        /// <param name="project">The job's name, for the corner of the page.</param>
        public static string Html(LabelRun? run, string? project = null)
        {
            var page = new StringBuilder();

            page.Append("<!DOCTYPE html>\r\n<html lang=\"en\"><head><meta charset=\"utf-8\">");
            page.Append("<title>").Append(Escape(run?.Recipe.Name ?? "Labels")).Append("</title>");

            var width = run?.Recipe.LabelWidth ?? 62;
            var height = run?.Recipe.LabelHeight ?? 32;
            var symbol = run?.Recipe.SymbolWidth ?? 26;

            page.Append("<style>")
                .Append("@page{size:A4;margin:10mm}")
                .Append("*{box-sizing:border-box}")
                .Append("body{margin:0;font:11px/1.3 'Segoe UI',Arial,sans-serif;color:#111}")
                .Append("h1{font-size:14px;margin:0 0 4mm}")
                .Append(".said{font-size:11px;color:#444;margin:0 0 4mm;max-width:170mm}")
                .Append(".sheet{display:flex;flex-wrap:wrap;gap:0}")
                .Append(".label{width:").Append(Number(width)).Append("mm;height:")
                .Append(Number(height)).Append("mm;padding:2mm;display:flex;align-items:center;")
                .Append("gap:2mm;border:0.2mm dashed #bbb;overflow:hidden;page-break-inside:avoid;")
                .Append("break-inside:avoid}")
                .Append(".code{width:").Append(Number(symbol)).Append("mm;flex:0 0 auto}")
                .Append(".code svg{display:block;width:100%;height:auto}")
                .Append(".words{min-width:0;flex:1 1 auto}")
                .Append(".mark{font-weight:700;font-size:12px;word-break:break-all}")
                .Append(".line{font-size:9px;color:#555;white-space:nowrap;overflow:hidden;")
                .Append("text-overflow:ellipsis}")
                .Append("@media print{.said,h1{display:none}.label{border-color:transparent}}")
                .Append("</style></head><body>");

            page.Append("<h1>").Append(Escape(run?.Recipe.Name ?? "Labels"));

            if (!string.IsNullOrWhiteSpace(project))
                page.Append(" — ").Append(Escape(project!.Trim()));

            page.Append("</h1>");

            if (run == null || !run.IsUsable)
            {
                page.Append("<p class=\"said\">")
                    .Append(Escape(run?.Explain() ?? "Nothing has been labelled."))
                    .Append("</p></body></html>");

                return page.ToString();
            }

            page.Append("<p class=\"said\">").Append(Escape(run.Explain())).Append("</p>");
            page.Append("<div class=\"sheet\">");

            foreach (var label in run.Labels)
            {
                page.Append("<div class=\"label\"><div class=\"code\">");
                page.Append(label.Symbol == null
                                ? string.Empty
                                : label.Symbol.Svg(run.Recipe.SymbolWidth));
                page.Append("</div><div class=\"words\"><div class=\"mark\">")
                    .Append(Escape(label.Heading)).Append("</div>");

                foreach (var line in label.Lines)
                    page.Append("<div class=\"line\">").Append(Escape(line)).Append("</div>");

                page.Append("</div></div>");
            }

            page.Append("</div></body></html>");

            return page.ToString();
        }

        /// <summary>The index of what was printed, so whatever scans the codes has something to look them up in.</summary>
        /// <param name="run">What was made.</param>
        public static string Csv(LabelRun? run)
        {
            var text = new StringBuilder();

            text.Append("Identity,Encoded,Element,Model,Version,Level\r\n");

            if (run == null) return text.ToString();

            foreach (var label in run.Labels)
            {
                text.Append(Cell(label.Identity)).Append(',');
                text.Append(Cell(label.Encoded)).Append(',');
                text.Append(Cell(label.Heading)).Append(',');
                text.Append(Cell(label.Key.ModelScope)).Append(',');
                text.Append((label.Symbol?.Version ?? 0).ToString(CultureInfo.InvariantCulture)).Append(',');
                text.Append(Cell(label.Symbol == null ? string.Empty : QrCode.Word(label.Symbol.Level)));
                text.Append("\r\n");
            }

            return text.ToString();
        }

        private static string? Identify(FieldElement element, LabelRecipe recipe,
                                        Func<FieldElement, string?>? identity) =>
            recipe.Source switch
            {
                LabelSource.Property => identity?.Invoke(element),
                LabelSource.Key => element.Key.ToString(),
                _ => element.Label,
            };

        private static LabelRun Nothing(LabelRecipe recipe, List<string> notes, string why) =>
            new LabelRun(recipe, Array.Empty<SiteLabel>(), 0, 0, 0, 0,
                         Array.Empty<string>(), notes, why);

        private static string Number(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Escape(string text)
        {
            var built = new StringBuilder(text.Length);

            foreach (var c in text)
                switch (c)
                {
                    case '&': built.Append("&amp;"); break;
                    case '<': built.Append("&lt;"); break;
                    case '>': built.Append("&gt;"); break;
                    case '"': built.Append("&quot;"); break;
                    default: built.Append(c); break;
                }

            return built.ToString();
        }

        private static string Cell(string? text)
        {
            var value = text ?? string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
