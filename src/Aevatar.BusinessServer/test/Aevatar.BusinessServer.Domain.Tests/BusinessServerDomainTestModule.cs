using Volo.Abp.Modularity;

namespace Aevatar.BusinessServer;

[DependsOn(
    typeof(BusinessServerDomainModule),
    typeof(BusinessServerTestBaseModule)
)]
public class BusinessServerDomainTestModule : AbpModule
{

}
