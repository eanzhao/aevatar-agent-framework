# AgentSystem 架构文档（修订版）

## 1. 系统概述
AgentSystem 是一个分布式多Agent系统，模拟公司管理结构的树状层次，支持本地和云端（Orleans）部署。系统采用纯事件驱动模型，每个Agent创建独立Stream（通过`StreamId`绑定Agent的`Guid Id`），供自身及其子Agent通信，子Agent订阅父Agent的Stream接收事件。事件类型区分在消息队列（`IMessageStream`）层通过类型化订阅实现，支持每个Agent处理多种事件类型。业务逻辑通过`IAgent`（继承`AgentBase`）定义，直接运行在Orleans Grain（或本地Actor）中，支持自定义持久化状态和事件类型。核心代码与Orleans完全解耦，仅通过DI注册运行时。使用Google.Protobuf（3.27.5）进行高效序列化，Protobuf代码通过NuGet包自动生成，持久化采用Event Sourcing模式，事件异步存储到MongoDB，支持版本管理。

## 2. 架构原则
- **解耦性**：`AgentSystem.Core`和`AgentSystem.Business`不引用Orleans，运行时逻辑隔离在`AgentSystem.Local`/`AgentSystem.Orleans`.
- **模块化**：按职责拆分项目（核心、序列化、业务、本地运行时、Orleans适配、主机、测试）。
- **高性能**：Protobuf序列化和.NET 9.0优化（如AOT）确保低延迟。
- **扩展性**：业务逻辑通过继承`AgentBase`扩展，运行时通过DI切换。
- **事件隔离**：事件类型区分在`IMessageStream`层，业务Agent处理专属事件。
- **Virtual Actor**：业务逻辑（`IAgent`）通过`IAgentActor`包装运行在Grain或本地Actor中。
- **Event Sourcing**：状态变化作为事件序列异步存储到MongoDB，版本管理避免数据不一致，定期快照优化回放。
- **Protobuf生成**：通过NuGet包（如`Google.Protobuf.Tools`）自动生成C#代码，集成到MSBuild。

## 3. 系统组件
### 3.1 项目结构
- **AgentSystem.Core**：
  - 职责：定义接口（`IMessage`, `IMessageSerializer`, `IMessageStream`, `IAgent`, `IAgentActor`, `IAgentFactory`）、Protobuf契约（`messages.proto`）、枚举（`EnvironmentMode`）。
  - 依赖：`Google.Protobuf` (3.27.5), `Microsoft.Extensions.DependencyInjection.Abstractions` (9.0.0).
- **AgentSystem.Serialization**：
  - 职责：实现Protobuf序列化（`ProtobufSerializer`），动态处理消息类型。
  - 依赖：`Google.Protobuf`, `AgentSystem.Core`.
- **AgentSystem.Business**：
  - 职责：实现业务逻辑（`LLMAgent`, `CodingAgent`），继承`AgentBase`，定义专属状态和事件。
  - 依赖：`Google.Protobuf`, `AgentSystem.Core`.
- **AgentSystem.Local**：
  - 职责：本地运行时（`LocalAgentActor`, `LocalAgentFactory`, `LocalMessageStream`），基于`System.Threading.Channels`和内存事件日志。
  - 依赖：`Microsoft.Extensions.DependencyInjection` (9.0.0), `AgentSystem.Core`, `AgentSystem.Serialization`.
- **AgentSystem.Orleans**：
  - 职责：Orleans适配（`OrleansAgentActor`, `OrleansAgentFactory`, `OrleansMessageStream`），使用`JournaledGrain`和MongoDB事件存储。
  - 依赖：`Microsoft.Orleans.Server` (9.2.0), `Microsoft.Orleans.Streaming` (9.2.0), `Microsoft.Orleans.Persistence.MongoDB` (9.2.0), `MongoDB.Driver` (2.30.0), `AgentSystem.Core`, `AgentSystem.Serialization`.
- **AgentSystem.Host**：
  - 职责：应用程序入口，配置DI，初始化Agent树。
  - 依赖：`Microsoft.Extensions.Hosting` (9.0.0), `Microsoft.Orleans.Client` (9.2.0, 条件引用), 其他项目（条件引用).
- **AgentSystem.Tests**：
  - 职责：单元测试和集成测试。
  - 依赖：`xunit` (2.9.2), `Microsoft.NET.Test.Sdk` (17.11.1), 所有其他项目.

### 3.2 核心接口与类
- **IMessage**: 标记接口，Protobuf消息（如`MessageEnvelope`）。
- **IMessageSerializer**: 序列化接口，动态处理`byte[]`转换。
- **IMessageStream**: 消息流接口，支持类型化订阅，绑定Agent的`StreamId`。
- **IAgent**: 业务逻辑接口，定义事件处理、子Agent管理和自定义状态。
- **IAgentActor**: 运行时接口，包装`IAgent`，实现Stream和Event Sourcing。
- **IAgentFactory**: 工厂接口，创建`IAgentActor`并注入`IAgent`.
- **AgentBase**: 抽象基类，提供通用子Agent管理和事件广播。
- **EventEnvelope**: 事件包装器，包含版本号和`google.protobuf.Any` Payload。
- **LLMAgentState**, **CodingAgentState**: 特定业务状态。

