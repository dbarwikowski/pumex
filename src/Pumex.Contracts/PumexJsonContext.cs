using System.Text.Json.Serialization;

namespace Pumex.Contracts;

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse<string>))]
[JsonSerializable(typeof(IpcResponse<object>))]
[JsonSerializable(typeof(IpcResponse<VaultRecord>))]
[JsonSerializable(typeof(IpcResponse<List<VaultRecord>>))]
[JsonSerializable(typeof(IpcResponse<List<SearchResult>>))]
[JsonSerializable(typeof(IpcResponse<List<TagCount>>))]
[JsonSerializable(typeof(IpcResponse<List<string>>))]
[JsonSerializable(typeof(IpcResponse<NoteContent>))]
[JsonSerializable(typeof(IpcResponse<List<NoteSummary>>))]
[JsonSerializable(typeof(IpcResponse<List<PropertyEntry>>))]
[JsonSerializable(typeof(IpcResponse<NotePathResult>))]
[JsonSerializable(typeof(VaultConfig))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PumexJsonContext : JsonSerializerContext { }
