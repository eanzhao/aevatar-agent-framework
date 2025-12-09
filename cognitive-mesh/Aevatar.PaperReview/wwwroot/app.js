// ═══════════════════════════════════════════════════════════════
//  PAPER REVIEW - MAKER SYSTEM FRONTEND
//  展示完整 MAKER 流程：分解 → 并行执行 → 投票 → 合成
// ═══════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────
//  STATE
// ─────────────────────────────────────────────────────────────

let sessions = [];
let currentSessionId = null;
let eventSource = null;
let uploadId = null;

// 每个 session 的独立状态存储
const sessionStates = new Map();  // sessionId -> SessionState

// 当前 session 的状态引用
let workers = new Map();      // workerId -> worker data
let llmCalls = new Map();     // callId -> call data
let votingRounds = [];        // 投票轮次
let stageLogs = [];           // 阶段日志
let timelineEvents = [];      // 时间线事件
let currentPhase = 'submit';

// MAKER 参数
let makerConfig = {
    k: 2,
    n: 3,
    type: 'Standard'
};

// 统计
let stats = {
    workers: 0,
    calls: 0,
    tokens: 0,
    progress: 0
};

// MAKER 参数映射表
const MAKER_PARAMS = {
    'Quick': { k: 1, n: 1, desc: 'Single shot, no voting' },
    'Standard': { k: 2, n: 3, desc: '3 workers, 2-vote lead' },
    'Detailed': { k: 3, n: 5, desc: '5 workers, 3-vote lead' },
    'Rigorous': { k: 4, n: 7, desc: '7 workers, 4-vote lead' },
    'Critical': { k: 5, n: 9, desc: '9 workers, 5-vote lead' }
};

// ─────────────────────────────────────────────────────────────
//  SESSION STATE MANAGEMENT
// ─────────────────────────────────────────────────────────────

function createEmptyState() {
    return {
        workers: new Map(),
        llmCalls: new Map(),
        votingRounds: [],
        stageLogs: [],
        timelineEvents: [],
        currentPhase: 'submit',
        stats: { workers: 0, calls: 0, tokens: 0, progress: 0 }
    };
}

function saveCurrentState() {
    if (!currentSessionId) return;
    
    sessionStates.set(currentSessionId, {
        workers: new Map(workers),
        llmCalls: new Map(llmCalls),
        votingRounds: [...votingRounds],
        stageLogs: [...stageLogs],
        timelineEvents: [...timelineEvents],
        currentPhase,
        stats: { ...stats }
    });
}

function loadSessionState(sessionId) {
    const state = sessionStates.get(sessionId);
    
    if (state) {
        workers = new Map(state.workers);
        llmCalls = new Map(state.llmCalls);
        votingRounds = [...state.votingRounds];
        stageLogs = [...state.stageLogs];
        timelineEvents = [...state.timelineEvents];
        currentPhase = state.currentPhase;
        stats = { ...state.stats };
    } else {
        // 新 session，创建空状态
        const emptyState = createEmptyState();
        workers = emptyState.workers;
        llmCalls = emptyState.llmCalls;
        votingRounds = emptyState.votingRounds;
        stageLogs = emptyState.stageLogs;
        timelineEvents = emptyState.timelineEvents;
        currentPhase = emptyState.currentPhase;
        stats = emptyState.stats;
    }
}

// ─────────────────────────────────────────────────────────────
//  INITIALIZATION
// ─────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    loadSessions();
    setupDragAndDrop();
});

async function loadSessions() {
    try {
        const res = await fetch('/api/sessions');
        sessions = await res.json();
        renderSessionList();
    } catch (err) {
        console.error('Failed to load sessions:', err);
    }
}

// ─────────────────────────────────────────────────────────────
//  SESSION LIST
// ─────────────────────────────────────────────────────────────

function renderSessionList() {
    const list = document.getElementById('session-list');
    const count = document.getElementById('session-count');
    
    count.textContent = sessions.length;
    
    if (sessions.length === 0) {
        list.innerHTML = '<div class="empty-hint">No sessions yet</div>';
        return;
    }
    
    list.innerHTML = sessions.map(s => `
        <div class="session-item ${s.id === currentSessionId ? 'active' : ''}" 
             onclick="selectSession('${s.id}')">
            <div class="session-title">${escapeHtml(s.title || 'Untitled')}</div>
            <div class="session-meta">
                <span class="session-status ${s.status.toLowerCase()}"></span>
                <span>${s.type}</span>
                <span>·</span>
                <span>${formatDate(s.createdAt)}</span>
            </div>
        </div>
    `).join('');
}

function selectSession(sessionId) {
    // 保存当前 session 的状态
    saveCurrentState();
    
    // 断开旧的事件流
    disconnectEventStream();
    
    currentSessionId = sessionId;
    const session = sessions.find(s => s.id === sessionId);
    
    if (!session) return;
    
    // 加载目标 session 的状态
    loadSessionState(sessionId);
    
    // 显示 dashboard
    document.getElementById('empty-state').classList.add('hidden');
    document.getElementById('dashboard').classList.remove('hidden');
    
    // 更新 UI
    updatePaperInfo(session);
    updateStats(session);
    renderSessionList();
    
    // 渲染已保存的状态
    renderAllState();
    
    // 根据状态处理
    if (session.status === 'Reviewing') {
        // 正在评审 - 连接事件流
        connectEventStream(sessionId);
    } else if (session.status === 'Completed' || session.status === 'Failed') {
        // 已完成 - 加载结果
        loadResult(sessionId);
    }
    // Pending 状态不需要额外处理
}

function renderAllState() {
    // 渲染所有 UI 组件
    renderWorkers();
    renderVoting();
    renderStageLogs();
    renderTimeline(timelineEvents);
    renderLlmCalls();
    updatePhaseNodes(currentPhase);
}

