using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Volo.Abp;
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
            .WriteTo.Console()
            .CreateLogger();
        
        try
        {
            Log.Information("Starting Aevatar.Agents.AuthServer.");
            
            var builder = WebApplication.CreateBuilder(args);
            
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseSerilog();
            
            // Configure logging before adding ABP application
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog();
            
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

