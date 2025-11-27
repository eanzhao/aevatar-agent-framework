using Volo.Abp.Modularity;

namespace Aevatar.App;

public abstract class AppApplicationTestBase<TStartupModule> : AppTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