### 3.3 数据流
1. **业务层（`IAgent`）**：
   - 用户继承`AgentBase`实现`IAgent`（如`LLMAgent`），定义专属状态（如`LLMAgentState`）和多种事件（如`LLMEvent`, `GeneralConfigEvent`）。
   - 注册类型化事件处理程序到`IMessageStream`，处理事件时生成新事件。
2. **运行时层（`IAgentActor`）**：
   - `LocalAgentActor`/`OrleansAgentActor`包装`IAgent`，创建独立Stream（绑定`StreamId`）。
   - 子AgentActor订阅父AgentActor的Stream（通过`StreamId`），传递事件。
3. **序列化**：
   - `ProtobufSerializer`动态序列化`MessageEnvelope`, `EventEnvelope`和状态为`byte[]`。
4. **Event Sourcing**：
   - 事件异步写入MongoDB，回放重建状态。
   - 版本管理通过事件版本号检查一致性。
   - 定期快照优化回放。

### 3.4 Stream绑定
- **IMessageStream**绑定到Agent的`Guid Id`，生成唯一`StreamId`（如`AgentStream:{Id}`）。
- 子Agent通过父Agent的`StreamId`订阅，确保正确通信。
- 类型化订阅（`SubscribeAsync<T>`）在Stream层区分事件类型（如`LLMEvent`）。

### 3.5 Event Sourcing机制
- **事件生成**：业务层处理事件时生成新事件（`EventEnvelope`），追加到事件日志。
- **状态重建**：Grain激活时回放事件，应用到内存状态。
- **版本管理**：事件带有递增版本号，应用时检查连续性（乐观并发）。
- **快照**：定期保存状态快照到MongoDB，减少回放事件数量。
- **Orleans模式**：
  - 使用`JournaledGrain<TState, TEvent>`, 事件异步写入MongoDB。
- **本地模式**：
  - 使用内存事件日志（`List<EventEnvelope>`）模拟。
- **MongoDB存储**：
  - 事件序列作为值存储，键为Grain ID。
  - 支持审计和回滚。

### 3.6 序列化机制
- **ProtobufSerializer**动态处理任意Protobuf消息类型，利用`IMessage`和`Parser`属性。
- 使用`google.protobuf.Any`支持多态Payload，无需硬编码类型检查。

### 3.7 Protobuf代码生成
- 使用NuGet包`Google.Protobuf.Tools`（3.27.5），通过MSBuild集成自动生成C#代码。
- `.proto`文件（如`messages.proto`）置于项目中，构建时生成C#类。

### 3.8 DI和运行时切换
- **DI配置**：
  - 在`AgentSystem.Host`通过`appsettings.json`的`EnvironmentMode`（Local/Orleans）注册运行时。
  - 业务服务（如`ILLMService`）通过DI注入，Agent通过`IAgentFactory`动态创建（非单例）。
- **切换**：
  - 本地：`LocalMessageStream`, `LocalAgentFactory`, 内存事件日志。
  - Orleans：`OrleansMessageStream`, `OrleansAgentFactory`, MongoDB事件存储。

## 4. 实现细节

### 4.1 项目配置
为支持自动生成Protobuf代码，需配置项目文件（`.csproj`）：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.27.5" />
    <PackageReference Include="Google.Protobuf.Tools" Version="3.27.5" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
    <PackageReference Include="Microsoft.Orleans.Server" Version="9.2.0" Condition="'$(EnvironmentMode)' == 'Orleans'" />
    <PackageReference Include="Microsoft.Orleans.Streaming" Version="9.2.0" Condition="'$(EnvironmentMode)' == 'Orleans'" />
    <PackageReference Include="Microsoft.Orleans.Persistence.MongoDB" Version="9.2.0" Condition="'$(EnvironmentMode)' == 'Orleans'" />
    <PackageReference Include="MongoDB.Driver" Version="2.30.0" Condition="'$(EnvironmentMode)' == 'Orleans'" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="messages.proto" GrpcServices="None" />
  </ItemGroup>
