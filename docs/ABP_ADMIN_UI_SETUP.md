# ABP 后端管理界面配置方案

## 概述

本文档说明如何为 `src/Aevatar.BusinessServer/src/Aevatar.AuthServer` 中现有的 **Aevatar.AuthServer** 项目维护 ABP Framework 的后端管理界面（基于 LeptonX Lite 主题）。请注意：

- 仓库已经内置 AuthServer（见 `AuthServerModule.cs` 与 `Aevatar.AuthServer.csproj`），以下内容用于校验/更新；
- 所有包版本通过 `Directory.Packages.props` 统一管理，当前稳定版本为 **ABP 9.3.1**；
- 如果未来创建单独的 AuthServer 变体，请保持命名与路径与当前实现一致，避免出现过时的 `Aevatar.Agents.AuthServer` 路径。

## 一、NuGet 包配置

### 1.1 更新 `Directory.Packages.props`

`Directory.Packages.props` 已集中声明 ABP 9.3.1 版本。若需校验或同步，请确认以下片段存在（节选自文件尾部）：

```xml
<!-- ABP Framework - Admin UI Packages -->
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Bootstrap" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Bundling" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite" Version="4.3.1" />
<PackageVersion Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.Account.Web.OpenIddict" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.Identity.Web" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.TenantManagement.Web" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.FeatureManagement.Web" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.Web" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.OpenIddict.MongoDB" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.Identity.MongoDB" Version="9.3.1" />
<PackageVersion Include="Volo.Abp.PermissionManagement.MongoDB" Version="9.3.1" />
<PackageVersion Include="Microsoft.AspNetCore.DataProtection.StackExchangeRedis" Version="10.0.0" />
```

若需要新增包，请保持注释段落与版本号风格一致，避免与集中式版本管理冲突。

### 1.2 校验 `Aevatar.AuthServer.csproj`

当前项目文件位于 `src/Aevatar.BusinessServer/src/Aevatar.AuthServer/Aevatar.AuthServer.csproj`，核心引用已经拆分为三组（项目引用 + 核心 ABP + UI）。节选如下：

```xml
<!-- Project References (共享业务模块) -->
<ItemGroup>
  <ProjectReference Include="..\Aevatar.BusinessServer.Domain\Aevatar.BusinessServer.Domain.csproj" />
  <ProjectReference Include="..\Aevatar.BusinessServer.Application\Aevatar.BusinessServer.Application.csproj" />
  <ProjectReference Include="..\Aevatar.BusinessServer.MongoDB\Aevatar.BusinessServer.MongoDB.csproj" />
  <ProjectReference Include="..\Aevatar.BusinessServer.HttpApi\Aevatar.BusinessServer.HttpApi.csproj" />
</ItemGroup>

<!-- Core ABP Framework -->
<ItemGroup>
  <PackageReference Include="Volo.Abp.Autofac" />
  <PackageReference Include="Volo.Abp.AspNetCore.Serilog" />
  <PackageReference Include="Volo.Abp.Swashbuckle" />
</ItemGroup>

<!-- UI & Web (AuthServer 前端) -->
<ItemGroup>
  <PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite" />
  <PackageReference Include="Volo.Abp.Account.Web.OpenIddict" />
  <PackageReference Include="Volo.Abp.Identity.Web" />
  <PackageReference Include="Volo.Abp.TenantManagement.Web" />
  <PackageReference Include="Volo.Abp.FeatureManagement.Web" />
  <PackageReference Include="Volo.Abp.PermissionManagement.Web" />
</ItemGroup>
```

若需要追加其它 ABP Web 模块，请放入对应 `ItemGroup`，并确保 `Directory.Packages.props` 已提供版本。

## 二、Module 配置

### 2.1 `AuthServerModule.cs`

`AuthServerModule` 已位于 `src/Aevatar.BusinessServer/src/Aevatar.AuthServer/AuthServerModule.cs`，负责注册 UI、OpenIddict 以及 BusinessServer 共享模块。关键结构如下（节选）：

