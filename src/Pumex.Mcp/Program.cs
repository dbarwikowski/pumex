using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pumex.Mcp;

var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.1";

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "pumex", Version = version };
    })
    .WithStdioServerTransport()
    .WithTools<PumexTools>();

await builder.Build().RunAsync();
