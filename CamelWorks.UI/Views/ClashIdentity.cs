using System;
using System.Collections.Generic;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Puts the IFC GlobalIds on clash items during an export.
    ///
    /// BCF names elements by their IFC GlobalId and by nothing else. A clash file written without
    /// them reaches the architect's tool as an issue with a camera and an empty selection: the view
    /// is roughly right and nothing is highlighted.
    ///
    /// It is not free. The GlobalId is a property on the element, so finding it means resolving the
    /// element and reading its properties. The document caches per element, so the cost is one read
    /// per distinct element rather than one per clash: a federation with four hundred ducts in
    /// eighteen thousand clashes pays four hundred times. It still reports progress, because on a
    /// first export of a large board it is the slow part.
    ///
    /// A federation with no IFC in it pays the cost once and finds nothing. The export then reports
    /// how many components a receiving tool will not be able to select, rather than writing an id
    /// that names nothing.
    /// </summary>
    internal static class ClashIdentity
    {
        /// <summary>
        /// The GlobalId and the route to every element these findings name, read once each.
        ///
        /// One read per distinct element rather than one per finding: a federation with four
        /// hundred ducts in eighteen thousand clashes pays four hundred times. Progress is
        /// reported, because on a first export of a large board this is the slow part.
        ///
        /// A federation with no IFC in it pays the cost once and finds nothing. The export then
        /// reports how many components a receiving tool will not be able to select, rather than
        /// writing an id that names nothing.
        /// </summary>
        internal sealed class Ids
        {
            private readonly Dictionary<ElementKey, string?> _guids = new Dictionary<ElementKey, string?>();

            private readonly Dictionary<ElementKey, ElementLocator> _where =
                new Dictionary<ElementKey, ElementLocator>();

            /// <summary>Read what the document knows about every element these findings name.</summary>
            /// <param name="session">The open document, or null for a file board.</param>
            /// <param name="findings">The findings going out.</param>
            /// <param name="job">Progress, and the way to stop.</param>
            internal Ids(Session? session, IReadOnlyList<Finding>? findings, Job? job)
            {
                if (findings == null) return;

                var wanted = new List<ElementKey>();

                foreach (var finding in findings)
                {
                    if (finding == null) continue;

                    foreach (var element in finding.AllElements())
                        if (!element.IsEmpty && !_guids.ContainsKey(element))
                        {
                            _guids[element] = null;
                            wanted.Add(element);
                        }
                }

                Wanted = wanted.Count;

                if (session == null) return;

                job?.Say("Reading IFC ids, so the receiving tool can select the elements...");

                var seen = 0;

                foreach (var element in wanted)
                {
                    if (job != null && !job.Step(++seen, wanted.Count, "elements read")) break;

                    try
                    {
                        var guid = session.Model.IfcGuidFor(element);

                        // Nothing on the element said what its GlobalId is - which is every
                        // element of a Revit- or DWG-sourced federation. The one this project's
                        // own IFC exporter would give it is not a guess: it is the same function
                        // over the same instance GUID. Counted apart from the ids actually read,
                        // because it is a narrower promise.
                        if (guid == null)
                        {
                            guid = session.Model.DerivedIfcGuidFor(element);

                            if (guid != null) Derived++;
                        }
                        else
                        {
                            Read++;
                        }

                        _guids[element] = guid;

                        // Where this element is in this document, written down for the seat that
                        // opens the file. That seat has only the key, and a key is a hash: the one
                        // way back from it is to hash the whole federation until something
                        // matches. This costs nothing here - the element has just been resolved
                        // for the GlobalId above.
                        _where[element] = new ElementLocator(element,
                                                             session.Model.PathIdFor(element),
                                                             session.Model.TreePathFor(element));
                    }
                    catch (Exception)
                    {
                        // An element the host will not read is an element with no GlobalId. Never
                        // a reason to fail the export it was only trying to improve.
                    }
                }
            }

            /// <summary>Ids taken from a GlobalId the element itself carried.</summary>
            internal int Read { get; private set; }

            /// <summary>Ids computed from the element's instance GUID, as our IFC exporter does.</summary>
            internal int Derived { get; private set; }

            /// <summary>How many elements were wanted.</summary>
            internal int Wanted { get; }

            /// <summary>What was read, in the shape the writer asks for it.</summary>
            internal IdStamp Stamp => new IdStamp(Read, Derived, Wanted);

            /// <summary>The GlobalId of one element, or null.</summary>
            /// <param name="key">The element.</param>
            internal string? Of(ElementKey key) =>
                _guids.TryGetValue(key, out var guid) ? guid : null;

            /// <summary>Where one element sits in this document.</summary>
            /// <param name="key">The element.</param>
            internal ElementLocator At(ElementKey key) =>
                _where.TryGetValue(key, out var found) ? found : new ElementLocator(key, null, null);
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

            /// <summary>How many elements were wanted.</summary>
            internal int Wanted { get; }

            /// <summary>How many components a receiving tool has some chance of resolving.</summary>
            internal int Total => Read + Derived;
        }

        /// <summary>
        /// How a clash gets its image, and what it costs.
        ///
        /// Two routes, in order of preference. The clash engine can render a result directly, which
        /// frames the clash as the native window does and never touches the camera the user is
        /// looking through. Where that is unavailable — a file source, an edition with no clash
        /// engine, a result the engine will not draw — the camera is moved to the clash point and
        /// the view captured, with the camera borrowed and restored so the run leaves the screen
        /// where it found it.
        ///
        /// It is slow, and opt-in for that reason: on a large board this is minutes.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="width">Pixels across.</param>
        /// <param name="height">Pixels down.</param>
        /// <param name="idOf">The test and result a clash came from, when they are known.</param>
        /// <param name="job">Progress, and the way to stop.</param>
        internal static Func<Finding, byte[]?> Pictures(
            Session session, int width, int height,
            Func<Finding, (string Test, string Result)?>? idOf = null,
            Job? job = null)
        {
            var taken = 0;

            return item =>
            {
                if (item == null) return null;

                job?.Step(++taken, "findings drawn");

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
                    session.View.LookAt(item.X ?? 0, item.Y ?? 0, item.Z ?? 0, 4.0);

                    return session.View.Capture(width, height);
                }
                catch (Exception)
                {
                    return null;
                }
            };
        }

        /// <summary>What to say about it, or null when there is nothing worth saying.</summary>
        /// <param name="items">How many findings were written.</param>
        /// <param name="stamp">What <see cref="Ids"/> read: ids read, and ids computed.</param>
        internal static string? Explain(int items, IdStamp stamp)
        {
            if (items <= 0) return null;

            var wanted = stamp.Wanted > 0 ? stamp.Wanted : items;

            if (stamp.Total >= wanted && stamp.Derived == 0)
                return "Every element carries an IFC id, so the receiving tool can select "
                       + "everything these topics point at.";

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