```csharp
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    typeof(BusinessServerMongoDbModule),
    typeof(BusinessServerApplicationModule),
    typeof(BusinessServerHttpApiModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpIdentityWebModule),
    typeof(AbpTenantManagementWebModule),
    typeof(AbpFeatureManagementWebModule),
    typeof(AbpPermissionManagementWebModule)
)]
public class AuthServerModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddServer(options =>
            {
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.SetIssuer(new Uri(configuration["AuthServer:Authority"]
                    ?? configuration["App:SelfUrl"]
                    ?? "https://localhost:44320"));
            });

            builder.AddValidation(options =>
            {
                options.AddAudiences("Aevatar");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
        {
            options.AddDevelopmentEncryptionAndSigningCertificate = true;
        });
    }
```

`ConfigureServices` 通过私有方法拆分关注点，重点关注以下几处：

- `ConfigureBundles()` 向 `LeptonXLiteThemeBundles` 注入 `/global-styles.css` 与 `/libs/timeago/timeago-compat.js`；
- `ConfigureNavigationServices()` 注册 `AuthServerMenuContributor`，确保 UI 菜单展示；
- `ConfigureSwaggerServices()` 打开 `/swagger`；
- `Configure<PermissionManagementOptions>` 启用动态权限管理；
- `ConfigureAuthentication()` 开启动态 Claims；
- `ConfigureVirtualFileSystem()` 允许嵌入式资源 (`wwwroot` + Razor)。

`OnApplicationInitialization` 则与 `Program.cs` 保持一致，依次启用：

- `UseForwardedHeaders`（容器化/反向代理必备）；
- `UseAbpRequestLocalization()`、`UseAuthentication()`、`UseAbpOpenIddictValidation()` 等标准中间件；
- Swagger UI (`/swagger/v1/swagger.json`)；
- `UseConfiguredEndpoints()`。

如需扩展（例如新增自定义 Grant Type），建议直接在 `AuthServerModule` 中扩展对应 `PreConfigure`/`Configure` 段落，并保持与 BusinessServer 共享模块一致。

## 三、Program.cs 配置

### 3.1 更新 `Program.cs`

`Program.cs`（路径同上）已经符合 ABP 官方模板并输出文件日志，可直接复用：

```csharp
namespace Aevatar.AuthServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            Log.Information("Starting Aevatar.AuthServer.");
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();

            await builder.AddApplicationAsync<AuthServerModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Aevatar.AuthServer terminated unexpectedly!");
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

实际文件位于 `src/Aevatar.BusinessServer/src/Aevatar.AuthServer/appsettings.json`，关键字段如下：

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

确保 `Aevatar.AuthServer.csproj` 包含：

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

### 6.1 自定义 Grant（可选）

当前仓库未包含单独的 “Grants” 模块；所有 OpenIddict 配置都在 `AuthServerModule.PreConfigureServices` 中完成。如果需要扩展授权类型，可在该方法内调用：

```csharp
PreConfigure<OpenIddictServerBuilder>(builder =>
{
    builder.Configure(options =>
    {
        options.GrantTypes.Add("custom_grant");
    });
});
```

若你更倾向于独立模块，也可以新建 `Aevatar.AuthServer.Grants` 项目，并在 `AuthServerModule` 的 `[DependsOn]` 中引用，但请确保依赖 `AbpOpenIddictExtensionGrantTypesModule`。

## 七、MongoDB 模块依赖

如果使用 MongoDB，需要确保相关模块已经引用。`AuthServerModule` 目前通过 `BusinessServerMongoDbModule` 间接注册仓储，同时 `Directory.Packages.props` 中也包含 `Volo.Abp.OpenIddict.MongoDB` / `Volo.Abp.Identity.MongoDB` / `Volo.Abp.PermissionManagement.MongoDB`。若拆分为独立解决方案，请记得同步这些依赖。

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

1. 运行项目：`dotnet run --project src/Aevatar.BusinessServer/src/Aevatar.AuthServer`
2. 访问管理界面：`https://localhost:44300`
3. 使用默认管理员账户登录（需要在数据库迁移时创建）
4. 验证各个管理功能是否正常

