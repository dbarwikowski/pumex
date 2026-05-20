using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class SearchHandlerTests
{
    [Theory]
    [InlineData("smoke-test", "\"smoke-test\"")]
    [InlineData("smoke",      "\"smoke\"")]
    [InlineData("foo bar",    "\"foo\" \"bar\"")]
    public void EscapeForFts_quotes_plain_tokens(string input, string expected) =>
        Assert.Equal(expected, SearchHandler.EscapeForFts(input));

    [Theory]
    [InlineData("foo AND bar")]
    [InlineData("foo OR bar")]
    [InlineData("NEAR(foo bar)")]
    [InlineData("\"already quoted\"")]
    [InlineData("(foo OR bar) AND baz")]
    public void EscapeForFts_passes_structured_queries_through(string input) =>
        Assert.Equal(input, SearchHandler.EscapeForFts(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EscapeForFts_returns_null_or_blank_unchanged(string? input) =>
        Assert.Equal(input, SearchHandler.EscapeForFts(input));
}
