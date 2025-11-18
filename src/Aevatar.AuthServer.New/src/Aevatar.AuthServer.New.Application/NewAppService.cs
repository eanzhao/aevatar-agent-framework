using Aevatar.AuthServer.New.Localization;
using Volo.Abp.Application.Services;

namespace Aevatar.AuthServer.New;

/* Inherit your application services from this class.
 */
public abstract class NewAppService : ApplicationService
{
    protected NewAppService()
    {
        LocalizationResource = typeof(NewResource);
    }
}
