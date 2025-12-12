// ============================================================
//  Axiom Reasoning Frontend (Dashboard)
//  - PaperReview-like layout
//  - Human-readable cards (step/proposal/consensus), not token spam
// ============================================================

const $ = (id) => document.getElementById(id);

const API = {
  listSessions: () => fetchJson("/api/sessions"),
  createSession: (payload) =>
    fetch("/api/sessions", { method: "POST", body: JSON.stringify(payload) }).then((r) => r.json()),
  run: (id) => fetch(`/api/sessions/${id}/run`, { method: "POST" }).then((r) => r.json()),
  stop: (id) => fetch(`/api/sessions/${id}/stop`, { method: "POST" }).then((r) => r.json()),
  result: (id) => fetchJson(`/api/sessions/${id}/result`),
  steps: (id) => fetch(`/api/sessions/${id}/artifacts/theorems`).then((r) => r.text()),
  state: (id) => fetch(`/api/sessions/${id}/artifacts/state`).then((r) => r.text()),
};

const PIPELINE = ["LOADING", "INITIALIZING", "EXECUTING", "COMPLETE"];

const state = {
  sessions: [],
  current: null,
  es: null,
  cache: {}, // sessionId -> { status, phase, tokens, llm, progress, steps: Map, workers: Map, lastVoteStepId }
  rawOpen: false,
  rawDropped: 0,
  renderScheduled: false,
  lastRenderAt: 0,
  modal: { sessionId: null, workerId: null, headTs: 0 },
  ui: {
    pointerDownWorkerId: null,
    pointerDownAt: 0,
    pointerDownX: 0,
    pointerDownY: 0,
  },
};

// ============================================================
//  Run Config (persisted in localStorage)
// ============================================================
const RUNCFG_KEY = "axiom_reasoning.run_config.v1";

