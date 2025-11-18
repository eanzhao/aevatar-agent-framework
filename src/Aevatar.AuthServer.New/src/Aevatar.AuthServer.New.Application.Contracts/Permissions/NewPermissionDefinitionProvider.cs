using Aevatar.AuthServer.New.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Aevatar.AuthServer.New.Permissions;

public class NewPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(NewPermissions.GroupName);

        //Define your own permissions here. Example:
        //myGroup.AddPermission(NewPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<NewResource>(name);
    }
}
