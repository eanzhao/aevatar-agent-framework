using Volo.Abp.Settings;

namespace Aevatar.BusinessServer.Settings;

public class BusinessServerSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(BusinessServerSettings.MySetting1));
    }
}
