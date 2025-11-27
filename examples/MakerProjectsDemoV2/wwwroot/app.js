// ============================================================
//  MAKER V2 Demo - Clean SSE-based Real-time UI
// ============================================================

// ============================================================
//  Theme Toggle System
// ============================================================

function initTheme() {
    const savedTheme = localStorage.getItem('maker-theme') || 'dark';
    applyTheme(savedTheme);
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'dark';
    const next = current === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    localStorage.setItem('maker-theme', next);
}

function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    
    const icon = document.getElementById('theme-icon');
    const label = document.getElementById('theme-label');
    
    if (icon && label) {
        if (theme === 'light') {
            icon.textContent = '☀️';
            label.textContent = 'LIGHT';
        } else {
            icon.textContent = '🌙';
            label.textContent = 'DARK';
        }
    }
}

// Initialize theme before DOM content loaded to prevent flash
initTheme();

// Expose to global
window.toggleTheme = toggleTheme;

// ============================================================
//  Application State
// ============================================================

const APP_STATE = {
    projects: {},
    activeProjectId: null,
    eventSource: null,
    eventSourceProjectId: null,  // Track which project the SSE is for
    proposals: {},       // taskId -> { content, success, error, promptTokens, completionTokens }
    tasks: [],           // { taskId, phase, message, depth }
    votingState: null,   // Current voting progress
    files: [],           // { category, name, path }
    activeFile: null,    // Currently selected file path
    artifacts: [],       // Stage results for ARTIFACTS view
    configTemplates: null,  // Config templates for project creation
    redFlags: [],        // Red flag events
    tokenStats: {        // Token statistics
        llmCalls: 0,
        promptTokens: 0,
        completionTokens: 0,
        totalTokens: 0
    },
    dom: {},
    // Project-level cache for state isolation
    projectCache: {}     // projectId -> { proposals, tasks, votingState, files, artifacts, redFlags, tokenStats, snapshot }
};

// ============================================================
//  Project State Cache Management
// ============================================================

function getEmptyProjectState() {
    return {
        proposals: {},
        tasks: [],
        votingState: null,
        files: [],
        activeFile: null,
        artifacts: [],
        redFlags: [],
        tokenStats: { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 },
        snapshot: null,
        lastUpdated: Date.now()
    };
}

function saveCurrentProjectState() {
    const pid = APP_STATE.activeProjectId;
    if (!pid) return;
    
    APP_STATE.projectCache[pid] = {
        proposals: { ...APP_STATE.proposals },
        tasks: [...APP_STATE.tasks],
        votingState: APP_STATE.votingState ? { ...APP_STATE.votingState } : null,
        files: [...APP_STATE.files],
        activeFile: APP_STATE.activeFile,
        artifacts: [...APP_STATE.artifacts],
        redFlags: [...APP_STATE.redFlags],
        tokenStats: { ...APP_STATE.tokenStats },
        snapshot: APP_STATE.projectCache[pid]?.snapshot || null,
        lastUpdated: Date.now()
    };
}

function loadProjectState(projectId) {
    const cached = APP_STATE.projectCache[projectId];
    if (cached) {
        APP_STATE.proposals = { ...cached.proposals };
        APP_STATE.tasks = [...cached.tasks];
        APP_STATE.votingState = cached.votingState ? { ...cached.votingState } : null;
        APP_STATE.files = [...cached.files];
        APP_STATE.activeFile = cached.activeFile;
        APP_STATE.artifacts = [...cached.artifacts];
        APP_STATE.redFlags = [...cached.redFlags];
        APP_STATE.tokenStats = { ...cached.tokenStats };
        return true;
    }
    return false;
}

function clearProjectState() {
    APP_STATE.proposals = {};
    APP_STATE.tasks = [];
    APP_STATE.votingState = null;
    APP_STATE.files = [];
    APP_STATE.activeFile = null;
    APP_STATE.artifacts = [];
    APP_STATE.redFlags = [];
    APP_STATE.tokenStats = { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 };
}

// ============================================================
//  Dynamic Project Creation (Zero-Code Config)
// ============================================================

function showCreateProjectModal() {
    document.getElementById('modal-overlay').classList.remove('hidden');
    // Load templates if not loaded
    if (!APP_STATE.configTemplates) {
        loadConfigTemplates();
    }
    // Load sample projects if not loaded
    if (!APP_STATE.sampleConfigs) {
        loadSampleProjects();
    }
}

function hideCreateProjectModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

function switchConfigMode(mode) {
    document.querySelectorAll('.modal-tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.config-mode').forEach(m => m.classList.remove('active'));
    
    document.querySelector(`.modal-tab:nth-child(${mode === 'simple' ? 1 : 2})`).classList.add('active');
    document.getElementById(`mode-${mode}`).classList.add('active');
}

