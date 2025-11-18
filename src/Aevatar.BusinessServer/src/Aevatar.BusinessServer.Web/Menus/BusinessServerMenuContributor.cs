using System.Threading.Tasks;
using Aevatar.BusinessServer.Localization;
using Aevatar.BusinessServer.Permissions;
using Aevatar.BusinessServer.MultiTenancy;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.Authorization.Permissions;
// Identity menu removed - managed in AuthServer
// using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.UI.Navigation;

namespace Aevatar.BusinessServer.Web.Menus;

public class BusinessServerMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }

    private static Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<BusinessServerResource>();

        // Home
        context.Menu.AddItem(
            new ApplicationMenuItem(
                BusinessServerMenus.Home,
                l["Menu:Home"],
                "~/",
                icon: "fa fa-home",
                order: 1
            )
        );

        // Administration menu
        var administration = context.Menu.GetAdministration();
        administration.Order = 5;

        // Identity and Permission Management UI removed
        // All user/role/permission management is done in AuthServer (https://localhost:44320)
        
        // Administration->Settings
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 3);
        
        return Task.CompletedTask;
    }
}
