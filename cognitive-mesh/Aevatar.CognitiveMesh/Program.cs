using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Core services
builder.Services.AddSingleton<CognitiveDslCompiler>();
builder.Services.AddHostedService<MeshWorkbench>();

var app = builder.Build();
await app.RunAsync();