async function loadConfigTemplates() {
    try {
        APP_STATE.configTemplates = await fetchJson('/api/projects/templates');
    } catch (err) {
        console.error("Failed to load templates:", err);
    }
}

// Load sample projects
async function loadSampleProjects() {
    try {
        const data = await fetchJson('/sample-configs.json');
        APP_STATE.sampleConfigs = data.samples;
        renderSampleList(data.samples);
    } catch (err) {
        console.error("Failed to load sample configs:", err);
    }
}

function renderSampleList(samples) {
    const container = document.getElementById('sample-list');
    if (!container) return;
    
    container.innerHTML = samples.map((s, idx) => `
        <div class="sample-card" onclick="loadSampleConfig(${idx})">
            <div class="sample-card-icon">${s.config.icon}</div>
            <div class="sample-card-name">${s.name}</div>
            <div class="sample-card-desc">${s.description}</div>
            <div class="sample-card-tags">
                ${s.testFocus.map(t => `<span class="sample-tag">${t}</span>`).join('')}
            </div>
        </div>
    `).join('');
}

function loadSampleConfig(idx) {
    const sample = APP_STATE.sampleConfigs[idx];
    if (!sample) return;
    
    const config = sample.config;
    
    // Populate simple mode fields
    document.getElementById('new-cfg-name').value = config.name;
    document.getElementById('new-cfg-icon').value = config.icon;
    document.getElementById('new-cfg-desc').value = config.description;
    document.getElementById('new-cfg-task').value = config.task;
    document.getElementById('new-cfg-reliability').value = config.reliability;
    document.getElementById('new-cfg-max-calls').value = config.maxTotalLlmCalls || 500;
    document.getElementById('new-cfg-max-tokens').value = config.maxTotalTokens || 2000000;
    document.getElementById('new-cfg-max-duration').value = config.maxDurationMinutes || 30;
    document.getElementById('new-cfg-mode').value = config.executionMode || 'Production';
    document.getElementById('new-cfg-granularity').value = config.granularity || 'Balanced';
    
    // Also populate JSON mode
    document.getElementById('new-cfg-json').value = JSON.stringify(config, null, 2);
    
    // Visual feedback
    document.querySelectorAll('.sample-card').forEach((card, i) => {
        card.style.borderColor = i === idx ? 'var(--accent-main)' : '';
    });
}

function loadTemplate(type) {
    if (!APP_STATE.configTemplates) {
        alert("Templates not loaded yet.");
        return;
    }
    const template = APP_STATE.configTemplates[type];
    if (template) {
        document.getElementById('new-cfg-json').value = JSON.stringify(template, null, 2);
    }
}

async function createProject() {
    const activeMode = document.querySelector('.config-mode.active').id;
    let config;
    
    if (activeMode === 'mode-simple') {
        // Build config from form fields
        config = {
            name: document.getElementById('new-cfg-name').value,
            icon: document.getElementById('new-cfg-icon').value,
            description: document.getElementById('new-cfg-desc').value,
            task: document.getElementById('new-cfg-task').value,
            reliability: document.getElementById('new-cfg-reliability').value,
            maxTotalLlmCalls: parseInt(document.getElementById('new-cfg-max-calls').value, 10),
            maxTotalTokens: parseInt(document.getElementById('new-cfg-max-tokens').value, 10),
            maxDurationMinutes: parseInt(document.getElementById('new-cfg-max-duration').value, 10),
            executionMode: document.getElementById('new-cfg-mode').value,
            granularity: document.getElementById('new-cfg-granularity').value
        };
    } else {
        // Parse JSON from textarea
        try {
            config = JSON.parse(document.getElementById('new-cfg-json').value);
        } catch (err) {
            alert("Invalid JSON: " + err.message);
            return;
        }
    }
    
    try {
        const result = await fetch('/api/projects/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(config)
        }).then(r => r.json());
        
        if (result.success) {
            hideCreateProjectModal();
            await loadProjectList();
            selectProject(result.projectId);
        } else {
            alert("Failed to create project: " + (result.error || "Unknown error"));
        }
    } catch (err) {
        alert("Error creating project: " + err.message);
    }
}

// --- Initialization ---

document.addEventListener("DOMContentLoaded", () => {
    initDomCache();
    initEventListeners();
    loadProjectList();
});

