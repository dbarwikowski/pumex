using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class TaskHandlersTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly TestDbFixture _fx;
    private readonly NoteParser _parser = new();
    private readonly string _today = DateTime.Now.ToString("yyyy-MM-dd");

    public TaskHandlersTests()
    {
        _fx = new TestDbFixture();
        _fx.Vaults.AddVaultAsync("test", _vault.Path).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _fx.Dispose();
        _vault.Dispose();
    }

    private static IpcRequest Req(string command, params (string Key, string Value)[] args)
        => new(command, args.ToDictionary(a => a.Key, a => a.Value));

    private async Task<TaskResult> CreateAsync(string name, string? date = null, string? content = null)
    {
        var handler = new TaskCreateHandler(_fx.Vaults, _fx.InlineIndex);
        var args = new List<(string, string)> { ("vault", "test"), ("name", name) };
        if (date is not null) args.Add(("date", date));
        if (content is not null) args.Add(("content", content));
        return (TaskResult)(await handler.HandleAsync(Req("task:create", args.ToArray()), CancellationToken.None))!;
    }

    // ---- create -------------------------------------------------------------

    [Fact]
    public async Task Create_makes_folder_and_note_with_scaffolded_frontmatter()
    {
        var result = await CreateAsync("write report", date: "2026-06-09");

        var expectedDir = Path.Combine(_vault.Path, "tasks", "task_2026-06-09_00");
        Assert.Equal(Path.Combine(expectedDir, "write_report.md"), result.Path);
        Assert.Equal("write_report", result.Name);
        Assert.True(File.Exists(result.Path));

        var fm = File.ReadAllText(result.Path).Replace("\r\n", "\n");
        Assert.Contains("status: NEW", fm);
        Assert.Contains("type: TASK", fm);
        Assert.Contains("name: write_report", fm);
        Assert.Contains("created: 2026-06-09", fm);
        Assert.Contains("updated: 2026-06-09", fm);
    }

    [Fact]
    public async Task Create_increments_the_counter_for_same_day_tasks()
    {
        var a = await CreateAsync("alpha", date: "2026-06-09");
        var b = await CreateAsync("beta", date: "2026-06-09");

        Assert.Contains("task_2026-06-09_00", a.Path);
        Assert.Contains("task_2026-06-09_01", b.Path);
    }

    [Fact]
    public async Task Create_writes_body_from_content()
    {
        var result = await CreateAsync("withbody", date: "2026-06-09", content: "do the thing");
        var text = File.ReadAllText(result.Path).Replace("\r\n", "\n");
        Assert.Contains("do the thing", text);
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("qmark?")]
    [InlineData("emoji✓")]
    public async Task Create_rejects_names_with_illegal_characters(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(name, date: "2026-06-09"));
    }

    // ---- status -------------------------------------------------------------

    [Fact]
    public async Task Status_get_returns_current_status()
    {
        await CreateAsync("statget", date: "2026-06-09");
        var handler = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);

        var status = (string)(await handler.HandleAsync(
            Req("task:status", ("vault", "test"), ("name", "statget")), CancellationToken.None))!;

        Assert.Equal("NEW", status);
    }

    [Fact]
    public async Task Status_set_to_DONE_stamps_completed_and_updated()
    {
        await CreateAsync("finishme", date: "2026-06-01");
        var handler = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);

        var summary = (TaskSummary)(await handler.HandleAsync(
            Req("task:status", ("vault", "test"), ("name", "finishme"), ("value", "DONE")),
            CancellationToken.None))!;

        Assert.Equal("DONE", summary.Status);
        Assert.Equal(_today, summary.Completed);
        Assert.Equal(_today, summary.Updated);
    }

    [Fact]
    public async Task Status_set_to_non_DONE_clears_completed()
    {
        await CreateAsync("reopen", date: "2026-06-01");
        var handler = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);

        await handler.HandleAsync(Req("task:status", ("vault", "test"), ("name", "reopen"), ("value", "DONE")),
            CancellationToken.None);
        var summary = (TaskSummary)(await handler.HandleAsync(
            Req("task:status", ("vault", "test"), ("name", "reopen"), ("value", "BLOCKED")),
            CancellationToken.None))!;

        Assert.Equal("BLOCKED", summary.Status);
        Assert.Equal("", summary.Completed);
    }

    [Fact]
    public async Task Status_set_rejects_illegal_status_value()
    {
        await CreateAsync("badstatus", date: "2026-06-09");
        var handler = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            Req("task:status", ("vault", "test"), ("name", "badstatus"), ("value", "in progress")),
            CancellationToken.None));
    }

    // ---- list ---------------------------------------------------------------

    [Fact]
    public async Task List_returns_tasks_newest_first()
    {
        await CreateAsync("older", date: "2026-06-01");
        await CreateAsync("newer", date: "2026-06-09");
        var handler = new TaskListHandler(_parser, _fx.Vaults);

        var tasks = (List<TaskSummary>)(await handler.HandleAsync(
            Req("task:list", ("vault", "test")), CancellationToken.None))!;

        Assert.Equal(2, tasks.Count);
        Assert.Equal("newer", tasks[0].Name);
        Assert.Equal("older", tasks[1].Name);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        await CreateAsync("keep", date: "2026-06-09");
        await CreateAsync("done1", date: "2026-06-08");
        var status = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);
        await status.HandleAsync(Req("task:status", ("vault", "test"), ("name", "done1"), ("value", "DONE")),
            CancellationToken.None);

        var handler = new TaskListHandler(_parser, _fx.Vaults);
        var done = (List<TaskSummary>)(await handler.HandleAsync(
            Req("task:list", ("vault", "test"), ("status", "DONE")), CancellationToken.None))!;
        Assert.Single(done);
        Assert.Equal("done1", done[0].Name);

        var open = (List<TaskSummary>)(await handler.HandleAsync(
            Req("task:list", ("vault", "test"), ("open", "true")), CancellationToken.None))!;
        Assert.Single(open);
        Assert.Equal("keep", open[0].Name);
    }

    // ---- attach -------------------------------------------------------------

    [Fact]
    public async Task Attach_moves_the_file_into_the_task_folder_and_links_it()
    {
        var task = await CreateAsync("attachhere", date: "2026-06-09");
        var source = Path.Combine(_vault.Path, "diagram.png");
        File.WriteAllText(source, "PNGDATA");

        var handler = new TaskAttachHandler(_fx.Vaults, _fx.InlineIndex);
        var result = (TaskAttachResult)(await handler.HandleAsync(
            Req("task:attach", ("vault", "test"), ("name", "attachhere"), ("file", source)),
            CancellationToken.None))!;

        Assert.False(File.Exists(source)); // moved, not copied
        Assert.True(File.Exists(result.AttachmentPath));
        Assert.Equal(Path.GetDirectoryName(task.Path), Path.GetDirectoryName(result.AttachmentPath));

        var body = File.ReadAllText(task.Path).Replace("\r\n", "\n");
        Assert.Contains("[[diagram.png]]", body);
    }

    [Fact]
    public async Task Attach_errors_when_destination_already_exists()
    {
        var task = await CreateAsync("dupattach", date: "2026-06-09");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(task.Path)!, "doc.txt"), "existing");
        var source = Path.Combine(_vault.Path, "doc.txt");
        File.WriteAllText(source, "incoming");

        var handler = new TaskAttachHandler(_fx.Vaults, _fx.InlineIndex);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            Req("task:attach", ("vault", "test"), ("name", "dupattach"), ("file", source)),
            CancellationToken.None));
    }

    // ---- resolution ---------------------------------------------------------

    [Fact]
    public async Task Status_throws_when_task_name_not_found()
    {
        var handler = new TaskStatusHandler(_fx.Vaults, _fx.InlineIndex);
        await Assert.ThrowsAsync<FileNotFoundException>(() => handler.HandleAsync(
            Req("task:status", ("vault", "test"), ("name", "ghost")), CancellationToken.None));
    }
}

public class NoteCheckboxHandlersTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly TestDbFixture _fx;

    public NoteCheckboxHandlersTests()
    {
        _fx = new TestDbFixture();
        _fx.Vaults.AddVaultAsync("test", _vault.Path).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _fx.Dispose();
        _vault.Dispose();
    }

    private static IpcRequest Req(string command, params (string Key, string Value)[] args)
        => new(command, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task NoteTasks_lists_the_notes_checkboxes()
    {
        var path = _vault.WriteNote("todo.md", "- [ ] buy milk\n- [x] call bob\n");
        var handler = new NoteTasksHandler(_fx.Vaults, _fx.Notes);

        var items = (List<CheckboxItem>)(await handler.HandleAsync(
            Req("note:tasks", ("vault", "test"), ("path", path)), CancellationToken.None))!;

        Assert.Equal(2, items.Count);
        Assert.Equal("buy milk", items[0].Text);
        Assert.False(items[0].Checked);
        Assert.True(items[1].Checked);
    }

    [Fact]
    public async Task NoteCheck_toggles_a_checkbox_and_persists_it()
    {
        var path = _vault.WriteNote("todo.md", "- [ ] buy milk\n- [x] call bob\n");
        var handler = new NoteCheckHandler(_fx.Vaults, _fx.Notes, _fx.InlineIndex);

        var result = (CheckboxToggleResult)(await handler.HandleAsync(
            Req("note:check", ("vault", "test"), ("path", path), ("index", "1")),
            CancellationToken.None))!;

        Assert.Equal(1, result.Index);
        Assert.True(result.Checked);

        var saved = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("- [x] buy milk", saved);
        Assert.Contains("- [x] call bob", saved); // untouched
    }
}
