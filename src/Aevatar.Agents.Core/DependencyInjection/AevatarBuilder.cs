using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.DependencyInjection;

internal sealed class AevatarBuilder : IAevatarBuilder
{
    public AevatarBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Properties = new Dictionary<string, object>();
    }

    public IServiceCollection Services { get; }

    public IDictionary<string, object> Properties { get; }
}

