const state = {};
let refreshTimer;

document.addEventListener("DOMContentLoaded", () => {
  init().catch((err) => console.error(err));
});

async function init() {
  await loadProjects();
  if (refreshTimer) {
    clearInterval(refreshTimer);
  }
  refreshTimer = setInterval(() => {
    Object.keys(state).forEach((projectId) => refreshProject(projectId));
  }, 2000);
}

async function loadProjects() {
  const projects = await fetchJson("/api/projects");
  const tabsEl = document.getElementById("projectTabs");
  const panelsEl = document.getElementById("projectPanels");

  tabsEl.innerHTML = "";
  panelsEl.innerHTML = "";
  Object.keys(state).forEach((key) => delete state[key]);

  projects.forEach((project) => {
    const tab = document.createElement("button");
    tab.className = "project-tab";
    tab.dataset.projectId = project.id;
    tab.innerHTML = `${project.icon || ""} <span>${project.name}</span>`;
    tab.addEventListener("click", () => activateProject(project.id));
    tabsEl.appendChild(tab);

    const panel = document.createElement("section");
    panel.className = "project-panel";
    panel.dataset.projectId = project.id;
    panel.innerHTML = buildPanelTemplate(project);
    panelsEl.appendChild(panel);

    const elements = collectElements(panel);
    elements.startBtn.addEventListener("click", () => startRun(project.id, elements.startBtn));

    state[project.id] = { info: project, tab, panel, elements };
  });

  if (projects.length > 0) {
    activateProject(projects[0].id);
  }
}

function buildPanelTemplate(project) {
  return `
    <div class="project-header">
      <div>
        <h2>${project.icon || "🚀"} ${project.name}</h2>
        <p>${project.description || ""}</p>
      </div>
      <button class="start-btn" data-role="start-btn">⚡ 开始运行</button>
    </div>
    <section class="status-bar">
      <div class="status-item">
        <span class="status-label">运行状态</span>
        <span class="status-value status-idle" data-role="status-text">未启动</span>
      </div>
      <div class="status-item">
        <span class="status-label">当前阶段</span>
        <span class="status-value" data-role="phase">-</span>
      </div>
      <div class="status-item">
        <span class="status-label">递归深度</span>
        <span class="status-value" data-role="depth">-</span>
      </div>
      <div class="status-item">
        <span class="status-label">当前微目标</span>
        <span class="status-value" data-role="micro-objective">-</span>
      </div>
    </section>
    <div class="dashboard-grid">
      <div class="col-left">
        <div class="panel">
          <h3>🗳️ 候选提案投票</h3>
          <div class="table-container">
            <table>
              <thead>
                <tr>
                  <th style="width: 15%">Cluster ID</th>
                  <th style="width: 15%">票数</th>
                  <th>提案内容预览</th>
                </tr>
              </thead>
              <tbody data-role="votes">
                <tr><td colspan="3" class="empty">暂无数据</td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="panel">
          <h3>📋 任务分解树</h3>
          <div class="pending-tags" data-role="pending"></div>
          <div class="table-container">
            <table>
              <thead>
                <tr>
                  <th style="width: 18%">ID</th>
                  <th>子任务描述</th>
                </tr>
              </thead>
              <tbody data-role="steps">
                <tr><td colspan="2" class="empty">暂无计划</td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="panel">
          <h3>🎯 微目标进度</h3>
          <ol class="micro-list" data-role="micro-list"></ol>
        </div>
      </div>
      <div class="col-right">
        <div class="panel">
          <h3>🤖 Worker 思考流</h3>
          <div class="workers-grid" data-role="workers"></div>
        </div>
      </div>
    </div>
    <div class="bottom-grid">
      <div class="panel">
        <h3>✨ 最终共识结果</h3>
        <div class="markdown-body" data-role="final-result">等待产出...</div>
      </div>
      <div class="panel">
        <h3>📜 系统日志流</h3>
        <ul class="timeline-list" data-role="timeline"></ul>
      </div>
    </div>
  `;
}

