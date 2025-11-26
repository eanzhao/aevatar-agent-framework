const APP_STATE = {
    projects: {},
    activeProjectId: null,
    refreshInterval: null,
    dom: {}, // Cache DOM elements
    activeFile: null // Currently active file in explorer
};

// --- Initialization ---

document.addEventListener("DOMContentLoaded", () => {
    initDomCache();
    initEventListeners();
    startApplicationLoop();
});

function initDomCache() {
    const q = (sel) => document.querySelector(sel);
    APP_STATE.dom = {
        nav: q('#project-nav'),
        dashboard: q('#dashboard'),
        emptyState: q('#empty-state'),
        
        // HUD
        title: q('#project-name'),
        id: q('#project-id'),
        startBtn: q('#btn-start'),
        
        // Stats
        valStatus: q('#val-status'),
        valPhase: q('#val-phase'),
        valDepth: q('#val-depth'),
        valObjective: q('#val-objective'),
        
        // Lists/Tables
        pendingList: q('#pending-tasks'),
        stepTable: q('#table-steps'),
        voteTable: q('#table-votes'),
        
        // Logs/Streams/Files
        logList: q('#log-stream'),
        workerGrid: q('#worker-grid'),
        finalResult: q('#final-result'),
        fileList: q('#file-list'),
        filePreview: q('#file-preview'),
        
        // Tabs
        tabs: document.querySelectorAll('.tab-btn'),
        views: document.querySelectorAll('.term-view')
    };
}

function initEventListeners() {
    // Tab Switching
    APP_STATE.dom.tabs.forEach(btn => {
        btn.addEventListener('click', (e) => {
            // UI Toggle
            APP_STATE.dom.tabs.forEach(b => b.classList.remove('active'));
            e.target.classList.add('active');
            
            // View Toggle
            const targetId = e.target.dataset.target;
            APP_STATE.dom.views.forEach(v => {
                v.classList.toggle('active', v.id === targetId);
            });
        });
    });

    // Start Button
    APP_STATE.dom.startBtn.addEventListener('click', handleStartRun);
}

async function startApplicationLoop() {
    await loadProjectList();
    
    // Polling Loop (Simulating Realtime)
    APP_STATE.refreshInterval = setInterval(async () => {
        if (APP_STATE.activeProjectId) {
            await refreshActiveProject();
        }
    }, 1500);
}

// --- Core Logic ---

async function loadProjectList() {
    try {
        const projects = await fetchJson('/api/projects');
        renderNav(projects);
        
        // Auto-select first if none selected
        if (projects.length > 0 && !APP_STATE.activeProjectId) {
            selectProject(projects[0].id);
        }
    } catch (err) {
        console.error("System Link Failure:", err);
    }
}

function selectProject(id) {
    APP_STATE.activeProjectId = id;
    APP_STATE.activeFile = null;
    
    // Update Nav UI
    document.querySelectorAll('.nav-item').forEach(el => {
        el.classList.toggle('active', el.dataset.id === id);
    });
    
    // Show Dashboard
    APP_STATE.dom.emptyState.classList.add('hidden');
    APP_STATE.dom.dashboard.classList.remove('hidden');
    
    // Immediate Refresh
    refreshActiveProject();
}

async function refreshActiveProject() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;

    try {
        // Parallel Fetch
        const [status, snapshot, timeline, files] = await Promise.all([
            fetchJson(`/api/projects/${id}/status`),
            fetchJson(`/api/projects/${id}/snapshot`),
            fetchJson(`/api/projects/${id}/timeline`),
            fetchJson(`/api/projects/${id}/files`)
        ]);

        renderHUD(status, snapshot, id);
        renderIntel(snapshot);
        renderLogs(timeline);
        renderWorkers(snapshot, timeline);
        renderResult(snapshot);
        renderFiles(files);

    } catch (err) {
        console.warn("Telemetry Interrupted:", err);
    }
}

async function handleStartRun() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;
    
    const btn = APP_STATE.dom.startBtn;
    btn.disabled = true;
    btn.innerHTML = '<span class="btn-icon">⏳</span> INITIATING...';
    
    try {
        await fetch(`/api/projects/${id}/run`, { method: 'POST' });
    } catch (err) {
        alert("Launch Failed: " + err.message);
    } finally {
        // Button resets via status update in next poll loop
    }
}

