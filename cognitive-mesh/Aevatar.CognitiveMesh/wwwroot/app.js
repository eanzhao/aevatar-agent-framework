// ═══════════════════════════════════════════════════════════════════════════
//  COGNITIVE MESH - Frontend Application
//  Unified interface for multiple reasoning strategies
// ═══════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────────
//  State
// ─────────────────────────────────────────────────────────────────────────────
let state = {
    strategies: [],
    projects: [],
    currentProject: null,
    currentRun: null,
    eventSource: null,
    workers: new Map(),
    files: []
};

// ─────────────────────────────────────────────────────────────────────────────
//  Initialization
// ─────────────────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
    initTheme();
    await loadStrategies();
    await loadProjects();
    setupTabs();
});

// ─────────────────────────────────────────────────────────────────────────────
//  Theme Management
// ─────────────────────────────────────────────────────────────────────────────
function initTheme() {
    const savedTheme = localStorage.getItem('cognitive-mesh-theme') || 'dark';
    applyTheme(savedTheme, false);
}

function toggleTheme() {
    const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
    const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
    applyTheme(newTheme, true);
}

function applyTheme(theme, animate) {
    if (animate) {
        document.documentElement.classList.add('theme-transitioning');
        setTimeout(() => {
            document.documentElement.classList.remove('theme-transitioning');
        }, 300);
    }
    
    if (theme === 'light') {
        document.documentElement.setAttribute('data-theme', 'light');
    } else {
        document.documentElement.removeAttribute('data-theme');
    }
    
    // Update toggle UI
    const icon = document.getElementById('theme-icon');
    const label = document.getElementById('theme-label');
    
    if (icon) icon.textContent = theme === 'light' ? '☀️' : '🌙';
    if (label) label.textContent = theme === 'light' ? 'LIGHT' : 'DARK';
    
    // Persist preference
    localStorage.setItem('cognitive-mesh-theme', theme);
}

async function loadStrategies() {
    try {
        const res = await fetch('/api/strategies');
        state.strategies = await res.json();
        
        // Update filter dropdown
        const filter = document.getElementById('strategy-filter');
        state.strategies.forEach(s => {
            const option = document.createElement('option');
            option.value = s.kind;
            option.textContent = s.displayName;
            filter.appendChild(option);
        });
        
        // Update strategy count
        document.getElementById('strategy-count').textContent = state.strategies.length;
        
        // Render strategy cards in empty state
        renderStrategyCards();
    } catch (err) {
        console.error('Failed to load strategies:', err);
    }
}

async function loadProjects() {
    try {
        const res = await fetch('/api/projects');
        state.projects = await res.json();
        renderProjects();
    } catch (err) {
        console.error('Failed to load projects:', err);
    }
}

function renderStrategyCards() {
    const container = document.getElementById('strategy-cards');
    container.innerHTML = state.strategies.map(s => `
        <div class="strategy-card" onclick="showCreateModalWithStrategy('${s.kind}')">
            <h3>${s.displayName}</h3>
            <p>${s.description}</p>
        </div>
    `).join('');
}

function renderProjects() {
    const nav = document.getElementById('project-nav');
    const filter = document.getElementById('strategy-filter').value;
    
    const filtered = filter === 'all' 
        ? state.projects 
        : state.projects.filter(p => p.strategy === filter);
    
    nav.innerHTML = filtered.map(p => {
        const strategyClass = p.strategy.toLowerCase().replace('combinational', '');
        return `
            <div class="nav-item ${state.currentProject?.id === p.id ? 'active' : ''}" 
                 onclick="selectProject('${p.id}')">
                <span class="nav-item-icon">${p.icon}</span>
                <span class="nav-item-text">${p.name}</span>
                <span class="nav-item-strategy ${strategyClass}">${p.strategy.replace('Combinational', '')}</span>
            </div>
        `;
    }).join('');
}