function collectElements(panel) {
  const query = (role) => panel.querySelector(`[data-role="${role}"]`);
  return {
    startBtn: query("start-btn"),
    statusText: query("status-text"),
    phase: query("phase"),
    depth: query("depth"),
    microObjective: query("micro-objective"),
    pending: query("pending"),
    votesBody: query("votes"),
    stepsBody: query("steps"),
    microList: query("micro-list"),
    workersContainer: query("workers"),
    timelineList: query("timeline"),
    finalResult: query("final-result")
  };
}

function activateProject(projectId) {
  Object.values(state).forEach(({ tab, panel }) => {
    tab.classList.toggle("active", tab.dataset.projectId === projectId);
    panel.classList.toggle("active", panel.dataset.projectId === projectId);
  });
}

async function startRun(projectId, button) {
  if (!state[projectId]) {
    return;
  }
  button.disabled = true;
  button.textContent = "运行中...";
  try {
    await fetch(`/api/projects/${projectId}/run`, {
      method: "POST"
    });
  } catch (err) {
    console.error(err);
  } finally {
    setTimeout(() => {
      button.disabled = false;
      button.textContent = "⚡ 开始运行";
    }, 1500);
  }
}

async function refreshProject(projectId) {
  const projectState = state[projectId];
  if (!projectState) {
    return;
  }
  try {
    const [status, snapshot, timeline] = await Promise.all([
      fetchJson(`/api/projects/${projectId}/status`),
      fetchJson(`/api/projects/${projectId}/snapshot`),
      fetchJson(`/api/projects/${projectId}/timeline`)
    ]);

    updateStatus(projectState, status);
    updateSnapshot(projectState, snapshot);
    renderTimeline(projectState, timeline);
  } catch (err) {
    console.error(err);
  }
}

function updateStatus(projectState, status) {
  const { elements } = projectState;
  const text = status.status === "running" ? "运行中" : "空闲";
  elements.statusText.textContent = text;
  elements.statusText.classList.remove("status-running", "status-idle", "status-error");
  elements.statusText.classList.add(status.status === "running" ? "status-running" : "status-idle");
  if (status.status === "running") {
    elements.startBtn.disabled = true;
  } else {
    elements.startBtn.disabled = false;
  }
}

function updateSnapshot(projectState, snapshot) {
  projectState.snapshot = snapshot;
  const { elements } = projectState;
  elements.phase.textContent = snapshot.phase || "-";
  elements.depth.textContent = snapshot.currentDepth ?? 0;
  elements.microObjective.textContent = snapshot.microObjective || "-";

  renderVotes(snapshot, elements.votesBody);
  renderPending(snapshot.pendingChildren || [], elements.pending);
  renderPlannedSteps(snapshot.plannedSteps || [], elements.stepsBody);
  renderMicroObjectives(snapshot.microObjectives || [], snapshot.microCursor ?? 0, elements.microList);
  renderFinalResult(snapshot.finalResult || "", elements.finalResult);
}

function renderVotes(snapshot, tbody) {
  tbody.innerHTML = "";
  const clusters = snapshot.voteClusters || [];
  if (clusters.length === 0) {
    const tr = document.createElement("tr");
    tr.innerHTML = '<td colspan="3" class="empty">暂无数据</td>';
    tbody.appendChild(tr);
    return;
  }

  clusters.forEach((cluster) => {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${cluster.id.slice(0, 8)}</td>
      <td>${cluster.votes} (${cluster.variants} 变体)</td>
      <td>${cluster.content || ""}</td>
    `;
    tbody.appendChild(tr);
  });
}

function renderPending(pending, container) {
  container.innerHTML = "";
  if (!pending.length) {
    container.innerHTML = '<span class="tag">暂无待处理子任务</span>';
    return;
  }
  pending.forEach((item) => {
    const span = document.createElement("span");
    span.className = "tag";
    span.textContent = item;
    container.appendChild(span);
  });
}

function renderPlannedSteps(steps, tbody) {
  tbody.innerHTML = "";
  if (!steps.length) {
    const tr = document.createElement("tr");
    tr.innerHTML = '<td colspan="2" class="empty">暂无计划</td>';
    tbody.appendChild(tr);
    return;
  }
  steps.forEach((step) => {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${step.stepId}</td>
      <td>${step.description}</td>
    `;
    tbody.appendChild(tr);
  });
}

