using System.Text.Json.Serialization;

namespace Pumex.Plugin.Sdk;

[JsonSerializable(typeof(PluginManifest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PumexPluginJsonContext : JsonSerializerContext { }
