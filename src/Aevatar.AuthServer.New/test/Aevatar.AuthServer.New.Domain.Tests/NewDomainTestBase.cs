using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.New;

/* Inherit from this class for your domain layer tests. */
public abstract class NewDomainTestBase<TStartupModule> : NewTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