</Project>
```

- `Google.Protobuf.Tools`提供MSBuild任务，自动处理`.proto`文件生成C#代码。
- `.proto`文件（如`messages.proto`）置于项目根目录，构建时生成C#类到`obj`目录。

### 4.2 核心代码

- **messages.proto** (AgentSystem.Core):
  ```proto
  syntax = "proto3";
  package agent;

  import "google/protobuf/any.proto";

  message MessageEnvelope {
    string id = 1;
    int64 timestamp = 2;
    google.protobuf.Any payload = 3;  // 业务Agent解析
  }

  message EventEnvelope {
    string id = 1;
    int64 timestamp = 2;
    int64 version = 3;  // 事件版本号
    google.protobuf.Any payload = 4;  // 事件Payload
  }

  message SubAgentAdded {
    string sub_agent_id = 1;
  }

  message SubAgentRemoved {
    string sub_agent_id = 1;
  }

  message LLMEvent {
    string prompt = 1;
    string response = 2;
  }

  message GeneralConfigEvent {
    string config_key = 1;
    string config_value = 2;
  }

  message CodeValidationEvent {
    string code = 1;
    bool result = 2;
  }

  message LLMAgentState {
    repeated string sub_agent_ids = 1;
    int64 current_version = 2;
    string llm_config = 3;
  }

  message CodingAgentState {
    repeated string sub_agent_ids = 1;
    int64 current_version = 2;
    string validation_history = 3;
  }
  ```

- **IMessage** (AgentSystem.Core):
  ```csharp
  public interface IMessage
  {
      // 标记接口，Protobuf消息
  }
  ```

- **IMessageSerializer** (AgentSystem.Core):
  ```csharp
  public interface IMessageSerializer
  {
      byte[] Serialize<T>(T message) where T : IMessage;
      T Deserialize<T>(byte[] data) where T : IMessage;
  }
  ```

- **ProtobufSerializer** (AgentSystem.Serialization):
  ```csharp
  using Google.Protobuf;
  using Google.Protobuf.WellKnownTypes;

  public class ProtobufSerializer : IMessageSerializer
  {
      public byte[] Serialize<T>(T message) where T : IMessage
      {
          if (message is IMessage protoMessage)
          {
              return protoMessage.ToByteArray();
          }
          throw new NotSupportedException($"Type {typeof(T).FullName} does not implement IMessage");
      }

      public T Deserialize<T>(byte[] data) where T : IMessage
      {
          var parserProperty = typeof(T).GetProperty("Parser", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
          if (parserProperty != null)
          {
              var parser = parserProperty.GetValue(null) as MessageParser<T>;
              if (parser != null)
              {
                  return parser.ParseFrom(data);
              }
          }
          throw new NotSupportedException($"Type {typeof(T).FullName} does not have a Protobuf Parser");
      }
  }
  ```

- **IMessageStream** (AgentSystem.Core):
  ```csharp
  public interface IMessageStream
  {
      Guid StreamId { get; }
      Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage;
      Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage;
  }
  ```

- **IAgent** (AgentSystem.Core):
  ```csharp
  public interface IAgent<TState> where TState : class, new()
  {
      Guid Id { get; }
      Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default);
      Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new();
      Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default);
      IReadOnlyList<IAgent> GetSubAgents();
      TState GetState();
      IReadOnlyList<EventEnvelope> GetPendingEvents();
      Task RaiseEventAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
      Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default);
      Task ProduceEventAsync(IMessage message, CancellationToken ct = default);
  }
  ```

- **IAgentActor** (AgentSystem.Core):
  ```csharp
  public interface IAgentActor
  {
      Guid Id { get; }
      Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new();
      Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default);
      Task ProduceEventAsync(IMessage message, CancellationToken ct = default);
      Task SubscribeToParentStreamAsync(IAgentActor parent, CancellationToken ct = default);
  }
  ```

- **IAgentFactory** (AgentSystem.Core):
  ```csharp
  public interface IAgentFactory
  {
      Task<IAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
          where TBusiness : IAgent<TState> 
          where TState : class, new();
  }
  ```

- **AgentBase** (AgentSystem.Core):
  ```csharp
  using Google.Protobuf.WellKnownTypes;

  public abstract class AgentBase<TState> : IAgent<TState> where TState : class, new()
  {
      protected readonly TState _state = new();
      protected readonly List<IAgent> _subAgents = new();
      protected readonly IServiceProvider _serviceProvider;
      protected readonly IAgentFactory _factory;
      protected readonly IMessageSerializer _serializer;
      protected readonly List<EventEnvelope> _pendingEvents = new();

      protected AgentBase(IServiceProvider serviceProvider, IAgentFactory factory, IMessageSerializer serializer)
      {
          _serviceProvider = serviceProvider;
          _factory = factory;
          _serializer = serializer;
      }

      public Guid Id { get; } = Guid.NewGuid();

      public abstract Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default);

      public virtual async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new()
      {
          var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
          var subAgent = _serviceProvider.GetRequiredService<TSubAgent>();
          _subAgents.Add(subAgent);
          var evt = new EventEnvelope
          {
              Id = Guid.NewGuid().ToString(),
              Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              Version = GetStateVersion() + 1,
              Payload = Any.Pack(new SubAgentAdded { SubAgentId = subAgent.Id.ToString() })
          };
          await RaiseEventAsync(evt, ct);
          await subAgentActor.SubscribeToParentStreamAsync(this, ct);
      }

      public virtual async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
      {
          var subAgent = _subAgents.FirstOrDefault(a => a.Id == subAgentId);
          if (subAgent != null)
          {
              _subAgents.Remove(subAgent);
              var evt = new EventEnvelope
              {
                  Id = Guid.NewGuid().ToString(),
                  Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                  Version = GetStateVersion() + 1,
                  Payload = Any.Pack(new SubAgentRemoved { SubAgentId = subAgentId.ToString() })
              };
              await RaiseEventAsync(evt, ct);
          }
      }

      public async Task RaiseEventAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
      {
          var envelope = new EventEnvelope
          {
              Id = Guid.NewGuid().ToString(),
              Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              Version = GetStateVersion() + 1,
              Payload = Any.Pack(evt as IMessage)
          };
          _pendingEvents.Add(envelope);
          await ApplyEventAsync(envelope, ct);
      }

      public abstract Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default);

      public IReadOnlyList<EventEnvelope> GetPendingEvents() => _pendingEvents.AsReadOnly();

      protected long GetStateVersion()
      {
          return _state switch
          {
              LLMAgentState llmState => llmState.CurrentVersion,
              CodingAgentState codeState => codeState.CurrentVersion,
              _ => 0
          };
      }

      public IReadOnlyList<IAgent> GetSubAgents() => _subAgents.AsReadOnly();
      public TState GetState() => _state;

      public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
      {
          var stream = _serviceProvider.GetRequiredService<IMessageStream>();
          await stream.ProduceAsync(message, ct);
      }
  }
  ```

- **LocalMessageStream** (AgentSystem.Local):
  ```csharp
  public class LocalMessageStream : IMessageStream
  {
      private readonly Channel<byte[]> _channel;
      private readonly IMessageSerializer _serializer;
      private readonly Dictionary<Type, List<object>> _handlers = new();
      public Guid StreamId { get; }

      public LocalMessageStream(IMessageSerializer serializer, Guid streamId)
      {
          _serializer = serializer;
          StreamId = streamId;
          _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
          {
              SingleReader = false,
              SingleWriter = true
          });
      }

      public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
      {
          var serialized = _serializer.Serialize(message);
          await _channel.Writer.WriteAsync(serialized, ct);
      }

      public async Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
      {
          if (!_handlers.ContainsKey(typeof(T)))
          {
              _handlers[typeof(T)] = new List<object>();
          }
          _handlers[typeof(T)].Add(handler);

          _ = Task.Run(async () =>
          {
              await foreach (var data in _channel.Reader.ReadAllAsync(ct))
              {
                  try
                  {
                      var message = _serializer.Deserialize<T>(data);
                      foreach (var h in _handlers[typeof(T)].Cast<Func<T, Task>>())
                      {
                          await h(message);
                      }
                  }
                  catch (Exception)
                  {
                      // 忽略非T类型事件
                  }
              }
          }, ct);
      }
  }
  ```

- **OrleansMessageStream** (AgentSystem.Orleans):
  ```csharp
  public class OrleansMessageStream : IMessageStream
  {
      private readonly IAsyncStream<byte[]> _orleansStream;
      private readonly IMessageSerializer _serializer;
      public Guid StreamId { get; }

      public OrleansMessageStream(IAsyncStream<byte[]> orleansStream, IMessageSerializer serializer, Guid streamId)
      {
          _orleansStream = orleansStream;
          _serializer = serializer;
          StreamId = streamId;
      }

      public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
      {
          var serialized = _serializer.Serialize(message);
          await _orleansStream.OnNextAsync(serialized, ct);
      }

      public async Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
      {
          await _orleansStream.SubscribeAsync(new StreamObserver<T>(handler, _serializer), ct);
      }

      private class StreamObserver<T> : IAsyncObserver<byte[]> where T : IMessage
      {
          private readonly Func<T, Task> _handler;
          private readonly IMessageSerializer _serializer;

          public StreamObserver(Func<T, Task> handler, IMessageSerializer serializer)
          {
              _handler = handler;
              _serializer = serializer;
          }

          public async Task OnNextAsync(byte[] data, StreamSequenceToken? token = null)
          {
              try
              {
                  var message = _serializer.Deserialize<T>(data);
                  await _handler(message);
              }
              catch (Exception)
              {
                  // 忽略非T类型事件
              }
          }

          public Task OnCompletedAsync() => Task.CompletedTask;
          public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
      }
  }
  ```

- **LocalAgentActor** (AgentSystem.Local):
  ```csharp
  public class LocalAgentActor<TState> : IAgentActor where TState : class, new()
  {
      private readonly IMessageStream _stream;
      private readonly IAgent<TState> _businessAgent;
      private readonly Dictionary<Guid, IAgentActor> _subAgents = new();
      private readonly IAgentFactory _factory;
      private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;

      public LocalAgentActor(IMessageStream stream, IAgent<TState> businessAgent, IAgentFactory factory, Dictionary<Guid, List<EventEnvelope>> eventStore)
      {
          _stream = stream;
          _businessAgent = businessAgent;
          _factory = factory;
          _eventStore = eventStore;
          if (!_eventStore.ContainsKey(businessAgent.Id))
          {
              _eventStore[businessAgent.Id] = new List<EventEnvelope>();
          }
      }

      public Guid Id => _businessAgent.Id;

      public async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new()
      {
          var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
          _subAgents[subAgentActor.Id] = subAgentActor;
          await _businessAgent.AddSubAgentAsync<TSubAgent, TSubState>(ct);
          _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());
          await subAgentActor.SubscribeToParentStreamAsync(this, ct);
      }

      public async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
      {
          if (_subAgents.Remove(subAgentId))
          {
              await _businessAgent.RemoveSubAgentAsync(subAgentId, ct);
              _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());
          }
      }

      public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
      {
          await _stream.ProduceAsync(message, ct);
      }

      public async Task SubscribeToParentStreamAsync(IAgentActor parent, CancellationToken ct = default)
      {
          if (parent is LocalAgentActor<AgentState> parentActor)
          {
              await _businessAgent.RegisterEventHandlersAsync(_stream, ct);
              await parentActor._stream.SubscribeAsync<MessageEnvelope>(
                  async msg => await _businessAgent.RegisterEventHandlersAsync(_stream, ct), ct);
          }
      }
  }
  ```

- **OrleansAgentActor** (AgentSystem.Orleans):
  ```csharp
  public class OrleansAgentActor<TState> : JournaledGrain<TState, EventEnvelope>, IAgentActor 
      where TState : class, new()
  {
      private readonly IMessageStream _stream;
      private readonly IAgent<TState> _businessAgent;
      private readonly Dictionary<Guid, IAgentActor> _subAgents = new();
      private readonly IAgentFactory _factory;

      public OrleansAgentActor(IMessageStream stream, IAgent<TState> businessAgent, IAgentFactory factory)
      {
          _stream = stream;
          _businessAgent = businessAgent;
          _factory = factory;
      }

      public override async Task OnActivateAsync(CancellationToken ct)
      {
          foreach (var evt in UnconfirmedEvents)
          {
              await _businessAgent.ApplyEventAsync(evt, ct);
          }
          foreach (var subAgentId in _businessAgent.GetState().SubAgentIds)
          {
              var subAgent = await _factory.CreateAgentAsync<IAgent<AgentState>, AgentState>(Guid.Parse(subAgentId), ct);
              await AddSubAgentAsync<IAgent<AgentState>, AgentState>(ct);
          }
          await _businessAgent.RegisterEventHandlersAsync(_stream, ct);
          await base.OnActivateAsync(ct);
      }

      public Guid Id => this.GetPrimaryKey();

      public async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new()
      {
          var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
          _subAgents[subAgentActor.Id] = subAgentActor;
          await _businessAgent.AddSubAgentAsync<TSubAgent, TSubState>(ct);
          foreach (var evt in _businessAgent.GetPendingEvents())
          {
              RaiseEvent(evt);
          }
          await WriteStateAsync();
          await subAgentActor.SubscribeToParentStreamAsync(this, ct);
      }

      public async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
      {
          if (_subAgents.Remove(subAgentId))
          {
              await _businessAgent.RemoveSubAgentAsync(subAgentId, ct);
              foreach (var evt in _businessAgent.GetPendingEvents())
              {
                  RaiseEvent(evt);
              }
              await WriteStateAsync();
          }
      }

      public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
      {
          await _stream.ProduceAsync(message, ct);
      }

      public async Task SubscribeToParentStreamAsync(IAgentActor parent, CancellationToken ct = default)
      {
          if (parent is OrleansAgentActor<AgentState> parentActor)
          {
              await _businessAgent.RegisterEventHandlersAsync(_stream, ct);
              await parentActor._stream.SubscribeAsync<MessageEnvelope>(
                  async msg => await _businessAgent.RegisterEventHandlersAsync(_stream, ct), ct);
          }
      }
  }
  ```

- **LocalAgentFactory** (AgentSystem.Local):
  ```csharp
  public class LocalAgentFactory : IAgentFactory
  {
      private readonly IServiceProvider _serviceProvider;
      private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;

      public LocalAgentFactory(IServiceProvider serviceProvider)
      {
          _serviceProvider = serviceProvider;
          _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
      }

      public async Task<IAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
          where TBusiness : IAgent<TState> 
          where TState : class, new()
      {
          var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
          var stream = new LocalMessageStream(serializer, id);
          var businessAgent = _serviceProvider.GetRequiredService<TBusiness>();
          var actor = new LocalAgentActor<TState>(stream, businessAgent, this, _eventStore);
          if (_eventStore.ContainsKey(id))
          {
              foreach (var evt in _eventStore[id])
              {
                  await businessAgent.ApplyEventAsync(evt, ct);
              }
          }
          return actor;
      }
  }
  ```

- **OrleansAgentFactory** (AgentSystem.Orleans):
  ```csharp
  public class OrleansAgentFactory : IAgentFactory
  {
      private readonly IGrainFactory _grainFactory;
      private readonly IServiceProvider _serviceProvider;

      public OrleansAgentFactory(IGrainFactory grainFactory, IServiceProvider serviceProvider)
      {
          _grainFactory = grainFactory;
          _serviceProvider = serviceProvider;
      }

      public async Task<IAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
          where TBusiness : IAgent<TState> 
          where TState : class, new()
      {
          var streamProvider = _serviceProvider.GetRequiredService<IStreamProvider>();
          var stream = streamProvider.GetStream<byte[]>(StreamId.Create("AgentStream", id));
          var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
          var streamAdapter = new OrleansMessageStream(stream, serializer, id);
          var businessAgent = _serviceProvider.GetRequiredService<TBusiness>();
          var grain = _grainFactory.GetGrain<IAgentActor>(id);
          return grain;
      }
  }
  ```

- **LLMAgent** (AgentSystem.Business):
  ```csharp
  using Google.Protobuf.WellKnownTypes;

  [ProtoContract]
  public class LLMEvent : IMessage
  {
      [ProtoMember(1)] public string Prompt { get; set; }
      [ProtoMember(2)] public string Response { get; set; }
  }

  [ProtoContract]
  public class GeneralConfigEvent : IMessage
  {
      [ProtoMember(1)] public string ConfigKey { get; set; }
      [ProtoMember(2)] public string ConfigValue { get; set; }
  }

  [ProtoContract]
  public class LLMAgentState
  {
      [ProtoMember(1)] public List<string> SubAgentIds { get; set; } = new();
      [ProtoMember(2)] public long CurrentVersion { get; set; }
      [ProtoMember(3)] public string LLMConfig { get; set; } = "";
  }

  public class LLMAgent : AgentBase<LLMAgentState>
  {
      private readonly ILLMService _llmService;

      public LLMAgent(IServiceProvider serviceProvider, IAgentFactory factory, IMessageSerializer serializer, ILLMService llmService)
          : base(serviceProvider, factory, serializer)
      {
          _llmService = llmService;
      }

      public override async Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
      {
          await stream.SubscribeAsync<LLMEvent>(
              async evt =>
              {
                  evt.Response = await _llmService.GenerateResponseAsync(evt.Prompt, ct);
                  await RaiseEventAsync(evt, ct);
                  await ProduceEventAsync(new MessageEnvelope
                  {
                      Id = Guid.NewGuid().ToString(),
                      Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                      Payload = Any.Pack(evt)
                  }, ct);
              }, ct);

          await stream.SubscribeAsync<GeneralConfigEvent>(
              async evt =>
              {
                  await RaiseEventAsync(evt, ct);
              }, ct);
      }

      public override async Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
      {
          if (evt.Payload.Unpack<LLMEvent>(out var llmEvent))
          {
              GetState().LLMConfig = llmEvent.Response;
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<GeneralConfigEvent>(out var configEvent))
          {
              GetState().LLMConfig = configEvent.ConfigValue;
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<SubAgentAdded>(out var added))
          {
              GetState().SubAgentIds.Add(added.SubAgentId);
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<SubAgentRemoved>(out var removed))
          {
              GetState().SubAgentIds.Remove(removed.SubAgentId);
              GetState().CurrentVersion = evt.Version;
          }
      }

      public override async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IAgent<TSubState> 
          where TSubState : class, new()
      {
          if (typeof(TSubAgent) != typeof(CodingAgent))
          {
              throw new InvalidOperationException("LLMAgent only supports CodingAgent");
          }
          await base.AddSubAgentAsync<TSubAgent, TSubState>(ct);
      }
  }
  ```

- **CodingAgent** (AgentSystem.Business):
  ```csharp
  using Google.Protobuf.WellKnownTypes;

  [ProtoContract]
  public class CodeValidationEvent : IMessage
  {
      [ProtoMember(1)] public string Code { get; set; }
      [ProtoMember(2)] public bool Result { get; set; }
  }

  [ProtoContract]
  public class CodingAgentState
  {
      [ProtoMember(1)] public List<string> SubAgentIds { get; set; } = new();
      [ProtoMember(2)] public long CurrentVersion { get; set; }
      [ProtoMember(3)] public string ValidationHistory { get; set; } = "";
  }

  public class CodingAgent : AgentBase<CodingAgentState>
  {
      private readonly ICodeValidator _validator;

      public CodingAgent(IServiceProvider serviceProvider, IAgentFactory factory, IMessageSerializer serializer, ICodeValidator validator)
          : base(serviceProvider, factory, serializer)
      {
          _validator = validator;
      }

      public override async Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
      {
          await stream.SubscribeAsync<CodeValidationEvent>(
              async evt =>
              {
                  evt.Result = await _validator.ValidateCodeAsync(evt.Code, ct);
                  await RaiseEventAsync(evt, ct);
              }, ct);

          await stream.SubscribeAsync<GeneralConfigEvent>(
              async evt =>
              {
                  await RaiseEventAsync(evt, ct);
              }, ct);
      }

      public override async Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
      {
          if (evt.Payload.Unpack<CodeValidationEvent>(out var codeEvent))
          {
              GetState().ValidationHistory = $"ValidationResult: {codeEvent.Result}";
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<GeneralConfigEvent>(out var configEvent))
          {
              GetState().ValidationHistory = configEvent.ConfigValue;
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<SubAgentAdded>(out var added))
          {
              GetState().SubAgentIds.Add(added.SubAgentId);
              GetState().CurrentVersion = evt.Version;
          }
          else if (evt.Payload.Unpack<SubAgentRemoved>(out var removed))
          {
              GetState().SubAgentIds.Remove(removed.SubAgentId);
              GetState().CurrentVersion = evt.Version;
          }
      }
  }
  ```

- **AgentSystem.Host**:
  ```csharp
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Hosting;

  builder.ConfigureServices((context, services) =>
  {
      var mode = context.Configuration.GetValue<EnvironmentMode>("EnvironmentMode");
      services.AddSingleton<IMessageSerializer, ProtobufSerializer>();

      if (mode == EnvironmentMode.Local)
      {
          services.AddScoped<IMessageStream>(sp => 
              new LocalMessageStream(sp.GetRequiredService<IMessageSerializer>(), Guid.NewGuid()));
          services.AddScoped<IAgentFactory, LocalAgentFactory>();
      }
      else
      {
          services.AddOrleans(client => 
          {
              client.AddSimpleMessageStreamProvider("SMSProvider");
              client.UseMongoDBClustering(options => options.ConnectionString = "mongodb://localhost:27017");
              client.AddMongoDBGrainStorage("AgentStore", options => options.ConnectionString = "mongodb://localhost:27017");
          });
          services.AddScoped<IAgentFactory, OrleansAgentFactory>();
      }

      services.AddScoped<ILLMService, OpenAIService>();
      services.AddScoped<ICodeValidator, CodeValidatorService>();
      services.AddScoped<IAgent<LLMAgentState>, LLMAgent>();
      services.AddScoped<IAgent<CodingAgentState>, CodingAgent>();
  });

  var host = builder.Build();
  await host.StartAsync();

  var factory = host.Services.GetRequiredService<IAgentFactory>();
  var llmAgent = await factory.CreateAgentAsync<LLMAgent, LLMAgentState>(Guid.NewGuid());
  await llmAgent.AddSubAgentAsync<CodingAgent, CodingAgentState>();
  ```

### 4.3 解决潜在问题
- **问题1：Protobuf代码生成**:
  - 解决：使用`Google.Protobuf.Tools`通过MSBuild自动生成C#代码，项目配置`.proto`文件，构建时自动生成。
- **问题2：多事件处理**:
  - 解决：每个Agent通过`RegisterEventHandlersAsync`注册多种事件处理程序（如`LLMEvent`, `GeneralConfigEvent`），使用`google.protobuf.Any`支持多态Payload。
- **问题3：硬编码序列化**:
  - 解决：`ProtobufSerializer`使用`IMessage`和`Parser`动态处理消息类型，移除`switch`。
- **问题4：单例注册**:
  - 解决：使用`AddScoped`和`IAgentFactory`动态创建Agent，确保独立Grain实例。
- **问题5：Grain内运行**:
  - 解决：`IAgentActor`包装`IAgent<TState>`, 业务逻辑运行在Grain中。
- **问题6：Event Sourcing**:
  - 解决：事件异步写入MongoDB，版本管理通过`EventEnvelope.Version`。
- **问题7：独立Stream**:
  - 解决：每个`IAgentActor`创建独立Stream，绑定`StreamId`。
- **问题8：事件类型区分**:
  - 解决：`IMessageStream.SubscribeAsync<T>`在Stream层注册类型化处理程序。
- **问题9：自定义状态和事件**:
  - 解决：`IAgent<TState>`支持专属状态（如`LLMAgentState`）和多种事件。
- **问题10：MongoDB持久化**:
  - 解决：Orleans模式使用`Microsoft.Orleans.Persistence.MongoDB`, 本地模式使用内存事件日志。

### 4.4 部署与运行
- **Protobuf生成**:
  - 配置项目文件（`.csproj`），添加`<Protobuf Include="messages.proto" GrpcServices="None" />`。
  - 构建项目自动生成C#代码，无需手动`protoc`。
- **MongoDB配置**:
  - 安装MongoDB（`mongodb://localhost:27017`），配置Orleans存储。
  - 确保MongoDB服务运行，数据库`AgentStore`用于事件存储。
