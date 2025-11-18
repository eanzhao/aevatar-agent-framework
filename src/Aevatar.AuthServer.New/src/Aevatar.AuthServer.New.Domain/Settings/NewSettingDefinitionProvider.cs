using Volo.Abp.Settings;

namespace Aevatar.AuthServer.New.Settings;

public class NewSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(NewSettings.MySetting1));
    }
}
