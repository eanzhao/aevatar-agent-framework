# MCP Tool Demo

This demo shows how to integrate MCP (Model Context Protocol) servers with an Aevatar AI agent.

## What It Does

- **Custom MCP Server**: Demonstrates registering a custom MCP server (`simple-mcp-server.mjs`)
- **Local Tools**: Shows mixing MCP tools with local tools (`TimeTool`)
- **Tool Discovery**: Automatically discovers and registers tools from the MCP server

## Prerequisites

1. **Node.js** (v18+)
2. **.NET 10**
3. **MCP SDK** (installed via npm)

## Setup

### 1. Install MCP Server Dependencies

```bash
cd examples/MCPToolDemo
npm install
```

### 2. Configure LLM Provider

```bash
cp appsettings.secrets.template.json appsettings.secrets.json
```

Edit `appsettings.secrets.json` and add your API key:

```json
{
  "LLMProviders": {
    "deepseek": {
      "ApiKey": "YOUR_API_KEY_HERE"
    }
  }
}
```

## Running the Demo

```bash
dotnet run --project examples/MCPToolDemo/MCPToolDemo.csproj
```

## What's Included

### Custom MCP Server (`simple-mcp-server.mjs`)

A simple MCP server with two tools:

- **`get_weather`**: Returns simulated weather data for a city
- **`calculate_sum`**: Adds two numbers

### Local Tools

- **`TimeTool`**: Returns current system time

## Code Highlights

### Registering the MCP Server

```csharp
protected override async Task RegisterToolsAsync()
{
    // Register MCP Server via npx
    await RegisterMCPServerViaNpxAsync(
        "@modelcontextprotocol/server-github",
        "github"
    );
}
```
