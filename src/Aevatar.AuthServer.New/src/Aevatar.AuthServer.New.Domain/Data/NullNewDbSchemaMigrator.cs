using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Aevatar.AuthServer.New.Data;

/* This is used if database provider does't define
 * INewDbSchemaMigrator implementation.
 */
public class NullNewDbSchemaMigrator : INewDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
