using System;
using System.Windows;
using System.Windows.Interop;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// Ties a WPF window to the Navisworks frame it belongs to.
    ///
    /// <b>Why every window has to go through here.</b> A plug-in's windows are WPF; Navisworks is
    /// a native application with its own message loop. A WPF window created inside it with no
    /// owner is a top-level window of the same thread that the host knows nothing about, and two
    /// things go wrong with that:
    ///
    /// <list type="number">
    /// <item><description>
    /// <b>A modal dialog takes the host down when it closes.</b> <c>ShowDialog</c> with no owner
    /// disables every top-level window on the thread — including the host's own native frame — and
    /// re-enables them from the owner it does not have. What the user sees is a settings window
    /// they closed and a Navisworks that never comes back. This is the bug this file exists for.
    /// </description></item>
    /// <item><description>
    /// <b>The window goes behind the model.</b> With no owner it is not kept above the frame, so
    /// alt-tabbing away and back leaves a settings dialog somebody has to hunt for behind a 3D
    /// view.
    /// </description></item>
    /// </list>
    ///
    /// And if the host has a WPF <c>Application</c> of its own, its default shutdown mode ends the
    /// process when the last window closes. Ours must never be able to count as that last window,
    /// so the mode is pinned the first time we show anything.
    ///
    /// Everything here is defensive: a host that will not give up its handle is worth one window
    /// that behaves the old way, not an exception on the way to a screen.
    /// </summary>
    public static class HostWindow
    {
        private static bool _pinned;
        private static Window? _last;
        private static IntPtr _frame;

        /// <summary>
        /// The last window this product showed, for a dialog that needs a WPF owner.
        ///
        /// A file dialog is a native modal, and one with no owner disables every top-level window
        /// on the thread and re-enables them from the owner it does not have — which is the same
        /// bug this class was written for, arriving through a different door. Held weakly so a
        /// closed window is not kept alive by having been the most recent one.
        /// </summary>
        public static Window? Active =>
            _last != null && _last.IsLoaded ? _last : null;

        /// <summary>
        /// Give a window the Navisworks frame as its owner, and pin the shutdown mode.
        ///
        /// Call it before <c>Show</c> or <c>ShowDialog</c> on every window this product creates.
        /// </summary>
        /// <param name="window">The window about to be shown.</param>
        /// <param name="topLevel">
        /// True for a window that stands beside the others rather than over one of them - an app
        /// window. Those take the host frame as their owner, so two of them can be brought in
        /// front of each other freely.
        ///
        /// Everything else - a settings window, a picker, anything shown over the screen that
        /// opened it - takes that screen as its owner, which is what keeps it in front of it. It
        /// is the default deliberately: a dialog owned by the host frame is a sibling of the app
        /// window rather than its child, so clicking the app window put the settings behind it and
        /// left somebody hunting for a window they had just opened. Defaulting the other way means
        /// the one kind that must opt out is the handful of app windows, not the dozens of
        /// dialogs.
        /// </param>
        public static void Own(Window? window, bool topLevel = false)
        {
            if (window == null) return;

            Pin();

            _last = window;
            window.Closed += (s, e) => { if (ReferenceEquals(_last, window)) _last = null; };

            // Two defensive lines about the pointer, both cheap and neither able to make anything
            // worse. A host runs a plug-in command with a wait cursor up, and a window opened
            // during one can be left showing it: the override is application-wide and static, so a
            // stale one follows the pointer onto every window afterwards. And a window with no
            // cursor of its own resolves whatever the chain above it says, which inside a native
            // frame is not ours to predict. Children that ask for their own - a hand over a link -
            // still win, because this is the window's cursor and not a forced one.
            try
            {
                if (System.Windows.Input.Mouse.OverrideCursor != null)
                    System.Windows.Input.Mouse.OverrideCursor = null;

                window.Cursor = System.Windows.Input.Cursors.Arrow;
            }
            catch (Exception)
            {
                // A pointer that will not be set is worth one odd-looking cursor, not an
                // exception on the way to a screen.
            }

            try
            {
                // The screen this was opened from, when there is one and it is not this window.
                // A real WPF owner, so the host keeps it above its parent and takes it away with
                // it - which the interop handle below cannot do, since the frame is every app
                // window's owner too and they are therefore all siblings.
                if (!topLevel)
                {
                    var over = Active;

                    if (over != null && !ReferenceEquals(over, window))
                    {
                        window.Owner = over;
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // A window that will not take an owner falls through to the frame below.
            }

            try
            {
                var handle = Handle();

                if (handle == IntPtr.Zero) return;

                // Set through the interop helper rather than Window.Owner: the frame is a native
                // window, not a WPF one, so there is no Window instance to hand over.
                var helper = new WindowInteropHelper(window);

                if (helper.Owner == IntPtr.Zero) helper.Owner = handle;
            }
            catch (Exception)
            {
                // A host that will not say where its frame is gets an unowned window, which is
                // what every window in this product had until now.
            }
        }

        /// <summary>
        /// Stop the host's WPF application from treating our last closed window as its own.
        ///
        /// Only when there is an application: showing a WPF window from a native host usually
        /// leaves <c>Application.Current</c> null, and nothing needs pinning in that case.
        /// </summary>
        private static void Pin()
        {
            if (_pinned) return;

            _pinned = true;

            try
            {
                if (Application.Current != null)
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            catch (Exception)
            {
                // An application mid-shutdown refuses the change, and there is nothing useful to
                // do about that from here.
            }
        }

        /// <summary>
        /// The Navisworks frame as something a native dialog can be owned by, or null.
        ///
        /// For the message boxes that are shown on the way to a screen that never opened: they are
        /// native modals, and a native modal with no owner disables the host's own frame and
        /// re-enables it from an owner it does not have.
        /// </summary>
        public static System.Windows.Forms.IWin32Window? Frame()
        {
            var handle = Handle();

            return handle == IntPtr.Zero ? null : new FrameHandle(handle);
        }

        /// <summary>The host frame, as WinForms wants to be handed a window.</summary>
        private sealed class FrameHandle : System.Windows.Forms.IWin32Window
        {
            internal FrameHandle(IntPtr handle) => Handle = handle;

            /// <inheritdoc />
            public IntPtr Handle { get; }
        }

        /// <summary>
        /// The Navisworks main frame's window handle, or zero.
        ///
        /// <b>Why it is remembered.</b> The API's <c>Gui</c> is not answerable at every moment of
        /// the host's life - with no document open it can be null or throw - and a zero here does
        /// not mean "no frame", it means "not now". Taking it at face value made every window
        /// opened before the first model an unowned one, which is exactly the two failures this
        /// class exists to prevent: an app window that goes behind the frame and reads as never
        /// having opened, and the next modal after it disabling the host and re-enabling it from
        /// an owner it does not have - a grey, dead Navisworks with no cursor. The frame outlives
        /// every document, so the first real answer is kept and the process's own main window
        /// stands in until there is one.
        /// </summary>
        private static IntPtr Handle()
        {
            if (_frame != IntPtr.Zero) return _frame;

            try
            {
                var gui = Autodesk.Navisworks.Api.Application.Gui;
                var main = gui?.MainWindow;

                _frame = main?.Handle ?? IntPtr.Zero;
            }
            catch (Exception)
            {
                // No document, no GUI, or a headless host. Only the last of those really means
                // there is no frame, and the fallback below tells them apart.
                _frame = IntPtr.Zero;
            }

            if (_frame != IntPtr.Zero) return _frame;

            try
            {
                // The plug-in runs inside Navisworks, so the process's main window is the frame
                // whenever the API will not say so. Headless, it is zero, which is the honest
                // answer there.
                using (var self = System.Diagnostics.Process.GetCurrentProcess())
                    _frame = self.MainWindowHandle;
            }
            catch (Exception)
            {
                _frame = IntPtr.Zero;
            }

            return _frame;
        }
    }
}
