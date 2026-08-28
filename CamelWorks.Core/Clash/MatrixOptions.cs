using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{

    /// <summary>How the grid is turned into tests.</summary>
    public sealed class MatrixOptions
    {
        /// <summary>The default tolerance, in metres. A centimetre, the usual first pass.</summary>
        public const double DefaultTolerance = 0.01;

        /// <summary>How many tests a single build will produce before it stops.</summary>
        public const int DefaultCap = 400;

        /// <summary>Tolerance for every test, in metres.</summary>
        public double Tolerance { get; set; } = DefaultTolerance;

        /// <summary>
        /// Whether a side is paired with itself.
        ///
        /// Off by default. A model against itself is a legitimate test — it finds the pipe running
        /// through its own hanger — but it is not what anybody means by a discipline matrix, and
        /// turning it on by default makes the diagonal of every grid a test nobody asked for.
        /// </summary>
        public bool SelfPairs { get; set; }

        /// <summary>Name template. <c>{a}</c>, <c>{b}</c> and <c>{n}</c> are replaced.</summary>
        public string NamePattern { get; set; } = "{a} vs {b}";

        /// <summary>Test names the document already has, so the builder does not propose a duplicate.</summary>
        public IReadOnlyCollection<string>? Existing { get; set; }

        /// <summary>How many tests to produce before stopping.</summary>
        public int Cap { get; set; } = DefaultCap;
    }
}
