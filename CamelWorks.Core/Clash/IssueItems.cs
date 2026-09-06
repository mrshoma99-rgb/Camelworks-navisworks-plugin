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
    /// only the issue has. The one thing that does travel the other way is the board key, which is
    /// derived once and kept on the issue so it cannot move: see <see cref="KeyFor"/>.
    /// </summary>
    public static class IssueItems
    {
        /// <summary>What the test column says for something nobody's engine found.</summary>
        public const string RaisedByHand = "Raised by hand";

        /// <summary>
        /// The board items for every hand-raised issue that is still open.
        ///
        /// Open is the lattice's <see cref="Issue.IsOpen"/>, so Approved comes off the board as well
        /// as Resolved. That is deliberate: Approved means somebody has signed the issue off, the
        /// board is the list of what is still to settle, and an issue nobody has anything left to do
        /// about is a record rather than a row. Both are kept in the register — the record of what
        /// was raised and settled is the evidence the week's work happened — and a board carrying
        /// every issue ever raised would be unusable within a month.
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

                var key = KeyFor(issue);

                if (key.IsEmpty) continue;

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
        /// This issue's board identity, derived once and then read back off the issue.
        ///
        /// A clash key quantises the point into a cell, so deriving it on every board build would
        /// move it whenever somebody nudged an element or edited the issue's coordinates: the board
        /// would show the issue resolved and then new, and the group decision, the pinned group and
        /// the per-key assignment held against the old key would be orphaned. So the first
        /// derivation is written to <see cref="Issue.KeyFact"/> and every later build reads it.
        ///
        /// The write is why this is not a pure projection. It is confined to the one fact, it is
        /// idempotent, and until the project file is next saved the derivation repeats to the same
        /// value, because nothing between two builds changes what it is derived from.
        ///
        /// Empty when the issue names fewer than two resolved elements: there is no honest key for
        /// a sentence about one element.
        /// </summary>
        /// <param name="issue">The issue.</param>
        public static ClashKey KeyFor(Issue? issue)
        {
            if (issue == null) return default;

            if (issue.Facts.TryGetValue(Issue.KeyFact, out var written)
                && ClashKey.TryParse(written, out var held)) return held;

            if (issue.Elements.Count < 2) return default;

            var first = issue.Elements[0];
            var second = issue.Elements[1];

            if (first.IsEmpty || second.IsEmpty) return default;

            var key = ClashKey.Create(first, second, issue.X, issue.Y, issue.Z);

            issue.Facts[Issue.KeyFact] = key.ToString();

            return key;
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
