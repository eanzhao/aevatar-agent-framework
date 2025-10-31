namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 资源上下文
/// 提供 Agent 可用的资源信息
/// </summary>
public class ResourceContext
{
    /// <summary>
    /// 可用资源列表
    /// </summary>
    public Dictionary<string, object> AvailableResources { get; set; } = new();
    
    /// <summary>
    /// 资源元数据
    /// </summary>
    public Dictionary<string, ResourceMetadata> Metadata { get; set; } = new();
    
    /// <summary>
    /// 添加资源
    /// </summary>
    public void AddResource(string key, object resource, string? description = null)
    {
        AvailableResources[key] = resource;
        Metadata[key] = new ResourceMetadata
        {
            Key = key,
            Type = resource.GetType().Name,
            Description = description ?? string.Empty,
            AddedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// 获取资源
    /// </summary>
    public T? GetResource<T>(string key) where T : class
    {
        if (AvailableResources.TryGetValue(key, out var resource))
        {
            return resource as T;
        }
        
        return null;
    }
    
    /// <summary>
    /// 移除资源
    /// </summary>
    public bool RemoveResource(string key)
    {
        Metadata.Remove(key);
        return AvailableResources.Remove(key);
    }
}

/// <summary>
/// 资源元数据
/// </summary>
public class ResourceMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

