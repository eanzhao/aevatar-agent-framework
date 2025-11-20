using Microsoft.Extensions.Localization;
using Aevatar.BusinessServer.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Aevatar.BusinessServer.HttpApi.Host;

[Dependency(ReplaceServices = true)]
public class BusinessServerHttpApiHostBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<BusinessServerResource> _localizer;

    public BusinessServerHttpApiHostBrandingProvider(IStringLocalizer<BusinessServerResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
