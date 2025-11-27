using Microsoft.AspNetCore.Builder;
using Aevatar.App;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("Aevatar.App.HttpApi.Host.csproj"); 
await builder.RunAbpModuleAsync<AppWebTestModule>(applicationName: "Aevatar.App.HttpApi.Host");

public partial class Program
{
}