function resetMakerState() {
    workers.clear();
    llmCalls.clear();
    votingRounds = [];
    stageLogs = [];
    timelineEvents = [];
    currentPhase = 'submit';
    stats = { workers: 0, calls: 0, tokens: 0, progress: 0 };
    
    // 保存到状态存储
    if (currentSessionId) {
        saveCurrentState();
    }
    
    // 重置 UI
    renderAllState();
}

// ─────────────────────────────────────────────────────────────
//  MAKER PARAMS
// ─────────────────────────────────────────────────────────────

function updateMakerParams() {
    const type = document.getElementById('new-review-type')?.value || 'Standard';
    const params = MAKER_PARAMS[type] || MAKER_PARAMS['Standard'];
    
    document.getElementById('param-k').textContent = params.k;
    document.getElementById('param-n').textContent = params.n;
}

function updateMakerConfigUI(type) {
    const params = MAKER_PARAMS[type] || MAKER_PARAMS['Standard'];
    makerConfig = { k: params.k, n: params.n, type };
    
    const kEl = document.getElementById('config-k');
    const nEl = document.getElementById('config-n');
    const typeEl = document.getElementById('config-type');
    
    if (kEl) kEl.textContent = params.k;
    if (nEl) nEl.textContent = params.n;
    if (typeEl) typeEl.textContent = type;
}

function updatePaperInfo(session) {
    document.getElementById('paper-title').textContent = session.title || 'Untitled';
    document.getElementById('paper-authors').textContent = session.authors || 'Unknown';
    document.getElementById('review-type').textContent = session.type;
    document.getElementById('venue-type').textContent = session.venueType;
    
    const badge = document.getElementById('status-badge');
    badge.textContent = session.status.toUpperCase();
    badge.className = 'status-badge ' + session.status.toLowerCase();
    
    const btnStart = document.getElementById('btn-start');
    const btnRestart = document.getElementById('btn-restart');
    
    if (session.status === 'Reviewing') {
        // 正在评审 - 显示 STOP 按钮
        btnStart.textContent = '⏹ STOP';
        btnStart.onclick = stopReview;
        btnStart.classList.remove('hidden');
        btnRestart.classList.add('hidden');
    } else if (session.status === 'Completed' || session.status === 'Failed') {
        // 已完成/失败 - 隐藏 START，显示 RESTART
        btnStart.classList.add('hidden');
        btnRestart.classList.remove('hidden');
    } else {
        // Pending - 显示 START
        btnStart.textContent = '⚡ START';
        btnStart.onclick = startReview;
        btnStart.classList.remove('hidden');
        btnRestart.classList.add('hidden');
    }
    
    // 更新 MAKER 配置显示
    updateMakerConfigUI(session.type);
}

function updateStats(session) {
    document.getElementById('stat-phase').textContent = session.currentPhase || '-';
    document.getElementById('stat-progress').textContent = (session.progressPercent || 0) + '%';
    document.getElementById('stat-workers').textContent = stats.workers;
    document.getElementById('stat-calls').textContent = session.totalLlmCalls || stats.calls;
    document.getElementById('stat-tokens').textContent = formatNumber(session.totalTokens || stats.tokens);
    
    document.getElementById('progress-bar').style.width = (session.progressPercent || 0) + '%';
}

// ─────────────────────────────────────────────────────────────
//  UPLOAD MODAL
// ─────────────────────────────────────────────────────────────

function showUploadModal() {
    document.getElementById('modal-overlay').classList.remove('hidden');
    // Reset
    document.getElementById('new-title').value = '';
    document.getElementById('new-authors').value = '';
    document.getElementById('paper-content').value = '';
    uploadId = null;
    document.getElementById('uploaded-file').classList.add('hidden');
}

function hideUploadModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

function switchUploadTab(tab) {
    document.querySelectorAll('.upload-tab').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tab);
    });
    document.querySelectorAll('.upload-tab-content').forEach(el => {
        el.classList.toggle('active', el.id === 'upload-tab-' + tab);
    });
}

function setupDragAndDrop() {
    const zone = document.getElementById('upload-zone');
    if (!zone) return;

    zone.addEventListener('dragover', e => {
        e.preventDefault();
        zone.style.borderColor = 'var(--accent)';
        zone.style.background = 'var(--accent-light)';
    });

    zone.addEventListener('dragleave', e => {
        e.preventDefault();
        zone.style.borderColor = '';
        zone.style.background = '';
    });

    zone.addEventListener('drop', e => {
        e.preventDefault();
        zone.style.borderColor = '';
        zone.style.background = '';
        if (e.dataTransfer.files.length > 0) {
            uploadFile(e.dataTransfer.files[0]);
        }
    });
}

function handleFileUpload(e) {
    if (e.target.files[0]) uploadFile(e.target.files[0]);
}

async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);

    try {
        const res = await fetch('/api/upload', { method: 'POST', body: formData });
        const result = await res.json();
        
        if (result.success) {
            uploadId = result.uploadId;
            const el = document.getElementById('uploaded-file');
            el.innerHTML = `✅ ${escapeHtml(result.fileName)} (${formatBytes(result.size)})`;
            el.classList.remove('hidden');
        } else {
            alert('Upload failed: ' + result.error);
        }
    } catch (err) {
        console.error('Upload error:', err);
        alert('Upload failed');
    }
}

async function submitPaper() {
    const title = '';    // no title input, will be set from file name or default
    const authors = '';  // no authors input
    const reviewType = document.getElementById('new-review-type').value;
    const venueType = document.getElementById('new-venue-type').value;
    const paperContent = document.getElementById('paper-content').value.trim();

    // 只校验内容/上传，标题作者可为空（后端有默认值）
    if (!paperContent && !uploadId) { alert('Please provide paper content or upload a file'); return; }

    try {
        const res = await fetch('/api/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: title || null, authors: authors || null, reviewType, venueType, uploadId, paperContent: paperContent || null })
        });

        const result = await res.json();
        
        if (result.success) {
            hideUploadModal();
            await loadSessions();
            selectSession(result.sessionId);
            startReview();
        } else {
            alert('Failed to create session: ' + result.error);
        }
    } catch (err) {
        console.error('Submit error:', err);
        alert('Failed to submit paper');
    }
}

