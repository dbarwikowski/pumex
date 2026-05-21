using System.Reflection;

namespace Pumex.Contracts;

public static class VersionInfo
{
    public static string Current => For(Assembly.GetEntryAssembly());

    public static string For(Assembly? assembly) =>
        StripBuildMetadata(assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static string StripBuildMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unknown";
        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }
}
