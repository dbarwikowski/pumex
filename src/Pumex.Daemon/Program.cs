using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pumex.Contracts;
using Pumex.Daemon;
using Pumex.Daemon.Ipc;
using Serilog;

// Required for self-contained single-file publish — without this the SQLite
// native bundle isn't initialised and Microsoft.Data.Sqlite throws on first use.
SQLitePCL.Batteries.Init();

PumexPaths.EnsureRoot();
Directory.CreateDirectory(PumexPaths.LogsDir);

// Daily-rolled file logs at $PUMEX_HOME/logs/daemon-<date>.log, retain 7.
// Detached spawns have no console; without file logging the daemon is silent.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: Path.Combine(PumexPaths.LogsDir, "daemon-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console()
    .CreateLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSystemd()
        .UseWindowsService()
        .UseSerilog()
        .ConfigureServices(s =>
        {
            // IndexDbContext is the shared connection; IndexSchema applies pragmas,
            // migration, and DDL once on first resolution.
            s.AddSingleton<IndexDbContext>(_ =>
            {
                var ctx = new IndexDbContext(PumexPaths.IndexDb);
                new IndexSchema(ctx).Apply();
                return ctx;
            });

            // Repositories — order matters for the graph:
            // VaultRepository depends on INoteRepository.
            s.AddSingleton<INoteRepository, NoteRepository>();
            s.AddSingleton<ILinkRepository, LinkRepository>();
            s.AddSingleton<IVaultRepository, VaultRepository>();
            s.AddSingleton<ISearchRepository, SearchRepository>();

            // Markdown is the only format parser compiled in by the framework;
            // Format parsers are added by their own work items (JSON below; CSV is
            // renderer-only). The registry dispatches by extension and falls back
            // to RawTextParser for any active extension without a dedicated parser.
            s.AddSingleton<NoteParser>();
            s.AddSingleton<IFormatParser>(sp => sp.GetRequiredService<NoteParser>());
            s.AddSingleton<IFormatParser, JsonFormatParser>();
            s.AddSingleton<RawTextParser>();
            s.AddSingleton<FormatParserRegistry>();
            s.AddSingleton<IInlineIndex, InlineIndex>();
            s.AddSingleton<IndexingServiceFactory>();
            s.AddSingleton<VaultIndexingOrchestrator>();
            s.AddHostedService(sp => sp.GetRequiredService<VaultIndexingOrchestrator>());

            s.AddSingleton<ICommandHandler, PingHandler>();
            s.AddSingleton<ICommandHandler>(_ => new VersionHandler(VersionInfo.Current));
            s.AddSingleton<ICommandHandler, StopHandler>();
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
            s.AddHostedService<IpcServer>();
        })
        .Build();

    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
