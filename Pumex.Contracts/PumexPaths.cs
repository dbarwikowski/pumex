namespace Pumex.Contracts;

public static class PumexPaths
{
    private static readonly string _defaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pumex");

    public static string Root { get; } =
        Environment.GetEnvironmentVariable("PUMEX_HOME") ?? _defaultRoot;

    // Default home → canonical pipe name so existing installs need no changes.
    // Custom home → short hex slug derived from the path so two daemons can
    // coexist on the same machine without competing for the same pipe.
    public static string PipeName { get; } = Root == _defaultRoot
        ? "pumex-daemon"
        : "pumex-" + Convert.ToHexString(
              System.Security.Cryptography.SHA256.HashData(
                  System.Text.Encoding.UTF8.GetBytes(Root)))[..8].ToLowerInvariant();

    public static string IndexDb => Path.Combine(Root, "index.db");
    public static string GlobalConfig => Path.Combine(Root, "config.json");
    public static string Plugins => Path.Combine(Root, "plugins");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);

    public const string VaultMarkerDir = ".pumex";
    public const string VaultConfigFile = "config.json";

    public static string? FindVaultRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, VaultMarkerDir)))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
