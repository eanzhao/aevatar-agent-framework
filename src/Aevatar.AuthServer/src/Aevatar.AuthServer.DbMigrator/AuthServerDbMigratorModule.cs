using Aevatar.AuthServer.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AuthServerMongoDbModule),
    typeof(AuthServerApplicationContractsModule)
)]
public class AuthServerDbMigratorModule : AbpModule
{
}
