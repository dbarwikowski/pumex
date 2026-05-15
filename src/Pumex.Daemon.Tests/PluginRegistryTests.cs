using System.Text.Json.Nodes;
using Pumex.Contracts;
using Pumex.Daemon.Plugins;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.Tests;

public class PluginRegistryTests
{
    [Fact]
    public void Register_then_get_returns_handler()
    {
        var registry = new PluginRegistry();
        var handler = new StubHandler("foo");
        registry.Register("plugin-x", handler);

        Assert.True(registry.TryGet("foo", out var got));
        Assert.Same(handler, got);
    }

    [Fact]
    public void Duplicate_command_throws_naming_first_owner()
    {
        var registry = new PluginRegistry();
        registry.Register("plugin-a", new StubHandler("foo"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register("plugin-b", new StubHandler("foo")));
        Assert.Contains("plugin-a", ex.Message);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var registry = new PluginRegistry();
        registry.Register("p", new StubHandler("Foo"));

        Assert.True(registry.TryGet("foo", out _));
        Assert.True(registry.TryGet("FOO", out _));
    }

    [Fact]
    public void Unknown_command_returns_false()
    {
        var registry = new PluginRegistry();
        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void List_returns_registered_pairs()
    {
        var registry = new PluginRegistry();
        registry.Register("a", new StubHandler("cmd1"));
        registry.Register("b", new StubHandler("cmd2"));

        var pairs = registry.List().OrderBy(p => p.Command).ToList();
        Assert.Equal([("a", "cmd1"), ("b", "cmd2")], pairs);
    }

    [Fact]
    public void RegisterOutOfProcess_then_TryGetOutOfProcess_returns_entry()
    {
        var registry = new PluginRegistry();
        registry.RegisterOutOfProcess("p", "1.0.0", "pumex-plugin-p-xyz", ["p:do", "p:list"]);

        Assert.True(registry.TryGetOutOfProcess("p:do", out var entry));
        Assert.Equal("p", entry.Name);
        Assert.Equal("pumex-plugin-p-xyz", entry.PipeName);
        Assert.Contains("p:do", entry.Commands);
        Assert.Contains("p:list", entry.Commands);
    }

    [Fact]
    public void In_proc_blocks_out_of_proc_registration_on_same_command()
    {
        var registry = new PluginRegistry();
        registry.Register("inproc", new StubHandler("shared"));

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterOutOfProcess("outproc", "0.1.0", "pipe", ["shared"]));
    }

    [Fact]
    public void Out_of_proc_blocks_in_proc_registration_on_same_command()
    {
        var registry = new PluginRegistry();
        registry.RegisterOutOfProcess("outproc", "0.1.0", "pipe", ["shared"]);

        Assert.Throws<InvalidOperationException>(
            () => registry.Register("inproc", new StubHandler("shared")));
    }

    [Fact]
    public void PreRegisterOutOfProcess_is_replaced_by_RegisterOutOfProcess()
    {
        var registry = new PluginRegistry();
        // Manifest pre-declares two commands; plugin claims only one of them
        // plus a third on its handshake. The pre-declared "p:gone" must be
        // dropped, "p:new" must be reachable.
        registry.PreRegisterOutOfProcess("p", "1.0", "pipe", ["p:keep", "p:gone"]);
        registry.RegisterOutOfProcess("p", "1.0", "pipe", ["p:keep", "p:new"]);

        Assert.True(registry.TryGetOutOfProcess("p:keep", out _));
        Assert.True(registry.TryGetOutOfProcess("p:new", out _));
        Assert.False(registry.TryGetOutOfProcess("p:gone", out _));
    }

    [Fact]
    public void Unregister_drops_all_commands_for_the_plugin()
    {
        var registry = new PluginRegistry();
        registry.RegisterOutOfProcess("p", "1.0", "pipe", ["p:a", "p:b"]);
        registry.Unregister("p");

        Assert.False(registry.TryGetOutOfProcess("p:a", out _));
        Assert.False(registry.TryGetOutOfProcess("p:b", out _));
    }

    [Fact]
    public void Unregister_is_idempotent_on_unknown_name()
    {
        var registry = new PluginRegistry();
        registry.Unregister("not-there"); // no throw
    }

    [Fact]
    public void Two_out_of_proc_plugins_cannot_claim_the_same_command()
    {
        var registry = new PluginRegistry();
        registry.RegisterOutOfProcess("a", "1.0", "pipe-a", ["shared"]);

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterOutOfProcess("b", "1.0", "pipe-b", ["shared"]));
    }

    [Fact]
    public void Snapshot_includes_in_proc_and_out_of_proc()
    {
        var registry = new PluginRegistry();
        registry.Register("inproc", new StubHandler("in:one"));
        registry.Register("inproc", new StubHandler("in:two"));
        registry.RegisterOutOfProcess("outproc", "0.2.0", "pipe-o", ["out:do"]);

        var snapshot = registry.Snapshot();

        Assert.Equal(2, snapshot.Count);
        var inProc = snapshot.Single(p => p.Name == "inproc");
        Assert.Equal("in-process", inProc.Kind);
        Assert.Null(inProc.Pipe);
        Assert.Contains("in:one", inProc.Commands);
        Assert.Contains("in:two", inProc.Commands);

        var outProc = snapshot.Single(p => p.Name == "outproc");
        Assert.Equal("out-of-process", outProc.Kind);
        Assert.Equal("pipe-o", outProc.Pipe);
        Assert.Equal("0.2.0", outProc.Version);
    }

    private sealed class StubHandler : IPluginCommandHandler
    {
        public StubHandler(string command) => Command = command;
        public string Command { get; }
        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
            => Task.FromResult<JsonNode?>(null);
    }
}
