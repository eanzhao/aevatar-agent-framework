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
  renderScheduled: false,
  lastRenderAt: 0,
  modal: { open: false, kind: null, id: null },
};

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
      workers: new Map(), // workerId -> { lastEvt }
      lastVoteStepId: null,
    };
  }
  return state.cache[sessionId];
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
    renderWorkers(cache);
    renderSteps(cache);
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
  const host = $("workers");
  const entries = Array.from(cache.workers.entries());
  if (entries.length === 0) {
    host.classList.add("empty-state");
    host.textContent = "No worker output yet";
    $("worker-n").textContent = "-";
    return;
  }
  host.classList.remove("empty-state");

  // Estimate N: if K exists in last vote, N=2K-1; else infer from workers count (excluding coordinator)
  const k = cache.lastVoteStepId ? (cache.steps.get(cache.lastVoteStepId)?.last?.voteK || 0) : 0;
  const n = k > 0 ? (2 * k - 1) : entries.filter(([id]) => id !== "coordinator").length;
  $("worker-n").textContent = String(n || "-");

  host.innerHTML = entries
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([workerId, w]) => {
      const evt = w.lastEvt || {};
      const body = evt.assistantResponsePreview || evt.assistantResponse || evt.message || "";
      const meta = `${evt.stepType || "-"} · ${evt.stepStatus || "-"}`;
      return `
        <div class="worker">
          <div class="hdr">
            <div class="wid">${escapeHtml(workerId)}</div>
            <div class="meta">${escapeHtml(meta)}</div>
          </div>
          <div class="body">${escapeHtml(body)}</div>
        </div>
      `;
    })
    .join("");
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
  const el = $("raw");
  const s = JSON.stringify(evt);
  el.textContent += (el.textContent ? "\n" : "") + s;
  if (!state.rawOpen) return;
  el.scrollTop = el.scrollHeight;
}

function openModalForStep(sessionId, stepId) {
  const cache = ensureSessionCache(sessionId);
  const step = cache.steps.get(stepId);
  if (!step || !step.last) return;
  const evt = step.last;

  $("modal-title").textContent = evt.stepId || stepId;
  $("modal-sub").textContent = `type=${evt.stepType || "-"} · status=${evt.stepStatus || "-"} · worker=${evt.workerId || "-"}`;
  $("modal-message").textContent = evt.message || "";
  $("modal-user").textContent = evt.userPrompt || "";
  $("modal-output").textContent = evt.assistantResponse || evt.assistantResponsePreview || "";

  const proposalsHost = $("modal-proposals");
  const proposals = step.proposals ? Array.from(step.proposals.values()) : [];
  if (!proposals.length) {
    proposalsHost.classList.add("empty-state");
    proposalsHost.textContent = "No proposals";
  } else {
    proposalsHost.classList.remove("empty-state");
    proposalsHost.innerHTML = proposals
      .map((p) => {
        const title = `${p.stepId || ""} · ${p.workerId || ""} · ${p.stepStatus || ""}`;
        const body = p.assistantResponse || p.assistantResponsePreview || p.message || "";
        return `<div class="mono"><b>${escapeHtml(title)}</b>\n${escapeHtml(body)}</div>`;
      })
      .join("");
  }

  $("modal").classList.remove("hidden");
  state.modal = { open: true, kind: "step", id: stepId };
}

function openModalForWorker(sessionId, workerId) {
  const cache = ensureSessionCache(sessionId);
  const w = cache.workers.get(workerId);
  if (!w || !w.lastEvt) return;
  const evt = w.lastEvt;

  $("modal-title").textContent = workerId;
  $("modal-sub").textContent = `lastStep=${evt.stepId || "-"} · ${evt.stepType || "-"} · ${evt.stepStatus || "-"}`;
  $("modal-message").textContent = evt.message || "";
  $("modal-user").textContent = evt.userPrompt || "";
  $("modal-output").textContent = evt.assistantResponse || evt.assistantResponsePreview || "";
  const proposalsHost = $("modal-proposals");
  proposalsHost.classList.add("empty-state");
  proposalsHost.textContent = "Open a vote step to see proposals.";

  $("modal").classList.remove("hidden");
  state.modal = { open: true, kind: "worker", id: workerId };
}

function closeModal() {
  $("modal").classList.add("hidden");
  state.modal = { open: false, kind: null, id: null };
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
    cache.workers.set(workerId, { lastEvt: evt });

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

    // 高频流式事件：节流渲染，避免卡顿
    const stepStatus = String(evt.stepStatus || "");
    const isCompleted = stepStatus.toLowerCase().includes("completed") || stepStatus.toLowerCase().includes("failed");
    scheduleRender(sessionId, isCompleted);
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

function connect(sessionId) {
  if (state.es) {
    state.es.close();
    state.es = null;
  }

  state.es = new EventSource(`/api/sessions/${sessionId}/events`);
  state.es.onmessage = (e) => {
    try {
      const evt = JSON.parse(e.data);
      appendRaw(evt);
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
  ensureSessionCache(sessionId);
  connect(sessionId);

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
  };

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

  $("btn-refresh").addEventListener("click", () => refreshSessions());
  $("btn-create").addEventListener("click", createSession);
  $("btn-run").addEventListener("click", runSession);
  $("btn-stop").addEventListener("click", stopSession);

  $("btn-toggle-raw").addEventListener("click", () => {
    state.rawOpen = !state.rawOpen;
    $("raw").classList.toggle("hidden", !state.rawOpen);
  });

  // modal close
  $("modal-close").addEventListener("click", closeModal);
  $("modal-backdrop").addEventListener("click", closeModal);
  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeModal();
  });

  // click-to-open (event delegation)
  $("workers").addEventListener("click", (e) => {
    const card = e.target.closest(".worker");
    if (!card || !state.current) return;
    const wid = card.querySelector(".wid")?.textContent;
    if (wid) openModalForWorker(state.current, wid.trim());
  });
  $("steps").addEventListener("click", (e) => {
    const card = e.target.closest(".step");
    if (!card || !state.current) return;
    const sid = card.getAttribute("data-step-id") || card.querySelector(".sid")?.textContent;
    if (sid) openModalForStep(state.current, sid.trim());
  });
  $("voting-body").addEventListener("click", (e) => {
    const card = e.target.closest(".step");
    if (!card || !state.current) return;
    const sid = card.getAttribute("data-step-id") || card.querySelector(".sid")?.textContent;
    if (sid) openModalForStep(state.current, sid.trim());
  });

  await refreshSessions(true);
});


