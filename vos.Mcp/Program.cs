using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Mcp;
using vos.Taproot;

var options = ConsoleOptions.Parse(args);
var builder = Host.CreateApplicationBuilder(args);

// Standard output carries the protocol, so every log line goes to standard error.
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddPlatformServer(ServerProgram.ClientFor(options.MyceliumUrl, options.ApiKey))
    .WithStdioServerTransport();

await builder.Build().RunAsync();