function readInt(id, fallback) {
  const el = $(id);
  if (!el) return fallback;
  const n = parseInt(el.value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function readBool(id, fallback) {
  const el = $(id);
  if (!el) return fallback;
  return !!el.checked;
}

function loadRunConfig() {
  try {
    const raw = localStorage.getItem(RUNCFG_KEY);
    if (!raw) return null;
    const obj = JSON.parse(raw);
    return obj && typeof obj === "object" ? obj : null;
  } catch {
    return null;
  }
}

function saveRunConfig(cfg) {
  try {
    localStorage.setItem(RUNCFG_KEY, JSON.stringify(cfg));
  } catch {
    // ignore
  }
}

function applyRunConfigToForm(cfg) {
  if (!cfg) return;
  if ($("input-max-duration-minutes") && typeof cfg.maxDurationMinutes === "number") $("input-max-duration-minutes").value = String(cfg.maxDurationMinutes);
  if ($("input-max-llm-calls") && typeof cfg.maxLlmCalls === "number") $("input-max-llm-calls").value = String(cfg.maxLlmCalls);
  if ($("input-max-tokens") && typeof cfg.maxTokens === "number") $("input-max-tokens").value = String(cfg.maxTokens);
  if ($("input-max-depth") && typeof cfg.maxDepth === "number") $("input-max-depth").value = String(cfg.maxDepth);
  if ($("input-continue-on-failure") && typeof cfg.continueOnFailure === "boolean") $("input-continue-on-failure").checked = cfg.continueOnFailure;
}

function fetchJson(url) {
  return fetch(url).then((r) => r.json());
}

function safeText(s) {
  return s == null ? "" : String(s);
}

function escapeHtml(str) {
  return safeText(str)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function escapeAttr(str) {
  return escapeHtml(str).replaceAll('"', "&quot;");
}

function stepParentId(stepId) {
  if (!stepId) return null;
  const idx = stepId.indexOf(".gen[");
  if (idx > 0) return stepId.slice(0, idx);
  return null;
}

function ensureSessionCache(sessionId) {
  if (!state.cache[sessionId]) {
    state.cache[sessionId] = {
      status: "pending",
      phase: "-",
      tokens: 0,
      llm: 0,
      progress: 0,
      steps: new Map(), // stepId -> { last: evt, proposals: Map }
      // PaperReview-like worker cache (stable DOM + history)
      workers: Object.create(null), // workerId -> { ... }
      graph: { iteration: 0, axioms: [], theorems: [] },
      graphIndex: { axiomsById: Object.create(null), theoremsById: Object.create(null) },
      graphSelectedId: null,
      lastVoteStepId: null,
      _stepsDirty: false,
    };
  }
  return state.cache[sessionId];
}

function getWorkerDisplayName(workerId) {
  if (workerId === "coordinator") return "Coordinator";
  const m = String(workerId).match(/worker-(\d+)/);
  if (m) return `Worker ${m[1]}`;
  return workerId;
}

function createWorkerCardDom(workerId) {
  const card = document.createElement("div");
  card.className = "worker-card";
  card.dataset.workerId = workerId;

  const header = document.createElement("div");
  header.className = "worker-header";

  const info = document.createElement("div");
  info.className = "worker-info";

  const nameEl = document.createElement("div");
  nameEl.className = "worker-name";

  const metaEl = document.createElement("div");
  metaEl.className = "worker-meta";

  info.appendChild(nameEl);
  info.appendChild(metaEl);

  const badgeEl = document.createElement("span");
  badgeEl.className = "badge pending";
  badgeEl.textContent = "pending";

  header.appendChild(info);
  header.appendChild(badgeEl);

  const body = document.createElement("div");
  body.className = "worker-body";

  const contentEl = document.createElement("div");
  contentEl.className = "worker-content";

  const emptyEl = document.createElement("div");
  emptyEl.className = "worker-empty";
  emptyEl.textContent = "Waiting for response...";

  body.appendChild(contentEl);
  body.appendChild(emptyEl);

  card.appendChild(header);
  card.appendChild(body);

  return { card, nameEl, metaEl, badgeEl, contentEl, emptyEl };
}

function updateWorkerCardDom(dom, w) {
  dom.nameEl.textContent = w.name || w.id;
  const provider = w.provider || "-";
  const tokens = w.tokenIndex || 0;
  const stepType = w.stepType || "-";
  const err = w.errorMessage ? ` · FAIL: ${String(w.errorMessage).slice(0, 80)}` : "";
  dom.metaEl.textContent = `${provider} · ${tokens} tokens · ${stepType}` + (w.streaming ? " · streaming" : "") + (w.status === "error" ? err : "");

  const statusClass = w.streaming ? "running" : (w.status || "pending");
  const statusText = w.streaming ? "streaming" : (w.status || "pending");
  dom.badgeEl.className = `badge ${statusClass}`;
  dom.badgeEl.textContent = statusText;
  dom.card.classList.toggle("streaming", !!w.streaming);

  // Content: show full content, keep scroll stable
  const displayContent = w.streamContent || w.lastResponse || "";
  if (displayContent) {
    dom.contentEl.style.display = "";
    dom.emptyEl.style.display = "none";

    const atBottom = dom.contentEl.scrollTop + dom.contentEl.clientHeight >= dom.contentEl.scrollHeight - 8;
    const oldTop = dom.contentEl.scrollTop;

    // Streaming perf:
    // - Prefer incremental append during streaming (PaperReview silky mode)
    // - Fallback to full replace when something is inconsistent (e.g. card recreated)
    const pending = w.pendingAppend || "";
    if (pending) {
      const current = dom.contentEl.textContent || "";
      const okToAppend = (current.length + pending.length === displayContent.length) && displayContent.endsWith(pending);
      if (okToAppend) {
        dom.contentEl.insertAdjacentText("beforeend", pending);
      } else {
        dom.contentEl.textContent = displayContent;
      }
      w.pendingAppend = "";
      dom.contentEl.scrollTop = atBottom ? dom.contentEl.scrollHeight : oldTop;
    } else if (dom.contentEl.textContent !== displayContent) {
      dom.contentEl.textContent = displayContent;
      dom.contentEl.scrollTop = atBottom ? dom.contentEl.scrollHeight : oldTop;
    }
    dom.contentEl.classList.toggle("streaming-text", !!w.streaming);
  } else {
    dom.contentEl.style.display = "none";
    dom.emptyEl.style.display = "";
  }
}

function scheduleRender(sessionId, force = false) {
  if (!state.current || state.current !== sessionId) return;

  const now = Date.now();
  if (!force && now - state.lastRenderAt < 120) {
    if (state.renderScheduled) return;
  }

  if (state.renderScheduled) return;
  state.renderScheduled = true;
  requestAnimationFrame(() => {
    state.renderScheduled = false;
    state.lastRenderAt = Date.now();
    const cache = ensureSessionCache(sessionId);
    renderTop(cache);
    renderVoting(cache);
    if (force || cache._stepsDirty) {
      cache._stepsDirty = false;
      renderSteps(cache);
    }
  });
}

// ─────────────────────────────────────────────────────────────
//  Render Scheduling (avoid re-render per token)
// ─────────────────────────────────────────────────────────────
let _workersRenderRaf = 0;
let _workersRenderCache = null;

function scheduleRenderWorkers(cache) {
  _workersRenderCache = cache;
  if (_workersRenderRaf) return;
  _workersRenderRaf = requestAnimationFrame(() => {
    _workersRenderRaf = 0;
    if (_workersRenderCache) {
      renderWorkers(_workersRenderCache);
      updateModalIfOpen(_workersRenderCache);
    }
  });
}

function setDownloads(sessionId, enabled) {
  const aState = $("dl-state");
  const aSteps = $("dl-steps");
  aState.href = `/api/sessions/${sessionId}/artifacts/state`;
  aSteps.href = `/api/sessions/${sessionId}/artifacts/theorems`;
  if (enabled) {
    aState.classList.remove("disabled");
    aSteps.classList.remove("disabled");
  } else {
    aState.classList.add("disabled");
    aSteps.classList.add("disabled");
  }
}

function renderSessions() {
  const el = $("session-list");
  if (!state.sessions || state.sessions.length === 0) {
    el.classList.add("empty-state");
    el.innerHTML = "No sessions";
    return;
  }

  el.classList.remove("empty-state");
  el.innerHTML = state.sessions
    .map((s) => {
      const active = s.id === state.current ? "active" : "";
      const sub = `progress=${s.progressPercent ?? 0}% · llm=${s.totalLlmCalls ?? 0} · tokens=${s.totalTokens ?? 0}`;
      return `
        <div class="session-item ${active}" data-id="${s.id}">
          <div class="line1">
            <div class="id">${escapeHtml(s.id)}</div>
            <div class="status">${escapeHtml(s.status)}</div>
          </div>
          <div class="line2">${escapeHtml(sub)}</div>
        </div>
      `;
    })
    .join("");

  for (const item of el.querySelectorAll(".session-item")) {
    item.addEventListener("click", () => selectSession(item.getAttribute("data-id")));
  }
}

function renderPipeline(phaseText) {
  const phase = (phaseText ?? "").toUpperCase();
  const active = PIPELINE.find((p) => phase.includes(p)) || (phase.includes("COMPLETE") ? "COMPLETE" : "EXECUTING");
  const activeIdx = PIPELINE.indexOf(active);
  $("pipeline-steps").innerHTML = PIPELINE.map((p, idx) => {
    const cls = idx < activeIdx ? "pipe-step done" : idx === activeIdx ? "pipe-step active" : "pipe-step";
    return `<div class="${cls}">${p}</div>`;
  }).join("");
}

function renderTop(cache) {
  $("status-text").textContent = cache.status;
  $("stat-phase").textContent = cache.phase || "-";
  $("stat-tokens").textContent = String(cache.tokens || 0);
  $("stat-llm").textContent = String(cache.llm || 0);
  $("progress-bar-inner").style.width = `${cache.progress || 0}%`;
  renderPipeline(cache.phase || "");
}

function renderVoting(cache) {
  const host = $("voting-body");
  const voteStepId = cache.lastVoteStepId;
  if (!voteStepId) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for vote steps";
    $("vote-k").textContent = "-";
    return;
  }

  const step = cache.steps.get(voteStepId);
  if (!step || !step.last) return;
  const evt = step.last;

  $("vote-k").textContent = String(evt.voteK || "-");

  host.classList.remove("empty-state");
  const consensus = evt.assistantResponse ? `<div class="mono">${escapeHtml(evt.assistantResponse)}</div>` : "";
  host.innerHTML = `
    <div class="step" data-step-id="${escapeHtml(voteStepId)}">
      <div class="top">
        <div>
          <div class="sid">${escapeHtml(evt.stepId || voteStepId)}</div>
          <div class="tag">type=${escapeHtml(evt.stepType)} · status=${escapeHtml(evt.stepStatus)}</div>
        </div>
        <div class="kv">
          <div class="pill">round ${evt.voteRound ?? 0}/${evt.voteMaxRounds ?? 0}</div>
          <div class="pill">votes ${evt.voteCurrentVotes ?? 0}/${evt.voteK ?? 0}</div>
        </div>
      </div>
      <div class="msg">${escapeHtml(evt.message || "")}</div>
      ${consensus}
    </div>
  `;
}

function renderWorkers(cache) {
  const host = $("worker-grid");
  const workers = cache.workers || {};
  const ids = Object.keys(workers);

  $("worker-count").textContent = String(ids.length || 0);

  if (!ids.length) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for workers";
    return;
  }

  // Remove empty placeholder text node without destroying existing cards
  host.classList.remove("empty-state");
  for (const n of Array.from(host.childNodes)) {
    if (n.nodeType === Node.TEXT_NODE && (n.textContent || "").trim().length > 0) {
      host.removeChild(n);
    }
  }

  for (const workerId of ids.sort()) {
    const w = workers[workerId];
    if (!w.dom) {
      w.dom = createWorkerCardDom(workerId);
      host.appendChild(w.dom.card);
    }
    updateWorkerCardDom(w.dom, w);
  }
}

function renderSteps(cache) {
  const host = $("steps");
  const entries = Array.from(cache.steps.entries());
  if (entries.length === 0) {
    host.classList.add("empty-state");
    host.textContent = "No steps yet";
    return;
  }
  host.classList.remove("empty-state");

  // Sort by depth then stepId
  entries.sort((a, b) => {
    const da = a[1].last?.depth ?? 0;
    const db = b[1].last?.depth ?? 0;
    if (da !== db) return da - db;
    return a[0].localeCompare(b[0]);
  });

  host.innerHTML = entries
    .map(([stepId, s]) => {
      const evt = s.last || {};
      const pills = [];
      if (evt.workerId) pills.push(`worker=${evt.workerId}`);
      if (evt.depth != null) pills.push(`depth=${evt.depth}`);
      if (evt.stepType) pills.push(`type=${evt.stepType}`);
      if (evt.stepStatus) pills.push(`status=${evt.stepStatus}`);
      if (evt.voteK) pills.push(`vote ${evt.voteCurrentVotes}/${evt.voteK}`);

      const preview = evt.assistantResponsePreview || "";
      const body = evt.assistantResponse || "";

      // proposals
      const proposals = s.proposals ? Array.from(s.proposals.values()) : [];
      const proposalHtml = proposals.length
        ? `<details><summary>Proposals (${proposals.length})</summary>${proposals
            .map((p) => {
              const pv = p.assistantResponsePreview || p.assistantResponse || p.message || "";
              return `<div class="mono">${escapeHtml(p.stepId || "")}\n${escapeHtml(pv)}</div>`;
            })
            .join("")}</details>`
        : "";

      return `
        <div class="step" data-step-id="${escapeHtml(stepId)}">
          <div class="top">
            <div>
              <div class="sid">${escapeHtml(stepId)}</div>
              <div class="tag">${escapeHtml(evt.message || "")}</div>
            </div>
            <div class="kv">${pills.map((x) => `<div class="pill">${escapeHtml(x)}</div>`).join("")}</div>
          </div>
          ${proposalHtml}
        </div>
      `;
    })
    .join("");
}

function renderResultBox(text, ok) {
  const host = $("result-body");
  host.classList.remove("empty-state");
  host.innerHTML = `<div class="mono">${escapeHtml(text || (ok ? "(empty)" : "(failed)"))}</div>`;
}

function appendRaw(evt) {
  if (!state.rawOpen) return;
  const el = $("raw");
  // Debug only: keep this lightweight to avoid killing streaming performance.
  // NOTE: This still does work; that's why it's collapsed by default.
  const s = JSON.stringify(evt);
  el.insertAdjacentText("beforeend", (el.textContent ? "\n" : "") + s);
  // Soft cap: if debug view gets too big, keep last ~200KB.
  if (el.textContent.length > 200_000) {
    el.textContent = el.textContent.slice(-180_000);
  }
  el.scrollTop = el.scrollHeight;
}

function openWorkerModal(workerId) {
  const cache = state.current ? ensureSessionCache(state.current) : null;
  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  state.modal.sessionId = state.current;
  state.modal.workerId = workerId;
  state.modal.headTs = worker.history?.[0]?.timestamp || 0;

  renderModal(worker);
  $("modal").classList.remove("hidden");
  document.body.style.overflow = "hidden";
}

function renderModal(worker) {
  $("modal-title").textContent = worker.name || worker.id;
  const err = worker.status === "error" && worker.errorMessage ? ` · FAIL: ${String(worker.errorMessage).slice(0, 120)}` : "";
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.tokenIndex || 0} tokens · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "") + err;

  const body = $("modal-body");
  body.innerHTML = "";

  if (!worker.history.length) {
    body.innerHTML = '<div class="empty-state">No conversation history</div>';
    return;
  }

  worker.history.forEach((h, idx) => {
    const item = document.createElement("details");
    item.className = "chat-item";
    item.open = idx === 0;

    const summary = document.createElement("summary");
    summary.className = "chat-header";
    summary.innerHTML = `
      <div class="chat-phase">${escapeHtml(h.phase || "LLM Call")}</div>
      <div class="chat-time">${new Date(h.timestamp).toLocaleTimeString()}</div>
    `;
    item.appendChild(summary);

    const bodyDiv = document.createElement("div");
    bodyDiv.className = "chat-body";

    if (h.system) bodyDiv.appendChild(buildChatSection("System Prompt", h.system, false));
    if (h.user) bodyDiv.appendChild(buildChatSection("User Prompt", h.user, true));

    const respSection = buildChatSection("Response", h.response || "", true);
    const respContent = respSection.querySelector(".chat-content");
    if (respContent) {
      respContent.dataset.role = "modal-response";
      respContent.dataset.idx = String(idx);
      setChatContentText(respContent, h.response || "", worker.streaming && idx === 0);
    }
    bodyDiv.appendChild(respSection);

    item.appendChild(bodyDiv);
    body.appendChild(item);
  });
}

