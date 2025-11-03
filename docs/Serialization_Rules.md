# Aevatar Agent Framework 序列化规则

## 核心原则

在 Aevatar Agent Framework 中，所有需要跨运行时边界传输的类型都**必须**使用 Protocol Buffers (Protobuf) 进行定义。这确保了在不同运行时（Local、ProtoActor、Orleans）之间的兼容性和高效序列化。

## 必须使用 Protobuf 的类型

### 1. Agent State 类型
所有 `IGAgent<TState>` 中的 `TState` 类型必须是 Protobuf 消息：

```protobuf
message MyAgentState {
    string id = 1;
    int32 count = 2;
    google.protobuf.Timestamp last_update = 3;
}
```

**原因**：State 需要通过 streaming 机制在不同 Actor 之间传输，特别是在 Orleans 中使用 `byte[]` 进行序列化。

### 2. 事件消息类型
所有通过 `EventEnvelope.Payload` 传输的消息必须是 Protobuf 消息：

```protobuf
message MyEvent {
    string event_id = 1;
    string content = 2;
    double value = 3;
}
```

**原因**：事件通过 `Google.Protobuf.WellKnownTypes.Any` 进行包装，需要正确的 Protobuf 序列化支持。

### 3. Event Sourcing 事件
用于 Event Sourcing 的状态变更事件必须是 Protobuf 消息：

```protobuf
message StateChangeEvent {
    string event_type = 1;
    google.protobuf.Any event_data = 2;
    int64 timestamp = 3;
}
```

**原因**：事件需要持久化并能够重放，Protobuf 提供了版本兼容性保证。

## 项目配置

### 1. 添加 Protobuf 支持

在项目文件中添加必要的包引用：

```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.28.3" />
  <PackageReference Include="Grpc.Tools" Version="2.67.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <Protobuf Include="messages.proto" />
</ItemGroup>
```

### 2. Proto 文件组织

建议的 proto 文件结构：

```
project/
├── messages.proto          # 通用消息定义
├── states.proto           # Agent State 定义
├── events.proto          # 事件定义
└── domain/
    ├── banking.proto     # 银行领域相关
    └── weather.proto     # 天气领域相关
```

## 类型映射指南

### C# 到 Protobuf 类型映射

| C# 类型 | Protobuf 类型 | 说明 |
|---------|---------------|------|
| `decimal` | `double` | Protobuf 不支持 decimal，使用 double 代替 |
| `DateTime` | `google.protobuf.Timestamp` | 使用标准时间戳类型 |
| `Guid` | `string` | GUID 以字符串形式存储 |
| `Dictionary<K,V>` | `map<K,V>` | 使用 map 类型 |
| `List<T>` | `repeated T` | 使用 repeated 字段 |

### 示例转换

**错误示例**（手动定义的 C# 类）：
```csharp
public class BankAccountState
{
    public decimal Balance { get; set; }  // ❌ decimal 不能序列化
    public DateTime LastTransaction { get; set; }  // ❌ DateTime 需要特殊处理
}
```

**正确示例**（Protobuf 定义）：
```protobuf
message BankAccountState {
    double balance = 1;  // ✅ 使用 double
    google.protobuf.Timestamp last_transaction = 2;  // ✅ 使用 Timestamp
}
```

## 最佳实践

### 1. 避免重复定义
不要在 C# 代码中手动定义已经在 proto 文件中定义的类型：

```csharp
// ❌ 错误：手动定义
public class MyState : IMessage<MyState> 
{
    // 手动实现 IMessage 接口
}

// ✅ 正确：使用 proto 生成的类型
// MyState 类会自动从 my_state.proto 生成
```

### 2. 使用 Any 进行灵活包装
当需要传输多种类型的消息时，使用 `Google.Protobuf.WellKnownTypes.Any`：

```csharp
var envelope = new EventEnvelope
{
    Id = Guid.NewGuid().ToString(),
    Payload = Any.Pack(myProtobufMessage),  // 自动序列化
    Direction = EventDirection.Down
};
```

### 3. 处理数值精度
如果需要精确的货币计算，考虑使用整数（分）而不是浮点数：

```protobuf
message Money {
    int64 cents = 1;  // 以分为单位存储
    string currency = 2;  // 货币类型
}
```

### 4. 版本兼容性
Protobuf 提供了良好的版本兼容性，遵循以下规则：

- 不要更改现有字段的编号
- 不要更改现有字段的类型
- 可以添加新的 optional 字段
- 可以删除字段（但不要重用其编号）

## Orleans 特殊考虑

Orleans Streaming 使用 `byte[]` 进行消息传输，因此：

1. 所有通过 Orleans Stream 传输的消息必须能够序列化为 `byte[]`
2. 使用 Protobuf 的 `ToByteArray()` 和 `Parser.ParseFrom()` 方法
3. 在 Grain 接口中避免直接传递复杂对象，使用 Protobuf 消息

```csharp
// Orleans Grain 接口
public interface IMyGrain : IGrainWithGuidKey
{
    Task ProcessMessage(byte[] messageData);  // 使用 byte[]
}

// 实现
public async Task ProcessMessage(byte[] messageData)
{
    var message = MyMessage.Parser.ParseFrom(messageData);
    // 处理消息
}
```

## 调试技巧

### 1. 验证序列化
```csharp
// 测试序列化/反序列化
var original = new MyState { ... };
var bytes = original.ToByteArray();
var deserialized = MyState.Parser.ParseFrom(bytes);
Assert.Equal(original, deserialized);
```

### 2. 检查生成的代码
生成的 Protobuf 代码位于 `obj/Debug/net9.0/` 目录下，文件名通常为 `[ProtoFileName].cs`。

### 3. 常见错误

- **CS0260: 缺少 partial 修饰符**：表示手动定义的类与 Protobuf 生成的类冲突
- **CS0102: 已包含定义**：表示字段重复定义
- **CS0111: 已定义成员**：表示方法重复定义

## 总结

使用 Protobuf 进行序列化是 Aevatar Agent Framework 的核心要求。这确保了：

1. ✅ 跨运行时兼容性
2. ✅ 高效的序列化性能
3. ✅ 版本兼容性
4. ✅ 类型安全
5. ✅ 与 Orleans Streaming 的无缝集成

始终记住：**如果数据需要跨边界传输，就使用 Protobuf 定义它**。
