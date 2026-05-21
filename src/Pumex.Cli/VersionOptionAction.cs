using System.CommandLine;
using System.CommandLine.Invocation;
using Pumex.Contracts;
using Pumex.Ipc;

namespace Pumex.Cli;

internal sealed class VersionOptionAction : AsynchronousCommandLineAction
{
    public override Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default) =>
        VersionCommand.RunAsync(new IpcClient(), VersionInfo.Current);
}
