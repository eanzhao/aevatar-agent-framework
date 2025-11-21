using Volo.Abp.Modularity;

namespace Aevatar.BusinessServer;

/* Inherit from this class for your domain layer tests. */
public abstract class BusinessServerDomainTestBase<TStartupModule> : BusinessServerTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