function initDomCache() {
    const q = (sel) => document.querySelector(sel);
    APP_STATE.dom = {
        nav: q('#project-nav'),
        dashboard: q('#dashboard'),
        emptyState: q('#empty-state'),
        title: q('#project-name'),
        id: q('#project-id'),
        startBtn: q('#btn-start'),
        valStatus: q('#val-status'),
        valPhase: q('#val-phase'),
        valDepth: q('#val-depth'),
        valObjective: q('#val-objective'),
        pendingTasks: q('#pending-tasks'),
        tableSteps: q('#table-steps'),
        tableVotes: q('#table-votes'),
        logList: q('#log-stream'),
        workerGrid: q('#worker-grid'),
        finalResult: q('#final-result'),
        fileList: q('#file-list'),
        filePreview: q('#file-preview'),
        tabs: document.querySelectorAll('.tab-btn'),
        views: document.querySelectorAll('.term-view'),
        // Config panel elements
        configDetails: q('#config-details'),
        configReliability: q('#config-reliability'),
        configTask: q('#config-task'),
        cfgReliability: q('#cfg-reliability'),
        cfgK: q('#cfg-k'),
        cfgN: q('#cfg-n'),
        cfgTimeout: q('#cfg-timeout'),
        cfgMaxCalls: q('#cfg-max-calls'),
        cfgMaxTokens: q('#cfg-max-tokens'),
        cfgMaxDuration: q('#cfg-max-duration'),
        cfgDecomposer: q('#cfg-decomposer'),
        cfgSolver: q('#cfg-solver'),
        cfgComposer: q('#cfg-composer'),
        cfgContext: q('#cfg-context'),
        configContextSection: q('#config-context-section'),
        // Execution mode
        cfgMode: q('#cfg-mode'),
        cfgGranularity: q('#cfg-granularity'),
        cfgHardDepth: q('#cfg-hard-depth'),
        // Advanced MAKER params
        cfgClustering: q('#cfg-clustering'),
        cfgSimilarity: q('#cfg-similarity'),
        cfgBaseTemp: q('#cfg-base-temp'),
        cfgTempVariance: q('#cfg-temp-variance'),
        cfgMultiProvider: q('#cfg-multi-provider'),
        cfgRedFlag: q('#cfg-red-flag'),
        // Token stats elements
        valLlmCalls: q('#val-llm-calls'),
        valPromptTokens: q('#val-prompt-tokens'),
        valCompletionTokens: q('#val-completion-tokens'),
        valTotalTokens: q('#val-total-tokens'),
        valRedFlags: q('#val-red-flags'),
        // Red flag panel
        redFlagPanel: q('#red-flag-panel'),
        redFlagList: q('#red-flag-list'),
        // Voting mode badge
        votingMode: q('#voting-mode')
    };
}

function initEventListeners() {
    APP_STATE.dom.tabs.forEach(btn => {
        btn.addEventListener('click', (e) => {
            APP_STATE.dom.tabs.forEach(b => b.classList.remove('active'));
            e.target.classList.add('active');
            const targetId = e.target.dataset.target;
            APP_STATE.dom.views.forEach(v => {
                v.classList.toggle('active', v.id === targetId);
            });
        });
    });
    APP_STATE.dom.startBtn.addEventListener('click', handleStartRun);
}

// --- Core Logic ---

async function loadProjectList() {
    try {
        const projects = await fetchJson('/api/projects');
        renderNav(projects);
        if (projects.length > 0) {
            selectProject(projects[0].id);
        }
    } catch (err) {
        console.error("Failed to load projects:", err);
    }
}

function selectProject(id) {
    // Skip if same project
    if (APP_STATE.activeProjectId === id) return;
    
    // Save current project state before switching
    saveCurrentProjectState();
    
    // Close SSE if switching projects
    if (APP_STATE.eventSource && APP_STATE.eventSourceProjectId !== id) {
        APP_STATE.eventSource.close();
        APP_STATE.eventSource = null;
        APP_STATE.eventSourceProjectId = null;
    }
    
    APP_STATE.activeProjectId = id;
    
    // Try to load cached state, otherwise clear
    if (!loadProjectState(id)) {
        clearProjectState();
    }
    
    // Update nav
    document.querySelectorAll('.nav-item').forEach(el => {
        el.classList.toggle('active', el.dataset.id === id);
    });
    
    // Show dashboard
    APP_STATE.dom.emptyState.classList.add('hidden');
    APP_STATE.dom.dashboard.classList.remove('hidden');
    
    // Update title
    const project = APP_STATE.projects[id];
    if (project) {
        APP_STATE.dom.title.textContent = project.name;
        APP_STATE.dom.id.textContent = `ID: ${id}`;
    }
    
    // Render cached data immediately (if available)
    renderTokenStats();
    renderRedFlags();
    renderTasks();
    renderWorkers();
    renderFiles();
    
    // Check if we have cached snapshot
    const cached = APP_STATE.projectCache[id];
    if (cached?.snapshot) {
        // Render cached snapshot immediately for instant feedback
        renderStatus({ status: cached.snapshot.status || 'idle' }, cached.snapshot);
        renderConfig(cached.snapshot.config);
        if (cached.snapshot.result?.content) {
            renderResultContent(cached.snapshot.result.content);
        }
    }
    
    // Fetch fresh data in background
    refreshProject();
}

