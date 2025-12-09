using Aevatar.Agents.AI.Abstractions.Providers;

namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// Microsoft.Extensions.AI LLM Provider 扩展
/// </summary>
public static class MEAIExtensions
{
    /// <summary>
    /// 添加 MEAI LLM Provider
    /// </summary>
    public static IServiceCollection AddMEAILLMProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 LLM Provider Factory
        // 这里使用 MEAI (Microsoft.Extensions.AI)
        // 需要根据实际项目中的 Provider 实现来配置

        // 从配置读取 LLM 设置
        var llmSection = configuration.GetSection("LLM");
        var provider = llmSection["Provider"] ?? "OpenAI";
        var model = llmSection["Model"] ?? "gpt-4";
        var apiKey = llmSection["ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? "";

        Console.WriteLine($"🧠 LLM Provider: {provider}");
        Console.WriteLine($"🤖 Model: {model}");
        Console.WriteLine($"🔑 API Key: {(string.IsNullOrEmpty(apiKey) ? "❌ Not Set" : "✅ Configured")}");

        // 注册 ILLMProviderFactory
        // 实际实现需要根据 Aevatar.Agents.AI.MEAI 的具体 API
        services.AddSingleton<ILLMProviderFactory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ILLMProviderFactory>>();
            
            // 使用项目中的 MEAI Provider Factory
            // 这里是占位实现，需要根据实际代码调整
            return sp.GetRequiredService<ILLMProviderFactory>();
        });

        return services;
    }
}