function renderMicroObjectives(list, cursor, container) {
  container.innerHTML = "";
  if (!list.length) {
    const li = document.createElement("li");
    li.textContent = "暂无目标";
    container.appendChild(li);
    return;
  }

  list.forEach((item, index) => {
    const li = document.createElement("li");
    li.textContent = item;
    if (index === cursor) {
      li.classList.add("active");
    }
    container.appendChild(li);
  });
}

function renderFinalResult(content, container) {
  const text = content?.trim();
  if (!text) {
    container.textContent = "暂无结果";
    return;
  }
  if (window.marked) {
    container.innerHTML = marked.parse(text);
  } else {
    container.textContent = text;
  }
}

function renderTimeline(projectState, events) {
  const { elements } = projectState;
  const list = elements.timelineList;
  list.innerHTML = "";
  const recent = events.slice(-200);

  const workerStreams = {};
  (projectState.snapshot?.workerIds || []).forEach((id) => {
    workerStreams[id] = [];
  });

  recent.forEach((evt) => {
    const li = document.createElement("li");
    li.className = "timeline-item";
    li.innerHTML = `
      <span class="timeline-time">${formatTime(evt.timestamp)}</span>
      <span class="timeline-source">${evt.source}</span>
      <span class="timeline-message">${evt.message}</span>
    `;
    if (evt.level) {
      li.classList.add(`timeline-level-${evt.level.toLowerCase()}`);
    }
    list.appendChild(li);
  });

  updateWorkerStreams(projectState, recent);
}

function updateWorkerStreams(projectState, events) {
  const { elements } = projectState;
  if (!projectState.snapshot) {
    return;
  }
  const container = elements.workersContainer;
  const snapshot = projectState.snapshot || {};
  const workerIds = snapshot.workerIds || [];

  const streamMap = {};
  workerIds.forEach((id) => {
    streamMap[id] = [];
  });

  events.forEach((evt) => {
    const msg = evt.message || "";
    if (msg.startsWith("WORKER_STREAM|")) {
      const [, workerId, requestId, chunk] = msg.split("|", 4);
      if (!streamMap[workerId]) {
        streamMap[workerId] = [];
      }
      streamMap[workerId].push(chunk || "");
    } else if (msg.startsWith("WORKER_DONE|")) {
      const [, workerId, , info] = msg.split("|", 4);
      if (!streamMap[workerId]) {
        streamMap[workerId] = [];
      }
      streamMap[workerId].push(`\n[完成] ${info || ""}`);
    }
  });

  container.innerHTML = "";
  if (!workerIds.length) {
    container.innerHTML = '<div class="tag">暂无 Worker</div>';
    return;
  }

  workerIds.forEach((id) => {
    const card = document.createElement("div");
    card.className = "worker-card";
    const content = (streamMap[id] || []).join("").trim() || "等待输出...";
    card.innerHTML = `
      <div class="worker-header">Worker ${id}</div>
      <div class="worker-body">${content}</div>
    `;
    container.appendChild(card);
  });
}

function formatTime(timestamp) {
  try {
    return new Date(timestamp).toLocaleTimeString();
  } catch {
    return "-";
  }
}

async function fetchJson(url) {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) {
    throw new Error(`请求失败: ${url}`);
  }
  return res.json();
}
