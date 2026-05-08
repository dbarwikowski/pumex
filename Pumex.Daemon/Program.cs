using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pumex.Contracts;
using Pumex.Daemon;
using Pumex.Daemon.Ipc;

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
        s.AddSingleton<ICommandHandler, NoteReadHandler>();
        s.AddSingleton<ICommandHandler, NoteCreateHandler>();
        s.AddSingleton<ICommandHandler, NoteAppendHandler>();
        s.AddSingleton<ICommandHandler, PropertyListHandler>();
        s.AddSingleton<ICommandHandler, PropertyGetHandler>();
        s.AddSingleton<ICommandHandler, PropertySetHandler>();
        s.AddHostedService<IpcServer>();
    })
    .Build();

await host.RunAsync();
