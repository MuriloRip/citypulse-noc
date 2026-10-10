const $ = (selector) => document.querySelector(selector);
let snapshot = null;
let indicator = null;
let triageAsset = null;
let triageTimer = null;
let discoveryDevices = [];
let selectedDiscoveryAddresses = new Set();

const labels = {
  online: 'ONLINE',
  degraded: 'DEGRADADO',
  pendingTriage: 'AGUARDANDO TRIAGEM',
  noPower: 'FALHA DE ENERGIA',
  down: 'OFFLINE / CRÍTICO',
  unreachable: 'INALCANÇÁVEL'
};

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (character) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[character]);
}

function normalizeSearchText(value) {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('pt-BR');
}

function parseUtc(value) {
  if (typeof value !== 'string') return NaN;
  const utcValue = /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`;
  return Date.parse(utcValue);
}

function formatDate(value) {
  const timestamp = parseUtc(value);
  if (!Number.isFinite(timestamp)) return 'sem registro';
  const minutes = Math.floor(Math.max(0, Date.now() - timestamp) / 60000);
  if (minutes < 1) return 'agora';
  if (minutes < 60) return `${minutes} min atrás`;
  return `${Math.floor(minutes / 60)}h atrás`;
}

function statusClass(status) {
  return status === 'online' ? 'online'
    : status === 'degraded' ? 'degraded'
      : status === 'pendingTriage' ? 'pending-triage'
        : status === 'noPower' ? 'no-power'
          : status === 'down' ? 'down' : 'unreachable';
}

function deviceIconForCategory(category) {
  const value = String(category ?? '').toLocaleLowerCase('pt-BR');
  if (value.includes('impressora') || value.includes('printer')) return 'printer';
  if (value.includes('câmera') || value.includes('camera')) return 'camera';
  if (value.includes('computador') || value.includes('computer')) return 'computer';
  if (value.includes('servidor') || value.includes('server') || value.includes('armazenamento') || value.includes('storage') || value.includes(' nas') || value.includes(' san')) return 'server';
  if (value === 'rede' || value.includes('equipamento de rede') || value.includes('switch') || value.includes('roteador') || value.includes('router') || value.includes('firewall') || value.includes('access point') || value.includes('ponto de acesso')) return 'network';
  return 'unknown';
}

async function request(path, options) {
  const response = await fetch(path, options);
  const body = await response.text();
  let result = null;
  if (body) {
    try {
      result = JSON.parse(body);
    } catch {
      if (response.ok) throw new Error('O serviço retornou uma resposta inválida.');
    }
  }

  if (!response.ok) {
    const message = typeof result?.error === 'string'
      ? result.error
      : typeof result?.title === 'string'
        ? result.title
        : `Falha na solicitação (${response.status}).`;
    throw new Error(message);
  }

  return result;
}

function renderMetrics() {
  const data = snapshot;
  const hasAvailability = typeof data.availability === 'number' && Number.isFinite(data.availability);
  const availability = hasAvailability ? data.availability : null;
  const percent = availability === null ? '—' : availability.toFixed(2).replace('.', ',');
  const ringPercent = availability === null ? '—' : `${availability.toFixed(1).replace('.', ',')}%`;

  $('#availability').innerHTML = `${percent}<span>%</span>`;
  $('#ring-percent').textContent = ringPercent;
  $('#mttr').innerHTML = `${typeof data.mttr === 'number' ? data.mttr : '—'}<span> min</span>`;
  $('#indicator-availability').textContent = typeof indicator?.availabilityPercent === 'number'
    ? `${indicator.availabilityPercent.toFixed(2).replace('.', ',')}%`
    : '—';
  $('#indicator-mttr').textContent = `${indicator?.meanDowntimeMinutes ?? 0} min`;
  $('#indicator-checks').textContent = indicator?.totalChecks ?? 0;
  $('#indicator-incidents').textContent = indicator?.incidentCount ?? 0;
  $('#indicator-downtime').textContent = indicator?.downtimeMinutes ?? 0;
  $('#active-incidents').textContent = data.activeIncidents;
  $('#total-assets').textContent = data.totalAssets;
  $('#online-count').textContent = data.online;
  $('#degraded-count').textContent = data.degraded;
  $('#health-online').innerHTML = `${data.online} <small>ativo${data.online === 1 ? '' : 's'}</small>`;
  $('#health-degraded').innerHTML = `${data.degraded} <small>ativo${data.degraded === 1 ? '' : 's'}</small>`;

  const unavailable = (data.down || 0) + (data.noPower || 0);
  $('#health-down').innerHTML = `${unavailable} <small>ativo${unavailable === 1 ? '' : 's'}</small>`;
  $('#health-unreachable').innerHTML = `${data.unreachable} <small>ativo${data.unreachable === 1 ? '' : 's'}</small>`;
  $('#nav-count').textContent = data.activeIncidents;
  $('#last-check').textContent = formatDate(data.lastPollAt);

  const headline = data.totalAssets === 0
    ? 'Cadastre ativos para iniciar o monitoramento.'
    : data.pendingTriage
    ? 'Há ativos aguardando validação operacional.'
    : data.down || data.unreachable
      ? 'Há falhas impactando ativos monitorados.'
      : data.noPower
        ? 'Um ou mais locais reportaram falta de energia.'
        : data.degraded
          ? 'A infraestrutura está operando com serviços sob observação.'
          : 'Os ativos monitorados estão respondendo.';
  $('#headline').textContent = headline;
  const recentPoll = parseUtc(data.lastPollAt);
  const workerIsCurrent = data.cycle > 0 && Number.isFinite(recentPoll) && Date.now() - recentPoll < 75000;
  $('#motor-status').textContent = workerIsCurrent ? 'Motor ativo' : data.cycle > 0 ? 'Verificação atrasada' : 'Aguardando primeira verificação';
  $('#motor-status-dot').className = `status-dot ${workerIsCurrent ? 'online' : 'offline'}`;
  const ringProgress = $('.health-ring-progress');
  const circumference = 2 * Math.PI * 44;
  const progress = availability === null ? 0 : Math.min(100, availability) / 100;
  ringProgress.setAttribute('stroke-dasharray', `${circumference * progress} ${circumference}`);
}

function renderTopology() {
  const nodes = $('#topology-nodes');
  const assetsById = Object.fromEntries(snapshot.assets.map((asset) => [asset.id, asset]));
  $('#topology-count').textContent = `${snapshot.assets.length} ativo${snapshot.assets.length === 1 ? '' : 's'}`;
  nodes.innerHTML = snapshot.assets.length
    ? snapshot.assets.map((asset) => {
      const status = statusClass(asset.status);
      const parent = asset.parentId ? assetsById[asset.parentId] : null;
      const relation = parent ? `Dependência de ${parent.name}` : 'Ativo raiz';
      const icon = deviceIconForCategory(asset.category);
      return `<div class="node ${parent ? '' : 'root-node'}">
        <span class="node-signal ${status}"></span>
        <svg class="node-device-icon" role="img" aria-label="${escapeHtml(asset.category)}"><use href="/icons/device-icons.svg#${icon}"></use></svg>
        <div><strong>${escapeHtml(asset.name)}</strong>
        <small>${escapeHtml(asset.address)} · ${escapeHtml(asset.location)} · ${escapeHtml(relation)}</small></div>
        <b class="status-chip ${status}-chip">${labels[asset.status] || 'DESCONHECIDO'}</b>
      </div>`;
    }).join('')
    : '<p class="incident-meta">Cadastre ativos para visualizar as dependências.</p>';
}

function renderDiscoveryResults() {
  const results = $('#discovery-results');
  results.innerHTML = discoveryDevices.map((device, index) => {
    const existingAsset = (snapshot?.assets ?? []).find((asset) => asset.address === device.address);
    const icon = ['computer', 'server', 'network', 'printer', 'camera'].includes(device.icon)
      ? device.icon
      : 'unknown';
    const confidence = device.confidence === 'medium' ? 'Média' : 'Baixa';
    return `<article class="discovery-device">
      <svg class="device-illustration" role="img" aria-label="${escapeHtml(device.category)}"><use href="/icons/device-icons.svg#${icon}"></use></svg>
      <div class="discovery-device-main">
        <strong>${escapeHtml(device.address)}</strong>
        <span>${escapeHtml(device.category)} · confiança ${confidence}</span>
        <small>${escapeHtml(device.evidence)}</small>
        <small>${device.pingLatencyMs === null ? 'ICMP sem resposta' : `ICMP ${device.pingLatencyMs} ms`} · portas TCP ${device.openTcpPorts.length ? escapeHtml(device.openTcpPorts.join(', ')) : 'sem resposta'}</small>
      </div>
      <div class="discovery-device-actions">
        <label><input class="select-discovered-device" type="checkbox" data-index="${index}" ${existingAsset ? 'disabled' : selectedDiscoveryAddresses.has(device.address) ? 'checked' : ''} /> Selecionar</label>
        <button class="secondary-button add-discovered" data-index="${index}" ${existingAsset ? 'disabled' : ''}>
          ${existingAsset ? 'Já inventariado' : 'Adicionar'}
        </button>
      </div>
    </article>`;
  }).join('');

  results.querySelectorAll('.select-discovered-device').forEach((checkbox) => checkbox.addEventListener('change', () => {
    const device = discoveryDevices[Number(checkbox.dataset.index)];
    if (!device) return;
    if (checkbox.checked) selectedDiscoveryAddresses.add(device.address);
    else selectedDiscoveryAddresses.delete(device.address);
    updateDiscoveryBulkActions();
  }));

  results.querySelectorAll('.add-discovered').forEach((button) => button.addEventListener('click', async () => {
    const device = discoveryDevices[Number(button.dataset.index)];
    if (!device) return;
    button.disabled = true;
    try {
      const result = await request('/api/assets/discovered/bulk', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          authorized: $('#discovery-authorized').checked,
          devices: [{ address: device.address, category: device.category }]
        })
      });
      selectedDiscoveryAddresses.delete(device.address);
      await refresh();
      renderDiscoveryResults();
      showToast(result.addedCount
        ? `${device.address} adicionado ao inventário.`
        : `${device.address} já estava no inventário.`);
    } catch (error) {
      button.disabled = false;
      showToast(error.message);
    }
  }));
  updateDiscoveryBulkActions();
}

function updateDiscoveryBulkActions() {
  const bulkActions = $('#discovery-bulk-actions');
  const availableDevices = discoveryDevices.filter((device) =>
    !(snapshot?.assets ?? []).some((asset) => asset.address === device.address));
  const selectedCount = availableDevices.filter((device) => selectedDiscoveryAddresses.has(device.address)).length;
  bulkActions.classList.toggle('hidden', discoveryDevices.length === 0);
  $('#add-selected-devices').disabled = selectedCount === 0;
  $('#add-selected-devices').textContent = `Adicionar selecionados (${selectedCount})`;
  $('#select-discovered').checked = availableDevices.length > 0 && selectedCount === availableDevices.length;
  $('#select-discovered').indeterminate = selectedCount > 0 && selectedCount < availableDevices.length;
  $('#select-discovered').disabled = availableDevices.length === 0;
}

function renderAssets() {
  const query = normalizeSearchText($('#asset-search').value.trim());
  const status = $('#asset-status-filter').value;
  const filteredAssets = snapshot.assets.filter((asset) => {
    const matchesQuery = !query || [asset.name, asset.address, asset.category, asset.location]
      .some((value) => normalizeSearchText(value).includes(query));
    return matchesQuery && (!status || asset.status === status);
  });
  $('#asset-results-count').textContent = snapshot.assets.length === 0
    ? 'Nenhum ativo cadastrado.'
    : `Exibindo ${filteredAssets.length} de ${snapshot.assets.length} ativos`;
  $('#assets-table').innerHTML = filteredAssets.length
    ? filteredAssets.map((asset) => {
      const status = statusClass(asset.status);
      return `<tr>
        <td><div class="asset-name"><i class="dot ${status}"></i>${escapeHtml(asset.name)}</div></td>
        <td class="mono">${escapeHtml(asset.address)}</td>
        <td>${escapeHtml(asset.category)}</td>
        <td><span class="status-tag ${status}"><i class="dot ${status}"></i>${labels[asset.status] || 'DESCONHECIDO'}</span></td>
        <td class="mono">${asset.latencyMs === null ? '—' : `${asset.latencyMs} ms`}</td>
        <td>
          <button class="text-button triage-asset" data-id="${asset.id}">Triagem</button>
          <button class="text-button edit-asset" data-id="${asset.id}">Editar</button>
          <button class="text-button delete-asset" data-id="${asset.id}" aria-label="Excluir ${escapeHtml(asset.name)}">Excluir</button>
        </td>
      </tr>`;
    }).join('')
    : `<tr><td colspan="6" class="empty-state">${snapshot.assets.length ? 'Nenhum ativo corresponde aos filtros.' : 'Nenhum ativo cadastrado.'}</td></tr>`;

  document.querySelectorAll('.delete-asset').forEach((button) => button.addEventListener('click', async () => {
    const asset = snapshot.assets.find((item) => item.id === button.dataset.id);
    if (!asset || !confirm(`Excluir "${asset.name}" do inventário?`)) return;
    try {
      await request(`/api/assets/${button.dataset.id}`, { method: 'DELETE' });
      await refresh();
      showToast('Ativo removido do inventário.');
    } catch (error) {
      showToast(error.message);
    }
  }));
  document.querySelectorAll('.edit-asset').forEach((button) => button.addEventListener('click', () => {
    openEditor(snapshot.assets.find((asset) => asset.id === button.dataset.id));
  }));
  document.querySelectorAll('.triage-asset').forEach((button) => button.addEventListener('click', () => {
    startTriage(snapshot.assets.find((asset) => asset.id === button.dataset.id));
  }));
}

function exportAssets() {
  const rows = [
    ['Nome', 'Endereço', 'Categoria', 'Status', 'Criticidade', 'Localização', 'Ativo pai', 'Latência (ms)', 'Última verificação'],
    ...snapshot.assets.map((asset) => [
      asset.name,
      asset.address,
      asset.category,
      labels[asset.status] || asset.status,
      asset.tier,
      asset.location,
      snapshot.assets.find((item) => item.id === asset.parentId)?.name || '',
      asset.latencyMs ?? '',
      asset.lastCheckedAtUtc || ''
    ])
  ];
  const csv = rows.map((row) => row.map((value) => {
    const text = String(value ?? '');
    const safeText = /^[\s]*[=+\-@]/.test(text) ? `'${text}` : text;
    return `"${safeText.replace(/"/g, '""')}"`;
  }).join(',')).join('\r\n');
  const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `citypulse-ativos-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function renderIncidents() {
  const assetsById = Object.fromEntries(snapshot.assets.map((asset) => [asset.id, asset]));
  $('#incidents-list').innerHTML = snapshot.incidents.length
    ? snapshot.incidents.map((incident) => {
      const asset = assetsById[incident.assetId];
      return `<div class="incident">
        <span class="incident-bar ${escapeHtml(incident.severity)}"></span>
        <div><div class="incident-title">${escapeHtml(incident.title)}</div>
        <div class="incident-meta">${escapeHtml(incident.type)} · ${escapeHtml(asset?.location || 'Local não informado')}</div></div>
        <span class="incident-time">${incident.resolvedAtUtc ? 'resolvido' : formatDate(incident.startedAtUtc)}</span>
      </div>`;
    }).join('')
    : '<p class="incident-meta">Nenhum incidente registrado.</p>';
}

function openEditor(asset = null) {
  const form = $('#asset-form');
  form.reset();
  form.elements.id.value = asset?.id || '';
  $('#modal-title').textContent = asset ? 'Editar ativo' : 'Cadastrar ativo';
  form.elements.parentId.innerHTML = '<option value="">Sem dependência</option>' +
    snapshot.assets.filter((item) => item.id !== asset?.id)
      .map((item) => `<option value="${item.id}">${escapeHtml(item.name)}</option>`).join('');

  if (asset) {
    Object.entries(asset).forEach(([key, value]) => {
      if (form.elements[key]) form.elements[key].value = value ?? '';
    });
  }
  $('#form-error').textContent = '';
  $('#asset-modal').showModal();
}

async function refresh() {
  try {
    [snapshot, indicator] = await Promise.all([
      request('/api/snapshot'),
      request('/api/indicator')
    ]);
    renderMetrics();
    renderAssets();
    renderIncidents();
    renderTopology();
    const pendingTriage = snapshot.assets.find((asset) => asset.status === 'pendingTriage');
    if (pendingTriage) showTriage(pendingTriage);
    else if (triageAsset) closeTriage();
    return true;
  } catch (error) {
    $('#headline').textContent = 'Não foi possível carregar os dados do serviço.';
    console.error('Falha ao atualizar o painel:', error);
    return false;
  }
}

async function poll() {
  await request('/api/poll', { method: 'POST' });
  return refresh();
}

function showToast(message) {
  const toast = $('#toast');
  toast.textContent = message;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), 3500);
}

async function startTriage(asset = null) {
  const target = asset || snapshot.assets.find((item) => item.status === 'online') || snapshot.assets[0];
  if (!target) return showToast('Cadastre um ativo antes de iniciar a triagem.');

  try {
    await request(`/api/assets/${target.id}/status`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ resolution: null })
    });
    target.status = 'pendingTriage';
    target.triageStartedAtUtc = new Date().toISOString();
    showTriage(target);
    $('#operations-console').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    await refresh();
    showToast('Triagem iniciada. O ativo ficará sem sondagem até sua resolução.');
  } catch (error) {
    showToast(error.message);
  }
}

async function resolveTriage(resolution) {
  if (!triageAsset) return;
  try {
    await request(`/api/assets/${triageAsset.id}/status`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ resolution })
    });
    await refresh();
    showToast(resolution === 'POWER_OUTAGE'
      ? 'Falta de energia registrada. Incidente técnico não foi acionado.'
      : resolution === 'RESTORED'
        ? 'Operação marcada como restaurada; será verificada na próxima sondagem.'
        : 'Falha de rede registrada e incidente técnico aberto.');
  } catch (error) {
    showToast(error.message);
  }
}

function closeTriage() {
  clearInterval(triageTimer);
  triageTimer = null;
  triageAsset = null;
  $('#triage-card').classList.add('hidden');
  $('#start-triage').classList.remove('hidden');
  $('#triage-timer').textContent = '05:00';
}

function showTriage(asset) {
  const sameAsset = triageAsset?.id === asset.id;
  triageAsset = asset;
  $('#triage-card').classList.remove('hidden');
  $('#start-triage').classList.add('hidden');
  $('#triage-message').textContent = `Triagem iniciada para ${asset.name}. Confirme a situação com a equipe responsável.`;
  if (sameAsset && triageTimer) return;

  clearInterval(triageTimer);
  const startedAt = parseUtc(asset.triageStartedAtUtc);
  let remaining = Number.isFinite(startedAt)
    ? Math.max(0, 300 - Math.floor((Date.now() - startedAt) / 1000))
    : 300;
  const updateTimer = () => {
    $('#triage-timer').textContent = `${String(Math.floor(remaining / 60)).padStart(2, '0')}:${String(remaining % 60).padStart(2, '0')}`;
  };
  updateTimer();
  if (remaining === 0) return;

  triageTimer = setInterval(() => {
    remaining = Math.max(0, remaining - 1);
    updateTimer();
    if (remaining === 0) {
      clearInterval(triageTimer);
      triageTimer = null;
      refresh();
    }
  }, 1000);
}

$('#refresh').addEventListener('click', async () => {
  try {
    if (await poll()) showToast('Verificação concluída.');
  } catch (error) {
    showToast(error.message);
  }
});
$('#start-triage').addEventListener('click', () => startTriage());
$('#power-outage').addEventListener('click', () => resolveTriage('POWER_OUTAGE'));
$('#network-fault').addEventListener('click', () => resolveTriage('NETWORK_FAULT'));
$('#recover').addEventListener('click', () => resolveTriage('RESTORED'));
$('#add-asset').addEventListener('click', () => openEditor());
$('#asset-search').addEventListener('input', renderAssets);
$('#asset-status-filter').addEventListener('change', renderAssets);
$('#export-assets').addEventListener('click', exportAssets);
$('#close-asset-modal').addEventListener('click', () => $('#asset-modal').close());
$('#asset-modal').addEventListener('keydown', (event) => {
  if (event.key === 'Escape') $('#asset-modal').close();
});

$('#discovery-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const submitButton = $('#discover-network');
  submitButton.disabled = true;
  submitButton.textContent = 'Verificando…';
  $('#discovery-summary').textContent = 'Verificando endereços privados e serviços de rede. Isso pode levar alguns segundos.';
  $('#discovery-results').replaceChildren();
  try {
    const result = await request('/api/discovery/scan', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        cidr: $('#discovery-cidr').value.trim(),
        authorized: $('#discovery-authorized').checked
      })
    });
    discoveryDevices = result.devices;
    selectedDiscoveryAddresses = new Set(discoveryDevices
      .filter((device) => !(snapshot?.assets ?? []).some((asset) => asset.address === device.address))
      .map((device) => device.address));
    $('#discovery-summary').textContent = `${result.devicesFound} dispositivo(s) encontrado(s) em ${result.network}; ${result.addressesChecked} endereços verificados. Confira os indícios antes de adicionar ao inventário.`;
    renderDiscoveryResults();
  } catch (error) {
    $('#discovery-summary').textContent = error.message;
  } finally {
    submitButton.disabled = false;
    submitButton.textContent = 'Verificar rede';
  }
});

$('#select-discovered').addEventListener('change', () => {
  const existingAddresses = new Set((snapshot?.assets ?? []).map((asset) => asset.address));
  selectedDiscoveryAddresses = $('#select-discovered').checked
    ? new Set(discoveryDevices.filter((device) => !existingAddresses.has(device.address)).map((device) => device.address))
    : new Set();
  renderDiscoveryResults();
});

$('#add-selected-devices').addEventListener('click', async () => {
  const selectedDevices = discoveryDevices.filter((device) => selectedDiscoveryAddresses.has(device.address));
  if (selectedDevices.length === 0) return;
  const button = $('#add-selected-devices');
  button.disabled = true;
  button.textContent = 'Adicionando…';
  try {
    const result = await request('/api/assets/discovered/bulk', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        authorized: $('#discovery-authorized').checked,
        devices: selectedDevices.map((device) => ({ address: device.address, category: device.category }))
      })
    });
    for (const device of selectedDevices) selectedDiscoveryAddresses.delete(device.address);
    await refresh();
    renderDiscoveryResults();
    showToast(`${result.addedCount} adicionado(s); ${result.alreadyPresentCount} já estavam no inventário.`);
    $('#discovery-summary').textContent += ` ${result.addedCount} adicionado(s) ao inventário; ${result.alreadyPresentCount} já existiam.`;
  } catch (error) {
    showToast(error.message);
  } finally {
    updateDiscoveryBulkActions();
  }
});

$('#asset-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(event.currentTarget));
  const method = data.id ? 'PUT' : 'POST';
  const path = data.id ? `/api/assets/${data.id}` : '/api/assets';
  delete data.id;
  data.parentId = data.parentId || null;
  try {
    await request(path, {
      method,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(data)
    });
    $('#asset-modal').close();
    await refresh();
    showToast(method === 'PUT' ? 'Ativo atualizado.' : 'Novo ativo adicionado ao monitoramento.');
  } catch (error) {
    $('#form-error').textContent = error.message;
  }
});

document.querySelectorAll('[data-section]').forEach((button) => button.addEventListener('click', () => {
  document.querySelectorAll('.nav-item').forEach((item) => item.classList.remove('active'));
  if (button.classList.contains('nav-item')) button.classList.add('active');
  if (button.dataset.section === 'overview') window.scrollTo({ top: 0, behavior: 'smooth' });
  else if (button.dataset.section === 'topology') $('#topology-view').scrollIntoView({ behavior: 'smooth' });
  else if (button.dataset.section === 'discovery') $('#network-discovery').scrollIntoView({ behavior: 'smooth' });
  else if (button.dataset.section === 'assets') $('.assets-panel').scrollIntoView({ behavior: 'smooth' });
  else if (button.dataset.section === 'incidents') $('.incident-panel').scrollIntoView({ behavior: 'smooth' });
}));

refresh();
setInterval(refresh, 7000);

if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost' || location.hostname === '127.0.0.1')) {
  navigator.serviceWorker.register('/service-worker.js')
    .catch((error) => console.error('Não foi possível registrar o app instalável:', error));
}