function buildChatSection(label, text, open = true) {
  const section = document.createElement("details");
  section.className = "chat-section";
  section.open = !!open;

  const summary = document.createElement("summary");
  summary.className = "chat-label";
  summary.textContent = label;

  const content = document.createElement("div");
  content.className = "chat-content";
  setChatContentText(content, text, false);

  section.appendChild(summary);
  section.appendChild(content);
  return section;
}

function setChatContentText(el, text, showWaitingWhenEmpty) {
  const val = text || "";
  if (!val && showWaitingWhenEmpty) {
    el.textContent = "Waiting...";
    el.style.color = "var(--text-muted)";
    return;
  }
  el.textContent = val;
  el.style.color = "";
}

function updateModalIfOpen(cache) {
  const modalEl = $("modal");
  if (modalEl.classList.contains("hidden")) return;

  const { sessionId, workerId } = state.modal || {};
  if (!sessionId || !workerId) return;
  if (sessionId !== state.current) return;

  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  const headTs = worker.history?.[0]?.timestamp || 0;
  if (headTs && headTs !== state.modal.headTs) {
    state.modal.headTs = headTs;
    renderModal(worker);
    return;
  }

  $("modal-title").textContent = worker.name || worker.id;
  const err = worker.status === "error" && worker.errorMessage ? ` · FAIL: ${String(worker.errorMessage).slice(0, 120)}` : "";
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.tokenIndex || 0} tokens · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "") + err;

  const respEl = $("modal-body").querySelector('[data-role="modal-response"][data-idx="0"]');
  if (!respEl) return;
  const latest = worker.history?.[0]?.response || "";
  setChatContentText(respEl, latest, !!worker.streaming);
}

