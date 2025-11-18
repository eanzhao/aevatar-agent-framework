using Aevatar.AuthServer.New.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.AuthServer.New.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class NewController : AbpControllerBase
{
    protected NewController()
    {
        LocalizationResource = typeof(NewResource);
    }
}
