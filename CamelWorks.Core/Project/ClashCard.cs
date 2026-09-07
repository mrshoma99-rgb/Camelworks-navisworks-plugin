using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Search;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>What a card does to the results it found.</summary>
    public enum ClashActionKind
    {
        /// <summary>Take the matches off the board, recording which card did it.</summary>
        Suppress = 0,

        /// <summary>Leave them on the board and mark them, recording which card did it.</summary>
        Flag = 1,

        /// <summary>Put them in a named group.</summary>
        Group = 2,

        /// <summary>Assign them to a party.</summary>
        Assign = 3,

        /// <summary>Set their status.</summary>
        Status = 4,

        /// <summary>Delete the results from the Navisworks document.</summary>
        Remove = 5,
    }

    /// <summary>
    /// One card: what to find, and what to do to it.
    ///
    /// The rules used to be three lists — suppress and flag, then group, then assign — each with
    /// its own editor and its own place in a fixed order. That put the shape of the machinery on
    /// screen rather than the shape of the work: a coordinator does not think "I am writing a
    /// stage two rule", they think "the ones under a hundredth of a cubic metre go to Mechanical".
    ///
    /// So one list, in the order it runs, and each entry says both halves. Every action is one the
    /// Manage board already offers over a picked set of rows; a card is that verb over whatever
    /// the criteria find, run on a press rather than by hand.
    /// </summary>
    public sealed class ClashCard
    {
        /// <summary>Create a card.</summary>
        /// <param name="name">What it is called, on the board and in the list.</param>
        /// <param name="when">The search that finds them.</param>
        /// <param name="action">What it does to what it finds.</param>
        public ClashCard(string name, ResultQuery when, ClashActionKind action)
        {
            _name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("name is required", nameof(name))
                : name.Trim();

            _when = when ?? throw new ArgumentNullException(nameof(when));
            Action = action;
        }

        private string _name;
        private ResultQuery _when;

        /// <summary>What it is called.</summary>
        /// <remarks>
        /// Settable, with the three below: a rule is opened and edited in place on the screen that
        /// lists it, so the object the list holds is the one the editor writes to.
        /// </remarks>
        public string Name
        {
            get => _name;
            set => _name = string.IsNullOrWhiteSpace(value) ? _name : value.Trim();
        }

        /// <summary>The search that finds what it acts on.</summary>
        public ResultQuery When
        {
            get => _when;
            set => _when = value ?? _when;
        }

        /// <summary>What it does to what it finds.</summary>
        public ClashActionKind Action { get; set; }

        /// <summary>
        /// The group name, the party or the status word, by action.
        ///
        /// One field rather than three, because a card carries exactly one of them and three
        /// nullable fields on a record that can only ever fill one is a shape that invites filling
        /// two. Which one it means is <see cref="Action"/>.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>Off rules stay in the list, in their place, and do nothing.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// How many clauses were dropped reading this rule back, and nought for every rule this
        /// build wrote.
        ///
        /// A rule saved by a newer build can hold a clause this one cannot read. Dropping it
        /// quietly would widen what the rule finds, so the count is kept and the rule arrives
        /// turned off with the screen saying why.
        /// </summary>
        public int Lost { get; set; }

        /// <summary>True when the action needs a value and has none.</summary>
        public bool NeedsValue =>
            Needs(Action) && string.IsNullOrWhiteSpace(Value);

        /// <summary>Whether an action is one that has to be told a group, a party or a status.</summary>
        /// <param name="action">The action.</param>
        public static bool Needs(ClashActionKind action) =>
            action == ClashActionKind.Group
            || action == ClashActionKind.Assign
            || action == ClashActionKind.Status;

        /// <summary>Whether an action cannot be undone by turning the card off.</summary>
        /// <param name="action">The action.</param>
        public static bool IsFinal(ClashActionKind action) => action == ClashActionKind.Remove;

        /// <summary>What the action does, as the card's second line reads it.</summary>
        public string Does => Worded(Action, Value);

        /// <summary>One action, worded with whatever it was given.</summary>
        /// <param name="action">The action.</param>
        /// <param name="value">The group, the party or the status, where it takes one.</param>
        public static string Worded(ClashActionKind action, string? value)
        {
            var said = (value ?? string.Empty).Trim();

            switch (action)
            {
                case ClashActionKind.Suppress: return "Take off the board";
                case ClashActionKind.Flag: return "Flag on the board";
                case ClashActionKind.Group:
                    return said.Length == 0 ? "Group" : "Group as “" + said + "”";
                case ClashActionKind.Assign:
                    return said.Length == 0 ? "Assign" : "Assign to " + said;
                case ClashActionKind.Status:
                    return said.Length == 0 ? "Set the status" : "Set the status to " + said;
                case ClashActionKind.Remove: return "Delete from the document";
                default: return action.ToString();
            }
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("name", JsonValue.String(Name))
                .Set("action", JsonValue.String(Action.ToString()))
                .Set("enabled", JsonValue.String(IsEnabled ? "yes" : "no"))
                .Set("when", When.ToJson());

            if (!string.IsNullOrWhiteSpace(Value)) json.Set("value", JsonValue.String(Value!));

            return json;
        }

        /// <summary>Read one back, or null when the file does not hold one.</summary>
        /// <param name="json">The candidate.</param>
        public static ClashCard? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var name = json["name"].AsString();

            if (string.IsNullOrWhiteSpace(name)) return null;

            // A clause this build cannot read is dropped by the reader and counted. Widening a
            // rule silently is the one thing a search must not do, so a rule that lost one is
            // turned off rather than left to run over more than it was written for.
            var when = ResultQuery.FromJson(json["when"], out var lost);

            // An action this build does not know is not read as the first one in the enum: a card
            // saved by a newer build as "Remove" must not come back as "Suppress" and then be run.
            if (!Enum.TryParse<ClashActionKind>(json["action"].AsString(), out var action))
                return null;

            return new ClashCard(name!, when, action)
            {
                Value = json["value"].AsString(),
                IsEnabled = lost == 0 && json["enabled"].AsString() != "no",
                Lost = lost,
            };
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + ": " + When.Clauses + " clause(s) → " + Does;
    }

    /// <summary>What one card would do, or did.</summary>
    public sealed class ClashCardOutcome
    {
        /// <summary>Describe one.</summary>
        /// <param name="card">The card.</param>
        /// <param name="matched">How many results it found.</param>
        /// <param name="changed">How many of those the action would actually change.</param>
        /// <param name="problem">Why it could not be done, or null.</param>
        public ClashCardOutcome(ClashCard card, int matched, int changed, string? problem = null)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Matched = matched;
            Changed = changed;
            Problem = problem;
        }

        /// <summary>The card.</summary>
        public ClashCard Card { get; }

        /// <summary>How many results the criteria found.</summary>
        public int Matched { get; }

        /// <summary>
        /// How many of them the action would change.
        ///
        /// Lower than <see cref="Matched"/> whenever some of them already hold what the card sets:
        /// a card that assigns forty results to Mechanical, run twice, changes forty and then
        /// none, and a run that reports "40 found, 0 changed" is the honest way to say so.
        /// </summary>
        public int Changed { get; }

        /// <summary>Why nothing was done, or null.</summary>
        public string? Problem { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Card.Name + ": " + Matched + " found, " + Changed + " changed"
            + (Problem == null ? string.Empty : " (" + Problem + ")");
    }
}
