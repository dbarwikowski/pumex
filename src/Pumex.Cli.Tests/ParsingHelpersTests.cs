using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class ParsingHelpersTests
{
    [Theory]
    [InlineData(new string[] { "work" }, new string[] { "work" })]
    [InlineData(new string[] { "work,personal" }, new string[] { "work", "personal" })]
    [InlineData(new string[] { "work", "personal" }, new string[] { "work", "personal" })]
    [InlineData(new string[] { "work,personal", "urgent" }, new string[] { "work", "personal", "urgent" })]
    [InlineData(new string[0], new string[0])]
    public void ExpandTags_splits_comma_separated_and_flattens(string[] input, string[] expected)
    {
        Assert.Equal(expected, Commands.ExpandTags(input));
    }

    [Theory]
    [InlineData(new string[] { "k=v" }, new string[] { "k=v" })]
    [InlineData(new string[] { "key", "value" }, new string[] { "key=value" })]
    [InlineData(new string[] { "k=v", "a", "b" }, new string[] { "k=v", "a=b" })]
    [InlineData(new string[0], new string[0])]
    public void ExpandProperties_handles_equals_and_pair_syntax(string[] input, string[] expected)
    {
        Assert.Equal(expected, Commands.ExpandProperties(input));
    }
}
