using Volo.Abp.Modularity;

namespace Aevatar.App;

[DependsOn(
    typeof(AppDomainModule),
    typeof(AppTestBaseModule)
)]
public class AppDomainTestModule : AbpModule
{

}
