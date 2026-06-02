using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class JsonRendererTests
{
    [Fact]
    public void TryPrepare_object_root_returns_indented_json_no_notice()
    {
        var ok = JsonRenderer.TryPrepare("""{"a":1,"b":2}""", limit: 100, out var json, out var notice);

        Assert.True(ok);
        Assert.Null(notice);
        Assert.Contains("\"a\": 1", json);   // re-serialised with indentation
    }

    [Fact]
    public void TryPrepare_limit_is_ignored_for_object_root()
    {
        // An object with more than `limit` keys is never capped — limit is rows-of-array only.
        var ok = JsonRenderer.TryPrepare("""{"a":1,"b":2,"c":3}""", limit: 1, out _, out var notice);

        Assert.True(ok);
        Assert.Null(notice);
    }

    [Fact]
    public void TryPrepare_array_root_within_limit_is_not_capped()
    {
        var ok = JsonRenderer.TryPrepare("[1,2,3]", limit: 100, out var json, out var notice);

        Assert.True(ok);
        Assert.Null(notice);
        Assert.Contains("3", json);
    }

    [Fact]
    public void TryPrepare_array_root_over_limit_is_capped_with_notice()
    {
        var ok = JsonRenderer.TryPrepare("[1,2,3,4,5]", limit: 2, out var json, out var notice);

        Assert.True(ok);
        Assert.Equal("showing 2 of 5 elements", notice);
        Assert.Contains("1", json);
        Assert.Contains("2", json);
        Assert.DoesNotContain("5", json);   // trimmed elements are gone
    }

    [Fact]
    public void TryPrepare_negative_limit_means_unlimited()
    {
        var ok = JsonRenderer.TryPrepare("[1,2,3,4,5]", limit: -1, out var json, out var notice);

        Assert.True(ok);
        Assert.Null(notice);
        Assert.Contains("5", json);
    }

    [Fact]
    public void TryPrepare_scalar_root_renders_without_notice()
    {
        var ok = JsonRenderer.TryPrepare("42", limit: 100, out var json, out var notice);

        Assert.True(ok);
        Assert.Null(notice);
        Assert.Contains("42", json);
    }

    [Fact]
    public void TryPrepare_tolerates_jsonc_comments_and_trailing_commas()
    {
        var ok = JsonRenderer.TryPrepare(
            """
            {
                // comment
                "name": "x",
            }
            """, limit: 100, out var json, out _);

        Assert.True(ok);
        Assert.Contains("\"name\": \"x\"", json);   // comment stripped, re-serialised cleanly
    }

    [Fact]
    public void TryPrepare_returns_false_for_malformed_json()
    {
        Assert.False(JsonRenderer.TryPrepare("{ not json", limit: 100, out _, out _));
    }

    [Fact]
    public void TryPrepare_returns_false_for_empty_input()
    {
        Assert.False(JsonRenderer.TryPrepare("", limit: 100, out _, out _));
    }

    [Fact]
    public void TryPrepare_preserves_polish_unicode()
    {
        var ok = JsonRenderer.TryPrepare("""{"city":"Łódź"}""", limit: 100, out var json, out _);

        Assert.True(ok);
        Assert.Contains("Łódź", json);
    }
}
