# ABP 后端管理界面配置方案

## 概述

本文档说明如何为 Aevatar.Agents.AuthServer 项目添加 ABP Framework 的后端管理界面（基于 LeptonX Lite 主题）。

## 一、NuGet 包配置

### 1.1 更新 `Directory.Packages.props`

在 `Directory.Packages.props` 中添加以下 ABP 包版本（版本 8.2.1 与现有包保持一致）：

```xml
<!-- ABP Framework - Admin UI Packages -->
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Bootstrap" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Bundling" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.Libs" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Account.Web" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Account.Application" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Account.HttpApi" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Identity.Application" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Identity.HttpApi" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Identity.Web" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.Application" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.HttpApi" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.Web" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Serilog" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Localization" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.UI.Navigation" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Auditing" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.BackgroundJobs" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Caching" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.OpenIddict.AspNetCore" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.OpenIddict.Domain" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.OpenIddict.MongoDB" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.OpenIddict.ExtensionGrantTypes" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.Identity.MongoDB" Version="8.2.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.MongoDB" Version="8.2.1" />
<PackageVersion Include="Microsoft.AspNetCore.DataProtection.StackExchangeRedis" Version="10.0.0" />
```

### 1.2 更新 `Aevatar.Agents.AuthServer.csproj`

在项目文件中添加包引用：

```xml
<ItemGroup>
  <!-- Existing packages -->
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  <PackageReference Include="Volo.Abp.Core" />
  <PackageReference Include="Volo.Abp.Ddd.Domain" />
  <PackageReference Include="Volo.Abp.AutoMapper" />
  
  <!-- ABP Admin UI packages -->
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Bootstrap" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Bundling" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared" />
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.Libs" />
  <PackageReference Include="Volo.Abp.Account.Web" />
  <PackageReference Include="Volo.Abp.Account.Application" />
  <PackageReference Include="Volo.Abp.Account.HttpApi" />
  <PackageReference Include="Volo.Abp.Identity.Application" />
  <PackageReference Include="Volo.Abp.Identity.HttpApi" />
  <PackageReference Include="Volo.Abp.Identity.Web" />
  <PackageReference Include="Volo.Abp.PermissionManagement.Application" />
  <PackageReference Include="Volo.Abp.PermissionManagement.HttpApi" />
  <PackageReference Include="Volo.Abp.PermissionManagement.Web" />
  <PackageReference Include="Volo.Abp.AspNetCore.Serilog" />
  <PackageReference Include="Volo.Abp.Localization" />
  <PackageReference Include="Volo.Abp.UI.Navigation" />
  <PackageReference Include="Volo.Abp.Auditing" />
  <PackageReference Include="Volo.Abp.BackgroundJobs" />
  <PackageReference Include="Volo.Abp.Caching" />
  <PackageReference Include="Volo.Abp.OpenIddict.AspNetCore" />
  <PackageReference Include="Volo.Abp.OpenIddict.Domain" />
  <PackageReference Include="Volo.Abp.OpenIddict.MongoDB" />
  <PackageReference Include="Volo.Abp.OpenIddict.ExtensionGrantTypes" />
  <PackageReference Include="Volo.Abp.Identity.MongoDB" />
  <PackageReference Include="Volo.Abp.PermissionManagement.MongoDB" />
  <PackageReference Include="Microsoft.AspNetCore.DataProtection.StackExchangeRedis" />
</ItemGroup>
```

## 二、Module 配置

### 2.1 创建/更新 `AevatarAgentsAuthServerModule.cs`

在 `src/Aevatar.Agents.AuthServer/` 目录下创建或更新 Module 文件：

```csharp
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.MongoDB;
using Volo.Abp.UI.Navigation.Urls;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using Aevatar.Agents.AuthServer.Grants;

namespace Aevatar.Agents.AuthServer;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpIdentityWebModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpPermissionManagementWebModule),
    typeof(AbpOpenIddictMongoDbModule),
    typeof(AbpIdentityMongoDbModule),
    typeof(AbpPermissionManagementMongoDbModule),
    typeof(AbpAuthorizationModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AevatarAgentsAuthServerGrantsModule) // 你的 Grants 模块
)]
public class AevatarAgentsAuthServerModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        // OpenIddict 配置
        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddServer(options =>
            {
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.SetIssuer(new Uri(configuration["AuthServer:IssuerUri"] ?? "https://localhost:44300"));
                
                // 证书配置
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
                
                // Token 过期时间配置
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
        
        // 扩展授权类型配置（如果需要）
        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.Configure(openIddictServerOptions =>
            {
                // 添加自定义 Grant Types
                // openIddictServerOptions.GrantTypes.Add("signature");
            });
        });
    }
    
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        // MVC 库配置
        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });
        
        // 本地化配置
        Configure<AbpLocalizationOptions>(options =>
        {
            // 添加语言支持
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
            options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));
            // 可以添加更多语言...
        });
        
        // 资源打包配置
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle => { bundle.AddFiles("/global-styles.css"); }
            );
        });
        
        // 审计配置
        Configure<AbpAuditingOptions>(options =>
        {
            options.ApplicationName = "AuthServer";
            options.IsEnabled = false; // 根据需要启用
        });
        
        // 应用 URL 配置
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"] ?? "https://localhost:44300";
            options.Applications["Angular"].RootUrl = configuration["App:ClientUrl"] ?? "https://localhost:4200";
        });
        
        // 后台作业配置
        Configure<AbpBackgroundJobOptions>(options => 
        { 
            options.IsJobExecutionEnabled = false; 
        });
        
        // 分布式缓存配置
        Configure<AbpDistributedCacheOptions>(options => 
        { 
            options.KeyPrefix = "Aevatar:"; 
        });
        
        // Redis 数据保护配置
        var redisConnectionString = configuration["Redis:Configuration"];
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
            context.Services
                .AddDataProtection()
                .PersistKeysToStackExchangeRedis(redis, "Aevatar-DataProtection-Keys")
                .SetApplicationName("AevatarAuthServer");
        }
        
        // 健康检查
        context.Services.AddHealthChecks();
        
        // MVC 选项配置
        Configure<MvcOptions>(options =>
        {
            // 可以添加自定义约定
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
```

