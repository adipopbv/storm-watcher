using StormWatcher.Detection.Host;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDetectionApplication();
builder.Services.AddDetectionInfrastructure(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
