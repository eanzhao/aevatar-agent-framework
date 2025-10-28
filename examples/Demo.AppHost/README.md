# Demo.AppHost - Aspire 编排项目

这是使用 .NET Aspire 9.5.2 的应用程序主机项目，用于编排和管理 Demo.Api 服务。

## 🎯 功能

- 📊 **Aspire Dashboard** - 实时监控、日志、追踪
- 🔧 **配置驱动** - 通过配置选择运行时（Local/Orleans/ProtoActor）
- 🌐 **服务编排** - 自动管理服务生命周期
- 📈 **性能监控** - 内置指标收集和可视化

## 🚀 使用方法

### 方式1：使用脚本启动（推荐）

```bash
# 在 examples 目录下
cd ..

# 启动不同运行时
./run-local.sh       # Local 运行时
./run-orleans.sh     # Orleans 运行时
./run-protoactor.sh  # ProtoActor 运行时
```

### 方式2：直接运行

```bash
cd examples/Demo.AppHost

# 默认使用 Local 运行时
dotnet run

# 或指定运行时
dotnet run --configuration Debug
```

### 方式3：使用环境变量

```bash
# 设置运行时类型
export AgentRuntime__RuntimeType=Orleans
dotnet run
```

## 📝 配置说明

### appsettings.json

```json
{
  "AgentRuntime": {
    "RuntimeType": "Local"  // Local | Orleans | ProtoActor
  }
}
```

### 不同配置文件

- `appsettings.json` - 默认配置（Local）
- `appsettings.Orleans.json` - Orleans 运行时
- `appsettings.ProtoActor.json` - ProtoActor 运行时

## 🔧 端点配置

Aspire 9.5.2 会**自动从项目的 launchSettings.json 读取端点配置**。

无需手动配置端口：

```csharp
// ✅ 正确 - 自动读取配置
var api = builder.AddProject<Projects.Demo_Api>("demo-api");

// ❌ 错误 - 会导致端点冲突
var api = builder.AddProject<Projects.Demo_Api>("demo-api");
api.WithHttpsEndpoint(port: 7001, name: "https");  // 不需要！
```

端口配置在 `Demo.Api/Properties/launchSettings.json`：

```json
{
  "profiles": {
    "https": {
      "applicationUrl": "https://localhost:7001;http://localhost:5001"
    }
  }
}
```

## 🌐 访问服务

启动后访问：

- **Aspire Dashboard**: https://localhost:15888
  - 📊 服务状态
  - 📝 实时日志
  - 🔍 分布式追踪
  - 📈 性能指标

- **API Swagger**: https://localhost:7001/swagger
  - API 文档和测试

## 🏗️ 项目结构

```
Demo.AppHost/
├── Program.cs              # 应用程序入口
├── appsettings.json        # Local 配置
├── appsettings.Orleans.json
├── appsettings.ProtoActor.json
├── Properties/
│   └── launchSettings.json # 启动配置
└── Demo.AppHost.csproj     # 项目文件
```

## 📦 依赖项

```xml
<Sdk Name="Aspire.AppHost.Sdk" Version="9.5.2" />

<ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.5.2" />
    <!-- Orleans 在 Demo.Api 内部配置，不需要 Aspire.Hosting.Orleans -->
</ItemGroup>
```

## 💡 Orleans 集成说明

Orleans 运行时**不使用** Aspire 的 Orleans 集成，而是在 `Demo.Api` 项目内部配置和启动 Orleans Silo。

这样做的好处：
- ✅ 配置更简单
- ✅ 避免 Aspire Orleans 扩展的兼容性问题
- ✅ 更灵活的 Orleans 配置选项

当 `AgentRuntime__RuntimeType=Orleans` 时，Demo.Api 会：
1. 启动内置的 Orleans Silo
2. 配置本地集群（开发模式）
3. 注册 Agent Grains

## 🐛 常见问题

### Q: 端点冲突错误

**A**: 不要手动配置端点，Aspire 会自动读取 launchSettings.json。

### Q: Dashboard 无法访问

**A**: 确保没有其他服务占用 15888 端口，或修改配置。

### Q: Orleans 运行时失败

**A**: Orleans 需要额外配置，建议先使用 Local 运行时测试。

## 📚 参考资料

- [.NET Aspire 文档](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire Networking](https://aka.ms/dotnet/aspire/networking)
- [项目完整文档](../README.md)

