using Aevatar.Agents.AI.Core;

namespace Aevatar.Agents.AI.Tests;

public class CustomerServiceAgent : AIGAgentBase<AevatarAIAgentState>
{
    // 在编码时定义 System Prompt
    public override string SystemPrompt =>
        "You are Emma, a friendly customer service representative. " +
        "You work for Aevatar Inc. and help customers with their questions. " +
        "Always be helpful, patient, and professional.";

    // 必须重写：提供代理描述
    public override string GetDescription()
    {
        return "Customer service agent for Aevatar Inc.";
    }

    // 可选：配置 AI 参数
    protected override void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        config.Model = "gpt-4";
        config.Temperature = 0.7;
        config.MaxTokens = 2000;
        config.MaxHistory = 50; // 保存 50 条对话历史
    }
}