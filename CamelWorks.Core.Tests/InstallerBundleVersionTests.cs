using CamelWorks.Installer;
using Xunit;

namespace CamelWorks.Core.Tests;

/// <summary>
/// The rule that stops a CamelWorks install downgrading a companion bundle:
/// Dyncamelo and the IFC exporter release on their own cadence, so the copies
/// folded into a CamelWorks release are regularly older than what somebody
/// installed directly. Overwriting a newer one takes working nodes away and
/// leaves saved graphs failing with "unresolved definition".
/// </summary>
public class InstallerBundleVersionTests
{
    [Theory]
    [InlineData("0.33.0.0", "0.27.0.0", true)]   // the real case: keep the newer one
    [InlineData("0.27.0.0", "0.33.0.0", false)]  // older installed: upgrade it
    [InlineData("0.33.0.0", "0.33.0.0", false)]  // same version: reinstall, harmless
    [InlineData("1.0.0.0", "0.99.99.99", true)]  // major beats a long minor
    [InlineData("0.9.0.0", "0.10.0.0", false)]   // numeric, not lexicographic
    public void IsNewer_ComparesNumerically(string installed, string staged, bool expected)
    {
        Assert.Equal(expected, BundleVersion.IsNewer(installed, staged));
    }

    [Theory]
    [InlineData("0.33", "0.33.0.0")]
    [InlineData("0.33.0", "0.33.0.0")]
    [InlineData("1", "1.0.0.0")]
    public void MissingComponents_ReadAsZero_SoPaddingIsNotAnUpgrade(string shortForm, string longForm)
    {
        Assert.False(BundleVersion.IsNewer(shortForm, longForm));
        Assert.False(BundleVersion.IsNewer(longForm, shortForm));
    }

    [Theory]
    [InlineData(null, "0.27.0.0")]
    [InlineData("", "0.27.0.0")]
    [InlineData("   ", "0.27.0.0")]
    [InlineData("not-a-version", "0.27.0.0")]
    [InlineData("0.33.0.0", null)]
    [InlineData("0.33.0.0", "")]
    [InlineData("0.33.0.0", "garbage")]
    public void UnreadableVersions_AnswerFalse_SoTheKnownGoodCopyWins(string? installed, string? staged)
    {
        Assert.False(BundleVersion.IsNewer(installed, staged));
    }

    [Fact]
    public void TryParse_NormalisesAbsentComponents()
    {
        Assert.True(BundleVersion.TryParse("2.1", out var version));
        Assert.Equal(0, version.Build);
        Assert.Equal(0, version.Revision);
        Assert.Equal(new System.Version(2, 1, 0, 0), version);
    }

    [Fact]
    public void TryParse_RejectsRubbish()
    {
        Assert.False(BundleVersion.TryParse("v1.2", out _));
        Assert.False(BundleVersion.TryParse("1.2.x", out _));
        Assert.False(BundleVersion.TryParse(null, out _));
    }
}
