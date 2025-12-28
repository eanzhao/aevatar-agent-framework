import React from "react";
import { apiFetch, prettyJson } from "../api";
import type { AgentsResponse, TradingSystemStatus } from "../types";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { StatusPill } from "../components/StatusPill";

type CallState = { busy: boolean; error?: string; last?: unknown };

function useCallState() {
  const [state, setState] = React.useState<CallState>({ busy: false });
  const run = React.useCallback(async <T,>(fn: () => Promise<T>) => {
    setState({ busy: true });
    try {
      const res = await fn();
      setState({ busy: false, last: res });
      return res;
    } catch (e) {
      const msg =
        typeof e === "object" && e && "message" in e ? String((e as { message?: unknown }).message) : String(e);
      setState({ busy: false, error: msg, last: e });
      throw e;
    }
  }, []);
  return { state, run };
}

export function TradingPage() {
  const [status, setStatus] = React.useState<TradingSystemStatus | null>(null);
  const [agents, setAgents] = React.useState<AgentsResponse | null>(null);
  const [stopReason, setStopReason] = React.useState("API request");

  const calls = useCallState();

  const refresh = React.useCallback(async () => {
    const [s, a] = await Promise.all([
      apiFetch<TradingSystemStatus>("/api/trading/status"),
      apiFetch<AgentsResponse>("/api/agents"),
    ]);
    setStatus(s);
    setAgents(a);
    return { s, a };
  }, []);

  React.useEffect(() => {
    void calls.run(refresh);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="grid cols-2">
      <Panel
        title="Trading System 控制台"
        subtitle="初始化 / 启动 / 停止 / 同步账户（后端：Aevatar.Trade.Api）"
        right={
          <div className="row">
            <Button onClick={() => calls.run(refresh)} disabled={calls.state.busy}>
              刷新状态
            </Button>
            <a href="/swagger" target="_blank" rel="noreferrer">
              Swagger
            </a>
          </div>
        }
      >
        <div className="row" style={{ marginBottom: 12 }}>
          <Button
            variant="primary"
            disabled={calls.state.busy}
            onClick={() =>
              calls.run(async () => {
                const res = await apiFetch("/api/trading/initialize", { method: "POST", body: "{}" });
                await refresh();
                return res;
              })
            }
          >
            Initialize
          </Button>
          <Button
            variant="primary"
            disabled={calls.state.busy}
            onClick={() =>
              calls.run(async () => {
                const res = await apiFetch("/api/trading/start", { method: "POST", body: "{}" });
                await refresh();
                return res;
              })
            }
          >
            Start
          </Button>
          <Button
            variant="danger"
            disabled={calls.state.busy}
            onClick={() =>
              calls.run(async () => {
                const q = new URLSearchParams({ reason: stopReason });
                const res = await apiFetch(`/api/trading/stop?${q.toString()}`, { method: "POST", body: "{}" });
                await refresh();
                return res;
              })
            }
          >
            Stop
          </Button>
          <div className="field" style={{ minWidth: 260 }}>
            <label>Stop reason</label>
            <input value={stopReason} onChange={(e) => setStopReason(e.target.value)} placeholder="reason" />
          </div>
          <Button
            disabled={calls.state.busy}
            onClick={() =>
              calls.run(async () => {
                const res = await apiFetch("/api/trading/sync-account", { method: "POST", body: "{}" });
                await refresh();
                return res;
              })
            }
          >
            Sync Account
          </Button>
        </div>

        {calls.state.error ? (
          <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
            <div className="k" style={{ color: "rgba(239, 68, 68, 0.9)" }}>
              Error
            </div>
            <div className="v">{calls.state.error}</div>
          </div>
        ) : null}

        <div className="grid" style={{ gap: 10 }}>
          {status ? (
            <div className="row" style={{ gap: 8 }}>
              <StatusPill label="DataCollector" value={status.dataCollector} />
              <StatusPill label="Sentiment" value={status.sentimentAnalyst} />
              <StatusPill label="Technical" value={status.technicalAnalyst} />
              <StatusPill label="Coordinator" value={status.coordinator} />
              <StatusPill label="Risk" value={status.riskManager} />
              <StatusPill label="Executor" value={status.executor} />
              <StatusPill label="Audit" value={status.tradeAudit} />
              <StatusPill label="AiWars" value={status.aiWarsUploader} />
            </div>
          ) : (
            <div className="muted">正在加载 status…</div>
          )}

          <div className="grid" style={{ gap: 10 }}>
            <div className="kv">
              <div className="kvItem">
                <div className="k">Agents（/api/agents）</div>
                <div className="v">
                  {agents ? (
                    <div className="kv" style={{ gap: 8 }}>
                      {agents.agents.map((x) => (
                        <div key={x.name} className="row" style={{ justifyContent: "space-between" }}>
                          <span style={{ color: "var(--muted2)", fontWeight: 650 }}>{x.name}</span>
                          <span style={{ textAlign: "right" }}>{x.status}</span>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <span className="muted">正在加载 agents…</span>
                  )}
                </div>
              </div>
            </div>
            <div className="kvItem">
              <div className="k">Last response（调试用）</div>
              <pre className="pre">
                <code>{prettyJson(calls.state.last ?? null)}</code>
              </pre>
            </div>
          </div>
        </div>
      </Panel>

      <Panel title="提示" subtitle="把演示做得像开源库一样稳：先“可控”，再“聪明”">
        <div className="kv">
          <div className="kvItem">
            <div className="k">推荐运行方式</div>
            <div className="v">
              后端用 <code>--launch-profile http</code>，前端走 Vite proxy；你就不会被 HTTPS 证书/跨域扯住脖子。
            </div>
          </div>
          <div className="kvItem">
            <div className="k">设计品味（最短的路径）</div>
            <div className="v">
              让前端只认一个入口：<code>/api/*</code>。同源代理把特殊情况“消失”，比堆 CORS/证书说明更干净。
            </div>
          </div>
          <div className="kvItem">
            <div className="k">下一步可加（不加也能演示）</div>
            <div className="v">
              把 TradingSystem 的“决策 trace/审计”事件做成可视化时间线；演示效果直接起飞。
            </div>
          </div>
        </div>
      </Panel>
    </div>
  );
}


