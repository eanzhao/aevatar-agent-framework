using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.Core.Tests.Agents.AI;

/// <summary>
/// Mock LLM provider for testing purposes
/// </summary>
public class MockLLMProvider : IAevatarLLMProvider
{
    private readonly Queue<AevatarLLMResponse> _responses = new();
    private readonly List<AevatarLLMRequest> _capturedRequests = new();
    private AevatarModelInfo? _modelInfo;
    private bool _throwOnGenerate;
    private Exception? _exceptionToThrow;
    
    public IReadOnlyList<AevatarLLMRequest> CapturedRequests => _capturedRequests;
    
    public MockLLMProvider(params AevatarLLMResponse[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }
    
    public void EnqueueResponse(AevatarLLMResponse response)
    {
        _responses.Enqueue(response);
    }
    
    public void SetModelInfo(AevatarModelInfo modelInfo)
    {
        _modelInfo = modelInfo;
    }
    
    public void ConfigureToThrow(Exception exception)
    {
        _throwOnGenerate = true;
        _exceptionToThrow = exception;
    }
    
    public Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        _capturedRequests.Add(request);
        
        if (_throwOnGenerate && _exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }
        
        if (_responses.Count == 0)
        {
            // Return a default response if none queued
            return Task.FromResult(new AevatarLLMResponse
            {
                Content = "Default mock response",
                AevatarStopReason = AevatarStopReason.Complete,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = 10,
                    CompletionTokens = 5,
                    TotalTokens = 15
                }
            });
        }
        
        return Task.FromResult(_responses.Dequeue());
    }
    
    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _capturedRequests.Add(request);
        
        if (_throwOnGenerate && _exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }
        
        var response = _responses.Count > 0 
            ? _responses.Dequeue() 
            : new AevatarLLMResponse { Content = "Streamed response" };
        
        // Simulate streaming by splitting the content into tokens
        var words = response.Content.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            yield return new AevatarLLMToken
            {
                Content = words[i] + (i < words.Length - 1 ? " " : ""),
                IsComplete = i == words.Length - 1
            };
            
            // Simulate some delay
            await Task.Delay(10, cancellationToken);
        }
    }
    
    public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.FromResult(_modelInfo ?? new AevatarModelInfo
        {
            Name = "mock-model",
            MaxTokens = 4096,
            SupportsStreaming = true,
            SupportsFunctions = true
        });
    }
}

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
