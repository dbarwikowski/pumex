using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pumex.Contracts;

namespace Pumex.Cli;

public class IpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IpcResponse<T>> SendAsync<T>(string command, Dictionary<string, string>? args = null, int connectTimeoutMs = 2000, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PumexPaths.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(connectTimeoutMs, ct);

        var request = new IpcRequest(command, args ?? new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(request, JsonOptions);
        await WriteMessageAsync(pipe, json, ct);

        var responseJson = await ReadMessageAsync(pipe, ct)
            ?? throw new IOException("Daemon closed the connection without a response");

        return JsonSerializer.Deserialize<IpcResponse<T>>(responseJson, JsonOptions)
            ?? new IpcResponse<T>(false, default, "Empty response");
    }

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
