using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// CQRS Demo - Pure HTTP Test Client
/// 
/// Usage:
///   dotnet run -- --api http://localhost:5000
///   dotnet run -- --api http://localhost:5000 --verbose
/// 
/// Requires: HttpApi.Host running (Local or Orleans mode)
/// </summary>

var apiUrl = GetArg(args, "--api") ?? "http://localhost:44351";
var verbose = args.Contains("--verbose");

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║      CQRS Demo - HTTP Test Client            ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine($"\n📡 API: {apiUrl}");
Console.WriteLine();

using var client = new HttpClient { BaseAddress = new Uri(apiUrl) };

try
{
    // Test 1: Health Check
    Console.WriteLine("1️⃣  Health Check...");
    var health = await client.GetAsync("/api/agent-demo/health");
    if (health.IsSuccessStatusCode)
    {
        Console.WriteLine($"   ✅ Status: {health.StatusCode}");
    }
    else
    {
        Console.WriteLine($"   ❌ Status: {health.StatusCode}");
        Console.WriteLine("   Make sure HttpApi.Host is running!");
        return;
    }

    // Test 2: Create Agent
    Console.WriteLine("\n2️⃣  Creating Agent...");
    var createResponse = await client.PostAsync("/api/agent-demo/agents", null);
    
    if (!createResponse.IsSuccessStatusCode)
    {
        var error = await createResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"   ❌ Failed: {createResponse.StatusCode}");
        if (verbose) Console.WriteLine($"   {error}");
        return;
    }
    
    var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
    var agentId = createResult.GetProperty("agentId").GetString();
    Console.WriteLine($"   ✅ Agent ID: {agentId}");
    Console.WriteLine($"   ✅ Description: {createResult.GetProperty("description").GetString()}");

    // Test 3: Send Messages (this triggers state changes)
    Console.WriteLine("\n3️⃣  Sending Messages (triggers state projection)...");
    for (int i = 1; i <= 3; i++)
    {
        var msgResponse = await client.PostAsJsonAsync($"/api/agent-demo/agents/{agentId}/messages", new
        {
            message = $"Test message {i}"
        });
        var msgResult = await msgResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"   Message {i}: {msgResult.GetProperty("response").GetString()}");
    }

    // Wait for state projection
    Console.WriteLine("\n⏳ Waiting for state projection (2s)...");
    await Task.Delay(2000);

    // Test 4: Get Agent Stats (from agent directly)
    Console.WriteLine("\n4️⃣  Agent Statistics...");
    var statsResponse = await client.GetAsync($"/api/agent-demo/agents/{agentId}/stats");
    if (statsResponse.IsSuccessStatusCode)
    {
        var stats = await statsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"   ✅ Processed Events: {stats.GetProperty("processedEventsCount").GetInt32()}");
        Console.WriteLine($"   ✅ Last Message: {stats.GetProperty("lastMessage").GetString()}");
    }
    else
    {
        Console.WriteLine($"   ⚠️ Stats not available: {statsResponse.StatusCode}");
    }

    // Test 5: Query State from ES (via StateQueryController)
    Console.WriteLine("\n5️⃣  Query State from Elasticsearch...");
    var agentType = "Aevatar.App.Agents.Agents.SimpleBusinessAgent";
    var stateResponse = await client.GetAsync($"/api/states/{agentType}/{agentId}");
    
    if (stateResponse.IsSuccessStatusCode)
    {
        var state = await stateResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"   ✅ State retrieved from ES:");
        
        try
        {
            var json = JsonDocument.Parse(state);
            var formatted = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            foreach (var line in formatted.Split('\n'))
            {
                Console.WriteLine($"      {line}");
            }
        }
        catch
        {
            Console.WriteLine($"      {state}");
        }
    }
    else
    {
        Console.WriteLine($"   ⚠️ State not in ES yet: {stateResponse.StatusCode}");
        if (verbose)
        {
            var body = await stateResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"      {body}");
        }
        Console.WriteLine("   💡 Tip: Check if IStateProjector is configured in HttpApi.Host");
    }

    // Test 6: Count Query
    Console.WriteLine("\n6️⃣  Count States in ES...");
    var countResponse = await client.GetAsync($"/api/states/{agentType}/count");
    if (countResponse.IsSuccessStatusCode)
    {
        var countResult = await countResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"   ✅ Total documents: {countResult.GetProperty("count").GetInt64()}");
    }
    else
    {
        Console.WriteLine($"   ⚠️ Count failed: {countResponse.StatusCode}");
    }

    Console.WriteLine("\n════════════════════════════════════════════════");
    Console.WriteLine("✅ CQRS Test Complete!");
    Console.WriteLine("════════════════════════════════════════════════\n");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"\n❌ Connection failed: {ex.Message}");
    Console.WriteLine($"   Make sure HttpApi.Host is running at {apiUrl}");
    Console.WriteLine("\n📖 To start HttpApi.Host:");
    Console.WriteLine("   cd apps/Aevatar.App/src/Aevatar.App.HttpApi.Host");
    Console.WriteLine("   dotnet run");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    if (verbose) Console.WriteLine(ex.StackTrace);
}

static string? GetArg(string[] args, string key)
{
    var idx = Array.IndexOf(args, key);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}
