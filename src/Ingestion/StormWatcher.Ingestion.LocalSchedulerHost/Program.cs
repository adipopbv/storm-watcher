using StormWatcher.Ingestion.LocalSchedulerHost;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddIngestionApplication();
builder.Services.AddIngestionInfrastructure(builder.Configuration);
builder.Services.AddIngestionScheduling();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
