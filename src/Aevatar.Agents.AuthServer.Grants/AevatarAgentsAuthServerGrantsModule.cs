using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;

namespace Aevatar.Agents.AuthServer.Grants;

[DependsOn(
    typeof(AbpOpenIddictExtensionGrantTypesModule)
)]
public class AevatarAgentsAuthServerGrantsModule : AbpModule
{
    // Extension grant types configuration
    // Grant handlers are registered automatically via dependency injection
}

