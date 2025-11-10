using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions.Tools;
using Aevatar.Agents.AI.Core.Tools;
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// MEAIGAgentBase 新工具系统测试
/// </summary>
public class MEAIGAgentBaseTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<ILogger> _mockLogger;

    public MEAIGAgentBaseTests(ITestOutputHelper output)
    {
        _output = output;
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger = new Mock<ILogger>();
    }

    /// <summary>
    /// 测试新工具系统的基本功能
    /// </summary>
    [Fact]
    public async Task Test_NewAIToolSystem_BasicFunctionality()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // 设置ChatClient的响应
        var mockResponse = new MockChatResponse("Test response");
        _mockChatClient
            .Setup(x => x.CompleteAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act - 测试工具管理器初始化
        Assert.NotNull(agent.AevatarAIToolManager);
        Assert.NotNull(agent.AevatarAIToolContext);
        Assert.Equal(agent.Id.ToString(), agent.AevatarAIToolContext.AgentId);

        // 测试获取所有工具
        var allTools = agent.GetAvailableAevatarAITools();
        Assert.NotEmpty(allTools); // 应该至少包含内置工具
        _output.WriteLine($"Registered tools count: {allTools.Count}");

        foreach (var tool in allTools)
        {
            _output.WriteLine($"Tool: {tool.Name} - {tool.Description}");
        }
    }

    /// <summary>
    /// 测试委托工具注册和执行
    /// </summary>
    [Fact]
    public async Task Test_DelegateTool_RegistrationAndExecution()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // 注册一个委托工具
        agent.RegisterDelegateTool(
            "test_calculator",
            "A simple calculator",
            async (context, parameters, ct) =>
            {
                var a = Convert.ToInt32(parameters["a"]);
                var b = Convert.ToInt32(parameters["b"]);
                var sum = a + b;
                return AevatarAIToolResult.CreateSuccess(new { sum, a, b });
            });

        // Act
        var result = await agent.ExecuteAevatarAIToolAsync(
            "test_calculator",
            new Dictionary<string, object> { { "a", 5 }, { "b", 3 } });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        dynamic data = result.Data;
        Assert.Equal(8, data.sum);
        Assert.Equal(5, data.a);
        Assert.Equal(3, data.b);

        _output.WriteLine($"Tool execution result: {result.Data}");
    }

    /// <summary>
    /// 测试接口工具注册和执行
    /// </summary>
    [Fact]
    public async Task Test_InterfaceTool_RegistrationAndExecution()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // 注册一个接口工具
        var testTool = new TestDataProcessorTool(_mockLogger.Object);
        agent.RegisterInterfaceTool(testTool);

        // Act
        var result = await agent.ExecuteAevatarAIToolAsync(
            "process_test_data",
            new Dictionary<string, object> { { "input", "hello world" } });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        dynamic data = result.Data;
        Assert.Equal("HELLO WORLD", data.result);
        Assert.Equal("hello world", data.input);

        _output.WriteLine($"Tool execution result: {result.Data}");
    }

    /// <summary>
    /// 测试内置工具（内存搜索）
    /// </summary>
    [Fact]
    public async Task Test_BuiltIn_MemorySearchTool()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // Act
        var result = await agent.ExecuteAevatarAIToolAsync(
            "search_memory",
            new Dictionary<string, object>
            {
                { "query", "test" },
                { "maxResults", 5 },
                { "memoryType", "all" }
            });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // 验证返回的数据结构
        dynamic data = result.Data;
        Assert.NotNull(data.results);
        Assert.NotNull(data.count);
        Assert.Equal("test", data.query);
        Assert.Equal("all", data.memoryType);

        _output.WriteLine($"Memory search result: {result.Data}");
    }

    /// <summary>
    /// 测试工具执行失败情况
    /// </summary>
    [Fact]
    public async Task Test_ToolExecution_Failure()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // 注册一个会失败的工具
        agent.RegisterDelegateTool(
            "failing_tool",
            "A tool that always fails",
            async (context, parameters, ct) =>
            {
                throw new InvalidOperationException("Tool execution failed");
            });

        // Act
        var result = await agent.ExecuteAevatarAIToolAsync(
            "failing_tool",
            new Dictionary<string, object>());

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Tool execution failed", result.ErrorMessage);

        _output.WriteLine($"Tool failure message: {result.ErrorMessage}");
    }

    /// <summary>
    /// 测试不存在的工具
    /// </summary>
    [Fact]
    public async Task Test_NonExistentTool()
    {
        // Arrange
        var agent = new TestAgent(_mockChatClient.Object, _mockLogger.Object);

        // Act
        var result = await agent.ExecuteAevatarAIToolAsync(
            "non_existent_tool",
            new Dictionary<string, object>());

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);

        _output.WriteLine($"Non-existent tool error: {result.ErrorMessage}");
    }
}