async function loadFileContent(fileName) {
    if (!APP_STATE.activeProjectId) return;
    
    APP_STATE.activeFile = fileName;
    renderFiles(null); // Re-render list to update active state
    
    const container = APP_STATE.dom.filePreview;
    container.innerHTML = '<div class="preview-placeholder">LOADING_DATA_STREAM...</div>';
    
    try {
        const res = await fetch(`/api/projects/${APP_STATE.activeProjectId}/files/${fileName}`);
        if (!res.ok) throw new Error("File load error");
        
        const text = await res.text();
        try {
            const json = JSON.parse(text);
            container.innerHTML = `<pre class="json-viewer">${syntaxHighlight(json)}</pre>`;
        } catch {
            container.innerHTML = `<pre>${escapeHtml(text)}</pre>`;
        }
    } catch (err) {
        container.innerHTML = `<div class="preview-placeholder" style="color:var(--accent-err)">DATA_CORRUPTED</div>`;
    }
}

// --- Rendering Subsystems ---

function renderNav(projects) {
    const container = APP_STATE.dom.nav;
    container.innerHTML = projects.map(p => `
        <div class="nav-item ${p.id === APP_STATE.activeProjectId ? 'active' : ''}" 
             data-id="${p.id}"
             onclick="selectProject('${p.id}')">
            <span>${p.icon || '◈'}</span>
            <span>${p.name}</span>
        </div>
    `).join('');
    
    // Save metadata for HUD usage
    projects.forEach(p => {
        APP_STATE.projects[p.id] = p;
    });
}

function renderHUD(status, snapshot, id) {
    const p = APP_STATE.projects[id] || {};
    const dom = APP_STATE.dom;
    
    // Header
    dom.title.textContent = p.name || 'UNKNOWN_PROJECT';
    dom.id.textContent = `UUID: ${id}`;
    
    // Status
    const isRunning = status.status === 'running';
    dom.valStatus.textContent = isRunning ? 'ACTIVE_SEQUENCE' : 'STANDBY';
    dom.valStatus.style.color = isRunning ? 'var(--accent-main)' : 'var(--text-dim)';
    
    // Button State
    if (isRunning) {
        dom.startBtn.disabled = true;
        dom.startBtn.innerHTML = '<span class="btn-icon">⚙️</span> RUNNING...';
    } else {
        dom.startBtn.disabled = false;
        dom.startBtn.innerHTML = '<span class="btn-icon">⚡</span> INITIALIZE SEQUENCE';
    }
    
    // Metrics
    dom.valPhase.textContent = snapshot.phase || 'INITIALIZING';
    dom.valDepth.textContent = snapshot.currentDepth ?? 0;
    dom.valObjective.textContent = snapshot.microObjective || 'WAITING_FOR_DIRECTIVE...';
}

function renderIntel(snapshot) {
    const dom = APP_STATE.dom;
    
    // Pending Tasks
    const pending = snapshot.pendingChildren || [];
    dom.pendingList.innerHTML = pending.length 
        ? pending.map(t => `<span class="sys-tag">${t}</span>`).join('')
        : '<span class="sys-tag" style="opacity:0.3">NO_PENDING_TASKS</span>';
        
    // Steps Table
    const steps = snapshot.plannedSteps || [];
    if (steps.length === 0) {
        let emptyMsg = 'NO_PLAN_DATA';
        // Context-aware empty states
        if (snapshot.phase === 'PHASE_WAITING_FOR_PROPOSALS') {
            emptyMsg = 'GENERATING_STRATEGY...';
        } else if (snapshot.phase === 'PHASE_ASSESSING_COMPLEXITY') {
            emptyMsg = 'ANALYZING_COMPLEXITY...';
        }
        
        dom.stepTable.innerHTML = `<tr><td colspan="2" style="text-align:center; color:var(--accent-info); opacity:0.8; padding:20px 0">${emptyMsg}</td></tr>`;
    } else {
        dom.stepTable.innerHTML = steps.map(s => `
            <tr>
                <td style="color:var(--accent-info)">${s.stepId}</td>
                <td>${s.description}</td>
            </tr>
        `).join('');
    }
    
    // Votes Table
    const votes = snapshot.voteClusters || [];
    
    // Enhance Votes: Show raw count even if no clusters yet?
    // The backend snapshot.voteTallies might be useful here but voteClusters is usually better.
    
    if (votes.length === 0) {
        let emptyMsg = 'AWAITING_CONSENSUS';
        if (snapshot.phase === 'PHASE_WAITING_FOR_PROPOSALS') {
            emptyMsg = 'COLLECTING_VOTES...';
        }
        
        dom.stepTable.innerHTML = `<tr><td colspan="2" style="text-align:center; color:var(--accent-info); opacity:0.8; padding:20px 0">${emptyMsg}</td></tr>`;
        dom.voteTable.innerHTML = `<tr><td colspan="3" style="text-align:center; color:var(--text-dim); padding:20px 0">${emptyMsg}</td></tr>`;
    } else {
        dom.voteTable.innerHTML = votes.map(v => `
            <tr>
                <td style="font-size:0.8em" title="${v.id}">${v.id.substring(0,8)}...</td>
                <td style="color:var(--accent-main)">${v.votes} ${v.votes === 1 ? 'vote' : 'votes'}</td>
                <td>${escapeHtml(v.content)}</td>
            </tr>
        `).join('');
    }
    
    // Inject Micro Objectives Visualization (Was missing!)
    // We can prepend this to the Pending Tasks container or Steps Table container
    if (snapshot.microObjectives && snapshot.microObjectives.length > 0) {
        // Find or create micro container
        let microContainer = document.getElementById('micro-objectives-container');
        if (!microContainer) {
            // Creating it dynamically inside the first panel
            const panel = dom.pendingList.parentElement.parentElement; // panel > term-body > pendingList
            // Actually, let's put it above the table
            const stepsSection = dom.stepTable.closest('.terminal-window');
            if (stepsSection) {
                const body = stepsSection.querySelector('.term-body');
                microContainer = document.createElement('div');
                microContainer.id = 'micro-objectives-container';
                microContainer.className = 'micro-list';
                // Insert after pending list
                dom.pendingList.after(microContainer);
            }
        }
        
        if (microContainer) {
            const currentIdx = snapshot.microCursor || 0;
            microContainer.innerHTML = snapshot.microObjectives.map((obj, i) => {
                let statusIcon = '○';
                let statusClass = '';
                if (i < currentIdx) { statusIcon = '●'; statusClass = 'completed'; }
                else if (i === currentIdx) { statusIcon = '◎'; statusClass = 'active'; }
                
                return `<div class="micro-item ${statusClass}"><span class="micro-icon">${statusIcon}</span> ${obj}</div>`;
            }).join('');
        }
    }
}

