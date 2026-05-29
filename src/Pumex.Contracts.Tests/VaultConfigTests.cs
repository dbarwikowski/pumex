using Pumex.Contracts;

namespace Pumex.Contracts.Tests;

public class VaultConfigTests
{
    private static VaultConfig Make(IReadOnlyList<string>? formats = null, IReadOnlyList<string>? ignore = null) =>
        new("v", DateTimeOffset.UtcNow, VaultConfig.CurrentVersion, Formats: formats, Ignore: ignore);

    [Fact]
    public void Absent_formats_means_markdown_only()
    {
        Assert.Empty(Make().EffectiveFormats);
        Assert.Empty(Make().EffectiveIgnore);
    }

    [Fact]
    public void Formats_are_normalised_to_lowercase_dotted_extensions()
    {
        var formats = Make(formats: ["CSV", ".Json", "yaml"]).EffectiveFormats;
        Assert.Equal([".csv", ".json", ".yaml"], formats);
    }

    [Fact]
    public void Markdown_is_dropped_from_extra_formats()
    {
        // Markdown is always-on; listing it must not duplicate or override that.
        Assert.DoesNotContain(".md", Make(formats: ["md", "csv"]).EffectiveFormats);
    }

    [Fact]
    public void Blank_and_duplicate_entries_are_dropped()
    {
        Assert.Equal([".csv"], Make(formats: ["csv", "  ", "csv"]).EffectiveFormats);
    }

    [Fact]
    public void Ignore_drops_blanks()
    {
        Assert.Equal(["templates/**"], Make(ignore: ["templates/**", "  "]).EffectiveIgnore);
    }
}
