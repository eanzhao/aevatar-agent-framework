# Aevatar Agent Framework 测试项目

此目录包含了 Aevatar Agent Framework 的测试项目，用于验证各个组件的功能正确性。

## 测试项目结构

- **Aevatar.Agents.Core.Tests**：测试核心组件和序列化功能
  - `SerializationTests.cs`：测试 Protobuf 序列化和反序列化功能
  - `GAgentBaseTests.cs`：测试代理基类功能

- **Aevatar.Agents.Local.Tests**：测试本地实现
  - `LocalMessageStreamTests.cs`：测试本地消息流
  - `LocalGAgentActorTests.cs`：测试本地代理Actor
  - `LocalGAgentFactoryTests.cs`：测试本地代理工厂

- **Aevatar.Agents.ProtoActor.Tests**：测试 Proto.Actor 实现
  - `ProtoActorMessageTests.cs`：测试消息包装器
  - `StreamActorTests.cs`：测试流Actor
  - `ProtoActorGAgentActorTests.cs`：测试Proto.Actor代理Actor

- **Aevatar.Agents.GAgents.Tests**：测试代理实现
  - `LlmGAgentTests.cs`：测试LLM代理
  - `CodingGAgentTests.cs`：测试代码验证代理

## 运行测试

可以使用以下命令运行所有测试：

```bash
dotnet test aevatar-agent-framework.sln
```

或者运行特定项目的测试：

```bash
dotnet test test/Aevatar.Agents.Core.Tests/Aevatar.Agents.Core.Tests.csproj
dotnet test test/Aevatar.Agents.Local.Tests/Aevatar.Agents.Local.Tests.csproj
dotnet test test/Aevatar.Agents.ProtoActor.Tests/Aevatar.Agents.ProtoActor.Tests.csproj
dotnet test test/Aevatar.Agents.GAgents.Tests/Aevatar.Agents.GAgents.Tests.csproj
```

## 测试结构说明

测试项目遵循以下结构：

1. **单元测试**：测试单个组件的功能，如序列化器、代理基类等
2. **集成测试**：测试组件间的交互，如消息发送和处理流程
3. **模拟测试**：使用Moq框架模拟依赖项，验证交互逻辑

## 添加新测试

添加新测试时，请遵循以下步骤：

1. 确定测试所属的模块（Core, Local, ProtoActor, GAgents）
2. 在相应项目中创建测试类，使用有意义的命名
3. 测试方法需要清晰描述测试意图，格式为：`方法名_条件_预期结果`
4. 使用AAA模式：Arrange（准备）、Act（执行）、Assert（断言）
5. 必要时使用Moq创建模拟对象

## 注意事项

- 确保测试不依赖外部服务（如数据库、网络服务等）
- 测试应该是可重复的，多次运行结果相同
- 测试应该是独立的，不依赖于其他测试的结果
- 测试应该包含适当的注释，解释复杂的测试逻辑
