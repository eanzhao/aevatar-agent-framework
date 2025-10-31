namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 标记配置处理方法
/// 方法签名必须是：Task HandleConfigAsync(TConfiguration config)
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ConfigurationAttribute : Attribute
{
}

