using Volo.Abp.Modularity;

namespace Aevatar.BusinessServer;

[DependsOn(
    typeof(BusinessServerApplicationModule),
    typeof(BusinessServerDomainTestModule)
)]
public class BusinessServerApplicationTestModule : AbpModule
{

}
