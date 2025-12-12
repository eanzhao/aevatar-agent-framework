// =========================================================================
//  AxiomReasoning.AppHost - Aspire Orchestration for Axiom Reasoning
// =========================================================================
//  Axiom Reasoning: Multi-Agent Axiom/Theorem Reasoning Platform
//
//  Notes:
//    - The service already exposes /health and is OTEL-ready.
//    - We pin HTTP port to 5001 to match the service banner and docs.
// =========================================================================

var builder = DistributedApplication.CreateBuilder(args);

Console.WriteLine("🧩 Aspire AppHost - Axiom Reasoning");
Console.WriteLine("===================================");

// -------------------------------------------------------------------------
// Axiom Reasoning Service
// -------------------------------------------------------------------------
// Public endpoints:
//   - Web UI:  http://localhost:5001
//   - Health:  http://localhost:5001/health
//   - API:     http://localhost:5001/api/sessions
var axiomReasoning = builder.AddProject<Projects.Aevatar_AxiomReasoning>("axiom-reasoning")
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

Console.WriteLine("✅ Axiom Reasoning: Axiom/Theorem Reasoning Service");
Console.WriteLine("");
Console.WriteLine("📊 服务端点:");
Console.WriteLine("   - Web UI:  http://localhost:5001");
Console.WriteLine("   - Health:  http://localhost:5001/health");
Console.WriteLine("   - API:     http://localhost:5001/api/sessions");
Console.WriteLine("");

var app = builder.Build();
await app.RunAsync();