async function refreshProject() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;

    try {
        const [status, snapshot, timeline, files] = await Promise.all([
            fetchJson(`/api/projects/${id}/status`),
            fetchJson(`/api/projects/${id}/snapshot`),
            fetchJson(`/api/projects/${id}/timeline`),
            fetchJson(`/api/projects/${id}/files`)
        ]);

        // Verify we're still on the same project (user might have switched during fetch)
        if (APP_STATE.activeProjectId !== id) {
            console.log(`[Refresh] Discarding stale data for ${id}, active is ${APP_STATE.activeProjectId}`);
            return;
        }

        APP_STATE.files = files || [];
        
        // Cache snapshot for this project
        if (!APP_STATE.projectCache[id]) {
            APP_STATE.projectCache[id] = getEmptyProjectState();
        }
        APP_STATE.projectCache[id].snapshot = snapshot;
        APP_STATE.projectCache[id].lastUpdated = Date.now();
        
        renderStatus(status, snapshot);
        renderLogs(timeline);
        renderResult(snapshot);
        renderTasks();
        renderVoting();
        renderWorkers();
        renderFiles();

    } catch (err) {
        console.warn("Refresh failed:", err);
    }
}

async function handleStartRun() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;
    
    APP_STATE.dom.startBtn.disabled = true;
    APP_STATE.dom.startBtn.textContent = 'INITIATING...';
    APP_STATE.proposals = {};
    APP_STATE.tasks = [];
    APP_STATE.votingState = null;
    APP_STATE.files = [];
    APP_STATE.artifacts = [];
    APP_STATE.redFlags = [];
    APP_STATE.tokenStats = { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 };
    
    // Clear displays
    renderTasks();
    renderVoting();
    renderWorkers();
    renderFiles();
    renderTokenStats();
    renderRedFlags();
    
    try {
        const res = await fetch(`/api/projects/${id}/run`, { method: 'POST' });
        const data = await res.json();
        
        if (data.success) {
            // Update status immediately
            APP_STATE.dom.valStatus.textContent = 'ACTIVE';
            APP_STATE.dom.valStatus.style.color = 'var(--accent-main)';
            APP_STATE.dom.startBtn.textContent = 'RUNNING...';
            startEventStream(id);
            // Refresh to get config after task starts
            setTimeout(() => refreshProject(), 300);
        } else {
            APP_STATE.dom.startBtn.disabled = false;
            APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
            alert(data.error || 'Failed to start');
        }
    } catch (err) {
        APP_STATE.dom.startBtn.disabled = false;
        APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
        console.error("Start failed:", err);
    }
}

// --- SSE Real-time Streaming ---

function startEventStream(projectId) {
    // Close existing connection if for different project
    if (APP_STATE.eventSource) {
        APP_STATE.eventSource.close();
        APP_STATE.eventSource = null;
    }
    
    APP_STATE.eventSourceProjectId = projectId;
    APP_STATE.eventSource = new EventSource(`/api/projects/${projectId}/events`);
    
    APP_STATE.eventSource.onmessage = (e) => {
        try {
            const event = JSON.parse(e.data);
            
            // CRITICAL: Only process events for the currently active project
            // This prevents data corruption when user switches projects
            if (APP_STATE.activeProjectId !== projectId) {
                console.log(`[SSE] Ignoring event for inactive project ${projectId}, active is ${APP_STATE.activeProjectId}`);
                return;
            }
            
            handleSSEEvent(event, projectId);
        } catch (err) {
            console.warn("Failed to parse SSE event:", err);
        }
    };
    
    APP_STATE.eventSource.onerror = () => {
        console.log("SSE connection closed for project:", projectId);
        if (APP_STATE.eventSource) {
            APP_STATE.eventSource.close();
            APP_STATE.eventSource = null;
        }
        APP_STATE.eventSourceProjectId = null;
        
        // Only refresh if this is still the active project
        if (APP_STATE.activeProjectId === projectId) {
            refreshProject();
            APP_STATE.dom.startBtn.disabled = false;
            APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
        }
    };
}

