using Volo.Abp.Modularity;

namespace Aevatar.AuthServer;

[DependsOn(
    typeof(AuthServerApplicationModule),
    typeof(AuthServerDomainTestModule)
)]
public class AuthServerApplicationTestModule : AbpModule
{

}
