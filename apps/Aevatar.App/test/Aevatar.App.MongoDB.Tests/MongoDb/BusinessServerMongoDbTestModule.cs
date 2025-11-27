using System;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;

namespace Aevatar.App.MongoDB;

[DependsOn(
    typeof(AppApplicationTestModule),
    typeof(AppMongoDbModule)
)]
public class AppMongoDbTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = AppMongoDbFixture.GetRandomConnectionString();
        });
    }
}
