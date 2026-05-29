using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class VaultIndexPolicyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "vault-root");

    private static VaultIndexPolicy Policy(IReadOnlyList<string>? formats = null, IReadOnlyList<string>? ignore = null) =>
        new(Root, new VaultConfig("v", DateTimeOffset.UtcNow, VaultConfig.CurrentVersion, Formats: formats, Ignore: ignore));

    private static string Rel(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    [Fact]
    public void Markdown_is_always_indexed_even_without_config()
    {
        var policy = Policy();
        Assert.True(policy.ShouldIndex(Rel("note.md")));
    }

    [Fact]
    public void Non_markdown_is_skipped_unless_enabled()
    {
        Assert.False(Policy().ShouldIndex(Rel("data.csv")));
        Assert.True(Policy(formats: ["csv"]).ShouldIndex(Rel("data.csv")));
    }

    [Fact]
    public void Format_config_is_normalised_dotted_and_lowercased()
    {
        var policy = Policy(formats: [".CSV", "Json"]);
        Assert.True(policy.ShouldIndex(Rel("a.csv")));
        Assert.True(policy.ShouldIndex(Rel("b.json")));
    }

    [Fact]
    public void Dot_directories_are_always_skipped()
    {
        var policy = Policy(formats: ["json"]);
        Assert.False(policy.ShouldIndex(Rel(".pumex", "config.json")));
        Assert.False(policy.ShouldIndex(Rel(".git", "HEAD.md")));
        Assert.False(policy.ShouldIndex(Rel(".obsidian", "x.md")));
    }

    [Fact]
    public void Ignore_globs_apply_to_markdown_too()
    {
        var policy = Policy(ignore: ["templates/**"]);
        Assert.False(policy.ShouldIndex(Rel("templates", "daily.md")));
        Assert.True(policy.ShouldIndex(Rel("notes", "daily.md")));
    }

    [Fact]
    public void Files_with_no_extension_are_skipped()
    {
        Assert.False(Policy(formats: ["csv"]).ShouldIndex(Rel("README")));
    }
}
