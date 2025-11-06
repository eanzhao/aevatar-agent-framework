# JSON Path 支持 (使用 System.Text.Json)

## 概述

`DefaultToolProvider` 现在使用 **System.Text.Json** 而不是 Newtonsoft.Json 来处理 JSON 操作。这提供了更好的性能和更小的依赖包体积。

## 功能特性

### 1. JSON Path 导航

虽然 System.Text.Json 不内置 JSON Path 支持，但我们实现了简化的 JSON Path 导航功能：

```csharp
// 支持的格式
"$.property"           // 访问对象属性
"$.nested.property"    // 访问嵌套属性
"$[0]"                // 访问数组元素
"$.array[2]"          // 访问数组中的特定元素
"$.nested[0].prop"    // 组合访问
```

### 2. 工具实现示例

#### query_state 工具

查询 Agent 状态，支持 JSON Path：

```csharp
// 使用示例
var parameters = new Dictionary<string, object>
{
    ["field"] = "State",
    ["path"] = "$.tasks[0].status"  // 可选的 JSON Path
};

// 返回 State.tasks[0].status 的值
```

#### publish_event 工具

动态创建和发布事件：

```csharp
// 使用示例
var parameters = new Dictionary<string, object>
{
    ["event_type"] = "TaskCompleted",
    ["payload"] = new 
    {
        TaskId = "123",
        Result = "Success"
    },
    ["direction"] = "up"
};
```

#### search_memory 工具

搜索 Agent 记忆：

```csharp
// 使用示例
var parameters = new Dictionary<string, object>
{
    ["query"] = "previous conversation about pricing",
    ["top_k"] = 5,
    ["memory_type"] = "conversation"
};
```

## 实现细节

### NavigateJsonPath 方法

```csharp
private object? NavigateJsonPath(object obj, string path)
{
    // 1. 序列化对象为 JSON
    var json = JsonSerializer.Serialize(obj);
    var jsonNode = JsonNode.Parse(json);
    
    // 2. 解析路径并导航
    var result = NavigateJsonNode(jsonNode, path);
    
    // 3. 转换结果为适当的类型
    return ConvertJsonNodeToObject(result);
}
```

### 路径解析

```csharp
private List<string> ParsePathSegments(string path)
{
    // 将路径分解为段
    // "$.nested[0].prop" -> ["nested", "[0]", "prop"]
}
```

### 动态事件创建

```csharp
private IMessage? CreateDynamicEvent(string? eventType, object payload)
{
    // 支持多种 payload 类型
    var jsonPayload = payload switch
    {
        string str => str,
        JsonNode jNode => jNode.ToJsonString(),
        JsonElement jElement => jElement.GetRawText(),
        _ => JsonSerializer.Serialize(payload)
    };
    
    // 创建 EventEnvelope
    return new EventEnvelope
    {
        Id = Guid.NewGuid().ToString(),
        Message = eventType ?? "DynamicEvent",
        Payload = Any.Pack(new StringValue { Value = jsonPayload }),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        // ... 其他字段
    };
}
```

## 性能优势

### 与 Newtonsoft.Json 的对比

| 特性 | System.Text.Json | Newtonsoft.Json |
|-----|-----------------|-----------------|
| 内存占用 | ✅ 更低 | 较高 |
| 序列化速度 | ✅ 更快 | 较慢 |
| 反序列化速度 | ✅ 更快 | 较慢 |
| 包体积 | ✅ 内置于 .NET | 需要额外包 |
| JSON Path 支持 | ⚠️ 自定义实现 | ✅ 内置支持 |
| 灵活性 | 适中 | ✅ 非常灵活 |

## 使用指南

### 1. 基本用法

```csharp
public class CustomToolProvider : DefaultToolProvider
{
    protected override async Task<IEnumerable<AevatarTool>> GetCustomToolsAsync(
        ToolContext context)
    {
        var tools = await base.GetCustomToolsAsync(context);
        
        // 添加自定义工具
        tools.Add(new AevatarTool
        {
            Name = "my_tool",
            ExecuteAsync = async (parameters, ctx, ct) =>
            {
                // 使用 System.Text.Json 处理参数
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                
                var json = JsonSerializer.Serialize(parameters, jsonOptions);
                // ... 处理逻辑
            }
        });
        
        return tools;
    }
}
```

### 2. JSON Path 查询

```csharp
// 获取嵌套属性
var value = GetFieldValue(myObject, "config", "$.database.connectionString");

// 获取数组元素
var firstItem = GetFieldValue(myList, "items", "$[0]");

// 复杂路径
var nested = GetFieldValue(data, "response", "$.results[2].metadata.tags");
```

### 3. 事件处理

```csharp
// 创建动态事件
var eventMessage = CreateDynamicEvent("UserAction", new
{
    Action = "clicked",
    Target = "submit_button",
    Timestamp = DateTime.UtcNow
});

// 发布事件
await PublishEventCallback(eventMessage);
```

## 限制和注意事项

1. **JSON Path 限制**
   - 不支持通配符 (`*`)
   - 不支持递归下降 (`..`)
   - 不支持过滤表达式 (`[?()]`)
   - 不支持切片操作 (`[start:end]`)

2. **性能考虑**
   - 大对象的序列化/反序列化可能影响性能
   - 深层嵌套的 JSON Path 查询可能较慢
   - 考虑缓存频繁访问的值

3. **类型处理**
   - 数值类型优先尝试 `long`，失败则使用 `double`
   - 日期时间需要正确的格式化
   - 自定义类型需要适当的转换器

## 未来改进

1. **增强 JSON Path 支持**
   - 添加通配符支持
   - 实现过滤表达式
   - 支持更复杂的查询

2. **性能优化**
   - 实现查询结果缓存
   - 优化路径解析算法
   - 减少不必要的序列化

3. **工具增强**
   - 添加批量操作支持
   - 实现工具组合
   - 支持异步流式处理

## 示例测试

```csharp
[Test]
public async Task TestJsonPathNavigation()
{
    var provider = new DefaultToolProvider();
    var data = new
    {
        Users = new[]
        {
            new { Name = "Alice", Age = 30 },
            new { Name = "Bob", Age = 25 }
        }
    };
    
    // 测试路径导航
    var result = provider.NavigateJsonPath(data, "$.Users[0].Name");
    Assert.AreEqual("Alice", result);
    
    // 测试数组访问
    var age = provider.NavigateJsonPath(data, "$.Users[1].Age");
    Assert.AreEqual(25L, age);
}
```

## 总结

使用 System.Text.Json 提供了更好的性能和更小的依赖，同时通过自定义实现保持了基本的 JSON Path 功能。虽然不如 Newtonsoft.Json 功能丰富，但对于大多数用例来说已经足够，并且带来了显著的性能提升。
