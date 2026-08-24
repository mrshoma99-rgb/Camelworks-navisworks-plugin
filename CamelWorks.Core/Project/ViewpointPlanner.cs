using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Project
{
    /// <summary>Which way the camera looks at each element.</summary>
    public enum ViewFrom
    {
        /// <summary>From the front — looking along +Y.</summary>
        Front,

        /// <summary>From the back — looking along -Y.</summary>
        Back,

        /// <summary>From the left — looking along +X.</summary>
        Left,

        /// <summary>From the right — looking along -X.</summary>
        Right,

        /// <summary>From above, looking down. Right for anything read as a plan.</summary>
        Above,
    }

    /// <summary>How a run of generated viewpoints is named and framed.</summary>
    public sealed class ViewpointRecipe
    {
        /// <summary>
        /// The name, with <c>{n}</c> for the number and <c>{name}</c> for the element's own name.
        ///
        /// A pattern rather than a prefix and a suffix, because a prefix and a suffix is a pattern
        /// with the tokens in fixed places, and the first person who wants the number at the end
        /// has to be told no.
        /// </summary>
        public string Pattern { get; set; } = "{n} {name}";

        /// <summary>What the first one is numbered.</summary>
        public int Start { get; set; } = 1;

        /// <summary>How many digits the number is padded to.</summary>
        public int Padding { get; set; } = 3;

        /// <summary>Which folder to put them in, or null for the root.</summary>
        public string? Folder { get; set; }

        /// <summary>Which way the camera looks.</summary>
        public ViewFrom From { get; set; } = ViewFrom.Front;

        /// <summary>How much room to leave around the element, in metres.</summary>
        public double Margin { get; set; } = 2.0;

        /// <summary>Whether to cut a section just in front of the element.</summary>
        public bool Section { get; set; }

        /// <summary>How far in front of the element to cut, in metres.</summary>
        public double SectionOffset { get; set; } = 0.1;
    }

    /// <summary>One viewpoint the run will make.</summary>
    public sealed class PlannedView
    {
        internal PlannedView(ElementKey key, string name, int number)
        {
            Key = key; Name = name; Number = number;
        }

        /// <summary>Which element it frames.</summary>
        public ElementKey Key { get; }

        /// <summary>What it will be called.</summary>
        public string Name { get; }

        /// <summary>Its number in the run.</summary>
        public int Number { get; }

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>Every viewpoint a run will make, and everything it will not.</summary>
    public sealed class ViewpointRun
    {
        internal ViewpointRun(IReadOnlyList<PlannedView> views, IReadOnlyList<string> skipped)
        {
            Views = views; Skipped = skipped;
        }

        /// <summary>The viewpoints, in order.</summary>
        public IReadOnlyList<PlannedView> Views { get; }

        /// <summary>Why each element that gets none gets none.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>True when there is nothing to make.</summary>
        public bool IsEmpty => Views.Count == 0;

        /// <summary>One line saying what the run comes to.</summary>
        public string Explain()
        {
            if (Views.Count == 0 && Skipped.Count == 0)
                return "Nothing is selected, so there is nothing to make a viewpoint of.";

            var line = Views.Count == 0
                ? "No viewpoints to make"
                : Views.Count.ToString(CultureInfo.InvariantCulture)
                  + (Views.Count == 1 ? " viewpoint" : " viewpoints") + " to make";

            if (Skipped.Count > 0)
                line += ", " + Skipped.Count.ToString(CultureInfo.InvariantCulture)
                        + (Skipped.Count == 1 ? " element" : " elements") + " skipped";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Works out the viewpoints a selection is asking for, before any camera moves.
    ///
    /// Generating a viewpoint per element is the thing people do to turn a list of problems into a
    /// walkthrough somebody else can follow, and it is the difference between a coordination tool
    /// and a viewer. Doing it by hand is forty minutes of clicking, framing and typing, so nobody
    /// does it, so the review is one person driving and everybody else watching.
    ///
    /// The naming is here, and only the naming, because that is the half that can be got wrong
    /// quietly: a number that does not pad sorts 10 before 2 in every list the host shows, and two
    /// viewpoints with one name are indistinguishable the moment the person who made them leaves.
    /// The camera work is the host's, and cannot be tested off a Navisworks machine.
    /// </summary>
    public static class ViewpointPlanner
    {
        /// <summary>How many a single run will make before it stops.</summary>
        public const int DefaultCap = 500;

        /// <summary>Plan the run.</summary>
        /// <param name="elements">The elements, in the order they should be numbered.</param>
        /// <param name="recipe">How to name and frame them.</param>
        /// <param name="existing">Viewpoint names the document already has.</param>
        /// <param name="cap">How many to plan before stopping.</param>
        public static ViewpointRun Plan(IEnumerable<KeyValuePair<ElementKey, string?>>? elements,
                                        ViewpointRecipe? recipe,
                                        IReadOnlyCollection<string>? existing = null,
                                        int cap = DefaultCap)
        {
            var views = new List<PlannedView>();
            var skipped = new List<string>();

            if (elements == null) return new ViewpointRun(views, skipped);

            var how = recipe ?? new ViewpointRecipe();

            var taken = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var number = how.Start;

            foreach (var element in elements)
            {
                if (element.Key.IsEmpty)
                {
                    skipped.Add("An element with no identity was left out: nothing could frame it.");
                    continue;
                }

                if (views.Count >= Math.Max(1, cap))
                {
                    skipped.Add("The selection asks for more than "
                                + cap.ToString(CultureInfo.InvariantCulture) + " viewpoints.");
                    break;
                }

                var name = Name(how, number, element.Value);

                // Two viewpoints with one name are indistinguishable in every list the host shows,
                // and the host will happily take both. Disambiguated here rather than left to be
                // discovered by whoever opens the model next.
                if (!taken.Add(name))
                {
                    var attempt = 2;
                    string candidate;

                    do
                    {
                        candidate = name + " (" + attempt.ToString(CultureInfo.InvariantCulture) + ")";
                        attempt++;
                    }
                    while (!taken.Add(candidate));

                    name = candidate;
                }

                views.Add(new PlannedView(element.Key, name, number));
                number++;
            }

            return new ViewpointRun(views, skipped);
        }

        private static string Name(ViewpointRecipe how, int number, string? label)
        {
            var pattern = how.Pattern ?? string.Empty;
            if (pattern.Trim().Length == 0) pattern = "{n} {name}";

            var digits = how.Padding < 1 ? 1 : how.Padding > 9 ? 9 : how.Padding;

            var counted = number.ToString(
                new string('0', digits), CultureInfo.InvariantCulture);

            var named = label ?? string.Empty;
            if (named.Trim().Length == 0) named = "(unnamed)";

            var text = pattern.Replace("{n}", counted).Replace("{name}", named).Trim();

            return text.Length == 0 ? counted : text;
        }
    }
}
