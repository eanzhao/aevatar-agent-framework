using Aevatar.BusinessServer.Localization;
using Volo.Abp.Application.Services;

namespace Aevatar.BusinessServer;

/* Inherit your application services from this class.
 */
public abstract class BusinessServerAppService : ApplicationService
{
    protected BusinessServerAppService()
    {
        LocalizationResource = typeof(BusinessServerResource);
    }
}
