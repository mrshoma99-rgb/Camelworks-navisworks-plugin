using System;
using System.IO;
using System.Linq;
using System.Text;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class IdsWriterTests
    {
        /// <summary>Every wall must carry a fire rating - the specification everybody writes first.</summary>
        private static IdsDocument FireRating()
        {
            var document = new IdsDocument("Riverside information requirements")
            {
                Version = "1.2",
                Author = "coordination@bimcamel.com",
                Description = "What the model has to carry before it is issued.",
            };

            var spec = new IdsSpecification("Walls carry a fire rating")
            {
                Description = "Every wall, every revision.",
                Instructions = "Fill it in on the type, not the instance.",
                MinOccurs = 1,
            };

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));

            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_WallCommon"), IdsValue.Simple("FireRating"),
                IdsValue.Restriction(enumeration: new[] { "30", "60", "120" }), "IFCLABEL"));

            document.Specifications.Add(spec);

            return document;
        }

        private static IdsReadResult Reread(IdsWriteResult written)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(written.Text)))
                return IdsReader.Read(stream);
        }

        // -----------------------------------------------------------------------------------
        // Out and back
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_file_we_wrote_reads_back_as_the_specification_it_was()
        {
            var written = IdsWriter.Write(FireRating(), 637_000_000_000_000_000L);

            Assert.Null(written.Problem);
            Assert.True(written.IsUsable);
            Assert.Equal(1, written.Specifications);
            Assert.Equal(2, written.Facets);

            var read = Reread(written);

            Assert.True(read.IsUsable);

            var document = read.Document!;

            Assert.Equal("Riverside information requirements", document.Title);
            Assert.Equal("1.2", document.Version);
            Assert.Equal("coordination@bimcamel.com", document.Author);

            var spec = Assert.Single(document.Specifications);

            Assert.Equal("Walls carry a fire rating", spec.Name);
            Assert.Equal("Every wall, every revision.", spec.Description);
            Assert.Equal(1, spec.MinOccurs);
            Assert.Equal(new[] { "IFC4" }, spec.IfcVersions);

            var applies = Assert.IsType<IdsEntityFacet>(Assert.Single(spec.Applicability));
            Assert.True(applies.Name.Matches("IFCWALL"));

            var wants = Assert.IsType<IdsPropertyFacet>(Assert.Single(spec.Requirements));

            Assert.True(wants.PropertySet.Matches("Pset_WallCommon"));
            Assert.True(wants.BaseName.Matches("FireRating"));
            Assert.True(wants.Value!.Matches("60"));
            Assert.False(wants.Value.Matches("45"));
        }

        [Fact]
        public void The_file_declares_the_namespaces_a_reader_looks_for()
        {
            var text = IdsWriter.Write(FireRating()).Text;

            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text, StringComparison.Ordinal);
            Assert.Contains("xmlns=\"" + IdsWriter.Namespace + "\"", text, StringComparison.Ordinal);
            Assert.Contains("xmlns:xs=\"" + IdsWriter.Schema + "\"", text, StringComparison.Ordinal);
            Assert.Contains("<specifications>", text, StringComparison.Ordinal);
            Assert.Contains("ifcVersion=\"IFC4\"", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_date_is_written_when_one_is_given_and_left_out_when_it_is_not()
        {
            Assert.Contains("<date>2019-", IdsWriter.Write(FireRating(), 637_000_000_000_000_000L).Text,
                            StringComparison.Ordinal);

            Assert.DoesNotContain("<date>", IdsWriter.Write(FireRating()).Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pattern_goes_out_as_the_author_spelled_it()
        {
            // Not as the anchored form the reader compiles: a file that comes back with \A(?:...)\z
            // in it says something subtly different from the one somebody sent.
            var document = new IdsDocument("Codes");
            var spec = new IdsSpecification("Every element carries a code");

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Identity"), IdsValue.Simple("Code"),
                IdsValue.Restriction(pattern: "[A-Z]{2}-[0-9]{4}")));

            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.Contains("<xs:pattern value=\"[A-Z]{2}-[0-9]{4}\" />", written.Text,
                            StringComparison.Ordinal);
            Assert.DoesNotContain(@"\A(?:", written.Text, StringComparison.Ordinal);

            var back = Assert.IsType<IdsPropertyFacet>(
                Assert.Single(Reread(written).Document!.Specifications[0].Requirements));

            Assert.True(back.Value!.Matches("AB-1234"));
            Assert.False(back.Value.Matches("xAB-1234"));
        }

        [Fact]
        public void Every_bound_and_length_survives()
        {
            var document = new IdsDocument("Bounds");
            var spec = new IdsSpecification("Thickness is sane");

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Qto_WallBaseQuantities"), IdsValue.Simple("Width"),
                IdsValue.Restriction(minInclusive: 75, maxExclusive: 500, minLength: 2)));

            document.Specifications.Add(spec);

            var text = IdsWriter.Write(document).Text;

            Assert.Contains("<xs:minInclusive value=\"75\" />", text, StringComparison.Ordinal);
            Assert.Contains("<xs:maxExclusive value=\"500\" />", text, StringComparison.Ordinal);
            Assert.Contains("<xs:minLength value=\"2\" />", text, StringComparison.Ordinal);
            Assert.DoesNotContain("maxInclusive", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_kind_of_facet_this_model_has_is_written()
        {
            var document = new IdsDocument("All of them");
            var spec = new IdsSpecification("One of each");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCDUCTSEGMENT"),
                                                      IdsValue.Simple("RIGIDSEGMENT")));

            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));
            spec.Requirements.Add(new IdsClassificationFacet(IdsValue.Simple("Uniclass 2015"),
                                                             IdsValue.Simple("Ss_65_40")));
            spec.Requirements.Add(new IdsMaterialFacet(IdsValue.Simple("Steel")));
            spec.Requirements.Add(new IdsPartOfFacet(IdsValue.Simple("IFCSYSTEM"), "IFCRELASSIGNSTOGROUP"));

            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.Empty(written.Warnings);
            Assert.Equal(5, written.Facets);

            var text = written.Text;

            Assert.Contains("<predefinedType><simpleValue>RIGIDSEGMENT</simpleValue></predefinedType>",
                            text, StringComparison.Ordinal);
            Assert.Contains("<attribute cardinality=\"required\">", text, StringComparison.Ordinal);
            Assert.Contains("<classification cardinality=\"required\">", text, StringComparison.Ordinal);
            Assert.Contains("<material cardinality=\"required\">", text, StringComparison.Ordinal);

            // partOf wraps a whole entity rather than a bare name, which is the one place the file's
            // shape and this model's differ.
            Assert.Contains("<partOf relation=\"IFCRELASSIGNSTOGROUP\" cardinality=\"required\">",
                            text, StringComparison.Ordinal);
            Assert.Contains("<entity>", text, StringComparison.Ordinal);

            var back = Reread(written).Document!.Specifications[0];

            Assert.Equal(4, back.Requirements.Count);
        }

        [Fact]
        public void Prohibited_and_optional_come_out_as_the_schema_spells_them()
        {
            var document = new IdsDocument("Cardinality");
            var spec = new IdsSpecification("What must not be there");

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));

            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_Draft"), IdsValue.Simple("Placeholder"))
            {
                Cardinality = IdsCardinality.Prohibited,
            });

            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Description"))
            {
                Cardinality = IdsCardinality.Optional,
                Instructions = "Nice to have, not a hold point.",
            });

            document.Specifications.Add(spec);

            var text = IdsWriter.Write(document).Text;

            Assert.Contains("cardinality=\"prohibited\"", text, StringComparison.Ordinal);
            Assert.Contains("cardinality=\"optional\"", text, StringComparison.Ordinal);
            Assert.Contains("instructions=\"Nice to have, not a hold point.\"", text,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void The_occurs_go_on_applicability_where_the_schema_puts_them()
        {
            var document = new IdsDocument("Occurs");

            var spec = new IdsSpecification("At least one core wall")
            {
                MinOccurs = 1,
                MaxOccurs = 4,
            };

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            document.Specifications.Add(spec);

            var text = IdsWriter.Write(document).Text;

            Assert.Contains("<applicability minOccurs=\"1\" maxOccurs=\"4\">", text,
                            StringComparison.Ordinal);

            spec.MaxOccurs = null;

            Assert.Contains("maxOccurs=\"unbounded\"", IdsWriter.Write(document).Text,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void A_specification_with_nothing_required_writes_no_requirements_element()
        {
            var document = new IdsDocument("Existence");
            var spec = new IdsSpecification("There is at least one wall") { MinOccurs = 1 };

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.DoesNotContain("<requirements>", written.Text, StringComparison.Ordinal);
            Assert.Empty(written.Warnings);
            Assert.Equal(1, written.Facets);
        }

        // -----------------------------------------------------------------------------------
        // Refusing what nobody could load
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_specification_that_applies_to_everything_is_refused()
        {
            var document = new IdsDocument("Careless");
            document.Specifications.Add(new IdsSpecification("Everything must have a name"));

            var written = IdsWriter.Write(document);

            Assert.NotNull(written.Problem);
            Assert.Contains("applies to every element", written.Problem!, StringComparison.Ordinal);
            Assert.False(written.IsUsable);
            Assert.Equal(string.Empty, written.Text);
        }

        [Fact]
        public void Nothing_to_write_is_refused_in_a_sentence_that_says_what_to_do()
        {
            Assert.Contains("nothing to write", IdsWriter.Write(null).Problem!,
                            StringComparison.OrdinalIgnoreCase);

            Assert.Contains("Add one", IdsWriter.Write(new IdsDocument("Empty")).Problem!,
                            StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------------------
        // What it had to decide
        // -----------------------------------------------------------------------------------

        [Fact]
        public void An_IFC_version_nobody_named_is_chosen_and_said()
        {
            var document = new IdsDocument("Unversioned");
            var spec = new IdsSpecification("Walls");

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));
            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.Contains("ifcVersion=\"" + IdsWriter.DefaultIfcVersion + "\"", written.Text,
                            StringComparison.Ordinal);
            Assert.Contains("names no IFC version", Assert.Single(written.Warnings),
                            StringComparison.Ordinal);
        }

        [Fact]
        public void A_cardinality_on_applicability_has_nowhere_to_go_and_that_is_said()
        {
            var document = new IdsDocument("Misplaced");
            var spec = new IdsSpecification("Walls");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL"))
            {
                Cardinality = IdsCardinality.Prohibited,
            });
            spec.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));

            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.Contains("written as a plain filter", Assert.Single(written.Warnings),
                            StringComparison.Ordinal);

            // And written without it, because the schema has nowhere to put it.
            var applicability = written.Text.Substring(
                written.Text.IndexOf("<applicability", StringComparison.Ordinal),
                written.Text.IndexOf("</applicability>", StringComparison.Ordinal)
                    - written.Text.IndexOf("<applicability", StringComparison.Ordinal));

            Assert.DoesNotContain("cardinality", applicability, StringComparison.Ordinal);
        }

        [Fact]
        public void A_restriction_that_restricts_nothing_is_left_out_and_said()
        {
            var document = new IdsDocument("Empty restriction");
            var spec = new IdsSpecification("Walls");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_WallCommon"), IdsValue.Simple("FireRating"),
                IdsValue.Restriction()));

            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.DoesNotContain("<xs:restriction", written.Text, StringComparison.Ordinal);
            Assert.Contains("restricts nothing", Assert.Single(written.Warnings),
                            StringComparison.Ordinal);

            // Left out means "any value", which is what the property facet already says.
            var back = Assert.IsType<IdsPropertyFacet>(
                Assert.Single(Reread(written).Document!.Specifications[0].Requirements));

            Assert.Null(back.Value);
        }

        [Fact]
        public void A_specification_that_asks_for_nothing_at_all_is_written_and_flagged()
        {
            var document = new IdsDocument("Vacuous");
            var spec = new IdsSpecification("Walls");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.True(written.IsUsable);
            Assert.Contains("passes whatever the model holds", Assert.Single(written.Warnings),
                            StringComparison.Ordinal);
        }

        [Fact]
        public void A_title_with_XML_in_it_does_not_break_the_file()
        {
            var document = new IdsDocument("M&E <requirements> \"v2\"");
            var spec = new IdsSpecification("Ducts & pipes");

            spec.IfcVersions.Add("IFC4");
            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCDUCTSEGMENT")));
            document.Specifications.Add(spec);

            var written = IdsWriter.Write(document);

            Assert.Contains("M&amp;E &lt;requirements&gt;", written.Text, StringComparison.Ordinal);

            var read = Reread(written);

            Assert.Equal("M&E <requirements> \"v2\"", read.Document!.Title);
            Assert.Equal("Ducts & pipes", read.Document.Specifications[0].Name);
        }

        // -----------------------------------------------------------------------------------
        // The dump the schema validator checks. See schemas/ids/validate.sh.
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Writes_the_files_the_schema_validator_checks()
        {
            // No assertion here can prove an IDS file loads: it can be well-formed, round-trip
            // through our own reader, pass everything above, and still be rejected by the tool it
            // is sent to. So the real output is written out and xmllint checks it against the
            // published schema in CI. That is how the classification order was found - value
            // before system, which reads backwards and is what the schema says.
            var root = Path.Combine(AppContext.BaseDirectory, "ids-validation");
            Directory.CreateDirectory(root);

            var everything = new IdsDocument("Every facet")
            {
                Version = "1.0",
                Author = "coordination@bimcamel.com",
                Description = "One of each, so the shape of every one is checked.",
            };

            var spec = new IdsSpecification("One of each")
            {
                Identifier = "CW-001",
                Instructions = "This is the one the validator reads.",
                MinOccurs = 1,
                MaxOccurs = 500,
            };

            spec.IfcVersions.Add("IFC4");
            spec.IfcVersions.Add("IFC4X3_ADD2");

            spec.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCDUCTSEGMENT"),
                                                      IdsValue.Simple("RIGIDSEGMENT")));
            spec.Applicability.Add(new IdsClassificationFacet(IdsValue.Simple("Uniclass 2015")));

            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_DuctSegmentTypeCommon"), IdsValue.Simple("Status"),
                IdsValue.Restriction(enumeration: new[] { "NEW", "EXISTING" }), "IFCLABEL"));

            spec.Requirements.Add(new IdsAttributeFacet(
                IdsValue.Simple("Name"), IdsValue.Restriction(pattern: "[A-Z]{2}-[0-9]+"))
            {
                Instructions = "Tag it before issue.",
            });

            spec.Requirements.Add(new IdsClassificationFacet(IdsValue.Simple("Uniclass 2015"),
                                                             IdsValue.Simple("Ss_65_40")));

            spec.Requirements.Add(new IdsMaterialFacet(IdsValue.Simple("Steel"))
            {
                Cardinality = IdsCardinality.Optional,
            });

            spec.Requirements.Add(new IdsPartOfFacet(IdsValue.Simple("IFCSYSTEM"),
                                                     "IFCRELASSIGNSTOGROUP"));

            spec.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Qto_DuctSegmentBaseQuantities"), IdsValue.Simple("Length"),
                IdsValue.Restriction(minInclusive: 0.1, maxExclusive: 12, minLength: 1)));

            everything.Specifications.Add(spec);

            // And the smallest thing that is still an IDS, since the minimal shape breaks
            // differently from the full one.
            var bare = new IdsSpecification("There is at least one wall") { MinOccurs = 1 };
            bare.IfcVersions.Add("IFC2X3");
            bare.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));

            var minimal = new IdsDocument("Minimal");
            minimal.Specifications.Add(bare);

            // And a rule set that is half portable: one rule scoped to a set this model holds,
            // which has no place in the schema, beside one that travels. The file must carry the
            // second and still validate - a scope leaking into the applicability sequence is
            // exactly the failure the split exists to make impossible, and only the schema can
            // prove it did not.
            var mixed = new IdsDocument("Half of it travels");

            var scoped = new IdsSpecification("Ducts carry a system name") { Identifier = "CW-900" };
            scoped.Applicability.Add(new IdsScopeFacet(IdsScope.Of("Level 3 Ductwork")!));
            scoped.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Element"), IdsValue.Simple("System Name")));

            var travels = new IdsSpecification("Walls carry a fire rating") { Identifier = "CW-901" };
            travels.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            travels.Requirements.Add(new IdsPropertyFacet(
                IdsValue.Simple("Pset_WallCommon"), IdsValue.Simple("FireRating")));

            mixed.Specifications.Add(scoped);
            mixed.Specifications.Add(travels);

            var written = 0;

            foreach (var pair in new[]
            {
                ("everything.ids", IdsWriter.Write(everything, 637_000_000_000_000_000L)),
                ("minimal.ids", IdsWriter.Write(minimal)),
                ("fire-rating.ids", IdsWriter.Write(FireRating(), 637_000_000_000_000_000L)),
                ("half-portable.ids", IdsWriter.Write(mixed, 637_000_000_000_000_000L)),
            })
            {
                Assert.Null(pair.Item2.Problem);

                File.WriteAllText(Path.Combine(root, pair.Item1), pair.Item2.Text,
                                  new UTF8Encoding(false));
                written++;
            }

            Assert.Equal(4, written);
        }

        [Fact]
        public void The_readout_counts_what_went_in()
        {
            var written = IdsWriter.Write(FireRating());

            Assert.Equal("1 specification, 2 facets.", written.Explain());
            Assert.Equal(written.Explain(), written.ToString());

            Assert.Equal(IdsWriter.Write(null).Problem, IdsWriter.Write(null).Explain());
        }
    }
}
