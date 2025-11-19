using Microsoft.AspNetCore.Builder;
using Aevatar.AuthServer;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("Aevatar.AuthServer.Web.csproj"); 
await builder.RunAbpModuleAsync<AuthServerWebTestModule>(applicationName: "Aevatar.AuthServer.Web");

public partial class Program
{
}
