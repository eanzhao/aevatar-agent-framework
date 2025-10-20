# Protobuf 代码自动生成配置指南

## 概述

本项目使用 `Grpc.Tools` 包来自动从 `.proto` 文件生成 C# 代码。这个过程在每次构建时自动完成，无需手动运行 `protoc` 编译器。

## 配置方式

### 1. 项目文件配置 (.csproj)

在 `Aevatar.Agents.Abstractions.csproj` 中的正确配置：

```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.33.0" />
  <PackageReference Include="Grpc.Tools" Version="2.71.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
    <Protobuf Include="messages.proto" GrpcServices="None" />
</ItemGroup>
```

### 2. 关键配置说明

#### Grpc.Tools 包
- **版本**: 2.71.0 (最新稳定版)
- **PrivateAssets**: 设置为 `all`，确保工具只在构建时使用，不会传播到依赖项
- **IncludeAssets**: 指定包含的资产类型

#### Protobuf 条目
- **Include**: 指定 `.proto` 文件路径
- **GrpcServices**: 设置为 `None`，因为我们只生成 Protobuf 消息，不生成 gRPC 服务代码

### 3. 生成的代码位置

自动生成的 C# 代码会放在：
```
obj/Debug/net9.0/Messages.cs
```

**重要**: 不要将生成的 `Messages.cs` 文件放在源代码根目录中！MSBuild 会自动包含 `obj/` 目录下的生成文件。

## 常见问题

### Q1: 为什么使用 Grpc.Tools 而不是 Google.Protobuf.Tools？

**A**: `Grpc.Tools` 是更现代的选择，它：
- 包含最新版本的 `protoc` 编译器
- 与 MSBuild 深度集成
- 支持跨平台（Windows, macOS, Linux）
- 自动处理依赖关系
- 更活跃的维护和更新

### Q2: 如何验证代码是否正确生成？

**A**: 运行以下命令：
```bash
dotnet clean src/Aevatar.Agents.Abstractions/Aevatar.Agents.Abstractions.csproj
dotnet build src/Aevatar.Agents.Abstractions/Aevatar.Agents.Abstractions.csproj
```

然后检查：
```bash
find src/Aevatar.Agents.Abstractions/obj -name "Messages.cs"
```

应该能看到：
```
src/Aevatar.Agents.Abstractions/obj/Debug/net9.0/Messages.cs
```

### Q3: 如果遇到重复定义错误怎么办？

**A**: 这通常是因为源代码目录中存在旧的 `Messages.cs` 文件。解决方法：

1. 删除源代码根目录中的 `Messages.cs`:
```bash
cd src/Aevatar.Agents.Abstractions
rm Messages.cs
```

2. 清理并重新构建：
```bash
dotnet clean
dotnet build
```

### Q4: 如何自定义生成的代码？

**A**: 可以在 `.csproj` 中添加额外的配置：

```xml
<ItemGroup>
    <Protobuf Include="messages.proto" 
              GrpcServices="None"
              ProtoRoot="."
              CompileOutputs="true"
              OutputDir="%(RelativeDir)Generated" />
</ItemGroup>
```

参数说明：
- `ProtoRoot`: Proto 文件的根目录
- `CompileOutputs`: 是否将生成的文件包含到编译中
- `OutputDir`: 自定义输出目录（相对于 obj 目录）

## 工作流程

### 正常开发流程

1. **修改 .proto 文件**: 编辑 `messages.proto`
2. **自动生成**: 运行 `dotnet build` 会自动重新生成 C# 代码
3. **使用生成的类**: 在代码中直接使用生成的消息类型

```csharp
var envelope = new EventEnvelope
{
    Id = Guid.NewGuid().ToString(),
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    Version = 1,
    Payload = Any.Pack(new LLMEvent { Prompt = "Hello", Response = "World" })
};
```

### 添加新消息类型

1. 在 `messages.proto` 中添加新消息定义：
```protobuf
message NewEventType {
  string field_name = 1;
  int64 field_value = 2;
}
```

2. 运行构建：
```bash
dotnet build
```

3. 新的 C# 类型自动可用：
```csharp
var newEvent = new NewEventType 
{ 
    FieldName = "example", 
    FieldValue = 42 
};
```

## 最佳实践

1. **永远不要手动编辑生成的代码**: `Messages.cs` 是自动生成的，会在每次构建时被覆盖

2. **将 obj/ 和 bin/ 添加到 .gitignore**: 生成的文件不应该提交到版本控制

3. **保持 .proto 文件简洁**: 使用清晰的命名和注释

4. **使用语义化版本**: 当修改 `.proto` 文件时，考虑向后兼容性

5. **定期更新包**: 保持 `Google.Protobuf` 和 `Grpc.Tools` 版本同步

## 参考资源

- [Protobuf C# 文档](https://protobuf.dev/getting-started/csharptutorial/)
- [Grpc.Tools NuGet 包](https://www.nuget.org/packages/Grpc.Tools)
- [Google.Protobuf NuGet 包](https://www.nuget.org/packages/Google.Protobuf)

---

**最后更新**: 2025-10-20  
**适用版本**: .NET 9.0, Protobuf 3.33.0, Grpc.Tools 2.71.0
