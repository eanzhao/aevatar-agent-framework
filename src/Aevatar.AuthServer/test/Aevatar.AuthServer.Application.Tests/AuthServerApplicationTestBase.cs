using Volo.Abp.Modularity;

namespace Aevatar.AuthServer;

public abstract class AuthServerApplicationTestBase<TStartupModule> : AuthServerTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