function renderLogs(timeline) {
    const list = APP_STATE.dom.logList;
    // Take last 100 logs
    const logs = timeline.slice(-100);
    
    list.innerHTML = logs.map(evt => {
        const time = new Date(evt.timestamp).toLocaleTimeString('en-US', {hour12:false});
        const lvlClass = evt.level ? `log-level-${evt.level.toLowerCase()}` : '';
        return `
            <li class="log-item ${lvlClass}">
                <span class="log-time">[${time}]</span>
                <span class="log-src" title="${escapeHtml(evt.source)}">${evt.source}</span>
                <span class="log-msg">${escapeHtml(evt.message)}</span>
            </li>
        `;
    }).join('');
}

function renderWorkers(snapshot, timeline) {
    const workerIds = snapshot.workerIds || [];
    const grid = APP_STATE.dom.workerGrid;
    
    // Process timeline for worker streams AND agent logs
    const streams = {};
    workerIds.forEach(id => streams[id] = "");
    
    // Regex to capture Task/Consensus Agent IDs from standard logs
    // Consensus: "Consensus agent vote update: task={TaskId}..."
    // Task: "Task {TaskId} received goal..."
    // Task: "Task {TaskId} requesting..."
    // Task: "Task {TaskId} assigning child..."
    // Consensus: "Consensus agent starting session..."
    // Consensus: "Consensus agent publishing result..."
    const agentIdRegex = /(?:Task|task|Consensus agent.*?task|Consensus agent)[=\s]([a-zA-Z0-9-]+(?:-[a-zA-Z0-9]+)*)/;

    // Scan timeline
    timeline.forEach(evt => {
        if (!evt.message) return;
        
        // 1. High Priority: Worker Stream
        if (evt.message.startsWith("WORKER_STREAM|")) {
            const parts = evt.message.split("|", 4);
            if (parts.length >= 4) {
                 const wId = parts[1];
                 if (streams[wId] === undefined) streams[wId] = "";
                 
                 const chunk = evt.message.substring(parts[0].length + parts[1].length + parts[2].length + 3);
                 streams[wId] += chunk;
            }
        } 
        // 2. Lifecycle: Worker Done
        else if (evt.message.startsWith("WORKER_DONE|")) {
            const parts = evt.message.split("|", 4);
            if (parts.length >= 2) {
                const wId = parts[1];
                if (streams[wId] === undefined) streams[wId] = "";
                
                const info = parts.length >= 4 ? parts[3] : "";
                streams[wId] += `\n[COMPLETED] ${info}\n`;
            }
        }
        // 3. General Agent Logs (Consensus, Orchestrator)
        // Only capture if not already a worker stream to avoid duplication
        else {
            // Special case for Consensus Agent logs which might be formatted differently
            // "Consensus agent vote update: task=..." -> we want the TASK ID, but maybe we want a separate Consensus Node?
            // Actually, separating Consensus Node is better for "Who is doing what".
            
            // Heuristic: 
            // If message contains "Consensus agent", map to "CONSENSUS_NODE".
            // If message starts with "Task {ID}", map to "{ID}".
            
            let nodeId = null;
            
            if (evt.message.includes("Consensus agent")) {
                // Try to extract task ID to make it unique per task? Or just one global Consensus Node?
                // User wants "Consensus Agent" shown. Let's make a dedicated node.
                const taskMatch = evt.message.match(/task=([a-zA-Z0-9-]+)/);
                if (taskMatch) {
                    nodeId = `CONSENSUS:${taskMatch[1].substring(0, 8)}...`;
                } else {
                    nodeId = "CONSENSUS_COORDINATOR";
                }
            } else {
                const match = evt.message.match(/^Task ([a-zA-Z0-9-]+(?:-[a-zA-Z0-9]+)*)/);
                if (match) {
                    nodeId = match[1]; // The Task Agent ID
                }
            }

            if (nodeId) {
                if (streams[nodeId] === undefined) streams[nodeId] = "";
                // Timestamp for context
                const timeStr = new Date(evt.timestamp).toLocaleTimeString([], {hour12:false, hour:'2-digit', minute:'2-digit', second:'2-digit'});
                streams[nodeId] += `[${timeStr}] ${evt.message}\n`;
            }
        }
    });
    
    const allNodeIds = Object.keys(streams).sort();
    
    if (allNodeIds.length === 0) {
        grid.innerHTML = '<div style="color:var(--text-dim); grid-column:1/-1; text-align:center">NO_ACTIVE_NODES</div>';
        return;
    }
    
    grid.innerHTML = allNodeIds.map(id => `
        <div class="worker-node">
            <div class="node-head">NODE::${id}</div>
            <div class="node-log">${escapeHtml(streams[id] || "Standby...")}</div>
        </div>
    `).join('');
}

