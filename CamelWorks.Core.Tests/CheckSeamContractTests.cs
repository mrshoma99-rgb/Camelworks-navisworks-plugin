using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The contract every check has to keep.
    ///
    /// Written against <see cref="FakeCheck"/>, for the reason
    /// <see cref="ClashSeamContractTests"/> is written against <see cref="FakeClashSource"/>: the
    /// seam has no consumer yet, and the rules below are the ones a consumer will get wrong —
    /// scope, run membership, the difference between a first run and an unchanged one, and where
    /// regression is read from.
    /// </summary>
    public class CheckSeamContractTests
    {
        private static FakeCheck Seat() =>
            new FakeCheck()
                .With("run-a", "Level 3")
                .Result("Duct through beam", 0.12)
                .Result("Tray through wall", 0.4)
                .With("run-b", "Level 4")
                .Result("Pipe through slab", 0.05);

        private static ProjectStore Project() =>
            ProjectStore.Open(new InMemoryFileSystem(), @"C:\job\Tower.nwf", null);

        // ---------------------------------------------------------------------------------
        // A check with no capabilities at all
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_check_with_every_capability_null_still_reads_and_its_findings_land()
        {
            var check = Seat();
            check.CanBeRun = false;
            check.CanBeNamed = false;

            Assert.Null(check.HostEcho);
            Assert.Null(check.Grouping);
            Assert.Null(check.Runner);
            Assert.Null(check.Naming);
            Assert.Null(check.Properties);
            Assert.Null(check.Geometry);

            var read = check.Read(null, null);
            var register = new FindingRegister();

            var intake = register.Observe(check.Key, read.Findings, 1, "the fake check", read.Scope);

            Assert.Equal(3, intake.Added);
            Assert.Equal(3, register.Count);
            Assert.Empty(intake.Reopened);
        }

        [Fact]
        public void An_unavailable_check_reads_nothing_and_says_why()
        {
            var check = Seat();
            check.IsAvailable = false;
            check.Unavailable = "this seat has no engine for it.";

            var read = check.Read(null, null);

            Assert.Empty(read.Findings);
            Assert.Empty(read.Runs);
            Assert.Equal("this seat has no engine for it.", Assert.Single(read.Notes));
        }

        [Fact]
        public void A_read_that_is_cancelled_stops_where_it_was_asked_to()
        {
            var check = Seat();
            var everything = check.Read(null, null);
            var read = check.Read(null, seen => seen < 2);

            Assert.Equal("run-a", Assert.Single(read.Runs).Id);
            Assert.Equal(2, read.Findings.Count);

            // The scope has to shrink with it. A cancelled read that still claimed every run would
            // mark the runs it never reached absent, so the run it never started is out of scope.
            Assert.All(read.Findings, finding => Assert.True(read.Scope(finding)));

            var unread = everything.Findings.Single(finding => CheckFacts.IsIn(finding, "run-b"));

            Assert.False(read.Scope(unread));

            var register = new FindingRegister();
            register.Observe(check.Key, everything.Findings, 1, null, everything.Scope);

            Assert.Equal(0, register.Observe(check.Key, read.Findings, 2, null, read.Scope).Absent);
        }

        // ---------------------------------------------------------------------------------
        // Scope, and run membership as a fact
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Reading_one_run_leaves_the_other_runs_findings_present()
        {
            var check = Seat();
            var register = new FindingRegister();
            var everything = check.Read(null, null);

            register.Observe(check.Key, everything.Findings, 1, null, everything.Scope);

            var one = check.Read(new[] { "run-a" }, null);
            var intake = register.Observe(check.Key, one.Findings, 2, null, one.Scope);

            Assert.Equal(0, intake.Absent);
            Assert.Equal(3, register.Present.Count());

            // And a run that really did stop reporting something is still marked absent, so the
            // scope is a scope and not a blanket.
            var narrowed = new CheckRead(one.Runs, Array.Empty<Finding>(), one.Scope, null);

            Assert.Equal(2, register.Observe(check.Key, narrowed.Findings, 3, null, narrowed.Scope).Absent);
            Assert.Single(register.Present);
        }

        [Fact]
        public void A_finding_in_two_runs_is_read_once_and_is_not_marked_absent_by_either_alone()
        {
            var check = new FakeCheck()
                .With("run-a")
                .Result("One physical conflict", 0.2)
                .AlsoIn("run-b")
                .With("run-b");

            var finding = Assert.Single(check.Findings);

            Assert.Equal(new[] { "run-a", "run-b" }, CheckFacts.RunsOf(finding));

            var both = check.Read(null, null);

            Assert.Single(both.Findings);
            Assert.Equal(1, both.CountFor("run-a"));
            Assert.Equal(1, both.CountFor("run-b"));

            var register = new FindingRegister();
            register.Observe(check.Key, both.Findings, 1, null, both.Scope);

            // A read of one run reports every result naming that run, wherever the fixture holds
            // it, because a result a read leaves out is a result reported gone.
            var one = check.Read(new[] { "run-a" }, null);

            Assert.Single(one.Findings);
            Assert.Equal(0, register.Observe(check.Key, one.Findings, 2, null, one.Scope).Absent);
            Assert.Single(register.Present);

            // And a read that reports nothing for run-b still cannot mark it absent: the finding
            // names run-a as well, and a read of run-b alone does not answer for run-a.
            var empty = new CheckRead(one.Runs, Array.Empty<Finding>(),
                                      CheckFacts.ScopeOf(new[] { "run-b" }), null);

            Assert.Equal(0, register.Observe(check.Key, empty.Findings, 3, null, empty.Scope).Absent);
            Assert.Single(register.Present);

            // A finding naming only the run that was read is marked absent, so the scope is a scope
            // and not a blanket.
            var single = new FakeCheck().With("run-b").Result("Only in run-b");
            var read = single.Read(null, null);

            register.Observe(single.Key, read.Findings, 4, null, read.Scope);

            Assert.Equal(1, register.Observe(single.Key, Array.Empty<Finding>(), 5, null,
                                             read.Scope).Absent);
        }

        [Fact]
        public void A_finding_naming_no_run_is_never_marked_absent_by_a_partial_read()
        {
            var check = Seat();
            var loose = Finding.Create(check.Key, "Level 3", "Raised by hand",
                                       Array.Empty<CamelWorks.Core.Identity.ElementKey>(), "by hand");

            var register = new FindingRegister();
            register.Put(loose);

            var read = check.Read(new[] { "run-a" }, null);

            Assert.False(read.Scope(loose));
            Assert.Equal(0, register.Observe(check.Key, read.Findings, 1, null, read.Scope).Absent);
            Assert.True(register.Find(loose.Id)!.IsPresent);
        }

        [Fact]
        public void Run_membership_is_replaced_rather_than_added_to()
        {
            var finding = Assert.Single(new FakeCheck().With("run-a").Result("One").Findings);

            CheckFacts.SetRuns(finding, new[] { "run-b", "run-b", " run-c " });

            Assert.Equal(new[] { "run-b", "run-c" }, CheckFacts.RunsOf(finding));

            CheckFacts.SetRuns(finding, Array.Empty<string>());

            Assert.Empty(CheckFacts.RunsOf(finding));
            Assert.False(finding.Facts.ContainsKey(CheckFacts.Runs));
        }

        // ---------------------------------------------------------------------------------
        // The registry
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_catalogue_finds_a_check_by_key_and_refuses_two_under_one_key()
        {
            var catalogue = new CheckCatalogue();
            var check = Seat();

            catalogue.Add(check);
            catalogue.Add(new FakeCheck("Other", "Other check") { IsAvailable = false });

            Assert.Same(check, catalogue.Find("Fake"));
            Assert.Null(catalogue.Find("nothing"));
            Assert.Equal(2, catalogue.All.Count);
            Assert.Same(check, Assert.Single(catalogue.Available));

            Assert.Throws<ArgumentException>(() => catalogue.Add(new FakeCheck()));
        }

        [Fact]
        public void An_empty_catalogue_is_an_ordinary_state()
        {
            var catalogue = new CheckCatalogue();

            Assert.Empty(catalogue.All);
            Assert.Empty(catalogue.Available);
            Assert.Null(catalogue.Find(null));
        }

        // ---------------------------------------------------------------------------------
        // Columns
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_declared_column_reads_a_typed_value_rather_than_a_fact_string()
        {
            var check = Seat();
            var finding = check.Findings[1];
            var number = check.Columns.Single(column => column.Kind == ColumnKind.Number);

            Assert.Equal(0.4, number.Number!(finding));
            Assert.Equal("0.4", number.Show(finding));

            number.Format = metres => metres.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                                     + " m";

            Assert.Equal("0.400 m", number.Show(finding));

            var text = check.Columns.Single(column => column.Kind == ColumnKind.Text);

            Assert.Equal("Tray through wall", text.Show(finding));
        }

        // ---------------------------------------------------------------------------------
        // The saved runs, and the section they share with the journals
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_saved_run_round_trips_including_a_settings_key_this_build_does_not_know()
        {
            var spec = new CheckRunSpec("Fake", "run-a", "Level 3") { Folder = "Weekly", LastRunTicks = 42 };

            spec.Settings["minimum"] = "2.1";
            spec.Settings["somethingLater"] = "kept";
            spec.Settings["cleared"] = null;

            var store = new CheckRunStore();
            store.Put(spec);

            var again = CheckRunStore.FromJson(store.ToJson());
            var read = Assert.Single(again.For("Fake"));

            Assert.Equal("Level 3", read.Name);
            Assert.Equal("Weekly", read.Folder);
            Assert.Equal(42, read.LastRunTicks);
            Assert.Equal("2.1", read.Settings["minimum"]);
            Assert.Equal("kept", read.Settings["somethingLater"]);
            Assert.Null(read.Settings["cleared"]);
        }

        [Fact]
        public void Putting_a_run_twice_replaces_it_and_removing_it_forgets_it()
        {
            var store = new CheckRunStore();

            store.Put(new CheckRunSpec("Fake", "run-a", "First"));
            store.Put(new CheckRunSpec("Fake", "run-a", "Second"));
            store.Put(new CheckRunSpec("Other", "run-a", "Another check's"));

            Assert.Equal(2, store.Count);
            Assert.Equal("Second", store.Find("Fake", "run-a")!.Name);

            Assert.True(store.Remove("Fake", "run-a"));
            Assert.False(store.Remove("Fake", "run-a"));
            Assert.Single(store.All);
        }

        [Fact]
        public void Writing_the_saved_runs_leaves_the_journals_in_the_same_section_untouched()
        {
            var store = Project();

            new RunJournal("Fake", "run-a", 7,
                           new Dictionary<string, FindingStatus> { ["f1"] = FindingStatus.Active },
                           new Dictionary<string, int> { ["Mechanical"] = 1 }).Write(store);

            var journals = store.Section(CheckRunStore.Section)[RunJournal.Key].ToJson();

            var runs = new CheckRunStore();
            runs.Put(new CheckRunSpec("Fake", "run-a", "Level 3"));
            runs.Write(store);

            Assert.Equal(journals, store.Section(CheckRunStore.Section)[RunJournal.Key].ToJson());
            Assert.Single(CheckRunStore.Read(store).All);

            // And the other way round: journalling again must not disturb the saved runs.
            var written = store.Section(CheckRunStore.Section)[CheckRunStore.Key].ToJson();

            RunJournal.Of("Fake", "run-a", 8, Seat().Findings).Write(store);

            Assert.Equal(written, store.Section(CheckRunStore.Section)[CheckRunStore.Key].ToJson());
        }

        [Fact]
        public void A_run_written_out_of_a_record_that_names_no_check_is_left_out_with_a_note()
        {
            var notes = new List<string>();

            var store = CheckRunStore.FromJson(
                JsonValue.Array(new[] { JsonValue.Object().Set("name", "nameless") }), notes);

            Assert.Empty(store.All);

            // Read rather than counted: two notes that both said "unreadable" would pass a count
            // and tell a reader nothing about which record went and why.
            Assert.Contains(notes, note => note.Contains("named no check", StringComparison.Ordinal));
            Assert.Contains(notes,
                            note => note.Contains("1 saved run(s) could not be read",
                                                  StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------------------------
        // The journal
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_journal_records_the_statuses_and_the_parties_and_reads_back()
        {
            var check = new FakeCheck()
                .With("run-a")
                .Result("Open one").AssignedTo("Mechanical")
                .Result("Settled one", status: FindingStatus.Approved).AssignedTo("Structures");

            var journal = RunJournal.Of("Fake", "run-a", 99, check.Findings);

            Assert.Equal(2, journal.Count);
            Assert.Equal(1, journal.OpenByParty["Mechanical"]);
            Assert.False(journal.OpenByParty.ContainsKey("Structures"));

            var again = RunJournal.FromJson(journal.ToJson())!;

            Assert.Equal("run-a", again.RunId);
            Assert.Equal(99, again.WhenTicks);
            Assert.Equal(journal.Statuses, again.Statuses);
            Assert.Equal(1, again.OpenByParty["Mechanical"]);
        }

        [Fact]
        public void Two_journals_are_kept_and_a_third_replaces_the_older_one()
        {
            var store = Project();

            Journal("first").Write(store);
            Assert.Null(RunJournal.Previous(store, "Fake", "run-a"));

            Journal("second").Write(store);
            Journal("third").Write(store);

            Assert.Equal(new[] { "third" }, RunJournal.Current(store, "Fake", "run-a")!.Statuses.Keys);
            Assert.Equal(new[] { "second" }, RunJournal.Previous(store, "Fake", "run-a")!.Statuses.Keys);
        }

        [Fact]
        public void A_run_that_has_never_run_has_no_journal_rather_than_an_empty_one()
        {
            Assert.Null(RunJournal.Current(Project(), "Fake", "run-a"));
            Assert.Null(RunJournal.FromJson(null));
            Assert.Null(RunJournal.FromJson(JsonValue.Object().Set("run", "run-a")));
        }

        // ---------------------------------------------------------------------------------
        // The delta
        // ---------------------------------------------------------------------------------

        [Fact]
        public void No_journal_reads_as_a_first_run_and_an_empty_journal_reads_as_everything_new()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var first = CheckDelta.Between(null, intake, register, read.Scope, check.Aliases);

            Assert.True(first.NoPreviousRun);
            Assert.Empty(first.New);
            Assert.Empty(first.Gone);
            Assert.Empty(first.Persisting);
            Assert.Contains("No previous run recorded", first.Explain(), StringComparison.Ordinal);

            var empty = new RunJournal("Fake", "run-a", 1, null, null);
            var against = CheckDelta.Between(empty, intake, register, read.Scope, check.Aliases);

            Assert.False(against.NoPreviousRun);
            Assert.Equal(3, against.New.Count);
            Assert.Empty(against.Gone);
        }

        [Fact]
        public void A_finding_the_journal_held_and_the_run_still_reports_is_persisting_not_new()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var journal = RunJournal.Of("Fake", "run-a", 1, read.Findings.Take(2).ToList());
            var delta = CheckDelta.Between(journal, intake, register, read.Scope, check.Aliases);

            Assert.Equal(2, delta.Persisting.Count);
            Assert.Single(delta.New);
            Assert.Empty(delta.Gone);
            Assert.Equal(0, delta.MatchedOnAlias);
        }

        [Fact]
        public void A_finding_the_journal_held_that_nothing_answers_to_is_gone()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var journal = new RunJournal("Fake", "run-a", 1,
                new Dictionary<string, FindingStatus> { ["went-away"] = FindingStatus.Active }, null);

            var delta = CheckDelta.Between(journal, intake, register, read.Scope, check.Aliases);

            Assert.Equal(new[] { "went-away" }, delta.Gone);
            Assert.Equal(3, delta.New.Count);
            Assert.False(delta.IsQuiet);
        }

        [Fact]
        public void A_check_offering_aliases_matches_a_finding_whose_id_moved_and_counts_it()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var moved = read.Findings[0];

            // What a re-exported model does to a clash: the id carries the quantised position, so
            // the same physical conflict comes back under a neighbouring cell's id.
            var journal = new RunJournal("Fake", "run-a", 1,
                new Dictionary<string, FindingStatus> { ["neighbour-of-" + moved.Id] = FindingStatus.Active },
                null);

            check.AliasSource = finding =>
                finding.Id == moved.Id ? new[] { "neighbour-of-" + moved.Id } : Array.Empty<string>();

            var delta = CheckDelta.Between(journal, intake, register, read.Scope, check.Aliases);

            Assert.Equal(new[] { moved.Id }, delta.Persisting);
            Assert.Equal(1, delta.MatchedOnAlias);
            Assert.Empty(delta.Gone);
            Assert.Contains("the model has moved", delta.Explain(), StringComparison.Ordinal);

            // Without the aliases the same journal reports it gone and new at once, which is the
            // failure the aliases exist to prevent.
            var blind = CheckDelta.Between(journal, intake, register, read.Scope, null);

            Assert.Single(blind.Gone);
            Assert.Equal(0, blind.MatchedOnAlias);
        }

        [Fact]
        public void An_alias_is_claimed_once_and_by_the_finding_that_offers_it()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var journal = new RunJournal("Fake", "run-a", 1,
                new Dictionary<string, FindingStatus> { ["one-old-id"] = FindingStatus.Active }, null);

            var first = read.Findings.Select(finding => finding.Id)
                            .OrderBy(id => id, StringComparer.Ordinal).First();

            check.AliasSource = _ => new[] { "one-old-id" };

            var delta = CheckDelta.Between(journal, intake, register, read.Scope, check.Aliases);

            // Which one took it, not just how many did. All three offer it and the journal holds
            // it once, so the first by id claims it and the other two are new.
            Assert.Equal(new[] { first }, delta.Persisting);
            Assert.Equal(2, delta.New.Count);
            Assert.Equal(1, delta.MatchedOnAlias);
            Assert.Empty(delta.Gone);
        }

        [Fact]
        public void An_exact_match_keeps_its_id_against_a_finding_that_offers_it_as_an_alias()
        {
            // Matching in one pass lets a finding that sorts earlier take, through an alias, the id
            // another finding owns exactly. The finding that genuinely persisted then reads as new,
            // and the alias match reports the model as having moved when it has not — with the
            // winner decided by the order the register happens to enumerate.
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);
            var intake = register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var ids = read.Findings.Select(finding => finding.Id)
                          .OrderBy(id => id, StringComparer.Ordinal).ToList();

            var owner = ids[ids.Count - 1];   // sorts last, so a one-pass match reaches it last
            var offering = ids[0];

            var journal = new RunJournal("Fake", "run-a", 1,
                new Dictionary<string, FindingStatus> { [owner] = FindingStatus.Active }, null);

            // Every other finding offers the owner's id as one of its aliases.
            check.AliasSource = finding =>
                finding.Id == owner ? Array.Empty<string>() : new[] { owner };

            var delta = CheckDelta.Between(journal, intake, register, read.Scope, check.Aliases);

            Assert.Equal(new[] { owner }, delta.Persisting);
            Assert.Contains(offering, delta.New);
            Assert.Equal(0, delta.MatchedOnAlias);
            Assert.Empty(delta.Gone);
        }

        [Fact]
        public void Regression_is_read_from_the_intake_and_is_empty_when_the_intake_reports_none()
        {
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);

            var settled = read.Findings[0];
            var quiet = CheckDelta.Between(RunJournal.Of("Fake", "run-a", 1, read.Findings),
                                           register.Observe(check.Key, read.Findings, 1, null, read.Scope),
                                           register, read.Scope, check.Aliases);

            Assert.Empty(quiet.Regressed);

            // Somebody approves it, and the next run reports it again. The register does not demote
            // it — the lattice promotes only — so the intake is the only place that knows.
            register.Find(settled.Id)!.Status = FindingStatus.Approved;

            var second = check.Read(null, null);
            var intake = register.Observe(check.Key, second.Findings, 2, null, second.Scope);

            Assert.Equal(new[] { settled.Id }, intake.Reopened);
            Assert.Equal(FindingStatus.Approved, register.Find(settled.Id)!.Status);

            var delta = CheckDelta.Between(RunJournal.Of("Fake", "run-a", 1, read.Findings),
                                           intake, register, second.Scope, check.Aliases);

            Assert.Equal(new[] { settled.Id }, delta.Regressed);
            Assert.Contains("reported again after being closed", delta.Explain(), StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------------------------
        // Running and naming, where a check offers them
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_renamed_run_comes_back_renamed_and_a_blank_name_is_refused()
        {
            var check = Seat();
            var naming = check.Naming!;

            Assert.True(naming.Rename("run-a", "Level 3 revised"));
            Assert.Equal("Level 3 revised", check.Runs()[0].Name);

            Assert.False(naming.Rename("run-a", "   "));
            Assert.False(naming.Rename("not-a-run", "Anything"));
            Assert.Equal("Level 3 revised", check.Runs()[0].Name);

            Assert.True(naming.Refile("run-a", "Weekly"));
            Assert.Equal("Weekly", check.Runs()[0].Folder);
            Assert.True(naming.Refile("run-a", "  "));
            Assert.Null(check.Runs()[0].Folder);

            Assert.False(naming.HasBook);
        }

        [Fact]
        public void Deleting_a_run_stops_it_being_read_and_compacting_keeps_the_run()
        {
            var check = Seat();
            var runner = check.Runner!;

            Assert.True(runner.Compact(new[] { "run-a" }));
            Assert.Equal(2, check.Runs().Count);
            Assert.Single(check.Read(null, null).Findings);

            Assert.True(runner.Delete(new[] { "run-b" }));
            Assert.Single(check.Runs());
            Assert.Empty(check.Read(null, null).Findings);

            Assert.False(runner.Delete(new[] { "run-b" }));
        }

        [Fact]
        public void A_check_that_cannot_run_says_why_rather_than_offering_a_button()
        {
            var check = Seat();
            check.IsAvailable = false;
            check.Unavailable = "there is no engine on this seat.";

            Assert.False(check.CanRun);
            Assert.Equal("there is no engine on this seat.", check.CannotRun);
            Assert.False(check.Run("run-a", null));
        }

        [Fact]
        public void Severity_is_the_checks_own_measure_and_it_words_its_own_bands()
        {
            var check = Seat();
            var finding = check.Findings[0];

            Assert.Null(check.Severity(finding));
            Assert.Equal("not measured", check.SeverityBand(null));

            check.SeverityOf = one => CheckFacts.RunsOf(one).Count > 0 ? 1.5 : (double?)null;

            Assert.Equal(1.5, check.Severity(finding));
            Assert.Equal("past", check.SeverityBand(check.Severity(finding)));
        }

        [Fact]
        public void Two_reads_of_the_same_results_derive_the_same_ids()
        {
            // A check builds its findings from whatever its engine reported this time. The ids have
            // to come back the same or every status, assignee, comment and issue attachment is
            // detached on the next read, so the fixture builds fresh objects too: a fake that
            // handed the same objects back would be the one check at this seam whose identity rule
            // was never exercised.
            var check = Seat();

            var first = check.Read(null, null);
            var second = check.Read(null, null);

            Assert.All(first.Findings.Zip(second.Findings, (one, two) => (one, two)),
                       pair => Assert.NotSame(pair.one, pair.two));

            Assert.Equal(first.Findings.Select(finding => finding.Id),
                         second.Findings.Select(finding => finding.Id));

            // And a read lands on the findings the register already holds rather than beside them.
            var register = new FindingRegister();

            register.Observe(check.Key, first.Findings, 1, null, first.Scope);

            var again = register.Observe(check.Key, second.Findings, 2, null, second.Scope);

            Assert.Equal(0, again.Added);
            Assert.Equal(3, register.Count);
        }

        [Fact]
        public void Regression_is_scoped_to_the_run_the_delta_is_for()
        {
            // The intake covers whatever the read covered. A delta for one run that passed it
            // through would report every run's reopenings on every run's line.
            var check = Seat();
            var register = new FindingRegister();
            var read = check.Read(null, null);

            register.Observe(check.Key, read.Findings, 1, null, read.Scope);

            var inB = read.Findings.Single(finding => CheckFacts.IsIn(finding, "run-b"));
            var inA = read.Findings.First(finding => CheckFacts.IsIn(finding, "run-a"));

            register.Find(inA.Id)!.Status = FindingStatus.Approved;
            register.Find(inB.Id)!.Status = FindingStatus.Approved;

            var again = check.Read(null, null);
            var intake = register.Observe(check.Key, again.Findings, 2, null, again.Scope);

            Assert.Equal(2, intake.Reopened.Count);

            var justB = CheckDelta.Between(RunJournal.Of("Fake", "run-b", 1, read.Findings),
                                           intake, register, CheckFacts.ScopeOf(new[] { "run-b" }),
                                           check.Aliases);

            Assert.Equal(new[] { inB.Id }, justB.Regressed);
        }

        [Fact]
        public void A_slot_key_cannot_be_confused_with_another_checks()
        {
            // Neither half is validated for the separator, and a host names its tests whatever the
            // user typed. Joined raw, these two write to one slot and each reads the other's
            // journal.
            Assert.NotEqual(RunJournal.Slot("a", "b/c"), RunJournal.Slot("a/b", "c"));

            var store = Project();

            new RunJournal("a", "b/c", 1,
                           new Dictionary<string, FindingStatus> { ["one"] = FindingStatus.Active },
                           null).Write(store);

            new RunJournal("a/b", "c", 2,
                           new Dictionary<string, FindingStatus> { ["two"] = FindingStatus.Active },
                           null).Write(store);

            Assert.Equal(new[] { "one" }, RunJournal.Current(store, "a", "b/c")!.Statuses.Keys);
            Assert.Equal(new[] { "two" }, RunJournal.Current(store, "a/b", "c")!.Statuses.Keys);
            Assert.Null(RunJournal.Previous(store, "a", "b/c"));
        }

        private static RunJournal Journal(string findingId) =>
            new RunJournal("Fake", "run-a", 1,
                           new Dictionary<string, FindingStatus> { [findingId] = FindingStatus.Active },
                           null);
    }
}
