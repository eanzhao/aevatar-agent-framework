using System.Threading.Tasks;

namespace Aevatar.AuthServer.New.Data;

public interface INewDbSchemaMigrator
{
    Task MigrateAsync();
}
