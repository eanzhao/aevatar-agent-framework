const statusText = document.getElementById("statusText");
const startBtn = document.getElementById("startBtn");
const phaseEl = document.getElementById("phase");
const depthEl = document.getElementById("depth");
const microObjectiveEl = document.getElementById("microObjective");
const pendingChildrenEl = document.getElementById("pendingChildren");
const votesTableBody = document.querySelector("#votesTable tbody");
const finalResultEl = document.getElementById("finalResult");
const microListEl = document.getElementById("microList");
const stepsTableBody = document.querySelector("#stepsTable tbody");
const workersContainer = document.getElementById("workersContainer");
const timelineList = document.getElementById("timelineList");
const workerBoxes = new Map();
let latestSnapshot = null;

startBtn.addEventListener("click", async () => {
  // Prevent double clicks
  if (startBtn.disabled) return;
  
  startBtn.disabled = true;
  startBtn.innerText = "启动中...";
  
  try {
    const response = await fetch("/api/run", { 
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        }
    });
    
    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`HTTP ${response.status}: ${errorText}`);
    }
    
    const data = await response.json();
    statusText.innerText = data.status;
    
    // Force immediate refresh
    await refreshAll();
  } catch (err) {
    console.error(err);
    statusText.innerText = "启动失败";
    statusText.classList.add("error");
    alert(`启动失败: ${err.message}`);
  } finally {
    // Re-enable button after a delay to allow state to settle
    setTimeout(() => {
        startBtn.disabled = false;
        startBtn.innerText = "开始新一轮推演";
    }, 2000);
  }
});

async function fetchJson(url) {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) {
    throw new Error(`请求失败: ${url}`);
  }
  return res.json();
}

async function refreshStatus() {
  try {
    const status = await fetchJson("/api/status");
    statusText.innerText = status.status;
  } catch (err) {
    console.error(err);
  }
}

async function refreshSnapshot() {
  try {
    const snapshot = await fetchJson("/api/snapshot");
    latestSnapshot = snapshot;
    phaseEl.innerText = snapshot.phase ?? "-";
    depthEl.innerText = snapshot.currentDepth ?? 0;
    microObjectiveEl.innerText = snapshot.microObjective ?? "-";
    pendingChildrenEl.innerText = (snapshot.pendingChildren || []).join(", ") || "-";

    finalResultEl.innerText = snapshot.finalResult?.trim() || "暂无结果";
    renderVotes(snapshot);
    renderMicroObjectives(snapshot.microObjectives || [], snapshot.microCursor);
    renderPlannedSteps(snapshot.plannedSteps || []);
    renderWorkerShells(snapshot.workerIds || []);
    
    // Check for failure
    if (snapshot.phase === "PHASE_FAILED") {
        statusText.classList.add("error");
        statusText.innerText = `失败: ${snapshot.failureReason || "未知错误"} (请查看控制台日志)`;
        // Force refresh failure status even if idle
        if (snapshot.status === "idle") {
             // Keep it red
        }
    } else if (snapshot.status === "idle" && snapshot.phase !== "PHASE_COMPLETED" && snapshot.phase !== "Unknown") {
        statusText.classList.add("error");
        // Try to infer reason if failureReason is missing but we stopped unexpectedly
        const reason = snapshot.failureReason || "未知原因";
        statusText.innerText = `异常停止: ${snapshot.phase} (${reason})`;
    } else {
        statusText.classList.remove("error");
    }
  } catch (err) {
    console.error(err);
  }
}

function renderVotes(snapshot) {
  votesTableBody.innerHTML = "";
  const clusters = snapshot.voteClusters || [];
  
  if (clusters.length > 0) {
      // Render clusters instead of raw tallies
      clusters.forEach(cluster => {
          const tr = document.createElement("tr");
          const hashCell = document.createElement("td");
          hashCell.innerText = cluster.id.slice(0, 8);
          
          const voteCell = document.createElement("td");
          voteCell.innerText = `${cluster.votes} (${cluster.variants} 变体)`;
          
          const previewCell = document.createElement("td");
          // Try to prettify JSON if it looks like one
          let content = cluster.content || "";
          if (content.trim().startsWith("[") || content.trim().startsWith("{")) {
              try {
                  // Just a quick attempt to format
                  // content = JSON.stringify(JSON.parse(content), null, 2);
              } catch {}
          }
          previewCell.innerText = content;
          
          tr.appendChild(hashCell);
          tr.appendChild(voteCell);
          tr.appendChild(previewCell);
          votesTableBody.appendChild(tr);
      });
      return;
  }

  // Fallback to raw tallies if no clusters (legacy)
  const tallies = snapshot.voteTallies || {};
  const previews = snapshot.candidatePreviews || {};
  
  if (Object.keys(tallies).length === 0 && votesTableBody.children.length === 0) {
    const tr = document.createElement("tr");
    const td = document.createElement("td");
    td.colSpan = 3;
    td.innerText = "暂无数据";
    tr.appendChild(td);
    votesTableBody.appendChild(tr);
    return;
  }

  Object.keys(tallies)
    .sort((a, b) => tallies[b] - tallies[a])
    .forEach((hash) => {
      const tr = document.createElement("tr");
      const hashCell = document.createElement("td");
      hashCell.innerText = hash.slice(0, 8);

      const voteCell = document.createElement("td");
      voteCell.innerText = tallies[hash];

      const previewCell = document.createElement("td");
      previewCell.innerText = previews[hash] || "";

      tr.appendChild(hashCell);
      tr.appendChild(voteCell);
      tr.appendChild(previewCell);
      votesTableBody.appendChild(tr);
    });
}