function renderResult(snapshot) {
    const container = APP_STATE.dom.finalResult;
    const content = snapshot.finalResult;
    
    if (!content) {
        container.innerHTML = '<div style="color:var(--text-dim); text-align:center; margin-top:20px">ARTIFACT_NOT_READY</div>';
        return;
    }
    
    if (window.marked) {
        container.innerHTML = marked.parse(content);
    } else {
        container.textContent = content;
    }
}

function renderFiles(files) {
    // If null passed (re-render only), we need cache, but we don't cache list yet.
    // So only render if files provided.
    if (!files) {
        // Re-highlight active
        const items = APP_STATE.dom.fileList.querySelectorAll('.file-item');
        items.forEach(el => {
            el.classList.toggle('active', el.dataset.name === APP_STATE.activeFile);
        });
        return;
    }

    const list = APP_STATE.dom.fileList;
    if (files.length === 0) {
        list.innerHTML = '<div style="padding:10px; color:var(--text-dim); font-size:0.8em">NO_DATA_ARTIFACTS</div>';
        return;
    }

    // Simple diff check to avoid flicker/scroll reset would be nice, but direct replace for now
    // unless user is scrolling.
    // Let's just rewrite it for now.
    
    list.innerHTML = files.map(f => `
        <div class="file-item ${f === APP_STATE.activeFile ? 'active' : ''}" 
             data-name="${f}"
             onclick="loadFileContent('${f}')">
            <span class="file-icon">📄</span>
            <span>${f}</span>
        </div>
    `).join('');
}

// --- Utilities ---

async function fetchJson(url) {
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
}

function escapeHtml(text) {
    if (!text) return '';
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

function syntaxHighlight(json) {
    if (typeof json != 'string') {
         json = JSON.stringify(json, undefined, 2);
    }
    json = json.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    return json.replace(/("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g, function (match) {
        var cls = 'json-number';
        if (/^"/.test(match)) {
            if (/:$/.test(match)) {
                cls = 'json-key';
            } else {
                cls = 'json-string';
            }
        } else if (/true|false/.test(match)) {
            cls = 'json-boolean';
        } else if (/null/.test(match)) {
            cls = 'json-null';
        }
        return '<span class="' + cls + '">' + match + '</span>';
    });
}

// Expose for debugging
window.APP = APP_STATE;
window.loadFileContent = loadFileContent; // Needs to be global for onclick? No, inline handler can't see module scope easily unless exposed.
// But here script is not module, so it's global anyway.
