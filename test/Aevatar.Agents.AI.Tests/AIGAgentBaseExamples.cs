using System;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// AIGAgentBase 使用示例
/// 展示如何正确地从 AIGAgentBase 继承创建 AI Agent
/// </summary>
public partial class AIGAgentBaseExamples
{
    /// <summary>
    /// 示例 2：带工具的数据分析 Agent（Level 2）
    /// </summary>
    public class DataAnalysisAgent : AIGAgentBase<AevatarAIAgentState>
    {
        public override string SystemPrompt =>
            "You are a data analyst. Help users analyze data and create visualizations. " +
            "Always explain your reasoning step by step.";

        public DataAnalysisAgent(
            IAevatarLLMProvider llmProvider,
            ILogger<DataAnalysisAgent> logger)
        {
        }

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult("Data analysis agent with visualization tools");
        }

        protected override void ConfigureAI(AevatarAIAgentConfiguration config)
        {
            config.Model = "gpt-4";
            config.Temperature = 0.3;  // Lower temperature for more deterministic analysis
            config.MaxTokens = 4000;
        }
    }

    /// <summary>
    /// 示例 3：如何在应用中使用 AIGAgentBase
    /// </summary>
    public async Task Example_UsingAIGAgentBase(ILLMProviderFactory factory)
    {
        // 1. 配置 LLM Provider（在启动时完成）
        var customerAgentProvider = await factory.GetProviderAsync("openai-gpt4");
        var analysisAgentProvider = await factory.GetProviderAsync("azure-gpt35");

        // 2. 在实际使用时（例如在 Silo 中），通过 GrainFactory 获取 Agent
        // 注意：这里只是示例，真实代码需要有 Orleans 环境

        // 示例：在 Silo 内部的使用方式
        /*
        public class MyService
        {
            private readonly IGrainFactory _grainFactory;
            private readonly ILLMProviderFactory _llmProviderFactory;

            public async Task<string> HandleCustomerQuery(string customerId, string query)
            {
                // 获取或创建 Agent（Grain）
                var agent = _grainFactory.GetGrain<ICustomerServiceAgent>(customerId);

                // Agent 会使用其在构造函数中配置的 LLM Provider
                var response = await agent.GenerateResponseAsync(query);

                return response;
            }

            public async Task<string> AnalyzeData(string taskId, string data)
            {
                var agent = _grainFactory.GetGrain<IDataAnalysisAgent>(taskId);

                // 使用工具进行分析
                var result = await agent.ProcessWithToolsAsync(data);

                return result;
            }
        }
        */

        await Task.CompletedTask;
    }

    /// <summary>
    /// 示例 4：如何直接创建 Agent（用于测试或独立场景）
    /// <para/>
    /// 注意：在真实 Orleans 环境中，应该通过 GrainFactory 获取 Agent
    /// </summary>
    public void Example_CreateAgentDirectly(ILLMProviderFactory factory, ILoggerFactory loggerFactory)
    {
        // 1. 获取 LLM Provider
        var customerAgentProvider = factory.GetProvider("openai-gpt4");
        var analysisAgentProvider = factory.GetProvider("azure-gpt35");

        // 2. 直接创建 Agent（适用于单元测试或独立使用）
        var customerAgent = new CustomerServiceAgent();

        var logger2 = loggerFactory.CreateLogger<DataAnalysisAgent>();
        var analysisAgent = new DataAnalysisAgent(analysisAgentProvider, logger2);

        // 3. 使用 Agent
        // await customerAgent.GenerateResponseAsync("Hello!");
        // await analysisAgent.GenerateResponseAsync("Analyze this data");
    }

    /// <summary>
    /// 示例 5：在 DI 容器中注册 Agent
    /// <para/>
    /// 适用于需要在应用中使用 Agent 的场景
    /// </summary>
    public void Example_RegisterAgentsInDI(IServiceCollection services)
    {
        // 注册 LLM Provider Factory
        services.AddSingleton<ILLMProviderFactory>();

        // 注册 Logger Factory
        services.AddSingleton<ILoggerFactory>();

        // 注册 Agent（简单方式）
        services.AddTransient<CustomerServiceAgent>(sp =>
        {
            var factory = sp.GetRequiredService<ILLMProviderFactory>();
            return new CustomerServiceAgent();
        });

        // 或使用扩展方法（如果创建）
        // services.AddAIGAgent<CustomerServiceAgent>("openai-gpt4");
        // services.AddAIGAgent<DataAnalysisAgent>("azure-gpt35");
    }
}
