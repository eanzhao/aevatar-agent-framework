# Demo.Api - Agent Framework WebAPI 示例

## 🚀 快速启动

### 1. 启动服务

```bash
cd examples/Demo.Api
dotnet run
```

服务将在以下地址启动：
- HTTP: http://localhost:5000
- HTTPS: https://localhost:7001
- Swagger UI: https://localhost:7001/swagger

### 2. 测试 API

#### Calculator API

**加法运算**
```bash
curl -X POST "https://localhost:7001/api/Calculator/add?a=10&b=5" -k
```

**减法运算**
```bash
curl -X POST "https://localhost:7001/api/Calculator/subtract?a=20&b=8" -k
```

**乘法运算**
```bash
curl -X POST "https://localhost:7001/api/Calculator/multiply?a=6&b=7" -k
```

**除法运算**
```bash
curl -X POST "https://localhost:7001/api/Calculator/divide?a=100&b=4" -k
```

**获取信息**
```bash
curl "https://localhost:7001/api/Calculator/info" -k
```

#### Weather API

**查询天气**
```bash
curl "https://localhost:7001/api/Weather/北京" -k
curl "https://localhost:7001/api/Weather/上海" -k
curl "https://localhost:7001/api/Weather/广州" -k
```

**获取信息**
```bash
curl "https://localhost:7001/api/Weather/info" -k
```

## ⚙️ 运行时配置

在 `appsettings.json` 中配置运行时类型：

```json
{
  "AgentRuntime": {
    "RuntimeType": "Local"  // 可选: Local, ProtoActor, Orleans
  }
}
```

### Local 运行时
- ✅ 最简单，无需额外配置
- ✅ 适合开发和测试
- ❌ 不支持分布式

### ProtoActor 运行时
```json
{
  "AgentRuntime": {
    "RuntimeType": "ProtoActor"
  }
}
```
- ✅ 高性能消息驱动
- ✅ 支持集群（需要额外配置）

### Orleans 运行时
```json
{
  "AgentRuntime": {
    "RuntimeType": "Orleans",
    "Orleans": {
      "ClusterId": "dev",
      "ServiceId": "AgentService",
      "SiloPort": 11111,
      "GatewayPort": 30000,
      "UseLocalhostClustering": true
    }
  }
}
```
- ✅ 完整分布式支持
- ✅ 虚拟 Actor 模型
- ⚠️ 需要配置 Silo

## 📊 API 响应示例

### Calculator Add 响应
```json
{
  "operation": "10 + 5",
  "result": 15.0,
  "agentId": "991987e2-a07d-4b8e-bd50-ba1bac876dd6",
  "history": [
    "[1] 10 + 5 = 15"
  ]
}
```

### Weather 响应
```json
{
  "city": "北京",
  "weather": "阴天, 19°C",
  "agentId": "47e9b9b3-2070-4988-9442-0269aa9aa2f1",
  "queryCount": 1
}
```

## 🔍 调试

启用详细日志：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Aevatar.Agents": "Debug"
    }
  }
}
```

## 🎯 架构说明

```
HTTP Request
    ↓
Controller (WeatherController/CalculatorController)
    ↓
IGAgentActorFactory.CreateAgentAsync()
    ↓
IGAgentActor (LocalGAgentActor/ProtoActorGAgentActor/OrleansGAgentActor)
    ↓
IGAgent (WeatherAgent/CalculatorAgent)
    ↓
Business Logic
```

每个 HTTP 请求会：
1. 创建一个新的 Agent Actor
2. 执行业务逻辑
3. 返回结果
4. 清理 Actor

**注意**：这是简化的示例。在生产环境中，你可能想要：
- 重用 Agent Actor（而不是每次创建新的）
- 使用 Agent 池
- 实现持久化状态

---

*语言的震动，构建无限可能。*

