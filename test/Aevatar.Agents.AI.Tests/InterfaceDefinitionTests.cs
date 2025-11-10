using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions.Tools;
using Aevatar.Agents.AI.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// 测试IAevatarAIToolManager接口定义
/// </summary>
public class InterfaceDefinitionTests
{
    [Fact]
    public void Test_IAevatarAIToolManager_InterfaceDefinition()
    {
        // Arrange
        var mockManager = new Mock<IAevatarAIToolManager>();
        var testTool = new TestTool();
        var testContext = new AevatarAIToolContext
        {
            AgentId = "test-agent",
            ServiceProvider = Microsoft.Extensions.DependencyInjection.EmptyServiceProvider.Instance,
            CancellationToken = CancellationToken.None
        };

        // Setup mock behavior
        mockManager.Setup(x => x.AevatarAIToolExists(It.IsAny<string>())).Returns(true);
        mockManager.Setup(x => x.GetAevatarAITool(It.IsAny<string>())).Returns(testTool);
        mockManager.Setup(x => x.GetAllAevatarAITools()).Returns(new List<IAevatarAITool> { testTool });
        mockManager.Setup(x => x.ExecuteAevatarAIToolAsync(
            It.IsAny<string>(),
            It.IsAny<AevatarAIToolContext>(),
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(AevatarAIToolResult.CreateSuccess("test result"));

        var manager = mockManager.Object;

        // Act & Assert
        Assert.NotNull(manager);

        // Test interface methods exist and can be called
        manager.RegisterAevatarAITool(testTool);

        manager.RegisterAevatarAITool(
            "test_delegate",
            "Test delegate tool",
            async (context, parameters, ct) =>
            {
                await Task.Delay(1, ct);
                return AevatarAIToolResult.CreateSuccess("delegate result");
            });

        var exists = manager.AevatarAIToolExists("test_tool");
        Assert.True(exists);

        var tool = manager.GetAevatarAITool("test_tool");
        Assert.NotNull(tool);
        Assert.Equal("test_tool", tool.Name);

        var allTools = manager.GetAllAevatarAITools();
        Assert.NotEmpty(allTools);

        var result = manager.ExecuteAevatarAIToolAsync(
            "test_tool",
            testContext,
            new Dictionary<string, object>(),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Equal("test result", result.Data);
    }

    [Fact]
    public void Test_IAevatarAITool_InterfaceDefinition()
    {
        // Arrange
        var tool = new TestTool();
        var context = new AevatarAIToolContext
        {
            AgentId = "test-agent",
            ServiceProvider = Microsoft.Extensions.DependencyInjection.EmptyServiceProvider.Instance,
            CancellationToken = CancellationToken.None
        };

        // Act & Assert
        Assert.Equal("test_tool", tool.Name);
        Assert.Equal("A test tool", tool.Description);

        var result = tool.ExecuteAsync(
            context,
            new Dictionary<string, object>(),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Equal("test executed", result.Data);
    }

    [Fact]
    public void Test_AevatarAIToolResult_Creation()
    {
        // Test success result
        var successResult = AevatarAIToolResult.CreateSuccess(new { data = "test" });
        Assert.True(successResult.Success);
        Assert.NotNull(successResult.Data);
        Assert.Null(successResult.ErrorMessage);

        // Test failure result
        var failureResult = AevatarAIToolResult.CreateFailure("test error");
        Assert.False(failureResult.Success);
        Assert.Null(failureResult.Data);
        Assert.Equal("test error", failureResult.ErrorMessage);

        // Test result with metadata
        var resultWithMetadata = new AevatarAIToolResult
        {
            Success = true,
            Data = new { value = 42 },
            Metadata = new Dictionary<string, object> { { "key", "value" } }
        };

        Assert.True(resultWithMetadata.Success);
        Assert.NotNull(resultWithMetadata.Metadata);
        Assert.Equal("value", resultWithMetadata.Metadata["key"]);
    }

    [Fact]
    public void Test_AevatarAIToolContext_Properties()
    {
        // Arrange
        var context = new AevatarAIToolContext
        {
            AgentId = "test-agent-123",
            ServiceProvider = Microsoft.Extensions.DependencyInjection.EmptyServiceProvider.Instance,
            CancellationToken = new CancellationTokenSource().Token
        };

        // Act & Assert
        Assert.Equal("test-agent-123", context.AgentId);
        Assert.NotNull(context.ServiceProvider);
        Assert.NotNull(context.CancellationToken);

        // Test GetService method (should throw for non-existent service)
        Assert.Throws<InvalidOperationException>(() => context.GetService<ILogger>());

        // Test GetConfiguration method
        var config = context.GetConfiguration<object>();
        Assert.NotNull(config);
    }
}

/// <summary>
/// 测试用的工具实现
/// </summary>
public class TestTool : IAevatarAITool
{
    public string Name => "test_tool";
    public string Description => "A test tool";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return AevatarAIToolResult.CreateSuccess("test executed");
    }
}