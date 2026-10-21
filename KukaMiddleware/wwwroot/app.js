const storageKey = 'kuka-middleware-api-key';
const apiKeyInput = document.getElementById('apiKeyInput');
const saveApiKeyBtn = document.getElementById('saveApiKeyBtn');
const dispatchForm = document.getElementById('dispatchForm');
const missionsTableBody = document.getElementById('missionsTableBody');
const robotsTableBody = document.getElementById('robotsTableBody');
const toast = document.getElementById('toast');
const robotSelect = document.getElementById('robotId');
const dispatchButton = document.getElementById('dispatchButton');
const dispatchStatus = document.getElementById('dispatchStatus');
const connectionStatus = document.getElementById('connectionStatus');
const missionState = document.getElementById('missionState');
const autoRefresh = document.getElementById('autoRefresh');
let availableRobots = [];
let refreshing = false;
const missionHistory = new Map();
const robotHistory = new Map();
const expandedMissionHistory = new Set();
const expandedRobotHistory = new Set();

const savedApiKey = localStorage.getItem(storageKey) || '';
if (savedApiKey) {
  apiKeyInput.value = savedApiKey;
}

function showToast(message, tone = 'info') {
  toast.textContent = message;
  toast.classList.toggle('error', tone === 'error');
  toast.classList.add('visible');
  setTimeout(() => toast.classList.remove('visible'), 5000);
}

function getApiKey() {
  const value = apiKeyInput.value.trim();
  if (!value) {
    showToast('Enter the middleware API key.', 'error');
    return null;
  }
  localStorage.setItem(storageKey, value);
  return value;
}

function getHeaders() {
  const key = getApiKey();
  return key ? { 'X-Api-Key': key, Accept: 'application/json' } : null;
}

function formatDate(value) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('json')
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    const validationErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : '';
    const message = typeof body === 'string'
      ? body
      : body?.detail || validationErrors || body?.title || `Request failed (${response.status})`;
    throw new Error(message);
  }
  return body;
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (character) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[character]);
}

function statusClass(status) {
  const normalized = String(status || '').toLowerCase();
  if (['available', 'completed', 'cancelled', 'manual completed'].includes(normalized)) return 'positive';
  if (['busy', 'executing', 'created', 'waiting', 'cancelling', 'pending'].includes(normalized)) return 'working';
  if (['offline', 'abnormal', 'warning', 'startup error'].includes(normalized)) return 'negative';
  return '';
}

function renderMissions(items) {
  if (!items.length) {
    missionsTableBody.innerHTML = '<tr><td colspan="8" class="empty-state">No missions found</td></tr>';
    return;
  }

  const rows = [];
  for (const item of items) {
    const active = [10, 20, 25, 28, 50].includes(item.statusCode);
    const code = escapeHtml(item.missionCode);
    rows.push(`
      <tr>
        <td class="mission-code">${code}</td>
        <td>${escapeHtml(item.robotId)}</td>
        <td>${escapeHtml(item.startNode)} → ${escapeHtml(item.targetNode)}</td>
        <td>${escapeHtml(item.containerCode)}</td>
        <td>${escapeHtml(item.priority)}</td>
        <td><span class="status-badge ${statusClass(item.statusName)}">${escapeHtml(item.statusName)} (${escapeHtml(item.statusCode)})</span></td>
        <td>${formatDate(item.createdAt)}<br /><span class="muted">${formatDate(item.completedAt)}</span></td>
        <td class="row-actions">
          <button class="secondary" type="button" data-mission-history="${code}">${expandedMissionHistory.has(item.missionCode) ? 'Hide history' : 'Show history'}</button>
          ${active ? `<button class="danger" type="button" data-cancel-id="${code}">Force cancel</button>` : ''}
        </td>
      </tr>
    `);

    if (expandedMissionHistory.has(item.missionCode)) {
      rows.push(`<tr class="history-row"><td colspan="8">${renderMissionHistory(item.missionCode)}</td></tr>`);
    }
  }
  missionsTableBody.innerHTML = rows.join('');

  missionsTableBody.querySelectorAll('[data-mission-history]').forEach((button) => {
    button.addEventListener('click', () => toggleMissionHistory(button.dataset.missionHistory));
  });
  missionsTableBody.querySelectorAll('[data-cancel-id]').forEach((button) => {
    button.addEventListener('click', () => cancelMission(button.dataset.cancelId));
  });
}