async function refreshTimeline() {
  try {
    const events = await fetchJson("/api/timeline");
    timelineList.innerHTML = "";
    events.slice(-200).forEach((evt) => {
      const li = document.createElement("li");
      li.innerHTML = `<span class="time">${new Date(evt.timestamp).toLocaleTimeString()}</span>
        <span class="level level-${evt.level.toLowerCase()}">${evt.level}</span>
        <span class="source">${evt.source}</span>
        <span class="message">${evt.message}</span>`;
      timelineList.appendChild(li);
    });
    renderWorkerStreams(events, latestSnapshot?.workerIds || []);
  } catch (err) {
    console.error(err);
  }
}

async function refreshAll() {
  await Promise.allSettled([refreshStatus(), refreshSnapshot(), refreshTimeline()]);
}

refreshAll();
setInterval(refreshAll, 2000);

function renderMicroObjectives(list, cursor) {
  microListEl.innerHTML = "";
  if (!list || list.length === 0) {
    const li = document.createElement("li");
    li.innerText = "暂无";
    microListEl.appendChild(li);
    return;
  }

  list.forEach((item, index) => {
    const li = document.createElement("li");
    li.innerText = item;
    if (index === cursor) {
      li.classList.add("active");
    }
    microListEl.appendChild(li);
  });
}

function renderPlannedSteps(steps) {
  stepsTableBody.innerHTML = "";
  if (!steps || steps.length === 0) {
    const tr = document.createElement("tr");
    const td = document.createElement("td");
    td.colSpan = 2;
    td.innerText = "暂无计划";
    tr.appendChild(td);
    stepsTableBody.appendChild(tr);
    return;
  }

  steps.forEach((step) => {
    const tr = document.createElement("tr");
    const idCell = document.createElement("td");
    idCell.innerText = step.stepId;
    const descCell = document.createElement("td");
    descCell.innerText = step.description;
    tr.appendChild(idCell);
    tr.appendChild(descCell);
    stepsTableBody.appendChild(tr);
  });
}

function renderWorkerShells(workerIds) {
  if (!workerIds) {
    return;
  }

  workerIds.forEach((id) => ensureWorkerBox(id));
}

function ensureWorkerBox(workerId) {
  if (workerBoxes.has(workerId)) {
    return workerBoxes.get(workerId);
  }

  const card = document.createElement("div");
  card.className = "worker-card";
  card.innerHTML = `
    <div class="worker-header">Worker ${workerId}</div>
    <div class="worker-body">
      <div class="stream" data-worker="${workerId}">等待输出...</div>
    </div>
  `;
  workersContainer.appendChild(card);
  workerBoxes.set(workerId, card);
  return card;
}

function renderWorkerStreams(events, workerIds) {
  const streams = {};
  workerIds?.forEach((id) => {
    streams[id] = [];
  });

  events.forEach((evt) => {
    const msg = evt.message || "";
    if (msg.startsWith("WORKER_STREAM|")) {
      // This log is now removed from backend, but let's keep logic in case we re-enable it or use another channel
      const [, workerId, requestId, chunk] = msg.split("|", 4);
      if (!streams[workerId]) {
        streams[workerId] = [];
      }
      streams[workerId].push(chunk);
    } else if (msg.startsWith("WORKER_DONE|")) {
      const [, workerId, requestId, info] = msg.split("|", 4);
      if (!streams[workerId]) {
        streams[workerId] = [];
      }
      streams[workerId].push(`\n[完成] ${info}`);
    }
  });

  Object.entries(streams).forEach(([workerId, chunks]) => {
    const card = ensureWorkerBox(workerId);
    const streamDiv = card.querySelector(".stream");
    if (chunks.length === 0) {
      streamDiv.innerText = "等待输出...";
    } else {
      // Join without spaces to fix the "space between tokens" issue
      streamDiv.innerText = chunks.join(""); 
    }
  });
}

