using Aevatar.AuthServer.New.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.AuthServer.New.Web.Pages;

public abstract class NewPageModel : AbpPageModel
{
    protected NewPageModel()
    {
        LocalizationResourceType = typeof(NewResource);
    }
}
