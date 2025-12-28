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
│   └── FRONTEND.md                # 前端（演示 UI）说明
│
├── frontend/                      # ✅ 演示 UI（Vite + React + TS）
│   ├── README.md                  # 前端启动/联调说明
│   ├── vite.config.ts             # 本地同源代理（默认转发到 http://localhost:7100）
│   └── src/                       # UI 源码（Trading 控制台 / WEEX 工具）
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

# 4. 访问 Swagger: http://localhost:7100/swagger
```

### 方式二：使用 Aspire AppHost

```bash
# 1. 进入仓库根目录 (推荐在根目录用 --project，避免目录切换/路径误用)
cd aevatar-agent-framework

# 2. 配置环境变量 (同上)

# 3. 运行（推荐 http profile：避免本机证书未信任导致 Dashboard gRPC 报 UntrustedRoot）
dotnet run --project trade/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --launch-profile http

# 4. 访问 Aspire Dashboard: http://localhost:15888
# 5. 访问 Trading API: http://localhost:7100/swagger
# 6. 访问 Frontend UI: http://localhost:5173
```

### 方式三：启动演示前端（推荐）

后端用 http profile（避免本机证书/CORS 干扰），前端用同源代理：

```bash
# 1) 启动后端 API（仓库根目录）
dotnet run --project trade/Aevatar.Trade.Api/Aevatar.Trade.Api.csproj --launch-profile http

# 2) 启动前端（新终端）
cd trade/frontend
npm install
npm run dev
```

前端：`http://localhost:5173`  
Swagger：`http://localhost:7100/swagger`

### 常见坑：HTTPS 开发证书未信任

如果你想用 `https` profile（或希望浏览器/客户端不再提示不安全证书），在 macOS 上执行：

```bash
dotnet dev-certs https --trust
```

### 常见坑：start 返回 `403 when 101 was expected`（WebSocket 握手被拒）

这意味着 **WEEX WebSocket 连接握手失败**（客户端期望 101 Switching Protocols，但服务端返回 403）。

建议排查顺序：

- **先验证 REST 是否通**：`GET /api/weex-test/ticker?symbol=cmt_btcusdt`
- **再配置 WebSocket**：在 `trade/Aevatar.Trade.Api/appsettings.secrets.json` 里补齐：
  - `Weex:PublicWebSocketUrl`：留空则会从 `Weex:BaseUrl` 推导（`BaseUrl` → `ws(s)` + `/ws/public`）。如果 WEEX 实际 WS 域名/路径不同，请显式填写完整 WS 地址。
  - `Weex:WebSocketOrigin`：默认 `https://www.weex.com`（部分 WS 网关会强制校验 Origin；不对会直接 403）。

### AI Wars（合约赛道）校准要点

根据 WEEX AI Wars 参赛指南，合约 OpenAPI 会进行 **IP 白名单**校验；提交 BUIDL 时需要填写你的出口 IP 才能“成功调用 OpenAPI”。参见：
- [AI Wars: Participant Guide](https://www.weex.com/api-doc/ai/introduction/ParticipantGuide)

工程侧建议这样配（避免“把合约网关当行情源”导致 start 直接炸）：

- **行情/历史K线**（Market Data）：默认继续走 `https://api-spot.weex.com`
- **下单/查询订单**（Trading）：走 `https://api-contract.weex.com`

对应配置（`trade/Aevatar.Trade.Api/appsettings.secrets.json`）：

- `Weex:TradingBaseUrl = "https://api-contract.weex.com"`
- `Weex:MarketDataBaseUrl = "https://api-spot.weex.com"`（留空也会在检测到 contract BaseUrl 时自动回退）

### API 操作

```bash
# 初始化系统
curl -X POST http://localhost:7100/api/trading/initialize

# 启动交易
curl -X POST http://localhost:7100/api/trading/start

# 获取状态
curl http://localhost:7100/api/trading/status

# 停止交易
curl -X POST http://localhost:7100/api/trading/stop
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
- [🖥️ 前端（演示 UI）](./docs/FRONTEND.md)
- [🧰 AI Wars API → DotNet Skills](./docs/AI_WARS_DOTNET_SKILLS.md)

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
  - [x] 演示 UI（trade/frontend）
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
