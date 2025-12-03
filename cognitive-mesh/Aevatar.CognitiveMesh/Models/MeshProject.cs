using Aevatar.CognitiveMesh.Abstractions;

namespace Aevatar.CognitiveMesh.Models;

// ============================================================
//  MESH PROJECT
//  项目定义模型
// ============================================================

/// <summary>
/// 网格项目定义。
/// </summary>
public sealed class MeshProject : IMeshProject
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public required string Description { get; init; }

    /// <inheritdoc />
    public required string Icon { get; init; }

    /// <inheritdoc />
    public required StrategyKind Strategy { get; init; }

    /// <inheritdoc />
    public required string Task { get; init; }

    /// <summary>
    /// 预配置的选项。
    /// </summary>
    public required ReasoningOptions Options { get; init; }

    /// <inheritdoc />
    public ReasoningOptions BuildOptions() => Options;
}

/// <summary>
/// 项目配置（从 JSON 创建）。
/// </summary>
public sealed class ProjectConfig
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Strategy { get; set; }
    public string? Task { get; set; }

    // 通用选项
    public string? ProviderName { get; set; }
    public int? MaxLlmCalls { get; set; }
    public long? MaxTokens { get; set; }
    public int? MaxDurationMinutes { get; set; }

    // MAKER 选项
    public string? Reliability { get; set; }

    // UoT 选项
    public string? DomainHint { get; set; }
    public int? MaxAnalogies { get; set; }
    public int? MaxCandidates { get; set; }

    // 上下文
    public Dictionary<string, string>? Context { get; set; }
}

