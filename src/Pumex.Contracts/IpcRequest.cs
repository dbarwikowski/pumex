namespace Pumex.Contracts;

public record IpcRequest(
    string Command,
    Dictionary<string, string> Args
);
