using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Cognitive Mesh App (actor runtime aware worker)
var meshApp = builder.AddProject("cognitive-mesh-app", "../CognitiveMesh.App/CognitiveMesh.App.csproj")
    .WithEnvironment("COGNITIVE_MESH_RUNTIME", "orleans")
    .WithEnvironment("COGNITIVE_MESH_STRATEGY_SET", "uot");

// TODO: Attach Orleans silo / dashboards / vector storage once available.

// Blueprint storage placeholder - points to future DSL registry service.
builder.AddParameter("MeshDslSchemaVersion", "0.1");

builder.Build().Run();
