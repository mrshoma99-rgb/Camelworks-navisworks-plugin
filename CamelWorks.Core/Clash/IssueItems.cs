using System;
using System.Collections.Generic;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Hand-raised issues as board items, so they sit on the clash board beside the engine's
    /// results.
    ///
    /// Most of what goes wrong in a federation is not an intersection. A door swings into a
    /// corridor, a valve has nowhere to be pulled to, a riser has no builder's work opening — none
    /// of them are clashes, all of them are coordination, and a board that only holds what the
    /// clash engine said sends them to a spreadsheet beside it.
    ///
    /// The item is a projection and is never written back. An issue is the record; a board item
    /// carries less than the issue does, so writing one over the other way would lose the fields
    /// only the issue has.
    /// </summary>
    public static class IssueItems
    {
        /// <summary>What the test column says for something nobody's engine found.</summary>
        public const string RaisedByHand = "Raised by hand";

        /// <summary>
        /// The board items for every hand-raised issue that is still open.
        ///
        /// Closed ones are left out rather than deleted: the record of what was raised and closed is
        /// the evidence the week's work happened, and a board carrying every issue ever raised would
        /// be unusable within a month.
        ///
        /// An issue with fewer than two resolved elements is skipped. A board item is identified by
        /// a clash key, which names two participants and a place; there is no honest key for a
        /// sentence about one element, and inventing one would put a row on the board that the next
        /// run cannot match to itself.
        /// </summary>
        /// <param name="register">Every issue on this project.</param>
        public static IReadOnlyList<ClashItem> Open(IssueRegister? register)
        {
            var items = new List<ClashItem>();

            foreach (var issue in register?.Issues ?? Array.Empty<Issue>())
            {
                if (!issue.IsOpen) continue;
                if (!string.Equals(issue.Provenance, Issue.ByHand, StringComparison.Ordinal)) continue;
                if (issue.Elements.Count < 2) continue;

                var first = issue.Elements[0];
                var second = issue.Elements[1];

                if (first.IsEmpty || second.IsEmpty) continue;

                var key = ClashKey.Create(first, second, issue.X, issue.Y, issue.Z);

                // Its own group, always: an issue somebody wrote a sentence about is not one of
                // eleven rows under "Grid C-4". The title is the group, and a pinned group is never
                // re-derived by the rules, so it stays where it was put.
                items.Add(new ClashItem(key, RaisedByHand)
                {
                    Status = issue.Status,
                    CarriedGroup = issue.Title,
                    Level = Fact(issue, Issue.LevelFact),
                    X = issue.X,
                    Y = issue.Y,
                    Z = issue.Z,
                    ModelA = issue.Author,
                    CategoryA = "Raised",
                    CategoryB = issue.Priority,
                });
            }

            return items;
        }

        /// <summary>
        /// The typed instruction somebody put on an issue, or <see cref="Resolution.None"/>.
        /// </summary>
        /// <param name="issue">The issue.</param>
        public static Resolution Instruction(Issue? issue)
        {
            if (issue == null) return Resolution.None;
            if (!issue.Facts.TryGetValue(Issue.InstructionFact, out var written)) return Resolution.None;
            if (string.IsNullOrWhiteSpace(written)) return Resolution.None;

            return JsonReader.TryParse(written, out var json)
                ? Resolution.FromJson(json)
                : Resolution.None;
        }

        private static string? Fact(Issue issue, string key) =>
            issue.Facts.TryGetValue(key, out var value) ? value : null;
    }
}
