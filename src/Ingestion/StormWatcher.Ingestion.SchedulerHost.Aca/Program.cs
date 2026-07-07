using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StormWatcher.Ingestion.Scheduling;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddIngestionApplication();
builder.Services.AddIngestionInfrastructure(builder.Configuration);

using var host = builder.Build();
await host.StartAsync();

var pollDispatchService = host.Services.GetRequiredService<PollDispatchService>();
await pollDispatchService.RunOnceAsync(CancellationToken.None);

await host.StopAsync();