function handleSSEEvent(event, projectId) {
    // Double-check project isolation (belt and suspenders)
    if (projectId && APP_STATE.activeProjectId !== projectId) {
        return;
    }
    
    switch (event.type) {
        case 'progress':
            // Update status to ACTIVE when receiving progress
            APP_STATE.dom.valStatus.textContent = 'ACTIVE';
            APP_STATE.dom.valStatus.style.color = 'var(--accent-main)';
            
            // Update HUD (always visible)
            APP_STATE.dom.valPhase.textContent = event.phase || '-';
            APP_STATE.dom.valDepth.textContent = event.depth ?? 0;
            APP_STATE.dom.valObjective.textContent = event.message || '-';
            
            // Update task progress (STRATEGIC_PLANNING)
            updateTaskProgress(event);
            
            // Handle RedFlag phase
            if (event.phase === 'RedFlag') {
                addRedFlag(event.taskId, event.message);
                addLogEntry('RED_FLAG', event.message);
            }
            // Only add to log for major phase changes
            else if (['Starting', 'Decomposing', 'Solving', 'Composing', 'Completed', 'Failed', 'Assessing'].includes(event.phase)) {
                addLogEntry(event.phase, event.message);
            }
            break;
            
        case 'voting':
            // Update voting state (CONSENSUS_PROTOCOL)
            APP_STATE.votingState = {
                taskId: event.taskId,
                type: event.votingType,
                round: event.round,
                totalVotes: event.totalVotes,
                votesNeeded: event.votesNeeded,
                leaderVotes: event.leaderVotes,
                runnerUpVotes: event.runnerUpVotes,
                clusterCount: event.clusterCount,
                usedSemanticClustering: event.usedSemanticClustering,
                earlyTermination: event.earlyTermination
            };
            renderVoting();
            
            // Log early termination events
            if (event.earlyTermination) {
                addLogEntry('CONSENSUS', `Early termination! Saved ${event.savedProposals || '?'} LLM calls`);
            }
            break;
            
        case 'proposal':
            // Update proposals (SYSTEM_NODES)
            APP_STATE.proposals[event.taskId] = {
                content: event.content || '',
                success: event.success,
                error: event.message,
                promptTokens: event.promptTokens || 0,
                completionTokens: event.completionTokens || 0
            };
            
            // Update token stats
            if (event.promptTokens || event.completionTokens) {
                APP_STATE.tokenStats.llmCalls++;
                APP_STATE.tokenStats.promptTokens += event.promptTokens || 0;
                APP_STATE.tokenStats.completionTokens += event.completionTokens || 0;
                APP_STATE.tokenStats.totalTokens = APP_STATE.tokenStats.promptTokens + APP_STATE.tokenStats.completionTokens;
                renderTokenStats();
            }
            
            renderWorkers();
            break;
            
        case 'file':
            // Update files (DATA_CORE)
            APP_STATE.files.push({
                category: event.phase,
                name: event.taskId,
                path: `${event.phase}/${event.taskId}`
            });
            renderFiles();
            // Only log artifact files, not proposals/votes
            if (event.phase === 'artifacts') {
                addLogEntry('ARTIFACT', event.taskId);
            }
            break;
            
        case 'result':
            // Final result (ARTIFACTS)
            APP_STATE.dom.valStatus.textContent = event.success ? 'COMPLETED' : 'FAILED';
            APP_STATE.dom.valStatus.style.color = event.success ? 'var(--accent-main)' : 'var(--accent-warn)';
            
            // Update final token stats from result
            if (event.totalTokens || event.promptTokens || event.completionTokens) {
                APP_STATE.tokenStats.totalTokens = event.totalTokens || APP_STATE.tokenStats.totalTokens;
                APP_STATE.tokenStats.promptTokens = event.promptTokens || APP_STATE.tokenStats.promptTokens;
                APP_STATE.tokenStats.completionTokens = event.completionTokens || APP_STATE.tokenStats.completionTokens;
                APP_STATE.tokenStats.llmCalls = event.totalLlmCalls || APP_STATE.tokenStats.llmCalls;
                renderTokenStats();
            }
            
            if (event.content) {
                renderResultContent(event.content);
            }
            addLogEntry(event.success ? 'COMPLETED' : 'FAILED', `Execution finished (${APP_STATE.tokenStats.totalTokens.toLocaleString()} tokens)`);
            
            // Save final state to cache after completion
            saveCurrentProjectState();
            
            setTimeout(() => refreshProject(), 500);
            break;
            
        case 'redFlag':
            // Direct red flag event
            addRedFlag(event.taskId, event.reason);
            break;
            
        case 'error':
            addLogEntry('ERROR', event.message);
            break;
    }
}

function updateTaskProgress(event) {
    let task = APP_STATE.tasks.find(t => t.taskId === event.taskId);
    if (!task) {
        task = { taskId: event.taskId, phases: [] };
        APP_STATE.tasks.push(task);
    }
    
    if (!task.phases.includes(event.phase)) {
        task.phases.push(event.phase);
    }
    task.currentPhase = event.phase;
    task.message = event.message;
    task.depth = event.depth;
    
    renderTasks();
}

// --- Rendering ---

function renderNav(projects) {
    APP_STATE.dom.nav.innerHTML = projects.map(p => {
        APP_STATE.projects[p.id] = p;
        return `
            <div class="nav-item" data-id="${p.id}" onclick="selectProject('${p.id}')">
                <span class="nav-icon">${p.icon}</span>
                <span>${p.name}</span>
            </div>
        `;
    }).join('');
}

