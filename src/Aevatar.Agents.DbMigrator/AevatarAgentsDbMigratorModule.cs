using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.MongoDB;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.MongoDB;
using Microsoft.AspNetCore.Identity;
using Aevatar.Agents.AuthServer.MongoDB;
using Aevatar.Agents.AuthServer.Data;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Aevatar.Agents.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpIdentityMongoDbModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpOpenIddictMongoDbModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpPermissionManagementMongoDbModule),
    typeof(AbpMongoDbModule)
)]
public class AevatarAgentsDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        // Distributed cache configuration
        Configure<AbpDistributedCacheOptions>(options => 
        { 
            options.KeyPrefix = "Aevatar:"; 
        });
        
        // User options configuration
        Configure<UsersOptions>(configuration.GetSection("User"));
        
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