// ─────────────────────────────────────────────────────────────
//  REVIEW CONTROL
// ─────────────────────────────────────────────────────────────

async function startReview() {
    if (!currentSessionId) return;

    try {
        const res = await fetch(`/api/sessions/${currentSessionId}/review`, { method: 'POST' });
        const result = await res.json();
        
        if (result.success) {
            const session = sessions.find(s => s.id === currentSessionId);
            if (session) {
                session.status = 'Reviewing';
                updatePaperInfo(session);
                renderSessionList();
            }
            
            // 重置当前 session 的状态
            resetMakerState();
            addTimelineEvent('phase', 'Review Started', 'MAKER system initialized');
            
            // 连接事件流
            connectEventStream(currentSessionId);
        } else {
            alert('Failed to start: ' + result.error);
        }
    } catch (err) {
        console.error('Start error:', err);
    }
}

async function stopReview() {
    if (!currentSessionId) return;
    
    try {
        await fetch(`/api/sessions/${currentSessionId}/stop`, { method: 'POST' });
        disconnectEventStream();
        await loadSessions();
        if (currentSessionId) selectSession(currentSessionId);
    } catch (err) {
        console.error('Stop error:', err);
    }
}

// ─────────────────────────────────────────────────────────────
//  SSE EVENT STREAM
// ─────────────────────────────────────────────────────────────

function connectEventStream(sessionId) {
    disconnectEventStream();
    
    eventSource = new EventSource(`/api/sessions/${sessionId}/events`);
    
    eventSource.onmessage = (e) => {
        const event = JSON.parse(e.data);
        handleEvent(event);
    };
    
    eventSource.onerror = () => {
        disconnectEventStream();
        loadSessions().then(() => {
            if (currentSessionId) selectSession(currentSessionId);
        });
    };
}

function disconnectEventStream() {
    if (eventSource) {
        eventSource.close();
        eventSource = null;
    }
}

// ─────────────────────────────────────────────────────────────
//  EVENT HANDLERS
// ─────────────────────────────────────────────────────────────

function handleEvent(event) {
    // 检查事件是否属于当前 session
    if (event.sessionId && event.sessionId !== currentSessionId) {
        // 事件不属于当前 session，忽略
        return;
    }
    
    const session = sessions.find(s => s.id === currentSessionId);
    
    switch (event.type) {
        case 'ProgressEvent':
            handleProgress(event, session);
            break;
            
        case 'WorkerStartedEvent':
            handleWorkerStarted(event);
            break;
            
        case 'WorkerCompletedEvent':
            handleWorkerCompleted(event);
            break;
            
        case 'LlmCallStartEvent':
            handleLlmStart(event);
            break;
            
        case 'LlmStreamingEvent':
            handleLlmStreaming(event);
            break;
            
        case 'LlmCallCompleteEvent':
            handleLlmComplete(event);
            break;
            
        case 'VotingRoundEvent':
            handleVotingRound(event);
            break;
            
        case 'ConsensusEvent':
            handleConsensus(event);
            break;
            
        case 'TaskDecomposedEvent':
            handleDecomposition(event);
            break;
            
        case 'TaskComposedEvent':
            handleComposition(event);
            break;
            
        case 'PhaseChangeEvent':
            handlePhaseChange(event);
            break;
            
        case 'StageLogEvent':
            handleStageLog(event);
            break;
            
        case 'ResultEvent':
            handleResult(event, session);
            break;
            
        case 'ErrorEvent':
            handleError(event, session);
            break;
    }
}

function handleProgress(event, session) {
    if (session) {
        session.progressPercent = event.progressPercent;
        session.currentPhase = event.phase;
        updateStats(session);
    }
    
    // 更新 phase
    updatePhaseFromMaker(event.phase);
    
    addTimelineEvent('phase', event.phase, event.message);
}

function handleWorkerStarted(event) {
    console.log('[WORKER_STARTED]', event.workerId, event.role, 'existing workers:', Array.from(workers.keys()));
    
    // 检查 worker 是否已存在（去重）
    if (workers.has(event.workerId)) {
        console.log('[WORKER_STARTED] Worker already exists, updating status only');
        const existing = workers.get(event.workerId);
        // 只有当状态不是 completed/history 时才更新
        if (existing.status !== 'completed' && existing.status !== 'history') {
            existing.status = 'streaming';
        }
        return;
    }
    
    const worker = {
        id: event.workerId,
        taskId: event.taskId,
        role: event.role,
        provider: event.providerName,
        temperature: event.temperature,
        status: 'streaming',
        content: '',
        startTime: new Date()
    };
    
    workers.set(event.workerId, worker);
    stats.workers = workers.size;
    document.getElementById('stat-workers').textContent = stats.workers;
    
    console.log('[WORKER_STARTED] After set, workers size:', workers.size);
    
    renderWorkers();
    addTimelineEvent('worker', `Worker ${event.workerId}`, `Started: ${event.role}`);
}

function handleWorkerCompleted(event) {
    const worker = workers.get(event.workerId);
    if (worker) {
        worker.status = event.success ? 'completed' : 'error';
        worker.content = event.content;
        worker.preview = event.contentPreview;
        worker.latency = event.latencyMs;
        worker.tokens = event.totalTokens;
    }
    
    renderWorkers();
    addTimelineEvent('worker', `Worker ${event.workerId}`, 
        event.success ? `Completed (${event.latencyMs}ms)` : `Failed`);
}

