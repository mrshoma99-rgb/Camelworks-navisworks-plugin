using System;
using System.Collections.Generic;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Puts the IFC GlobalIds on clash items on the way out of the door.
    ///
    /// <b>Why an export does this at all.</b> BCF names elements by their IFC GlobalId and by
    /// nothing else. A clash file written without them reaches the architect's tool as an issue
    /// with a camera and an empty selection: they are flown to roughly the right place and shown
    /// nothing highlighted, which is the difference between a file somebody works from and a file
    /// somebody scrolls past.
    ///
    /// <b>Why it is not free.</b> The GlobalId is a property on the element, so finding it means
    /// resolving the element and reading its properties. The document caches per element, so the
    /// cost is one read per distinct element rather than one per clash - a federation with four
    /// hundred ducts in eighteen thousand clashes pays four hundred times, not thirty-six thousand.
    /// It still reports progress, because on a first export of a large board it is the slow part.
    ///
    /// A federation with no IFC in it pays the cost once and finds nothing, which is the honest
    /// outcome: the export then says how many components a receiving tool will not be able to
    /// select, rather than writing an id that names nothing.
    /// </summary>
    internal static class ClashIdentity
    {
        /// <summary>
        /// Stamp every item with the GlobalIds of its two elements.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="items">The items, edited in place.</param>
        /// <param name="job">Progress, and the way to stop.</param>
        /// <returns>How many of the two-per-item ids were found.</returns>
        internal static IdStamp Stamp(Session? session, IReadOnlyList<ClashItem>? items, Job? job)
        {
            if (session == null || items == null || items.Count == 0) return default;

            var found = 0;
            var derived = 0;
            var seen = 0;

            job?.Say("Reading IFC ids, so the receiving tool can select the elements...");

            foreach (var item in items)
            {
                if (job != null && !job.Step(++seen, items.Count, "elements read")) break;

                if (item == null) continue;

                try
                {
                    item.IfcGuidA ??= session.Model.IfcGuidFor(item.Key.A);
                    item.IfcGuidB ??= session.Model.IfcGuidFor(item.Key.B);

                    // Where these two elements are in this document, written down for the seat
                    // that opens the file. That seat has only the keys, and a key is a hash: the
                    // one way back from it is to hash the whole federation until something
                    // matches. This costs nothing here - the elements have just been resolved for
                    // the GlobalIds above - and it is the difference between a reviewer who can
                    // isolate a clash and one who is told to read the results first.
                    item.PathIdA ??= session.Model.PathIdFor(item.Key.A);
                    item.PathIdB ??= session.Model.PathIdFor(item.Key.B);
                    item.TreeA ??= session.Model.TreePathFor(item.Key.A);
                    item.TreeB ??= session.Model.TreePathFor(item.Key.B);

                    // Nothing on the element said what its GlobalId is - which is every element of
                    // a Revit- or DWG-sourced federation. The one this project's own IFC exporter
                    // would give it is not a guess: it is the same function over the same instance
                    // GUID. Counted apart from the ids actually read, because it is a narrower
                    // promise.
                    if (item.IfcGuidA == null && (item.IfcGuidA = session.Model.DerivedIfcGuidFor(item.Key.A)) != null)
                        derived++;

                    if (item.IfcGuidB == null && (item.IfcGuidB = session.Model.DerivedIfcGuidFor(item.Key.B)) != null)
                        derived++;
                }
                catch (Exception)
                {
                    // An element the host will not read is an element with no GlobalId. Never a
                    // reason to fail the export it was only trying to improve.
                    continue;
                }

                if (item.IfcGuidA != null) found++;
                if (item.IfcGuidB != null) found++;
            }

            return new IdStamp(found - derived, derived, items.Count * 2);
        }

        /// <summary>
        /// How many of an export's element ids came from the model, and how many were computed.
        ///
        /// The two are worth different things to whoever opens the file, so they are counted
        /// apart rather than added up: an id read off the element resolves in any correctly
        /// written IFC of that model, and a computed one resolves in an IFC this project's own
        /// exporter wrote. Saying "every element carries an id" of the second kind would be a
        /// promise the file cannot keep.
        /// </summary>
        internal readonly struct IdStamp
        {
            internal IdStamp(int read, int derived, int wanted)
            {
                Read = read; Derived = derived; Wanted = wanted;
            }

            /// <summary>Ids taken from a GlobalId the element itself carried.</summary>
            internal int Read { get; }

            /// <summary>Ids computed from the element's instance GUID, as our IFC exporter does.</summary>
            internal int Derived { get; }

            /// <summary>How many were wanted: two per clash.</summary>
            internal int Wanted { get; }

            /// <summary>How many components a receiving tool has some chance of resolving.</summary>
            internal int Total => Read + Derived;
        }

        /// <summary>
        /// How a clash gets its picture, and what it costs.
        ///
        /// Two ways, in order of preference. The clash engine can render a result directly, which
        /// is the good one: it frames the clash the way the native window does, and it never
        /// touches the camera the coordinator is looking through. Where that is unavailable - a
        /// file source, an edition with no clash engine, a result the engine will not draw - the
        /// camera is moved to the clash point and the view captured, with the camera borrowed and
        /// put back so the run leaves the screen where it found it.
        ///
        /// <b>It is slow, and it is opt-in for that reason.</b> A render is a render: on a large
        /// board this is minutes, and it is the one part of an export worth being asked about
        /// rather than surprised by.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="width">Pixels across.</param>
        /// <param name="height">Pixels down.</param>
        /// <param name="idOf">The test and result a clash came from, when they are known.</param>
        /// <param name="job">Progress, and the way to stop.</param>
        internal static Func<ClashItem, byte[]?> Pictures(
            Session session, int width, int height,
            Func<ClashItem, (string Test, string Result)?>? idOf = null,
            Job? job = null)
        {
            var taken = 0;

            return item =>
            {
                if (item == null) return null;

                job?.Step(++taken, "clashes drawn");

                var named = idOf?.Invoke(item);

                if (named != null)
                {
                    try
                    {
                        var drawn = session.Clash.ResultSnapshot(named.Value.Test, named.Value.Result,
                                                                 width, height);

                        if (drawn != null && drawn.Length > 0) return drawn;
                    }
                    catch (Exception)
                    {
                        // The engine would not draw this one. The camera route still might.
                    }
                }

                try
                {
                    // Four metres back is the framing Focus uses, so the picture matches what
                    // pressing Focus on the same row would show.
                    session.View.LookAt(item.X, item.Y, item.Z, 4.0);

                    return session.View.Capture(width, height);
                }
                catch (Exception)
                {
                    return null;
                }
            };
        }

        /// <summary>What to say about it, or null when there is nothing worth saying.</summary>
        /// <param name="items">How many items were written.</param>
        /// <param name="stamp">What <see cref="Stamp"/> returned: ids read, and ids computed.</param>
        internal static string? Explain(int items, IdStamp stamp)
        {
            if (items <= 0) return null;

            var wanted = stamp.Wanted > 0 ? stamp.Wanted : items * 2;

            if (stamp.Total >= wanted && stamp.Derived == 0)
                return "Every element carries an IFC id, so the receiving tool can select both "
                       + "sides of every clash.";

            if (stamp.Derived == 0)
                return null;

            // The interesting case, and the one that used to read as a flat failure. Nothing on
            // these elements says what their GlobalId is, but this project's IFC exporter derives
            // one from the same instance GUID - so a file sent alongside an IFC it wrote will
            // select, and one sent alongside anybody else's will not.
            var computed = Ui.Count(stamp.Derived, "element")
                + " carried no IFC id, so the id this project's own IFC exporter gives "
                + (stamp.Derived == 1 ? "it was" : "them was") + " computed instead. "
                + (stamp.Derived == 1 ? "It selects" : "They select")
                + " in an IFC written by that exporter, and in nothing else.";

            return stamp.Read == 0
                ? computed
                : Ui.Count(stamp.Read, "element") + " carried an IFC id of "
                  + wanted.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + computed;
        }
    }
}
