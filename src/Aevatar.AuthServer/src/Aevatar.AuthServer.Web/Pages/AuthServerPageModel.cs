using Aevatar.AuthServer.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.AuthServer.Web.Pages;

public abstract class AuthServerPageModel : AbpPageModel
{
    protected AuthServerPageModel()
    {
        LocalizationResourceType = typeof(AuthServerResource);
    }
}
