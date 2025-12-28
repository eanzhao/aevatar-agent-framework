import React from "react";
import { TradingPage } from "./pages/TradingPage";
import { WeexPage } from "./pages/WeexPage";

type TabKey = "trading" | "weex";

export function App() {
  const [tab, setTab] = React.useState<TabKey>("trading");

  return (
    <div className="app">
      <div className="shell">
        <div className="topbar">
          <div className="brand">
            <h1>WEEX AI Trading Console</h1>
            <p>
              Aevatar.Trade 演示 UI：系统状态 / Agent 状态 / WEEX 调试工具。<span style={{ color: "var(--muted2)" }}>默认走同源代理。</span>
            </p>
          </div>
          <div className="nav">
            <div className="tabs" role="tablist" aria-label="console-tabs">
              <button className={`tab ${tab === "trading" ? "active" : ""}`} onClick={() => setTab("trading")}>
                Trading
              </button>
              <button className={`tab ${tab === "weex" ? "active" : ""}`} onClick={() => setTab("weex")}>
                WEEX Tools
              </button>
            </div>
          </div>
        </div>

        {tab === "trading" ? <TradingPage /> : <WeexPage />}

        <div className="muted" style={{ marginTop: 14 }}>
          Tip：后端建议用 <code>--launch-profile http</code>（见 <code>trade/frontend/README.md</code>）。
        </div>
      </div>
    </div>
  );
}


