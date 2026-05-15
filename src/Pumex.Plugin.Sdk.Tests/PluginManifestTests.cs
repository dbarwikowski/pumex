using System.Text.Json;

namespace Pumex.Plugin.Sdk.Tests;

public class PluginManifestTests
{
    [Fact]
    public void Round_trip_minimal_manifest()
    {
        var manifest = new PluginManifest(
            SchemaVersion: 1,
            Name: "my-plugin",
            Version: "0.1.0",
            EntryAssembly: "MyPlugin.dll",
            EntryType: "My.Plugin.Entry");

        var json = JsonSerializer.Serialize(manifest, PumexPluginJsonContext.Default.PluginManifest);
        var roundTripped = JsonSerializer.Deserialize(json, PumexPluginJsonContext.Default.PluginManifest);

        Assert.Equal(manifest, roundTripped);
    }

    [Fact]
    public void Round_trip_full_manifest()
    {
        var manifest = new PluginManifest(
            SchemaVersion: 1,
            Name: "x",
            Version: "0.2.0",
            EntryAssembly: "X.dll",
            EntryType: "X.Entry",
            Description: "does x",
            Commands: ["x:foo", "x:bar"],
            SdkVersion: "0.2.0",
            Executable: "x.exe");

        var json = JsonSerializer.Serialize(manifest, PumexPluginJsonContext.Default.PluginManifest);
        var rt = JsonSerializer.Deserialize(json, PumexPluginJsonContext.Default.PluginManifest);

        Assert.NotNull(rt);
        // Record-level Equals doesn't structurally compare IReadOnlyList<string>;
        // assert collection contents explicitly, then compare the manifest with
        // Commands stripped so the rest of the fields go through record equality.
        Assert.Equal(manifest.Commands, rt!.Commands);
        Assert.Equal(manifest with { Commands = null }, rt with { Commands = null });
    }

    [Fact]
    public void Uses_camelCase_keys()
    {
        var manifest = new PluginManifest(
            SchemaVersion: 1,
            Name: "x",
            Version: "0.1",
            EntryAssembly: "X.dll",
            EntryType: "X.Entry");

        var json = JsonSerializer.Serialize(manifest, PumexPluginJsonContext.Default.PluginManifest);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"entryAssembly\"", json);
        Assert.Contains("\"entryType\"", json);
    }
}
