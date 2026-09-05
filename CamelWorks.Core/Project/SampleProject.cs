using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// A worked project file, for reading rather than for working in.
    ///
    /// Every screen in CamelWorks runs with nothing set up, which is the point of the product and
    /// also why nobody sees what a set-up project looks like. Somebody ready to enter their firm's
    /// parties, priorities and codes has no example of the shape those take, and "fill in the
    /// Profile tab" says what to press rather than what to write.
    ///
    /// So: a real project file with a real coordination setup in it, which opens in the Project tab
    /// like any other. Fictional job, fictional firms, no client data.
    ///
    /// It is a file rather than a mode. Nothing in the product treats it differently from a project
    /// somebody made, so everything in it can be changed, deleted or copied into a real job.
    /// </summary>
    public static class SampleProject
    {
        /// <summary>What the sample job is called.</summary>
        public const string JobName = "Riverside Depot (sample)";

        /// <summary>What to call the file on disk.</summary>
        public const string FileName = "Riverside Depot (sample).camelworks";

        /// <summary>
        /// The whole file, as text ready to write.
        /// </summary>
        /// <param name="whenTicks">
        /// The timestamp to stamp the activity entries with, so the file is reproducible. Pass the
        /// current time for a file that reads as "just now".
        /// </param>
        public static string Write(long whenTicks)
        {
            var document = VersionedDocument.Create(ProjectStore.SchemaVersion);

            Profile(document.Section(ProjectStore.ProfileSection));
            Sets(document.Section(ProjectStore.SetsSection));
            Layers(document.Section(ProjectStore.LayersSection));

            // Straight onto the root, because the activity log is stored as a bare array where
            // every other section is an object. Section() would have made it an object and the
            // store would read the log back as empty.
            document.Root.Set(ProjectStore.ActivitySection, History(whenTicks));

            return document.Save();
        }

        /// <summary>What the sample is meant to show, in the words the window uses.</summary>
        public static IReadOnlyList<string> Explains { get; } = new[]
        {
            "Four parties with the initials a report and a BCF file will use, and three priorities "
            + "in the order they sort in.",
            "A discipline code table - the first two letters of a model name mapped to who owns it - "
            + "so the board can say \"MEP\" about a file called \"RD-MEP-M3-Ducts.nwc\".",
            "Clash settings that are not the defaults, so it is obvious which numbers are yours to "
            + "change: a 15 mm tolerance and a 250 mm proximity band.",
            "Four saved sets built as rules rather than as lists, including one that ANDs two "
            + "properties and one that subtracts.",
            "An appearance layer that greys the structure back, of the kind you switch on before a "
            + "coordination meeting and off afterwards.",
        };

        private static void Profile(JsonValue profile)
        {
            profile.Set(ProfileKeys.ProjectName, JsonValue.String(JobName));
            profile.Set(ProfileKeys.Author, JsonValue.String("A. Coordinator"));

            // Not the defaults, deliberately. A sample whose numbers match the built-in ones teaches
            // nothing about which numbers are yours to change.
            profile.Set(ProfileKeys.ClashTolerance, JsonValue.Number(0.015));
            profile.Set(ProfileKeys.ClashProximity, JsonValue.Number(0.25));
            profile.Set(ProfileKeys.Units, JsonValue.String("Metres"));

            profile.Set(ProfileKeys.Parties, JsonValue.String(
                "Meridian Architects (ARC)\n"
                + "Halden Structural (STR)\n"
                + "Ovett Building Services (MEP)\n"
                + "Riverside Contracting (CON)"));

            profile.Set(ProfileKeys.Priorities, JsonValue.String("Blocker\nThis week\nWhen convenient"));

            profile.Set(ProfileKeys.LevelSource, JsonValue.String("Grid"));

            // The discipline map the board reads to say who owns a model. Two letters out of a file
            // name is how every naming standard in the industry actually gets used.
            profile.Set(ProfileKeys.DisciplinePrefix + "AR", JsonValue.String("ARC"));
            profile.Set(ProfileKeys.DisciplinePrefix + "ST", JsonValue.String("STR"));
            profile.Set(ProfileKeys.DisciplinePrefix + "ME", JsonValue.String("MEP"));
            profile.Set(ProfileKeys.DisciplinePrefix + "EL", JsonValue.String("MEP"));

            var codes = new CodeTables();

            codes.Put(new CodeTable("Discipline", new[]
            {
                new CodeEntry("ARC", "Architecture"),
                new CodeEntry("STR", "Structure"),
                new CodeEntry("MEP", "Mechanical, electrical and public health"),
                new CodeEntry("CON", "Main contractor"),
            }));

            codes.Put(new CodeTable("Zone", new[]
            {
                new CodeEntry("Z1", "Workshop"),
                new CodeEntry("Z2", "Offices"),
                new CodeEntry("Z3", "Yard and canopy"),
            }));

            profile.Set("codeTables", codes.ToJson());
        }

        private static void Sets(JsonValue sets)
        {
            var library = new SetLibrary();

            library.Put(new SavedSet("set-1", "Ductwork",
                SetExpression.Where("Element", "Category", SetOperator.Equals, "Ducts"))
            {
                Note = "A rule, not a list, so it also covers ducts added later.",
            });

            library.Put(new SavedSet("set-2", "Level 01 structure",
                SetExpression.And(
                    SetExpression.Where("Element", "Level", SetOperator.Equals, "Level 01"),
                    SetExpression.Where("Element", "Discipline", SetOperator.Equals, "STR")))
            {
                Note = "Two conditions ANDed. This is the shape the family generator produces.",
            });

            library.Put(new SavedSet("set-3", "Services, but not the small stuff",
                SetExpression.And(
                    SetExpression.Where("Element", "Discipline", SetOperator.Equals, "MEP"),
                    SetExpression.Not(
                        SetExpression.Where("Element", "Size", SetOperator.LessThan, "50"))))
            {
                Note = "A negation, which compiles to a subtraction rather than a not-equals - "
                     + "so an element with no Size at all is still in it, which is what you want.",
            });

            library.Put(new SavedSet("set-4", "Everything above the canopy",
                SetExpression.Where("Element", "Elevation", SetOperator.GreaterThan, "6.5"))
            {
                Note = "A numeric comparison. The value is in metres, as everything stored is.",
            });

            sets.Set("items", library.ToJson());
        }

        private static void Layers(JsonValue layers)
        {
            // A rule target rather than a list of elements, because the sample has to mean
            // something in a model it has never seen: a rule re-runs its search wherever it is
            // opened, where saved element keys would resolve to nothing in anybody else's model
            // and leave a layer that claims to cover things and covers none.
            var layer = new AppearanceLayer("layer-1", "Structure, greyed back",
                LayerTarget.Set(SetExpression.Where("Element", "Discipline", SetOperator.Equals, "STR")))
            {
                Note = "The one you switch on before a coordination meeting and off afterwards.",
                Colour = new Colour(0x9a, 0xa0, 0xa6),
                Transparency = 0.65,
            };

            layers.Set("stack", LayerStackJson.Write(new[] { layer }));
        }

        // A few entries so the Home screen has something to show, dated backwards from the moment
        // the file was made. A sample whose history is empty looks broken rather than new.
        private static JsonValue History(long whenTicks)
        {
            var log = new ActivityLog();
            var day = TimeSpan.TicksPerDay;

            log.Record(new Activity(ActivityKind.Reconcile, whenTicks - (3 * day),
                                    "made 4 sets split on Discipline"));

            log.Record(new Activity(ActivityKind.Report, whenTicks - (2 * day),
                                    "took off volume by category: 412 elements in 9 groups"));

            log.Record(new Activity(ActivityKind.Export, whenTicks - day,
                                    "exported 18 issues as BCF 2.1"));

            return log.ToJson();
        }

        /// <summary>The note that goes beside the file, so somebody opening it knows what it is.</summary>
        public static string ReadMe()
        {
            var lines = new List<string>
            {
                JobName,
                new string('=', JobName.Length),
                string.Empty,
                "A sample CamelWorks project file. The job, the firms and the models in it are made "
                + "up; none of it comes from a real project.",
                string.Empty,
                "Open a model, save it in this folder, and rename this file to match the model's "
                + "name to have CamelWorks pick it up. Or just read it: it is JSON, and every "
                + "section is what one tab of the Project workspace writes.",
                string.Empty,
                "What is in it:",
                string.Empty,
            };

            lines.AddRange(Explains.Select((e, i) =>
                (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + e));

            lines.Add(string.Empty);
            lines.Add("Nothing in CamelWorks treats this file as special. Change it, delete parts of "
                      + "it, or copy the parts you want into a real job.");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
