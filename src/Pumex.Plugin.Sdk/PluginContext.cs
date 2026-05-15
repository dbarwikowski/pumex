using Microsoft.Extensions.Logging;

namespace Pumex.Plugin.Sdk;

public record PluginContext(
    PluginManifest Manifest,
    IPumexHost Host,
    string DataDirectory,
    ILogger Logger);
