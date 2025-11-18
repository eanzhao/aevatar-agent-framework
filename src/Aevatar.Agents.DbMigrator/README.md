# Aevatar.Agents.DbMigrator

Database migrator service migrated from old Aevatar.DbMigrator project.

## Migrated Components

### Core
- `DbMigratorHostedService` - Hosted service for running database migrations
- `AevatarAgentsDbMigratorModule` - ABP module for database migration
- `AevatarAgentsDbMigrationService` - Migration service implementation
- `Program.cs` - Entry point for the migrator application

## Dependencies

This project depends on:
- ABP Framework modules (Volo.Abp.Core, Volo.Abp.Autofac, Volo.Abp.Data)
- Serilog for logging
- Microsoft.Extensions.Hosting

## Notes

- Namespace changed from `Aevatar.DbMigrator` to `Aevatar.Agents.DbMigrator`
- Code structure remains unchanged, only namespace updated
- Orleans dependencies have been removed as they are not required for database migration

