using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Localization;
using Aevatar.AuthServer.New.Localization;

namespace Aevatar.AuthServer.New.Web;

[Dependency(ReplaceServices = true)]
public class NewBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<NewResource> _localizer;

    public NewBrandingProvider(IStringLocalizer<NewResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
