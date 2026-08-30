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

        /// <summary>
        /// Give a window the Navisworks frame as its owner, and pin the shutdown mode.
        ///
        /// Call it before <c>Show</c> or <c>ShowDialog</c> on every window this product creates.
        /// </summary>
        /// <param name="window">The window about to be shown.</param>
        public static void Own(Window? window)
        {
            if (window == null) return;

            Pin();

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

        /// <summary>The Navisworks main frame's window handle, or zero.</summary>
        private static IntPtr Handle()
        {
            try
            {
                var gui = Autodesk.Navisworks.Api.Application.Gui;
                var main = gui?.MainWindow;

                return main?.Handle ?? IntPtr.Zero;
            }
            catch (Exception)
            {
                // No document, no GUI, or a headless host: all of them mean no frame to own to.
                return IntPtr.Zero;
            }
        }
    }
}
