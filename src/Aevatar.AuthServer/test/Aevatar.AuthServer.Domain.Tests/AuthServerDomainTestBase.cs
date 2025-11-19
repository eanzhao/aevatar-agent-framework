using Volo.Abp.Modularity;

namespace Aevatar.AuthServer;

/* Inherit from this class for your domain layer tests. */
public abstract class AuthServerDomainTestBase<TStartupModule> : AuthServerTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
