using System.Text.Json.Serialization;
using Pumex.Contracts;

namespace Pumex.Mcp;

[JsonSerializable(typeof(List<TagCount>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<NoteSummary>))]
[JsonSerializable(typeof(List<VaultRecord>))]
[JsonSerializable(typeof(List<PropertyEntry>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal partial class PumexMcpOutputContext : JsonSerializerContext { }
