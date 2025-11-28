// ============================================================
//  C-UoT Creative Reasoning Console - Frontend
// ============================================================

let sampleProblems = [];
let currentRunId = null;
let eventSource = null;

// Icons for categories
const categoryIcons = {
    'Engineering': '🏗️',
    'Business': '💼',
    'Product': '📱',
    'Workplace': '🏢',
    'Environment': '🌱'
};

// ============================================================
//  Theme Management
// ============================================================

function initTheme() {
    const saved = localStorage.getItem('uot-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = saved || (prefersDark ? 'dark' : 'light');
    setTheme(theme);
}

function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('uot-theme', theme);
    updateThemeUI(theme);
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'dark';
    const next = current === 'dark' ? 'light' : 'dark';
    setTheme(next);
}

function updateThemeUI(theme) {
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

// ============================================================
//  Initialization
// ============================================================

async function init() {
    initTheme();
    await loadSampleProblems();
}

async function loadSampleProblems() {
    try {
        const response = await fetch('/api/problems');
        sampleProblems = await response.json();
        renderSampleProblems();
    } catch (err) {
        console.error('Failed to load sample problems:', err);
    }
}

function renderSampleProblems() {
    const container = document.getElementById('sample-list');
    container.innerHTML = sampleProblems.map(p => `
        <div class="sample-item" onclick="selectSample('${p.id}')">
            <span class="sample-icon">${categoryIcons[p.category] || '📝'}</span>
            <span class="sample-title">${p.title}</span>
            <span class="sample-category">${p.category}</span>
        </div>
    `).join('');
}

function selectSample(id) {
    const problem = sampleProblems.find(p => p.id === id);
    if (!problem) return;

    // Update selection UI
    document.querySelectorAll('.sample-item').forEach(el => el.classList.remove('selected'));
    event.currentTarget.classList.add('selected');

    // Fill form
    document.getElementById('problem-input').value = problem.problem;
    document.getElementById('domain-hint').value = problem.domainHint;
}

// ============================================================
//  Solve
// ============================================================

async function startSolve() {
    const problem = document.getElementById('problem-input').value.trim();
    if (!problem) {
        alert('Please enter a problem description');
        return;
    }

    const request = {
        problem,
        domainHint: document.getElementById('domain-hint').value || null,
        maxAnalogies: parseInt(document.getElementById('opt-analogies').value) || 5,
        maxCandidates: parseInt(document.getElementById('opt-candidates').value) || 10,
        feasibilityThreshold: parseFloat(document.getElementById('opt-feasibility').value) || 0.6,
        utilityWeight: parseFloat(document.getElementById('opt-utility').value) || 0.5,
        noveltyWeight: parseFloat(document.getElementById('opt-novelty').value) || 0.5
    };

    // Disable button
    const btn = document.getElementById('btn-solve');
    btn.disabled = true;
    btn.innerHTML = '<span class="btn-icon">⏳</span> Processing...';

    // Show progress section
    document.getElementById('empty-state').style.display = 'none';
    document.getElementById('progress-section').style.display = 'block';
    document.getElementById('results-section').style.display = 'none';

    // Reset stats
    resetProgress();

    try {
        const response = await fetch('/api/solve', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });

        const result = await response.json();
        currentRunId = result.runId;

        // Start SSE
        startEventStream(currentRunId);

    } catch (err) {
        console.error('Failed to start solve:', err);
        alert('Failed to start creative reasoning: ' + err.message);
        resetButton();
    }
}

function resetProgress() {
    document.getElementById('progress-bar').style.width = '0%';
    document.getElementById('current-phase').textContent = 'Starting';
    document.getElementById('stat-analogies').textContent = '0';
    document.getElementById('stat-thoughts').textContent = '0';
    document.getElementById('stat-candidates').textContent = '0';
    document.getElementById('stat-passed').textContent = '0';
    document.getElementById('timeline').innerHTML = '';
    document.getElementById('status-badge').textContent = 'Running';
    document.getElementById('status-badge').className = 'status-badge';
}

function resetButton() {
    const btn = document.getElementById('btn-solve');
    btn.disabled = false;
    btn.innerHTML = '<span class="btn-icon">⚡</span> Start Creative Reasoning';
}

// ============================================================
//  SSE Event Stream
// ============================================================

function startEventStream(runId) {
    if (eventSource) {
        eventSource.close();
    }

    eventSource = new EventSource(`/api/runs/${runId}/events`);

    eventSource.onmessage = function(event) {
        const data = JSON.parse(event.data);
        handleEvent(data);
    };

    eventSource.onerror = function(err) {
        console.error('SSE error:', err);
        eventSource.close();
        loadResult(runId);
    };
}

