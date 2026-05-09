using Pumex.Contracts;

namespace Pumex.Cli;

/// <summary>
/// Vault scope passed alongside an IPC request. Either an explicit vault
/// (by name or path), an auto-discovered one from CWD, or none. Auto-discovered
/// scope is "soft" — if the path isn't registered, the daemon falls back to
/// global rather than erroring.
/// </summary>
public record VaultScope(string? Name, string? Path, bool IsExplicit)
{
    public static VaultScope Global { get; } = new(null, null, IsExplicit: false);

    public bool HasScope => Name is not null || Path is not null;

    public void ApplyTo(Dictionary<string, string> args)
    {
        if (Name is not null) args["vault"] = Name;
        if (Path is not null)
        {
            args["vaultPath"] = Path;
            if (!IsExplicit) args["vaultOptional"] = "1";
        }
    }

    /// <summary>
    /// Base directory for resolving user-supplied relative paths. Only set
    /// when the vault was picked explicitly — auto-discovery preserves the
    /// CWD-relative behavior users already expect.
    /// </summary>
    public string? RelativeRoot => IsExplicit ? Path : null;
}

public static class VaultArgs
{
    /// <summary>
    /// Strips <c>--vault NAME</c>, <c>--vault-path PATH</c> and <c>--all</c>
    /// from <paramref name="args"/>. Falls back to <see cref="PumexPaths.FindVaultRoot"/>
    /// against the current working directory when no override is given.
    /// </summary>
    public static (VaultScope Scope, string[] Remaining) Extract(string[] args, string? cwd = null)
    {
        string? name = null;
        string? explicitPath = null;
        var explicitGlobal = false;
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vault" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--vault-path" when i + 1 < args.Length:
                    explicitPath = System.IO.Path.GetFullPath(args[++i]);
                    break;
                case "--all":
                    explicitGlobal = true;
                    break;
                default:
                    remaining.Add(args[i]);
                    break;
            }
        }

        if (explicitGlobal)
            return (VaultScope.Global, remaining.ToArray());

        if (name is not null || explicitPath is not null)
            return (new VaultScope(name, explicitPath, IsExplicit: true), remaining.ToArray());

        var root = PumexPaths.FindVaultRoot(cwd ?? Environment.CurrentDirectory);
        return (new VaultScope(null, root, IsExplicit: false), remaining.ToArray());
    }

    /// <summary>
    /// Canonicalises a user-supplied note reference before sending it to the
    /// daemon. Rules:
    /// <list type="bullet">
    ///   <item>Absolute path → canonical absolute.</item>
    ///   <item>Bare name (no <c>/</c> or <c>\</c>) → passed through raw so the
    ///     daemon can resolve via the index.</item>
    ///   <item>Relative path with separators → joined with <c>--vault-path</c>
    ///     when explicit, left raw under <c>--vault NAME</c> (daemon knows the
    ///     root), otherwise resolved CWD-relative.</item>
    /// </list>
    /// </summary>
    public static string ResolvePath(VaultScope scope, string path)
    {
        if (System.IO.Path.IsPathFullyQualified(path))
            return System.IO.Path.GetFullPath(path);

        var hasSeparator = path.Contains('/') || path.Contains('\\');
        if (!hasSeparator) return path;

        if (scope.IsExplicit && scope.Name is not null && scope.Path is null)
            return path;
        var root = scope.RelativeRoot;
        return root is null
            ? System.IO.Path.GetFullPath(path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
    }
}
