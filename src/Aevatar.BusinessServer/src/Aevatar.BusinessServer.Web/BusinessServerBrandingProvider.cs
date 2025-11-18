using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Localization;
using Aevatar.BusinessServer.Localization;

namespace Aevatar.BusinessServer.Web;

[Dependency(ReplaceServices = true)]
public class BusinessServerBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<BusinessServerResource> _localizer;

    public BusinessServerBrandingProvider(IStringLocalizer<BusinessServerResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