/// <summary>
/// 测试用的Agent实现
/// </summary>
public class TestAgent : MEAIGAgentBase<TestAgentState>
{
    protected override string SystemPrompt => "You are a test agent for unit testing";

    public TestAgent(IChatClient chatClient, ILogger? logger = null)
        : base(chatClient, logger)
    {
    }

    /// <summary>
    /// 用于测试的委托工具注册方法
    /// </summary>
    public void RegisterDelegateTool(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc)
    {
        AevatarAIToolManager.RegisterAevatarAITool(name, description, executeFunc);
    }

    /// <summary>
    /// 用于测试的接口工具注册方法
    /// </summary>
    public void RegisterInterfaceTool(IAevatarAITool tool)
    {
        AevatarAIToolManager.RegisterAevatarAITool(tool);
    }
}

/// <summary>
/// 测试用的数据处理工具
/// </summary>
public class TestDataProcessorTool : IAevatarAITool
{
    private readonly ILogger _logger;

    public string Name => "process_test_data";
    public string Description => "Process test data by converting to uppercase";

    public TestDataProcessorTool(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var input = parameters.GetValueOrDefault("input")?.ToString() ?? "";
        var result = input.ToUpper();

        _logger.LogInformation("Processed test data: {Input} -> {Result}", input, result);

        return AevatarAIToolResult.CreateSuccess(new { input, result });
    }
}

/// <summary>
/// 测试用的状态类
/// </summary>
public class TestAgentState : IMessage<TestAgentState>
{
    public string TestData { get; set; } = string.Empty;
    public int Counter { get; set; }

    // 简化的IMessage实现
    public MessageDescriptor Descriptor => throw new NotImplementedException();
    public int CalculateSize() => 0;
    public void MergeFrom(TestAgentState message) { }
    public void MergeFrom(CodedInputStream input) { }
    public void WriteTo(CodedOutputStream output) { }
    public TestAgentState Clone() => new TestAgentState
    {
        TestData = this.TestData,
        Counter = this.Counter
    };
    public bool Equals(TestAgentState? other) => false;
    public void Freeze() { }
    public bool IsFrozen => false;
    Google.Protobuf.IMessage Google.Protobuf.IMessage.Clone() => Clone();
    public void WriteTo(CodedOutputStream output) { }
    public int CalculateSize() => 0;
    public Google.Protobuf.IMessage? FindFieldByName(string name) => null;
    public Google.Protobuf.Reflection.TypeRegistry TypeRegistry { get; set; } = Google.Protobuf.Reflection.TypeRegistry.Empty;
}

/// <summary>
/// 模拟的ChatResponse
/// </summary>
public class MockChatResponse : ChatResponse
{
    public MockChatResponse(string text)
    {
        Message = new ChatMessage(ChatRole.Assistant, text);
    }

    public override ChatMessage Message { get; }
    public override string? Text => Message.Text;
    public override IReadOnlyList<ChatMessage>? Messages => new[] { Message };
    public override int Count => 1;
    public override ChatFinishReason FinishReason => ChatFinishReason.Stop;
    public override UsageDetails? Usage => null;
    public override IReadOnlyList<ChatResponseUpdate>? Updates => null;
    public override ChatResponse this[int index] => this;
    public override IEnumerator<ChatResponse> GetEnumerator()
    {
        yield return this;
    }
    public override ChatResponse UpdateFrom(ChatResponseUpdate update) => this;
    public override ChatResponse Clone() => new MockChatResponse(Text ?? "");
}