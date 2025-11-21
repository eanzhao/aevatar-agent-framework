using Volo.Abp.Modularity;

namespace Aevatar.BusinessServer;

public abstract class BusinessServerApplicationTestBase<TStartupModule> : BusinessServerTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
