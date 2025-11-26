using System.Text;

namespace MakerProjectsDemo.Projects.Bazi;

internal static class BaziGoalBuilder
{
    public static string BuildGoal(BaziProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"请基于传统子平八字法，为这位{profile.Gender}缘主生成一份详尽的【事业与健康运势分析报告】。");
        sb.AppendLine();
        sb.AppendLine("【缘主信息】");
        sb.AppendLine($"- 出生公历：{profile.SolarBirth}");
        sb.AppendLine($"- 出生地：{profile.Birthplace}");
        sb.AppendLine($"- 八字排盘：{profile.LunarYearStemBranch}年 {profile.LunarMonthStemBranch}月 {profile.LunarDayStemBranch}日 {profile.LunarHourStemBranch}时");
        sb.AppendLine($"- 核心诉求：{profile.FocusTopic}");
        sb.AppendLine();
        sb.AppendLine("【分析要求】");
        sb.AppendLine("1. 这是一个复杂推理任务，禁止直接给出结论。必须先将问题拆解为依赖关系的子步骤（如：先定格局喜忌，再论大运，最后断事）。");
        sb.AppendLine("2. 在推导“旺衰”与“格局”等基础事实时，必须追求绝对准确的共识。");
        sb.AppendLine("3. 在推导“建议”时，应综合考虑命局的优劣势。");
        sb.AppendLine("4. 所有推导必须有理有据，引用《滴天髓》或《三命通会》等经典理论更佳。");
        sb.AppendLine("5. 最终输出必须结构化，便于阅读。");
        
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

