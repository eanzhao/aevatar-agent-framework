using Aevatar.BusinessServer.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Aevatar.BusinessServer.Permissions;

public class BusinessServerPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(BusinessServerPermissions.GroupName);

        //Define your own permissions here. Example:
        //myGroup.AddPermission(BusinessServerPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BusinessServerResource>(name);
    }
}
