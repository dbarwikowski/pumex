using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class DailyHandlersTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly TestDbFixture _fx;
    private readonly NoteParser _parser = new();
    private readonly long _vaultId;
    private readonly VaultRecord _vaultRecord;

    public DailyHandlersTests()
    {
        _fx = new TestDbFixture();
        _vaultId = _fx.Vaults.AddVaultAsync("test", _vault.Path).GetAwaiter().GetResult();
        _vaultRecord = _fx.Vaults.GetVaultByPathAsync(_vault.Path).GetAwaiter().GetResult()!;
    }

    public void Dispose()
    {
        _fx.Dispose();
        _vault.Dispose();
    }

    private static IpcRequest Req(string command, params (string Key, string Value)[] args)
        => new(command, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public void PathFor_uses_vault_config_defaults()
    {
        var config = new VaultConfig("v", DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);
        var path = Daily.PathFor(_vaultRecord, config, new DateTime(2026, 5, 8));

        Assert.EndsWith(Path.Combine("daily", "2026-05-08.md"), path);
    }

    [Fact]
    public void PathFor_honours_custom_folder_and_format()
    {
        var config = new VaultConfig("v", DateTimeOffset.UtcNow, VaultConfig.CurrentVersion,
            DailyFolder: "journal",
            DailyFormat: "yyyy_MM_dd");
        var path = Daily.PathFor(_vaultRecord, config, new DateTime(2026, 5, 8));

        Assert.EndsWith(Path.Combine("journal", "2026_05_08.md"), path);
    }

    [Fact]
    public async Task DailyRead_creates_file_when_missing_and_inline_indexes_it()
    {
        var handler = new DailyReadHandler(_parser, _fx.Vaults, _fx.InlineIndex);

        var result = (NoteContent)(await handler.HandleAsync(
            Req("daily:read", ("vault", "test"), ("date", "2026-05-08")),
            CancellationToken.None))!;

        Assert.True(File.Exists(result.Path));
        Assert.EndsWith(Path.Combine("daily", "2026-05-08.md"), result.Path);
        // Inline-indexed: count == 1 immediately, no watcher delay.
        Assert.Single(await _fx.Notes.GetAllPathsAsync(_vaultId));
    }

    [Fact]
    public async Task DailyAppend_creates_file_when_missing_and_writes_content()
    {
        var handler = new DailyAppendHandler(_parser, _fx.Vaults, _fx.InlineIndex);

        var result = (NotePathResult)(await handler.HandleAsync(
            Req("daily:append",
                ("vault", "test"),
                ("date", "2026-05-08"),
                ("content", "first entry")),
            CancellationToken.None))!;

        var written = File.ReadAllText(result.Path).Replace("\r\n", "\n");
        Assert.Equal("first entry\n", written);
    }

    [Fact]
    public async Task DailyAppend_appends_to_existing_file()
    {
        var handler = new DailyAppendHandler(_parser, _fx.Vaults, _fx.InlineIndex);
        await handler.HandleAsync(
            Req("daily:append", ("vault", "test"), ("date", "2026-05-08"), ("content", "first")),
            CancellationToken.None);

        await handler.HandleAsync(
            Req("daily:append", ("vault", "test"), ("date", "2026-05-08"), ("content", "second")),
            CancellationToken.None);

        var path = Daily.PathFor(_vaultRecord, await Daily.LoadConfigAsync(_vaultRecord, default), new DateTime(2026, 5, 8));
        var written = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("first", written);
        Assert.Contains("second", written);
    }

    [Fact]
    public async Task DailyRead_throws_when_no_vault_in_scope()
    {
        var handler = new DailyReadHandler(_parser, _fx.Vaults, _fx.InlineIndex);

        await Assert.ThrowsAsync<ArgumentException>(async () => await handler.HandleAsync(
            Req("daily:read", ("date", "2026-05-08")),
            CancellationToken.None));
    }
}

public class InlineIndexTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly TestDbFixture _fx;
    private readonly NoteParser _parser = new();
    private readonly long _vaultId;

    public InlineIndexTests()
    {
        _fx = new TestDbFixture();
        _vaultId = _fx.Vaults.AddVaultAsync("v", _vault.Path).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _fx.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public async Task NoteCreate_makes_the_note_searchable_immediately_without_a_watcher()
    {
        var handler = new NoteCreateHandler(_fx.Vaults, _fx.Notes, _fx.InlineIndex);
        var path = Path.Combine(_vault.Path, "fresh.md");
        var request = new IpcRequest("note:create", new()
        {
            ["path"] = path,
            ["content"] = "marker word: capybara",
            ["vaultPath"] = _vault.Path,
        });

        await handler.HandleAsync(request, CancellationToken.None);

        // Immediate search — would race the 200 ms watcher debounce without
        // inline indexing. With it, the note is in the index before the
        // handler returns.
        var hits = await _fx.Search.SearchAsync("capybara", vaultId: _vaultId);
        Assert.Single(hits);
        Assert.Equal(path, hits[0].Path);
    }

    [Fact]
    public async Task NoteDelete_removes_the_note_from_the_index_immediately()
    {
        var handler = new NoteDeleteHandler(_fx.Vaults, _fx.Notes, _fx.InlineIndex);
        var path = Path.Combine(_vault.Path, "doomed.md");
        File.WriteAllText(path, "body");
        await _fx.UpsertAsync(_vaultId, [_parser.Parse(path)]);
        Assert.Single(await _fx.Notes.GetAllPathsAsync(_vaultId));

        var request = new IpcRequest("note:delete", new()
        {
            ["path"] = path,
            ["vaultPath"] = _vault.Path,
        });
        await handler.HandleAsync(request, CancellationToken.None);

        Assert.Empty(await _fx.Notes.GetAllPathsAsync(_vaultId));
    }
}