function closeModal() {
  $("modal").classList.add("hidden");
  document.body.style.overflow = "";
  state.modal.sessionId = null;
  state.modal.workerId = null;
  state.modal.headTs = 0;
}

function applyEvent(sessionId, evt) {
  const cache = ensureSessionCache(sessionId);

  if (evt.type === "ProgressEvent") {
    cache.phase = evt.phase || cache.phase;
    cache.progress = evt.progressPercent ?? cache.progress;
    cache.tokens = evt.totalTokens ?? cache.tokens;
    cache.llm = evt.totalLlmCalls ?? cache.llm;
    cache.status = "running";

    const workerId = evt.workerId || "coordinator";
    if (!cache.workers[workerId]) {
      cache.workers[workerId] = {
        id: workerId,
        name: getWorkerDisplayName(workerId),
        provider: "",
        tokenIndex: 0,
        status: "pending",
        streaming: false,
        streamContent: "",
        lastResponse: "",
        pendingAppend: "",
        errorMessage: "",
        history: [],
        stepId: "",
        stepType: "",
        dom: null,
      };
    }
    const w = cache.workers[workerId];
    w.stepId = evt.stepId || "";
    w.stepType = evt.stepType || "";
    if (evt.providerName) w.provider = evt.providerName;
    if (typeof evt.tokenIndex === "number") w.tokenIndex = evt.tokenIndex;

    const st = String(evt.stepStatus || "").toLowerCase();
    w.status = st.includes("failed") ? "error" : st.includes("completed") ? "completed" : "running";
    if (w.status === "error") {
      w.errorMessage = evt.error || evt.message || w.errorMessage || "failed";
    } else if (w.status === "completed") {
      w.errorMessage = "";
    }

    const sid = evt.stepId || evt.phase || "main";
    const parent = stepParentId(sid);
    if (parent) {
      // proposal under vote
      if (!cache.steps.has(parent)) cache.steps.set(parent, { last: null, proposals: new Map() });
      const p = cache.steps.get(parent);
      p.proposals.set(sid, evt);
    }

    if (!cache.steps.has(sid)) cache.steps.set(sid, { last: null, proposals: new Map() });
    const step = cache.steps.get(sid);
    step.last = evt;

    if (String(evt.stepType || "").toLowerCase() === "vote") {
      cache.lastVoteStepId = sid;
    }

    // Worker history (PaperReview-like):
    // - We treat each completed llm_call as one history item.
    // - During streaming, we keep updating the head item to show live text.
    if (String(evt.stepType || "").toLowerCase() === "llm_call") {
      const now = Date.now();
      const isCompleted = st.includes("completed");
      const isFailed = st.includes("failed");
      const delta = evt.tokenDelta || "";
      const finalBody = evt.assistantResponse || "";
      const content = finalBody || evt.assistantResponsePreview || "";

      const head = w.history[0];
      const isSameStep = head && head.stepId === (evt.stepId || "");
      if (!isSameStep) {
        w.history.unshift({
          timestamp: now,
          stepId: evt.stepId || "",
          phase: evt.phase || "LLM Call",
          status: evt.stepStatus || "",
          system: evt.systemPrompt || "",
          user: evt.userPrompt || "",
          response: "",
        });
        if (w.history.length > 12) w.history.pop();
      }
      if (w.history.length) {
        if (delta) w.history[0].response = (w.history[0].response || "") + delta;
        else if (content) w.history[0].response = content;
        w.history[0].status = evt.stepStatus || w.history[0].status;
        // fill prompts if they arrive later
        if (!w.history[0].system && evt.systemPrompt) w.history[0].system = evt.systemPrompt;
        if (!w.history[0].user && evt.userPrompt) w.history[0].user = evt.userPrompt;
      }

      w.streaming = !(isCompleted || isFailed);
      if (delta) {
        w.pendingAppend = (w.pendingAppend || "") + delta;
        w.streamContent = (w.streamContent || "") + delta;
      } else if (content) {
        // Completed/fallback: replace to authoritative content
        w.pendingAppend = "";
        w.streamContent = content;
      }
      if (isCompleted && content) w.lastResponse = content;
    }

    // 高频流式事件：节流渲染，避免卡顿
    const isDone = st.includes("completed") || st.includes("failed");
    const stepType = String(evt.stepType || "").toLowerCase();
    // Only mark steps panel dirty for non-streaming / stateful transitions
    if (isDone || (stepType && stepType !== "llm_call")) {
      cache._stepsDirty = true;
    }
    scheduleRender(sessionId, isDone);

    // Keep workers/cards stable: update via rAF to avoid per-token DOM churn
    scheduleRenderWorkers(cache);
    return;
  }

  if (evt.type === "GraphEvent") {
    cache.graph = {
      iteration: evt.iteration || 0,
      axioms: Array.isArray(evt.axioms) ? evt.axioms : [],
      theorems: Array.isArray(evt.theorems) ? evt.theorems : [],
    };
    $("graph-iter").textContent = String(cache.graph.iteration || 0);
    // Build index for inspector (axiomsById / theoremsById)
    const axiomsById = Object.create(null);
    const axId = (line, idx) => {
      const m = String(line || "").match(/^([A-Za-z]\w*)\s*:/);
      return m ? m[1] : `A${idx + 1}`;
    };
    for (let i = 0; i < cache.graph.axioms.length; i++) {
      const line = cache.graph.axioms[i];
      axiomsById[axId(line, i)] = line;
    }
    const theoremsById = Object.create(null);
    for (const t of cache.graph.theorems) {
      if (t && t.id) theoremsById[t.id] = t;
    }
    cache.graphIndex = { axiomsById, theoremsById };

    renderGraph(cache.graph, cache.graphSelectedId);
    renderGraphInspector(cache);
    return;
  }

  if (evt.type === "ResultEvent") {
    cache.status = evt.success ? "completed" : "failed";
    cache.phase = "COMPLETE";
    cache.progress = 100;
    cache.tokens = evt.totalTokens ?? cache.tokens;
    cache.llm = evt.totalLlmCalls ?? cache.llm;
    scheduleRender(sessionId, true);

    $("btn-stop").disabled = true;
    $("btn-run").disabled = false;
    setDownloads(sessionId, true);

    // Render result + try parse artifacts
    const summary = evt.success ? (evt.content || "(no content)") : (evt.error || "failed");
    renderResultBox(summary, evt.success);

    // Best-effort: fetch steps.json for nicer display
    void (async () => {
      try {
        const stepsText = await API.steps(sessionId);
        renderResultBox(stepsText, true);
      } catch {
        // ignore
      }
    })();
  }
}

