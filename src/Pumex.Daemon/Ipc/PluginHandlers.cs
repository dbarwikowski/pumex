using Pumex.Contracts;
using Pumex.Daemon.Plugins;

namespace Pumex.Daemon.Ipc;

// Handshake from an out-of-proc plugin. Args: name (required), pipe (required),
// version (optional, defaults to 0.0.0), commands (required, comma-separated —
// IpcRequest.Args is Dictionary<string, string>, so we serialise the list as a
// joined string on the wire).
public sealed class PluginRegisterHandler : ICommandHandler
{
    private readonly PluginRegistry _registry;

    public string Command => "plugin:register";

    public PluginRegisterHandler(PluginRegistry registry) => _registry = registry;

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var name = request.Require("name");
        var pipe = request.Require("pipe");
        var version = request.Optional("version") ?? "0.0.0";
        var commands = (request.Optional("commands") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _registry.RegisterOutOfProcess(name, version, pipe, commands);
        return Task.FromResult<object?>(new PluginInfo(
            Name: name,
            Version: version,
            Kind: "out-of-process",
            Pipe: pipe,
            Commands: commands));
    }
}

public sealed class PluginUnregisterHandler : ICommandHandler
{
    private readonly PluginRegistry _registry;

    public string Command => "plugin:unregister";

    public PluginUnregisterHandler(PluginRegistry registry) => _registry = registry;

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var name = request.Require("name");
        _registry.Unregister(name);
        return Task.FromResult<object?>(name);
    }
}

public sealed class PluginListHandler : ICommandHandler
{
    private readonly PluginRegistry _registry;

    public string Command => "plugin:list";

    public PluginListHandler(PluginRegistry registry) => _registry = registry;

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
        => Task.FromResult<object?>(_registry.Snapshot());
}

public sealed class PluginLoadHandler : ICommandHandler
{
    private readonly PluginLoader _loader;

    public string Command => "plugin:load";

    public PluginLoadHandler(PluginLoader loader) => _loader = loader;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var path = request.Require("path");
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Plugin directory not found: {full}");

        await _loader.LoadFromDirectoryAsync(full, ct);
        return path;
    }
}
