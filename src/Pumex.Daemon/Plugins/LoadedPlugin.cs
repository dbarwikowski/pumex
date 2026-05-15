using System.Runtime.Loader;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.Plugins;

internal sealed record LoadedPlugin(
    PluginManifest Manifest,
    PumexPlugin Instance,
    AssemblyLoadContext LoadContext,
    IReadOnlyList<IPluginCommandHandler> Handlers);
