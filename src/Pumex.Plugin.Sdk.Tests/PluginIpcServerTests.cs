using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pumex.Contracts;

namespace Pumex.Plugin.Sdk.Tests;

public class PluginIpcServerTests
{
    [Fact]
    public async Task Handler_round_trips_request_and_response_over_pipe()
    {
        var pipeName = "pumex-test-plugin-" + Guid.NewGuid().ToString("N");
        var handler = new EchoHandler();

        var server = new PluginIpcServer(pipeName, [handler]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.RunAsync(cts.Token);

        try
        {
            // Give the accept loop a beat to bind the pipe.
            await Task.Delay(50, cts.Token);

            var response = await SendAsync(pipeName, "echo", new() { ["msg"] = "ping" }, cts.Token);

            var success = response["success"]!.GetValue<bool>();
            Assert.True(success);
            Assert.Equal("ping", response["data"]!.GetValue<string>());
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { /* expected */ }
        }
    }

    [Fact]
    public async Task Unknown_command_returns_error_envelope()
    {
        var pipeName = "pumex-test-plugin-" + Guid.NewGuid().ToString("N");

        var server = new PluginIpcServer(pipeName, []);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.RunAsync(cts.Token);

        try
        {
            await Task.Delay(50, cts.Token);

            var response = await SendAsync(pipeName, "missing", new(), cts.Token);

            Assert.False(response["success"]!.GetValue<bool>());
            Assert.Contains("missing", response["error"]!.GetValue<string>());
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { /* expected */ }
        }
    }

    [Fact]
    public async Task Handler_exception_becomes_error_envelope()
    {
        var pipeName = "pumex-test-plugin-" + Guid.NewGuid().ToString("N");

        var server = new PluginIpcServer(pipeName, [new ThrowingHandler()]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.RunAsync(cts.Token);

        try
        {
            await Task.Delay(50, cts.Token);

            var response = await SendAsync(pipeName, "boom", new(), cts.Token);

            Assert.False(response["success"]!.GetValue<bool>());
            Assert.Contains("boom-from-handler", response["error"]!.GetValue<string>());
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { /* expected */ }
        }
    }

    private static async Task<JsonObject> SendAsync(string pipeName, string command, Dictionary<string, string> args, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2_000, ct);

        var request = new IpcRequest(command, args);
        var json = JsonSerializer.Serialize(request, PumexJsonContext.Default.IpcRequest);
        var buf = Encoding.UTF8.GetBytes(json);
        await pipe.WriteAsync(BitConverter.GetBytes(buf.Length), ct);
        await pipe.WriteAsync(buf, ct);
        await pipe.FlushAsync(ct);

        var lenBuf = new byte[4];
        await pipe.ReadExactlyAsync(lenBuf, ct);
        var length = BitConverter.ToInt32(lenBuf, 0);
        var responseBuf = new byte[length];
        await pipe.ReadExactlyAsync(responseBuf, ct);
        var responseJson = Encoding.UTF8.GetString(responseBuf);

        return (JsonObject)JsonNode.Parse(responseJson)!;
    }

    private sealed class EchoHandler : IPluginCommandHandler
    {
        public string Command => "echo";
        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var msg = request.Args.TryGetValue("msg", out var v) ? v : "";
            return Task.FromResult<JsonNode?>(JsonValue.Create(msg));
        }
    }

    private sealed class ThrowingHandler : IPluginCommandHandler
    {
        public string Command => "boom";
        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
            => throw new InvalidOperationException("boom-from-handler");
    }
}
