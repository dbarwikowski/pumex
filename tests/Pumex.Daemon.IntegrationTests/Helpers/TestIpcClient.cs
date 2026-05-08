using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pumex.Contracts;

namespace Pumex.Daemon.IntegrationTests.Helpers;

/// <summary>
/// Minimal IPC client that talks to a named pipe specified by the test (so we
/// don't collide with a real daemon listening on the production pipe name).
/// Mirrors the wire format of <see cref="Pumex.Cli.IpcClient"/> exactly.
/// </summary>
internal sealed class TestIpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _pipeName;

    public TestIpcClient(string pipeName) => _pipeName = pipeName;

    public async Task<IpcResponse<T>> SendAsync<T>(string command, Dictionary<string, string>? args = null, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, ct);

        var json = JsonSerializer.Serialize(new IpcRequest(command, args ?? new()), JsonOptions);
        await WriteAsync(pipe, json, ct);
        var responseJson = await ReadAsync(pipe, ct)
            ?? throw new IOException("Daemon closed the connection without a response");
        return JsonSerializer.Deserialize<IpcResponse<T>>(responseJson, JsonOptions)
            ?? new IpcResponse<T>(false, default, "Empty response");
    }

    private static async Task WriteAsync(PipeStream pipe, string message, CancellationToken ct)
    {
        var buf = Encoding.UTF8.GetBytes(message);
        await pipe.WriteAsync(BitConverter.GetBytes(buf.Length), ct);
        await pipe.WriteAsync(buf, ct);
        await pipe.FlushAsync(ct);
    }

    private static async Task<string?> ReadAsync(PipeStream pipe, CancellationToken ct)
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
}
