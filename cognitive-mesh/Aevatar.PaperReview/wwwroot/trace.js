// ═══════════════════════════════════════════════════════════════
//  LLM TRACE - 实时 LLM 调用追踪前端
// ═══════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────
//  STATE
// ─────────────────────────────────────────────────────────────

let sessions = [];
let currentSessionId = null;
let eventSource = null;
let calls = new Map();  // callId -> call data
let selectedCallId = null;

// 统计数据
let stats = {
    totalCalls: 0,
    activeCalls: 0,
    completedCalls: 0,
    totalTokens: 0,
    totalLatency: 0
};

// ─────────────────────────────────────────────────────────────
//  INITIALIZATION
// ─────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    loadSessions();
    
    // URL 参数自动选择 session
    const params = new URLSearchParams(window.location.search);
    const sessionId = params.get('session');
    if (sessionId) {
        setTimeout(() => selectSession(sessionId), 500);
    }
});

async function loadSessions() {
    try {
        const res = await fetch('/api/sessions');
        sessions = await res.json();
        renderSessionSelect();
    } catch (err) {
        console.error('Failed to load sessions:', err);
    }
}

function renderSessionSelect() {
    const select = document.getElementById('session-select');
    select.innerHTML = '<option value="">-- Select Session --</option>' +
        sessions.map(s => `
            <option value="${s.id}" ${s.id === currentSessionId ? 'selected' : ''}>
                ${escapeHtml(s.title || 'Untitled')} (${s.status})
            </option>
        `).join('');
}

// ─────────────────────────────────────────────────────────────
//  SESSION SELECTION
// ─────────────────────────────────────────────────────────────

function selectSession(sessionId) {
    if (!sessionId) {
        currentSessionId = null;
        disconnectEventStream();
        clearCalls();
        updateSessionBadge(null);
        return;
    }
    
    currentSessionId = sessionId;
    const session = sessions.find(s => s.id === sessionId);
    
    // 更新 URL
    history.replaceState(null, '', `?session=${sessionId}`);
    
    // 更新 badge
    updateSessionBadge(session);
    
    // 清空并重新连接
    clearCalls();
    connectEventStream(sessionId);
}

function updateSessionBadge(session) {
    const badge = document.getElementById('session-status');
    if (!session) {
        badge.textContent = '-';
        badge.className = 'session-badge';
        return;
    }
    badge.textContent = session.status;
    badge.className = 'session-badge ' + session.status.toLowerCase();
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
        console.log('SSE connection closed');
        // 不自动断开，允许持续监听
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
    switch (event.type) {
        case 'LlmCallStartEvent':
            handleCallStart(event);
            break;
            
        case 'LlmStreamingEvent':
            handleStreaming(event);
            break;
            
        case 'LlmCallCompleteEvent':
            handleCallComplete(event);
            break;
            
        case 'ResultEvent':
            // 评审完成，更新 session 状态
            loadSessions();
            break;
            
        case 'ErrorEvent':
            console.error('Error:', event.message);
            break;
    }
}

function handleCallStart(event) {
    const call = {
        id: event.callId,
        workerId: event.workerId,
        providerName: event.providerName || 'unknown',
        systemPrompt: event.systemPrompt || '',
        userPrompt: event.userPrompt || '',
        phase: event.phase || '',
        startTime: new Date(event.timestamp),
        status: 'streaming',
        content: '',
        tokens: { prompt: 0, completion: 0 },
        latencyMs: 0
    };
    
    calls.set(event.callId, call);
    stats.totalCalls++;
    stats.activeCalls++;
    
    updateStats();
    renderCallList();
    
    // 自动选中新的调用
    selectCall(event.callId);
}

function handleStreaming(event) {
    const call = calls.get(event.callId);
    if (!call) return;
    
    call.content = event.accumulatedContent;
    call.status = 'streaming';
    
    // 更新列表中的预览
    updateCallPreview(event.callId);
    
    // 如果当前选中的是这个调用，实时更新详情
    if (selectedCallId === event.callId) {
        updateResponseContent(call, true);
    }
}