function renderGraph(graph, selectedId) {
  const viewport = $("graph-viewport");
  if (!viewport) return;

  const axioms = Array.isArray(graph.axioms) ? graph.axioms : [];
  const theorems = Array.isArray(graph.theorems) ? graph.theorems : [];

  function axId(line, idx) {
    // NOTE: Regex literal must use single backslashes (\w, \s). Double backslashes would match literal "\w".
    const m = String(line || "").match(/^([A-Za-z]\w*)\s*:/);
    return m ? m[1] : `A${idx + 1}`;
  }

  const nodes = [];
  for (let i = 0; i < axioms.length; i++) nodes.push({ id: axId(axioms[i], i), label: axioms[i], kind: "axiom" });
  for (const t of theorems) nodes.push({
    id: t.id || "",
    label: t.statement || t.id || "",
    kind: "theorem",
    dependsOn: t.dependsOn || t.depends_on || [],
    proof: t.proof || "",
  });

  const byId = Object.create(null);
  for (const n of nodes) byId[n.id] = n;

  // layout
  const pos = Object.create(null);
  let y = 50;
  for (const n of nodes.filter(n => n.kind === "axiom")) {
    pos[n.id] = { x: 40, y };
    y += 90;
  }
  const ths = nodes.filter(n => n.kind === "theorem").sort((a, b) => String(a.id).localeCompare(String(b.id)));
  const laneY = Object.create(null);
  for (let i = 0; i < ths.length; i++) {
    const id = ths[i].id;
    const m = id.match(/^T(\\d+)$/i);
    const layer = m ? parseInt(m[1], 10) : (i + 1);
    if (!laneY[layer]) laneY[layer] = 50;
    pos[id] = { x: 320 + (layer - 1) * 280, y: laneY[layer] };
    laneY[layer] += 120;
  }

  // edges
  const edges = [];
  for (const t of ths) {
    const deps = Array.isArray(t.dependsOn) ? t.dependsOn : [];
    for (const d of deps) {
      const depId = String(d || "").trim();
      if (byId[depId]) edges.push({ from: depId, to: t.id });
    }
  }

  const parts = [];
  for (const e of edges) {
    const a = pos[e.from];
    const b = pos[e.to];
    if (!a || !b) continue;
    const x1 = a.x + 220, y1 = a.y;
    const x2 = b.x, y2 = b.y;
    const mx = (x1 + x2) / 2;
    parts.push(`<path d="M ${x1} ${y1} C ${mx} ${y1}, ${mx} ${y2}, ${x2} ${y2}" stroke="#94a3b8" stroke-width="2" fill="none" />`);
  }
  for (const n of nodes) {
    const p = pos[n.id];
    if (!p) continue;
    const fill = n.kind === "axiom" ? "#eff6ff" : "#f0fdf4";
    const stroke = n.kind === "axiom" ? "#bfdbfe" : "#bbf7d0";
    const title = escapeHtml(String(n.label || "").replace(/\\s+/g, " ").slice(0, 72));
    const sel = selectedId && selectedId === n.id;
    parts.push(`
      <g class="graph-node ${sel ? "selected" : ""}" data-node-id="${escapeAttr(n.id)}" data-node-kind="${escapeAttr(n.kind)}">
        <rect x="${p.x}" y="${p.y - 22}" rx="10" ry="10" width="220" height="54" fill="${fill}" stroke="${stroke}" stroke-width="2"></rect>
        <text x="${p.x + 10}" y="${p.y - 2}" font-family="ui-monospace, Menlo, Consolas" font-size="12" fill="#0f172a">${escapeHtml(n.id)}</text>
        <text x="${p.x + 10}" y="${p.y + 16}" font-family="ui-sans-serif, system-ui" font-size="12" fill="#334155">${title}</text>
      </g>
    `);
  }
  viewport.innerHTML = parts.join("");
}

