using System;
using System.Collections.Generic;

namespace CamelWorks.Core.Clash
{
    /// <summary>Which of a clash's two elements a value is read from.</summary>
    public enum ClashElementSide
    {
        /// <summary>Both, joined when they differ.</summary>
        Both = 0,

        /// <summary>The first element only.</summary>
        First = 1,

        /// <summary>The second element only.</summary>
        Second = 2,
    }

    /// <summary>One property a board wants read off the elements its results are between.</summary>
    public sealed class ClashPropertyWanted
    {
        /// <summary>Name it.</summary>
        /// <param name="id">What the caller keys the answer by.</param>
        /// <param name="tab">The property's category, or empty for any.</param>
        /// <param name="name">The property's name.</param>
        /// <param name="side">Which element to read it from.</param>
        public ClashPropertyWanted(string id, string tab, string name, ClashElementSide side)
        {
            Id = id ?? string.Empty;
            Tab = tab ?? string.Empty;
            Name = name ?? string.Empty;
            Side = side;
        }

        /// <summary>What the caller keys the answer by.</summary>
        public string Id { get; }

        /// <summary>The property's category, or empty for any.</summary>
        public string Tab { get; }

        /// <summary>The property's name.</summary>
        public string Name { get; }

        /// <summary>Which element to read it from.</summary>
        public ClashElementSide Side { get; }
    }

    /// <summary>What one result's elements answered, by the id the caller asked under.</summary>
    public sealed class ClashResultValues
    {
        /// <summary>Name it.</summary>
        /// <param name="resultId">Which result.</param>
        public ClashResultValues(string resultId) => ResultId = resultId ?? string.Empty;

        /// <summary>Which result.</summary>
        public string ResultId { get; }

        /// <summary>The values, by the id they were asked under. Absent means the element has none.</summary>
        public Dictionary<string, string> Values { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>The IFC GlobalIds of the two elements one result is between.</summary>
    public sealed class ClashResultIfc
    {
        /// <summary>Name it.</summary>
        /// <param name="resultId">Which result.</param>
        /// <param name="a">The first element's GlobalId, or null.</param>
        /// <param name="b">The second element's GlobalId, or null.</param>
        public ClashResultIfc(string resultId, string? a, string? b)
        {
            ResultId = resultId ?? string.Empty;
            GuidA = a;
            GuidB = b;
        }

        /// <summary>Which result.</summary>
        public string ResultId { get; }

        /// <summary>The first element's GlobalId, or null.</summary>
        public string? GuidA { get; }

        /// <summary>The second element's GlobalId, or null.</summary>
        public string? GuidB { get; }
    }

    /// <summary>One result's whole comment trail.</summary>
    public sealed class ClashResultComments
    {
        /// <summary>Name it.</summary>
        /// <param name="resultId">Which result.</param>
        public ClashResultComments(string resultId) => ResultId = resultId ?? string.Empty;

        /// <summary>Which result.</summary>
        public string ResultId { get; }

        /// <summary>The trail, oldest first.</summary>
        public List<ClashComment> Trail { get; } = new List<ClashComment>();
    }

    /// <summary>A property name as the host files it: which tab it appears under, and its name.</summary>
    public readonly struct ClashPropertyName
    {
        private readonly string? _tab;
        private readonly string? _name;

        /// <summary>Name it.</summary>
        /// <param name="tab">The category the host files it under.</param>
        /// <param name="name">The property's name.</param>
        public ClashPropertyName(string tab, string name)
        {
            _tab = tab ?? string.Empty;
            _name = name ?? string.Empty;
        }

        /// <summary>The category the host files it under.</summary>
        public string Tab => _tab ?? string.Empty;

        /// <summary>The property's name.</summary>
        public string Name => _name ?? string.Empty;
    }
}
