using System.CommandLine;

namespace Pumex.Cli.Commands;

/// <summary>
/// Factory for the three vault-scope options shared across most commands.
/// Each command must create its own instances — System.CommandLine options
/// cannot be shared across commands.
/// </summary>
internal static class VaultOptions
{
    internal static Option<string?> Vault() =>
        new("--vault") { Description = "Named vault to use" };

    internal static Option<string?> VaultPath() =>
        new("--vault-path") { Description = "Vault path to use" };

    internal static Option<bool> All() =>
        new("--all") { Description = "Apply to all vaults" };
}
