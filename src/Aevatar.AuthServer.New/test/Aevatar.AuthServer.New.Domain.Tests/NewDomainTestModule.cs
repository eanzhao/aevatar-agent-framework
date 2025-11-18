using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.New;

[DependsOn(
    typeof(NewDomainModule),
    typeof(NewTestBaseModule)
)]
public class NewDomainTestModule : AbpModule
{

}
