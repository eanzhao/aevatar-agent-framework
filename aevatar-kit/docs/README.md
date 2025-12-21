## AevatarKit MVP

这个目录包含 **AevatarKit 的产品形态 MVP**（前后端齐全，但功能不全是预期的）。

### 目录结构（当前）

```text
aevatar-kit/
  docs/
    PLAN.md            # 产品与架构计划（SSOT）
    README.md          # 你正在看的这份
  src/
    AevatarKit.Core/   # Protobuf 契约 + 基础序列化工具
    AevatarKit.Runtime/# MVP Run Engine（in-memory，模拟 Timeline/Streaming/Memory）
    AevatarKit.Api/    # ASP.NET Core API + SSE + 静态前端托管
  frontend/
    index.html         # 单页 UI（无构建步骤）
    app.js             # 调用 API + SSE Timeline
    styles.css         # 现代 UI 皮肤（展示产品形态）
```

### MVP 已实现的展示能力

- **Graph 输入框**：可粘贴 YAML/JSON（MVP 用字符串占位）
- **Start Run**：POST `/api/runs` 创建 run
- **Run Timeline**：SSE `/api/runs/{runId}/events` 实时显示 step 事件（含 streaming chunk）
- **Run Memory**：GET `/api/runs/{runId}/memory` 展示 run scope 的 memory entries
- **Skills（占位）**：展示未来把 Graph/Tool/Prompt/MemoryProfile 打包成 Skill 的产品方向

### 运行方式

```bash
cd aevatar-kit/src/AevatarKit.Api
dotnet run
```

启动后访问：`http://localhost:5777`。

### 约束与已知限制（MVP 故意不做）

- **不接真实 Aevatar runtime**：Run Engine 目前是模拟器（用于“形态展示”）
- **不做多租户/权限**：Memory/Graph/Run 目前都在内存里
- **不做 GraphDefinition(IR) 执行**：MVP 接受字符串，V1 才会以 Protobuf IR 为 SSOT 并编译执行


