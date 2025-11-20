namespace Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;

/// <summary>
/// Mock LLM provider with function call support
/// </summary>
public class MockFunctionCallingLLMProvider : MockLLMProvider
{
    private readonly Queue<AevatarFunctionCall> _functionCalls = new();

    public MockFunctionCallingLLMProvider() : base()
    {
    }

    public void EnqueueFunctionCall(AevatarFunctionCall functionCall)
    {
        _functionCalls.Enqueue(functionCall);
    }

    public new Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // If function call is queued, return it
        if (_functionCalls.Count > 0)
        {
            return Task.FromResult(new AevatarLLMResponse
            {
                Content = string.Empty,
                AevatarFunctionCall = _functionCalls.Dequeue(),
                AevatarStopReason = AevatarStopReason.AevatarFunctionCall,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = 10,
                    CompletionTokens = 5,
                    TotalTokens = 15
                }
            });
        }

        return base.GenerateAsync(request, cancellationToken);
    }
}