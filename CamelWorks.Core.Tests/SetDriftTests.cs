using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Sets;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// A set that stopped returning what it used to.
    ///
    /// The check exists because a set going quiet does not fail anything - it disarms everything
    /// pointing at it. A clash test whose side is empty finds no clashes; a clearance check over
    /// nothing finds nothing too low; a scoped rule reports "not applicable". Every one reads as
    /// green. So what is tested here is mostly the grading: that the two disarming cases are
    /// always said, and that ordinary movement on a live model is not, because a check that
    /// reports every change is a check nobody reads.
    /// </summary>
    public class SetDriftTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.nwc");

        private static ElementKey Key(int n) =>
            ElementKey.FromInstanceGuid(Scope,
                Guid.Parse(n.ToString("x8") + "-0000-0000-0000-000000000000"));

        private static IReadOnlyList<ElementKey> Keys(int from, int count) =>
            Enumerable.Range(from, count).Select(Key).ToList();

        private static SetReading Read(string name, int from, int count, long when = 1) =>
            new SetReading(name, Keys(from, count), when);

        [Fact]
        public void ASetThatHoldsWhatItHeldSaysNothing()
        {
            var drift = SetDrift.Between(Read("Ducts", 1, 40), Read("Ducts", 1, 40));

            Assert.Equal(SetDriftKind.Steady, drift!.Kind);
            Assert.False(drift.Matters);
            Assert.Empty(drift.Left);
        }

        [Fact]
        public void ASetThatEmptiedIsAFault()
        {
            // The one that matters most: everything pointing at it now checks nothing and passes.
            var drift = SetDrift.Between(Read("Ducts", 1, 40), new SetReading("Ducts", null, 2));

            Assert.Equal(SetDriftKind.Emptied, drift!.Kind);
            Assert.True(drift.Matters);
            Assert.Equal(40, drift.Was);
            Assert.Equal(0, drift.Now);
            Assert.Equal(40, drift.Left.Count);
            Assert.Contains("returns nothing", drift.Explain());
            Assert.Contains("held 40 elements", drift.Explain());
        }

        [Fact]
        public void ASetTheDocumentNoLongerHasIsAFault()
        {
            // Easier to miss than an empty one, because there is no row for it anywhere.
            var drift = SetDrift.Between(Read("Ducts", 1, 40), SetReading.Missing("Ducts", 2));

            Assert.Equal(SetDriftKind.Gone, drift!.Kind);
            Assert.True(drift.Matters);
            Assert.Contains("not in this document any more", drift.Explain());
        }

        [Fact]
        public void ASetThatLostAQuarterIsWorthALook()
        {
            var drift = SetDrift.Between(Read("Ducts", 1, 40), Read("Ducts", 1, 30));

            Assert.Equal(SetDriftKind.Shrank, drift!.Kind);
            Assert.True(drift.Matters);
            Assert.Equal(10, drift.Left.Count);
            Assert.Contains("25% of it is gone", drift.Explain());
        }

        [Fact]
        public void ASetThatLostAFewIsNotNews()
        {
            // A live model changes every day. A row for every change is a page nobody reads, and
            // the two that matter hide in it.
            var drift = SetDrift.Between(Read("Ducts", 1, 40), Read("Ducts", 1, 38));

            Assert.Equal(SetDriftKind.Steady, drift!.Kind);
            Assert.False(drift.Matters);

            // The elements that went are still named, for somebody who asks.
            Assert.Equal(2, drift.Left.Count);
        }

        [Fact]
        public void HowMuchCountsAsDriftIsASetting()
        {
            var was = Read("Ducts", 1, 40);
            var now = Read("Ducts", 1, 36);

            Assert.Equal(SetDriftKind.Steady, SetDrift.Between(was, now)!.Kind);
            Assert.Equal(SetDriftKind.Shrank, SetDrift.Between(was, now, loss: 0.1)!.Kind);
        }

        [Fact]
        public void ASetThatGrewIsOrdinary()
        {
            var drift = SetDrift.Between(Read("Ducts", 1, 40), Read("Ducts", 1, 60));

            Assert.Equal(SetDriftKind.Grew, drift!.Kind);
            Assert.False(drift.Matters);
            Assert.Equal(20, drift.Joined.Count);
            Assert.Empty(drift.Left);
        }

        [Fact]
        public void ASetThatSwappedItsMembersSaysWhichWayRound()
        {
            // Same count, different elements. Not a shrink by number, and worth being able to see.
            var drift = SetDrift.Between(Read("Ducts", 1, 40), Read("Ducts", 41, 40));

            Assert.Equal(SetDriftKind.Steady, drift!.Kind);
            Assert.Equal(40, drift.Left.Count);
            Assert.Equal(40, drift.Joined.Count);
        }

        [Fact]
        public void ASetNobodyReadBeforeIsNotDrift()
        {
            // Otherwise every set made today is a finding today, and the check cries wolf on the
            // first run it ever does.
            Assert.Null(SetDrift.Between(null, Read("Ducts", 1, 40)));
            Assert.Null(SetDrift.Between(Read("Ducts", 1, 40), null));
        }

        [Fact]
        public void ACappedReadingSaysItsListIsASample()
        {
            // The counts stay exact and the list does not. A report that showed a sample as the
            // whole would send somebody hunting elements it never mentioned.
            var was = new SetReading("Ducts", Keys(1, 10), 1, whole: false, held: 900);
            var now = new SetReading("Ducts", Keys(1, 5), 2, whole: false, held: 400);

            var drift = SetDrift.Between(was, now)!;

            Assert.False(drift.IsCertain);
            Assert.Equal(900, drift.Was);
            Assert.Equal(400, drift.Now);
            Assert.Equal(SetDriftKind.Shrank, drift.Kind);
        }

        [Fact]
        public void AWholeRunIsWorstFirst()
        {
            var was = new[]
            {
                Read("Steady", 1, 10), Read("Grew", 1, 10),
                Read("Shrank", 1, 40), Read("Emptied", 1, 10), Read("Gone", 1, 10),
            };

            var now = new[]
            {
                Read("Steady", 1, 10), Read("Grew", 1, 20),
                Read("Shrank", 1, 20), new SetReading("Emptied", null, 2),
            };

            var drifted = SetDrift.Across(was, now);

            Assert.Equal(5, drifted.Count);
            Assert.Equal(new[] { "Gone", "Emptied", "Shrank", "Grew", "Steady" },
                         drifted.Select(d => d.Name).ToArray());

            Assert.Equal(3, drifted.Count(d => d.Matters));
        }

        [Fact]
        public void ASetNothingReadThisTimeIsGoneRatherThanForgotten()
        {
            var drifted = SetDrift.Across(new[] { Read("Ducts", 1, 40) },
                                          Array.Empty<SetReading>());

            Assert.Equal(SetDriftKind.Gone, drifted.Single().Kind);
        }

        [Fact]
        public void ANewSetIsNotReportedOnTheRunThatFirstSeesIt()
        {
            Assert.Empty(SetDrift.Across(Array.Empty<SetReading>(), new[] { Read("Ducts", 1, 40) }));
            Assert.Empty(SetDrift.Across(null, null));
        }

        [Fact]
        public void ASetNeedsAName()
        {
            Assert.Throws<ArgumentException>(() => new SetReading("  ", null));
        }

        [Fact]
        public void AnEmptyKeyIsNotAMember()
        {
            // The host hands back what it can resolve. A blank among them is a gap in the read,
            // not an element, and counting it would make a set look fuller than it is.
            var reading = new SetReading("Ducts", new[] { Key(1), default, Key(2) });

            Assert.Equal(2, reading.Held);
        }
    }
}
