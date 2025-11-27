using Aevatar.Agents.Maker.V2;

namespace MakerProjectsDemoV2.Projects.Bazi;

// ============================================================
//  Bazi Domain Strategies - Only 80 lines total!
//  Compare to 457 lines in V1
// ============================================================

/// <summary>
/// Bazi profile information.
/// </summary>
public sealed record BaziProfile(
    string SolarBirth,
    string Gender,
    string Birthplace,
    string LunarYearStemBranch,
    string LunarMonthStemBranch,
    string LunarDayStemBranch,
    string LunarHourStemBranch,
    string FocusTopic)
{
    public static BaziProfile CreateDemo() => new(
        SolarBirth: "1992年7月26日 18:13",
        Gender: "男性",
        Birthplace: "山西运城",
        LunarYearStemBranch: "壬申",
        LunarMonthStemBranch: "丁未",
        LunarDayStemBranch: "癸卯",
        LunarHourStemBranch: "辛酉",
        FocusTopic: "事业晋升与身体健康");

    public string BuildGoal() => $"""
        请基于传统子平八字法，为这位{Gender}缘主生成详尽的【事业与健康运势分析报告】。

        【缘主信息】
        - 出生公历：{SolarBirth}
        - 出生地：{Birthplace}
        - 八字排盘：{LunarYearStemBranch}年 {LunarMonthStemBranch}月 {LunarDayStemBranch}日 {LunarHourStemBranch}时
        - 核心诉求：{FocusTopic}

        【分析要求】
        1. 这是一个复杂推理任务，必须先将问题拆解为依赖关系的子步骤。
        2. 推导"旺衰"与"格局"时必须追求绝对准确。
        3. 所有推导必须有理有据，引用经典理论更佳。
        """;
}

/// <summary>
/// Bazi decomposition strategy.
/// </summary>
public sealed class BaziDecomposer : IDecompositionStrategy
{
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var jsonFormat = """[{"step_id":"S1","description":"..."}]""";
        var ctxSection = ctx.Count > 0
            ? $"\n[已知信息]\n{string.Join("\n", ctx.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        return $"""
            你是八字命理分析专家。将以下任务分解成4-6个逻辑步骤。
            
            分解规则：
            1. 第一步必须是「校验排盘数据」
            2. 必须包含「判断日主强弱」、「确定用神忌神」
            3. 分析具体领域（事业/健康等）
            4. 最后一步是「综合建议」
            
            输出格式：JSON数组 {jsonFormat}
            仅输出JSON，不要其他文字。
            {ctxSection}
            [任务]
            {task}
            """;
    }

    public bool IsAtomic(string task, int depth, int maxDepth) => depth >= 1;

    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
        => new DefaultDecomposer().ParseDecomposition(output);
}

/// <summary>
/// Bazi solution strategy.
/// </summary>
public sealed class BaziSolver : ISolutionStrategy
{
    public string BuildSolvePrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var ctxSection = ctx.Count > 0
            ? $"\n[已确认信息]\n{string.Join("\n", ctx.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        return $"""
            你是八字命理大师，精通子平真诠与滴天髓。
            {ctxSection}
            [任务]
            {task}
            
            要求：引用经典理论，语言专业但通俗易懂，控制在200字以内。
            
            [分析结果]
            """;
    }
}

