namespace Pumex.Plugin.Sdk;

// Shape of manifest.json next to the plugin DLL/executable. Strictly required:
// SchemaVersion, Name, Version. For in-proc plugins: EntryAssembly + EntryType.
// For out-of-proc plugins (001B): Executable. Optional: Description, Commands
// (advisory pre-handshake — the authoritative list comes from OnInitAsync /
// plugin:register), SdkVersion (for compat gating), ExecutableArgs (extra
// command-line args passed to Executable on spawn).
public record PluginManifest(
    int SchemaVersion,
    string Name,
    string Version,
    string? EntryAssembly = null,
    string? EntryType = null,
    string? Description = null,
    IReadOnlyList<string>? Commands = null,
    string? SdkVersion = null,
    string? Executable = null,
    IReadOnlyList<string>? ExecutableArgs = null);