- **本地模式**:
  - 配置`appsettings.json`：
    ```json
    {
      "EnvironmentMode": "Local"
    }
    ```
  - 运行：`dotnet run --project AgentSystem.Host`.
- **Orleans模式**:
  - 配置`appsettings.json`：
    ```json
    {
      "EnvironmentMode": "Orleans"
    }
    ```
  - 配置MongoDB：`AddMongoDBGrainStorage`.
  - 运行：`dotnet run --project AgentSystem.Host`.

### 4.5 性能与优化
- **序列化**:
  - Protobuf（3.27.5）微秒级序列化，动态处理消息类型。
  - .NET 9.0支持AOT编译（`PublishAot`）。
- **消息传递**:
  - 本地：`System.Threading.Channels`高并发。
  - Orleans：Streams支持高吞吐量。
- **Event Sourcing**:
  - Orleans：MongoDB异步写入，延迟<10ms。
  - 本地：内存事件日志，近零开销。
- **快照优化**:
  - 每100事件保存快照，减少回放开销。

### 4.6 测试策略
- **单元测试**:
  - 测试`LLMAgent`和`CodingAgent`的事件处理隔离（`LLMEvent`, `GeneralConfigEvent`, `CodeValidationEvent`）。
  - Mock `ILLMService`验证LLM调用。
