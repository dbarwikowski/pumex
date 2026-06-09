using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class ContextHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly IContextRepository _context;

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
        var limit = request.Args.TryGetValue("limit", out var l) && int.TryParse(l, out var n) && n > 0 ? n : 5;
        var budget = request.Args.TryGetValue("budget", out var b) && int.TryParse(b, out var m) && m > 0 ? m : 6000;
        var vault = await request.ResolveVaultAsync(_vaults);

        return await _context.ContextAsync(query, limit, budget, vault?.Id);
    }
}