function renderGraphInspector(cache) {
  const host = $("graph-inspector");
  if (!host) return;

  const graph = cache?.graph;
  const axioms = Array.isArray(graph?.axioms) ? graph.axioms : [];
  const theorems = Array.isArray(graph?.theorems) ? graph.theorems : [];

  if (!axioms.length && !theorems.length) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for graph...";
    return;
  }

  const selectedId = cache.graphSelectedId;
  if (!selectedId) {
    host.classList.add("empty-state");
    host.textContent = "Click a node to inspect details.";
    return;
  }

  host.classList.remove("empty-state");
  const idx = cache.graphIndex || { axiomsById: {}, theoremsById: {} };
  const ax = idx.axiomsById ? idx.axiomsById[selectedId] : null;
  const th = idx.theoremsById ? idx.theoremsById[selectedId] : null;

  if (ax) {
    host.innerHTML = `
      <div class="title">Axiom · ${escapeHtml(selectedId)}</div>
      <div class="kv"><div class="pill">kind=axiom</div></div>
      <div class="mono">${escapeHtml(ax)}</div>
    `;
    return;
  }

  if (th) {
    const deps = Array.isArray(th.dependsOn) ? th.dependsOn : (Array.isArray(th.depends_on) ? th.depends_on : []);
    const depPills = deps.map((d) => `<div class="pill">${escapeHtml(String(d))}</div>`).join("");
    const proof = th.proof || "";
    host.innerHTML = `
      <div class="title">Theorem · ${escapeHtml(th.id || selectedId)}</div>
      <div class="kv">
        <div class="pill">kind=theorem</div>
        <div class="pill">deps=${deps.length}</div>
      </div>
      <div class="mono">${escapeHtml(th.statement || "")}</div>
      <div style="margin-top:10px; font-weight:800; color: var(--text);">Depends on</div>
      <div class="kv" style="margin-top:6px;">${depPills || '<div class="empty-state">none</div>'}</div>
      <details style="margin-top:8px;">
        <summary style="cursor:pointer; font-weight:800; color: var(--text-secondary);">Proof</summary>
        <div class="mono" style="margin-top:8px;">${escapeHtml(proof || "(no proof in graph event)")}</div>
      </details>
    `;
    return;
  }

  host.classList.add("empty-state");
  host.textContent = "Selected node not found in current graph.";
}

