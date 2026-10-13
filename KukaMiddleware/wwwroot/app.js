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
let availableRobots = [];

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
    showToast('Введите ключ доступа из MIDDLEWARE_API_KEY.', 'error');
    return null;
  }
  localStorage.setItem(storageKey, value);
  return value;
}

function getHeaders() {
  const key = getApiKey();
  if (!key) {
    return null;
  }
  return {
    'Content-Type': 'application/json',
    'X-Api-Key': key,
    'Accept': 'application/json'
  };
}

function formatDate(value) {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString();
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('application/json') ? await response.json() : await response.text();

  if (!response.ok) {
    const validationErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : '';
    const message = typeof body === 'string' ? body : body?.detail || validationErrors || body?.title || `Ошибка запроса (${response.status})`;
    throw new Error(message);
  }

  return body;
}

function renderMissions(items) {
  if (!items || items.length === 0) {
    missionsTableBody.innerHTML = '<tr><td colspan="5" class="empty-state">Активных миссий нет</td></tr>';
    return;
  }

  missionsTableBody.innerHTML = items.map((item) => `
    <tr>
      <td class="mission-code">${escapeHtml(item.missionCode || '—')}</td>
      <td>${escapeHtml(item.robotId || '—')}</td>
      <td><span class="status-badge ${statusClass(item.status)}">${escapeHtml(statusLabel(item.status))}</span></td>
      <td>${formatDate(item.createdAt)}</td>
      <td>
        ${isActiveMission(item.status) ? `<button class="danger" data-cancel-id="${escapeHtml(item.missionCode)}" title="Отменить миссию">Отмена</button>` : ''}
      </td>
    </tr>
  `).join('');

  missionsTableBody.querySelectorAll('[data-cancel-id]').forEach((button) => {
    button.addEventListener('click', async () => {
      const missionCode = button.getAttribute('data-cancel-id');
      await cancelMission(missionCode);
    });
  });
}

function renderRobots(items) {
  const previousRobotId = robotSelect.value;
  availableRobots = items || [];
  const readyRobots = availableRobots.filter((item) => item.status === 'AVAILABLE');
  robotSelect.innerHTML = availableRobots.length
    ? availableRobots.map((item) => `<option value="${escapeHtml(item.robotId)}" ${item.status !== 'AVAILABLE' ? 'disabled' : ''}>${escapeHtml(item.robotId)} · ${escapeHtml(statusLabel(item.status))}</option>`).join('')
    : '<option value="">Нет роботов</option>';
  const preferredRobot = readyRobots.find((item) => item.robotId === previousRobotId)
    || readyRobots.find((item) => item.robotId === '3')
    || readyRobots[0];
  if (preferredRobot) {
    robotSelect.value = preferredRobot.robotId;
  }
  robotSelect.disabled = readyRobots.length === 0;
  dispatchButton.disabled = readyRobots.length === 0;

  if (!items || items.length === 0) {
    robotsTableBody.innerHTML = '<tr><td colspan="3" class="empty-state">Роботы не найдены</td></tr>';
    return;
  }

  robotsTableBody.innerHTML = items.map((item) => `
    <tr>
      <td>${escapeHtml(item.robotId || '—')}</td>
      <td><span class="status-badge ${statusClass(item.status)}">${escapeHtml(statusLabel(item.status))}</span></td>
      <td>${formatDate(item.lastUpdated)}</td>
    </tr>
  `).join('');
}

async function loadMissions() {
  const headers = getHeaders();
  if (!headers) return;

  try {
    const data = await fetchJson('/api/missions', { headers });
    renderMissions(data);
    connectionStatus.textContent = 'Подключено';
    connectionStatus.classList.add('connected');
  } catch (error) {
    connectionStatus.textContent = 'Ошибка подключения';
    connectionStatus.classList.remove('connected');
    showToast(error.message, 'error');
  }
}

async function loadRobots() {
  const headers = getHeaders();
  if (!headers) return;

  try {
    const data = await fetchJson('/api/robots', { headers });
    renderRobots(data);
  } catch (error) {
    robotSelect.innerHTML = '<option value="">Нет данных</option>';
    robotSelect.disabled = true;
    dispatchButton.disabled = true;
    connectionStatus.textContent = 'Ошибка подключения';
    connectionStatus.classList.remove('connected');
    showToast(error.message, 'error');
  }
}

