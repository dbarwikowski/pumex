using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pumex.Contracts;
using Pumex.Daemon.Ipc;
using Pumex.Daemon.Plugins;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon;

public class IpcServer : BackgroundService
{
    private readonly Dictionary<string, ICommandHandler> _handlers;
    private readonly PluginRegistry? _plugins;
    private readonly ILogger<IpcServer> _logger;
    private readonly string _pipeName;

    public IpcServer(
        IEnumerable<ICommandHandler> handlers,
        ILogger<IpcServer> logger,
        string? pipeName = null,
        PluginRegistry? plugins = null)
    {
        _handlers = handlers.ToDictionary(h => h.Command, StringComparer.OrdinalIgnoreCase);
        _plugins = plugins;
        _logger = logger;
        _pipeName = pipeName ?? PumexPaths.PipeName;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("IPC server listening on pipe {Pipe}", _pipeName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipe(_pipeName);

                await pipe.WaitForConnectionAsync(ct);

                _ = HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC server error");
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                var raw = await ReadMessageAsync(pipe, ct);
                if (raw is null) return;

                var request = JsonSerializer.Deserialize(raw, PumexJsonContext.Default.IpcRequest);
                if (request is null) return;

                var response = await DispatchAsync(request, ct);
                await WriteMessageAsync(pipe, response, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connection error");
                try
                {
                    await WriteMessageAsync(pipe, SerializeResponse(IpcResponse.Fail(ex.Message)), ct);
                }
                catch { /* pipe may be closed */ }
            }
        }
    }

    private async Task<string> DispatchAsync(IpcRequest request, CancellationToken ct)
    {
        if (_handlers.TryGetValue(request.Command, out var handler))
        {
            try
            {
                var result = await handler.HandleAsync(request, ct);
                return SerializeResponse(IpcResponse.Ok(result));
            }
            catch (Exception ex)
            {
                return SerializeResponse(IpcResponse.Fail(ex.Message));
            }
        }

        if (_plugins is not null && _plugins.TryGet(request.Command, out var pluginHandler))
            return await RunPluginAsync(pluginHandler, request, ct);

        if (_plugins is not null && _plugins.TryGetOutOfProcess(request.Command, out var outProc))
            return await ProxyAsync(outProc, request, ct);

        return SerializeResponse(IpcResponse.Fail($"Unknown command: {request.Command}"));
    }

    // Daemon → plugin proxy. The daemon acts as a client to the plugin's pipe.
    // Per-request connection is fine for v1; pool later if the handshake cost
    // shows up in proxy-latency benchmarks. Plugin owns its response shape —
    // we pass the raw JSON through verbatim so the daemon's AOT type graph
    // doesn't need to know about every plugin's response DTO.
    private async Task<string> ProxyAsync(OutOfProcessEntry plugin, IpcRequest request, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            plugin.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(2_000, ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Plugin '{Name}' did not respond on pipe '{Pipe}' — unregistering.",
                plugin.Name, plugin.PipeName);
            _plugins!.MarkDead(plugin.Name);
            return SerializeResponse(IpcResponse.Fail(
                $"Plugin '{plugin.Name}' did not respond on pipe '{plugin.PipeName}'."));
        }

        var requestJson = JsonSerializer.Serialize(request, PumexJsonContext.Default.IpcRequest);
        await WriteMessageAsync(pipe, requestJson, ct);

        var responseJson = await ReadMessageAsync(pipe, ct);
        // Pass-through: never re-parse, never re-serialise. Plugins can return
        // any JSON shape without touching PumexJsonContext.
        return responseJson ?? SerializeResponse(IpcResponse.Fail(
            $"Plugin '{plugin.Name}' closed the connection without response."));
    }

    private static async Task<string> RunPluginAsync(IPluginCommandHandler h, IpcRequest req, CancellationToken ct)
    {
        try
        {
            var data = await h.HandleAsync(req, ct);
            // Hand-roll the envelope: plugins return arbitrary JsonNode shapes,
            // and we don't want every plugin's response type registered in
            // PumexJsonContext. Splice the node in verbatim.
            var envelope = new JsonObject
            {
                ["success"] = true,
                ["data"] = data,
                ["error"] = null,
            };
            return envelope.ToJsonString();
        }
        catch (Exception ex)
        {
            return SerializeResponse(IpcResponse.Fail(ex.Message));
        }
    }

    // Handler return types are preserved by DI registrations; IpcResponse<object?> Data is
    // serialized via the runtime type, which matches the type the client deserializes into.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "IpcResponse types are in PumexJsonContext; handler types are rooted by DI.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "IpcResponse types are in PumexJsonContext; handler types are rooted by DI.")]
    private static string SerializeResponse<T>(IpcResponse<T> response)
        => JsonSerializer.Serialize(response, PumexJsonContext.Default.Options);

    // -------------------------
    // Wire format: 4-byte little-endian length + UTF-8 JSON, max 10 MB.
    // -------------------------

    private static async Task<string?> ReadMessageAsync(PipeStream pipe, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        var read = 0;
        while (read < 4)
        {
            var n = await pipe.ReadAsync(lenBuf.AsMemory(read, 4 - read), ct);
            if (n == 0) return null;
            read += n;
        }

        var length = BitConverter.ToInt32(lenBuf, 0);
        if (length <= 0 || length > 10 * 1024 * 1024) return null;

        var buf = new byte[length];
        await pipe.ReadExactlyAsync(buf, ct);
        return Encoding.UTF8.GetString(buf);
    }

    // On Windows the pipe must grant AuthenticatedUsers read/write so a non-elevated
    // CLI can connect to a daemon running as a Windows Service (Session 0).
    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                security);
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static async Task WriteMessageAsync(PipeStream pipe, string message, CancellationToken ct)
    {
        var buf = Encoding.UTF8.GetBytes(message);
        var lenBuf = BitConverter.GetBytes(buf.Length);
        await pipe.WriteAsync(lenBuf, ct);
        await pipe.WriteAsync(buf, ct);
        await pipe.FlushAsync(ct);
    }
}
