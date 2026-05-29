namespace Pumex.Daemon.Tests;

public class WikilinkResolverTests
{
    [Fact]
    public void Resolve_returns_null_when_resolver_is_empty()
    {
        var resolver = new WikilinkResolver();

        Assert.Null(resolver.Resolve("anything", sourcePath: "/x.md"));
    }

    [Fact]
    public void Rebuild_indexes_paths_by_filename_and_resolves_them()
    {
        var resolver = new WikilinkResolver();
        var path = Path.Combine("vault", "notes", "alpha.md");
        resolver.Rebuild(new[] { path });

        Assert.Equal(path, resolver.Resolve("alpha", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Add_makes_a_path_resolvable()
    {
        var resolver = new WikilinkResolver();
        var path = Path.Combine("vault", "added.md");

        resolver.Add(path);

        Assert.Equal(path, resolver.Resolve("added", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Remove_unindexes_a_path()
    {
        var resolver = new WikilinkResolver();
        var path = Path.Combine("vault", "doomed.md");
        resolver.Add(path);

        resolver.Remove(path);

        Assert.Null(resolver.Resolve("doomed", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Resolve_prefers_the_nearest_match_when_multiple_share_a_name()
    {
        var resolver = new WikilinkResolver();
        var nearby = Path.Combine("vault", "team", "notes", "shared.md");
        var faraway = Path.Combine("vault", "archive", "2019", "shared.md");
        resolver.Rebuild(new[] { nearby, faraway });

        var source = Path.Combine("vault", "team", "current.md");
        var picked = resolver.Resolve("shared", source);

        Assert.Equal(nearby, picked);
    }

    [Fact]
    public void Resolve_matches_path_suffix_for_qualified_links()
    {
        var resolver = new WikilinkResolver();
        var aPath = Path.Combine("vault", "a", "shared.md");
        var bPath = Path.Combine("vault", "b", "shared.md");
        resolver.Rebuild(new[] { aPath, bPath });

        var picked = resolver.Resolve(Path.Combine("b", "shared"), sourcePath: Path.Combine("vault", "src.md"));

        Assert.Equal(bPath, picked);
    }

    [Fact]
    public void Resolve_is_case_insensitive_on_filename_lookup()
    {
        var resolver = new WikilinkResolver();
        var path = Path.Combine("vault", "Mixed-Case.md");
        resolver.Add(path);

        Assert.Equal(path, resolver.Resolve("mixed-case", sourcePath: Path.Combine("vault", "src.md")));
        Assert.Equal(path, resolver.Resolve("MIXED-CASE", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Bare_link_resolves_to_markdown_only_never_to_a_non_markdown_sibling()
    {
        var resolver = new WikilinkResolver();
        var md = Path.Combine("vault", "data.md");
        var csv = Path.Combine("vault", "data.csv");
        resolver.Rebuild(new[] { md, csv });

        Assert.Equal(md, resolver.Resolve("data", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Non_markdown_file_is_not_resolvable_by_bare_name()
    {
        var resolver = new WikilinkResolver();
        resolver.Rebuild(new[] { Path.Combine("vault", "data.csv") });

        Assert.Null(resolver.Resolve("data", sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Explicit_extension_resolves_a_non_markdown_target()
    {
        var resolver = new WikilinkResolver();
        var csv = Path.Combine("vault", "sub", "data.csv");
        resolver.Rebuild(new[] { csv });

        Assert.Equal(csv, resolver.Resolve("data.csv", sourcePath: Path.Combine("vault", "src.md")));
        Assert.Equal(csv, resolver.Resolve(Path.Combine("sub", "data.csv"), sourcePath: Path.Combine("vault", "src.md")));
    }

    [Fact]
    public void Explicit_extension_does_not_fall_back_to_markdown()
    {
        var resolver = new WikilinkResolver();
        resolver.Rebuild(new[] { Path.Combine("vault", "data.md") });

        // [[data.csv]] must not resolve to data.md
        Assert.Null(resolver.Resolve("data.csv", sourcePath: Path.Combine("vault", "src.md")));
    }
}
