using Aevatar.AuthServer.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.AuthServer.Pages;

public abstract class NewPageModel : AbpPageModel
{
    protected NewPageModel()
    {
        LocalizationResourceType = typeof(AuthServerResource);
    }
}
