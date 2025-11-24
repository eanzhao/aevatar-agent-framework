using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool;
using Aevatar.Agents.AI.WithTool.MCP;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Logging;
// For IMessage
using Microsoft.Extensions.Logging.Abstractions; // For Timestamp

namespace MCPToolDemo;

/// <summary>
/// An agent that demonstrates MCP tool integration.
/// </summary>
public class MCPAgent : AIGAgentWithToolBase<AevatarAIAgentState>
{
    public MCPAgent() : base()
    {
        // Force initialization
        _ = ToolManager;
    }

    protected override async Task RegisterToolsAsync()
    {
        Logger?.LogInformation("🔧 Starting tool registration...");

        // 1. Register local tool
        await RegisterToolAsync(new TimeTool(), Logger);
        Logger?.LogInformation("✅ Registered local tool: TimeTool");

        // 2. Register custom MCP server
        try
        {
            Logger?.LogInformation("🚀 Registering Simple MCP Server...");
            
            var demoDir = Directory.GetCurrentDirectory();
            var config = new MCPServerConfig
            {
                Name = "simple-demo",
                TransportType = MCPTransportType.Stdio,
                Command = "node",
                Arguments = new List<string> { "simple-mcp-server.mjs" },
                WorkingDirectory = demoDir
            };
            
            await ToolManager.RegisterMCPServerAsync(
                "simple-demo",
                await MCPClientWrapper.CreateAsync(config, NullLogger<MCPClientWrapper>.Instance)
            );
            
            Logger?.LogInformation("✅ Registered Simple MCP Server");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to register MCP Server");
        }
        
        var tools = await GetRegisteredToolsAsync();
        Logger?.LogInformation("🎉 Tool registration complete! Total tools: {Count}", tools.Count);
    }

    public override string SystemPrompt => @"
You are a helpful AI assistant with access to tools.

You have access to:
1. Time tool: Can get the current time.

If the user asks for the time, use the get_current_time tool.

Please be concise and helpful.
";
}