namespace Pumex.Contracts;

public record IpcResponse<T>(
    bool Success,
    T? Data,
    string? Error
);

public static class IpcResponse
{
    public const string PipeName = "pumex-daemon";

    public static IpcResponse<T> Ok<T>(T data) => new(true, data, null);
    public static IpcResponse<object?> Fail(string error) => new(false, null, error);
}
