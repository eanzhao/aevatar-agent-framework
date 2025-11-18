using Volo.Abp.Modularity;

namespace Aevatar.Agents.AuthServer.Grants;

[DependsOn(
    typeof(AbpOpenIddictDomainModule)
)]
public class AevatarAgentsAuthServerGrantsModule : AbpModule
{
    // Extension grant types configuration
    // Grant handlers are registered automatically via dependency injection
}

