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

## 设计约束

- **不改动框架调用链**：沿用 `CognitiveStrategy` 的 Actor 并行与 vote 共识机制。
- **输入限制**：当前 `CognitiveStrategy` 默认只注入 `task/context`，所以本项目把 `axioms + focus(可选)` 编码进 task 文本，由 `axiom_theorem_loop.yaml` 在 init 步骤解析并写入 `state`。


