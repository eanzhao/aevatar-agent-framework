namespace Aevatar.Agents.Abstractions.Context;

/// <summary>
/// Well-known agent context keys used across the framework.
/// </summary>
public static class AgentContextKeys
{
    /// <summary>
    /// Correlation ID for distributed tracing.
    /// NOTE: No default value - caller must set explicitly per request.
    /// DO NOT use Guid.NewGuid() as default (static field executes only once).
    /// </summary>
    public static readonly AgentContextKey<string> CorrelationId =
        new("CorrelationId");

    /// <summary>
    /// User ID for authentication context.
    /// </summary>
    public static readonly AgentContextKey<string> UserId =
        new("UserId");

    /// <summary>
    /// Language/locale setting.
    /// </summary>
    public static readonly AgentContextKey<string> Language =
        new("Language", "en");

    /// <summary>
    /// Region indicator (e.g., for geo-routing).
    /// </summary>
    public static readonly AgentContextKey<string> Region =
        new("Region");

    /// <summary>
    /// Whether client is in China mainland (for compliance routing).
    /// </summary>
    public static readonly AgentContextKey<bool> IsCN =
        new("IsCN", false);

    /// <summary>
    /// Request timestamp.
    /// </summary>
    public static readonly AgentContextKey<DateTime> RequestTime =
        new("RequestTime");

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public static readonly AgentContextKey<string> TenantId =
        new("TenantId");

    /// <summary>
    /// Allowlist of keys that should be propagated through EventEnvelope.
    /// Only these keys will be serialized for performance and security.
    /// </summary>
    public static readonly HashSet<string> AllowedPropagationKeys = new()
    {
        "CorrelationId",
        "UserId",
        "Language",
        "Region",
        "IsCN",
        "RequestTime",
        "TenantId"
    };
}

