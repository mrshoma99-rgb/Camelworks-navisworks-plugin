using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Sign-off and fabrication release, and what has moved since.
    ///
    /// Two things a coordination model has to be able to record: that a level was signed off on a
    /// date, and that a set of components was released to fabrication on another. Both are the same
    /// act — putting a name to a state of the model — and both are worthless unless the tool can
    /// afterwards say what has changed since. So it is one mechanism, and the Change Notice is the
    /// half that makes it useful.
    ///
    /// It lives in Compare Track. It was previously a Clash Manager tab because the element list was
    /// harvested from the clash board. Nothing about a freeze is clash-shaped: it is a named
    /// baseline with a signature and a tamper hash, and the half that reads it back was already
    /// here. The scope is now anything the model can name, so a level can be signed off in a week
    /// when no clash test was run.
    ///
    /// The unfreeze is always visible. A mechanism that can only be entered is one people learn not
    /// to enter.
    /// </summary>
    public static class FreezeViews
    {
        private const string Selection = "The current selection";
        private const string Everything = "Everything loaded";
        private const string Board = "What is on the clash board";
        private const string ASet = "A set";

        private static string _scope = Selection;
        private static string _set = string.Empty;

        /// <summary>The freeze this project holds, or null.</summary>
        internal static Freeze? Frozen(Session session) =>
            Freeze.FromJson(session.Store.Section(ProjectStore.ClashSection)[ClashKeys.Freeze]);

        /// <summary>
        /// What is frozen right now, and the way back off it.
        ///
        /// Both screens lead with this: freezing on top of a freeze, or writing a notice against
        /// one that has been edited under you, are the two ways this record misleads somebody.
        /// </summary>
        private static Panel FrozenState()
        {
            var state = Ui.Stack();
            var session = Host.Current;

            if (session == null) return state;

            var held = Frozen(session);
            if (held == null) return state;

            state.Children.Add(Ui.Line(held.Describe(), 1, true));

            if (!held.IsIntact)
                state.Children.Add(Ui.Problem(
                    "That record no longer hashes to its own contents. It has been edited in the "
                    + "project file, by hand or by a merge, and what it says was agreed is no "
                    + "longer what it says it was."));

            state.Children.Add(Ui.Button("Unfreeze", () =>
            {
                state.Children.Clear();

                session.Store.Section(ProjectStore.ClashSection).Remove(ClashKeys.Freeze);

                state.Children.Add(session.Store.Save()
                    ? Ui.Done("Unfrozen. Nothing in the model changed - only the record that it "
                              + "had been agreed.")
                    : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                session.Record(ActivityKind.Regroup, "unfroze " + held.Scope);
            }));

            return state;
        }

        /// <summary>Freeze: record where every element in the scope is, right now.</summary>
        public static UIElement FreezeScreen()
        {
            var results = Ui.Stack();

            var scope = Ui.Text(string.Empty, "Level 04 services", null, 220);
            var signatory = Ui.Text(string.Empty, "who is putting their name to it", null, 180);
            var release = Ui.Text(string.Empty, "a works order, a drawing number", null, 140);

            results.Children.Add(Bench.Ready(
                "Records the current position of every element in scope, against a named author.",
                "The record cannot be edited afterwards. Change Notice reports what has "
                + "moved since."));

            var choices = new List<string> { Selection, ASet, Everything };

            // Offered only when there is one. A picker naming something that does not exist is a
            // press that fails, and this screen is about not being surprised later.
            if (CoordinateViews.LastBoard != null) choices.Add(Board);

            if (!choices.Contains(_scope)) _scope = Selection;

            var setPicker = Pickers.Set(_set, pick => _set = pick, 220);

            return Bench.Surface(
                Ui.Stack(
                    Bench.Group("What is being frozen",
                        Ui.Field("Scope", Ui.Choice(choices, _scope, pick => _scope = pick, 220)),
                        Ui.Field("Set", setPicker, "used when the scope is a set"),
                        Ui.Field("Call it", scope, "how the record will read"),
                        Ui.Field("Signed by", signatory),
                        Ui.Field("Release reference", release, "for a fabrication release")),
                    Bench.Group(string.Empty,
                        Ui.Small("A freeze is an authored baseline. It records each element's "
                                 + "bounding box and hashes the set, so later edits to the record "
                                 + "are detectable."))),
                Ui.Runner("Freeze", results,
                          job => Do(results, scope, signatory, release, job)),
                Ui.Stack(FrozenState(), results));
        }

        /// <summary>
        /// The change notice: the difference between what was agreed and what the model says now.
        ///
        /// The thing that stops a hanger being made to a drawing three weeks old.
        /// </summary>
        public static UIElement NoticeScreen()
        {
            var results = Ui.Stack();

            results.Children.Add(Bench.Ready(
                "Reports what has moved since the freeze was authored.",
                "Measured against the freeze, not against last week."));

            return Bench.Surface(
                null,
                Ui.Runner("Write the change notice", results, job => Notice(results, job)),
                Ui.Stack(FrozenState(), results));
        }

        /// <summary>Record where everything in the scope is, right now.</summary>
        private static void Do(Panel results, TextBox called, TextBox signatory, TextBox release,
                               Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var metres = session.MetresPerUnit;
            var elements = new List<FrozenElement>();
            var groups = new List<string>();
            var missing = 0;
            var read = 0;

            void Record(ElementKey key)
            {
                var element = session.Model.Resolve(key);

                if (element == null) { missing++; return; }

                var b = element.Bounds;

                elements.Add(new FrozenElement(key, new BoundingBox(
                    b.MinX * metres, b.MinY * metres, b.MinZ * metres,
                    b.MaxX * metres, b.MaxY * metres, b.MaxZ * metres)));
            }

            if (_scope == Board)
            {
                var board = CoordinateViews.LastBoard;

                if (board == null)
                {
                    results.Children.Add(CoordinateViews.LoadHere("The board is not loaded, and "
                        + "this scope freezes what is on it."));
                    return;
                }

                foreach (var group in board.Groups)
                {
                    groups.Add(group.Name);

                    foreach (var item in group.Items)
                        foreach (var key in new[] { item.Key.A, item.Key.B })
                        {
                            if (!job.Step(read++, "elements read")) return;
                            Record(key);
                        }
                }
            }
            else
            {
                if (_scope == ASet && _set.Trim().Length == 0)
                {
                    results.Children.Add(Ui.Problem("Pick the set to freeze first."));
                    return;
                }

                var walk = _scope == Selection ? TraversalScope.CurrentSelection
                         : _scope == ASet ? TraversalScope.Set(_set.Trim())
                         : TraversalScope.WholeDocument;

                foreach (var item in session.Model.Traverse(walk))
                {
                    if (!job.Step(read++, "elements read")) return;
                    if (!item.HasGeometry) continue;

                    var b = item.Bounds;

                    elements.Add(new FrozenElement(item.Key, new BoundingBox(
                        b.MinX * metres, b.MinY * metres, b.MinZ * metres,
                        b.MaxX * metres, b.MaxY * metres, b.MaxZ * metres)));
                }
            }

            if (elements.Count == 0)
            {
                results.Children.Add(Ui.Empty("Nothing to freeze.",
                    _scope == Selection
                        ? "Nothing is selected in the host."
                        : _scope == ASet
                            ? "\"" + _set + "\" holds nothing this document can resolve."
                            : "Nothing loaded has geometry."));
                return;
            }

            var name = called.Text.Trim().Length > 0
                ? called.Text.Trim()
                : _scope == ASet ? _set.Trim()
                : _scope == Board ? (CoordinateViews.LastBoardLabel ?? "the clash board")
                : _scope;

            var record = Freeze.Of(name, Environment.UserName, DateTimeOffset.UtcNow, groups,
                                   elements, signatory.Text, release.Text);

            session.Store.Section(ProjectStore.ClashSection).Set(ClashKeys.Freeze, record.ToJson());

            results.Children.Add(session.Store.Save()
                ? Ui.Line(record.Describe(), 1, true)
                : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

            if (missing > 0)
                results.Children.Add(Ui.Note(Ui.Count(missing, "element")
                    + " could not be found in the model and are not in the record. They cannot be "
                    + "reported on later either, which is worth knowing now."));

            session.Record(ActivityKind.Regroup, record.Describe());

            // A freeze exists so a later argument has a fixed point; the argument itself is the
            // change notice, which is the next tab.
            results.Children.Add(Ui.Quiet("Write the change notice",
                () => Ui.Told(Shell.AppWindow.Show("track", "notice"))));
        }

        /// <summary>What has moved since somebody put their name to it.</summary>
        private static void Notice(Panel results, Job job)
        {
            var session = Host.Current;
            if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

            var held = Frozen(session);

            if (held == null)
            {
                results.Children.Add(Ui.Empty("Nothing is frozen.",
                    "A change notice compares an agreed record against the model as it stands, "
                    + "so a freeze is required first."));

                results.Children.Add(Ui.Actions(Ui.Quiet("Freeze something",
                    () => Ui.Told(Shell.AppWindow.Show("track", "freeze")))));
                return;
            }

            var units = session.Profile.Units;
            var metres = session.MetresPerUnit;

            var now = new Dictionary<string, BoundingBox>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            var read = 0;

            foreach (var frozen in held.Elements)
            {
                if (!job.Step(read++, "elements checked")) return;

                var element = session.Model.Resolve(frozen.Key);
                if (element == null) continue;

                var b = element.Bounds;

                now[frozen.Key.ToString()] = new BoundingBox(
                    b.MinX * metres, b.MinY * metres, b.MinZ * metres,
                    b.MaxX * metres, b.MaxY * metres, b.MaxZ * metres);

                names[frozen.Key.ToString()] = element.DisplayName;
            }

            var notice = held.Since(now);

            results.Children.Add(Ui.Line(notice.Explain(units), 1, true));

            if (notice.IsClear) return;

            var rows = notice.Changes.Take(400).Select(c => new TableRow(c,
                names.TryGetValue(c.Was.Key.ToString(), out var name) ? name : c.Was.Key.ToString(),
                c.Describe(units),
                c.Moved > 0 ? units.Bare(c.Moved) : string.Empty,
                c.Resized > 0 ? units.Bare(c.Resized) : string.Empty)
            {
                Tone = c.IsMissing ? Tone.Bad : Tone.Warn,
            }).ToList();

            var table = Ui.Table(rows,
                ("Element", 240), ("What changed", 200),
                ("Moved (" + units.LengthUnit + ")", 110),
                ("Resized (" + units.LengthUnit + ")", 0));

            results.Children.Add(Ui.Picking(table,
                new Ui.OnPicked("Focus {n}", (picked, say) =>
                {
                    say.Children.Clear();

                    var keys = picked.OfType<FrozenChange>()
                                     .Select(c => c.Was.Key)
                                     .Where(k => session.Model.Resolve(k) != null)
                                     .ToList();

                    if (keys.Count == 0)
                    {
                        say.Children.Add(Ui.Note("Those elements are not in the model any more - "
                            + "which is itself what the notice is reporting."));
                        return;
                    }

                    session.Model.Select(keys);
                    session.View.ZoomTo(keys, 2.0);

                    say.Children.Add(Ui.Done("Selected " + Ui.Count(keys.Count, "element") + "."));
                }, primary: true),
                new Ui.OnPicked("Copy {n}",
                    (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "row"))));

            session.Record(ActivityKind.Report, "produced a change notice: " + notice.Explain(units));
        }
    }
}
