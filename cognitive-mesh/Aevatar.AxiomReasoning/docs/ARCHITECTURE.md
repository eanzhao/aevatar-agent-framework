# Aevatar.AxiomReasoning - Architecture

目标：复刻 `Aevatar.PaperReview` 的“Session + SSE + CognitiveStrategy”模式，实现 **定理发现循环**：

- Coordinator 提出一个新定理（theorem candidate）
- 所有 Workers 并行给出证明/反证/漏洞
- Coordinator 通过 vote 达成“是否证明成功 + 最终证明”的共识
- 证明成功后把定理加入已知集合，并基于“公理 + 已证定理”继续推导下一条

## 目录结构

```
Aevatar.AxiomReasoning/
├── Program.cs
├── Aevatar.AxiomReasoning.csproj
├── Models/
│   └── AxiomSession.cs
├── Services/
│   ├── AxiomReasoningService.cs
│   └── AxiomReasoningEventBridge.cs
└── wwwroot/
    ├── index.html
    ├── styles.css
    └── app.js
```

## 关键路径

1. 前端提交公理与目标 → `POST /api/sessions`
2. 启动推理 → `POST /api/sessions/{id}/run`
3. 后端调用 `CognitiveStrategy.ExecuteAsync`，指定 `CognitiveWorkflow="axiom_theorem_loop"`
4. 进度回调 `ReasoningProgress` 经 `AxiomReasoningEventBridge` 映射成 SSE 事件推给前端
5. 完成后落盘 artifacts：`state.json / theorems.json`
6. （可选）完成后写入 Supabase：将 `state.json / theorems.json` 作为 JSON 字符串持久化到 Postgres

## 设计约束

- **不改动框架调用链**：沿用 `CognitiveStrategy` 的 Actor 并行与 vote 共识机制。
- **输入限制**：当前 `CognitiveStrategy` 默认只注入 `task/context`，所以本项目把 `axioms + focus(可选)` 编码进 task 文本，由 `axiom_theorem_loop.yaml` 在 init 步骤解析并写入 `state`。

## Supabase 持久化（JSON）

### 配置

- 配置入口：`appsettings.secrets.json` → `Supabase` 段
- 关键字段：
  - `Enabled`: 是否启用（默认 false）
  - `Url`: 项目 URL
  - `Key`: anon key
  - `ResultsTable`: 表名（兼容旧字段名 `ReviewsTable`）

### 建表 SQL

在 Supabase Dashboard → SQL Editor 执行（表名默认 `axiom_reasoning_results`）：

```sql
CREATE TABLE axiom_reasoning_results (
  id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id TEXT NOT NULL UNIQUE,
  axioms_text TEXT,
  goal TEXT,
  status TEXT NOT NULL,
  state_json TEXT,
  theorems_json TEXT,
  content TEXT,
  error TEXT,
  llm_calls INTEGER DEFAULT 0,
  total_tokens BIGINT DEFAULT 0,
  duration_seconds DOUBLE PRECISION DEFAULT 0,
  created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
  completed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_axiom_reasoning_results_session_id ON axiom_reasoning_results(session_id);
CREATE INDEX idx_axiom_reasoning_results_created_at ON axiom_reasoning_results(created_at DESC);

ALTER TABLE axiom_reasoning_results ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Allow anonymous access" ON axiom_reasoning_results
  FOR ALL USING (true) WITH CHECK (true);
```


