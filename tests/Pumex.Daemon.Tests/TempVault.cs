namespace Pumex.Daemon.Tests;

/// <summary>
/// Disposable scratch directory for tests that need to write notes to disk.
/// </summary>
internal sealed class TempVault : IDisposable
{
    public string Path { get; }

    public TempVault()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteNote(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }
}
