using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// What counts as a change, and how hard to look.
    ///
    /// <b>Settings, not Options.</b> <c>System.Globalization.CompareOptions</c> exists and is
    /// imported by every file in this product that formats a number, so a Core type of that name
    /// compiles perfectly here and is an ambiguous reference the moment any screen uses it.
    ///
    /// Every field has a working default. The type exists so that the answers a comparison gives
    /// can be argued with — "why is this listed as moved" has to have an answer better than
    /// "the tool said so", and that answer is a tolerance somebody can see and change.
    /// </summary>
    public sealed class CompareSettings
    {
        /// <summary>The settings a first run uses, having been configured by nobody.</summary>
        public static CompareSettings Default { get; } = new CompareSettings();

        /// <summary>Which properties are recorded and compared.</summary>
        public PropertyWatch Watch { get; set; } = PropertyWatch.Standard;

        /// <summary>
        /// The properties to watch under <see cref="PropertyWatch.Named"/>.
        ///
        /// Ignored under the other modes, and kept rather than cleared when the mode changes, so
        /// that switching to Standard to see what it says and back again does not lose the list.
        /// </summary>
        public IReadOnlyList<PropertyRef> Watched
        {
            get => _watched;
            set => _watched = value == null ? Array.Empty<PropertyRef>() : value.ToArray();
        }

        private IReadOnlyList<PropertyRef> _watched = Array.Empty<PropertyRef>();

        /// <summary>
        /// Properties never compared, whatever the mode.
        ///
        /// Starts at <see cref="ComparePolicy.NoisyByDefault"/>. Adding to it is how somebody puts
        /// down a property their own workflow rewrites on every issue — a revision stamp, a
        /// check-in date — without turning property comparison off altogether.
        /// </summary>
        public IReadOnlyList<PropertyRef> Ignored
        {
            get => _ignored;
            set => _ignored = value == null ? Array.Empty<PropertyRef>() : value.ToArray();
        }

        private IReadOnlyList<PropertyRef> _ignored = ComparePolicy.NoisyByDefault;

        /// <summary>
        /// How far a centre may move before it is a move, in the document's length unit.
        ///
        /// See <see cref="ComparePolicy.DefaultTolerance"/> for why this is not zero.
        /// </summary>
        public double Tolerance { get; set; } = ComparePolicy.DefaultTolerance;

        /// <summary>How much an extent may change before it is a resize. Defaults to <see cref="Tolerance"/>.</summary>
        public double SizeTolerance
        {
            get => _sizeTolerance ?? Tolerance;
            set => _sizeTolerance = value;
        }

        private double? _sizeTolerance;

        /// <summary>
        /// The weakest identity a match may rest on.
        ///
        /// <see cref="KeyRung.Geometry"/> — the default — accepts all three, which is what makes a
        /// comparison work on the DWG- and IFC-sourced models that carry no usable instance GUID.
        /// Tightening it to <see cref="KeyRung.TreePath"/> trades matches for certainty: fewer
        /// elements pair up, and the ones that do are not two identical brackets swapped for each
        /// other. The result always reports how many matches rested on each rung.
        /// </summary>
        public KeyRung WeakestRung { get; set; } = KeyRung.Geometry;

        /// <summary>
        /// Whether unchanged elements are listed as well as counted.
        ///
        /// Off. On a real revision they are ninety-odd percent of the model, and a change list
        /// whose first two hundred rows all say "unchanged" hides the twelve rows that do not. The
        /// counts include them either way, so a short list is not the same as nothing happening.
        /// </summary>
        public bool KeepUnchanged { get; set; }

        /// <summary>
        /// Whether elements carrying no geometry take part.
        ///
        /// Off. A Revit NWC is full of grouping nodes — levels, categories, families — that exist
        /// to hold other things, and counting them as added or removed doubles every number in the
        /// report without naming a single thing anybody built.
        /// </summary>
        public bool IncludeGroupingNodes { get; set; }

        /// <summary>How many rows are kept per change kind for the screen.</summary>
        public int ExampleCap { get; set; } = ComparePolicy.DefaultExampleCap;

        /// <summary>
        /// Whether a recorded mesh hash takes part in deciding whether an element changed.
        ///
        /// Only meaningful when both sides actually carry one — see
        /// <see cref="ElementFingerprint.MeshHash"/> for why that is rare and deliberate. An
        /// element whose box is unchanged and whose mesh hash differs was remodelled inside its own
        /// envelope, which no other signal here can see.
        /// </summary>
        public bool UseMeshHash { get; set; } = true;

        /// <summary>True when this property is one the comparison records and compares.</summary>
        /// <param name="property">The property in question.</param>
        public bool Watches(PropertyRef property)
        {
            if (Watch == PropertyWatch.None) return false;
            if (ComparePolicy.Covers(_ignored, property)) return false;

            return Watch switch
            {
                PropertyWatch.All => true,
                PropertyWatch.Standard => ComparePolicy.Covers(ComparePolicy.StandardWatch, property),
                PropertyWatch.Named => ComparePolicy.Covers(_watched, property),
                _ => false,
            };
        }

        /// <summary>A copy, so a screen can offer "try it with…" without editing the live settings.</summary>
        public CompareSettings Copy() => new CompareSettings
        {
            Watch = Watch,
            Watched = _watched,
            Ignored = _ignored,
            Tolerance = Tolerance,
            _sizeTolerance = _sizeTolerance,
            WeakestRung = WeakestRung,
            KeepUnchanged = KeepUnchanged,
            IncludeGroupingNodes = IncludeGroupingNodes,
            ExampleCap = ExampleCap,
            UseMeshHash = UseMeshHash,
        };

        /// <summary>
        /// The settings in words, for the top of a report.
        ///
        /// A comparison is an argument, and an argument without its premises written down is one
        /// nobody can check. This line is what makes "312 moved" mean something six weeks later.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>
            {
                "moves over " + Tolerance.ToString("0.####", CultureInfo.InvariantCulture)
                    + " and resizes over " + SizeTolerance.ToString("0.####", CultureInfo.InvariantCulture)
                    + " model units",

                Watch switch
                {
                    PropertyWatch.None => "no properties compared",
                    PropertyWatch.All => "every property compared",
                    PropertyWatch.Named => _watched.Count == 1
                        ? "one named property compared"
                        : ComparePolicy.Count(_watched.Count) + " named properties compared",
                    _ => "the standard property list compared",
                },

                WeakestRung switch
                {
                    KeyRung.InstanceGuid => "matched on instance GUID only",
                    KeyRung.TreePath => "matched on GUID or tree path",
                    _ => "matched on GUID, tree path or shape",
                },
            };

            if (IncludeGroupingNodes) parts.Add("grouping nodes included");
            if (KeepUnchanged) parts.Add("unchanged elements listed");

            return string.Join("; ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }
}