function renderStatus(status, snapshot) {
    const isRunning = status.status === 'running';
    APP_STATE.dom.valStatus.textContent = isRunning ? 'ACTIVE' : (status.status || 'STANDBY').toUpperCase();
    APP_STATE.dom.valStatus.style.color = isRunning ? 'var(--accent-main)' : 'var(--text-dim)';
    
    if (isRunning) {
        APP_STATE.dom.startBtn.disabled = true;
        APP_STATE.dom.startBtn.textContent = 'RUNNING...';
        if (!APP_STATE.eventSource) {
            startEventStream(APP_STATE.activeProjectId);
        }
    } else {
        APP_STATE.dom.startBtn.disabled = false;
        APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
    }
    
    // Render config if available
    if (snapshot?.config) {
        renderConfig(snapshot.config);
    }
}

function renderConfig(config) {
    if (!config) return;
    
    // Update config badge
    APP_STATE.dom.configReliability.textContent = config.reliability;
    
    // Update task description
    APP_STATE.dom.configTask.textContent = config.task || '-';
    
    // Update config grid values
    APP_STATE.dom.cfgReliability.textContent = config.reliability;
    APP_STATE.dom.cfgK.textContent = config.consensusK;
    APP_STATE.dom.cfgN.textContent = config.samplesPerRound;
    APP_STATE.dom.cfgTimeout.textContent = `${config.stepTimeoutSeconds}s`;
    
    // Budget-based limits
    APP_STATE.dom.cfgMaxCalls.textContent = config.maxTotalLlmCalls || '-';
    APP_STATE.dom.cfgMaxTokens.textContent = config.maxTotalTokens ? `${(config.maxTotalTokens / 1000).toFixed(0)}K` : '-';
    APP_STATE.dom.cfgMaxDuration.textContent = config.maxDurationMinutes ? `${config.maxDurationMinutes}min` : '-';
    
    // Execution mode
    const modeText = config.executionMode || 'Production';
    APP_STATE.dom.cfgMode.textContent = modeText;
    APP_STATE.dom.cfgMode.style.color = modeText === 'Academic' ? 'var(--accent-warn)' : 'var(--accent-main)';
    APP_STATE.dom.cfgGranularity.textContent = config.granularity || 'Balanced';
    APP_STATE.dom.cfgHardDepth.textContent = config.hardDepthCap || '50';
    
    // Update strategy types
    APP_STATE.dom.cfgDecomposer.textContent = config.decomposerType || 'Default';
    APP_STATE.dom.cfgSolver.textContent = config.solverType || 'Default';
    APP_STATE.dom.cfgComposer.textContent = config.composerType || 'Default';
    
    // Update advanced MAKER parameters
    APP_STATE.dom.cfgClustering.textContent = config.clusteringMethod || 'exact';
    APP_STATE.dom.cfgSimilarity.textContent = config.semanticSimilarityThreshold?.toFixed(2) || '0.85';
    APP_STATE.dom.cfgBaseTemp.textContent = config.baseTemperature?.toFixed(2) || '0.30';
    APP_STATE.dom.cfgTempVariance.textContent = `±${config.temperatureVariance?.toFixed(2) || '0.10'}`;
    APP_STATE.dom.cfgMultiProvider.textContent = config.useMultipleProviders ? 'YES' : 'NO';
    APP_STATE.dom.cfgRedFlag.textContent = config.redFlagThreshold || '3';
    
    // Update context if available
    if (config.context && Object.keys(config.context).length > 0) {
        APP_STATE.dom.configContextSection.style.display = 'block';
        APP_STATE.dom.cfgContext.innerHTML = Object.entries(config.context)
            .map(([k, v]) => `<span class="context-tag"><span class="key">${escapeHtml(k)}:</span> <span class="val">${escapeHtml(v)}</span></span>`)
            .join('');
    } else {
        APP_STATE.dom.configContextSection.style.display = 'none';
    }
}

function renderTasks() {
    const tasks = APP_STATE.tasks;
    
    const pending = tasks.filter(t => t.currentPhase !== 'Completed' && t.currentPhase !== 'Failed');
    APP_STATE.dom.pendingTasks.innerHTML = pending.length === 0 
        ? '<span style="color:var(--text-dim)">NO_PENDING</span>'
        : pending.map(t => `<span class="tag phase-${t.currentPhase?.toLowerCase()}">[D${t.depth}] ${t.taskId.substring(0,8)}...</span>`).join('');
    
    APP_STATE.dom.tableSteps.innerHTML = tasks.length === 0
        ? '<tr><td colspan="2" style="color:var(--text-dim)">AWAITING_DIRECTIVES</td></tr>'
        : tasks.map(t => {
            const phases = t.phases.map(p => {
                const isCurrent = p === t.currentPhase;
                return `<span class="phase-tag ${isCurrent ? 'current' : ''}">${p}</span>`;
            }).join(' → ');
            return `
                <tr>
                    <td><code>[D${t.depth}] ${t.taskId.substring(0,8)}</code></td>
                    <td>${phases}<br><small style="color:var(--text-dim)">${escapeHtml(t.message || '')}</small></td>
                </tr>
            `;
        }).join('');
}

