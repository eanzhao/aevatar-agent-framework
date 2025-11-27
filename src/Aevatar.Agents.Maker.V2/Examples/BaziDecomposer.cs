namespace Aevatar.Agents.Maker.V2.Examples;

// ============================================================
//  Example: Bazi (八字) Domain Decomposer
//  Only 30 lines needed for domain-specific decomposition!
// ============================================================

/// <summary>
/// Bazi-specific decomposition strategy.
/// This is ALL the domain code needed - compare to 457 lines in V1.
/// </summary>
public sealed class BaziDecomposer : IDecompositionStrategy
{
    /// <inheritdoc />
    public string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        var contextSection = context.Count > 0
            ? $"\n[已知信息]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        var jsonFormat = """[{"step_id":"S1","description":"..."}]""";
        return $"""
            你是八字命理分析专家。将以下任务分解成4-6个逻辑步骤。
            
            分解规则：
            1. 第一步必须是「校验排盘数据」（核对干支、五行、十神是否齐全）
            2. 必须包含「判断日主强弱」、「确定用神忌神」
            3. 分析具体领域（事业/婚姻/健康等）
            4. 最后一步是「综合建议」
            
            输出格式：JSON数组 {jsonFormat}
            仅输出JSON，不要其他文字。
            {contextSection}
            [任务]
            {taskDescription}
            """;
    }

    /// <inheritdoc />
    public bool IsAtomic(string taskDescription, int currentDepth, int maxDepth)
    {
        // Bazi analysis: 2 levels is enough (decompose → execute)
        return currentDepth >= 1;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput)
    {
        // Use default parser
        return new DefaultDecomposer().ParseDecomposition(llmOutput);
    }
}

/// <summary>
/// Bazi-specific solution strategy.
/// </summary>
public sealed class BaziSolver : ISolutionStrategy
{
    /// <inheritdoc />
    public string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        var contextSection = context.Count > 0
            ? $"\n[已确认信息]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        return $"""
            你是八字命理大师，精通子平真诠与滴天髓。
            
            请完成以下分析任务：
            {contextSection}
            [任务]
            {taskDescription}
            
            要求：
            1. 引用经典理论支撑观点
            2. 语言专业但通俗易懂
            3. 给出具体、可操作的建议
            4. 控制在200字以内
            
            [分析结果]
            """;
    }
}

