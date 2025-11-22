using System;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// 标记工具类，支持自动扫描和注册
/// <para/>
/// 使用示例:
/// <code>
/// [AevatarTool(
///     Name = "send_email",
///     Description = "Send email to recipient",
///     Category = ToolCategory.Communication,
///     AutoRegister = true
/// )]
/// public class EmailTool : AevatarToolBase
/// {
///     // 工具实现...
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class AevatarToolAttribute : Attribute
{
    /// <summary>
    /// 工具名称（唯一标识）
    /// <para/>如果未指定，则使用类名（去掉"Tool"后缀）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 工具描述
    /// <para/>说明工具的用途和使用场景
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 工具类别
    /// <para/>用于工具分组和权限控制
    /// </summary>
    public ToolCategory Category { get; set; } = ToolCategory.Custom;

    /// <summary>
    /// 是否自动注册
    /// <para/>如果为true，工具扫描器会自动发现并注册此工具
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// 工具标签
    /// <para/>用于工具搜索和分类
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 工具版本
    /// <para/>遵循语义化版本规范（SemVer）
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 是否需要确认
    /// <para/>如果为true，执行前需要用户确认（危险操作）
    /// </summary>
    public bool RequiresConfirmation { get; set; } = false;

    /// <summary>
    /// 是否是危险操作
    /// <para/>用于标记可能影响系统安全的操作
    /// </summary>
    public bool IsDangerous { get; set; } = false;

    /// <summary>
    /// 是否需要内部访问权限
    /// <para/>如果为true，只能由系统内部调用
    /// </summary>
    public bool RequiresInternalAccess { get; set; } = false;

    /// <summary>
    /// 是否可以被覆盖
    /// <para/>如果为true，允许子类或配置覆盖此工具
    /// </summary>
    public bool CanBeOverridden { get; set; } = true;
}
