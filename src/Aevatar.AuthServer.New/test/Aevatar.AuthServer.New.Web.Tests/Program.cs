using Microsoft.AspNetCore.Builder;
using Aevatar.AuthServer.New;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("Aevatar.AuthServer.New.Web.csproj"); 
await builder.RunAbpModuleAsync<NewWebTestModule>(applicationName: "Aevatar.AuthServer.New.Web");

public partial class Program
{
}