function handleCallComplete(event) {
    const call = calls.get(event.callId);
    if (!call) {
        // 如果没有 start 事件（可能是非 streaming 调用），创建新记录
        const newCall = {
            id: event.callId,
            workerId: event.workerId,
            providerName: event.providerName || 'unknown',
            systemPrompt: '',
            userPrompt: '',
            phase: event.phase || '',
            startTime: new Date(event.timestamp),
            status: event.success ? 'completed' : 'error',
            content: event.content,
            error: event.error,
            tokens: { prompt: event.promptTokens, completion: event.completionTokens },
            latencyMs: event.latencyMs
        };
        calls.set(event.callId, newCall);
        stats.totalCalls++;
    } else {
        call.status = event.success ? 'completed' : 'error';
        call.content = event.content || call.content;
        call.error = event.error;
        call.tokens = { prompt: event.promptTokens, completion: event.completionTokens };
        call.latencyMs = event.latencyMs;
        stats.activeCalls = Math.max(0, stats.activeCalls - 1);
    }
    
    stats.completedCalls++;
    stats.totalTokens += event.promptTokens + event.completionTokens;
    stats.totalLatency += event.latencyMs;
    
    updateStats();
    renderCallList();
    
    // 更新详情面板
    if (selectedCallId === event.callId) {
        renderCallDetail(calls.get(event.callId));
    }
}

// ─────────────────────────────────────────────────────────────
//  UI RENDERING
// ─────────────────────────────────────────────────────────────

function updateStats() {
    document.getElementById('stat-calls').textContent = stats.totalCalls;
    document.getElementById('stat-active').textContent = stats.activeCalls;
    document.getElementById('stat-completed').textContent = stats.completedCalls;
    document.getElementById('stat-tokens').textContent = formatNumber(stats.totalTokens);
    
    const avgLatency = stats.completedCalls > 0 
        ? Math.round(stats.totalLatency / stats.completedCalls)
        : 0;
    document.getElementById('stat-latency').textContent = avgLatency > 0 
        ? avgLatency + 'ms' 
        : '-';
}

