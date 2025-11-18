using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.New;

[DependsOn(
    typeof(NewApplicationModule),
    typeof(NewDomainTestModule)
)]
public class NewApplicationTestModule : AbpModule
{

}
