using System;
using System.Collections.Generic;

namespace CamelWorks.Nav
{
    /// <summary>
    /// Writing a deliverable file out of the host.
    ///
    /// Deliberately one small class, because every line of it is a host call this repository cannot
    /// test: there is no Navisworks on the machine the Core tests run on, and the only proof these
    /// members exist is that the Windows job compiles. Keeping them together means the whole
    /// unverifiable surface is four calls in one file rather than scattered through a screen.
    ///
    /// It takes the session over. The Navisworks .NET API has one document and no call that builds a
    /// file on the side, so compiling means clearing what is open, appending the sources and saving:
    /// what was open is gone, and the caller has to have said so first.
    /// </summary>
    public sealed class NavPublish
    {
        private readonly NavDocument _document;

        /// <summary>Work against one document.</summary>
        /// <param name="document">The document.</param>
        public NavPublish(NavDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <summary>
        /// Clear the document, append the sources and save it.
        /// </summary>
        /// <param name="sources">The files to append, in order.</param>
        /// <param name="output">Where to write it. The extension decides NWD or NWF.</param>
        /// <param name="appended">
        /// Called before each file goes in, with how many are already in and which is next. Where
        /// the progress on the screen comes from - appending a large model takes minutes and a
        /// window with nothing moving in it is a window somebody kills.
        /// </param>
        /// <returns>Null when it worked, or what went wrong.</returns>
        public string? Compile(IReadOnlyList<string>? sources, string output,
                               Action<int, string>? appended = null)
        {
            if (sources == null || sources.Count == 0) return "there was nothing to put in it";
            if (string.IsNullOrWhiteSpace(output)) return "there was nowhere to write it";

            try
            {
                var document = _document.Document;

                document.Clear();

                var done = 0;

                foreach (var path in sources)
                {
                    appended?.Invoke(done, path);

                    document.AppendFile(path);

                    done++;
                }

                document.SaveFile(output);

                return null;
            }
            catch (Exception e)
            {
                // The message rather than the type: what comes back here is a file that is open in
                // another program, a path nobody has rights to, or a model the reader gave up on,
                // and the host's own wording for those is better than anything invented here.
                return e.Message;
            }
        }
    }
}
