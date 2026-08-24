using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class FreezeTests
    {
        private static readonly DateTimeOffset Signed =
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

        private static ElementKey Key(string name) => ClashFixture.Mep(name);

        private static BoundingBox Box(double x, double y, double z, double size = 1) =>
            new BoundingBox(x, y, z, x + size, y + size, z + size);

        private static FrozenElement At(string name, double x, double y, double z, double size = 1) =>
            new FrozenElement(Key(name), Box(x, y, z, size));

        private static Freeze Frozen(params FrozenElement[] elements) =>
            Freeze.Of("Level 3", "A Coordinator", Signed, new[] { "L03 MEP v STR" }, elements,
                      signatory: "J. Engineer");

        private static Dictionary<string, BoundingBox> Now(params (string Name, BoundingBox Box)[] rows) =>
            rows.ToDictionary(r => Key(r.Name).ToString(), r => r.Box, StringComparer.Ordinal);

        [Fact]
        public void A_sign_off_and_a_release_are_the_same_record_with_a_different_name_on_it()
        {
            var elements = new[] { At("hanger", 0, 0, 3) };

            var signOff = Freeze.Of("L03", "me", Signed, null, elements, signatory: "J. Engineer");
            var release = Freeze.Of("L03", "me", Signed, null, elements, release: "FAB-014");
            var plain = Freeze.Of("L03", "me", Signed, null, elements);

            Assert.Equal("signed off", signOff.Kind);
            Assert.Equal("released for fabrication", release.Kind);
            Assert.Equal("frozen", plain.Kind);

            // Same members, same hash: the record is of the model, not of who signed it.
            Assert.Equal(signOff.Hash, release.Hash);
        }

        [Fact]
        public void Nothing_moving_is_a_clear_notice_that_says_so_plainly()
        {
            var freeze = Frozen(At("hanger", 0, 0, 3), At("duct", 5, 0, 3));

            var notice = freeze.Since(Now(("hanger", Box(0, 0, 3)), ("duct", Box(5, 0, 3))));

            Assert.True(notice.IsClear);
            Assert.Empty(notice.Changes);
            Assert.Contains("nothing frozen has moved", notice.Explain());
            Assert.Contains("14 August 2026", notice.Explain());
        }

        [Fact]
        public void Something_that_moved_is_named_with_how_far()
        {
            var freeze = Frozen(At("hanger", 0, 0, 3));

            var notice = freeze.Since(Now(("hanger", Box(0, 0, 3.15))), tolerance: 0.001);

            var only = Assert.Single(notice.Changes);

            Assert.Equal(0.15, only.Moved, 6);
            Assert.Equal(0, only.Resized, 6);
            Assert.False(only.IsMissing);
            Assert.Equal("moved 150 mm", only.Describe(Measure.Millimetres));
            Assert.Equal(1, notice.Moved);
            Assert.Contains("the furthest by 150 mm", notice.Explain(Measure.Millimetres));
        }

        [Fact]
        public void Something_that_changed_size_is_named_separately_from_something_that_moved()
        {
            // A hanger that is still in the same place but is now 100 mm longer is a different
            // conversation from one that has been shifted.
            var freeze = Frozen(At("duct", 0, 0, 3, size: 1));

            // Widened symmetrically, so the centre is exactly where it was.
            var notice = freeze.Since(Now(("duct", new BoundingBox(-0.1, 0, 3, 1.1, 1, 4))));

            var only = Assert.Single(notice.Changes);

            Assert.Equal(0.2, only.Resized, 6);
            Assert.Equal(0, only.Moved, 6);
            Assert.Equal("resized by 200 mm", only.Describe(Measure.Millimetres));
            Assert.Equal(1, notice.Resized);
            Assert.Equal(0, notice.Moved);
        }

        [Fact]
        public void Something_no_longer_in_the_model_outranks_everything_else_on_the_notice()
        {
            var freeze = Frozen(At("hanger", 0, 0, 3), At("duct", 5, 0, 3));

            var notice = freeze.Since(Now(("duct", Box(5, 0, 3.4))));

            Assert.Equal(2, notice.Changes.Count);
            Assert.True(notice.Changes[0].IsMissing);
            Assert.Equal("gone from the model", notice.Changes[0].Describe());
            Assert.Equal(1, notice.Missing);
            Assert.Contains("1 gone from the model", notice.Explain());
        }

        [Fact]
        public void A_re_export_that_nudges_the_last_decimal_place_is_not_a_change()
        {
            // A change notice that reported every one of those would be a change notice nobody
            // reads, and the first thing anybody does with it is stop reading it.
            var freeze = Frozen(At("hanger", 0, 0, 3));

            var notice = freeze.Since(Now(("hanger", Box(0.0000004, 0, 3))));

            Assert.True(notice.IsClear);
        }

        [Fact]
        public void The_tolerance_is_settable_because_a_millimetre_is_not_always_the_answer()
        {
            var freeze = Frozen(At("hanger", 0, 0, 3));
            var moved = Now(("hanger", Box(0, 0, 3.02)));

            Assert.False(freeze.Since(moved).IsClear);
            Assert.True(freeze.Since(moved, tolerance: 0.05).IsClear);
        }

        [Fact]
        public void The_worst_thing_is_at_the_top_because_that_is_what_gets_read()
        {
            var freeze = Frozen(At("a", 0, 0, 0), At("b", 10, 0, 0), At("c", 20, 0, 0));

            var notice = freeze.Since(Now(
                ("a", Box(0, 0, 0.05)),
                ("b", Box(10, 0, 0.4)),
                ("c", Box(20, 0, 0.1))));

            Assert.Equal(3, notice.Changes.Count);
            Assert.Equal(0.4, notice.Changes[0].Moved, 6);
            Assert.Equal(0.4, notice.Worst, 6);
        }

        [Fact]
        public void An_altered_record_no_longer_verifies()
        {
            // Not a security measure - anybody who can edit the file can recompute it. It catches
            // the ordinary case, where the alternative is a document somebody signed quietly
            // meaning something else.
            var freeze = Frozen(At("hanger", 0, 0, 3));

            Assert.True(freeze.IsIntact);

            // The hanger is the only thing in the record whose centre is at 3.5, so moving it to
            // 4.5 in the text is exactly the hand edit this catches - and the stored hash still
            // says 3.5.
            var text = freeze.ToJson().ToJson().Replace("3.5", "4.5");

            var tampered = Freeze.FromJson(JsonReader.Parse(text))!;

            Assert.False(tampered.IsIntact);
            Assert.Contains("HAS BEEN ALTERED", tampered.Describe());
        }

        [Fact]
        public void It_writes_down_and_reads_back_whole()
        {
            var freeze = Freeze.Of("Level 3", "A Coordinator", Signed,
                new[] { "L03 MEP v STR", "L03 MEP v ARCH" },
                new[] { At("hanger", 1.5, 2.5, 3.5), At("duct", 5, 0, 3) },
                release: "FAB-014");

            var back = Freeze.FromJson(JsonReader.Parse(freeze.ToJson().ToJson()))!;

            Assert.Equal(freeze.Id, back.Id);
            Assert.Equal("Level 3", back.Scope);
            Assert.Equal(Signed, back.When);
            Assert.Equal("A Coordinator", back.By);
            Assert.Equal("FAB-014", back.Release);
            Assert.Null(back.Signatory);
            Assert.Equal(2, back.Groups.Count);
            Assert.Equal(2, back.Elements.Count);
            Assert.True(back.IsIntact);

            // And it still measures against the model afterwards, which is the whole point of
            // keeping it.
            Assert.True(back.Since(Now(("hanger", Box(1.5, 2.5, 3.5)), ("duct", Box(5, 0, 3)))).IsClear);
        }

        [Fact]
        public void Anything_unreadable_reads_back_as_no_freeze_rather_than_throwing()
        {
            Assert.Null(Freeze.FromJson(null));
            Assert.Null(Freeze.FromJson(JsonValue.Array()));
            Assert.Null(Freeze.FromJson(JsonValue.Object()));
            Assert.Null(FrozenElement.FromJson(JsonValue.Array()));
        }

        [Fact]
        public void The_same_element_twice_is_frozen_once()
        {
            var freeze = Freeze.Of("L03", "me", Signed, null,
                new[] { At("hanger", 0, 0, 3), At("hanger", 0, 0, 3) });

            Assert.Single(freeze.Elements);
        }

        [Fact]
        public void A_freeze_of_nothing_is_a_record_rather_than_an_error()
        {
            var freeze = Freeze.Of(null, null, Signed, null, null);

            Assert.Equal("the whole board", freeze.Scope);
            Assert.Equal("somebody", freeze.By);
            Assert.Empty(freeze.Elements);
            Assert.True(freeze.Since(null).IsClear);
        }

        [Fact]
        public void The_notice_reads_as_a_table_with_the_project_own_units()
        {
            var freeze = Frozen(At("hanger", 0, 0, 3));

            var table = freeze.Since(Now(("hanger", Box(0, 0, 3.15)))).Table(Measure.Millimetres,
                _ => "Hanger 12");

            Assert.Equal("Moved (mm)", table.Headers[2]);

            var row = Assert.Single(table.Rows);

            Assert.Equal("Hanger 12", row[0]);
            Assert.Equal("150", row[2]);
            Assert.Equal(string.Empty, row[3]);
        }
    }
}
