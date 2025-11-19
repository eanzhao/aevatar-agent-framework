using System;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;

namespace Aevatar.AuthServer.MongoDB;

[DependsOn(
    typeof(AuthServerApplicationTestModule),
    typeof(AuthServerMongoDbModule)
)]
public class AuthServerMongoDbTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = AuthServerMongoDbFixture.GetRandomConnectionString();
        });
    }
}
