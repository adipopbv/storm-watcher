var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddLocationCatalogApplication();
builder.Services.AddLocationCatalogInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello World!"); // template placeholder, replaced when real endpoints land

await app.RunAsync();
