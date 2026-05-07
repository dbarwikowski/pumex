namespace Pumex.Contracts;

public static class PumexPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pumex");

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
