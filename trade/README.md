# 🚀 WEEX AI Trading System

> 基于 Aevatar Agent Framework 的多智能体加密货币交易系统

## 项目概述

为 [WEEX AI Wars Hackathon](https://dorahacks.io/hackathon/weex-ai-trading/detail) 设计的 AI 驱动交易系统。

**核心理念**：不是更快的交易机器，而是更智能的决策大脑。

## 架构亮点

```
                    ┌─────────────────────────┐
                    │  TradingCoordinator     │
                    │  (首席交易决策者)        │
                    └───────────┬─────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        │                       │                       │
        ▼                       ▼                       ▼
┌───────────────┐     ┌───────────────┐     ┌───────────────┐
│  Sentiment    │     │  Technical    │     │    News       │
│  Agent        │     │  Agent        │     │    Agent      │
│  市场情绪分析  │     │  技术分析      │     │  新闻分析      │
└───────────────┘     └───────────────┘     └───────────────┘
                                │
                    ┌───────────▼─────────────┐
                    │    RiskManager          │
                    │    (风控经理)            │
                    └───────────┬─────────────┘
                                │
                    ┌───────────▼─────────────┐
                    │    Executor             │
                    │    (交易执行者)          │
                    └─────────────────────────┘
```

## 目录结构

```
trade/
├── README.md                      # 本文件
├── docs/
│   └── ARCHITECTURE.md            # 详细架构文档
│
├── Aevatar.Trade/                 # 核心库
│   ├── trade_messages.proto       # Protobuf 消息定义
│   ├── Aevatar.Trade.csproj
│   ├── TradingSystem.cs           # 系统编排
│   ├── Agents/                    # Agent 实现
│   │   ├── Data/DataCollectorAgent.cs
│   │   ├── Analysts/
│   │   │   ├── MarketSentimentAgent.cs
│   │   │   └── TechnicalAnalystAgent.cs
│   │   ├── Coordinator/TradingCoordinatorAgent.cs
│   │   ├── RiskControl/RiskManagerAgent.cs
│   │   └── Execution/ExecutorAgent.cs
│   └── Infrastructure/WeexApi/    # WEEX API 封装
│
├── Aevatar.Trade.Api/             # Web API Host
│   ├── Aevatar.Trade.Api.csproj
│   ├── Program.cs                 # 应用入口
│   ├── Controllers/               # API 控制器
│   │   └── TradingController.cs
│   ├── Extensions/                # 扩展方法
│   └── appsettings.json
│
└── Aevatar.Trade.AppHost/         # Aspire 编排
    ├── Aevatar.Trade.AppHost.csproj
    ├── Program.cs
    └── appsettings.json
```

## 快速开始

### 方式一：直接运行 API

```bash
# 1. 进入 API 目录
cd trade/Aevatar.Trade.Api

# 2. 配置环境变量
export WEEX_API_KEY="your_api_key"
export WEEX_API_SECRET="your_api_secret"
export WEEX_PASSPHRASE="your_passphrase"
export OPENAI_API_KEY="your_openai_key"

# 3. 运行
dotnet run

# 4. 访问 Swagger: https://localhost:7100/swagger
```

### 方式二：使用 Aspire AppHost

```bash
# 1. 进入 AppHost 目录
cd trade/Aevatar.Trade.AppHost

# 2. 配置环境变量 (同上)

# 3. 运行
dotnet run

# 4. 访问 Aspire Dashboard: http://localhost:15888
# 5. 访问 Trading API: https://localhost:7100/swagger
```

### API 操作

```bash
# 初始化系统
curl -X POST https://localhost:7100/api/trading/initialize

# 启动交易
curl -X POST https://localhost:7100/api/trading/start

# 获取状态
curl https://localhost:7100/api/trading/status

# 停止交易
curl -X POST https://localhost:7100/api/trading/stop
```

## 核心 Agent

| Agent | 职责 | 技术 |
|-------|------|------|
| DataCollector | 数据采集 | WebSocket, REST API |
| MarketSentiment | 市场情绪分析 | LLM + 情绪指标 |
| TechnicalAnalyst | 技术分析 | 指标计算 + LLM |
| NewsAnalyst | 新闻分析 | NLP + LLM |
| TradingCoordinator | 决策协调 | 多源融合 + LLM |
| RiskManager | 风险控制 | 规则引擎 + LLM |
| Executor | 交易执行 | WEEX API |

## 文档

- [📖 详细架构文档](./docs/ARCHITECTURE.md)
- [🧠 Cognitive Mesh 集成计划](./docs/COGNITIVE_MESH_PLAN.md)

## 开发计划

- [x] Week 1: 基础骨架 + WEEX API ✅
  - [x] Protobuf 消息定义
  - [x] WEEX REST API 封装
  - [x] WEEX WebSocket 封装
  - [x] DataCollectorAgent
  - [x] ExecutorAgent
- [x] Week 2: 分析 Agent 实现 ✅
  - [x] MarketSentimentAgent (AI)
  - [x] TechnicalAnalystAgent (AI)
- [x] Week 3: 决策 + 风控逻辑 ✅
  - [x] TradingCoordinatorAgent (AI)
  - [x] RiskManagerAgent (AI)
- [x] Week 4: 优化 + 演示准备 ✅
  - [x] 创建 Host 应用 (Aevatar.Trade.Api)
  - [x] 创建 Aspire AppHost
  - [ ] 集成测试
  - [ ] 演示 UI
  - [ ] 演示视频

## 技术栈

- **框架**: Aevatar Agent Framework
- **运行时**: .NET 8 + Orleans
- **AI**: Microsoft Semantic Kernel / LLM Tornado
- **通信**: Protobuf + Event-Driven
- **可观测性**: OpenTelemetry

---

*WEEX AI Wars: Alpha Awakens*  
*Prize Pool: $880,000 🏆*
