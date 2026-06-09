using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class ContextHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly IContextRepository _context;

    // Upper bounds so a caller can't trigger an unbounded scan / response.
    // Per-source size is already capped by ContextRepository.MaxPassageLines;
    // these cap the number of sources and total passage text.
    private const int MaxLimit = 100;
    private const int MaxBudgetChars = 100_000;

    public string Command => "context";

    public ContextHandler(IVaultRepository vaults, IContextRepository context)
    {
        _vaults = vaults;
        _context = context;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        // Unlike search, a query is mandatory — context is text-driven.
        var query = request.Require("query");
        // Clamp (not reset) over-large values so `--limit 200` gives 100, not the default 5.
        var limit = request.Args.TryGetValue("limit", out var l) && int.TryParse(l, out var n) && n > 0
            ? Math.Min(n, MaxLimit) : 5;
        var budget = request.Args.TryGetValue("budget", out var b) && int.TryParse(b, out var m) && m > 0
            ? Math.Min(m, MaxBudgetChars) : 6000;
        var vault = await request.ResolveVaultAsync(_vaults);

        return await _context.ContextAsync(query, limit, budget, vault?.Id);
    }
}
