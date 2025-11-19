using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Localization;
using Aevatar.AuthServer.Localization;

namespace Aevatar.AuthServer.Web;

[Dependency(ReplaceServices = true)]
public class AuthServerBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<AuthServerResource> _localizer;

    public AuthServerBrandingProvider(IStringLocalizer<AuthServerResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
