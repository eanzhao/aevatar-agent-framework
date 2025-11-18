using Volo.Abp.Modularity;

namespace Aevatar.AuthServer.New;

public abstract class NewApplicationTestBase<TStartupModule> : NewTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
