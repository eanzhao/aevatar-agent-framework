namespace Aevatar.Agents.AI.WithTool.MCP.Configuration;

/// <summary>
/// Configuration for connecting to an MCP server.
/// Supports both remote servers (HTTP) and local servers (stdio via npx/uvx).
/// </summary>
public class MCPServerConfig
{
    /// <summary>
    /// Server name for identification.
    /// </summary>
    public string Name { get; set; } = "MCP Server";

    /// <summary>
    /// Transport type (Stdio for local, Http for remote).
    /// </summary>
    public MCPTransportType TransportType { get; set; } = MCPTransportType.Stdio;

    // ===== Stdio Transport Configuration (for npx/uvx) =====

    /// <summary>
    /// Command to execute (e.g., "npx", "uvx", "node").
    /// Required for Stdio transport.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Command-line arguments (e.g., ["-y", "@modelcontextprotocol/server-github"]).
    /// Required for Stdio transport.
    /// </summary>
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Environment variables for the process.
    /// Optional for Stdio transport.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Working directory for the process.
    /// Optional for Stdio transport.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    // ===== HTTP Transport Configuration (for remote servers) =====

    /// <summary>
    /// Server URL (e.g., "https://mcp.example.com").
    /// Required for Http transport.
    /// </summary>
    public string? ServerUrl { get; set; }

    /// <summary>
    /// Authentication token for HTTP requests.
    /// Optional for Http transport.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Additional HTTP headers.
    /// Optional for Http transport.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    // ===== Common Configuration =====

    /// <summary>
    /// Connection timeout in milliseconds.
    /// Default: 120000 (2 minutes) to allow for npx package installation.
    /// </summary>
    public int TimeoutMs { get; set; } = 120000;

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to cache tool definitions.
    /// </summary>
    public bool CacheToolDefinitions { get; set; } = true;

    /// <summary>
    /// Cache expiration time in minutes.
    /// </summary>
    public int CacheExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Custom metadata for this server connection.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // ===== Helper Methods =====

    /// <summary>
    /// Creates a configuration for a local MCP server via npx.
    /// </summary>
    public static MCPServerConfig CreateNpxConfig(string packageName, string? name = null)
    {
        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({packageName})",
            TransportType = MCPTransportType.Stdio,
            Command = "npx",
            Arguments = ["-y", packageName]
        };
    }

    /// <summary>
    /// Creates a configuration for a local MCP server via uvx.
    /// </summary>
    public static MCPServerConfig CreateUvxConfig(string packageName, string? name = null)
    {
        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({packageName})",
            TransportType = MCPTransportType.Stdio,
            Command = "uvx",
            Arguments = [packageName]
        };
    }

    /// <summary>
    /// Creates a configuration for a remote HTTP MCP server.
    /// </summary>
    public static MCPServerConfig CreateHttpConfig(string serverUrl, string? authToken = null, string? name = null)
    {
        return new MCPServerConfig
        {
            Name = name ?? $"MCP Server ({serverUrl})",
            TransportType = MCPTransportType.Http,
            ServerUrl = serverUrl,
            AuthToken = authToken
        };
    }
}

/// <summary>
/// MCP transport type.
/// </summary>
public enum MCPTransportType
{
    /// <summary>
    /// Stdio transport for local servers (npx/uvx).
    /// </summary>
    Stdio,

    /// <summary>
    /// HTTP transport for remote servers.
    /// </summary>
    Http
}
