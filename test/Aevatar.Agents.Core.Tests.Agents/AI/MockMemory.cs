using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.Core.Tests.Agents.AI;

/// <summary>
/// Mock memory implementation for testing
/// </summary>
public class MockMemory : IAevatarAIMemory
{
    private readonly List<Aevatar.Agents.AI.AevatarConversationEntry> _history = new();
    private readonly List<(string query, IReadOnlyList<string> results)> _searchHistory = new();
    
    public IReadOnlyList<Aevatar.Agents.AI.AevatarConversationEntry> History => _history;
    public IReadOnlyList<(string query, IReadOnlyList<string> results)> SearchHistory => _searchHistory;
    
    public Task AddMessageAsync(string role, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        _history.Add(new Aevatar.Agents.AI.AevatarConversationEntry
        {
            Role = role,
            Content = content
        });
        
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<AevatarConversationEntry>> GetConversationHistoryAsync(
        int? limit = null, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var result = limit.HasValue 
            ? _history.TakeLast(limit.Value).ToList() 
            : _history.ToList();
        
        return Task.FromResult<IReadOnlyList<Aevatar.Agents.AI.AevatarConversationEntry>>(result);
    }
    
    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _history.Clear();
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<string>> SearchAsync(
        string query, 
        int topK = 5, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        // Simulate semantic search - return messages containing the query string
        var results = _history
            .Where(h => h.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Content)
            .Take(topK)
            .ToList();
        
        _searchHistory.Add((query, results));
        
        return Task.FromResult<IReadOnlyList<string>>(results);
    }
}

/// <summary>
/// Mock vector memory with simulated semantic search
/// </summary>
public class MockVectorMemory : MockMemory
{
    private readonly Dictionary<string, float[]> _vectorStore = new();
    private readonly Random _random = new(42);
    
    public new async Task AddMessageAsync(string role, string content, CancellationToken cancellationToken = default)
    {
        await base.AddMessageAsync(role, content, cancellationToken);
        
        // Simulate vector embedding
        var vector = GenerateMockVector(content);
        _vectorStore[content] = vector;
    }
    
    public new Task<IReadOnlyList<string>> SearchAsync(
        string query, 
        int topK = 5, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (_vectorStore.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string>());
        }
        
        // Simulate vector similarity search
        var queryVector = GenerateMockVector(query);
        var scores = new List<(string content, float similarity)>();
        
        foreach (var kvp in _vectorStore)
        {
            var similarity = CalculateMockSimilarity(queryVector, kvp.Value);
            scores.Add((kvp.Key, similarity));
        }
        
        var results = scores
            .OrderByDescending(s => s.similarity)
            .Take(topK)
            .Select(s => s.content)
            .ToList();
        
        return Task.FromResult<IReadOnlyList<string>>(results);
    }
    
    private float[] GenerateMockVector(string text)
    {
        // Generate a deterministic mock vector based on text content
        var hash = text.GetHashCode();
        var vector = new float[128];
        
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)((hash + i) % 100) / 100f;
        }
        
        return vector;
    }
    
    private float CalculateMockSimilarity(float[] vector1, float[] vector2)
    {
        // Simple dot product for similarity
        float similarity = 0;
        for (int i = 0; i < Math.Min(vector1.Length, vector2.Length); i++)
        {
            similarity += vector1[i] * vector2[i];
        }
        return similarity;
    }
}
