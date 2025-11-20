using System.Collections.Generic;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI Agent配置选项类
/// <para/>
/// 用于依赖注入的Options模式，提供类型安全、可配置的Agent设置
/// <para/>
/// 使用示例:
/// <code>
/// services.Configure&lt;AIAgentOptions&gt;(options =>
/// {
///     options.SystemPrompt = "You are a helpful assistant.";
///     options.Model = "gpt-4";
///     options.Temperature = 0.7;
/// });
/// </code>
/// </summary>
public class AIAgentOptions
{
    /// <summary>
    /// 系统提示词
    /// <para/>用于定义Agent的角色和行为准则
    /// </summary>
    public string SystemPrompt { get; set; } = "You are a helpful AI assistant.";

    /// <summary>
    /// LLM模型名称
    /// <para/>例如: gpt-4, gpt-3.5-turbo, claude-3-opus
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// 温度参数（0-2）
    /// <para/>控制响应的随机性和创造性
    /// <para/>较低值（如0.2）产生更确定性的响应
    /// <para/>较高值（如0.8）产生更多样化的响应
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 最大令牌数
    /// <para/>限制响应的最大长度
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// 最大对话历史记录数
    /// <para/>限制保留在内存中的对话轮数
    /// </summary>
    public int MaxHistory { get; set; } = 20;

    /// <summary>
    /// 处理模式
    /// <para/>定义Agent使用的思考策略（Standard, ChainOfThought, ReAct, TreeOfThoughts）
    /// </summary>
    public AevatarAIProcessingMode ProcessingMode { get; set; } = AevatarAIProcessingMode.Standard;

    /// <summary>
    /// 启用工具
    /// <para/>如果为true，Agent可以使用工具
    /// </summary>
    public bool EnableTools { get; set; } = true;

    /// <summary>
    /// 自动注册工具
    /// <para/>如果为true，自动扫描并注册标记了[AevatarTool]特性的工具类
    /// </summary>
    public bool AutoRegisterTools { get; set; } = true;

    /// <summary>
    /// 工具扫描程序集
    /// <para/>指定要扫描的工具所在的程序集名称列表
    /// <para/>如果为空，则扫描当前AppDomain中的所有程序集
    /// </summary>
    public List<string> ToolScanAssemblies { get; set; } = new();

    /// <summary>
    /// 提供商特定设置
    /// <para/>存储特定LLM提供商的配置（如API密钥、端点等）
    /// </summary>
    public Dictionary<string, object> ProviderSettings { get; set; } = new();

    /// <summary>
    /// 启用流式响应
    /// <para/>如果为true，使用流式方式获取AI响应（逐步显示）
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    /// 记录详细日志
    /// <para/>如果为true，记录详细的AI请求和响应日志（用于调试）
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// 自定义工具注册程序集
    /// <para/>工具扫描器会扫描这些程序集中的工具类
    /// <para/>如果为空，则扫描调用者的程序集
    /// </summary>
    public List<string> CustomToolAssemblies { get; set; } = new();

    /// <summary>
    /// 内存配置选项
    /// </summary>
    public MemoryOptions Memory { get; set; } = new();

    /// <summary>
    /// 策略配置选项
    /// </summary>
    public StrategyOptions Strategies { get; set; } = new();
}

/// <summary>
/// 内存配置选项
/// </summary>
public class MemoryOptions
{
    /// <summary>
    /// 启用内存功能
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大内存项数（长期记忆）
    /// </summary>
    public int MaxMemoryItems { get; set; } = 1000;

    /// <summary>
    /// 内存保留天数
    /// <para/>超过此天数的记忆项可能会被清理
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 相似度阈值（0-1）
    /// <para/>用于内存搜索，只返回相似度大于此阈值的结果
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.7;

    /// <summary>
    /// 最大搜索结果数
    /// </summary>
    public int MaxSearchResults { get; set; } = 10;
}

/// <summary>
/// 策略配置选项
/// </summary>
public class StrategyOptions
{
    /// <summary>
    /// 最大Chain-of-Thought步骤数
    /// </summary>
    public int MaxChainOfThoughtSteps { get; set; } = 5;

    /// <summary>
    /// 最大ReAct迭代次数
    /// </summary>
    public int MaxReActIterations { get; set; } = 10;

    /// <summary>
    /// Tree-of-Thoughts最大深度
    /// </summary>
    public int MaxTreeDepth { get; set; } = 3;

    /// <summary>
    /// Tree-of-Thoughts分支因子
    /// </summary>
    public int TreeBranchingFactor { get; set; } = 3;
}
