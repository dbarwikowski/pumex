using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Contracts;

namespace Pumex.Plugin.Sdk;

// Mini IPC server inside a plugin process. Listens on PUMEX_PLUGIN_PIPE for
// proxied requests from the daemon. Wire format mirrors the daemon exactly
// (4-byte little-endian length + UTF-8 JSON, 10 MB cap) so the daemon's
// per-request pipe handshake works against this with no protocol translation.
//
// Refactor opportunity once 001B settles: extract the length-prefix wire
// helpers into a shared package — Pumex.IpcClient is the obvious home — so
// the daemon's IpcServer and this server stop carrying parallel copies.
public sealed class PluginIpcServer
{
    private readonly string _pipeName;
    private readonly Dictionary<string, IPluginCommandHandler> _handlers;
    private readonly ILogger _logger;

    public PluginIpcServer(
        string pipeName,
        IEnumerable<IPluginCommandHandler> handlers,
        ILogger? logger = null)
    {
        _pipeName = pipeName;
        _handlers = handlers.ToDictionary(h => h.Command, StringComparer.OrdinalIgnoreCase);
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Plugin IPC server listening on pipe {Pipe}", _pipeName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                // Fire-and-forget: a plugin handler must not be able to block
                // the accept loop, otherwise a slow handler stalls the daemon's
                // entire dispatch.
                _ = HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin IPC server error");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
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
                _logger.LogWarning(ex, "Plugin connection error");
                try { await WriteMessageAsync(pipe, ErrorEnvelope(ex.Message), ct); }
                catch { /* pipe may already be closed */ }
            }
        }
    }

    private async Task<string> DispatchAsync(IpcRequest request, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(request.Command, out var handler))
            return ErrorEnvelope($"Unknown command: {request.Command}");

        try
        {
            var data = await handler.HandleAsync(request, ct);
            // Hand-roll the envelope: plugin handlers return arbitrary JsonNode
            // shapes; the daemon passes our bytes through verbatim to the CLI,
            // so we need to produce a final IpcResponse-shaped JSON document.
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
            return ErrorEnvelope(ex.Message);
        }
    }

    private static string ErrorEnvelope(string error)
    {
        var envelope = new JsonObject
        {
            ["success"] = false,
            ["data"] = null,
            ["error"] = error,
        };
        return envelope.ToJsonString();
    }

    // -------------------------
    // Wire format: 4-byte little-endian length + UTF-8 JSON, max 10 MB.
    // -------------------------

    [SuppressMessage("Reliability", "CA2016",
        Justification = "Mirrors IpcServer.ReadMessageAsync — ct passes through to ReadAsync.")]
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

    private static async Task WriteMessageAsync(PipeStream pipe, string message, CancellationToken ct)
    {
        var buf = Encoding.UTF8.GetBytes(message);
        var lenBuf = BitConverter.GetBytes(buf.Length);
        await pipe.WriteAsync(lenBuf, ct);
        await pipe.WriteAsync(buf, ct);
        await pipe.FlushAsync(ct);
    }
}
