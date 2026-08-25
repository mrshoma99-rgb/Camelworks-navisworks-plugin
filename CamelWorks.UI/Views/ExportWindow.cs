using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Send somebody the model, or send them the numbers.
    ///
    /// <b>GLB is the one that matters.</b> It opens in a browser, on a phone, on a tablet on site,
    /// with nothing installed and no licence. Every other way of handing a client a model asks them
    /// to install something, and the tools that write this format for Navisworks are per-seat,
    /// per-format licences on top of the Navisworks the firm already bought.
    ///
    /// <b>It is geometry, not a model.</b> Triangles, names and colours go; properties, structure,
    /// clashes and viewpoints do not. That is stated on the screen rather than discovered, because
    /// somebody who sends a client a GLB expecting IFC has sent the wrong thing and will not find
    /// out until they are asked for a property.
    ///
    /// The spreadsheet and CSV halves are the properties, which is the other half of what people
    /// mean by "send them the model".
    /// </summary>
    public static class ExportWindow
    {
        /// <summary>How many elements one export will read before it stops and says so.</summary>
        public const int Ceiling = 250_000;


        /// <summary>
        /// Show the window.
        /// </summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("export"), () => Ui.Scroll(Body(session)), 720, 640);
        }

        private static UIElement Body(Session session)
        {
            var scope = "Current selection";
            var picker = Ui.Choice(new[] { "Current selection", "Everything loaded" }, scope,
                                   pick => scope = pick);

            return Ui.Stack(
                Ui.Heading("Export"),
                Ui.Sub("Geometry a browser can open, or the properties as a spreadsheet."),
                Ui.Field("Scope", picker),
                Geometry(session, () => scope),
                Properties(session, () => scope));
        }

        /// <summary>Which elements the scope means, in the order the model has them.</summary>
        private static IEnumerable<IModelItem> Scope(Session session, string scope) =>
            session.Model.Traverse(scope == "Current selection"
                ? TraversalScope.CurrentSelection
                : TraversalScope.WholeDocument);

        // ===================================================================================
        // Geometry
        // ===================================================================================

        private static UIElement Geometry(Session session, Func<string> scope)
        {
            var path = Ui.Text(Suggested(session, ".glb"), null, 380);
            var colour = "By discipline";
            var results = Ui.Stack();

            var colours = Ui.Choice(new[] { "By discipline", "By model", "One grey" }, colour,
                                    pick => colour = pick);

            var run = Ui.Runner("Write the GLB", results, job =>
            {
                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    results.Children.Add(Ui.Problem("Say where to write it."));
                    return;
                }

                job.Say("Reading geometry...");

                var geometry = new NavGeometry(session.Model);
                var meshes = new List<GltfMesh>();
                var read = 0;
                var silent = 0;
                var capped = false;

                foreach (var item in Scope(session, scope()))
                {
                    if (!job.Step(read, "elements read")) return;

                    if (read >= Ceiling) { capped = true; break; }

                    read++;

                    if (!item.HasGeometry) continue;

                    var mesh = geometry.Read(item, Name(item));

                    // Counted, not dropped. An element the tree says has geometry and the COM
                    // layer will not give triangles for is worth a number on the screen: it is
                    // usually a handful and occasionally it is everything, and those two look
                    // identical if nothing counts them.
                    if (mesh == null) { silent++; continue; }

                    Paint(session, item, mesh, colour);
                    meshes.Add(mesh);
                }

                if (read == 0)
                {
                    results.Children.Add(Ui.Empty("Nothing in scope.",
                        scope() == "Current selection"
                            ? "Select some elements, or switch the scope to everything loaded."
                            : "No models are loaded."));
                    return;
                }

                job.Say("Writing the file...");

                var written = GltfWriter.Write(meshes, session.Profile.ProjectName);

                try
                {
                    File.WriteAllBytes(file, written.Bytes);
                }
                catch (Exception e)
                {
                    results.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                results.Children.Add(Ui.Line(written.Explain(), 1, true));
                results.Children.Add(Ui.Done("Written to " + file + "."));

                foreach (var note in written.Notes) results.Children.Add(Ui.Note(note));

                if (silent > 0)
                    results.Children.Add(Ui.Note(Ui.Count(silent, "element")
                        + " the model says carry geometry gave no triangles when read. A handful is "
                        + "ordinary - a group node that owns its children's geometry rather than any "
                        + "of its own reads this way. A large number means the read is failing, and "
                        + "the file is short by that much."));

                if (capped)
                    results.Children.Add(Ui.Note("It stopped at " + Ui.Count(Ceiling, "element")
                        + ". That is a limit on this export, not on the model: what is in the file is "
                        + "the first " + Ui.Count(Ceiling, "element") + " of the scope and the rest is "
                        + "not. Export a selection or a level rather than the whole federation."));

                session.Record(ActivityKind.Export,
                    "wrote " + Ui.Count(written.Meshes, "object") + " as GLB");
            });

            return Ui.Card("Geometry, as a GLB a browser can open",
                "One file. Nothing to install at the other end.",
                Ui.Line("What goes in it: triangles, one named object per element, and a colour. "
                        + "Coordinates are in metres and the model comes out the right way up.", 0.85),
                Ui.Line("What does not: every property, the selection tree's structure, clashes, "
                        + "viewpoints, sets and layers. This is geometry, not a model - if somebody "
                        + "is going to ask you for a property, they want IFC and not this.", 0.85),
                Ui.Field("Colour", colours),
                Ui.PathField("Write to", path, PathKind.Save),
                run,
                results);
        }

        /// <summary>What the object is called in the viewer's tree.</summary>
        private static string Name(IModelItem item)
        {
            var name = item.DisplayName;

            if (!string.IsNullOrWhiteSpace(name)) return name;

            // An export full of objects called "element" is one nobody can navigate, so the
            // category is a better fallback than a number.
            return item.Category ?? item.TypeName ?? "element";
        }

        /// <summary>
        /// Give the mesh a colour that means something.
        ///
        /// Not the model's own materials: reading those is a second COM walk per fragment for a
        /// result that is usually "grey" anyway, and the thing a coordinator actually wants when
        /// they send a client a GLB is to be able to tell the services from the structure.
        /// </summary>
        private static void Paint(Session session, IModelItem item, GltfMesh mesh, string by)
        {
            if (by == "One grey") return;

            var key = by == "By model"
                ? item.Model.DisplayName
                : session.Profile.DisciplineOf(item.Model.DisplayName) ?? item.Model.DisplayName;

            mesh.Material = string.IsNullOrWhiteSpace(key) ? "default" : key;

            // A hash rather than a palette, because a federation has as many disciplines as it has
            // and running out of colours mid-export would put two of them in the same one. Kept
            // light and unsaturated so overlapping objects stay readable.
            var hash = 0;
            foreach (var c in mesh.Material) hash = ((hash * 31) + c) & 0x7FFFFFFF;

            mesh.Red = 0.45 + (((hash >> 0) & 0xFF) / 255.0 * 0.45);
            mesh.Green = 0.45 + (((hash >> 8) & 0xFF) / 255.0 * 0.45);
            mesh.Blue = 0.45 + (((hash >> 16) & 0xFF) / 255.0 * 0.45);
        }

        // ===================================================================================
        // Properties
        // ===================================================================================

        private static UIElement Properties(Session session, Func<string> scope)
        {
            var path = Ui.Text(Suggested(session, ".xlsx"), null, 380);
            var format = "Spreadsheet (.xlsx)";
            var tab = Ui.Text("Element", null, 150);
            var names = Ui.Text("Category, Type, Level, Discipline", null, 300);
            var results = Ui.Stack();

            var formats = Ui.Choice(new[] { "Spreadsheet (.xlsx)", "Comma-separated (.csv)" }, format,
                pick =>
                {
                    format = pick;

                    // The extension follows the format, because a CSV called .xlsx is a file Excel
                    // refuses to open and blames on the person who sent it.
                    path.Text = System.IO.Path.ChangeExtension(
                        path.Text, pick.StartsWith("Comma", StringComparison.Ordinal) ? ".csv" : ".xlsx");
                });

            var run = Ui.Runner("Write it", results, job =>
            {
                var file = path.Text.Trim();

                if (file.Length == 0)
                {
                    results.Children.Add(Ui.Problem("Say where to write it."));
                    return;
                }

                var category = tab.Text.Trim();

                var wanted = names.Text
                    .Split(',')
                    .Select(n => n.Trim())
                    .Where(n => n.Length > 0)
                    .ToList();

                if (wanted.Count == 0)
                {
                    results.Children.Add(Ui.Problem("Name at least one property to write."));
                    return;
                }

                job.Say("Reading properties...");

                var headers = new List<string> { "Element", "Model" };
                headers.AddRange(wanted);

                var table = new TableBlock(headers.ToArray());
                var rows = 0;
                var blank = 0;

                foreach (var item in Scope(session, scope()))
                {
                    if (!job.Step(rows, "elements read")) return;

                    var cells = new List<string?> { item.DisplayName, item.Model.DisplayName };

                    var anything = false;

                    foreach (var property in wanted)
                    {
                        var value = item.Property(category, property)
                                    ?? item.Property("Item", property)
                                    ?? item.Property(NavWriteTransaction.TabName, property);

                        if (value != null) anything = true;

                        cells.Add(value);
                    }

                    // An element carrying none of the asked-for properties is still a row. Dropping
                    // it would make a spreadsheet that quietly disagrees with the model's own count,
                    // which is the number somebody checks it against.
                    if (!anything) blank++;

                    table.Row(cells.ToArray());
                    rows++;
                }

                if (rows == 0)
                {
                    results.Children.Add(Ui.Empty("Nothing in scope.",
                        scope() == "Current selection"
                            ? "Select some elements, or switch the scope to everything loaded."
                            : "No models are loaded."));
                    return;
                }

                job.Say("Writing the file...");

                try
                {
                    if (format.StartsWith("Comma", StringComparison.Ordinal))
                        File.WriteAllText(file, Csv(table), new UTF8Encoding(true));
                    else
                        File.WriteAllBytes(file, XlsxWriter.Write("Properties", table));
                }
                catch (Exception e)
                {
                    results.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                results.Children.Add(Ui.Line(Ui.Count(rows, "row") + " written to " + file + ".", 1, true));

                if (blank > 0)
                    results.Children.Add(Ui.Note(Ui.Count(blank, "element")
                        + " carry none of those properties and are in the file as empty rows. They are "
                        + "not dropped: a spreadsheet whose row count disagrees with the model's is one "
                        + "somebody spends an afternoon reconciling."));

                session.Record(ActivityKind.Export, "wrote " + Ui.Count(rows, "row") + " of properties");
            });

            return Ui.Card("Properties, as a spreadsheet",
                "The other half of what people mean by \"send them the model\".",
                Ui.Field("Format", formats),
                Ui.Field("Property tab", tab),
                Ui.Field("Properties", names),
                Ui.PathField("Write to", path, PathKind.Save),
                run,
                results);
        }

        /// <summary>
        /// Comma-separated text, with a BOM written by the caller.
        ///
        /// The BOM is what makes Excel open a UTF-8 CSV as UTF-8 rather than as the machine's
        /// ANSI code page, which is how an "Ø" in a Danish level name becomes two characters.
        /// </summary>
        private static string Csv(TableBlock table)
        {
            var text = new StringBuilder();

            text.AppendLine(string.Join(",", table.Headers.Select(Escape)));

            foreach (var row in table.Rows)
                text.AppendLine(string.Join(",", row.Select(c => Escape(c ?? string.Empty))));

            return text.ToString();
        }

        private static string Escape(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"") + "\"";

        /// <summary>A path beside the document, or in the user's own folder.</summary>
        private static string Suggested(Session session, string extension)
        {
            var name = Deliverable.Safe(session.Profile.ProjectName) + extension;

            try
            {
                var saved = session.SavedPath;

                if (!string.IsNullOrWhiteSpace(saved))
                {
                    var folder = System.IO.Path.GetDirectoryName(saved);

                    if (!string.IsNullOrEmpty(folder)) return System.IO.Path.Combine(folder!, name);
                }

                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                return string.IsNullOrEmpty(documents) ? name : System.IO.Path.Combine(documents, name);
            }
            catch (Exception)
            {
                return name;
            }
        }
    }
}