## 三、Program.cs 配置

### 3.1 更新 `Program.cs`

确保 `Program.cs` 使用 ABP 的 WebApplication 模式：

```csharp
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Aevatar.Agents.AuthServer;

namespace Aevatar.Agents.AuthServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.Console())
            .CreateLogger();
        
        try
        {
            Log.Information("Starting Aevatar.Agents.AuthServer.");
            
            var builder = WebApplication.CreateBuilder(args);
            
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            
            await builder.AddApplicationAsync<AevatarAgentsAuthServerModule>();
            
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }
            
            Log.Fatal(ex, "Aevatar AuthServer terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
```

## 四、配置文件

### 4.1 `appsettings.json` 配置示例

```json
{
  "App": {
    "SelfUrl": "https://localhost:44300",
    "ClientUrl": "https://localhost:4200"
  },
  "AuthServer": {
    "IssuerUri": "https://localhost:44300"
  },
  "OpenIddict": {
    "Certificate": {
      "UseProductionCertificate": false,
      "CertificatePath": "openiddict.pfx",
      "CertificatePassword": "00000000-0000-0000-0000-000000000000"
    }
  },
  "Redis": {
    "Configuration": "localhost:6379"
  },
  "ExpirationHour": "24"
}
```

## 五、静态资源文件

### 5.1 创建 `wwwroot/global-styles.css`

在项目根目录创建 `wwwroot` 文件夹，并添加 `global-styles.css`：

```css
/* 全局样式自定义 */
```

### 5.2 项目文件配置

确保 `Aevatar.Agents.AuthServer.csproj` 包含：

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>

<ItemGroup>
  <Content Include="wwwroot\**\*" />
</ItemGroup>
```

## 六、依赖模块

### 6.1 创建 `AevatarAgentsAuthServerGrantsModule`

如果还没有创建，需要在 `Aevatar.Agents.AuthServer.Grants` 项目中创建：

```csharp
using Volo.Abp.Modularity;

namespace Aevatar.Agents.AuthServer.Grants;

[DependsOn(
    typeof(AbpOpenIddictExtensionGrantTypesModule)
)]
public class AevatarAgentsAuthServerGrantsModule : AbpModule
{
    // 配置扩展授权类型
}
```

## 七、MongoDB 模块依赖

如果使用 MongoDB，需要确保有对应的 MongoDB 模块：

```csharp
// 在 AevatarAgentsAuthServerModule 的 DependsOn 中已包含：
// typeof(AbpOpenIddictMongoDbModule)
// typeof(AbpIdentityMongoDbModule)
// typeof(AbpPermissionManagementMongoDbModule)
```

## 八、总结

完成以上配置后，ABP 后端管理界面将提供：

1. **用户管理界面** - 通过 `/Identity/Users` 访问
2. **角色管理界面** - 通过 `/Identity/Roles` 访问
3. **权限管理界面** - 通过 `/PermissionManagement` 访问
4. **账户管理** - 登录、注册、密码重置等功能
5. **LeptonX Lite 主题** - 现代化的管理界面 UI

## 九、注意事项

1. **数据库连接**：确保 MongoDB 连接字符串正确配置
2. **Redis 连接**：如果使用 Redis 缓存和数据保护，确保连接字符串正确
3. **证书配置**：生产环境需要配置 SSL 证书
4. **CORS**：如果前端应用在不同域名，需要配置 CORS
5. **本地化资源**：根据需要添加本地化资源文件

## 十、验证步骤

1. 运行项目：`dotnet run --project src/Aevatar.Agents.AuthServer`
2. 访问管理界面：`https://localhost:44300`
3. 使用默认管理员账户登录（需要在数据库迁移时创建）
4. 验证各个管理功能是否正常

