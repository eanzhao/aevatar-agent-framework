using Aevatar.BusinessServer.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.BusinessServer.Web.Pages;

public abstract class BusinessServerPageModel : AbpPageModel
{
    protected BusinessServerPageModel()
    {
        LocalizationResourceType = typeof(BusinessServerResource);
    }
}
