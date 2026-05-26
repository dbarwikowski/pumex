using System.CommandLine;
using Pumex.Cli;
using Pumex.Cli.Commands;

var root = new RootCommand("pumex — headless markdown vault");

// Replace System.CommandLine's default `--version` action so the flag reports both
// the CLI and the running daemon's version.
var versionOpt = root.Options.OfType<System.CommandLine.VersionOption>().FirstOrDefault();
if (versionOpt is not null)
    versionOpt.Action = new VersionOptionAction();

root.Add(PingCommand.Build());
root.Add(NewVaultCommand.Build());
root.Add(VaultCommand.Build());
root.Add(SearchCommand.Build());
root.Add(TagsCommand.Build());
root.Add(BacklinksCommand.Build());
root.Add(ListCommand.Build());
root.Add(ReadCommand.Build());
root.Add(CreateCommand.Build());
root.Add(AppendCommand.Build());
root.Add(DeleteCommand.Build());
root.Add(PropCommand.Build());
root.Add(DailyCommand.Build());
root.Add(DaemonCommand.Build());

return await root.Parse(args).InvokeAsync();
