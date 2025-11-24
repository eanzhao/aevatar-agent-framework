using Aevatar.BusinessServer.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.BusinessServer.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class BusinessServerController : AbpControllerBase
{
    protected BusinessServerController()
    {
        LocalizationResource = typeof(BusinessServerResource);
    }
}