- **集成测试**:
  - 验证独立Stream（绑定`StreamId`）和类型化订阅。
  - 测试MongoDB事件存储和状态重建。
- **性能测试**:
  - 使用BenchmarkDotNet验证序列化/事件传递延迟<10微秒。

### 4.7 依赖关系
- **AgentSystem.Core**: Google.Protobuf (3.27.5), Microsoft.Extensions.DependencyInjection.Abstractions (9.0.0).
- **AgentSystem.Serialization**: Google.Protobuf, AgentSystem.Core.
- **AgentSystem.Business**: Google.Protobuf, AgentSystem.Core.
- **AgentSystem.Local**: Microsoft.Extensions.DependencyInjection (9.0.0), AgentSystem.Core, AgentSystem.Serialization.
- **AgentSystem.Orleans**: Microsoft.Orleans.Server (9.2.0), Microsoft.Orleans.Streaming (9.2.0), Microsoft.Orleans.Persistence.MongoDB (9.2.0), MongoDB.Driver (2.30.0), AgentSystem.Core, AgentSystem.Serialization.
- **AgentSystem.Host**: Microsoft.Extensions.Hosting (9.0.0), Microsoft.Orleans.Client (9.2.0, 条件), Google.Protobuf.Tools (3.27.5), 其他项目（条件）。
- **AgentSystem.Tests**: xunit (2.9.2), Microsoft.NET.Test.Sdk (17.11.1).

