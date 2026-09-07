using System;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The one way to save work that spans the findings sidecar and the project file.
    ///
    /// An issue in the project file names finding ids, and the findings are in their own file
    /// beside it. Saving the two through separate calls with separate failure paths is how an
    /// issue comes to name a finding that is nowhere: several call sites did one write and not the
    /// other, and a refused sidecar write was reported as a note while the issue was written
    /// anyway.
    ///
    /// There is no cross-file transaction here and none is claimed: these are two files and two
    /// <see cref="Store.AtomicFile"/> writes. What is guaranteed is the order and the refusals.
    /// The window that survives a crash between the two writes leaves a finding nothing points at,
    /// which the register keeps by design; the other order would leave an issue pointing at
    /// nothing.
    /// </summary>
    public static class WorkSave
    {
        /// <summary>
        /// Save the findings sidecar and the project file, in that order.
        ///
        /// Refuses before writing anything when either file would not take a write, so a refused
        /// project file does not leave a rewritten sidecar behind it. Returns false with the
        /// reason rather than throwing: a failed save is a line on a pane.
        /// </summary>
        /// <param name="store">The project file.</param>
        /// <param name="findings">The findings sidecar beside it.</param>
        /// <param name="problem">Why nothing was saved, or null when both writes landed.</param>
        public static bool Save(ProjectStore store, FindingStore findings, out string? problem)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (findings == null) throw new ArgumentNullException(nameof(findings));

            problem = store.WhySaveRefused;

            if (problem != null) return false;

            if (findings.IsMemoryOnly)
            {
                problem = "there is nowhere to save findings to — " + findings.Where;
                return false;
            }

            // The sidecar first. A project file written over a sidecar that then refused would
            // name findings this build cannot read back.
            if (!findings.Save())
            {
                problem = findings.LastSaveProblem ?? "the findings could not be written.";
                return false;
            }

            if (!store.Save())
            {
                problem = store.LastSaveProblem ?? "the project file could not be written.";
                return false;
            }

            problem = null;
            return true;
        }
    }
}
