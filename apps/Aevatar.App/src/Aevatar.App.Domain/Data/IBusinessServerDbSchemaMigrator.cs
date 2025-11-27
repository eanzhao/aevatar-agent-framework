using System.Threading.Tasks;

namespace Aevatar.App.Data;

public interface IAppDbSchemaMigrator
{
    Task MigrateAsync();
}