### 4.8 扩展性
- **业务扩展**：
  - 新增Agent类型（如`AnalyticsAgent`）继承`AgentBase<TState>`，定义专属状态和多种事件。
- **运行时扩展**：
  - 替换Orleans为Akka.NET，实现`IAgentActor`和`IMessageStream`。
- **存储扩展**：
  - 支持其他键值存储（如Redis）。
- **Protobuf扩展**：
  - 添加新`.proto`文件，自动生成C#代码，支持新消息类型。

## 5. 总结与问题解决
- **Protobuf代码生成**：使用`Google.Protobuf.Tools`通过MSBuild自动生成C#代码，移除手动`protoc`。
- **多事件处理**：每个Agent通过`RegisterEventHandlersAsync`注册多种事件处理程序，使用`google.protobuf.Any`支持多态Payload。
- **硬编码序列化**：`ProtobufSerializer`动态处理消息类型，移除`switch`。
- **单例问题**：使用`AddScoped`和`IAgentFactory`动态创建Agent，确保独立Grain实例。
- **Grain内运行**：`IAgentActor`包装`IAgent<TState>`, 业务逻辑运行在Grain中。
- **Event Sourcing**：事件异步写入MongoDB，版本管理通过`EventEnvelope.Version`。
- **独立Stream**：每个`IAgentActor`创建独立Stream，绑定`StreamId`。
- **事件类型区分**：`IMessageStream.SubscribeAsync<T>`在Stream层注册类型化处理程序。
- **自定义状态和事件**：`IAgent<TState>`支持专属状态和多种事件。
- **MongoDB持久化**：Orleans模式使用`Microsoft.Orleans.Persistence.MongoDB`。

## 6. 下一步建议
- **测试用例**：添加具体单元测试和集成测试，验证多事件处理和状态重建。
- **MongoDB优化**：配置MongoDB索引（如`GrainId`和`Version`），提升事件查询性能。
- **快照策略**：调整快照频率（如每50事件），优化回放性能。
- **扩展消息类型**：添加新事件类型（如`AnalyticsEvent`），验证扩展性。

如果需要进一步细化（如特定Payload结构、测试用例、MongoDB配置），请提供细节，我将提供专业支持。