function renderMissionHistory(code) {
  const records = missionHistory.get(code);
  if (!records) return '<span class="muted">Loading history…</span>';
  if (!records.length) return '<span class="muted">No status history.</span>';
  return `<table class="history-table"><thead><tr><th>Status</th><th>Valid from</th><th>Valid to</th></tr></thead><tbody>${
    records.map((item) => `<tr>
      <td>${escapeHtml(item.statusName)} (${escapeHtml(item.statusCode)})</td>
      <td>${formatDate(item.validFrom)}</td>
      <td>${formatDate(item.validTo)}</td>
    </tr>`).join('')
  }</tbody></table>`;
}

function renderRobots(items) {
  const previousRobotId = robotSelect.value;
  availableRobots = items;
  const readyRobots = items.filter((item) => item.availability === 'Available');
  robotSelect.innerHTML = items.length
    ? items.map((item) => `<option value="${escapeHtml(item.robotId)}" ${item.availability !== 'Available' ? 'disabled' : ''}>${escapeHtml(item.robotId)} · ${escapeHtml(item.availability)}</option>`).join('')
    : '<option value="">No robots found</option>';

  const preferred = readyRobots.find((item) => item.robotId === previousRobotId) || readyRobots[0];
  if (preferred) robotSelect.value = preferred.robotId;
  robotSelect.disabled = readyRobots.length === 0;
  dispatchButton.disabled = readyRobots.length === 0;

  if (!items.length) {
    robotsTableBody.innerHTML = '<tr><td colspan="8" class="empty-state">No robots found</td></tr>';
    return;
  }

  const rows = [];
  for (const item of items) {
    const id = escapeHtml(item.robotId);
    rows.push(`
      <tr>
        <td>${id}<br /><span class="muted">${escapeHtml(item.robotType || '—')}</span></td>
        <td><span class="status-badge ${statusClass(item.availability)}">${escapeHtml(item.availability)}</span></td>
        <td>${escapeHtml(item.statusName)} (${escapeHtml(item.statusCode)})</td>
        <td>${item.batteryLevel == null ? '—' : `${escapeHtml(item.batteryLevel)}%`}</td>
        <td>${escapeHtml(item.mapCode || '—')} / ${escapeHtml(item.nodeCode || '—')}</td>
        <td>${escapeHtml(item.missionCode || '—')}</td>
        <td>${formatDate(item.updatedAt)}</td>
        <td><button class="secondary" type="button" data-robot-history="${id}">${expandedRobotHistory.has(item.robotId) ? 'Hide' : 'Show'}</button></td>
      </tr>
    `);
    if (expandedRobotHistory.has(item.robotId)) {
      rows.push(`<tr class="history-row"><td colspan="8">${renderRobotHistory(item.robotId)}</td></tr>`);
    }
  }
  robotsTableBody.innerHTML = rows.join('');
  robotsTableBody.querySelectorAll('[data-robot-history]').forEach((button) => {
    button.addEventListener('click', () => toggleRobotHistory(button.dataset.robotHistory));
  });
}

function renderRobotHistory(robotId) {
  const records = robotHistory.get(robotId);
  if (!records) return '<span class="muted">Loading history…</span>';
  if (!records.length) return '<span class="muted">No previous status changes.</span>';
  return `<table class="history-table"><thead><tr><th>Availability</th><th>KUKA state</th><th>Valid from</th><th>Valid to</th></tr></thead><tbody>${
    records.map((item) => `<tr>
      <td>${escapeHtml(item.availability)}</td>
      <td>${escapeHtml(item.statusName)} (${escapeHtml(item.statusCode)})</td>
      <td>${formatDate(item.validFrom)}</td>
      <td>${formatDate(item.validTo)}</td>
    </tr>`).join('')
  }</tbody></table>`;
}

async function loadMissions() {
  const headers = getHeaders();
  if (!headers) return;
  const query = missionState.value ? `?state=${encodeURIComponent(missionState.value)}` : '';
  try {
    const items = await fetchJson(`/api/missions${query}`, { headers });
    renderMissions(items);
    connectionStatus.textContent = 'Connected';
    connectionStatus.classList.add('connected');
  } catch (error) {
    connectionStatus.textContent = 'Connection error';
    connectionStatus.classList.remove('connected');
    showToast(error.message, 'error');
  }
}

