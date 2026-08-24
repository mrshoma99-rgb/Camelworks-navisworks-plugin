using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// What a tool was last told, so it does not ask again.
    ///
    /// Most of what "usable on day two" means. A tool that starts empty every morning is a tool
    /// somebody retypes a path into every morning - the workbook, the column numbers, the
    /// clearance, the naming pattern - and after the third time they stop using the tool and go
    /// back to doing it by hand.
    ///
    /// <b>In the project file, not in the user's profile.</b> Which is the difference between a
    /// habit and a setting: the column numbers for this job's schedule are about this job, and the
    /// next person to open the federation should find them already filled in. A path remembered
    /// per machine helps one person; a path remembered per project helps the team.
    ///
    /// <b>Nothing here is required.</b> Every read takes a fallback, and a project that has never
    /// used a tool gives back exactly the fallback - so a tool works the first time, before there
    /// is anything to remember.
    /// </summary>
    public sealed class ToolMemory
    {
        private readonly JsonValue _bag;

        private ToolMemory(ProjectStore? store, string tool, JsonValue bag)
        {
            Store = store; Tool = tool; _bag = bag;
        }

        /// <summary>Which tool this is the memory of.</summary>
        public string Tool { get; }

        /// <summary>The project it belongs to, or null for a memory that goes nowhere.</summary>
        public ProjectStore? Store { get; }

        /// <summary>How many things it holds.</summary>
        public int Count => _bag.Keys.Count;

        /// <summary>True when this tool has never been used in this project.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// The memory of one tool.
        ///
        /// A tool with no name shares the untitled drawer with every other one, which is the
        /// failure a name exists to prevent, so it is refused.
        /// </summary>
        /// <param name="store">The project. Null gives a memory that reads its fallbacks and keeps nothing.</param>
        /// <param name="tool">What the tool is called. Whatever it is, it must be the same every time.</param>
        public static ToolMemory Of(ProjectStore? store, string tool)
        {
            var named = (tool ?? string.Empty).Trim();

            if (named.Length == 0)
                throw new ArgumentException("a tool memory needs a tool name", nameof(tool));

            if (store == null) return new ToolMemory(null, named, JsonValue.Object());

            var section = store.Section(ProjectStore.ToolsSection);
            var bag = section[named];

            if (bag.Kind != JsonKind.Object)
            {
                bag = JsonValue.Object();
                section.Set(named, bag);
            }

            return new ToolMemory(store, named, bag);
        }

        /// <summary>What was typed here last time, or the fallback.</summary>
        /// <param name="key">Which box.</param>
        /// <param name="fallback">What to say when nothing was.</param>
        public string? Text(string key, string? fallback = null)
        {
            var value = _bag[Clean(key)].AsString();

            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>What was chosen here last time, or the fallback.</summary>
        /// <param name="key">Which box.</param>
        /// <param name="fallback">What to say when nothing was.</param>
        public int Number(string key, int fallback = 0)
        {
            var at = _bag[Clean(key)];

            return at.Kind == JsonKind.Number ? (int)at.AsLong(fallback) : fallback;
        }

        /// <summary>What was ticked here last time, or the fallback.</summary>
        /// <param name="key">Which box.</param>
        /// <param name="fallback">What to say when nothing was.</param>
        public bool Flag(string key, bool fallback = false)
        {
            var at = _bag[Clean(key)];

            return at.Kind == JsonKind.Bool ? at.AsBool(fallback) : fallback;
        }

        /// <summary>
        /// Remember a piece of text. An empty one is forgotten rather than stored.
        ///
        /// Because a box somebody cleared is a box they want empty next time, and a stored empty
        /// string is indistinguishable from never having used the tool - which is the one case the
        /// fallback exists for.
        /// </summary>
        /// <param name="key">Which box.</param>
        /// <param name="value">What is in it.</param>
        public ToolMemory Set(string key, string? value)
        {
            var named = Clean(key);
            var text = (value ?? string.Empty).Trim();

            if (text.Length == 0) _bag.Remove(named);
            else _bag.Set(named, text);

            return this;
        }

        /// <summary>Remember a number.</summary>
        /// <param name="key">Which box.</param>
        /// <param name="value">What is in it.</param>
        public ToolMemory Set(string key, int value)
        {
            _bag.Set(Clean(key), value);
            return this;
        }

        /// <summary>Remember a tick.</summary>
        /// <param name="key">Which box.</param>
        /// <param name="value">Whether it is ticked.</param>
        public ToolMemory Set(string key, bool value)
        {
            _bag.Set(Clean(key), value);
            return this;
        }

        /// <summary>Forget one thing.</summary>
        /// <param name="key">Which box.</param>
        public ToolMemory Forget(string key)
        {
            _bag.Remove(Clean(key));
            return this;
        }

        /// <summary>Forget everything this tool remembers.</summary>
        public ToolMemory Clear()
        {
            foreach (var key in _bag.Keys.ToList()) _bag.Remove(key);

            return this;
        }

        /// <summary>
        /// Write it to the project file.
        ///
        /// Returns false when there was nowhere to write, which is an ordinary outcome - a
        /// document nobody has saved has no project file yet - and never a reason to stop.
        /// </summary>
        public bool Keep() => Store != null && Store.Save();

        /// <summary>Everything it holds, for a panel that wants to show or clear it.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Everything() =>
            _bag.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => new KeyValuePair<string, string>(k, Describe(_bag[k])))
                .ToList();

        private static string Describe(JsonValue value) => value.Kind switch
        {
            JsonKind.Bool => value.AsBool() ? "yes" : "no",
            JsonKind.Number => value.AsDouble().ToString("0.######", CultureInfo.InvariantCulture),
            JsonKind.String => value.AsString() ?? string.Empty,
            _ => string.Empty,
        };

        private static string Clean(string key)
        {
            var named = (key ?? string.Empty).Trim();

            return named.Length == 0
                ? throw new ArgumentException("a remembered value needs a name", nameof(key))
                : named;
        }

        /// <inheritdoc />
        public override string ToString() =>
            IsEmpty
                ? Tool + " remembers nothing yet"
                : Tool + " remembers " + Count.ToString(CultureInfo.InvariantCulture)
                  + (Count == 1 ? " thing" : " things");
    }
}
