using Volo.Abp.Modularity;

namespace Aevatar.App;

[DependsOn(
    typeof(AppApplicationModule),
    typeof(AppDomainTestModule)
)]
public class AppApplicationTestModule : AbpModule
{

}
