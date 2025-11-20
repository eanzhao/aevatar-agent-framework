using Microsoft.AspNetCore.Builder;
using Aevatar.BusinessServer;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("Aevatar.BusinessServer.HttpApi.Host.csproj"); 
await builder.RunAbpModuleAsync<BusinessServerWebTestModule>(applicationName: "Aevatar.BusinessServer.HttpApi.Host");

public partial class Program
{
}