function renderCallList() {
    const list = document.getElementById('call-list');
    
    if (calls.size === 0) {
        list.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">🤖</div>
                <p>Waiting for LLM calls...</p>
            </div>
        `;
        return;
    }
    
    // 按时间倒序排列
    const sortedCalls = Array.from(calls.values())
        .sort((a, b) => b.startTime - a.startTime);
    
    list.innerHTML = sortedCalls.map(call => `
        <div class="call-item ${call.status} ${selectedCallId === call.id ? 'active' : ''}"
             onclick="selectCall('${call.id}')">
            <div class="call-item-header">
                <span class="call-id">${escapeHtml(call.id)}</span>
                <span class="call-status ${call.status}">${call.status.toUpperCase()}</span>
            </div>
            <div class="call-item-meta">
                <span class="worker">${escapeHtml(call.workerId)}</span>
                <span>·</span>
                <span>${call.providerName}</span>
                ${call.latencyMs > 0 ? `<span>· ${call.latencyMs}ms</span>` : ''}
            </div>
            <div id="preview-${call.id}" class="call-item-preview ${call.status === 'streaming' ? 'streaming' : ''}">
                ${escapeHtml(call.content.substring(0, 100) || '...')}
            </div>
        </div>
    `).join('');
}

function updateCallPreview(callId) {
    const call = calls.get(callId);
    if (!call) return;
    
    const el = document.getElementById(`preview-${callId}`);
    if (el) {
        el.textContent = call.content.substring(0, 100) || '...';
        el.className = `call-item-preview ${call.status === 'streaming' ? 'streaming' : ''}`;
    }
}

function selectCall(callId) {
    selectedCallId = callId;
    const call = calls.get(callId);
    
    // 更新列表选中状态
    document.querySelectorAll('.call-item').forEach(el => {
        el.classList.toggle('active', el.querySelector('.call-id').textContent === callId);
    });
    
    if (call) {
        renderCallDetail(call);
    }
}

function renderCallDetail(call) {
    const detail = document.getElementById('call-detail');
    const title = document.getElementById('detail-title');
    const provider = document.getElementById('detail-provider');
    const phase = document.getElementById('detail-phase');
    
    title.textContent = `📝 ${call.id}`;
    provider.textContent = call.providerName;
    phase.textContent = call.phase || '-';
    
    const isStreaming = call.status === 'streaming';
    
    detail.innerHTML = `
        <!-- Stats -->
        <div class="detail-stats">
            <div class="detail-stat">
                <span class="detail-stat-label">Status</span>
                <span class="detail-stat-value ${call.status === 'error' ? 'error' : call.status === 'completed' ? 'success' : ''}">${call.status.toUpperCase()}</span>
            </div>
            <div class="detail-stat">
                <span class="detail-stat-label">Prompt Tokens</span>
                <span class="detail-stat-value">${formatNumber(call.tokens.prompt)}</span>
            </div>
            <div class="detail-stat">
                <span class="detail-stat-label">Completion Tokens</span>
                <span class="detail-stat-value">${formatNumber(call.tokens.completion)}</span>
            </div>
            <div class="detail-stat">
                <span class="detail-stat-label">Latency</span>
                <span class="detail-stat-value">${call.latencyMs > 0 ? call.latencyMs + 'ms' : '-'}</span>
            </div>
        </div>
        
        <!-- System Prompt -->
        ${call.systemPrompt ? `
        <div class="detail-section">
            <div class="detail-section-header">
                <span class="detail-section-title">📋 System Prompt</span>
                <button class="detail-section-copy" onclick="copyText('${escapeHtml(call.systemPrompt).replace(/'/g, "\\'")}')">📋 Copy</button>
            </div>
            <div class="detail-content">${escapeHtml(call.systemPrompt)}</div>
        </div>
        ` : ''}
        
        <!-- User Prompt -->
        ${call.userPrompt ? `
        <div class="detail-section">
            <div class="detail-section-header">
                <span class="detail-section-title">👤 User Prompt</span>
                <button class="detail-section-copy" onclick="copyText('${escapeHtml(call.userPrompt).replace(/'/g, "\\'")}')">📋 Copy</button>
            </div>
            <div class="detail-content">${escapeHtml(call.userPrompt)}</div>
        </div>
        ` : ''}
        
        <!-- Response -->
        <div class="detail-section">
            <div class="detail-section-header">
                <span class="detail-section-title">🤖 Response</span>
                <button class="detail-section-copy" onclick="copyResponse()">📋 Copy</button>
            </div>
            <div id="response-content" class="detail-content ${isStreaming ? 'streaming' : ''}">${escapeHtml(call.content)}${isStreaming ? '<span class="cursor"></span>' : ''}</div>
        </div>
        
        <!-- Error -->
        ${call.error ? `
        <div class="detail-section">
            <div class="detail-section-header">
                <span class="detail-section-title">❌ Error</span>
            </div>
            <div class="detail-content" style="color: var(--error)">${escapeHtml(call.error)}</div>
        </div>
        ` : ''}
    `;
}

function updateResponseContent(call, isStreaming) {
    const el = document.getElementById('response-content');
    if (!el) return;
    
    el.innerHTML = escapeHtml(call.content) + (isStreaming ? '<span class="cursor"></span>' : '');
    el.className = `detail-content ${isStreaming ? 'streaming' : ''}`;
    
    // 自动滚动到底部
    el.scrollTop = el.scrollHeight;
}

// ─────────────────────────────────────────────────────────────
//  ACTIONS
// ─────────────────────────────────────────────────────────────

function clearCalls() {
    calls.clear();
    selectedCallId = null;
    stats = {
        totalCalls: 0,
        activeCalls: 0,
        completedCalls: 0,
        totalTokens: 0,
        totalLatency: 0
    };
    
    updateStats();
    
    document.getElementById('call-list').innerHTML = `
        <div class="empty-state">
            <div class="empty-icon">🤖</div>
            <p>Select a session to view LLM calls</p>
        </div>
    `;
    
    document.getElementById('call-detail').innerHTML = `
        <div class="empty-state">
            <div class="empty-icon">👆</div>
            <p>Select a call from the list</p>
        </div>
    `;
    
    document.getElementById('detail-title').textContent = '📝 CALL DETAIL';
    document.getElementById('detail-provider').textContent = '-';
    document.getElementById('detail-phase').textContent = '-';
}

function copyResponse() {
    const call = calls.get(selectedCallId);
    if (call) {
        navigator.clipboard.writeText(call.content);
    }
}

function copyText(text) {
    navigator.clipboard.writeText(text);
}

// ─────────────────────────────────────────────────────────────
//  UTILITIES
// ─────────────────────────────────────────────────────────────

function escapeHtml(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function formatNumber(n) {
    if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
    if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
    return n.toString();
}