function renderVoting() {
    const v = APP_STATE.votingState;
    
    // Update voting mode badge
    if (APP_STATE.dom.votingMode) {
        APP_STATE.dom.votingMode.textContent = v?.usedSemanticClustering ? 'SEMANTIC_CLUSTERING' : 'STREAMING_RACE';
    }
    
    if (!v) {
        APP_STATE.dom.tableVotes.innerHTML = '<tr><td colspan="3" style="color:var(--text-dim)">NO_ACTIVE_VOTE</td></tr>';
        return;
    }
    
    const gap = v.leaderVotes - v.runnerUpVotes;
    const progress = v.votesNeeded > 0 ? Math.round((gap / v.votesNeeded) * 100) : 0;
    const earlyBadge = v.earlyTermination ? '<span class="early-termination">EARLY_TERMINATION</span>' : '';
    
    APP_STATE.dom.tableVotes.innerHTML = `
        <tr>
            <td><code>${v.taskId.substring(0,8)}</code></td>
            <td>
                <div class="vote-bar">
                    <div class="vote-fill" style="width:${Math.min(100, Math.max(0, progress))}%"></div>
                </div>
                <small>Leader: ${v.leaderVotes} | RunnerUp: ${v.runnerUpVotes} | Gap: ${gap}/${v.votesNeeded}</small>
                ${v.clusterCount ? `<br><small>Clusters: ${v.clusterCount}</small>` : ''}
            </td>
            <td>
                <span class="vote-type">${v.type}</span>
                <span class="vote-round">R${v.round}</span>
                ${earlyBadge}
            </td>
        </tr>
    `;
}

function renderTokenStats() {
    const stats = APP_STATE.tokenStats;
    
    if (APP_STATE.dom.valLlmCalls) {
        APP_STATE.dom.valLlmCalls.textContent = stats.llmCalls.toLocaleString();
    }
    if (APP_STATE.dom.valPromptTokens) {
        APP_STATE.dom.valPromptTokens.textContent = stats.promptTokens.toLocaleString();
    }
    if (APP_STATE.dom.valCompletionTokens) {
        APP_STATE.dom.valCompletionTokens.textContent = stats.completionTokens.toLocaleString();
    }
    if (APP_STATE.dom.valTotalTokens) {
        APP_STATE.dom.valTotalTokens.textContent = stats.totalTokens.toLocaleString();
    }
    if (APP_STATE.dom.valRedFlags) {
        APP_STATE.dom.valRedFlags.textContent = APP_STATE.redFlags.length;
    }
}

function addRedFlag(taskId, reason) {
    APP_STATE.redFlags.push({
        taskId: taskId,
        reason: reason,
        timestamp: new Date()
    });
    renderRedFlags();
}

function renderRedFlags() {
    const flags = APP_STATE.redFlags;
    
    // Update counter
    if (APP_STATE.dom.valRedFlags) {
        APP_STATE.dom.valRedFlags.textContent = flags.length;
    }
    
    // Show/hide panel
    if (APP_STATE.dom.redFlagPanel) {
        APP_STATE.dom.redFlagPanel.style.display = flags.length > 0 ? 'flex' : 'none';
    }
    
    if (APP_STATE.dom.redFlagList && flags.length > 0) {
        APP_STATE.dom.redFlagList.innerHTML = flags.slice(-10).map(rf => {
            const time = rf.timestamp.toLocaleTimeString([], {hour12:false});
            return `<li><span class="rf-time">${time}</span><span class="rf-task">[${rf.taskId?.substring(0,8) || 'UNKNOWN'}]</span>${escapeHtml(rf.reason)}</li>`;
        }).join('');
    }
}

function renderLogs(timeline) {
    if (!timeline || timeline.length === 0) {
        APP_STATE.dom.logList.innerHTML = '<div style="color:var(--text-dim)">NO_LOGS</div>';
        return;
    }
    
    APP_STATE.dom.logList.innerHTML = timeline.slice(-50).map(e => {
        const time = new Date(e.timestamp).toLocaleTimeString([], {hour12:false});
        return `<div class="log-entry"><span class="log-time">${time}</span> <span class="log-phase">[${e.phase}]</span> ${escapeHtml(e.message)}</div>`;
    }).join('');
    
    APP_STATE.dom.logList.scrollTop = APP_STATE.dom.logList.scrollHeight;
}

