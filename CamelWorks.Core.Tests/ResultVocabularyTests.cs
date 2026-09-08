using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Search;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What a field can be set to, from one place.
    ///
    /// The point of the class is that four screens stop deriving this for themselves, so the
    /// cases worth pinning are the ones they disagreed on: whether a value the project has
    /// stopped declaring still appears, and whether a list that is a ranking gets sorted.
    /// </summary>
    public class ResultVocabularyTests
    {
        private static Finding Found(string name) =>
            Finding.Create(FindingSource.Clash, "Hard clash", name,
                           Array.Empty<ElementKey>(), discriminator: name);

        [Fact]
        public void A_status_list_keeps_the_lattices_order_rather_than_being_sorted()
        {
            var said = ResultVocabulary.Of(ResultFields.Status, null);

            Assert.Equal(new[] { "New", "Active", "Reviewed", "Approved", "Resolved" }, said);
        }

        [Fact]
        public void A_priority_the_project_declares_comes_before_one_only_a_result_carries()
        {
            var profile = ProjectProfile.Derive(Array.Empty<Abstractions.IModelSource>());

            profile.Find(ProfileKeys.Priorities)!.Override = "Critical, High, Low";

            var one = Found("a");
            var two = Found("b");

            one.Priority = "High";

            // Arrived in somebody's markup and never declared here.
            two.Priority = "Blocker";

            var said = ResultVocabulary.Of(ResultFields.Priority, new[] { one, two }, profile);

            // The declared ranking in its own order, then what the board adds.
            Assert.Equal(new[] { "Critical", "High", "Low", "Blocker" }, said);
        }

        [Fact]
        public void A_group_is_whatever_the_results_carry_and_nothing_invented()
        {
            var one = Found("a");
            var two = Found("b");
            var three = Found("c");

            one.Group = "Risers";
            two.Group = "risers";
            three.Group = "  ";

            // Once each, case-insensitively, in order, with the empty one left out.
            Assert.Equal(new[] { "Risers" },
                         ResultVocabulary.Of(ResultFields.Group, new[] { one, two, three }));
        }

        [Fact]
        public void A_free_text_field_offers_nothing_rather_than_guessing()
        {
            Assert.Empty(ResultVocabulary.Of(ResultFields.Title, new[] { Found("a") }));
            Assert.Empty(ResultVocabulary.Of(ResultFields.Run, new[] { Found("a") }));
        }

        [Fact]
        public void No_results_and_no_project_is_an_empty_list_rather_than_a_throw()
        {
            foreach (var field in new[] { ResultFields.Priority, ResultFields.Assignee,
                                          ResultFields.Group, ResultFields.Label })
                Assert.Empty(ResultVocabulary.Of(field, null));
        }
    }
}
