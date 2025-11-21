using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Aevatar.BusinessServer.Data;

/* This is used if database provider does't define
 * IBusinessServerDbSchemaMigrator implementation.
 */
public class NullBusinessServerDbSchemaMigrator : IBusinessServerDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
