namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Red Flag Strategy - Content Validation Interface
//  Allows customization of proposal validation rules
// ============================================================

/// <summary>
/// Strategy for validating LLM proposal content before voting.
/// Implement this to customize red-flag detection for your domain.
/// </summary>
public interface IRedFlagStrategy
{
    /// <summary>
    /// Validate proposal content.
    /// </summary>
    /// <param name="content">The LLM-generated content to validate.</param>
    /// <param name="proposalId">The proposal identifier (for logging).</param>
    /// <param name="reason">If invalid, the reason for rejection.</param>
    /// <returns>True if content is valid, false if it should be rejected.</returns>
    bool Validate(string content, string proposalId, out string? reason);
}

/// <summary>
/// Red flag validation options - configurable limits.
/// </summary>
public sealed record RedFlagOptions
{
    /// <summary>
    /// Maximum allowed content length. Default: 8000 characters.
    /// For long-form content (e.g., novels), increase this significantly.
    /// </summary>
    public int MaxContentLength { get; init; } = 8000;
    
    /// <summary>
    /// Minimum required content length. Default: 10 characters.
    /// Very short responses are usually invalid.
    /// </summary>
    public int MinContentLength { get; init; } = 10;
    
    /// <summary>
    /// Minimum repeated character length for degeneration detection. Default: 3.
    /// </summary>
    public int DegenerationMinLength { get; init; } = 3;
    
    /// <summary>
    /// Maximum repetitions before flagging as degeneration. Default: 10.
    /// </summary>
    public int DegenerationMaxRepetitions { get; init; } = 10;
    
    /// <summary>
    /// Custom refusal prefixes to detect. If null, uses language-specific defaults.
    /// </summary>
    public IReadOnlyList<string>? CustomRefusalPrefixes { get; init; }
    
    /// <summary>
    /// Enable/disable refusal detection. Default: true.
    /// </summary>
    public bool EnableRefusalDetection { get; init; } = true;
    
    /// <summary>
    /// Enable/disable degeneration detection. Default: true.
    /// </summary>
    public bool EnableDegenerationDetection { get; init; } = true;
    
    /// <summary>
    /// Enable/disable length validation. Default: true.
    /// </summary>
    public bool EnableLengthValidation { get; init; } = true;
}

/// <summary>
/// Default red flag strategy for English content.
/// Detects: length violations, LLM refusals, repetitive degeneration.
/// </summary>
public sealed class DefaultEnglishRedFlagStrategy : IRedFlagStrategy
{
    private readonly RedFlagOptions _options;
    
    /// <summary>
    /// Common English refusal prefixes.
    /// </summary>
    private static readonly string[] DefaultEnglishRefusalPrefixes =
    [
        "I cannot",
        "I can't",
        "I'm unable to",
        "I am unable to",
        "I'm not able to",
        "I am not able to",
        "I apologize",
        "I'm sorry",
        "I am sorry",
        "Sorry, but I",
        "Unfortunately, I cannot",
        "As an AI",
        "As a language model",
        "I don't have the ability"
    ];
    
    public DefaultEnglishRedFlagStrategy() : this(new RedFlagOptions()) { }
    
    public DefaultEnglishRedFlagStrategy(RedFlagOptions options)
    {
        _options = options ?? new RedFlagOptions();
    }
    
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "Empty content";
            return false;
        }
        
        // Length validation
        if (_options.EnableLengthValidation)
        {
            if (content.Length > _options.MaxContentLength)
            {
                reason = $"Content too long ({content.Length} > {_options.MaxContentLength} chars)";
                return false;
            }
            
            if (content.Length < _options.MinContentLength)
            {
                reason = $"Content too short ({content.Length} < {_options.MinContentLength} chars)";
                return false;
            }
        }
        
        // Refusal detection
        if (_options.EnableRefusalDetection)
        {
            var prefixes = _options.CustomRefusalPrefixes ?? DefaultEnglishRefusalPrefixes;
            foreach (var prefix in prefixes)
            {
                if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"LLM refusal detected: \"{prefix}...\"";
                    return false;
                }
            }
        }
        
        // Degeneration detection (repetitive text)
        if (_options.EnableDegenerationDetection && HasExcessiveRepetition(content))
        {
            reason = "Excessive repetition detected (potential degeneration)";
            return false;
        }
        
        return true;
    }
    
    private bool HasExcessiveRepetition(string content)
    {
        var minLen = _options.DegenerationMinLength;
        var maxReps = _options.DegenerationMaxRepetitions;
        
        for (var len = minLen; len <= Math.Min(20, content.Length / maxReps); len++)
        {
            for (var start = 0; start <= content.Length - len * maxReps; start++)
            {
                var pattern = content.Substring(start, len);
                var count = 1;
                var pos = start + len;
                
                while (pos + len <= content.Length && 
                       content.Substring(pos, len) == pattern)
                {
                    count++;
                    pos += len;
                    if (count >= maxReps) return true;
                }
            }
        }
        return false;
    }
}

