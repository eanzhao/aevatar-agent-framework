using Aevatar.AuthServer.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.AuthServer.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class AuthServerController : AbpControllerBase
{
    protected AuthServerController()
    {
        LocalizationResource = typeof(AuthServerResource);
    }
}
