using DbaTool.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureDefaultBindingUrl();
builder.Services.AddDbaToolServices(builder.Configuration);

var app = builder.Build();

app.UseDbaToolPipeline();
await app.InitializeDbaToolAsync();
app.MapDbaToolEndpoints();

await app.RunAsync();
