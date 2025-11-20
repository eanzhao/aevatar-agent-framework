using Aevatar.Agents.Abstractions;

namespace Aevatar.Playground;

public abstract class AgentDemoBase
{
    public abstract Task RunAsync(IGAgentActorFactory factory, ILogger<Program> logger);
}