using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Automatically propagates context through event publishing.
/// Integrates with IEventPublisher to inject context into EventEnvelope.
/// </summary>
public class AgentContextPropagator
{
    private readonly IAgentContextAccessor _contextAccessor;

    /// <summary>
    /// Creates a new context propagator.
    /// </summary>
    /// <param name="contextAccessor">Context accessor for reading current context</param>
    public AgentContextPropagator(IAgentContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    /// <summary>
    /// Inject current context into EventEnvelope before publishing.
    /// </summary>
    /// <param name="envelope">The EventEnvelope to inject context into</param>
    public void InjectContext(EventEnvelope envelope)
    {
        var context = _contextAccessor.Context;
        if (context == null) return;

        var metadata = AgentContextSerializer.Serialize(context);
        foreach (var (key, value) in metadata)
        {
            envelope.ContextMetadata[key] = value;
        }
    }

    /// <summary>
    /// Extract context from EventEnvelope and return a new context instance.
    /// Does not modify the current ambient context - use AgentContextScope for that.
    /// </summary>
    /// <param name="envelope">The EventEnvelope to extract context from</param>
    /// <returns>New context instance with extracted values, or null if no context metadata</returns>
    public IAgentContext? ExtractContext(EventEnvelope envelope)
    {
        if (envelope.ContextMetadata.Count == 0)
            return null;

        var context = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context);
        return context;
    }

    /// <summary>
    /// Extract context from EventEnvelope and apply to current ambient context.
    /// Warning: This modifies the current ambient context. Use AgentContextScope for safe scoped access.
    /// </summary>
    /// <param name="envelope">The EventEnvelope to extract context from</param>
    public void ApplyContext(EventEnvelope envelope)
    {
        if (envelope.ContextMetadata.Count == 0) return;

        var context = _contextAccessor.Context ?? new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context);
        _contextAccessor.Context = context;
    }
}

