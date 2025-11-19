using Volo.Abp.Modularity;

namespace Aevatar.AuthServer;

[DependsOn(
    typeof(AuthServerDomainModule),
    typeof(AuthServerTestBaseModule)
)]
public class AuthServerDomainTestModule : AbpModule
{

}
