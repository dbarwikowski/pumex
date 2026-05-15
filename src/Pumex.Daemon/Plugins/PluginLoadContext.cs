using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Pumex.Daemon.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string mainAssemblyPath)
        : base(isCollectible: false)
        => _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Plugin loading is inherently runtime-resolved; trimming is incompatible with the plugin pathway.")]
    protected override Assembly? Load(AssemblyName name)
    {
        // Defer Pumex.* and the shared abstractions to the default context so
        // the plugin sees the same types the daemon does. Without this, a
        // plugin instantiating IPluginCommandHandler from its own ALC copy
        // would not satisfy the daemon's interface check on the way back in.
        if (name.Name is { } n &&
            (n.StartsWith("Pumex.", StringComparison.Ordinal) ||
             n is "Microsoft.Extensions.Logging.Abstractions"
                or "Microsoft.Extensions.Hosting.Abstractions"
                or "Microsoft.Extensions.DependencyInjection.Abstractions"))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
