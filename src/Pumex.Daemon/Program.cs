using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pumex.Contracts;
using Pumex.Daemon;
using Pumex.Daemon.Ipc;
using Pumex.Daemon.Plugins;
using Pumex.Plugin.Sdk;

// Required for self-contained single-file publish — without this the SQLite
// native bundle isn't initialised and Microsoft.Data.Sqlite throws on first use.
SQLitePCL.Batteries.Init();

PumexPaths.EnsureRoot();

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .UseWindowsService()
    .ConfigureServices(s =>
    {
        s.AddSingleton<IndexDb>(_ => new IndexDb(PumexPaths.IndexDb));
        s.AddSingleton<NoteParser>();
        s.AddSingleton<IndexingServiceFactory>();
        s.AddSingleton<VaultIndexingOrchestrator>();
        s.AddHostedService(sp => sp.GetRequiredService<VaultIndexingOrchestrator>());

        s.AddSingleton<ICommandHandler, PingHandler>();
        s.AddSingleton<ICommandHandler, SearchHandler>();
        s.AddSingleton<ICommandHandler, TagsHandler>();
        s.AddSingleton<ICommandHandler, BacklinksHandler>();
        s.AddSingleton<ICommandHandler, VaultsHandler>();
        s.AddSingleton<ICommandHandler, VaultAddHandler>();
        s.AddSingleton<ICommandHandler, VaultRemoveHandler>();
        s.AddSingleton<ICommandHandler, NoteReadHandler>();
        s.AddSingleton<ICommandHandler, NoteCreateHandler>();
        s.AddSingleton<ICommandHandler, NoteAppendHandler>();
        s.AddSingleton<ICommandHandler, NoteDeleteHandler>();
        s.AddSingleton<ICommandHandler, NoteListHandler>();
        s.AddSingleton<ICommandHandler, PropertyListHandler>();
        s.AddSingleton<ICommandHandler, PropertyGetHandler>();
        s.AddSingleton<ICommandHandler, PropertySetHandler>();
        s.AddSingleton<ICommandHandler, DailyReadHandler>();
        s.AddSingleton<ICommandHandler, DailyAppendHandler>();

        // Plugin host: registry holds the dispatch table for both in-proc and
        // out-of-proc plugins; host gives in-proc plugins read-only access to
        // the indexed primitives. The loader must run *before* IpcServer
        // accepts so plugin commands are in the dispatch table when the first
        // client connects (BackgroundServices start in registration order).
        s.AddSingleton<PluginRegistry>();
        s.AddSingleton<IPumexHost, InProcessPumexHost>();
        s.AddSingleton<PluginLoader>();
        s.AddHostedService(sp => sp.GetRequiredService<PluginLoader>());

        // 001B control-plane handlers: register/unregister/list/load for
        // out-of-proc plugins. plugin:register is what spawned plugins call
        // back on; plugin:list / plugin:load drive the `pumex plugin` CLI.
        s.AddSingleton<ICommandHandler, PluginRegisterHandler>();
        s.AddSingleton<ICommandHandler, PluginUnregisterHandler>();
        s.AddSingleton<ICommandHandler, PluginListHandler>();
        s.AddSingleton<ICommandHandler, PluginLoadHandler>();

        s.AddHostedService<IpcServer>();
    })
    .Build();

await host.RunAsync();
