using System;
using System.Linq;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What the assemblies are allowed to know about each other.
    ///
    /// Its own file rather than the tail of the hashing tests: the two have nothing to do with
    /// each other, and a rule about the shape of the product is one somebody should be able to
    /// find by its name.
    /// </summary>
    public class ArchitectureTests
    {
        /// <summary>
        /// The seam, asserted rather than hoped for. CamelWorks.Core must stay free of Autodesk
        /// references: it is what lets Core run on the Linux CI job, and what stops host-specific
        /// behaviour leaking into logic that four host years have to share.
        /// </summary>
        [Fact]
        public void Core_references_no_Autodesk_assembly()
        {
            var core = typeof(ElementKey).Assembly;

            var offenders = core.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => n.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)
                         || n.IndexOf("Navisworks", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.True(offenders.Count == 0,
                "CamelWorks.Core must not reference Autodesk assemblies, but references: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// Core must also stay free of third-party runtime packages — the project rule that keeps
        /// the bundle dependency-free, and the reason EPPlus (Polyform Noncommercial) can never
        /// ship in a product advertised as free for commercial use.
        /// </summary>
        [Fact]
        public void Core_references_only_the_framework()
        {
            var allowedPrefixes = new[] { "System", "netstandard", "mscorlib", "Microsoft.Win32" };

            var offenders = typeof(ElementKey).Assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => !allowedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
                .ToList();

            Assert.True(offenders.Count == 0,
                "CamelWorks.Core must depend only on the framework, but references: "
                + string.Join(", ", offenders));
        }
    }
}
