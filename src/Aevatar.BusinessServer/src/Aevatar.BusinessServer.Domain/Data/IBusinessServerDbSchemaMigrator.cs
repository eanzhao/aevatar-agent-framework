using System.Threading.Tasks;

namespace Aevatar.BusinessServer.Data;

public interface IBusinessServerDbSchemaMigrator
{
    Task MigrateAsync();
}
