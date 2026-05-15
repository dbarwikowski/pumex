using Pumex.Contracts;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.Plugins;

// Registry of plugin-contributed commands. In-proc entries carry the handler
// instance; out-of-proc entries carry the pipe name the daemon dispatches to.
// Lookups go through TryGet (in-proc) and TryGetOutOfProcess (proxy) — the
// IpcServer consults them in that order so an in-proc plugin can't be shadowed
// by an out-of-proc registration on the same command name.
public sealed class PluginRegistry
{
    private readonly Dictionary<string, (string Plugin, IPluginCommandHandler Handler)> _byCommand
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, OutOfProcessEntry> _outOfProcByCommand
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, OutOfProcessEntry> _outOfProcByName
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    public void Register(string plugin, IPluginCommandHandler handler)
    {
        lock (_lock)
        {
            if (_byCommand.TryGetValue(handler.Command, out var existing))
                throw new InvalidOperationException(
                    $"Command '{handler.Command}' already registered by plugin '{existing.Plugin}'.");
            if (_outOfProcByCommand.TryGetValue(handler.Command, out var existingOop))
                throw new InvalidOperationException(
                    $"Command '{handler.Command}' already registered by out-of-process plugin '{existingOop.Name}'.");
            _byCommand[handler.Command] = (plugin, handler);
        }
    }

    public bool TryGet(string command, out IPluginCommandHandler handler)
    {
        lock (_lock)
        {
            if (_byCommand.TryGetValue(command, out var entry))
            {
                handler = entry.Handler;
                return true;
            }
        }
        handler = null!;
        return false;
    }

    public IEnumerable<(string Plugin, string Command)> List()
    {
        lock (_lock)
            return _byCommand.Select(kv => (kv.Value.Plugin, kv.Key)).ToArray();
    }

    /// <summary>
    /// Pre-register an out-of-process plugin before its handshake arrives.
    /// Used by the auto-spawn path: the loader knows the pipe name (it picked
    /// it) and the advertised command set (from manifest.json), so it populates
    /// the proxy table *before* starting the process — closes a race where the
    /// first CLI request for the plugin's command could arrive before the
    /// plugin had time to send plugin:register.
    /// </summary>
    public void PreRegisterOutOfProcess(string name, string version, string pipeName, IReadOnlyList<string> commands)
        => RegisterOutOfProcessCore(name, version, pipeName, commands, preregistered: true);

    /// <summary>
    /// Register or update an out-of-process plugin from a plugin:register
    /// handshake. The plugin's own command list is authoritative — anything
    /// the manifest pre-declared but the plugin didn't claim is dropped.
    /// </summary>
    public void RegisterOutOfProcess(string name, string version, string pipeName, IReadOnlyList<string> commands)
        => RegisterOutOfProcessCore(name, version, pipeName, commands, preregistered: false);

    private void RegisterOutOfProcessCore(string name, string version, string pipeName, IReadOnlyList<string> commands, bool preregistered)
    {
        lock (_lock)
        {
            // Replace any previous registration for this plugin name — the
            // auto-spawn path may have pre-registered with the manifest's
            // commands; the plugin's own list supersedes that.
            if (_outOfProcByName.TryGetValue(name, out var existing))
            {
                foreach (var c in existing.Commands)
                    _outOfProcByCommand.Remove(c);
            }

            foreach (var c in commands)
            {
                if (_byCommand.TryGetValue(c, out var inProc))
                    throw new InvalidOperationException(
                        $"Command '{c}' already registered by in-process plugin '{inProc.Plugin}'.");
                if (_outOfProcByCommand.TryGetValue(c, out var otherOop) && !string.Equals(otherOop.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Command '{c}' already registered by out-of-process plugin '{otherOop.Name}'.");
            }

            var entry = new OutOfProcessEntry(name, version, pipeName, commands.ToArray(), preregistered);
            _outOfProcByName[name] = entry;
            foreach (var c in commands)
                _outOfProcByCommand[c] = entry;
        }
    }

    public bool TryGetOutOfProcess(string command, out OutOfProcessEntry entry)
    {
        lock (_lock)
        {
            if (_outOfProcByCommand.TryGetValue(command, out var found))
            {
                entry = found;
                return true;
            }
        }
        entry = null!;
        return false;
    }

    /// <summary>
    /// Drops an out-of-process plugin and all of its commands. Called when the
    /// plugin process exits or when a proxy attempt times out. No-op if the
    /// plugin isn't registered.
    /// </summary>
    public void Unregister(string name)
    {
        lock (_lock)
        {
            if (!_outOfProcByName.TryGetValue(name, out var entry)) return;
            _outOfProcByName.Remove(name);
            foreach (var c in entry.Commands)
                _outOfProcByCommand.Remove(c);
        }
    }

    /// <summary>Alias for <see cref="Unregister"/> used at proxy-timeout sites for readability.</summary>
    public void MarkDead(string name) => Unregister(name);

    public IReadOnlyList<PluginInfo> Snapshot()
    {
        lock (_lock)
        {
            var collapsed = _byCommand
                .GroupBy(kv => kv.Value.Plugin, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PluginInfo(
                    Name: g.Key,
                    Version: null,
                    Kind: "in-process",
                    Pipe: null,
                    Commands: g.Select(kv => kv.Key)
                        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToList();

            foreach (var entry in _outOfProcByName.Values)
            {
                collapsed.Add(new PluginInfo(
                    Name: entry.Name,
                    Version: entry.Version,
                    Kind: "out-of-process",
                    Pipe: entry.PipeName,
                    Commands: entry.Commands));
            }
            return collapsed;
        }
    }
}

public sealed record OutOfProcessEntry(
    string Name,
    string Version,
    string PipeName,
    IReadOnlyList<string> Commands,
    bool PreRegistered);
