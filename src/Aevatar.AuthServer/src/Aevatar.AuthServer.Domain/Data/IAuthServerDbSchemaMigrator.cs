using System.Threading.Tasks;

namespace Aevatar.AuthServer.Data;

public interface IAuthServerDbSchemaMigrator
{
    Task MigrateAsync();
}