function filterProjects() {
    renderProjects();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Project Selection
// ─────────────────────────────────────────────────────────────────────────────
async function selectProject(id) {
    const project = state.projects.find(p => p.id === id);
    if (!project) return;
    
    state.currentProject = project;
    
    // Update UI
    document.getElementById('empty-state').classList.add('hidden');
    document.getElementById('dashboard').classList.remove('hidden');
    
    // Update header
    document.getElementById('project-name').textContent = project.name;
    document.getElementById('project-id').textContent = `ID: ${project.id}`;
    
    const strategyBadge = document.getElementById('project-strategy');
    strategyBadge.textContent = project.strategyDisplayName;
    strategyBadge.style.background = project.strategy.includes('Uot') 
        ? 'var(--strategy-uot)' 
        : 'var(--strategy-maker)';
    
    // Update panel titles based on strategy
    if (project.strategy.includes('Uot')) {
        document.getElementById('panel-tasks-title').textContent = '>> ANALOGIES';
        document.getElementById('panel-consensus-title').textContent = '>> CANDIDATES';
    } else {
        document.getElementById('panel-tasks-title').textContent = '>> STRATEGIC_PLANNING';
        document.getElementById('panel-consensus-title').textContent = '>> CONSENSUS_PROTOCOL';
    }
    
    // Clear previous state
    clearRunState();
    
    // Load current status
    await refreshStatus();
    
    // Re-render project list to show active
    renderProjects();
}

async function refreshStatus() {
    if (!state.currentProject) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/status`);
        const status = await res.json();
        
        updateStatusChips(status);
        
        if (status.status === 'running' && !state.eventSource) {
            connectSSE();
        }
        
        // Load snapshot if completed
        if (status.status === 'completed' || status.status === 'failed') {
            await loadSnapshot();
        }
    } catch (err) {
        console.error('Failed to refresh status:', err);
    }
}

function updateStatusChips(status) {
    const chipStatus = document.getElementById('chip-status');
    chipStatus.textContent = status.status?.toUpperCase() || 'IDLE';
    chipStatus.className = `status-chip ${status.status || ''}`;
    
    document.getElementById('chip-phase').textContent = status.phase || '-';
    document.getElementById('chip-depth').textContent = `D:${status.depth || 0}`;
    document.getElementById('chip-tokens').textContent = `${formatNumber(status.totalTokens || 0)} tok`;
    document.getElementById('chip-calls').textContent = `${status.llmCalls || 0} calls`;
    
    // Progress bar
    const progress = status.progress || 0;
    const progressContainer = document.getElementById('progress-container');
    const progressBar = document.getElementById('progress-bar');
    const progressLabel = document.getElementById('progress-label');
    
    if (status.status === 'running') {
        progressContainer.style.display = 'block';
        progressBar.style.width = `${progress * 100}%`;
        progressLabel.textContent = `${Math.round(progress * 100)}%`;
    } else {
        progressContainer.style.display = 'none';
    }
}

async function loadSnapshot() {
    if (!state.currentProject) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/snapshot`);
        const snapshot = await res.json();
        
        if (snapshot.result) {
            document.getElementById('final-result').innerHTML = 
                typeof marked !== 'undefined' ? marked.parse(snapshot.result) : snapshot.result;
        }
        
        // Load files
        await loadFiles();
        
        // Load timeline
        await loadTimeline();
    } catch (err) {
        console.error('Failed to load snapshot:', err);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Run Execution
// ─────────────────────────────────────────────────────────────────────────────
async function startRun() {
    if (!state.currentProject) return;
    
    const btn = document.getElementById('btn-start');
    btn.disabled = true;
    
    clearRunState();
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/run`, {
            method: 'POST'
        });
        const result = await res.json();
        
        if (result.success) {
            state.currentRun = result.runId;
            connectSSE();
        } else {
            alert('Failed to start: ' + result.error);
        }
    } catch (err) {
        console.error('Failed to start run:', err);
        alert('Failed to start run');
    } finally {
        btn.disabled = false;
    }
}

function clearRunState() {
    // Close existing SSE connection
    if (state.eventSource) {
        state.eventSource.close();
        state.eventSource = null;
    }
    
    // Clear UI
    document.getElementById('log-stream').innerHTML = '';
    document.getElementById('table-tasks').innerHTML = '';
    document.getElementById('table-consensus').innerHTML = '';
    document.getElementById('worker-grid').innerHTML = '';
    document.getElementById('file-list').innerHTML = '';
    document.getElementById('file-preview').innerHTML = '<div class="preview-placeholder">[ SELECT_FILE ]</div>';
    document.getElementById('final-result').innerHTML = '';
    
    state.workers.clear();
    state.files = [];
}

// ─────────────────────────────────────────────────────────────────────────────
//  SSE Event Handling
// ─────────────────────────────────────────────────────────────────────────────
function connectSSE() {
    if (!state.currentProject) return;
    
    const url = `/api/projects/${state.currentProject.id}/events`;
    state.eventSource = new EventSource(url);
    
    state.eventSource.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);
            handleEvent(data);
        } catch (err) {
            console.error('Failed to parse SSE event:', err);
        }
    };
    
    state.eventSource.onerror = () => {
        console.log('SSE connection closed');
        state.eventSource?.close();
        state.eventSource = null;
        refreshStatus();
    };
}

function handleEvent(evt) {
    switch (evt.type) {
        case 'progress':
            handleProgress(evt);
            break;
        case 'voting':
            handleVoting(evt);
            break;
        case 'proposal':
            handleProposal(evt);
            break;
        case 'streaming':
            handleStreaming(evt);
            break;
        case 'result':
            handleResult(evt);
            break;
        case 'error':
            handleError(evt);
            break;
        case 'file':
            handleFile(evt);
            break;
    }
}

function handleProgress(evt) {
    // Update status
    updateStatusChips({
        status: 'running',
        phase: evt.phase,
        depth: evt.depth,
        progress: evt.progressPercent
    });
    
    // Add to log
    addLog(evt.phase, evt.message);
    
    // Add to task table if it's a new task
    if (evt.taskId && evt.message?.includes('task')) {
        addTask(evt.taskId, evt.message);
    }
}

function handleVoting(evt) {
    const tbody = document.getElementById('table-consensus');
    const row = document.createElement('tr');
    
    const gap = evt.leaderVotes - evt.runnerUpVotes;
    const status = gap >= evt.votesNeeded ? '✓' : `${gap}/${evt.votesNeeded}`;
    
    row.innerHTML = `
        <td>${evt.votingType}</td>
        <td>${evt.leaderVotes}/${evt.totalVotes}</td>
        <td>${status} (${evt.clusterCount} clusters)</td>
    `;
    
    tbody.appendChild(row);
    
    // Update mode badge
    document.getElementById('consensus-mode').textContent = 
        evt.usedSemanticClustering ? 'SEMANTIC' : 'HASH';
}

function handleProposal(evt) {
    // Create or update worker card
    const workerId = evt.taskId;
    
    if (!state.workers.has(workerId)) {
        createWorkerCard(workerId, evt.providerName);
    }
    
    const card = document.getElementById(`worker-${workerId.replace(/[^a-zA-Z0-9]/g, '-')}`);
    if (card) {
        const content = card.querySelector('.worker-content');
        const status = card.querySelector('.worker-status');
        
        content.textContent = evt.content || evt.error || '';
        status.textContent = evt.success ? 'COMPLETED' : 'FAILED';
        status.className = `worker-status ${evt.success ? 'completed' : 'failed'}`;
    }
}

function handleStreaming(evt) {
    const workerId = evt.taskId;
    
    if (!state.workers.has(workerId)) {
        createWorkerCard(workerId, evt.providerName);
    }
    
    const cardId = `worker-${workerId.replace(/[^a-zA-Z0-9]/g, '-')}`;
    const card = document.getElementById(cardId);
    
    if (card) {
        const content = card.querySelector('.worker-content');
        const status = card.querySelector('.worker-status');
        
        content.textContent = evt.accumulatedContent || '';
        status.textContent = evt.isLastToken ? 'COMPLETED' : 'STREAMING';
        status.className = `worker-status ${evt.isLastToken ? 'completed' : 'streaming'}`;
        
        // Auto-scroll
        content.scrollTop = content.scrollHeight;
    }
}

function handleResult(evt) {
    updateStatusChips({
        status: evt.success ? 'completed' : 'failed',
        totalTokens: evt.totalTokens,
        llmCalls: evt.totalLlmCalls
    });
    
    if (evt.content) {
        document.getElementById('final-result').innerHTML = 
            typeof marked !== 'undefined' ? marked.parse(evt.content) : evt.content;
    }
    
    addLog('RESULT', evt.success ? 'Execution completed successfully' : `Failed: ${evt.error}`);
    loadFiles();
}

function handleError(evt) {
    addLog('ERROR', evt.message);
    updateStatusChips({ status: 'failed' });
}

function handleFile(evt) {
    state.files.push({ category: evt.category, name: evt.fileName });
    renderFileList();
}

// ─────────────────────────────────────────────────────────────────────────────
//  UI Helpers
// ─────────────────────────────────────────────────────────────────────────────
function addLog(phase, message) {
    const list = document.getElementById('log-stream');
    const li = document.createElement('li');
    const time = new Date().toLocaleTimeString('en-US', { hour12: false });
    
    li.innerHTML = `
        <span class="log-time">${time}</span>
        <span class="log-phase">[${phase}]</span>
        ${message || ''}
    `;
    
    list.appendChild(li);
    list.scrollTop = list.scrollHeight;
}

function addTask(id, description) {
    const tbody = document.getElementById('table-tasks');
    const existing = tbody.querySelector(`[data-task-id="${id}"]`);
    
    if (existing) return;
    
    const row = document.createElement('tr');
    row.setAttribute('data-task-id', id);
    row.innerHTML = `
        <td><code>${id}</code></td>
        <td>${description}</td>
    `;
    
    tbody.appendChild(row);
}

function createWorkerCard(workerId, providerName) {
    state.workers.set(workerId, { content: '' });
    
    const grid = document.getElementById('worker-grid');
    const card = document.createElement('div');
    const cardId = `worker-${workerId.replace(/[^a-zA-Z0-9]/g, '-')}`;
    
    card.id = cardId;
    card.className = 'worker-card';
    card.innerHTML = `
        <div class="worker-header">
            <span class="worker-id">${workerId}</span>
            <span class="worker-status streaming">STREAMING</span>
        </div>
        <div class="worker-provider">${providerName || 'LLM'}</div>
        <div class="worker-content"></div>
    `;
    
    grid.appendChild(card);
}

async function loadFiles() {
    if (!state.currentProject) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/files`);
        state.files = await res.json();
        renderFileList();
    } catch (err) {
        console.error('Failed to load files:', err);
    }
}

