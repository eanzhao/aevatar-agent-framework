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
        const { label, cssClass } = getStrategyBadge(p.strategy);
        return `
            <div class="nav-item ${state.currentProject?.id === p.id ? 'active' : ''}" 
                 onclick="selectProject('${p.id}')">
                <span class="nav-item-icon">${p.icon}</span>
                <span class="nav-item-text">${p.name}</span>
                <span class="nav-item-strategy ${cssClass}">${label}</span>
                <button class="nav-item-delete" onclick="event.stopPropagation(); deleteProject('${p.id}', '${p.name.replace(/'/g, "\\'")}')" title="Archive project">×</button>
            </div>
        `;
    }).join('');
}

// 策略标签映射（短名称）
function getStrategyBadge(strategy) {
    const map = {
        'Direct': { label: 'AI', cssClass: 'direct' },
        'Maker': { label: 'MKR', cssClass: 'maker' },
        'UotCombinational': { label: 'C', cssClass: 'uot-c' },
        'UotExploratory': { label: 'E', cssClass: 'uot-e' },
        'UotTransformative': { label: 'T', cssClass: 'uot-t' }
    };
    return map[strategy] || { label: '?', cssClass: '' };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Delete / Archive Project
// ─────────────────────────────────────────────────────────────────────────────
async function deleteProject(id, name) {
    const confirmed = confirm(`确定要归档项目 "${name}" 吗？\n\n归档后可以在 Archive 中恢复。`);
    if (!confirmed) return;
    
    try {
        const res = await fetch(`/api/projects/${id}`, { method: 'DELETE' });
        const result = await res.json();
        
        if (result.success) {
            // 如果删除的是当前选中的项目，清空选择
            if (state.currentProject?.id === id) {
                state.currentProject = null;
                document.getElementById('dashboard').classList.add('hidden');
                document.getElementById('empty-state').classList.remove('hidden');
            }
            
            // 重新加载项目列表
            await loadProjects();
            
            // 显示提示
            showToast(`项目 "${name}" 已归档`);
        } else {
            alert('归档失败: ' + (result.error || 'Unknown error'));
        }
    } catch (err) {
        console.error('Failed to delete project:', err);
        alert('归档失败');
    }
}

// Toast 提示
function showToast(message, duration = 3000) {
    // 移除旧的 toast
    const existing = document.querySelector('.toast');
    if (existing) existing.remove();
    
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.textContent = message;
    document.body.appendChild(toast);
    
    // 动画显示
    requestAnimationFrame(() => toast.classList.add('show'));
    
    // 自动消失
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 300);
    }, duration);
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
    
    // RUN button state
    const btnStart = document.getElementById('btn-start');
    
    if (status.status === 'running') {
        progressContainer.style.display = 'block';
        progressBar.style.width = `${progress * 100}%`;
        progressLabel.textContent = `${Math.round(progress * 100)}%`;
        
        // Update button to show running state
        btnStart.innerHTML = '<span class="btn-icon">⏹</span> STOP';
        btnStart.classList.add('running');
        btnStart.onclick = () => stopRun();
    } else {
        progressContainer.style.display = 'none';
        
        // Reset button to normal state
        btnStart.innerHTML = '<span class="btn-icon">⚡</span> RUN';
        btnStart.classList.remove('running');
        btnStart.onclick = () => startRun();
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
            // Update button state
            updateStatusChips({ status: 'running', progress: 0 });
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

async function stopRun() {
    if (!state.currentProject) return;
    
    const confirmed = confirm('确定要停止当前运行吗？');
    if (!confirmed) return;
    
    try {
        const res = await fetch(`/api/projects/${state.currentProject.id}/stop`, {
            method: 'POST'
        });
        const result = await res.json();
        
        if (result.success) {
            showToast('运行已停止');
            // Close SSE connection
            if (state.eventSource) {
                state.eventSource.close();
                state.eventSource = null;
            }
            // Refresh status
            await refreshStatus();
        } else {
            alert('停止失败: ' + (result.error || 'Unknown error'));
        }
    } catch (err) {
        console.error('Failed to stop run:', err);
        alert('停止失败');
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

// Track current content source type
let currentContentSource = 'none';
let uploadedFileInfo = null;

function showCreateModal() {
    // Reset form
    currentContentSource = 'none';
    uploadedFileInfo = null;
    document.getElementById('uploaded-files').innerHTML = '';
    document.getElementById('upload-id').value = '';
    switchContentSource('none');
    updateTaskTemplate();
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

// ─────────────────────────────────────────────────────────────────────────────
//  Config Modal
// ─────────────────────────────────────────────────────────────────────────────
async function showConfigModal() {
    if (!state.currentProject) return;
    
    try {
        // 获取项目详情
        const res = await fetch(`/api/projects/${state.currentProject.id}/config`);
        const config = await res.json();
        
        // 显示任务
        document.getElementById('config-task').textContent = config.task || 'N/A';
        
        // 显示选项
        const options = config.options || {};
        document.getElementById('config-options').textContent = JSON.stringify(options, null, 2);
        
        // 显示内容来源
        const content = config.content || { source: 'None' };
        document.getElementById('config-content').textContent = JSON.stringify(content, null, 2);
        
        document.getElementById('config-modal-overlay').classList.remove('hidden');
    } catch (err) {
        console.error('Failed to load config:', err);
        
        // Fallback: 显示基本信息
        document.getElementById('config-task').textContent = state.currentProject.task || 'N/A';
        document.getElementById('config-options').textContent = JSON.stringify({
            strategy: state.currentProject.strategy,
            strategyDisplayName: state.currentProject.strategyDisplayName
        }, null, 2);
        document.getElementById('config-content').textContent = 'N/A';
        
        document.getElementById('config-modal-overlay').classList.remove('hidden');
    }
}

function hideConfigModal() {
    document.getElementById('config-modal-overlay').classList.add('hidden');
}

function updateFormForStrategy() {
    const strategy = document.getElementById('new-strategy').value;
    
    // Hide all strategy options first
    document.getElementById('maker-options').classList.add('hidden');
    document.getElementById('uot-options').classList.add('hidden');
    document.getElementById('euot-options').classList.add('hidden');
    document.getElementById('tuot-options').classList.add('hidden');
    
    // Show relevant options
    if (strategy === 'Maker') {
        document.getElementById('maker-options').classList.remove('hidden');
    } else if (strategy.includes('Uot')) {
        document.getElementById('uot-options').classList.remove('hidden');
        
        if (strategy === 'UotExploratory') {
            document.getElementById('euot-options').classList.remove('hidden');
        } else if (strategy === 'UotTransformative') {
            document.getElementById('tuot-options').classList.remove('hidden');
        }
    }
    // Direct 策略不需要额外配置
}

// ─────────────────────────────────────────────────────────────────────────────
//  Content Source Management
// ─────────────────────────────────────────────────────────────────────────────
function switchContentSource(source) {
    currentContentSource = source;
    
    // Update tabs
    document.querySelectorAll('.source-tab').forEach(tab => {
        tab.classList.toggle('active', tab.dataset.source === source);
    });
    
    // Update panels
    document.getElementById('content-upload').classList.toggle('hidden', source !== 'upload');
    document.getElementById('content-path').classList.toggle('hidden', source !== 'path');
    document.getElementById('content-text').classList.toggle('hidden', source !== 'text');
}

// File upload handling
async function handleFileSelect(event) {
    const files = event.target.files;
    if (files.length === 0) return;
    
    const formData = new FormData();
    for (const file of files) {
        formData.append('files', file);
    }
    
    try {
        const res = await fetch('/api/upload', {
            method: 'POST',
            body: formData
        });
        
        const result = await res.json();
        
        if (result.success) {
            uploadedFileInfo = result;
            document.getElementById('upload-id').value = result.uploadId;
            renderUploadedFiles(result.files);
        } else {
            alert('Upload failed: ' + result.error);
        }
    } catch (err) {
        console.error('Upload failed:', err);
        alert('Upload failed');
    }
}

function renderUploadedFiles(files) {
    const container = document.getElementById('uploaded-files');
    container.innerHTML = files.map(f => `
        <div class="uploaded-file">
            <span class="uploaded-file-name">📄 ${f.name}</span>
            <span class="uploaded-file-size">${formatBytes(f.size)}</span>
        </div>
    `).join('');
}

function formatBytes(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

// Drag and drop support
document.addEventListener('DOMContentLoaded', () => {
    const uploadZone = document.getElementById('upload-zone');
    if (uploadZone) {
        uploadZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            uploadZone.classList.add('drag-over');
        });
        
        uploadZone.addEventListener('dragleave', () => {
            uploadZone.classList.remove('drag-over');
        });
        
        uploadZone.addEventListener('drop', (e) => {
            e.preventDefault();
            uploadZone.classList.remove('drag-over');
            
            const files = e.dataTransfer.files;
            if (files.length > 0) {
                document.getElementById('file-input').files = files;
                handleFileSelect({ target: { files } });
            }
        });
    }
});

// ─────────────────────────────────────────────────────────────────────────────
//  Task Template Management
// ─────────────────────────────────────────────────────────────────────────────
function updateTaskTemplate() {
    const template = document.getElementById('new-task-template').value;
    
    // Show/hide custom task textarea
    document.getElementById('task-custom').classList.toggle('hidden', template !== 'Custom');
    
    // Show/hide template-specific options
    document.getElementById('task-qa-options').classList.toggle('hidden', template !== 'QA');
    document.getElementById('task-translate-options').classList.toggle('hidden', template !== 'Translate');
    document.getElementById('task-extract-options').classList.toggle('hidden', template !== 'Extract');
    document.getElementById('task-continue-options').classList.toggle('hidden', template !== 'Continue');
    
    // Auto-update icon based on template
    const iconMap = {
        'Summarize': '📋',
        'Analyze': '🔍',
        'Review': '📝',
        'Critique': '🎭',
        'Rewrite': '✏️',
        'Continue': '📖',
        'Extract': '🎯',
        'Compare': '⚖️',
        'QA': '❓',
        'Translate': '🌐',
        'Custom': '🔬'
    };
    
    document.getElementById('new-icon').value = iconMap[template] || '🔬';
}

// ─────────────────────────────────────────────────────────────────────────────
//  Create Project
// ─────────────────────────────────────────────────────────────────────────────
async function createProject() {
    const strategy = document.getElementById('new-strategy').value;
    const template = document.getElementById('new-task-template').value;
    
    const config = {
        name: document.getElementById('new-name').value,
        description: document.getElementById('new-description').value,
        icon: document.getElementById('new-icon').value,
        strategy: strategy
    };
    
    // ─── Content Source ───
    if (currentContentSource !== 'none') {
        config.content = {};
        
        if (currentContentSource === 'upload' && document.getElementById('upload-id').value) {
            config.content.uploadId = document.getElementById('upload-id').value;
        } else if (currentContentSource === 'path') {
            const filePath = document.getElementById('content-file-path').value;
            if (filePath) {
                // Check if it looks like a directory
                if (filePath.endsWith('/') || !filePath.includes('.')) {
                    config.content.directoryPath = filePath;
                    config.content.recursive = document.getElementById('content-recursive').checked;
                    const exts = document.getElementById('content-extensions').value;
                    if (exts) {
                        config.content.extensions = exts.split(',').map(e => e.trim());
                    }
                } else {
                    config.content.filePath = filePath;
                }
            }
        } else if (currentContentSource === 'text') {
            const directContent = document.getElementById('content-direct').value;
            if (directContent) {
                config.content.directContent = directContent;
            }
        }
        
        const contentDesc = document.getElementById('content-description').value;
        if (contentDesc) {
            config.content.contentDescription = contentDesc;
        }
    }
    
    // ─── Task Configuration ───
    if (template !== 'Custom') {
        config.taskConfig = {
            template: template
        };
        
        // Template-specific options
        if (template === 'QA') {
            config.taskConfig.question = document.getElementById('task-question').value;
        } else if (template === 'Translate') {
            config.taskConfig.targetLanguage = document.getElementById('task-target-lang').value;
        } else if (template === 'Extract') {
            config.taskConfig.extractionTarget = document.getElementById('task-extract-target').value;
        } else if (template === 'Continue') {
            config.taskConfig.continuationHint = document.getElementById('task-continue-hint').value;
        }
        
        const customInstruction = document.getElementById('new-custom-instruction').value;
        if (customInstruction) {
            config.taskConfig.customInstruction = customInstruction;
        }
        
        // Task can be empty when using template with content
        config.task = document.getElementById('new-task').value || `${template} task`;
    } else {
        config.task = document.getElementById('new-task').value;
        if (!config.task) {
            alert('Please provide a task description');
            return;
        }
    }
    
    // ─── Strategy Options ───
    if (strategy === 'Maker') {
        config.reliability = document.getElementById('new-reliability').value;
        config.maxLlmCalls = parseInt(document.getElementById('new-max-calls').value);
    } else if (strategy.includes('Uot')) {
        config.domainHint = document.getElementById('new-domain-hint').value;
        config.maxAnalogies = parseInt(document.getElementById('new-max-analogies').value);
        config.maxCandidates = parseInt(document.getElementById('new-max-candidates').value);
        
        if (strategy === 'UotExploratory') {
            config.maxOutsideThoughts = parseInt(document.getElementById('new-max-outside-thoughts').value);
            config.explorationDirections = parseInt(document.getElementById('new-exploration-directions').value);
        } else if (strategy === 'UotTransformative') {
            config.maxRuleSets = parseInt(document.getElementById('new-max-rule-sets').value);
            config.minRadicality = parseFloat(document.getElementById('new-min-radicality').value);
        }
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

