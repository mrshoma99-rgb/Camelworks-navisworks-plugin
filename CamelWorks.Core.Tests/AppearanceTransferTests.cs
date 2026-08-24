using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class AppearanceTransferTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");
        private static readonly string Other = ElementKey.ScopeOf(@"C:\p\ARCH-rev2.nwc");

        private static readonly Colour Red = new Colour(0xFF, 0x00, 0x00);
        private static readonly Colour Green = new Colour(0x00, 0xFF, 0x00);

        private static ElementKey Key(string name, string? scope = null) =>
            ElementKey.FromTreePath(scope ?? Scope, @"C:\p\ARCH.nwc", "L01", name);

        private static AppearanceState State(ElementKey key, Colour? colour = null,
                                             double? transparency = null, bool hidden = false) =>
            new AppearanceState(key, colour, transparency, hidden, false);

        /// <summary>Two red walls, a half-transparent duct, a hidden beam, and one clean element.</summary>
        private static AppearanceSnapshot Scheme()
        {
            var paths = new Dictionary<ElementKey, string>
            {
                [Key("Wall A")] = "Model > L01 > Wall A",
                [Key("Wall B")] = "Model > L01 > Wall B",
                [Key("Duct")] = "Model > L01 > Duct",
                [Key("Beam")] = "Model > L02 > Beam",
                [Key("Gone")] = "Model > L02 > Gone",
            };

            return AppearanceTransfer.Capture(new[]
            {
                State(Key("Wall A"), Red),
                State(Key("Wall B"), Red),
                State(Key("Duct"), transparency: 0.5),
                State(Key("Beam"), hidden: true),
                State(Key("Gone"), Green),
                State(Key("Untouched")),
            }, paths);
        }

        // -----------------------------------------------------------------------------------
        // Taking it off a model
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Only_what_is_actually_overridden_is_taken()
        {
            var snapshot = Scheme();

            // A snapshot of the pristine elements would be a file the size of the federation
            // saying nothing.
            Assert.Equal(5, snapshot.Entries.Count);
            Assert.DoesNotContain(snapshot.Entries, e => e.Key == Key("Untouched"));

            Assert.Equal(3, snapshot.Coloured);
            Assert.Equal(1, snapshot.Transparent);
            Assert.Equal(1, snapshot.Hidden);
            Assert.Equal(5, snapshot.Pathed);
            Assert.True(snapshot.IsUsable);

            Assert.Contains("3 coloured", snapshot.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_model_with_nothing_overridden_says_so_rather_than_writing_a_file()
        {
            var snapshot = AppearanceTransfer.Capture(new[] { State(Key("a")), State(Key("b")) });

            Assert.False(snapshot.IsUsable);
            Assert.Equal("Nothing in this model is overridden.", snapshot.Explain());
        }

        [Fact]
        public void Capturing_nothing_at_all_is_not_a_crash()
        {
            Assert.False(AppearanceTransfer.Capture(null).IsUsable);
        }

        // -----------------------------------------------------------------------------------
        // Finding it again somewhere else
        // -----------------------------------------------------------------------------------

        /// <summary>Two elements the target keys the same way, two only its names find, one absent.</summary>
        private static IReadOnlyList<KeyValuePair<ElementKey, string?>> Target() =>
            new[]
            {
                new KeyValuePair<ElementKey, string?>(Key("Wall A"), null),
                new KeyValuePair<ElementKey, string?>(Key("Wall B"), "Model > L01 > Wall B"),
                new KeyValuePair<ElementKey, string?>(Key("Duct", Other), "Model > L01 > Duct"),
                new KeyValuePair<ElementKey, string?>(Key("Beam", Other), "Model > L02 > Beam"),
            };

        [Fact]
        public void The_key_is_tried_first_and_the_name_second()
        {
            var match = AppearanceTransfer.Match(Scheme(), Target());

            Assert.Equal(new[] { "Wall A", "Wall B" },
                         match.ByKey.Select(e => Name(e.Path)));
            Assert.Equal(new[] { "Duct", "Beam" }, match.ByPath.Select(e => Name(e.Path)));
            Assert.Equal(new[] { "Gone" }, match.Missing.Select(e => Name(e.Path)));

            Assert.Equal(4, match.Found);
            Assert.True(match.IsUsable);

            // Where each one landed is what the layer targets, and for the two found by name that
            // is a key from the other model.
            Assert.Equal(Key("Duct", Other), match.Landed[match.ByPath[0]]);

            var line = match.Explain();

            Assert.Contains("4 of 5 found", line, StringComparison.Ordinal);
            Assert.Contains("2 of them by name", line, StringComparison.Ordinal);
            Assert.Contains("one is not here", line, StringComparison.Ordinal);
        }

        [Fact]
        public void Turning_the_name_fallback_off_leaves_only_what_the_identity_finds()
        {
            var match = AppearanceTransfer.Match(Scheme(), Target(),
                                                 new TransferOptions { ByPath = false });

            Assert.Equal(2, match.ByKey.Count);
            Assert.Empty(match.ByPath);
            Assert.Equal(3, match.Missing.Count);
        }

        [Fact]
        public void A_name_two_elements_share_decides_nothing_and_says_so()
        {
            // Picking the first is how a scheme lands on the wrong wall.
            var snapshot = AppearanceTransfer.Capture(
                new[] { State(Key("x"), Red) },
                new Dictionary<ElementKey, string> { [Key("x")] = "Same name" });

            var match = AppearanceTransfer.Match(snapshot, new[]
            {
                new KeyValuePair<ElementKey, string?>(Key("p", Other), "Same name"),
                new KeyValuePair<ElementKey, string?>(Key("q", Other), "Same name"),
            });

            Assert.Empty(match.ByKey);
            Assert.Empty(match.ByPath);
            Assert.Single(match.Missing);
            Assert.Equal(1, match.Ambiguous);
            Assert.Contains("matched more than one element", match.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_snapshot_from_another_federation_entirely_says_that_plainly()
        {
            var match = AppearanceTransfer.Match(Scheme(), new[]
            {
                new KeyValuePair<ElementKey, string?>(Key("nothing", Other), "Nowhere > At all"),
            });

            Assert.False(match.IsUsable);
            Assert.Contains("None of 5 elements", match.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_snapshot_against_anything_is_empty()
        {
            var match = AppearanceTransfer.Match(null, Target());

            Assert.Equal("That snapshot is empty.", match.Explain());
            Assert.False(match.IsUsable);
        }

        // -----------------------------------------------------------------------------------
        // Putting it on
        // -----------------------------------------------------------------------------------

        [Fact]
        public void One_layer_per_distinct_appearance_not_one_per_element()
        {
            var layers = AppearanceTransfer.Layers(AppearanceTransfer.Match(Scheme(), Target()),
                                                   name: "Workshop scheme");

            // Two reds, one transparency, one hidden - three layers, biggest first.
            Assert.Equal(3, layers.Count);

            // As a set: a key's text form is a hash, so ordering two of them proves nothing about
            // anything and would pass or fail on which way the hashes fell.
            Assert.Equal(2, Targeted(layers[0]).Count);
            Assert.Contains(Key("Wall A"), Targeted(layers[0]));
            Assert.Contains(Key("Wall B"), Targeted(layers[0]));

            Assert.Equal(Red, layers[0].Colour);
            Assert.Null(layers[0].Transparency);
            Assert.Null(layers[0].Visible);
            Assert.StartsWith("Workshop scheme: coloured", layers[0].Name, StringComparison.Ordinal);

            var clear = layers.Single(l => l.Transparency != null);

            Assert.Equal(0.5, clear.Transparency!.Value, 3);
            Assert.Equal(new[] { Key("Duct", Other) }, Targeted(clear));

            var gone = layers.Single(l => l.Visible == false);

            Assert.Equal(new[] { Key("Beam", Other) }, Targeted(gone));
            Assert.Null(gone.Colour);
        }

        [Fact]
        public void A_layer_never_claims_visible_on_what_was_only_coloured()
        {
            // Asserting "visible" on everything it touched would overrule every layer under it,
            // which is not what bringing a colour scheme in means.
            var layers = AppearanceTransfer.Layers(AppearanceTransfer.Match(Scheme(), Target()));

            Assert.All(layers.Where(l => l.Colour != null), l => Assert.Null(l.Visible));
        }

        [Fact]
        public void Leaving_the_hiding_behind_leaves_the_hiding_behind()
        {
            var options = new TransferOptions { Hidden = false };

            var layers = AppearanceTransfer.Layers(
                AppearanceTransfer.Match(Scheme(), Target(), options), options);

            // The hidden beam carried nothing else, so it makes no layer at all.
            Assert.Equal(2, layers.Count);
            Assert.All(layers, l => Assert.Null(l.Visible));
        }

        [Fact]
        public void Taking_only_the_colours_takes_only_the_colours()
        {
            var options = new TransferOptions { Transparency = false, Hidden = false };

            var layers = AppearanceTransfer.Layers(
                AppearanceTransfer.Match(Scheme(), Target(), options), options);

            var layer = Assert.Single(layers);

            Assert.Equal(Red, layer.Colour);
            Assert.Null(layer.Transparency);
        }

        [Fact]
        public void Every_layer_says_where_it_came_from_and_that_it_can_be_undone()
        {
            foreach (var layer in AppearanceTransfer.Layers(
                         AppearanceTransfer.Match(Scheme(), Target())))
            {
                Assert.Contains("Brought in from", layer.Note!, StringComparison.Ordinal);
                Assert.Contains("Switch it off", layer.Note!, StringComparison.Ordinal);
                Assert.False(layer.IsEmpty);
            }
        }

        [Fact]
        public void Layer_ids_are_distinct_so_a_stack_can_hold_them_all()
        {
            var layers = AppearanceTransfer.Layers(AppearanceTransfer.Match(Scheme(), Target()),
                                                   stem: "scheme");

            Assert.Equal(layers.Count, layers.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(layers, l => Assert.StartsWith("scheme-", l.Id, StringComparison.Ordinal));
        }

        [Fact]
        public void Nothing_matched_makes_no_layers_rather_than_an_empty_one()
        {
            Assert.Empty(AppearanceTransfer.Layers(null));

            Assert.Empty(AppearanceTransfer.Layers(AppearanceTransfer.Match(Scheme(), null)));
        }

        // -----------------------------------------------------------------------------------
        // The file
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_scheme_survives_the_file()
        {
            var snapshot = Scheme();

            snapshot.SourceModel = @"C:\p\Federation.nwf";
            snapshot.WhenTicks = 637_000_000_000_000_000L;
            snapshot.Note = "Agreed at the Thursday workshop.";

            var read = AppearanceSnapshot.FromJson(JsonReader.Parse(snapshot.ToJson().ToString()));

            Assert.Equal(@"C:\p\Federation.nwf", read.SourceModel);
            Assert.Equal(637_000_000_000_000_000L, read.WhenTicks);
            Assert.Equal("Agreed at the Thursday workshop.", read.Note);

            Assert.Equal(5, read.Entries.Count);
            Assert.Equal(3, read.Coloured);
            Assert.Equal(1, read.Transparent);
            Assert.Equal(1, read.Hidden);

            var wall = read.Entries.Single(e => e.Path == "Model > L01 > Wall A");

            Assert.Equal(Red, wall.Colour);
            Assert.Equal(Key("Wall A"), wall.Key);

            var duct = read.Entries.Single(e => e.Path == "Model > L01 > Duct");

            Assert.Equal(0.5, duct.Transparency!.Value, 6);
            Assert.Null(duct.Colour);

            // And it still lands where it did before the round trip.
            Assert.Equal(4, AppearanceTransfer.Match(read, Target()).Found);
        }

        [Fact]
        public void A_file_from_a_newer_build_is_refused_rather_than_half_read()
        {
            // A snapshot half applied is a model somebody puts back by hand, and the half that was
            // dropped is invisible.
            var future = JsonReader.Parse("{\"schema\":99,\"elements\":[]}");

            Assert.Contains("newer CamelWorks", AppearanceSnapshot.Refuses(future)!,
                            StringComparison.Ordinal);

            Assert.Null(AppearanceSnapshot.Refuses(
                JsonReader.Parse(Scheme().ToJson().ToString())));
        }

        [Fact]
        public void Something_that_is_not_a_snapshot_at_all_is_refused()
        {
            Assert.Contains("nothing in that file", AppearanceSnapshot.Refuses(null)!,
                            StringComparison.OrdinalIgnoreCase);

            Assert.Contains("not an appearance snapshot",
                            AppearanceSnapshot.Refuses(JsonReader.Parse("{\"topics\":[]}"))!,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void An_entry_naming_no_element_is_dropped_rather_than_read_as_a_blank_one()
        {
            var read = AppearanceSnapshot.FromJson(JsonReader.Parse(
                "{\"schema\":1,\"elements\":[{\"colour\":\"#ff0000\"},{\"key\":\"\"}]}"));

            Assert.Empty(read.Entries);
        }

        private static string Name(string? path) =>
            (path ?? string.Empty).Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Last().Trim();

        private static IReadOnlyList<ElementKey> Targeted(AppearanceLayer layer) =>
            layer.Target.Keys;
    }
}
