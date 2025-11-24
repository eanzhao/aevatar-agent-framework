using System;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;

namespace Aevatar.BusinessServer.MongoDB;

[DependsOn(
    typeof(BusinessServerApplicationTestModule),
    typeof(BusinessServerMongoDbModule)
)]
public class BusinessServerMongoDbTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = BusinessServerMongoDbFixture.GetRandomConnectionString();
        });
    }
}
