using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class ContextHandlerTests
{
    // Captures the limit/budget the handler forwards to the repository. No vault
    // args are passed, so ResolveVaultAsync returns null without touching the
    // vault repo — which is why a null vault repository is safe here.
    private sealed class CapturingContextRepository : IContextRepository
    {
        public int Limit;
        public int Budget;

        public Task<List<ContextResult>> ContextAsync(
            string query, int limit = 5, int budgetChars = 6000, long? vaultId = null)
        {
            Limit = limit;
            Budget = budgetChars;
            return Task.FromResult(new List<ContextResult>());
        }
    }

    private static async Task<(int Limit, int Budget)> RunAsync(Dictionary<string, string> args)
    {
        var repo = new CapturingContextRepository();
        var handler = new ContextHandler(vaults: null!, context: repo);
        await handler.HandleAsync(new IpcRequest("context", args), CancellationToken.None);
        return (repo.Limit, repo.Budget);
    }

    [Fact]
    public async Task Applies_defaults_when_limit_and_budget_absent()
    {
        var (limit, budget) = await RunAsync(new() { ["query"] = "x" });

        Assert.Equal(5, limit);
        Assert.Equal(6000, budget);
    }

    [Fact]
    public async Task Passes_through_valid_in_range_values()
    {
        var (limit, budget) = await RunAsync(new() { ["query"] = "x", ["limit"] = "12", ["budget"] = "3000" });

        Assert.Equal(12, limit);
        Assert.Equal(3000, budget);
    }

    [Fact]
    public async Task Clamps_over_large_values_to_the_maximum()
    {
        var (limit, budget) = await RunAsync(new() { ["query"] = "x", ["limit"] = "5000", ["budget"] = "9999999" });

        Assert.Equal(100, limit);        // MaxLimit, not the default 5
        Assert.Equal(100_000, budget);   // MaxBudgetChars
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("notanumber")]
    public async Task Falls_back_to_defaults_for_non_positive_or_unparseable(string raw)
    {
        var (limit, budget) = await RunAsync(new() { ["query"] = "x", ["limit"] = raw, ["budget"] = raw });

        Assert.Equal(5, limit);
        Assert.Equal(6000, budget);
    }

    [Fact]
    public async Task Requires_a_query()
    {
        var repo = new CapturingContextRepository();
        var handler = new ContextHandler(vaults: null!, context: repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new IpcRequest("context", new()), CancellationToken.None));
    }
}
