using System.Text;

namespace MakerBaziDemo;

internal static class BaziGoalBuilder
{
    public static string BuildGoal(BaziProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是资深命理总监，需要指挥多个微型 MAKER agent 分析八字并输出统一口径的命理报告。");
        sb.AppendLine("务必先分解任务，再在每个步骤中通过投票选择最可靠的方案，直到所有子问题都达到原子级别。");
        sb.AppendLine();
        sb.AppendLine("【输入信息】");
        sb.AppendLine($"- 公历：{profile.SolarBirth}");
        sb.AppendLine($"- 性别：{profile.Gender}");
        sb.AppendLine($"- 出生地：{profile.Birthplace}");
        sb.AppendLine($"- 八字：年柱 {profile.LunarYearStemBranch}，月柱 {profile.LunarMonthStemBranch}，日柱 {profile.LunarDayStemBranch}，时柱 {profile.LunarHourStemBranch}");
        sb.AppendLine($"- 重点关注：{profile.FocusTopic}");
        sb.AppendLine($"- 额外线索：{profile.Notes}");
        sb.AppendLine();
        sb.AppendLine("【任务要求】");
        sb.AppendLine("1. 识别命局格局、阴阳平衡、五行旺衰，必要时继续下钻。");
        sb.AppendLine("2. 将任务拆分为“排盘校验→格局判断→用神分析→运势建议→风险红旗”五个阶段。");
        sb.AppendLine("3. 每个阶段至少生成 3 个候选方案，通过 first-to-ahead-by-K 规则达成共识。");
        sb.AppendLine("4. 最终报告需包含：核心命局摘要、三条运势洞察、两条建议，以及一个风险观察列表。");
        sb.AppendLine("5. 结果必须结构化，可被父级 agent 聚合。");
        sb.AppendLine("6. 每完成一个阶段都要输出《阶段总结》，列出“已锁定的事实、争议点、下一阶段输入”，并把总结传入后续阶段。");
        sb.AppendLine("7. 未完成阶段禁止跳跃，必须在该阶段内部继续细分任务、扩增候选，直到共识明确。");
        sb.AppendLine("8. 若出现分歧，优先通过再分解或重新投票来取得新的共识，不允许直接给出最终答案。");
        sb.AppendLine("9. 在日志或输出中简要记录每次投票的候选要点与差异，方便上层监控节奏。");
        sb.AppendLine();
        sb.AppendLine("【输出格式参考 (JSON 或 Markdown)】");
        sb.AppendLine("""
{
  "profile_summary": "...",
  "insights": [
    {"title": "...", "detail": "..."},
    {"title": "...", "detail": "..."},
    {"title": "...", "detail": "..."}
  ],
  "recommendations": [
    {"area": "career", "advice": "..."},
    {"area": "relationship", "advice": "..."}
  ],
  "risks": [
    {"signal": "...", "mitigation": "..."}
  ]
}
""");
        sb.AppendLine();
        sb.AppendLine("当所有子任务完成后，综合所有孩子节点的输出，给出一份自然语言总结。");
        return sb.ToString();
    }
}

internal sealed record BaziProfile(
    string SolarBirth,
    string Gender,
    string Birthplace,
    string LunarYearStemBranch,
    string LunarMonthStemBranch,
    string LunarDayStemBranch,
    string LunarHourStemBranch,
    string FocusTopic,
    string Notes)
{
    public static BaziProfile CreateDemoProfile() => new(
        SolarBirth: "1992年7月26日 18:13",
        Gender: "男性",
        Birthplace: "山西运城",
        LunarYearStemBranch: "壬申",
        LunarMonthStemBranch: "丁未",
        LunarDayStemBranch: "癸卯",
        LunarHourStemBranch: "辛酉",
        FocusTopic: "事业晋升与身体健康",
        Notes: "程序员，在写multi ai agent框架");
}

