using Volo.Abp.Settings;

namespace Aevatar.AuthServer.Settings;

public class AuthServerSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(NewSettings.MySetting1));
    }
}