async function loadRobots() {
  const headers = getHeaders();
  if (!headers) return;
  try {
    renderRobots(await fetchJson('/api/robots', { headers }));
  } catch (error) {
    robotSelect.innerHTML = '<option value="">No data</option>';
    robotSelect.disabled = true;
    dispatchButton.disabled = true;
    connectionStatus.textContent = 'Connection error';
    connectionStatus.classList.remove('connected');
    showToast(error.message, 'error');
  }
}

async function refreshAll() {
  if (refreshing || !apiKeyInput.value.trim()) return;
  refreshing = true;
  try {
    await Promise.all([loadMissions(), loadRobots()]);
  } finally {
    refreshing = false;
  }
}

async function toggleMissionHistory(missionCode) {
  if (expandedMissionHistory.has(missionCode)) {
    expandedMissionHistory.delete(missionCode);
    renderMissions(await fetchMissionList());
    return;
  }

  expandedMissionHistory.add(missionCode);
  try {
    missionHistory.set(missionCode, await fetchJson(
      `/api/missions/${encodeURIComponent(missionCode)}/history`,
      { headers: getHeaders() }
    ));
  } catch (error) {
    expandedMissionHistory.delete(missionCode);
    showToast(error.message, 'error');
  }
  renderMissions(await fetchMissionList());
}

async function toggleRobotHistory(robotId) {
  if (expandedRobotHistory.has(robotId)) {
    expandedRobotHistory.delete(robotId);
  } else {
    expandedRobotHistory.add(robotId);
    try {
      robotHistory.set(robotId, await fetchJson(
        `/api/robots/${encodeURIComponent(robotId)}/history`,
        { headers: getHeaders() }
      ));
    } catch (error) {
      expandedRobotHistory.delete(robotId);
      showToast(error.message, 'error');
    }
  }
  await loadRobots();
}

async function fetchMissionList() {
  const query = missionState.value ? `?state=${encodeURIComponent(missionState.value)}` : '';
  return fetchJson(`/api/missions${query}`, { headers: getHeaders() });
}

async function cancelMission(missionCode) {
  if (!missionCode || !confirm(`Force-cancel mission ${missionCode}? This cannot be undone.`)) return;
  const headers = getHeaders();
  if (!headers) return;
  try {
    await fetchJson(`/api/missions/${encodeURIComponent(missionCode)}/cancel`, {
      method: 'POST',
      headers
    });
    showToast(`Force cancellation requested for ${missionCode}.`);
    await loadMissions();
  } catch (error) {
    showToast(error.message, 'error');
  }
}

saveApiKeyBtn.addEventListener('click', () => {
  if (getApiKey()) {
    showToast('API key saved in this browser.');
    refreshAll();
  }
});

dispatchForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  const headers = getHeaders();
  if (!headers) return;

  const form = new FormData(dispatchForm);
  const payload = {
    robotId: String(form.get('robotId') || '').trim(),
    startNode: String(form.get('startNode') || '').trim(),
    targetNode: String(form.get('targetNode') || '').trim(),
    containerCode: String(form.get('containerCode') || '').trim(),
    priority: Number(form.get('priority'))
  };

  const robot = availableRobots.find((item) => item.robotId === payload.robotId);
  if (!robot || robot.availability !== 'Available') {
    showDispatchStatus('Select an available robot.', 'error');
    return;
  }

  try {
    dispatchButton.disabled = true;
    dispatchButton.textContent = 'Dispatching…';
    showDispatchStatus('Sending mission to KUKA Fleet Manager…', 'pending');
    const result = await fetchJson('/api/missions', {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    showDispatchStatus(`Mission ${result.missionCode} was accepted.`, 'success');
    showToast('Mission accepted by KUKA Fleet Manager.');
    dispatchForm.reset();
    document.getElementById('priority').value = '50';
    await refreshAll();
  } catch (error) {
    showDispatchStatus(error.message, 'error');
    showToast(error.message, 'error');
  } finally {
    dispatchButton.textContent = 'Dispatch mission';
    dispatchButton.disabled = !availableRobots.some((item) => item.availability === 'Available');
  }
});

document.getElementById('refreshMissionsBtn').addEventListener('click', loadMissions);
document.getElementById('refreshRobotsBtn').addEventListener('click', loadRobots);
missionState.addEventListener('change', loadMissions);
setInterval(() => {
  if (autoRefresh.checked) refreshAll();
}, 1000);

function showDispatchStatus(message, tone) {
  dispatchStatus.textContent = message;
  dispatchStatus.className = `dispatch-status ${tone}`;
}