/// <summary>
/// Red flag strategy for Chinese content.
/// </summary>
public sealed class ChineseRedFlagStrategy : IRedFlagStrategy
{
    private readonly RedFlagOptions _options;
    
    private static readonly string[] ChineseRefusalPrefixes =
    [
        "我无法",
        "我不能",
        "抱歉",
        "对不起",
        "很抱歉",
        "非常抱歉",
        "作为AI",
        "作为一个AI",
        "作为语言模型",
        "我没有能力",
        "这超出了我的能力",
        "我无权"
    ];
    
    public ChineseRedFlagStrategy() : this(new RedFlagOptions()) { }
    
    public ChineseRedFlagStrategy(RedFlagOptions options)
    {
        _options = options ?? new RedFlagOptions();
    }
    
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "内容为空";
            return false;
        }
        
        // Length validation
        if (_options.EnableLengthValidation)
        {
            if (content.Length > _options.MaxContentLength)
            {
                reason = $"内容过长 ({content.Length} > {_options.MaxContentLength} 字符)";
                return false;
            }
            
            if (content.Length < _options.MinContentLength)
            {
                reason = $"内容过短 ({content.Length} < {_options.MinContentLength} 字符)";
                return false;
            }
        }
        
        // Refusal detection
        if (_options.EnableRefusalDetection)
        {
            var prefixes = _options.CustomRefusalPrefixes ?? ChineseRefusalPrefixes;
            foreach (var prefix in prefixes)
            {
                if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"检测到LLM拒绝: \"{prefix}...\"";
                    return false;
                }
            }
        }
        
        // Degeneration detection
        if (_options.EnableDegenerationDetection && HasExcessiveRepetition(content))
        {
            reason = "检测到过度重复（可能是生成退化）";
            return false;
        }
        
        return true;
    }
    
    private bool HasExcessiveRepetition(string content)
    {
        // Chinese characters need shorter pattern detection
        var minLen = Math.Max(1, _options.DegenerationMinLength - 1);
        var maxReps = _options.DegenerationMaxRepetitions;
        
        for (var len = minLen; len <= Math.Min(10, content.Length / maxReps); len++)
        {
            for (var start = 0; start <= content.Length - len * maxReps; start++)
            {
                var pattern = content.Substring(start, len);
                var count = 1;
                var pos = start + len;
                
                while (pos + len <= content.Length && 
                       content.Substring(pos, len) == pattern)
                {
                    count++;
                    pos += len;
                    if (count >= maxReps) return true;
                }
            }
        }
        return false;
    }
}

/// <summary>
/// Composite strategy that combines multiple red flag strategies.
/// Content must pass ALL strategies to be valid.
/// </summary>
public sealed class CompositeRedFlagStrategy : IRedFlagStrategy
{
    private readonly IReadOnlyList<IRedFlagStrategy> _strategies;
    
    public CompositeRedFlagStrategy(params IRedFlagStrategy[] strategies)
    {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
    }
    
    public bool Validate(string content, string proposalId, out string? reason)
    {
        foreach (var strategy in _strategies)
        {
            if (!strategy.Validate(content, proposalId, out reason))
            {
                return false;
            }
        }
        reason = null;
        return true;
    }
}

/// <summary>
/// No-op strategy that accepts all content.
/// Use when you want to disable red-flag checking entirely.
/// </summary>
public sealed class NoOpRedFlagStrategy : IRedFlagStrategy
{
    public static readonly NoOpRedFlagStrategy Instance = new();
    
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        return true;
    }
}

/// <summary>
/// Code-aware red flag strategy that avoids false positives in code content.
/// </summary>
public sealed class CodeAwareRedFlagStrategy : IRedFlagStrategy
{
    private readonly RedFlagOptions _options;
    
    public CodeAwareRedFlagStrategy() : this(new RedFlagOptions
    {
        // Code often has longer outputs
        MaxContentLength = 50000,
        // Disable refusal detection - comments might contain "I cannot" etc.
        EnableRefusalDetection = false,
        // Relax degeneration detection - code has legitimate repetition
        DegenerationMaxRepetitions = 20
    }) { }
    
    public CodeAwareRedFlagStrategy(RedFlagOptions options)
    {
        _options = options ?? new RedFlagOptions();
    }
    
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "Empty content";
            return false;
        }
        
        // Only validate length for code
        if (_options.EnableLengthValidation)
        {
            if (content.Length > _options.MaxContentLength)
            {
                reason = $"Content too long ({content.Length} > {_options.MaxContentLength} chars)";
                return false;
            }
        }
        
        return true;
    }
}

