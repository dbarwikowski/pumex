using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pumex.Contracts;
using Pumex.Daemon;
using Pumex.Daemon.Ipc;

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
        s.AddHostedService<IpcServer>();
    })
    .Build();

await host.RunAsync();
