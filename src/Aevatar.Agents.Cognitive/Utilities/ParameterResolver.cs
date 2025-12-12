using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Maker;

namespace Aevatar.Agents.Cognitive.Utilities;

// ============================================================
//  参数解析器
//  职责：从步骤参数中解析类型化值
// ============================================================

/// <summary>
/// 参数解析工具。
/// </summary>
public sealed class ParameterResolver
{
    private readonly TemplateEngine _templateEngine;
    private readonly Dictionary<string, object> _variables;
    
    public ParameterResolver(TemplateEngine templateEngine, Dictionary<string, object> variables)
    {
        _templateEngine = templateEngine;
        _variables = variables;
    }
    
    /// <summary>
    /// 解析整数参数。
    /// </summary>
    public int GetInt(Dictionary<string, object?> parameters, string key, int defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ToInt(_templateEngine.Evaluate(s, _variables), defaultValue),
            _ => defaultValue
        };
    }
    
    /// <summary>
    /// 解析浮点参数。
    /// </summary>
    public float GetFloat(Dictionary<string, object?> parameters, string key, float defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            string s when float.TryParse(s, out var parsed) => parsed,
            string s => ToFloat(_templateEngine.Evaluate(s, _variables), defaultValue),
            _ => defaultValue
        };
    }
    
    /// <summary>
    /// 解析字符串参数（支持模板渲染）。
    /// </summary>
    public string? GetString(Dictionary<string, object?> parameters, string key, string? defaultValue = null)
    {
        var value = parameters.GetValueOrDefault(key)?.ToString();
        return value != null ? _templateEngine.Render(value, _variables) : defaultValue;
    }
    
    /// <summary>
    /// 解析 Red-Flag 策略。
    /// </summary>
    public IRedFlagStrategy? GetRedFlagStrategy(Dictionary<string, object?> parameters, IRedFlagStrategy? defaultStrategy)
    {
        var config = parameters.GetValueOrDefault("red_flag");
        return config switch
        {
            false or "false" or "none" or "disabled" => null,
            true or "true" or "default" => defaultStrategy,
            "english" => new DefaultEnglishRedFlagStrategy(),
            "chinese" => new ChineseRedFlagStrategy(),
            "code" => new CodeAwareRedFlagStrategy(),
            Dictionary<string, object?> dict => ParseRedFlagConfig(dict),
            string name => ParseRedFlagByName(name) ?? defaultStrategy,
            null => defaultStrategy,
            _ => defaultStrategy
        };
    }
    
    // ─────────────────────────────────────────────────────────
    //  私有方法
    // ─────────────────────────────────────────────────────────
    
    private static int ToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };
    
    private static float ToFloat(object? value, float defaultValue) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        decimal m => (float)m,
        string s when float.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };
    
    private static IRedFlagStrategy? ParseRedFlagConfig(Dictionary<string, object?> config)
    {
        if (config.GetValueOrDefault("enabled") is false or "false") return null;
        
        var options = new RedFlagOptions
        {
            MinContentLength = config.TryGetValue("min_length", out var min) && min != null 
                ? Convert.ToInt32(min) : 10,
            MaxContentLength = config.TryGetValue("max_length", out var max) && max != null 
                ? Convert.ToInt32(max) : 8000,
            EnableRefusalDetection = config.GetValueOrDefault("detect_refusal") is not (false or "false"),
            EnableDegenerationDetection = config.GetValueOrDefault("detect_degeneration") is not (false or "false"),
            EnableLengthValidation = config.GetValueOrDefault("validate_length") is not (false or "false")
        };
        
        var name = config.GetValueOrDefault("strategy")?.ToString() ?? "english";
        return name.ToLowerInvariant() switch
        {
            "english" => new DefaultEnglishRedFlagStrategy(options),
            "chinese" => new ChineseRedFlagStrategy(options),
            "code" => new CodeAwareRedFlagStrategy(options),
            _ => new DefaultEnglishRedFlagStrategy(options)
        };
    }
    
    private static IRedFlagStrategy? ParseRedFlagByName(string name) => name.ToLowerInvariant() switch
    {
        "english" => new DefaultEnglishRedFlagStrategy(),
        "chinese" => new ChineseRedFlagStrategy(),
        "code" => new CodeAwareRedFlagStrategy(),
        "none" or "disabled" or "false" => null,
        _ => null
    };
}

/// <summary>
/// 通用类型转换工具。
/// </summary>
public static class TypeConverter
{
    /// <summary>
    /// 将对象转换为列表。
    /// </summary>
    public static List<object> ToList(object items) => items switch
    {
        IEnumerable<object> enumerable => enumerable.ToList(),
        System.Collections.IList list => list.Cast<object>().ToList(),
        System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
        _ => [items]
    };
    
    /// <summary>
    /// 应用聚合器。
    /// </summary>
    public static object ApplyReducer(List<object> results, string reducer) =>
        reducer.ToLowerInvariant() switch
        {
            "collect" => results,
            "flatten" => results.SelectMany(r => r switch
            {
                IEnumerable<object> list => list,
                System.Collections.IList list => list.Cast<object>(),
                _ => new[] { r }
            }).ToList(),
            "first" => results.FirstOrDefault()!,
            "last" => results.LastOrDefault()!,
            "concat" => string.Join("\n", results.Select(r => r.ToString())),
            _ => results
        };
}
