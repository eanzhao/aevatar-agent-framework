using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.MongoDB;
using Microsoft.AspNetCore.Identity;
using Aevatar.Agents.AuthServer.MongoDB;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Aevatar.Agents.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpIdentityMongoDbModule),
    typeof(AbpMongoDbModule)
)]
public class AevatarAgentsDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Distributed cache configuration
        Configure<AbpDistributedCacheOptions>(options => 
        { 
            options.KeyPrefix = "Aevatar:"; 
        });
        
        // Identity configuration
        context.Services.AddIdentity<IdentityUser, IdentityRole>()
            .AddDefaultTokenProviders();
        
        // MongoDB context for custom entities
        context.Services.AddMongoDbContext<AevatarAgentsAuthServerMongoDbContext>(options =>
        {
            options.AddDefaultRepositories();
        });
    }
}

