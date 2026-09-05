using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// Works out what changed between two snapshots.
    ///
    /// The whole of the matching problem is here, in three parts:
    ///
    /// 1. Which model is which — <see cref="ModelPairing"/>, done first, because an element key is
    ///    scoped to its model's path and nothing can match across two files until those two files
    ///    have been declared two revisions of one thing.
    /// 2. Which element is which — rung by rung, strongest first, inside a pair. An element matched
    ///    on its instance GUID is never re-offered to the weaker rungs.
    /// 3. What is different about it — position, size, orientation, name, type, properties, and,
    ///    where a mesh signature was taken, shape.
    ///
    /// Each of the three can be wrong, and each records how it decided, so a row carries its own
    /// evidence.
    /// </summary>
    public static class ModelCompare
    {
        /// <summary>
        /// The largest group of indistinguishable elements paired by nearest neighbour.
        ///
        /// Rung 3 keys on category, type and size, so four hundred identical brackets share one.
        /// Pairing them by nearest neighbour is what turns "400 removed, 400 added" into "3 moved",
        /// and it costs the square of the group size — bounded here, because a model with nine
        /// thousand identical bolts would otherwise spend a minute deciding which bolt is which
        /// when nobody can tell anyway.
        /// </summary>
        public const int NeighbourCap = 64;

        /// <summary>Compare two snapshots.</summary>
        /// <param name="before">The baseline side.</param>
        /// <param name="after">The current side.</param>
        /// <param name="options">What counts as a change. Defaults to <see cref="CompareSettings.Default"/>.</param>
        public static CompareResult Of(ModelSnapshot? before, ModelSnapshot? after,
                                       CompareSettings? options = null)
        {
            var settings = options ?? CompareSettings.Default;
            var baseline = before ?? new ModelSnapshot();
            var current = after ?? new ModelSnapshot();
            var terms = baseline.Terms();

            var pairing = ModelPairing.Of(baseline.Models, current.Models);
            var notes = new List<string>();
            var tallies = new List<ModelTally>();

            var skipped = 0;
            var left = Bucket(baseline, settings, ref skipped);
            var right = Bucket(current, settings, ref skipped);

            var run = new Run(settings, terms);

            foreach (var pair in pairing.Pairs)
            {
                var tally = new ModelTally(pair.Title, pair.Strength, pair.Reason);
                tallies.Add(tally);

                if (pair.IsMatched)
                {
                    var mine = Take(left, pair.Before!.Scope);
                    var theirs = Take(right, pair.After!.Scope);
                    Pair(run, mine, theirs, tally);
                }
                else if (pair.IsNewModel)
                {
                    foreach (var print in Take(right, pair.After!.Scope)) run.Added(print, tally);
                }
                else
                {
                    foreach (var print in Take(left, pair.Before!.Scope)) run.Removed(print, tally);
                }
            }

            // Elements whose model was never listed in the snapshot's own model table. A snapshot
            // should not have them; one that has been hand-edited, or written by a build with a
            // different idea of scope, will. Reporting them as added and removed is wrong but
            // visible; dropping them silently loses elements out of a total nobody can reconcile.
            Orphans(run, left, right, tallies, notes);

            var result = run.Finish(pairing, tallies, notes);

            result.BaselineLabel = baseline.Label;
            result.BaselineTakenUtc = baseline.TakenUtc;
            result.CurrentLabel = current.Label;
            result.CurrentTakenUtc = current.TakenUtc;
            result.Skipped = skipped;

            // Assigned rather than appended to. Several of these notes are about numbers the run
            // only knows once it has finished — how many matches were weak, how many rows the cap
            // kept out — so the list has to grow after the result exists, and handing the result a
            // list somebody then mutates is the kind of aliasing that survives every review and
            // breaks the day a note is added in a second place.
            Notes(notes, result, baseline, current, settings, terms, pairing);
            result.Notes = notes.ToArray();

            return result;
        }

        /// <summary>
        /// The current-side elements a triangle read would report something about.
        ///
        /// Every other element is time spent for no result. A shape signature says something only
        /// when both revisions have one, so an element whose baseline counterpart was never measured
        /// cannot be reported as remodelled. Neither can one whose bounding box already differs: it
        /// is on the change list already and measuring it changes no answer.
        ///
        /// Worked out from the two snapshots directly rather than from a first comparison, because
        /// the elements this is about are the ones a comparison reports as unchanged, which are
        /// exactly the rows a comparison does not keep.
        /// </summary>
        /// <param name="baseline">The baseline side.</param>
        /// <param name="current">The snapshot about to be measured.</param>
        /// <param name="settings">Used for the size tolerance. Defaults to <see cref="CompareSettings.Default"/>.</param>
        /// <returns>The scoped key of each element worth measuring.</returns>
        public static IReadOnlyCollection<string> WorthDeepening(ModelSnapshot? baseline,
                                                                 ModelSnapshot? current,
                                                                 CompareSettings? settings = null)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            if (baseline == null || current == null) return wanted;

            var terms = settings ?? CompareSettings.Default;
            var measured = new Dictionary<string, ElementFingerprint>(StringComparer.Ordinal);

            foreach (var print in baseline.Elements)
            {
                if (print.MeshHash == null || print.Local.Length == 0) continue;
                if (!measured.ContainsKey(print.Local)) measured[print.Local] = print;
            }

            if (measured.Count == 0) return wanted;

            foreach (var print in current.Elements)
            {
                if (print.MeshHash != null) continue;
                if (!print.HasGeometry) continue;
                if (!measured.TryGetValue(print.Local, out var was)) continue;

                var mine = was.SortedExtents();
                var theirs = print.SortedExtents();

                var resized = Math.Abs(mine[0] - theirs[0]) > terms.SizeTolerance
                              || Math.Abs(mine[1] - theirs[1]) > terms.SizeTolerance
                              || Math.Abs(mine[2] - theirs[2]) > terms.SizeTolerance;

                if (!resized) wanted.Add(print.Scoped);
            }

            return wanted;
        }

        // ---- setting the two sides up -------------------------------------------------------

        private static Dictionary<string, List<ElementFingerprint>> Bucket(
            ModelSnapshot snapshot, CompareSettings settings, ref int skipped)
        {
            var by = new Dictionary<string, List<ElementFingerprint>>(StringComparer.Ordinal);

            foreach (var print in snapshot.Elements)
            {
                if (!settings.IncludeGroupingNodes && !print.HasGeometry) { skipped++; continue; }
                if (print.Rung > settings.WeakestRung) { skipped++; continue; }

                if (!by.TryGetValue(print.ModelScope, out var list))
                    by[print.ModelScope] = list = new List<ElementFingerprint>();

                list.Add(print);
            }

            return by;
        }

        private static List<ElementFingerprint> Take(
            Dictionary<string, List<ElementFingerprint>> by, string scope)
        {
            if (!by.TryGetValue(scope, out var list)) return new List<ElementFingerprint>();

            by.Remove(scope);
            return list;
        }

        private static void Orphans(Run run,
                                    Dictionary<string, List<ElementFingerprint>> left,
                                    Dictionary<string, List<ElementFingerprint>> right,
                                    List<ModelTally> tallies, List<string> notes)
        {
            if (left.Count == 0 && right.Count == 0) return;

            var tally = new ModelTally("(elements with no model record)", PairStrength.None,
                                       "the snapshot did not say which model these came from");
            tallies.Add(tally);

            var stray = 0;

            foreach (var list in right.Values) { foreach (var p in list) { run.Added(p, tally); stray++; } }
            foreach (var list in left.Values) { foreach (var p in list) { run.Removed(p, tally); stray++; } }

            notes.Add(ComparePolicy.Count(stray) + " elements name a model the snapshot does not "
                      + "list, so nothing could pair them. They are counted as added or removed, "
                      + "which is almost certainly not what happened to them.");
        }

        // ---- matching within one pair of models ---------------------------------------------

        private static void Pair(Run run, List<ElementFingerprint> lefts, List<ElementFingerprint> rights,
                                 ModelTally tally)
        {
            var usedLeft = new bool[lefts.Count];
            var usedRight = new bool[rights.Count];

            // Strongest first, and a match is never re-offered. An element that paired on its
            // instance GUID must not also be a candidate for the geometry rung, or one element
            // ends up standing in for two.
            for (var rung = KeyRung.InstanceGuid; rung <= run.Settings.WeakestRung; rung++)
                Rung(run, lefts, rights, usedLeft, usedRight, tally, rung);

            // The fallback that keeps a comparison working across a change of exporter: an element
            // that carried an instance GUID last time and does not this time has no rung in common
            // with itself, and would otherwise be reported as removed and added in the same spot.
            if (run.Settings.WeakestRung >= KeyRung.Geometry)
                Fallback(run, lefts, rights, usedLeft, usedRight, tally);

            for (var i = 0; i < rights.Count; i++)
                if (!usedRight[i]) run.Added(rights[i], tally);

            for (var i = 0; i < lefts.Count; i++)
                if (!usedLeft[i]) run.Removed(lefts[i], tally);
        }

        private static void Rung(Run run, List<ElementFingerprint> lefts, List<ElementFingerprint> rights,
                                 bool[] usedLeft, bool[] usedRight, ModelTally tally, KeyRung rung)
        {
            var mine = Index(lefts, usedLeft, p => p.Rung == rung ? p.Local : null);
            if (mine.Count == 0) return;

            var theirs = Index(rights, usedRight, p => p.Rung == rung ? p.Local : null);
            if (theirs.Count == 0) return;

            Join(run, lefts, rights, usedLeft, usedRight, tally, mine, theirs, rung);
        }

        private static void Fallback(Run run, List<ElementFingerprint> lefts, List<ElementFingerprint> rights,
                                     bool[] usedLeft, bool[] usedRight, ModelTally tally)
        {
            var mine = Index(lefts, usedLeft, NamedShape);
            if (mine.Count == 0) return;

            var theirs = Index(rights, usedRight, NamedShape);
            if (theirs.Count == 0) return;

            Join(run, lefts, rights, usedLeft, usedRight, tally, mine, theirs, KeyRung.Geometry);
        }

        private static void Join(Run run, List<ElementFingerprint> lefts, List<ElementFingerprint> rights,
                                 bool[] usedLeft, bool[] usedRight, ModelTally tally,
                                 Dictionary<string, List<int>> mine, Dictionary<string, List<int>> theirs,
                                 KeyRung rung)
        {
            foreach (var entry in mine.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (!theirs.TryGetValue(entry.Key, out var others)) continue;

                foreach (var (i, j) in Nearest(lefts, rights, entry.Value, others))
                {
                    usedLeft[i] = true;
                    usedRight[j] = true;
                    run.Matched(lefts[i], rights[j], rung, tally);
                }
            }
        }

        private static Dictionary<string, List<int>> Index(
            IReadOnlyList<ElementFingerprint> prints, bool[] used, Func<ElementFingerprint, string?> keyOf)
        {
            var by = new Dictionary<string, List<int>>(StringComparer.Ordinal);

            for (var i = 0; i < prints.Count; i++)
            {
                if (used[i]) continue;

                var key = keyOf(prints[i]);
                if (string.IsNullOrEmpty(key)) continue;

                if (!by.TryGetValue(key!, out var list)) by[key!] = list = new List<int>();
                list.Add(i);
            }

            return by;
        }

        /// <summary>
        /// Which of these elements is which, when their identities cannot tell them apart.
        ///
        /// One on each side is the ordinary case and settles itself. Beyond that the only signal
        /// left is where they are, so the closest pairs are taken first — which is what makes a
        /// hanger that shifted 40 mm read as one moved hanger rather than as a deletion and an
        /// unrelated addition four rows apart in the report.
        /// </summary>
        private static IEnumerable<(int Left, int Right)> Nearest(
            IReadOnlyList<ElementFingerprint> lefts, IReadOnlyList<ElementFingerprint> rights,
            List<int> mine, List<int> theirs)
        {
            if (mine.Count == 1 && theirs.Count == 1)
            {
                yield return (mine[0], theirs[0]);
                yield break;
            }

            if (mine.Count > NeighbourCap || theirs.Count > NeighbourCap)
            {
                // Past the cap, order by position and zip. Arbitrary, and deliberately so: these
                // elements are identical in category, type and size, so any pairing of them is as
                // true as any other, and a stable one at least reports the same thing twice.
                var byPlace = mine.OrderBy(i => lefts[i].CentreX).ThenBy(i => lefts[i].CentreY)
                                  .ThenBy(i => lefts[i].CentreZ).ToList();
                var alsoByPlace = theirs.OrderBy(j => rights[j].CentreX).ThenBy(j => rights[j].CentreY)
                                        .ThenBy(j => rights[j].CentreZ).ToList();

                var take = Math.Min(byPlace.Count, alsoByPlace.Count);

                for (var k = 0; k < take; k++) yield return (byPlace[k], alsoByPlace[k]);
                yield break;
            }

            var options = new List<(double Distance, int Left, int Right)>(mine.Count * theirs.Count);

            foreach (var i in mine)
                foreach (var j in theirs)
                    options.Add((lefts[i].DistanceTo(rights[j]), i, j));

            // Sorted whole rather than picked greedily per element: taking each left's nearest in
            // turn lets the first element claim a right that the second needed far more, and the
            // answer then depends on the order the walk happened to produce.
            options.Sort(static (a, b) =>
            {
                var byDistance = a.Distance.CompareTo(b.Distance);
                if (byDistance != 0) return byDistance;

                var byLeft = a.Left.CompareTo(b.Left);
                return byLeft != 0 ? byLeft : a.Right.CompareTo(b.Right);
            });

            var tookLeft = new HashSet<int>();
            var tookRight = new HashSet<int>();

            foreach (var option in options)
            {
                if (tookLeft.Contains(option.Left) || tookRight.Contains(option.Right)) continue;

                tookLeft.Add(option.Left);
                tookRight.Add(option.Right);

                yield return (option.Left, option.Right);
            }
        }

        /// <summary>
        /// The fallback identity: the element's name as well as its category, type and size.
        ///
        /// The name is what makes this safe. Category, type and size alone are shared by an added
        /// duct and an unrelated deleted duct of the same size elsewhere in the building, and
        /// pairing those turns a real addition and a real deletion into one fictitious "moved 40
        /// metres", which removes both from the report.
        ///
        /// An element that lost its instance GUID between exports keeps its display name, so
        /// requiring the name costs nothing in the case this exists for. The geometry rung
        /// deliberately does not include the name: two identical brackets are interchangeable there,
        /// which is what lets a shifted bracket read as one move.
        ///
        /// Built through <see cref="ElementKey.FromGeometry"/> so the quantisation matches the one
        /// identity uses elsewhere. Null when an extent cannot be quantised, which a degenerate
        /// element does produce and which must not throw mid-comparison.
        /// </summary>
        private static string? NamedShape(ElementFingerprint print)
        {
            if (!Usable(print.SizeX) || !Usable(print.SizeY) || !Usable(print.SizeZ)) return null;
            if (print.ModelScope.Length == 0) return null;

            return print.Name + "\u0000" + ElementKey
                .FromGeometry(print.ModelScope, print.Category, print.Type,
                              print.SizeX, print.SizeY, print.SizeZ)
                .UnscopedValue;
        }

        /// <summary>
        /// An extent a geometry key can actually be built from.
        ///
        /// Finite is not enough: the key quantises onto a millimetre grid, and an extent of 1e30 —
        /// which a model with a corrupt transform genuinely produces — overflows that and throws.
        /// A hundred kilometres is past anything anybody models and well short of the overflow.
        /// </summary>
        private static bool Usable(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1e5;

        // ---- what is different about one matched element ------------------------------------

        private static ChangeKind Differences(ElementFingerprint before, ElementFingerprint after,
                                              CompareSettings settings, CompareSettings terms,
                                              out double distance, out List<ValueChange> properties)
        {
            var kind = ChangeKind.None;

            distance = before.DistanceTo(after);
            if (distance > settings.Tolerance) kind |= ChangeKind.Moved;

            var mine = before.SortedExtents();
            var theirs = after.SortedExtents();

            var resized = Math.Abs(mine[0] - theirs[0]) > settings.SizeTolerance
                          || Math.Abs(mine[1] - theirs[1]) > settings.SizeTolerance
                          || Math.Abs(mine[2] - theirs[2]) > settings.SizeTolerance;

            if (resized)
            {
                kind |= ChangeKind.Resized;
            }
            else if (Math.Abs(before.SizeX - after.SizeX) > settings.SizeTolerance
                     || Math.Abs(before.SizeY - after.SizeY) > settings.SizeTolerance
                     || Math.Abs(before.SizeZ - after.SizeZ) > settings.SizeTolerance)
            {
                // The same three extents in a different order: a quarter turn about an axis. A
                // rotation that leaves the box alone — a square duct turned about its own run — is
                // invisible here and always will be, because a bounding box is all the host gives.
                kind |= ChangeKind.Rotated;
            }

            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                kind |= ChangeKind.Renamed;

            if (!string.Equals(before.Type, after.Type, StringComparison.Ordinal)
                || !string.Equals(before.Category, after.Category, StringComparison.Ordinal))
                kind |= ChangeKind.Retyped;

            if (settings.UseMeshHash && !resized
                && before.MeshHash != null && after.MeshHash != null
                && !string.Equals(before.MeshHash, after.MeshHash, StringComparison.Ordinal))
                kind |= ChangeKind.Remodelled;

            properties = Values(before, after, settings, terms);
            if (properties.Count > 0) kind |= ChangeKind.PropertyChanged;

            return kind;
        }

        /// <summary>
        /// Which watched properties differ.
        ///
        /// Only properties both sides were watching. A baseline taken watching three named
        /// properties, compared against a run watching the standard list, would otherwise report
        /// every standard property on every element as newly arrived: a hundred thousand changes,
        /// none of them real.
        ///
        /// A merge-walk rather than a dictionary per element, because both lists were sorted by the
        /// same comparison when they were recorded and a federation is a few hundred thousand
        /// elements whose transient dictionaries are pure garbage.
        /// </summary>
        private static List<ValueChange> Values(ElementFingerprint before, ElementFingerprint after,
                                                CompareSettings settings, CompareSettings terms)
        {
            var changes = new List<ValueChange>();

            if (settings.Watch == PropertyWatch.None || terms.Watch == PropertyWatch.None) return changes;

            var mine = before.Values;
            var theirs = after.Values;
            int i = 0, j = 0;

            while (i < mine.Count || j < theirs.Count)
            {
                var order = i >= mine.Count ? 1
                          : j >= theirs.Count ? -1
                          : ElementFingerprint.Order(mine[i].Property, theirs[j].Property);

                if (order == 0)
                {
                    Note(changes, mine[i].Property, mine[i].Value, theirs[j].Value, settings, terms);
                    i++;
                    j++;
                }
                else if (order < 0)
                {
                    Note(changes, mine[i].Property, mine[i].Value, null, settings, terms);
                    i++;
                }
                else
                {
                    Note(changes, theirs[j].Property, null, theirs[j].Value, settings, terms);
                    j++;
                }
            }

            return changes;
        }

        private static void Note(List<ValueChange> changes, PropertyRef property,
                                 string? before, string? after,
                                 CompareSettings settings, CompareSettings terms)
        {
            if (string.Equals(before, after, StringComparison.Ordinal)) return;
            if (!settings.Watches(property) || !terms.Watches(property)) return;

            changes.Add(new ValueChange(property, before, after));
        }

        // ---- what the comparison could not answer -------------------------------------------

        private static void Notes(List<string> notes, CompareResult result,
                                  ModelSnapshot baseline, ModelSnapshot current,
                                  CompareSettings settings, CompareSettings terms, ModelPairing pairing)
        {
            foreach (var ambiguity in pairing.Ambiguities) notes.Add(ambiguity);

            if (settings.Watch == PropertyWatch.None)
                notes.Add("Properties were not compared, so a value corrected without the element "
                          + "moving does not appear here.");
            else if (terms.Watch == PropertyWatch.None)
                notes.Add("The baseline recorded no properties, so no property change can be "
                          + "reported however this run is set up. Take a fresh baseline to fix it "
                          + "from the next comparison onward.");
            else if (terms.Watch != settings.Watch)
                notes.Add("The baseline was taken watching " + Watching(terms.Watch)
                          + " and this run watches " + Watching(settings.Watch)
                          + ". Only properties both were watching take part.");

            if (settings.UseMeshHash && baseline.Shapes > 0 && current.Shapes == 0)
                notes.Add("The baseline measured " + ComparePolicy.Count(baseline.Shapes)
                          + " elements' triangles and this run measured none, so nothing can be "
                          + "reported as remodelled. Tick \"read the actual triangles\" to use "
                          + "what the baseline already paid for.");

            if (settings.UseMeshHash && baseline.Shapes == 0 && current.Shapes == 0)
                notes.Add("Neither side carries a shape signature, so an element remodelled inside "
                          + "its own bounding box is invisible here. Reading one means a geometry "
                          + "pass per element, which is why it is a choice rather than a default — "
                          + "and it has to be made on BOTH sides, because a signature compares "
                          + "against nothing on its own.");

            if (result.WeakMatches > 0)
                notes.Add(ComparePolicy.Count(result.WeakMatches) + " matches rest on category, "
                          + "type and size alone, because those elements carry neither a usable "
                          + "instance GUID nor a stable tree path. Two identical parts cannot be "
                          + "told apart that way, so treat those rows as a proposal.");

            if (result.Skipped > 0)
                notes.Add(ComparePolicy.Count(result.Skipped) + " elements took no part — grouping "
                          + "nodes, or identities weaker than this run allows.");

            if (result.TotalDropped > 0)
                notes.Add(ComparePolicy.Count(result.TotalDropped) + " rows are not listed here "
                          + "because the list is capped at " + ComparePolicy.Count(settings.ExampleCap)
                          + " per bucket. The counts above are complete; export the report for "
                          + "every row.");

            foreach (var gone in pairing.GoneModels)
                notes.Add(gone.Before!.DisplayName + " is in the baseline and not in the current "
                          + "document, so all of it reads as removed. Check whether it was renamed "
                          + "rather than deleted.");

            foreach (var added in pairing.NewModels)
                notes.Add(added.After!.DisplayName + " is in the current document and not in the "
                          + "baseline, so all of it reads as added.");
        }

        private static string Watching(PropertyWatch watch) => watch switch
        {
            PropertyWatch.All => "every property",
            PropertyWatch.Named => "a named list",
            PropertyWatch.None => "no properties",
            _ => "the standard list",
        };

        // ---- accumulating ---------------------------------------------------------------------

        /// <summary>
        /// The accumulator, so that counting, capping and tallying happen in exactly one place.
        ///
        /// Every change goes through <see cref="Keep"/>, which is the only thing that knows about
        /// the per-bucket cap. Written any other way, the counts on the scoreboard and the rows in
        /// the list disagree the first time somebody adds a code path.
        /// </summary>
        private sealed class Run
        {
            private readonly List<ElementChange> _changes = new List<ElementChange>();
            private readonly Dictionary<ChangeBucket, int> _kept = new Dictionary<ChangeBucket, int>();
            private readonly Dictionary<ChangeBucket, int> _dropped = new Dictionary<ChangeBucket, int>();
            private readonly Dictionary<KeyRung, int> _byRung = new Dictionary<KeyRung, int>();
            private readonly Dictionary<ChangeKind, int> _byKind = new Dictionary<ChangeKind, int>();
            private readonly Dictionary<ChangeBucket, List<ElementKey>> _keys =
                new Dictionary<ChangeBucket, List<ElementKey>>();
            private readonly CompareSettings _terms;

            internal Run(CompareSettings settings, CompareSettings terms)
            {
                Settings = settings;
                _terms = terms;
            }

            internal CompareSettings Settings { get; }

            internal int AddedCount { get; private set; }

            internal int RemovedCount { get; private set; }

            internal int MovedCount { get; private set; }

            internal int ChangedCount { get; private set; }

            internal int UnchangedCount { get; private set; }

            internal void Added(ElementFingerprint print, ModelTally tally)
            {
                Keep(new ElementChange(ChangeKind.Added, null, print, tally.Title, null, 0,
                                       Array.Empty<ValueChange>()));
                tally.Added++;
            }

            internal void Removed(ElementFingerprint print, ModelTally tally)
            {
                Keep(new ElementChange(ChangeKind.Removed, print, null, tally.Title, null, 0,
                                       Array.Empty<ValueChange>()));
                tally.Removed++;
            }

            internal void Matched(ElementFingerprint before, ElementFingerprint after, KeyRung rung,
                                  ModelTally tally)
            {
                _byRung[rung] = _byRung.TryGetValue(rung, out var seen) ? seen + 1 : 1;

                var kind = Differences(before, after, Settings, _terms, out var distance, out var values);
                var change = new ElementChange(kind, before, after, tally.Title, rung, distance, values);

                Keep(change);

                switch (change.Bucket)
                {
                    case ChangeBucket.Moved: tally.Moved++; break;
                    case ChangeBucket.Changed: tally.Changed++; break;
                    default: tally.Unchanged++; break;
                }
            }

            private void Keep(ElementChange change)
            {
                var bucket = change.Bucket;

                switch (bucket)
                {
                    case ChangeBucket.Added: AddedCount++; break;
                    case ChangeBucket.Removed: RemovedCount++; break;
                    case ChangeBucket.Moved: MovedCount++; break;
                    case ChangeBucket.Changed: ChangedCount++; break;
                    default: UnchangedCount++; break;
                }

                foreach (var flag in ElementChange.Flags)
                {
                    if ((change.Kind & flag) == 0) continue;
                    _byKind[flag] = _byKind.TryGetValue(flag, out var n) ? n + 1 : 1;
                }

                // Collected for every bucket including unchanged, and before the cap: this is
                // what the model is coloured from, and a ghosted "everything else" needs the
                // unchanged keys as much as the added layer needs the added ones.
                if (change.Print.TryKey(out var key))
                {
                    if (!_keys.TryGetValue(bucket, out var keys)) _keys[bucket] = keys = new List<ElementKey>();
                    keys.Add(key);
                }

                if (bucket == ChangeBucket.Unchanged && !Settings.KeepUnchanged) return;

                var already = _kept.TryGetValue(bucket, out var count) ? count : 0;

                if (already >= Settings.ExampleCap)
                {
                    _dropped[bucket] = _dropped.TryGetValue(bucket, out var over) ? over + 1 : 1;
                    return;
                }

                _kept[bucket] = already + 1;
                _changes.Add(change);
            }

            internal CompareResult Finish(ModelPairing pairing, IReadOnlyList<ModelTally> tallies,
                                          IReadOnlyList<string> notes)
            {
                var result = new CompareResult(Settings, pairing, _changes, tallies, notes)
                {
                    Added = AddedCount,
                    Removed = RemovedCount,
                    Moved = MovedCount,
                    Changed = ChangedCount,
                    Unchanged = UnchangedCount,
                };

                foreach (var entry in _keys) result._keys[entry.Key] = entry.Value;
                foreach (var entry in _dropped) result._dropped[entry.Key] = entry.Value;
                foreach (var entry in _byRung) result._byRung[entry.Key] = entry.Value;
                foreach (var entry in _byKind) result._byKind[entry.Key] = entry.Value;

                return result;
            }
        }
    }
}
