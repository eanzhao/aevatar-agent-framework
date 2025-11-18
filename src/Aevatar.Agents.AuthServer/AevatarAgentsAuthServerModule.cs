using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.MongoDB;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.MongoDB;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using Aevatar.Agents.AuthServer.Grants;
using Aevatar.Agents.AuthServer.MongoDB;

namespace Aevatar.Agents.AuthServer;

[DependsOn(
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpOpenIddictMongoDbModule),
    typeof(AbpIdentityMongoDbModule),
    typeof(AbpPermissionManagementMongoDbModule),
    typeof(AbpAuthorizationModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpMongoDbModule),
    typeof(AevatarAgentsAuthServerGrantsModule)
)]
public class AevatarAgentsAuthServerModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        // OpenIddict configuration
        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddServer(options =>
            {
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.SetIssuer(new Uri(configuration["AuthServer:IssuerUri"] ?? "https://localhost:44300"));
                
                // Certificate configuration
                var useProductionCert = configuration.GetValue<bool>("OpenIddict:Certificate:UseProductionCertificate");
                var certPath = configuration["OpenIddict:Certificate:CertificatePath"] ?? "openiddict.pfx";
                var certPassword = configuration["OpenIddict:Certificate:CertificatePassword"] ?? "00000000-0000-0000-0000-000000000000";
                
                if (useProductionCert && File.Exists(certPath))
                {
                    PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
                    {
                        options.AddDevelopmentEncryptionAndSigningCertificate = false;
                    });
                    options.AddProductionEncryptionAndSigningCertificate(certPath, certPassword);
                }
                
                options.DisableAccessTokenEncryption();
                
                // Token expiration configuration
                int.TryParse(configuration["ExpirationHour"], out int expirationHour);
                if (expirationHour > 0)
                {
                    options.SetAccessTokenLifetime(DateTime.Now.AddHours(expirationHour) - DateTime.Now);
                }
            });
            
            builder.AddValidation(options =>
            {
                options.AddAudiences("Aevatar");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });
        
        // Extension grant types configuration (if needed)
        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.Configure(openIddictServerOptions =>
            {
                // Add custom Grant Types here if needed
                // openIddictServerOptions.GrantTypes.Add("signature");
            });
        });
    }
    
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        // Localization configuration
        Configure<AbpLocalizationOptions>(options =>
        {
            // Add language support
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
            options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));
            // Add more languages as needed...
        });
        
        // Bundling configuration
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle => { bundle.AddFiles("/global-styles.css"); }
            );
        });
        
        // Auditing configuration
        Configure<AbpAuditingOptions>(options =>
        {
            options.ApplicationName = "AuthServer";
            options.IsEnabled = false; // Enable as needed
        });
        
        // Application URL configuration
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"] ?? "https://localhost:44300";
            options.Applications["Angular"].RootUrl = configuration["App:ClientUrl"] ?? "https://localhost:4200";
        });
        
        // Background job configuration
        Configure<AbpBackgroundJobOptions>(options => 
        { 
            options.IsJobExecutionEnabled = false; 
        });
        
        // Distributed cache configuration
        Configure<AbpDistributedCacheOptions>(options => 
        { 
            options.KeyPrefix = "Aevatar:"; 
        });
        
        // Redis data protection configuration
        var redisConnectionString = configuration["Redis:Configuration"];
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(redisConnectionString);
                context.Services
                    .AddDataProtection()
                    .PersistKeysToStackExchangeRedis(redis, "Aevatar-DataProtection-Keys")
                    .SetApplicationName("AevatarAuthServer");
            }
            catch (Exception ex)
            {
                // Log error but don't fail startup if Redis is unavailable
                // Logger will be available after service provider is built
                System.Diagnostics.Debug.WriteLine($"Failed to connect to Redis for data protection: {ex.Message}");
            }
        }
        
        // Health checks
        context.Services.AddHealthChecks();
        
        // MongoDB configuration for custom entities
        context.Services.AddMongoDbContext<AevatarAgentsAuthServerMongoDbContext>(options =>
        {
            options.AddDefaultRepositories();
        });
        
        // MVC options configuration
        Configure<MvcOptions>(options =>
        {
            // Add custom conventions if needed
        });
    }
    
    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();
        
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        
        app.UseAbpRequestLocalization();
        
        if (!env.IsDevelopment())
        {
            app.UseErrorPage();
        }
        
        app.UseHealthChecks("/health");
        app.UseCorrelationId();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();
        app.UseUnitOfWork();
        app.UseAuthorization();
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}