function handleLlmStart(event) {
    console.log('[LLM_START]', event.callId, 'worker:', event.workerId, 'existing calls:', llmCalls.size);
    
    // 检查 call 是否已存在（去重）
    if (llmCalls.has(event.callId)) {
        console.log('[LLM_START] Call already exists, skipping');
        return;
    }
    
    const call = {
        id: event.callId,
        workerId: event.workerId,
        provider: event.providerName,
        systemPrompt: event.systemPrompt,
        userPrompt: event.userPrompt,
        phase: event.phase,
        status: 'streaming',
        content: '',
        startTime: new Date()
    };
    
    llmCalls.set(event.callId, call);
    stats.calls++;
    document.getElementById('stat-calls').textContent = stats.calls;
    
    console.log('[LLM_START] After set, llmCalls size:', llmCalls.size);
    
    // 更新 worker 状态
    const worker = workers.get(event.workerId);
    if (worker) {
        worker.status = 'streaming';
        worker.currentCallId = event.callId;
        worker.history = worker.history || [];
    } else {
        console.warn('[LLM_START] Worker not found:', event.workerId);
    }
    
    renderLlmCalls();
    renderWorkers();
}

function handleLlmStreaming(event) {
    const call = llmCalls.get(event.callId);
    if (call) {
        call.content = event.accumulatedContent || call.content;
    }
    
    // 更新 worker preview - 优先使用 workerId 直接查找
    let worker = workers.get(event.workerId);
    
    // 备用：通过 callId 查找
    if (!worker) {
        worker = Array.from(workers.values()).find(w => w.currentCallId === event.callId);
    }
    
    if (worker) {
        // 使用 accumulatedContent 如果有，否则追加 token
        if (event.accumulatedContent) {
            worker.content = event.accumulatedContent;
        } else if (event.token) {
            worker.content = (worker.content || '') + event.token;
        }
        
        worker.status = 'streaming';
        updateWorkerPreview(worker.id);
    }
}

function handleLlmComplete(event) {
    const call = llmCalls.get(event.callId);
    if (call) {
        call.status = event.success ? 'completed' : 'error';
        call.content = event.content || call.content;
        call.promptTokens = event.promptTokens;
        call.completionTokens = event.completionTokens;
        call.latency = event.latencyMs;
    }
    
    stats.tokens += (event.promptTokens || 0) + (event.completionTokens || 0);
    document.getElementById('stat-tokens').textContent = formatNumber(stats.tokens);
    
    // 记录 worker 历史对话
    const worker = workers.get(event.workerId);
    if (worker) {
        worker.history = worker.history || [];
        const snapshot = {
            callId: event.callId,
            status: call?.status || (event.success ? 'completed' : 'error'),
            systemPrompt: call?.systemPrompt,
            userPrompt: call?.userPrompt,
            content: event.content || call?.content || '',
            tokens: (event.promptTokens || 0) + (event.completionTokens || 0),
            latencyMs: event.latencyMs,
            finishedAt: new Date()
        };
        worker.history.unshift(snapshot);
        if (worker.history.length > 10) worker.history.pop();
    }
    
    renderLlmCalls();
}

function handleVotingRound(event) {
    votingRounds.push(event);
    updatePhaseNodes('vote');
    renderVoting();
    
    const status = event.consensusReached ? 'Consensus!' : `${event.candidates[0]?.votes || 0}/${event.votesNeeded} votes`;
    addTimelineEvent('voting', `Voting Round ${event.round}`, status);
}

function handleConsensus(event) {
    addTimelineEvent('success', 'Consensus Reached', 
        `Round ${event.round}: ${event.leaderVotes}/${event.totalVotes} votes`);
}

function handleDecomposition(event) {
    updatePhaseNodes('decompose');
    
    // 展示 AI 分析出的评审维度
    if (event.depth === 0 && event.subTasks?.length > 0) {
        // 首层分解 = 论文分析完成，生成了评审维度
        addTimelineEvent('phase', '📊 Paper Analyzed', 
            `${event.subTasks.length} review dimensions identified`);
        
        // 添加详细的维度日志
        stageLogs.unshift({
            id: `dim-${Date.now()}`,
            stage: 'Paper Analysis',
            status: 'completed',
            summary: `AI analyzed paper and identified ${event.subTasks.length} review dimensions`,
            stats: { workerCount: event.subTasks.length },
            details: {
                candidates: event.subTasks.map((task, idx) => ({
                    id: `D${idx + 1}`,
                    preview: task.substring(0, 100),
                    votes: 0
                })),
                workerOutputs: [],
                winnerContent: null
            },
            startTime: new Date(),
            endTime: new Date(),
            timestamp: new Date()
        });
        renderStageLogs();
    } else {
        addTimelineEvent('phase', 'Task Decomposed', 
            `Split into ${event.subTasks?.length || 0} subtasks (depth: ${event.depth})`);
    }
}

function handleComposition(event) {
    updatePhaseNodes('compose');
    addTimelineEvent('phase', 'Results Composed', event.composedPreview.substring(0, 100) + '...');
}

function handlePhaseChange(event) {
    updatePhaseFromMaker(event.newPhase);
    addTimelineEvent('phase', event.newPhase, event.message);
}

