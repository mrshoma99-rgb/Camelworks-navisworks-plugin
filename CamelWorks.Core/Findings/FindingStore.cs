using System;
using System.Collections.Generic;
using System.IO;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The findings file, beside the project file.
    ///
    /// Its own file rather than a section of the project file. The project file is rewritten every
    /// time any tool stores anything — a folder path, a column width, a tick. Findings are the large
    /// item here: about two hundred bytes each, so an eighteen-thousand-clash job is some three and
    /// a half megabytes. Storing a text box should not rewrite that, and a findings write should not
    /// rewrite everybody's settings.
    ///
    /// It still travels with the job: same <c>.camelworks</c> folder, same stem, beside the
    /// <c>.cwproj</c>. A document that has never been saved keeps its findings in the user's own
    /// folder, like its project file, until it has a home.
    ///
    /// Written atomically, through the same path the project file uses: a findings file half-written
    /// by a Navisworks that closed mid-save costs a week of work.
    /// </summary>
    public sealed class FindingStore
    {
        /// <summary>What the findings file is called, after the model's own stem.</summary>
        public const string Extension = ".findings.json";

        private readonly IFileSystem _fs;

        private FindingStore(IFileSystem fs, string? path, string where, FindingRegister findings,
                             IReadOnlyList<string> notes)
        {
            _fs = fs; Path = path; Where = where; Findings = findings; Notes = notes;
        }

        /// <summary>Where the file is, or null when there is nowhere to keep one.</summary>
        public string? Path { get; }

        /// <summary>Where that is, in words, for a screen to say.</summary>
        public string Where { get; }

        /// <summary>The findings.</summary>
        public FindingRegister Findings { get; }

        /// <summary>Anything worth saying about what was read.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why the last save did not land, or null.</summary>
        public string? LastSaveProblem { get; private set; }

        /// <summary>True when there is nowhere to write.</summary>
        public bool IsMemoryOnly => Path == null;

        /// <summary>
        /// Open the findings beside a project file.
        /// </summary>
        /// <param name="fs">The file system.</param>
        /// <param name="project">The project file this belongs to.</param>
        public static FindingStore Open(IFileSystem fs, ProjectStore? project)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));

            var path = PathFor(project?.Path);

            if (path == null)
                return new FindingStore(fs, null, "in memory only — nothing will be kept when "
                                        + "Navisworks closes", new FindingRegister(),
                                        Array.Empty<string>());

            var notes = new List<string>();
            string? text = null;

            try
            {
                text = fs.ReadAllText(path);
            }
            catch (Exception e)
            {
                notes.Add("The findings file could not be read, so this session starts with none: "
                          + e.Message);
            }

            var register = text == null || !JsonReader.TryParse(text, out var json)
                ? new FindingRegister()
                : FindingRegister.FromJson(json, notes);

            if (text != null && !JsonReader.TryParse(text, out _))
                notes.Add("The findings file is not readable JSON, so this session starts with "
                          + "none. The file has been left alone rather than overwritten.");

            return new FindingStore(fs, path, "beside the document, in "
                                    + ProjectStore.SidecarFolder, register, notes);
        }

        /// <summary>
        /// Where the findings for a project file live.
        /// </summary>
        /// <param name="projectPath">The project file's path, or null.</param>
        public static string? PathFor(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) return null;

            var directory = System.IO.Path.GetDirectoryName(projectPath!);
            var stem = System.IO.Path.GetFileNameWithoutExtension(projectPath!);

            if (directory == null || string.IsNullOrEmpty(stem)) return null;

            return PathText.Join(directory, stem + Extension);
        }

        /// <summary>
        /// Write it.
        ///
        /// Returns false rather than throwing, and leaves the reason on
        /// <see cref="LastSaveProblem"/> — a failed save is a line on a pane, not something every
        /// caller wraps in a try.
        /// </summary>
        public bool Save()
        {
            LastSaveProblem = null;

            if (IsMemoryOnly)
            {
                LastSaveProblem = "there is nowhere to save findings to — " + Where;
                return false;
            }

            try
            {
                var folder = System.IO.Path.GetDirectoryName(Path!);

                if (!string.IsNullOrEmpty(folder)) _fs.CreateDirectory(folder!);

                AtomicFile.Write(_fs, Path!, Findings.ToJson().ToJson());
                return true;
            }
            catch (IOException e)
            {
                LastSaveProblem = "could not write " + Path + ": " + e.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                LastSaveProblem = "no permission to write " + Path;
                return false;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            Findings.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            + " findings, " + Where;
    }
}