function addLogEntry(phase, message) {
    const time = new Date().toLocaleTimeString([], {hour12:false});
    const entry = document.createElement('div');
    entry.className = 'log-entry';
    entry.innerHTML = `<span class="log-time">${time}</span> <span class="log-phase">[${phase}]</span> ${escapeHtml(message)}`;
    APP_STATE.dom.logList.appendChild(entry);
    APP_STATE.dom.logList.scrollTop = APP_STATE.dom.logList.scrollHeight;
}

function renderWorkers() {
    const proposals = APP_STATE.proposals;
    const ids = Object.keys(proposals).sort();
    
    if (ids.length === 0) {
        APP_STATE.dom.workerGrid.innerHTML = '<div style="color:var(--text-dim); text-align:center">NO_ACTIVE_NODES</div>';
        return;
    }
    
    APP_STATE.dom.workerGrid.innerHTML = ids.map(id => {
        const p = proposals[id];
        const statusIcon = p.success ? '✓' : '✗';
        const statusClass = p.success ? 'success' : 'error';
        const content = p.content || p.error || 'No content';
        const tokens = (p.promptTokens || p.completionTokens) 
            ? `<span class="node-tokens">${p.promptTokens || 0}+${p.completionTokens || 0}</span>` 
            : '';
        
        return `
            <div class="worker-node">
                <div class="node-head">
                    <span class="node-status ${statusClass}">${statusIcon}</span>
                    NODE::${id.substring(0, 12)}
                    ${tokens}
                </div>
                <div class="node-log">${escapeHtml(content)}</div>
            </div>
        `;
    }).join('');
}

function renderFiles() {
    const files = APP_STATE.files;
    
    if (!files || files.length === 0) {
        APP_STATE.dom.fileList.innerHTML = '<div style="padding:10px; color:var(--text-dim)">NO_FILES_YET</div>';
        APP_STATE.dom.filePreview.innerHTML = '<div class="preview-placeholder">[ SELECT_FILE ]</div>';
        return;
    }
    
    // Group by category
    const grouped = {};
    files.forEach(f => {
        if (!grouped[f.category]) grouped[f.category] = [];
        grouped[f.category].push(f);
    });
    
    const categoryIcons = {
        'proposals': '📝',
        'votes': '🗳️',
        'consensus': '📊',
        'artifacts': '📦'
    };
    
    let html = '';
    for (const [category, categoryFiles] of Object.entries(grouped).sort()) {
        html += `<div class="file-category">${categoryIcons[category] || '📄'} ${category.toUpperCase()}</div>`;
        categoryFiles.forEach(f => {
            const isActive = APP_STATE.activeFile === f.path;
            html += `
                <div class="file-item ${isActive ? 'active' : ''}" 
                     data-path="${f.path}"
                     onclick="loadFile('${f.category}', '${f.name}')">
                    <span class="file-icon">📄</span>
                    <span>${f.name}</span>
                </div>
            `;
        });
    }
    
    APP_STATE.dom.fileList.innerHTML = html;
}

async function loadFile(category, name) {
    const id = APP_STATE.activeProjectId;
    if (!id) return;
    
    APP_STATE.activeFile = `${category}/${name}`;
    renderFiles();
    
    APP_STATE.dom.filePreview.innerHTML = '<div class="preview-placeholder">LOADING...</div>';
    
    try {
        const res = await fetch(`/api/projects/${id}/files/${category}/${name}`);
        if (!res.ok) throw new Error('Failed to load file');
        
        const content = await res.text();
        
        // Use marked.js for proper markdown rendering (including tables)
        const html = typeof marked !== 'undefined' 
            ? marked.parse(content) 
            : escapeHtml(content);
        
        APP_STATE.dom.filePreview.innerHTML = `<div class="file-content markdown-body">${html}</div>`;
        
    } catch (err) {
        APP_STATE.dom.filePreview.innerHTML = `<div class="preview-placeholder">ERROR: ${err.message}</div>`;
    }
}

function renderResult(snapshot) {
    const content = snapshot.finalResult || snapshot.result;
    if (!content) {
        APP_STATE.dom.finalResult.innerHTML = '<div style="color:var(--text-dim); text-align:center; margin-top:20px">AWAITING_ARTIFACTS</div>';
        return;
    }
    renderResultContent(content);
}

function renderResultContent(content) {
    // Use marked.js for proper markdown rendering
    const html = typeof marked !== 'undefined' 
        ? marked.parse(content) 
        : escapeHtml(content);
    
    APP_STATE.dom.finalResult.innerHTML = `<div class="result-content markdown-body">${html}</div>`;
}

// --- Utilities ---

async function fetchJson(url) {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

// Expose to global for onclick handlers
window.selectProject = selectProject;
window.loadFile = loadFile;
