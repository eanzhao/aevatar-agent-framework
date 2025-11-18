using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.Agents.DbMigrator;

[DependsOn(typeof(AbpAutofacModule))]
public class AevatarAgentsDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Configure services for database migration
        // Add specific configurations as needed
    }
}

