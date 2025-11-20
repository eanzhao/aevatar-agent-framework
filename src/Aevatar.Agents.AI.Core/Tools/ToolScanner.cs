using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AI.Core.Tools;

/// <summary>
/// 工具扫描器，用于自动发现和注册工具类
/// </summary>
public static class ToolScanner
{
    /// <summary>
    /// 从程序集扫描工具类
    /// </summary>
    /// <param name="assembly">要扫描的程序集</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>工具定义列表</returns>
    public static IEnumerable<ToolDefinition> ScanTools(Assembly assembly, ILogger? logger = null)
    {
        var tools = new List<ToolDefinition>();

        try
        {
            // 查找所有带有AevatarToolAttribute的类
            var toolTypes = assembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => type.GetCustomAttribute<AevatarToolAttribute>() != null)
                .Where(type => typeof(IAevatarTool).IsAssignableFrom(type));  // 必须实现IAevatarTool

            foreach (var toolType in toolTypes)
            {
                try
                {
                    var toolDefinition = CreateToolDefinition(toolType, logger);
                    if (toolDefinition != null)
                    {
                        tools.Add(toolDefinition);
                        logger?.LogInformation("Scanned tool: {ToolName} from {TypeName}", toolDefinition.Name, toolType.Name);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to create tool definition for type {TypeName}", toolType.Name);
                    // 继续扫描其他工具，不因单个失败而中断
                }
            }

            logger?.LogInformation("Scanned {ToolCount} tools from assembly {AssemblyName}", tools.Count, assembly.GetName().Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to scan tools from assembly {AssemblyName}", assembly.GetName().Name);
        }

        return tools;
    }

    /// <summary>
    /// 从多个程序集扫描工具类
    /// </summary>
    public static IEnumerable<ToolDefinition> ScanTools(IEnumerable<Assembly> assemblies, ILogger? logger = null)
    {
        var tools = new List<ToolDefinition>();

        foreach (var assembly in assemblies)
        {
            var assemblyTools = ScanTools(assembly, logger);
            tools.AddRange(assemblyTools);
        }

        logger?.LogInformation("Scanned total {ToolCount} tools from {AssemblyCount} assemblies",
            tools.Count, assemblies.Count());

        return tools;
    }

    /// <summary>
    /// 从当前程序集和引用的程序集扫描工具
    /// </summary>
    public static IEnumerable<ToolDefinition> ScanToolsInAppDomain(ILogger? logger = null)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Where(a => !a.FullName?.StartsWith("System.") ?? false)
            .Where(a => !a.FullName?.StartsWith("Microsoft.") ?? false)
            .Where(a => !a.FullName?.StartsWith("Grpc.") ?? false);

        return ScanTools(assemblies, logger);
    }

    /// <summary>
    /// 创建工具定义
    /// </summary>
    private static ToolDefinition? CreateToolDefinition(Type toolType, ILogger? logger)
    {
        var attribute = toolType.GetCustomAttribute<AevatarToolAttribute>();
        if (attribute == null || !attribute.AutoRegister)
        {
            return null;
        }

        // 获取工具名称（优先使用Attribute指定的，否则使用类名）
        var toolName = attribute.Name ?? GenerateToolName(toolType.Name);

        // 检查是否有无参构造函数
        var constructor = toolType.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            logger?.LogWarning("Tool type {TypeName} does not have a parameterless constructor", toolType.Name);
            return null;
        }

        // 创建工具实例（用于获取定义）
        var toolInstance = (IAevatarTool)Activator.CreateInstance(toolType)!;
        try
        {
            var context = new ToolContext
            {
                AgentId = "scanner",
                GetSessionIdCallback = () => Guid.NewGuid().ToString()
            };

            // 创建工具定义
            var toolDefinition = toolInstance.CreateToolDefinition(context, logger);

            // 使用Attribute中的值覆盖
            toolDefinition.Name = toolName;
            toolDefinition.Description = attribute.Description;
            toolDefinition.Category = attribute.Category;
            toolDefinition.Tags = attribute.Tags.ToList();
            toolDefinition.Version = attribute.Version;
            toolDefinition.RequiresConfirmation = attribute.RequiresConfirmation;
            toolDefinition.IsDangerous = attribute.IsDangerous;
            toolDefinition.RequiresInternalAccess = attribute.RequiresInternalAccess;
            toolDefinition.CanBeOverridden = attribute.CanBeOverridden;

            return toolDefinition;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create tool definition from instance of {TypeName}", toolType.Name);
            return null;
        }
    }

    /// <summary>
    /// 生成工具名称（从类名）
    /// </summary>
    private static string GenerateToolName(string typeName)
    {
        // Remove "Tool" suffix if present
        var name = typeName.EndsWith("Tool", StringComparison.OrdinalIgnoreCase)
            ? typeName.Substring(0, typeName.Length - 4)
            : typeName;

        // Convert PascalCase to snake_case
        return string.Concat(name.Select((x, i) =>
            i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLowerInvariant() : x.ToString().ToLowerInvariant()));
    }
}
