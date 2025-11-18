using Aevatar.AuthServer.New.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.New.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(NewMongoDbModule),
    typeof(NewApplicationContractsModule)
)]
public class NewDbMigratorModule : AbpModule
{
}