function handleStageLog(event) {
    const logEntry = {
        id: `log-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
        stage: event.stage,
        status: event.status,
        summary: event.summary,
        stats: event.stats,
        details: event.details,
        durationMs: event.durationMs,
        startTime: new Date(event.startTime),
        endTime: new Date(event.endTime),
        timestamp: new Date(event.timestamp)
    };
    
    stageLogs.unshift(logEntry);
    
    // Keep last 50
    if (stageLogs.length > 50) stageLogs.pop();
    
    renderStageLogs();
    
    // 如果是 Configuration 阶段，更新 MAKER 参数
    if (event.stage === 'Configuration' && event.stats) {
        const n = event.stats.workerCount || 3;
        const k = Math.ceil((n + 1) / 2);
        makerConfig = { k, n, type: getTypeFromN(n) };
        updateMakerConfigUI(makerConfig.type);
    }
}

function getTypeFromN(n) {
    for (const [type, params] of Object.entries(MAKER_PARAMS)) {
        if (params.n === n) return type;
    }
    return 'Standard';
}

function handleResult(event, session) {
    if (session) {
        session.status = event.success ? 'Completed' : 'Failed';
        session.totalLlmCalls = event.totalLlmCalls;
        session.totalTokens = event.totalTokens;
        updatePaperInfo(session);
        updateStats(session);
        renderSessionList();
    }
    
    currentPhase = event.success ? 'complete' : 'execute';
    updatePhaseNodes(currentPhase);
    
    // 标记所有 worker 为历史完成状态
    workers.forEach(w => {
        w.status = 'history';
    });
    renderWorkers();
    
    if (event.content) {
        renderResult(event.content);
        switchDetailTab('result');
    }
    
    addTimelineEvent(event.success ? 'success' : 'error', 
        event.success ? 'Review Completed' : 'Review Failed',
        `${event.totalLlmCalls} LLM calls, ${formatNumber(event.totalTokens)} tokens`);
    
    // 保存最终状态
    saveCurrentState();
    
    // 断开事件流
    disconnectEventStream();
}

function handleError(event, session) {
    if (session) {
        session.status = 'Failed';
        updatePaperInfo(session);
        renderSessionList();
    }
    
    addTimelineEvent('error', 'Error', event.message);
}

// ─────────────────────────────────────────────────────────────
//  PHASE VISUALIZATION
// ─────────────────────────────────────────────────────────────

function updatePhaseFromMaker(makerPhase) {
    const phaseMap = {
        'Starting': 'submit',
        'Assessing': 'decompose',
        'Decomposing': 'decompose',
        'Voting': 'vote',
        'Executing': 'execute',
        'Solving': 'execute',
        'Streaming': 'execute',
        'Composing': 'compose',
        'Completed': 'complete',
        'Failed': 'execute',
        'RedFlag': 'execute'
    };
    
    const phase = phaseMap[makerPhase] || currentPhase;
    updatePhaseNodes(phase);
}

function updatePhaseNodes(activePhase) {
    currentPhase = activePhase;
    const phases = ['submit', 'decompose', 'execute', 'vote', 'compose', 'complete'];
    const activeIndex = phases.indexOf(activePhase);
    
    phases.forEach((phase, index) => {
        const node = document.getElementById('phase-' + phase);
        if (!node) return;
        
        node.classList.remove('active', 'done');
        if (index < activeIndex) {
            node.classList.add('done');
        } else if (index === activeIndex) {
            node.classList.add('active');
        }
    });
}

// ─────────────────────────────────────────────────────────────
//  STAGE LOG RENDERING
// ─────────────────────────────────────────────────────────────

function renderStageLogs() {
    const container = document.getElementById('stage-log');
    const countEl = document.getElementById('stage-log-count');
    
    if (!container) return;
    
    if (countEl) {
        countEl.textContent = `${stageLogs.length} events`;
    }
    
    if (stageLogs.length === 0) {
        container.innerHTML = '<div class="stage-log-empty">Stage events will appear here</div>';
        return;
    }
    
    container.innerHTML = stageLogs.map(log => {
        const timeStr = formatTime(log.timestamp);
        const durationStr = log.durationMs > 0 ? formatDuration(log.durationMs) : '';
        const hasDetails = log.details && (
            (log.details.candidates && log.details.candidates.length > 0) ||
            (log.details.workerOutputs && log.details.workerOutputs.length > 0) ||
            log.details.winnerContent
        );
        
        // 构建统计信息
        let statsHtml = '';
        if (log.stats && log.status === 'completed') {
            const statItems = [];
            if (log.stats.llmCalls > 0) statItems.push(`<span class="stage-log-stat">🤖 ${log.stats.llmCalls} calls</span>`);
            if (log.stats.tokens > 0) statItems.push(`<span class="stage-log-stat">📊 ${formatNumber(log.stats.tokens)} tokens</span>`);
            if (log.stats.workerCount > 0) statItems.push(`<span class="stage-log-stat">👥 ${log.stats.workerCount} workers</span>`);
            if (log.stats.votingRounds > 0) statItems.push(`<span class="stage-log-stat">🗳️ ${log.stats.votingRounds} rounds</span>`);
            if (log.stats.consensusReached) statItems.push(`<span class="stage-log-stat">✓ consensus</span>`);
            
            if (statItems.length > 0) {
                statsHtml = `<div class="stage-log-stats">${statItems.join('')}</div>`;
            }
        }
        
        return `
            <div class="stage-log-item ${log.status} ${hasDetails ? 'has-details' : ''}" 
                 onclick="showStageDetail('${log.id}')"
                 title="${hasDetails ? 'Click to view details' : ''}">
                <div class="stage-log-time">${timeStr}</div>
                <div class="stage-log-content">
                    <div class="stage-log-header">
                        <span class="stage-log-stage">${escapeHtml(log.stage)}</span>
                        <span class="stage-log-status ${log.status}">${log.status.toUpperCase()}</span>
                        ${durationStr ? `<span class="stage-log-duration">${durationStr}</span>` : ''}
                    </div>
                    <div class="stage-log-summary">${escapeHtml(log.summary)}</div>
                    ${statsHtml}
                </div>
            </div>
        `;
    }).join('');
}

function formatDuration(ms) {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    return `${(ms / 60000).toFixed(1)}m`;
}

// ─────────────────────────────────────────────────────────────
//  STAGE DETAIL MODAL
// ─────────────────────────────────────────────────────────────

function showStageDetail(logId) {
    const log = stageLogs.find(l => l.id === logId);
    if (!log) return;
    
    const titleEl = document.getElementById('stage-modal-title');
    const bodyEl = document.getElementById('stage-modal-body');
    
    titleEl.textContent = `📋 ${log.stage} - ${log.status.toUpperCase()}`;
    
    let html = '';
    
    // Summary Section
    html += `
        <div class="stage-detail-section">
            <div class="stage-detail-title">
                <span class="icon">📊</span> Summary
            </div>
            <div class="stage-summary-grid">
                ${log.durationMs > 0 ? `<div class="summary-item"><div class="summary-value">${formatDuration(log.durationMs)}</div><div class="summary-label">Duration</div></div>` : ''}
                ${log.stats?.llmCalls ? `<div class="summary-item"><div class="summary-value">${log.stats.llmCalls}</div><div class="summary-label">LLM Calls</div></div>` : ''}
                ${log.stats?.tokens ? `<div class="summary-item"><div class="summary-value">${formatNumber(log.stats.tokens)}</div><div class="summary-label">Tokens</div></div>` : ''}
                ${log.stats?.workerCount ? `<div class="summary-item"><div class="summary-value">${log.stats.workerCount}</div><div class="summary-label">Workers</div></div>` : ''}
                ${log.stats?.votingRounds ? `<div class="summary-item"><div class="summary-value">${log.stats.votingRounds}</div><div class="summary-label">Voting Rounds</div></div>` : ''}
                ${log.stats?.consensusReached ? `<div class="summary-item"><div class="summary-value">✓</div><div class="summary-label">Consensus</div></div>` : ''}
            </div>
            <p style="color: var(--text-secondary); font-size: 13px;">${escapeHtml(log.summary)}</p>
        </div>
    `;
    
    // Candidates Section (投票详情)
    if (log.details?.candidates && log.details.candidates.length > 0) {
        html += `
            <div class="stage-detail-section">
                <div class="stage-detail-title">
                    <span class="icon">🗳️</span> Voting Candidates (${log.details.candidates.length})
                </div>
                <div class="candidates-grid">
                    ${log.details.candidates.map((c, i) => `
                        <div class="candidate-card ${c.isWinner ? 'winner' : ''}">
                            <div class="candidate-header">
                                <span class="candidate-id">${escapeHtml(c.id) || `Candidate ${i + 1}`}</span>
                                <div class="candidate-votes">
                                    <span class="vote-badge">${c.votes} votes</span>
                                    ${c.isWinner ? '<span class="winner-badge">WINNER</span>' : ''}
                                </div>
                            </div>
                            <div class="candidate-content">${escapeHtml(c.content || c.preview || 'No content')}</div>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }
    
    // Worker Outputs Section
    if (log.details?.workerOutputs && log.details.workerOutputs.length > 0) {
        html += `
            <div class="stage-detail-section">
                <div class="stage-detail-title">
                    <span class="icon">👥</span> Worker Outputs (${log.details.workerOutputs.length})
                </div>
                <div class="workers-list">
                    ${log.details.workerOutputs.map(w => `
                        <div class="worker-item">
                            <div class="worker-item-id">${escapeHtml(w.workerId)}</div>
                            <div class="worker-item-content">${escapeHtml(w.preview || w.content?.substring(0, 200) || 'No output')}</div>
                            <div class="worker-item-stats">
                                <span>${w.latencyMs}ms</span>
                                <span>${w.tokens} tokens</span>
                                <span>${w.success ? '✓' : '✗'}</span>
                            </div>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }
    
    // Winner Content Section
    if (log.details?.winnerContent) {
        html += `
            <div class="stage-detail-section">
                <div class="stage-detail-title">
                    <span class="icon">🏆</span> Winner Content
                </div>
                <div class="candidate-content">${escapeHtml(log.details.winnerContent)}</div>
            </div>
        `;
    }
    
    // No details
    if (!html.includes('stage-detail-section') || html.split('stage-detail-section').length <= 2) {
        html += `
            <div style="text-align: center; padding: 40px; color: var(--text-muted);">
                No detailed information available for this stage.
            </div>
        `;
    }
    
    bodyEl.innerHTML = html;
    document.getElementById('stage-modal').classList.remove('hidden');
}

function hideStageModal() {
    document.getElementById('stage-modal').classList.add('hidden');
}

// ─────────────────────────────────────────────────────────────
//  WORKERS RENDERING
// ─────────────────────────────────────────────────────────────

function renderWorkers() {
    const grid = document.getElementById('workers-grid');
    const status = document.getElementById('workers-status');
    
    if (workers.size === 0) {
        grid.innerHTML = '<div class="empty-hint">Workers will appear here</div>';
        status.textContent = 'Waiting...';
        return;
    }
    
    const activeCount = Array.from(workers.values()).filter(w => w.status === 'streaming').length;
    const completedCount = Array.from(workers.values()).filter(w => w.status === 'completed' || w.status === 'history').length;
    const historyCount = Array.from(workers.values()).filter(w => w.status === 'history').length;
    
    if (historyCount === workers.size) {
        status.textContent = `${completedCount} completed (historical)`;
    } else {
        status.textContent = `${activeCount} active, ${completedCount} completed`;
    }
    
    grid.innerHTML = Array.from(workers.values()).map(w => {
        const contentLen = (w.content || '').length;
        const previewLen = w.status === 'streaming' ? 150 : 100;
        const preview = (w.content || w.preview || '...').substring(0, previewLen);
        const tokenCount = Math.ceil(contentLen / 4);  // ~4 chars per token
        
        return `
        <div class="worker-card ${w.status}" onclick="showWorkerDetail('${w.id}')">
            <div class="worker-header">
                <span class="worker-id">${escapeHtml(w.id)}</span>
                <span class="worker-status ${w.status}">${w.status === 'history' ? 'DONE' : w.status.toUpperCase()}</span>
            </div>
            <div class="worker-meta">
                ${w.provider || 'default'} · ${w.role || 'worker'}
                ${w.status === 'streaming' ? `<span class="streaming-indicator">⚡ ${tokenCount} tokens</span>` : ''}
                ${w.latency ? `· ${w.latency}ms` : ''}
            </div>
            <div id="worker-preview-${w.id}" class="worker-preview ${w.status === 'streaming' ? 'streaming' : ''}">
                ${escapeHtml(preview)}${contentLen > previewLen ? '...' : ''}
            </div>
            ${w.status === 'streaming' ? '<div class="streaming-cursor"></div>' : ''}
        </div>
    `}).join('');
}

// 节流更新 - 避免过于频繁的 DOM 更新
let workerUpdatePending = {};
let workerUpdateTimeout = {};

function updateWorkerPreview(workerId) {
    const worker = workers.get(workerId);
    if (!worker) return;
    
    // 标记需要更新
    workerUpdatePending[workerId] = true;
    
    // 节流：最多每 50ms 更新一次
    if (workerUpdateTimeout[workerId]) return;
    
    workerUpdateTimeout[workerId] = setTimeout(() => {
        workerUpdateTimeout[workerId] = null;
        
        if (!workerUpdatePending[workerId]) return;
        workerUpdatePending[workerId] = false;
        
        const w = workers.get(workerId);
        if (!w) return;
        
        const el = document.getElementById(`worker-preview-${workerId}`);
        if (el) {
            const previewLen = w.status === 'streaming' ? 150 : 100;
            const content = w.content || '...';
            el.textContent = content.substring(0, previewLen) + (content.length > previewLen ? '...' : '');
            el.className = `worker-preview ${w.status === 'streaming' ? 'streaming' : ''}`;
        }
        
        // 更新 token count
        const card = el?.closest('.worker-card');
        const indicator = card?.querySelector('.streaming-indicator');
        if (indicator && w.content) {
            const tokenCount = Math.ceil(w.content.length / 4);
            indicator.textContent = `⚡ ${tokenCount} tokens`;
        }
    }, 50);
}

function showWorkerDetail(workerId) {
    const worker = workers.get(workerId);
    if (!worker) return;
    
    const call = llmCalls.get(worker.currentCallId);
    
    const historyHtml = (worker.history || []).map(h => `
        <div class="worker-history-item">
            <div class="worker-history-header">
                <span class="worker-status ${h.status}">${(h.status || 'done').toUpperCase()}</span>
                ${h.tokens ? `<span class="worker-history-metric">${h.tokens} tok</span>` : ''}
                ${h.latencyMs ? `<span class="worker-history-metric">${h.latencyMs} ms</span>` : ''}
            </div>
            ${h.systemPrompt ? `<div class="worker-detail-title">System Prompt</div>
            <div class="worker-detail-content">${escapeHtml(h.systemPrompt)}</div>` : ''}
            ${h.userPrompt ? `<div class="worker-detail-title">User Prompt</div>
            <div class="worker-detail-content">${escapeHtml(h.userPrompt)}</div>` : ''}
            <div class="worker-detail-title">Response</div>
            <div class="worker-detail-content">${escapeHtml(h.content || '')}</div>
        </div>
    `).join('') || '<div class="worker-detail-content" style="color: var(--text-muted);">No history yet</div>';
    
    document.getElementById('worker-modal-title').textContent = `🤖 Worker: ${workerId}`;
    document.getElementById('worker-modal-body').innerHTML = `
        <div class="worker-detail-section">
            <div class="worker-detail-title">Status</div>
            <div style="margin-bottom: 12px;">
                <span class="worker-status ${worker.status}">${worker.status.toUpperCase()}</span>
                ${worker.latency ? `<span style="margin-left: 8px; color: var(--text-muted);">${worker.latency}ms</span>` : ''}
                ${worker.tokens ? `<span style="margin-left: 8px; color: var(--text-muted);">${worker.tokens} tokens</span>` : ''}
            </div>
        </div>
        ${call?.systemPrompt ? `
        <div class="worker-detail-section">
            <div class="worker-detail-title">System Prompt</div>
            <div class="worker-detail-content">${escapeHtml(call.systemPrompt)}</div>
        </div>
        ` : ''}
        ${call?.userPrompt ? `
        <div class="worker-detail-section">
            <div class="worker-detail-title">User Prompt</div>
            <div class="worker-detail-content">${escapeHtml(call.userPrompt)}</div>
        </div>
        ` : ''}
        <div class="worker-detail-section">
            <div class="worker-detail-title">Response</div>
            <div class="worker-detail-content">${escapeHtml(worker.content || 'No content yet')}</div>
        </div>
        <div class="worker-detail-section">
            <div class="worker-detail-title">History (latest 10)</div>
            ${historyHtml}
        </div>
    `;
    
    document.getElementById('worker-modal').classList.remove('hidden');
}

function hideWorkerModal() {
    document.getElementById('worker-modal').classList.add('hidden');
}

// ─────────────────────────────────────────────────────────────
//  RESTART MODAL
// ─────────────────────────────────────────────────────────────

function showRestartModal() {
    if (!currentSessionId) return;
    
    const session = sessions.find(s => s.id === currentSessionId);
    if (!session) return;
    
    // 填充论文信息
    document.getElementById('restart-title').textContent = session.title || 'Untitled';
    document.getElementById('restart-authors').textContent = session.authors || 'Unknown';
    
    // 设置默认值（可以用上次的配置）
    document.getElementById('restart-venue-type').value = session.venueType || 'AI Conference';
    
    // 显示 modal
    document.getElementById('restart-modal').classList.remove('hidden');
    updateRestartParams();
}

function hideRestartModal() {
    document.getElementById('restart-modal').classList.add('hidden');
}

function updateRestartParams() {
    const type = document.getElementById('restart-review-type')?.value || 'Standard';
    const params = MAKER_PARAMS[type] || MAKER_PARAMS['Standard'];
    
    const kEl = document.getElementById('restart-param-k');
    const nEl = document.getElementById('restart-param-n');
    
    if (kEl) kEl.textContent = params.k;
    if (nEl) nEl.textContent = params.n;
}

async function restartReview() {
    if (!currentSessionId) return;
    
    const oldSession = sessions.find(s => s.id === currentSessionId);
    if (!oldSession) return;
    
    const reviewType = document.getElementById('restart-review-type').value;
    const venueType = document.getElementById('restart-venue-type').value;
    
    try {
        // 创建新的 session，复用老论文的内容
        const res = await fetch('/api/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                title: oldSession.title,
                authors: oldSession.authors,
                reviewType: reviewType,
                venueType: venueType,
                // 复用 uploadId 或者需要从后端获取论文内容
                uploadId: oldSession.uploadId || null,
                copyFromSessionId: currentSessionId  // 告诉后端从哪个 session 复制论文内容
            })
        });
        
        const result = await res.json();
        
        if (result.success) {
            hideRestartModal();
            await loadSessions();
            selectSession(result.sessionId);
            // 自动开始评审
            startReview();
        } else {
            alert('Failed to create new session: ' + result.error);
        }
    } catch (err) {
        console.error('Restart error:', err);
        alert('Failed to restart review');
    }
}

// ─────────────────────────────────────────────────────────────
//  VOTING RENDERING
// ─────────────────────────────────────────────────────────────

function renderVoting() {
    const content = document.getElementById('voting-content');
    const status = document.getElementById('voting-status');
    
    if (votingRounds.length === 0) {
        content.innerHTML = '<div class="voting-placeholder">Voting will appear here when workers complete</div>';
        status.textContent = '-';
        return;
    }
    
    const lastRound = votingRounds[votingRounds.length - 1];
    status.textContent = lastRound.consensusReached ? 'Consensus!' : `Round ${lastRound.round}`;
    
    content.innerHTML = votingRounds.slice(-3).reverse().map(round => {
        const maxVotes = Math.max(...round.candidates.map(c => c.votes), 1);
        
        return `
            <div class="vote-round">
                <div class="vote-round-header">
                    <span class="vote-round-title">Round ${round.round} · ${round.votingType}</span>
                    <span class="vote-round-status ${round.consensusReached ? 'consensus' : 'voting'}">
                        ${round.consensusReached ? '✓ Consensus' : `${round.candidates[0]?.votes || 0}/${round.votesNeeded} needed`}
                    </span>
                </div>
                <div class="vote-candidates">
                    ${round.candidates.slice(0, 5).map(c => `
                        <div class="vote-candidate ${c.isLeader ? 'leader' : ''}">
                            <div class="vote-bar">
                                <div class="vote-bar-fill" style="width: ${(c.votes / maxVotes) * 100}%"></div>
                            </div>
                            <span class="vote-count">${c.votes}</span>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }).join('');
}

// ─────────────────────────────────────────────────────────────
//  TIMELINE RENDERING
// ─────────────────────────────────────────────────────────────

function addTimelineEvent(type, title, message) {
    timelineEvents.unshift({
        type,
        title,
        message,
        time: new Date()
    });
    
    // Keep only last 50
    if (timelineEvents.length > 50) timelineEvents.pop();
    
    renderTimeline(timelineEvents);
}

function renderTimeline(events) {
    const el = document.getElementById('timeline');
    
    if (events.length === 0) {
        el.innerHTML = '<div class="timeline-empty">Events will appear here</div>';
        return;
    }
    
    el.innerHTML = events.map(e => `
        <div class="timeline-item ${e.type}">
            <div class="timeline-header">
                <span class="timeline-type">${escapeHtml(e.title)}</span>
                <span class="timeline-time">${formatTime(e.time)}</span>
            </div>
            <div class="timeline-message">${escapeHtml(e.message || '')}</div>
        </div>
    `).join('');
}

// ─────────────────────────────────────────────────────────────
//  LLM CALLS RENDERING
// ─────────────────────────────────────────────────────────────

function renderLlmCalls() {
    const el = document.getElementById('llm-calls');
    
    if (llmCalls.size === 0) {
        el.innerHTML = '<div class="llm-empty">LLM calls will appear here</div>';
        return;
    }
    
    const calls = Array.from(llmCalls.values()).slice(-20).reverse();
    
    el.innerHTML = calls.map(c => `
        <div class="llm-call-item" onclick="showWorkerDetail('${c.workerId}')">
            <div class="llm-call-header">
                <span class="llm-call-id">${escapeHtml(c.id)}</span>
                <span class="llm-call-status ${c.status}">${c.status.toUpperCase()}</span>
            </div>
            <div class="llm-call-meta">
                ${c.provider || 'default'} · ${c.phase} 
                ${c.latency ? `· ${c.latency}ms` : ''}
            </div>
        </div>
    `).join('');
}

// ─────────────────────────────────────────────────────────────
//  RESULT
// ─────────────────────────────────────────────────────────────

async function loadResult(sessionId) {
    try {
        const res = await fetch(`/api/sessions/${sessionId}/result`);
        const result = await res.json();
        if (result.content) renderResult(result.content);
    } catch (err) {
        console.error('Failed to load result:', err);
    }
}

function renderResult(content) {
    const el = document.getElementById('result-content');
    const header = document.getElementById('result-header');
    
    el.innerHTML = marked.parse(content);
    el.classList.add('has-content');
    
    // Show header button
    if (header) {
        header.classList.remove('hidden');
    }
}

function openResultPage() {
    if (!currentSessionId) return;
    
    const content = document.getElementById('result-content');
    if (!content || content.querySelector('.result-placeholder')) {
        alert('No result available yet');
        return;
    }
    
    window.open(`/result.html?session=${currentSessionId}`, '_blank');
}

// ─────────────────────────────────────────────────────────────
//  DETAIL TABS
// ─────────────────────────────────────────────────────────────

function switchDetailTab(tab) {
    document.querySelectorAll('.detail-tabs .tab-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tab);
    });
    document.querySelectorAll('.tab-content').forEach(el => {
        el.classList.toggle('active', el.id === 'tab-' + tab);
    });
}

// ─────────────────────────────────────────────────────────────
//  UTILITIES
// ─────────────────────────────────────────────────────────────

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function formatDate(dateStr) {
    const d = new Date(dateStr);
    return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatTime(date) {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function formatNumber(n) {
    if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
    if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
    return n.toString();
}

function formatBytes(bytes) {
    if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
    if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return bytes + ' B';
}
