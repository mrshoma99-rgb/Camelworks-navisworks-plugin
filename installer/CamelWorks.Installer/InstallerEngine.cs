using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.Win32;

namespace CamelWorks.Installer;

/// <summary>
/// All installer mechanics, free of any UI dependency (unit- and
/// scratch-compile-testable). Installs the Autodesk application bundles to the
/// per-user ApplicationPlugins folder — no admin rights involved anywhere.
///
/// <b>Three bundles, one install.</b> CamelWorks ships the Dyncamelo and IFC exporter bundles
/// beside its own, because the three are one product on one ribbon tab. They stay separate
/// bundles rather than being merged into one: each carries its own PackageContents.xml with its
/// own ProductCode and its own per-year module entries, and merging those by hand is a way to
/// produce a manifest Navisworks ignores without saying why. So the payload holds every
/// <c>*.bundle</c> folder and each is installed and removed whole.
///
/// <b>What that means for somebody who already has one.</b> Installing CamelWorks installs the
/// Dyncamelo or exporter bundle it ships — <i>unless the one already there is newer</i>. The
/// companions release on their own cadence, so the copy CamelWorks folded in at its build time is
/// often older than what somebody installed directly; overwriting it would silently take working
/// nodes away and leave graphs failing with "unresolved definition". A newer neighbour is
/// therefore left untouched and reported, and only the bundles this installer actually wrote are
/// removed on uninstall (recorded in <see cref="CompanionRecordName"/>). CamelWorks' own bundle is
/// always replaced. Their Add/Remove Programs entries are left exactly alone — this installer does
/// not touch another product's registry keys.
/// </summary>
public static class InstallerEngine
{
    private const string BundleFolderName = "CamelWorks.bundle";
    private const string PayloadResource = "CamelWorks.Installer.payload.zip";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CamelWorks";

    // The bundles this installer ships: its own, plus the two companions. CamelWorks' own is
    // always replaced; a companion is replaced only when the installed copy is not newer (see
    // Install). Registry keys belonging to the other two products are never touched - an installer
    // that tidied up its neighbour's bookkeeping would be a very unwelcome surprise.
    private static readonly string[] Bundles =
    {
        BundleFolderName, "Dyncamelo.bundle", "BIMCamel.bundle",
    };

    /// <summary>The file inside CamelWorks' bundle listing the companion bundles it installed.</summary>
    internal const string CompanionRecordName = "installed-companions.txt";

    /// <summary>Reports installer progress: 0-100 plus a status line.</summary>
    public delegate void ProgressHandler(int percent, string status);

    // Where this exe actually lives right now. A running exe cannot be
    // deleted but CAN be renamed/moved, so before touching the bundle folder
    // we evacuate ourselves to %TEMP% if we run from inside it (the copy that
    // Add/Remove Programs launches does). Assembly.Location is cached by the
    // CLR, hence tracking the path here.
    private static string _selfPath = Assembly.GetExecutingAssembly().Location;

    /// <summary>The per-user bundle install directory.</summary>
    public static string BundleDir => BundleDirFor(BundleFolderName);