function connect(sessionId) {
  if (state.es) {
    state.es.close();
    state.es = null;
  }

  state.es = new EventSource(`/api/sessions/${sessionId}/events`);
  state.es.onmessage = (e) => {
    try {
      const evt = JSON.parse(e.data);
      if (state.rawOpen) {
        appendRaw(evt);
      } else {
        state.rawDropped++;
      }
      applyEvent(sessionId, evt);
    } catch {
      // ignore parse errors
    }
  };
}

async function refreshSessions(selectFirst = false) {
  state.sessions = await API.listSessions();
  renderSessions();
  if (selectFirst && !state.current && state.sessions.length) {
    selectSession(state.sessions[0].id);
  }
}

function selectSession(sessionId) {
  if (!sessionId) return;
  state.current = sessionId;
  renderSessions();
  setDownloads(sessionId, false);
  const cache = ensureSessionCache(sessionId);
  connect(sessionId);
  renderGraph(cache.graph, cache.graphSelectedId);
  renderGraphInspector(cache);

  // Allow stop/run for current
  $("btn-stop").disabled = false;
  $("btn-run").disabled = false;
}

async function createSession() {
  const payload = {
    axioms: $("input-axioms").value.trim(),
    goal: $("input-goal").value.trim(),
    k: parseInt($("input-k").value, 10) || 3,
    maxRounds: parseInt($("input-max-rounds").value, 10) || 10,
    maxDepth: parseInt($("input-max-depth").value, 10) || 10,

    // Long-run budgets (frontend-configurable)
    maxDurationMinutes: readInt("input-max-duration-minutes", 120),
    maxLlmCalls: readInt("input-max-llm-calls", 5000),
    maxTokens: readInt("input-max-tokens", 8000000),
    continueOnFailure: readBool("input-continue-on-failure", true),
  };

  // Persist run config for next page load
  saveRunConfig({
    maxDurationMinutes: payload.maxDurationMinutes,
    maxLlmCalls: payload.maxLlmCalls,
    maxTokens: payload.maxTokens,
    maxDepth: payload.maxDepth,
    continueOnFailure: payload.continueOnFailure,
  });

  const res = await API.createSession(payload);
  if (!res.success) {
    $("status-text").textContent = `Create failed: ${res.error || "unknown"}`;
    return;
  }
  await refreshSessions();
  selectSession(res.sessionId);
  $("btn-run").disabled = false;
}

