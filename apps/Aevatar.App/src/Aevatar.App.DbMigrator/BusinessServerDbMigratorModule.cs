using Aevatar.App.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.App.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AppMongoDbModule),
    typeof(AppApplicationContractsModule)
)]
public class AppDbMigratorModule : AbpModule
{
}
