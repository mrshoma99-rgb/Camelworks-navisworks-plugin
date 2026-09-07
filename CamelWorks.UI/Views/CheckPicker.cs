using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Findings;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Which check a tab is working, as one control.
    ///
    /// A dropdown showing the current check's name and opening a list of every check the session
    /// registered. Not a tab strip and not a combo box: the list is short, an entry has to be able
    /// to say why it cannot be picked, and a menu is what the rest of this product uses for
    /// exactly that.
    ///
    /// Unavailable checks are listed, greyed, with their reason. Leaving them out gives a seat
    /// that cannot offer a check no way to say so, which is reported as the check's results having
    /// disappeared.
    ///
    /// The count beside each entry is read off the findings register rather than by reading the
    /// check, so opening the menu costs nothing: the register is already loaded and a read of the
    /// host is a walk of every test.
    /// </summary>
    public static class CheckPicker
    {
        /// <summary>The picker, for a tab that holds one check at a time.</summary>
        /// <param name="checks">Every check, available or not, in the order the picker lists them.</param>
        /// <param name="current">The one being worked, or null when there is none.</param>
        /// <param name="register">The findings, for the open count beside each entry.</param>
        /// <param name="onPick">Called with the check somebody picked.</param>
        public static UIElement Draw(IReadOnlyList<ICheck> checks, ICheck? current,
                                     FindingRegister? register, Action<ICheck> onPick) =>
            Parts.MenuButton(current?.Name ?? "No check",
                             current == null ? Glyphs.Layers : Ui.CheckSymbol(current.Symbol),
                             Entries(checks, current, register, onPick));

        /// <summary>
        /// The lines the picker holds.
        ///
        /// Separate from the control so the render harness can draw them: a menu lives in a popup,
        /// a popup needs a screen to open on, and until this was split out no picture of this list
        /// existed.
        /// </summary>
        /// <param name="checks">Every check, available or not.</param>
        /// <param name="current">The one being worked, or null.</param>
        /// <param name="register">The findings, for the open count beside each entry.</param>
        /// <param name="onPick">Called with the check somebody picked.</param>
        public static IReadOnlyList<Parts.Entry> Entries(IReadOnlyList<ICheck>? checks,
                                                         ICheck? current,
                                                         FindingRegister? register,
                                                         Action<ICheck>? onPick)
        {
            var lines = new List<Parts.Entry>();

            foreach (var check in checks ?? Array.Empty<ICheck>())
            {
                var one = check;
                var said = Said(one, register);

                // The check's own symbol on every line, including the unavailable ones and the
                // one already showing. One glyph for the whole list left the kinds distinguishable
                // only by their names, which is what this menu is for.
                var symbol = Ui.CheckSymbol(one.Symbol);

                if (!one.IsAvailable)
                {
                    lines.Add(Parts.Entry.Barred(said, symbol,
                                                 one.Unavailable ?? "This seat cannot offer it."));

                    continue;
                }

                var here = current != null
                           && string.Equals(current.Key, one.Key, StringComparison.OrdinalIgnoreCase);

                lines.Add(here
                    ? Parts.Entry.Saying(said, symbol, "already showing", null)
                    : Parts.Entry.Act(said, symbol, () => onPick?.Invoke(one)));
            }

            if (lines.Count == 0)
                lines.Add(Parts.Entry.Barred("No checks", Glyphs.Layers,
                                             "This session registered none."));

            return lines;
        }

        /// <summary>How one entry reads: the check's name, and how much of it is still open.</summary>
        /// <param name="check">The check.</param>
        /// <param name="register">The findings, or null when there are none to count.</param>
        private static string Said(ICheck check, FindingRegister? register)
        {
            var open = register == null
                ? 0
                : register.From(check.Key)
                          .Count(one => one.IsPresent && FindingStatusLattice.IsOpen(one.Status));

            return open == 0
                ? check.Name
                : check.Name + " — " + Ui.Count(open, "open " + check.ResultWord);
        }
    }
}