function renderFileList() {
    const list = document.getElementById('file-list');
    
    // Group by category
    const grouped = {};
    state.files.forEach(f => {
        if (!grouped[f.category]) grouped[f.category] = [];
        grouped[f.category].push(f.name);
    });
    
    list.innerHTML = Object.entries(grouped).map(([cat, files]) => `
        <div class="file-category">${cat.toUpperCase()}</div>
        ${files.map(name => `
            <div class="file-item" onclick="loadFileContent('${cat}', '${name}')">
                📄 ${name}
            </div>
        `).join('')}
    `).join('');
}

async function loadFileContent(category, name) {
    if (!state.currentProject) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/files/${category}/${name}`);
        const content = await res.text();
        
        const preview = document.getElementById('file-preview');
        preview.innerHTML = typeof marked !== 'undefined' 
            ? `<div class="markdown-content">${marked.parse(content)}</div>`
            : `<pre>${content}</pre>`;
        
        // Mark active
        document.querySelectorAll('.file-item').forEach(el => el.classList.remove('active'));
        event.target.classList.add('active');
    } catch (err) {
        console.error('Failed to load file:', err);
    }
}

async function loadTimeline() {
    if (!state.currentProject) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/timeline`);
        const timeline = await res.json();
        
        const list = document.getElementById('log-stream');
        list.innerHTML = timeline.map(e => {
            const time = new Date(e.timestamp).toLocaleTimeString('en-US', { hour12: false });
            return `
                <li>
                    <span class="log-time">${time}</span>
                    <span class="log-phase">[${e.phase}]</span>
                    ${e.message}
                </li>
            `;
        }).join('');
    } catch (err) {
        console.error('Failed to load timeline:', err);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Tabs
// ─────────────────────────────────────────────────────────────────────────────
function setupTabs() {
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const target = btn.dataset.target;
            
            // Update buttons
            btn.parentElement.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            
            // Update views
            const wrapper = btn.closest('.terminal-window').querySelector('.term-content-wrapper');
            wrapper.querySelectorAll('.term-view').forEach(v => v.classList.remove('active'));
            document.getElementById(target).classList.add('active');
        });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
