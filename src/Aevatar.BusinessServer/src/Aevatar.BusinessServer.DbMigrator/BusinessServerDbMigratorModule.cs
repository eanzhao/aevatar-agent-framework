using Aevatar.BusinessServer.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.BusinessServer.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BusinessServerMongoDbModule),
    typeof(BusinessServerApplicationContractsModule)
)]
public class BusinessServerDbMigratorModule : AbpModule
{
}
