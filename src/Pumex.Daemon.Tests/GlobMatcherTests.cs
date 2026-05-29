namespace Pumex.Daemon.Tests;

public class GlobMatcherTests
{
    [Fact]
    public void Empty_matcher_matches_nothing()
    {
        var matcher = new GlobMatcher([]);
        Assert.True(matcher.IsEmpty);
        Assert.False(matcher.IsMatch("anything.md"));
    }

    [Theory]
    [InlineData("*.log", "app.log", true)]
    [InlineData("*.log", "sub/deep/app.log", true)]   // no-slash pattern matches basename at any depth
    [InlineData("*.log", "app.txt", false)]
    [InlineData("*.tmp.md", "draft.tmp.md", true)]
    public void Basename_patterns_match_at_any_depth(string glob, string path, bool expected) =>
        Assert.Equal(expected, new GlobMatcher([glob]).IsMatch(path));

    [Theory]
    [InlineData("templates/**", "templates/a.md", true)]
    [InlineData("templates/**", "templates/sub/b.md", true)]
    [InlineData("templates/**", "other/a.md", false)]
    [InlineData("archive/2019/*.md", "archive/2019/old.md", true)]
    [InlineData("archive/2019/*.md", "archive/2020/old.md", false)]
    [InlineData("archive/*.md", "archive/sub/old.md", false)]   // single * does not cross '/'
    public void Path_patterns_anchor_to_relative_path(string glob, string path, bool expected) =>
        Assert.Equal(expected, new GlobMatcher([glob]).IsMatch(path));

    [Fact]
    public void Matching_is_case_insensitive_and_separator_agnostic()
    {
        var matcher = new GlobMatcher(["Templates/**"]);
        Assert.True(matcher.IsMatch(@"templates\note.md"));
    }

    [Fact]
    public void Any_pattern_matching_wins()
    {
        var matcher = new GlobMatcher(["*.log", "drafts/**"]);
        Assert.True(matcher.IsMatch("drafts/x.md"));
        Assert.True(matcher.IsMatch("y.log"));
        Assert.False(matcher.IsMatch("keep.md"));
    }
}
