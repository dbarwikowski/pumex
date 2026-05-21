using System.Reflection;
using Pumex.Contracts;

namespace Pumex.Contracts.Tests;

public class VersionInfoTests
{
    [Fact]
    public void For_returns_unknown_when_assembly_is_null()
    {
        Assert.Equal("unknown", VersionInfo.For(null));
    }

    [Fact]
    public void For_reads_informational_version_from_assembly()
    {
        // Pumex.Contracts inherits <Version>0.1.0</Version> from Directory.Build.props,
        // so its assembly always carries an AssemblyInformationalVersionAttribute.
        var v = VersionInfo.For(typeof(PumexPaths).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(v));
        Assert.NotEqual("unknown", v);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+abc1234", "0.1.0")]
    [InlineData("1.2.3+build.456", "1.2.3")]
    [InlineData("2.0.0-preview.1", "2.0.0-preview.1")]
    [InlineData("2.0.0-preview.1+sha", "2.0.0-preview.1")]
    public void StripBuildMetadata_handles_all_known_shapes(string? raw, string expected)
    {
        Assert.Equal(expected, VersionInfo.StripBuildMetadata(raw));
    }

    [Fact]
    public void Current_returns_a_non_empty_string()
    {
        var v = VersionInfo.Current;
        Assert.False(string.IsNullOrWhiteSpace(v));
    }
}
