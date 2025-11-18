using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Aevatar.Agents.DbMigrator;

public class AevatarAgentsDbMigrationService : ITransientDependency
{
    private readonly ILogger<AevatarAgentsDbMigrationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public AevatarAgentsDbMigrationService(
        ILogger<AevatarAgentsDbMigrationService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task MigrateAsync()
    {
        _logger.LogInformation("Starting database migration...");
        
        // Get admin password from configuration
        var adminPassword = _configuration["User:AdminPassword"] ?? "1q2W3e*";
        var adminEmail = "admin@abp.io";
        
        // Run data seeders with admin user properties
        var dataSeeder = _serviceProvider.GetRequiredService<IDataSeeder>();
        await dataSeeder.SeedAsync(new DataSeedContext()
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, adminEmail)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, adminPassword)
        );
        
        _logger.LogInformation("Database migration completed successfully.");
    }
}