function handleEvent(evt) {
    console.log('Event:', evt);

    // Update progress
    if (evt.progress != null) {
        document.getElementById('progress-bar').style.width = (evt.progress * 100) + '%';
    }

    if (evt.phase) {
        document.getElementById('current-phase').textContent = evt.phase;
    }

    // Update stats
    if (evt.analogiesFound != null) {
        document.getElementById('stat-analogies').textContent = evt.analogiesFound;
    }
    if (evt.thoughtsExtracted != null) {
        document.getElementById('stat-thoughts').textContent = evt.thoughtsExtracted;
    }
    if (evt.candidatesGenerated != null) {
        document.getElementById('stat-candidates').textContent = evt.candidatesGenerated;
    }
    if (evt.candidatesPassed != null) {
        document.getElementById('stat-passed').textContent = evt.candidatesPassed;
    }

    // Add to timeline
    if (evt.message) {
        addTimelineEntry(evt);
    }

    // Handle completion
    if (evt.type === 'result') {
        document.getElementById('status-badge').textContent = 'Completed';
        document.getElementById('status-badge').classList.add('completed');
        loadResult(evt.runId);
    } else if (evt.type === 'error') {
        document.getElementById('status-badge').textContent = 'Failed';
        document.getElementById('status-badge').classList.add('failed');
        addTimelineEntry({ phase: 'Error', message: evt.message, timestamp: new Date().toISOString() });
        resetButton();
    }
}

function addTimelineEntry(evt) {
    const timeline = document.getElementById('timeline');
    const time = new Date(evt.timestamp).toLocaleTimeString();
    
    const li = document.createElement('li');
    li.className = 'timeline-item';
    li.innerHTML = `
        <span class="timeline-time">${time}</span>
        <span class="timeline-phase">${evt.phase || '-'}</span>
        <span class="timeline-message">${evt.message}</span>
    `;
    
    timeline.insertBefore(li, timeline.firstChild);
}

// ============================================================
//  Load Result
// ============================================================

async function loadResult(runId) {
    try {
        const response = await fetch(`/api/runs/${runId}/result`);
        if (!response.ok) {
            console.error('Result not ready');
            return;
        }

        const result = await response.json();
        renderResult(result);
        resetButton();

    } catch (err) {
        console.error('Failed to load result:', err);
    }
}

function renderResult(result) {
    // Show results section
    document.getElementById('results-section').style.display = 'block';

    // Meta info
    document.getElementById('result-count').textContent = `${result.allCandidates.length} solutions`;
    document.getElementById('result-llm-calls').textContent = `${result.trace.totalLLMCalls} LLM calls`;

    // Best solution
    if (result.bestSolution) {
        const best = result.bestSolution;
        document.getElementById('best-feasibility').textContent = `F: ${(best.score?.feasibility || 0).toFixed(2)}`;
        document.getElementById('best-utility').textContent = `U: ${(best.score?.utility || 0).toFixed(2)}`;
        document.getElementById('best-novelty').textContent = `N: ${(best.score?.novelty || 0).toFixed(2)}`;
        document.getElementById('best-composite').textContent = `Score: ${(best.score?.composite || 0).toFixed(2)}`;
        document.getElementById('best-content').innerHTML = marked.parse(best.content || '');
    }

    // All candidates
    const candidatesContainer = document.getElementById('candidates-list');
    candidatesContainer.innerHTML = result.allCandidates.map((c, i) => `
        <div class="candidate-item" onclick="toggleCandidate(this)">
            <div class="candidate-header">
                <span class="candidate-rank">#${i + 1}</span>
                <div class="score-pills">
                    <span class="score-pill feasibility">F: ${(c.score?.feasibility || 0).toFixed(2)}</span>
                    <span class="score-pill utility">U: ${(c.score?.utility || 0).toFixed(2)}</span>
                    <span class="score-pill novelty">N: ${(c.score?.novelty || 0).toFixed(2)}</span>
                    <span class="score-pill composite">${(c.score?.composite || 0).toFixed(2)}</span>
                </div>
            </div>
            <div class="candidate-content">${marked.parse(c.content || '')}</div>
        </div>
    `).join('');

    // Trace info
    document.getElementById('trace-info').innerHTML = `
        <p><strong>Execution ID:</strong> ${result.trace.executionId}</p>
        <p><strong>Analogies Explored:</strong> ${result.trace.analogiesExplored}</p>
        <p><strong>Thoughts Extracted:</strong> ${result.trace.thoughtsExtracted}</p>
        <p><strong>Candidates Generated:</strong> ${result.trace.candidatesGenerated}</p>
        <p><strong>Passed Feasibility:</strong> ${result.trace.candidatesPassedFeasibility}</p>
        <p><strong>Total LLM Calls:</strong> ${result.trace.totalLLMCalls}</p>
        <p><strong>Total Tokens:</strong> ${result.trace.totalTokens.toLocaleString()}</p>
        <p><strong>Duration:</strong> ${formatDuration(result.trace.duration)}</p>
    `;
}

function toggleCandidate(el) {
    el.classList.toggle('expanded');
}

function formatDuration(duration) {
    // duration is a .NET TimeSpan string like "00:01:23.456"
    if (!duration) return '-';
    const parts = duration.split(':');
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const seconds = parseFloat(parts[2]);
    
    if (hours > 0) return `${hours}h ${minutes}m`;
    if (minutes > 0) return `${minutes}m ${Math.round(seconds)}s`;
    return `${seconds.toFixed(1)}s`;
}

// ============================================================
//  Init on load
// ============================================================

document.addEventListener('DOMContentLoaded', init);