//  Create Project Modal
// ─────────────────────────────────────────────────────────────────────────────
function showCreateModal() {
    document.getElementById('modal-overlay').classList.remove('hidden');
}

function showCreateModalWithStrategy(strategy) {
    document.getElementById('new-strategy').value = strategy;
    updateFormForStrategy();
    showCreateModal();
}

function hideCreateModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

function updateFormForStrategy() {
    const strategy = document.getElementById('new-strategy').value;
    
    document.getElementById('maker-options').classList.toggle('hidden', strategy !== 'Maker');
    document.getElementById('uot-options').classList.toggle('hidden', !strategy.includes('Uot'));
}

async function createProject() {
    const strategy = document.getElementById('new-strategy').value;
    
    const config = {
        name: document.getElementById('new-name').value,
        description: document.getElementById('new-description').value,
        icon: document.getElementById('new-icon').value,
        strategy: strategy,
        task: document.getElementById('new-task').value
    };
    
    if (strategy === 'Maker') {
        config.reliability = document.getElementById('new-reliability').value;
        config.maxLlmCalls = parseInt(document.getElementById('new-max-calls').value);
    } else {
        config.domainHint = document.getElementById('new-domain-hint').value;
        config.maxAnalogies = parseInt(document.getElementById('new-max-analogies').value);
        config.maxCandidates = parseInt(document.getElementById('new-max-candidates').value);
    }
    
    try {
        const res = await fetch('/api/projects', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(config)
        });
        
        const result = await res.json();
        
        if (result.success) {
            hideCreateModal();
            await loadProjects();
            selectProject(result.projectId);
        } else {
            alert('Failed to create project: ' + result.error);
        }
    } catch (err) {
        console.error('Failed to create project:', err);
        alert('Failed to create project');
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Utilities
// ─────────────────────────────────────────────────────────────────────────────
function formatNumber(num) {
    if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
    if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
    return num.toString();
}