async function runSession() {
  if (!state.current) return;
  $("btn-run").disabled = true;
  $("btn-stop").disabled = false;
  setDownloads(state.current, false);

  const res = await API.run(state.current);
  if (!res.success) {
    $("status-text").textContent = `Run failed: ${res.error || "unknown"}`;
    $("btn-run").disabled = false;
    $("btn-stop").disabled = true;
  }
}

async function stopSession() {
  if (!state.current) return;
  const res = await API.stop(state.current);
  $("status-text").textContent = res.success ? "Stopped" : `Stop failed: ${res.error || "unknown"}`;
  $("btn-stop").disabled = true;
  $("btn-run").disabled = false;
}

function initDefaults() {
  if (!$("input-axioms").value.trim()) {
    $("input-axioms").value = [
      "O1: |Ψ⟩ ∈ ℋ, ⟨Ψ|Ψ⟩=1, and ∂t|Ψ⟩=0 (no fundamental external time evolution).",
      "O2: For any bounded causally closed region with boundary area A: dim(ℋ_region) < ∞ and dim(ℋ_region) ~ exp(A/(4 l_P^2)).",
      "O3: ℋ = ⊗_{v∈V} ℋ_v on a countable graph G=(V,E), and U = ∏_k U_local^(k) where each U_local acts only on adjacent vertices / finite neighborhood.",
      "O4: There exists a holographic isometry Φ: ℋ_bulk → ℋ_{∂G} (e.g., Golden MERA / QECC encoding).",
    ].join("\n");
  }
  if (!$("input-goal").value.trim()) {
    $("input-goal").value =
      "Discover novel non-trivial implications and conjectures from O1–O4. Each step must be either (A) DEDUCTION strictly from O1–O4 or prior derived facts, or (B) INTERPRETATION clearly labeled. Prefer small, checkable steps; avoid repetition.";
  }
}

window.addEventListener("load", async () => {
  initDefaults();
  applyRunConfigToForm(loadRunConfig());

  $("btn-refresh").addEventListener("click", () => refreshSessions());
  $("btn-create").addEventListener("click", createSession);
  $("btn-run").addEventListener("click", runSession);
  $("btn-stop").addEventListener("click", stopSession);

  $("btn-toggle-raw").addEventListener("click", () => {
    state.rawOpen = !state.rawOpen;
    $("raw").classList.toggle("hidden", !state.rawOpen);
    if (state.rawOpen) {
      const el = $("raw");
      if (state.rawDropped > 0) {
        el.textContent = `[raw disabled] dropped ${state.rawDropped} events before open\n`;
        state.rawDropped = 0;
      }
    }
  });

  // modal close
  $("modal-close").addEventListener("click", closeModal);
  $("modal-backdrop").addEventListener("click", closeModal);
  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeModal();
  });

  // Workers: PaperReview-style pointerdown/up to survive DOM churn during streaming
  const grid = $("worker-grid");
  grid.addEventListener("pointerdown", (e) => {
    const card = e.target.closest(".worker-card");
    if (!card) return;
    state.ui.pointerDownWorkerId = card.dataset.workerId || null;
    state.ui.pointerDownAt = Date.now();
    state.ui.pointerDownX = e.clientX;
    state.ui.pointerDownY = e.clientY;
  }, { passive: true });
  grid.addEventListener("pointerup", (e) => {
    const wid = state.ui.pointerDownWorkerId;
    if (!wid || !state.current) return;
    const dt = Date.now() - (state.ui.pointerDownAt || 0);
    const dx = Math.abs(e.clientX - (state.ui.pointerDownX || 0));
    const dy = Math.abs(e.clientY - (state.ui.pointerDownY || 0));
    state.ui.pointerDownWorkerId = null;
    if (dt < 600 && dx < 8 && dy < 8) {
      openWorkerModal(wid);
    }
  });
  grid.addEventListener("click", (e) => {
    const card = e.target.closest(".worker-card");
    if (!card || !state.current) return;
    const wid = card.dataset.workerId;
    if (wid) openWorkerModal(wid);
  });

  // Graph: click nodes to inspect details (right inspector panel)
  const svg = $("graph-svg");
  if (svg) {
    svg.addEventListener("click", (e) => {
      if (!state.current) return;
      const t = e.target;
      if (!t || typeof t.closest !== "function") return;
      const node = t.closest(".graph-node");
      if (!node) return;
      const id = node.dataset.nodeId;
      if (!id) return;

      const cache = ensureSessionCache(state.current);
      cache.graphSelectedId = id;
      renderGraph(cache.graph, cache.graphSelectedId);
      renderGraphInspector(cache);
    });
  }

  await refreshSessions(true);
});