    /// <summary>Where a named bundle goes.</summary>
    /// <param name="bundle">The bundle folder's name, e.g. "Dyncamelo.bundle".</param>
    public static string BundleDirFor(string bundle) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "ApplicationPlugins", bundle);

    /// <summary>The bundles this installer ships, in the order they are installed.</summary>
    public static IReadOnlyList<string> ShippedBundles => Bundles;

    /// <summary>Whether a CamelWorks bundle is currently installed.</summary>
    public static bool IsInstalled =>
        File.Exists(Path.Combine(BundleDir, "PackageContents.xml"));

    /// <summary>The installed bundle version ("1.2.3.0"), or null.</summary>
    public static string? InstalledVersion() => BundleVersionIn(BundleDir);

    /// <summary>
    /// The version declared by the PackageContents.xml of the bundle folder at
    /// <paramref name="bundleDir"/>, or null when it is absent or unreadable.
    /// </summary>
    /// <param name="bundleDir">A *.bundle folder, installed or staged.</param>
    public static string? BundleVersionIn(string bundleDir)
    {
        var manifest = Path.Combine(bundleDir, "PackageContents.xml");
        if (!File.Exists(manifest))
        {
            return null;
        }

        try
        {
            var doc = new XmlDocument();
            doc.Load(manifest);
            var version = doc.DocumentElement?.GetAttribute("Version");
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception ex) when (ex is XmlException || ex is IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the installed bundle is strictly newer than the one in the
    /// payload — see <see cref="BundleVersion.IsNewer"/> for the exact rule.
    /// </summary>
    /// <param name="installed">Version string found in ApplicationPlugins.</param>
    /// <param name="staged">Version string of the bundle in the payload.</param>
    public static bool IsNewer(string? installed, string? staged) =>
        BundleVersion.IsNewer(installed, staged);

    /// <summary>This installer's own version, e.g. "0.6.0".</summary>
    public static string SetupVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version == null ? "dev" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>The Navisworks releases this bundle ships plug-ins for.</summary>
    public static readonly string[] SupportedYears = { "2024", "2025", "2026", "2027" };

    /// <summary>
    /// Detects which supported Navisworks releases are installed (Manage or Simulate)
    /// by probing the standard install folders for Roamer.exe. The bundle ships every
    /// year and Navisworks loads the matching one via PackageContents.xml, so this is
    /// used to inform the user (and warn when no supported release is present).
    /// </summary>
    public static string[] DetectNavisworksYears()
    {
        var found = new System.Collections.Generic.List<string>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        foreach (var year in SupportedYears)
        {
            var installed = false;
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                foreach (var product in new[] { "Navisworks Manage " + year, "Navisworks Simulate " + year })
                {
                    if (File.Exists(Path.Combine(root, "Autodesk", product, "Roamer.exe")))
                    {
                        installed = true;
                        break;
                    }
                }

                if (installed)
                {
                    break;
                }
            }

            if (installed)
            {
                found.Add(year);
            }
        }

        return found.ToArray();
    }

    /// <summary>Whether Navisworks (Roamer.exe) is currently running.</summary>
    public static bool IsNavisworksRunning()
    {
        try
        {
            return Process.GetProcessesByName("Roamer").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs the bundle: extracts the payload into a temp folder, swaps it
    /// into ApplicationPlugins, copies this exe in for uninstall and registers
    /// the per-user Add/Remove Programs entry. Throws with a user-readable
    /// message on failure.
    /// </summary>
    public static void Install(ProgressHandler progress)
    {
        progress(0, "Preparing…");
        var parent = Directory.GetParent(BundleDir)!.FullName;
        Directory.CreateDirectory(parent);

        var temp = Path.Combine(parent, BundleFolderName + ".installing-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var payload = OpenPayload(out var payloadSource))
            {
                progress(5, payloadSource);
                ExtractPayload(payload, temp, progress);
            }

            // Every folder the payload actually carries, not every folder it might: a release
            // built when one of the companion downloads was unavailable ships fewer bundles, and
            // that is a smaller product rather than a broken install.
            var staged = Directory.GetDirectories(temp)
                .Where(d => File.Exists(Path.Combine(d, "PackageContents.xml")))
                .ToList();

            if (!staged.Any(d => string.Equals(Path.GetFileName(d), BundleFolderName,
                                               StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "The installer payload is missing PackageContents.xml — this download looks corrupted. " +
                    "Please re-download CamelWorksSetup.exe from bimcamel.com.");
            }

            // A companion that is already installed at a NEWER version is left alone. The
            // companions ship on their own cadence, so the copy folded in at CamelWorks build
            // time is regularly behind what somebody installed directly, and overwriting it
            // would take working nodes away from graphs that already use them.
            var keep = new List<string>();
            var write = new List<string>();
            foreach (var source in staged)
            {
                var bundle = Path.GetFileName(source);
                if (string.Equals(bundle, BundleFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    write.Add(source); // our own bundle is always replaced
                    continue;
                }

                var installedVersion = BundleVersionIn(BundleDirFor(bundle));
                var stagedVersion = BundleVersionIn(source);
                if (IsNewer(installedVersion, stagedVersion))
                {
                    keep.Add(bundle);
                    progress(85, "Keeping your newer " + FriendlyName(bundle) + " " + installedVersion + ".");
                }
                else
                {
                    write.Add(source);
                }
            }

            progress(85, "Removing previous version…");
            EvacuateSelfFromBundle();

            foreach (var bundle in Bundles)
            {
                if (!keep.Contains(bundle, StringComparer.OrdinalIgnoreCase))
                {
                    DeleteBundleDir(BundleDirFor(bundle));
                }
            }

            progress(90, "Moving files into place…");

            foreach (var source in write)
            {
                Directory.Move(source, BundleDirFor(Path.GetFileName(source)));
            }

            progress(95, "Registering uninstaller…");
            CopySelfIntoBundle();
            RecordInstalledCompanions(write.Select(Path.GetFileName));
            RegisterUninstall();
            progress(100, keep.Count == 0
                ? "Done."
                : "Done — kept your newer " +
                  string.Join(" and ", keep.Select(FriendlyName)) + ".");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// Removes the bundle and the Add/Remove Programs entry. When this exe
    /// runs from inside the bundle folder it first moves itself to %TEMP%
    /// (a running exe can be renamed, not deleted), so the folder always
    /// goes away completely.
    /// </summary>
    public static void Uninstall()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(
                   Path.GetDirectoryName(UninstallKeyPath)!, writable: true))
        {
            key?.DeleteSubKeyTree("CamelWorks", throwOnMissingSubKey: false);
        }

        // Only the bundles this installer actually wrote come out. A companion that was left
        // alone at install time because it was newer belongs to whoever put it there.
        var ours = ReadInstalledCompanions();

        EvacuateSelfFromBundle();

        foreach (var bundle in Bundles)
        {
            if (string.Equals(bundle, BundleFolderName, StringComparison.OrdinalIgnoreCase) ||
                ours.Contains(bundle, StringComparer.OrdinalIgnoreCase))
            {
                DeleteBundleDir(BundleDirFor(bundle));
            }
        }
    }

    /// <summary>The friendly product name behind a bundle folder name.</summary>
    /// <param name="bundle">A bundle folder name, e.g. "Dyncamelo.bundle".</param>
    internal static string FriendlyName(string bundle)
    {
        if (string.Equals(bundle, "Dyncamelo.bundle", StringComparison.OrdinalIgnoreCase))
        {
            return "Dyncamelo";
        }

        return string.Equals(bundle, "BIMCamel.bundle", StringComparison.OrdinalIgnoreCase)
            ? "BIMCamel IFC exporter"
            : Path.GetFileNameWithoutExtension(bundle);
    }

    /// <summary>
    /// Notes which companion bundles this install wrote, so uninstall removes
    /// exactly those and leaves a newer neighbour it skipped in place.
    /// </summary>
    /// <param name="bundles">The bundle folder names written (CamelWorks' own is ignored).</param>
    private static void RecordInstalledCompanions(IEnumerable<string> bundles)
    {
        var companions = bundles
            .Where(b => !string.Equals(b, BundleFolderName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        try
        {
            File.WriteAllLines(Path.Combine(BundleDir, CompanionRecordName), companions);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Losing the note only costs precision at uninstall; never fail the install for it.
        }
    }

    /// <summary>
    /// The companion bundles the last install wrote. An install from before this
    /// record existed has no file, and is read as "both" so upgrading from such a
    /// version still cleans up after itself.
    /// </summary>
    private static string[] ReadInstalledCompanions()
    {
        var record = Path.Combine(BundleDir, CompanionRecordName);
        try
        {
            if (File.Exists(record))
            {
                return File.ReadAllLines(record)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Fall through to the pre-record behaviour.
        }

        return Bundles.Where(b => !string.Equals(b, BundleFolderName, StringComparison.OrdinalIgnoreCase))
                      .ToArray();
    }

    // ------------------------------------------------------------- payload

    private static Stream OpenPayload(out string source)
    {
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource);
        if (embedded != null)
        {
            source = "Unpacking…";
            return embedded;
        }

        // Fallback for the zip-style distribution: a CamelWorks.bundle folder
        // sitting next to the exe is zipped into memory and installed the
        // same way (keeps a single code path with the same validations).
        var sibling = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, BundleFolderName);
        if (Directory.Exists(sibling))
        {
            source = "Copying bundle…";
            var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.GetFiles(sibling, "*", SearchOption.AllDirectories))
                {
                    var relative = BundleFolderName + "/" +
                        file.Substring(sibling.Length + 1).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, relative);
                }
            }

            buffer.Position = 0;
            return buffer;
        }

        throw new FileNotFoundException(
            "This CamelWorksSetup.exe carries no bundle payload and no CamelWorks.bundle " +
            "folder sits next to it. Download the full installer from bimcamel.com.");
    }

    private static void ExtractPayload(Stream payload, string targetDir, ProgressHandler progress)
    {
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The installer payload is empty.");
        }

        var targetRoot = Path.GetFullPath(targetDir + Path.DirectorySeparatorChar);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The installer payload contains an unsafe path: " + entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            progress(5 + (int)(75.0 * (i + 1) / entries.Count), entry.Name);
        }
    }

    // ------------------------------------------------- uninstall bookkeeping

    private static void CopySelfIntoBundle()
    {
        var target = Path.Combine(BundleDir, "CamelWorksSetup.exe");
        if (!string.Equals(_selfPath, target, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(_selfPath, target, overwrite: true);
        }
    }

    private static void RegisterUninstall()
    {
        var exe = Path.Combine(BundleDir, "CamelWorksSetup.exe");
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "CamelWorks for Navisworks for Navisworks");
        key.SetValue("DisplayVersion", InstalledVersion() ?? SetupVersion());
        key.SetValue("Publisher", "CamelWorks");
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", BundleDir);
        key.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
        key.SetValue("URLInfoAbout", "https://www.bimcamel.com/Export-Navisworks-to-Ifc");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void EvacuateSelfFromBundle()
    {
        if (!_selfPath.StartsWith(BundleDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var temp = Path.Combine(
            Path.GetTempPath(), "CamelWorksSetup-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.Move(_selfPath, temp);
            _selfPath = temp;
        }
        catch (IOException)
        {
            // Cross-volume TEMP (exotic): fall back to a detached delayed
            // delete of the folder after this process exits.
            var script = "ping -n 4 127.0.0.1 > nul & rmdir /s /q \"" + BundleDir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
    }

    private static void DeleteBundleDir(string where)
    {
        // Retry a few times: Explorer / indexing can hold short-lived locks.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(where))
                {
                    Directory.Delete(where, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(250);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Leftover temp folder is harmless; the next install cleans it up.
        }
    }
}
