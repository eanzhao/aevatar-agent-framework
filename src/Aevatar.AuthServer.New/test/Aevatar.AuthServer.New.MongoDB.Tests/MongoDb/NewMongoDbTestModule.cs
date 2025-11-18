using System;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;

namespace Aevatar.AuthServer.New.MongoDB;

[DependsOn(
    typeof(NewApplicationTestModule),
    typeof(NewMongoDbModule)
)]
public class NewMongoDbTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = NewMongoDbFixture.GetRandomConnectionString();
        });
    }
}