async function cancelMission(missionCode) {
  const headers = getHeaders();
  if (!headers || !missionCode) return;

  try {
    await fetchJson(`/api/missions/${encodeURIComponent(missionCode)}`, {
      method: 'DELETE',
      headers
    });
    showToast(`Запрошена отмена миссии ${missionCode}`);
    await loadMissions();
  } catch (error) {
    showToast(error.message, 'error');
  }
}

saveApiKeyBtn.addEventListener('click', () => {
  if (getApiKey()) {
    showToast('Ключ сохранён в этом браузере.');
    loadMissions();
    loadRobots();
  }
});

dispatchForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  const headers = getHeaders();
  if (!headers) return;

  const form = new FormData(dispatchForm);
  const targetPoint = form.get('targetPoint').toString().trim();
  const robotId = robotSelect.value;
  const robot = availableRobots.find((item) => item.robotId === robotId);
  if (!robot || robot.status !== 'AVAILABLE') {
    showDispatchStatus('Выберите доступного робота.', 'error');
    return;
  }
  if (!targetPoint) {
    showDispatchStatus('Введите UUID узла назначения.', 'error');
    return;
  }

  const payload = {
    orgId: 'UNIVERSAL',
    robotId,
    missionType: 'MOVE',
    priority: 50,
    missionData: [{
      sequence: 1,
      position: targetPoint,
      type: 'NODE_POINT',
      name: `Go to ${targetPoint}`
    }]
  };

  try {
    dispatchButton.disabled = true;
    dispatchButton.textContent = 'Отправка...';
    showDispatchStatus(`Отправляю робота ${robotId} к узлу ${targetPoint}...`, 'pending');
    await fetchJson('/api/missions', {
      method: 'POST',
      headers,
      body: JSON.stringify(payload)
    });
    showDispatchStatus(`Миссия отправлена: робот ${robotId} → ${targetPoint}. Статус появится после ответа Fleet Manager.`, 'success');
    showToast('Миссия принята KUKA Fleet Manager.');
    await loadMissions();
    await loadRobots();
  } catch (error) {
    showDispatchStatus(error.message, 'error');
    showToast(error.message, 'error');
  } finally {
    dispatchButton.textContent = 'Отправить';
    dispatchButton.disabled = !availableRobots.some((item) => item.status === 'AVAILABLE');
  }
});

document.getElementById('refreshMissionsBtn').addEventListener('click', loadMissions);
document.getElementById('refreshRobotsBtn').addEventListener('click', loadRobots);

loadMissions();
loadRobots();
setInterval(() => {
  if (apiKeyInput.value.trim()) {
    loadMissions();
    loadRobots();
  }
}, 10000);

function showDispatchStatus(message, tone) {
  dispatchStatus.textContent = message;
  dispatchStatus.className = `dispatch-status ${tone}`;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[character]);
}

function statusLabel(status) {
  return ({
    AVAILABLE: 'Свободен',
    BUSY: 'Занят',
    DEPARTURE: 'Выезжает',
    OFFLINE: 'Не в сети',
    CHARGING: 'Заряжается',
    PENDING: 'Ожидает',
    CREATED: 'Создана',
    EXECUTING: 'Выполняется',
    WAITING: 'Ожидает',
    CANCELLING: 'Отменяется',
    COMPLETED: 'Завершена',
    CANCELLED: 'Отменена',
    MANUAL_COMPLETED: 'Завершена вручную',
    WARNING: 'Предупреждение',
    STARTUP_ERROR: 'Ошибка запуска',
    ABNORMAL: 'Ошибка'
  })[status] || status || 'Неизвестно';
}

function statusClass(status) {
  if (['AVAILABLE', 'COMPLETED', 'MANUAL_COMPLETED'].includes(status)) return 'positive';
  if (['BUSY', 'EXECUTING', 'PENDING', 'CREATED', 'WAITING'].includes(status)) return 'working';
  if (['OFFLINE', 'ABNORMAL', 'WARNING', 'STARTUP_ERROR'].includes(status)) return 'negative';
  return '';
}

function isActiveMission(status) {
  return ['PENDING', 'CREATED', 'EXECUTING', 'WAITING', 'CANCELLING'].includes(status);
}
