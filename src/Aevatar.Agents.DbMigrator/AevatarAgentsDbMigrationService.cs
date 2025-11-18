using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace Aevatar.Agents.DbMigrator;

public class AevatarAgentsDbMigrationService : ITransientDependency
{
    private readonly ILogger<AevatarAgentsDbMigrationService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public AevatarAgentsDbMigrationService(
        ILogger<AevatarAgentsDbMigrationService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        _logger.LogInformation("Starting database migration...");
        
        // Run data seeders
        var dataSeeder = _serviceProvider.GetRequiredService<IDataSeeder>();
        await dataSeeder.SeedAsync(new DataSeedContext());
        
        _logger.LogInformation("Database migration completed successfully.");
    }
}

