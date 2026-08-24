using System;
using System.IO;
using System.Linq;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SelfTestTests
    {
        [Fact]
        public void Everything_passes_on_a_machine_where_nothing_is_wrong()
        {
            var report = SelfTest.Run();

            // If this ever fails on CI it is not the self-test that is broken. It is the thing the
            // check is about, and the detail says which.
            Assert.True(report.IsClean,
                        "the self-test found something wrong on CI:" + Environment.NewLine + report.ToText());

            Assert.Equal(report.Results.Count, report.Passed);
            Assert.Empty(report.Failures);
        }

        [Fact]
        public void It_covers_every_seam_to_the_platform()
        {
            var areas = SelfTest.Run().Results.Select(r => r.Area).Distinct().ToList();

            // Numbers and storage are the two that actually differ machine to machine; the writers
            // are here because a replaced compression or text stack breaks them silently.
            Assert.Contains("Numbers", areas);
            Assert.Contains("Storage", areas);
            Assert.Contains("Report", areas);
            Assert.Contains("openBIM", areas);
        }

        [Fact]
        public void Every_check_says_what_it_found_and_not_only_whether_it_passed()
        {
            foreach (var result in SelfTest.Run().Results)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Name), result.Area + " has a check with no name");
                Assert.False(string.IsNullOrWhiteSpace(result.Detail),
                             result.Area + " / " + result.Name + " says nothing about what it found");
            }
        }

        [Fact]
        public void Without_a_folder_the_disk_check_says_it_did_not_run_rather_than_passing_quietly()
        {
            var storage = SelfTest.Run().Results.Single(r => r.Area == "Storage");

            Assert.Contains("not run", storage.Detail);
        }

        [Fact]
        public void With_a_folder_it_writes_replaces_and_reads_back_and_leaves_nothing_behind()
        {
            var folder = Path.Combine(Path.GetTempPath(), "camelworks-selftest-" + Guid.NewGuid().ToString("N"));

            try
            {
                var report = SelfTest.Run(folder);
                var storage = report.Results.Single(r => r.Area == "Storage");

                Assert.True(storage.Passed, storage.Detail);
                Assert.Contains("backup path included", storage.Detail);

                // No litter. A self-test that leaves files behind is one somebody stops running.
                Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0,
                            "the self-test left files in " + folder);
            }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (IOException) { }
            }
        }

        [Fact]
        public void A_folder_it_cannot_write_to_is_a_failure_and_not_a_crash()
        {
            var impossible = Path.Combine(Path.GetTempPath(),
                                          "camelworks-selftest-nul\0-" + Guid.NewGuid().ToString("N"));

            var report = SelfTest.Run(impossible);
            var storage = report.Results.Single(r => r.Area == "Storage");

            Assert.False(storage.Passed);
            Assert.False(report.IsClean);
            Assert.Single(report.Failures);

            // Everything else still ran. A run that stopped at the first failure would hide the
            // rest, which is the opposite of what a support call needs.
            Assert.True(report.Passed > 5, report.Explain());
        }

        [Fact]
        public void The_readout_is_something_a_person_can_paste_into_a_bug_report()
        {
            var report = SelfTest.Run();
            var text = report.ToText();

            Assert.StartsWith("CamelWorks self-test", text);
            Assert.Contains(report.Explain(), text);
            Assert.Contains("checks passed", report.Explain());
            Assert.Contains("ms", report.Explain());

            foreach (var result in report.Results)
                Assert.Contains(result.Name, text);
        }
    }
}
