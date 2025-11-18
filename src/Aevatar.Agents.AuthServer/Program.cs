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

