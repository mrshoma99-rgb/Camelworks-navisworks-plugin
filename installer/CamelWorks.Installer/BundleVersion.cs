using System;

namespace CamelWorks.Installer;

/// <summary>
/// Version comparison for the bundles the installer ships. Pure and dependency
/// free, so the rule that decides whether a companion bundle is overwritten can
/// be unit-tested on the Linux CI job even though the installer itself is a
/// Windows WPF exe (CamelWorks.Core.Tests link-compiles this file).
/// </summary>
public static class BundleVersion
{
    /// <summary>
    /// Whether <paramref name="installed"/> is strictly newer than
    /// <paramref name="staged"/>. Either side being absent or unparsable means
    /// "cannot tell", which answers false: the shipped bundle is a known-good
    /// copy, so an unreadable neighbour is repaired rather than preserved.
    /// Components compare numerically with missing ones read as 0, so "0.33"
    /// and "0.33.0.0" are the same version.
    /// </summary>
    /// <param name="installed">Version declared by the bundle in ApplicationPlugins.</param>
    /// <param name="staged">Version declared by the bundle in this installer's payload.</param>
    public static bool IsNewer(string? installed, string? staged)
    {
        return TryParse(installed, out var left) &&
               TryParse(staged, out var right) &&
               left > right;
    }

    /// <summary>
    /// Parses a PackageContents.xml version ("1", "1.2", "1.2.3.4"). Missing
    /// components read as 0; anything non-numeric fails.
    /// </summary>
    /// <param name="text">The version attribute's text.</param>
    /// <param name="version">The parsed version, or 0.0 when parsing fails.</param>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Version.TryParse rejects a bare "1"; pad to at least major.minor.
        var trimmed = text!.Trim();
        if (!trimmed.Contains("."))
        {
            trimmed += ".0";
        }

        if (!Version.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        // Normalise -1 (absent) components to 0 so "0.33" == "0.33.0.0".
        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
    }
}
