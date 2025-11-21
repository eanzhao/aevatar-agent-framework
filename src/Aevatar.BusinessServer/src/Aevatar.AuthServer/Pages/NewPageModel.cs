using Aevatar.BusinessServer.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.AuthServer.Pages;

public abstract class AuthServerPageModel : AbpPageModel
{
    protected AuthServerPageModel()
    {
        LocalizationResourceType = typeof(BusinessServerResource);
    }
}
