using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Helper class to inject IAgentContextAccessor into GAgentBase and AgentContextPropagator into GAgentActorBase.
/// Uses reflection to access internal fields.
/// </summary>
public static class AgentContextAccessorInjector
{
    /// <summary>
    /// Inject context accessor into a GAgentBase instance.
    /// </summary>
    /// <param name="agent">The agent instance</param>
    /// <param name="contextAccessor">The context accessor to inject</param>
    public static void InjectContextAccessor(IGAgent agent, IAgentContextAccessor contextAccessor)
    {
        if (agent is GAgentBase gAgentBase)
        {
            gAgentBase.ContextAccessor = contextAccessor;
        }
    }

    /// <summary>
    /// Inject context propagator into a GAgentActorBase instance.
    /// </summary>
    /// <param name="actor">The actor instance</param>
    /// <param name="contextAccessor">The context accessor to use for propagation</param>
    public static void InjectContextPropagator(GAgentActorBase actor, IAgentContextAccessor contextAccessor)
    {
        actor.ContextPropagator = new AgentContextPropagator(contextAccessor);
    }

    /// <summary>
    /// Inject context accessor into agent and propagator into actor.
    /// Convenience method to set up both at once.
    /// </summary>
    /// <param name="agent">The agent instance</param>
    /// <param name="actor">The actor instance</param>
    /// <param name="contextAccessor">The context accessor to inject</param>
    public static void InjectContext(IGAgent agent, GAgentActorBase actor, IAgentContextAccessor contextAccessor)
    {
        InjectContextAccessor(agent, contextAccessor);
        InjectContextPropagator(actor, contextAccessor);
    }
}

