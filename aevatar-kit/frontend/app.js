/* AevatarKit MVP frontend (no build step)
 *
 * - Start run via POST /api/runs
 * - Stream events via SSE /api/runs/{runId}/events
 * - Load memory via GET /api/runs/{runId}/memory
 */

let currentRunId = null;
let es = null;

const $ = (id) => document.getElementById(id);

function setConn(connected, text) {
  const dot = $("connDot");
  const t = $("connText");
  dot.classList.remove("ok", "bad");
  dot.classList.add(connected ? "ok" : "bad");
  t.textContent = text;
}

function statusBadge(status) {
  const s = (status || "").toString().toLowerCase();
  if (s.includes("running")) return `<span class="badge running">RUNNING</span>`;
  if (s.includes("stream")) return `<span class="badge streaming">STREAMING</span>`;
  if (s.includes("complete")) return `<span class="badge completed">COMPLETED</span>`;
  if (s.includes("fail")) return `<span class="badge failed">FAILED</span>`;
  return `<span class="badge">${escapeHtml(status || "UNKNOWN")}</span>`;
}

function appendEvent(evt) {
  const el = document.createElement("div");
  el.className = "evt";

  const step = evt.stepName || evt.step_id || "(step)";
  const st = evt.status || evt.status?.toString() || "UNKNOWN";
  const out = evt.outputChunk || evt.output_chunk || "";
  const err = evt.error || "";

  el.innerHTML = `
    <div class="evtTop">
      <div class="evtStep">${escapeHtml(step)}</div>
      <div class="evtStatus">${statusBadge(st)}</div>
    </div>
    <div class="evtBody">${escapeHtml(out || err)}</div>
  `;

  const box = $("events");
  box.appendChild(el);
  box.scrollTop = box.scrollHeight;
}

function renderMemory(entries) {
  const box = $("memoryList");
  box.innerHTML = "";
  for (const e of entries) {
    const el = document.createElement("div");
    el.className = "evt";
    const role = e.role || "unknown";
    const content = e.content || "";
    const scope = e.scope?.type || e.scope?.type?.toString() || "";
    el.innerHTML = `
      <div class="evtTop">
        <div class="evtStep">${escapeHtml(role)}</div>
        <div class="evtStatus"><span class="badge">${escapeHtml(scope)}</span></div>
      </div>
      <div class="evtBody">${escapeHtml(content)}</div>
    `;
    box.appendChild(el);
  }
  box.scrollTop = box.scrollHeight;
}

async function startRun() {
  $("events").innerHTML = "";
  $("memoryList").innerHTML = "";

  const graph = $("graph").value || "";
  const input = $("input").value || "";

  const payload = {
    graphSource: graph,
    input: input,
    sessionId: "demo-session"
  };

  const res = await fetch("/api/runs", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(payload)
  });

  if (!res.ok) {
    const t = await res.text();
    throw new Error(`Start failed: ${res.status} ${t}`);
  }

  const json = await res.json();
  currentRunId = json.runId || json.run_id;

  $("runId").textContent = currentRunId || "-";
  $("sseUrl").textContent = currentRunId ? `/api/runs/${currentRunId}/events` : "-";

  connectSse();
}

function connectSse() {
  if (!currentRunId) return;

  if (es) {
    es.close();
    es = null;
  }

  setConn(false, "Connecting…");

  es = new EventSource(`/api/runs/${currentRunId}/events`);
  es.addEventListener("hello", () => {
    setConn(true, "Connected");
  });
  es.addEventListener("step", (e) => {
    try {
      const evt = JSON.parse(e.data);
      appendEvent(evt);
    } catch (err) {
      appendEvent({ stepName: "client", status: "FAILED", error: String(err), outputChunk: e.data });
    }
  });
  es.onerror = () => {
    setConn(false, "Disconnected");
  };
}

async function refreshMemory() {
  if (!currentRunId) return;
  const res = await fetch(`/api/runs/${currentRunId}/memory?limit=200`);
  if (!res.ok) return;
  const json = await res.json();
  const entries = json.entries || [];
  renderMemory(entries);
}

function escapeHtml(s) {
  return String(s)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function setupTabs() {
  const tabs = document.querySelectorAll(".tab");
  const bodies = document.querySelectorAll(".tabBody");

  tabs.forEach((t) => {
    t.addEventListener("click", () => {
      tabs.forEach((x) => x.classList.remove("active"));
      bodies.forEach((x) => x.classList.remove("active"));
      t.classList.add("active");
      const id = t.dataset.tab;
      document.getElementById(id)?.classList.add("active");
    });
  });
}

async function main() {
  setupTabs();
  setConn(false, "Disconnected");

  $("startBtn").addEventListener("click", async () => {
    try {
      await startRun();
    } catch (e) {
      setConn(false, "Error");
      appendEvent({ stepName: "client", status: "FAILED", error: String(e), outputChunk: "" });
    }
  });

  $("refreshMemoryBtn").addEventListener("click", async () => {
    await refreshMemory();
  });
}

main();


