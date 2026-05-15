using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pumex.Contracts;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.Plugins;

public sealed class PluginLoader : IHostedService
{
    private readonly PluginRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginLoader> _logger;
    private readonly string _pluginsRoot;
    private readonly string _daemonPipeName;
    private readonly List<LoadedPlugin> _loaded = new();
    private readonly List<SpawnedPlugin> _spawned = new();

    public PluginLoader(
        PluginRegistry registry,
        IServiceProvider services,
        ILogger<PluginLoader> logger,
        string? pluginsRoot = null,
        string? daemonPipeName = null)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
        _pluginsRoot = pluginsRoot ?? PumexPaths.Plugins;
        _daemonPipeName = daemonPipeName ?? PumexPaths.PipeName;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_pluginsRoot))
        {
            _logger.LogDebug("Plugins root {Root} does not exist — nothing to load.", _pluginsRoot);
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(_pluginsRoot))
        {
            try
            {
                await LoadFromDirectoryAsync(dir, ct);
            }
            catch (Exception ex)
            {
                // One bad plugin must not crash the daemon. Surface and skip.
                _logger.LogError(ex, "Failed to load plugin from {Dir}", dir);
            }
        }
    }

    // Plugin loading is fundamentally incompatible with trimming/AOT — entry
    // types are resolved by name at runtime out of assemblies the trimmer has
    // never seen. Suppress the analyzer here; the daemon's published image
    // would need PublishAot=false anyway if it shipped with plugins enabled.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Plugin entry types come from runtime-loaded assemblies.")]
    [UnconditionalSuppressMessage("Trimming", "IL2057",
        Justification = "Plugin entry type is named by manifest at runtime — by design.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "ActivatorUtilities receives a runtime-loaded plugin Type.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "ActivatorUtilities receives a runtime-loaded plugin Type.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Plugin loading uses Assembly.LoadFromAssemblyPath; not used under Native AOT.")]
    internal async Task LoadFromDirectoryAsync(string dir, CancellationToken ct)
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _logger.LogDebug("No manifest.json in {Dir}", dir);
            return;
        }

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize(json, PumexPluginJsonContext.Default.PluginManifest)
            ?? throw new InvalidDataException($"Empty manifest in {dir}");

        if (manifest.SchemaVersion != 1)
            throw new NotSupportedException($"Manifest schema {manifest.SchemaVersion} not supported");

        if (manifest.Executable is not null)
        {
            StartOutOfProcessPlugin(manifest, dir);
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            throw new InvalidDataException(
                $"Plugin manifest '{manifest.Name}' is missing entryAssembly (required for in-process plugins).");
        if (string.IsNullOrWhiteSpace(manifest.EntryType))
            throw new InvalidDataException(
                $"Plugin manifest '{manifest.Name}' is missing entryType (required for in-process plugins).");

        var asmPath = Path.Combine(dir, manifest.EntryAssembly);
        if (!File.Exists(asmPath))
            throw new FileNotFoundException($"Entry assembly not found: {asmPath}");

        var alc = new PluginLoadContext(asmPath);
        var asm = alc.LoadFromAssemblyPath(asmPath);

        var entryType = asm.GetType(manifest.EntryType, throwOnError: true)
            ?? throw new InvalidDataException($"Entry type {manifest.EntryType} not found in {asmPath}");

        var instance = (PumexPlugin)ActivatorUtilities.CreateInstance(_services, entryType);

        var dataDir = Path.Combine(dir, "data");
        Directory.CreateDirectory(dataDir);

        var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        var pluginLogger = loggerFactory.CreateLogger(manifest.Name);
        var host = _services.GetRequiredService<IPumexHost>();

        var context = new PluginContext(manifest, host, dataDir, pluginLogger);
        instance.Bind(context);

        var handlers = await instance.OnInitAsync(ct);
        foreach (var h in handlers)
            _registry.Register(manifest.Name, h);

        // BackgroundService.StartAsync returns as soon as ExecuteAsync hits its
        // first await — same pattern as the daemon's other hosted services.
        await instance.StartAsync(ct);

        _loaded.Add(new LoadedPlugin(manifest, instance, alc, handlers));
        _logger.LogInformation(
            "Loaded plugin {Name} v{Version} ({Count} commands)",
            manifest.Name, manifest.Version, handlers.Count);
    }

    // Spawn an out-of-process plugin. The daemon picks the plugin's pipe name
    // and PRE-REGISTERS it in the registry — this closes a window where the
    // first CLI request could arrive before the plugin has had a chance to
    // call plugin:register. The plugin's own register call later supersedes
    // the pre-registration with its authoritative command list.
    private void StartOutOfProcessPlugin(PluginManifest manifest, string pluginDir)
    {
        var executable = Path.Combine(pluginDir, manifest.Executable!);
        if (!File.Exists(executable))
            throw new FileNotFoundException($"Plugin executable not found: {executable}");

        var pipeName =
            $"pumex-plugin-{manifest.Name}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";

        // Pre-register so the proxy table is populated before the plugin process
        // has had a chance to call plugin:register. If the plugin advertised no
        // commands in the manifest, we register nothing here — only its later
        // plugin:register call wires commands up.
        _registry.PreRegisterOutOfProcess(
            manifest.Name,
            manifest.Version,
            pipeName,
            manifest.Commands ?? []);

        var dataDir = Path.Combine(pluginDir, "data");
        Directory.CreateDirectory(dataDir);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = pluginDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in manifest.ExecutableArgs ?? [])
            psi.ArgumentList.Add(arg);

        psi.Environment["PUMEX_DAEMON_PIPE"] = _daemonPipeName;
        psi.Environment["PUMEX_PLUGIN_PIPE"] = pipeName;
        psi.Environment["PUMEX_PLUGIN_NAME"] = manifest.Name;
        psi.Environment["PUMEX_PLUGIN_DATA"] = dataDir;

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start plugin process: {executable}");

        proc.EnableRaisingEvents = true;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) _logger.LogDebug("[{Name}] {Line}", manifest.Name, e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) _logger.LogWarning("[{Name}] {Line}", manifest.Name, e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Unregister on crash. No auto-restart in v1 — a back-off / max-attempts
        // policy is a follow-up; for now the user re-issues `pumex plugin load`.
        proc.Exited += (_, _) =>
        {
            _logger.LogWarning(
                "Plugin '{Name}' exited (code={Code})",
                manifest.Name, SafeExitCode(proc));
            _registry.Unregister(manifest.Name);
        };

        lock (_spawned)
            _spawned.Add(new SpawnedPlugin(manifest.Name, pipeName, proc));

        _logger.LogInformation(
            "Spawned out-of-proc plugin {Name} v{Version} on pipe '{Pipe}' (pid={Pid})",
            manifest.Name, manifest.Version, pipeName, proc.Id);
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch { return -1; }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        foreach (var p in _loaded)
        {
            try { await p.Instance.StopAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping plugin {Name}", p.Manifest.Name);
            }
        }

        SpawnedPlugin[] spawnedSnapshot;
        lock (_spawned)
        {
            spawnedSnapshot = _spawned.ToArray();
            _spawned.Clear();
        }

        foreach (var s in spawnedSnapshot)
        {
            try
            {
                if (!s.Process.HasExited)
                {
                    // Best-effort: close the child. Plugins should react to pipe
                    // closure / EOF on their accept loop; if they don't exit
                    // within a short grace period we kill them.
                    s.Process.Kill(entireProcessTree: false);
                    s.Process.WaitForExit(2_000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping out-of-proc plugin {Name}", s.Name);
            }
            finally
            {
                _registry.Unregister(s.Name);
                s.Process.Dispose();
            }
        }
    }

    private sealed record SpawnedPlugin(string Name, string PipeName, Process Process);
}
