import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PUBLIC = join(ROOT, 'public');
const PORT = Number(process.env.PORT || 4173);
const POLL_INTERVAL_MS = 7000;
const MAX_CONCURRENCY = 8;
const state = { assets: [], incidents: [], lastPollAt: new Date().toISOString(), scenario: 'normal', cycle: 0 };

const seed = [
  { id: 'gw-central', name: 'Gateway Central', address: '10.44.0.1', category: 'Rede', tier: 'high', location: 'Paço Municipal', parentId: null, protocol: 'ICMP', status: 'online', latency: 8, consecutiveFailures: 0, uptime: 99.98 },
  { id: 'ubs-centro', name: 'UBS Centro', address: '10.44.12.10', category: 'Saúde', tier: 'high', location: 'UBS Centro', parentId: 'gw-central', protocol: 'ICMP', status: 'online', latency: 18, consecutiveFailures: 0, uptime: 99.91 },
  { id: 'escola-urupes', name: 'Escola Urupe', address: '10.44.24.20', category: 'Educação', tier: 'medium', location: 'Jardim Urupe', parentId: 'gw-central', protocol: 'ICMP', status: 'degraded', latency: 122, consecutiveFailures: 0, uptime: 99.64 },
  { id: 'portal', name: 'Portal do Cidadão', address: 'portal.campomourao.pr.gov.br', category: 'Serviços', tier: 'high', location: 'Data Center', parentId: 'gw-central', protocol: 'HTTP', status: 'online', latency: 42, consecutiveFailures: 0, uptime: 99.97 },
  { id: 'camera-01', name: 'Câmera Av. Capitão', address: '10.44.31.8', category: 'Segurança', tier: 'low', location: 'Av. Capitão Índio Bandeira', parentId: 'gw-central', protocol: 'ICMP', status: 'online', latency: 31, consecutiveFailures: 0, uptime: 99.52 },
  { id: 'switch-escolas', name: 'Switch Escolas Norte', address: '10.44.24.2', category: 'Rede', tier: 'medium', location: 'Núcleo Norte', parentId: 'gw-central', protocol: 'ICMP', status: 'online', latency: 14, consecutiveFailures: 0, uptime: 99.88 }
];
state.assets = seed.map((a) => ({ ...a, createdAt: new Date().toISOString() }));
state.incidents = [
  { id: 'inc-1001', assetId: 'escola-urupes', title: 'Latência acima do limiar', type: 'Degradação de rede', severity: 'medium', startedAt: new Date(Date.now() - 42 * 60000).toISOString(), resolvedAt: null, durationMinutes: 42 },
  { id: 'inc-0998', assetId: 'portal', title: 'HTTP 500 intermitente', type: 'Erro de aplicação', severity: 'high', startedAt: new Date(Date.now() - 3 * 86400000).toISOString(), resolvedAt: new Date(Date.now() - 3 * 86400000 + 11 * 60000).toISOString(), durationMinutes: 11 }
];

function json(res, status, body) { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); }
function parseBody(req) { return new Promise((resolve, reject) => { let raw = ''; req.on('data', (chunk) => { raw += chunk; if (raw.length > 10000) reject(new Error('payload too large')); }); req.on('end', () => { try { resolve(raw ? JSON.parse(raw) : {}); } catch { reject(new Error('invalid json')); } }); }); }
function safeAddress(value) {
  if (!value || typeof value !== 'string' || value.length > 253) return false;
  if (/^https?:\/\//i.test(value)) return new URL(value).hostname.length > 0;
  const parts = value.split('.').map(Number);
  return parts.length === 4 && parts.every((n) => Number.isInteger(n) && n >= 0 && n <= 255) && (parts[0] === 10 || parts[0] === 127 || (parts[0] === 192 && parts[1] === 168) || (parts[0] === 172 && parts[1] >= 16 && parts[1] <= 31));
}
function metrics() {
  const active = state.incidents.filter((i) => !i.resolvedAt);
  const resolved = state.incidents.filter((i) => i.resolvedAt);
  const mttr = resolved.length ? Math.round(resolved.reduce((sum, i) => sum + i.durationMinutes, 0) / resolved.length) : 0;
  const availability = state.assets.length ? state.assets.reduce((sum, a) => sum + a.uptime, 0) / state.assets.length : 100;
  return { availability: Number(availability.toFixed(2)), mttr, activeIncidents: active.length, totalAssets: state.assets.length, online: state.assets.filter((a) => a.status === 'online').length, degraded: state.assets.filter((a) => a.status === 'degraded').length, down: state.assets.filter((a) => a.status === 'down').length, unreachable: state.assets.filter((a) => a.status === 'unreachable').length };
}
async function probe(asset) {
  await new Promise((resolve) => setTimeout(resolve, 18 + Math.random() * 35));
  const parent = state.assets.find((a) => a.id === asset.parentId);
  if (parent?.status === 'down') return { ok: false, status: 'unreachable', latency: null, type: 'Falha em cascata' };
  if (state.scenario === 'outage' && asset.id === 'gw-central') return { ok: false, status: 'down', latency: null, type: 'Falha de rede' };
  if (asset.id === 'escola-urupes') return { ok: true, status: 'degraded', latency: 100 + Math.round(Math.random() * 40), type: 'Degradação de rede' };
  return { ok: true, status: 'online', latency: 8 + Math.round(Math.random() * 45), type: asset.protocol === 'HTTP' ? 'HTTP 200' : 'ICMP reply' };
}
async function runPollCycle() {
  const queue = [...state.assets];
  const results = new Map();
  async function worker() {
    while (queue.length) {
      const asset = queue.shift();
      if (!asset) return;
      results.set(asset.id, await probe(asset));
    }
  }
  await Promise.all(Array.from({ length: Math.min(MAX_CONCURRENCY, state.assets.length) }, worker));
  for (const asset of state.assets) {
    const result = results.get(asset.id);
    if (result.ok) {
      asset.consecutiveFailures = 0;
      asset.status = result.status;
      asset.latency = result.latency;
    } else {
      asset.consecutiveFailures += 1;
      if (asset.consecutiveFailures >= 3) asset.status = result.status;
      asset.latency = null;
      const existing = state.incidents.find((i) => i.assetId === asset.id && !i.resolvedAt);
      if (!existing && asset.status === 'down') state.incidents.unshift({ id: `inc-${Date.now()}`, assetId: asset.id, title: `${asset.name} indisponível`, type: result.type, severity: asset.tier === 'high' ? 'high' : 'medium', startedAt: new Date().toISOString(), resolvedAt: null, durationMinutes: 0 });
    }
  }
  // Dependências são aplicadas depois das sondagens concorrentes: um gateway
  // confirmado como down torna os filhos inalcançáveis no mesmo ciclo.
  for (const asset of state.assets) {
    const parent = state.assets.find((candidate) => candidate.id === asset.parentId);
    if (parent?.status === 'down') {
      asset.status = 'unreachable';
      asset.latency = null;
    }
    const open = state.incidents.find((i) => i.assetId === asset.id && !i.resolvedAt);
    if (open && asset.status === 'online') {
      open.resolvedAt = new Date().toISOString();
      open.durationMinutes = Math.max(1, Math.round((Date.now() - Date.parse(open.startedAt)) / 60000));
    }
  }
  state.lastPollAt = new Date().toISOString();
  state.cycle += 1;
}
function snapshot() { return { ...metrics(), assets: state.assets, incidents: state.incidents.slice(0, 12), lastPollAt: state.lastPollAt, cycle: state.cycle, scenario: state.scenario }; }
function contentType(path) { return { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8' }[extname(path)] || 'application/octet-stream'; }
async function serve(req, res) {
  const url = new URL(req.url, `http://${req.headers.host}`);
  if (url.pathname === '/api/snapshot') return json(res, 200, snapshot());
  if (url.pathname === '/api/poll' && req.method === 'POST') { await runPollCycle(); return json(res, 200, snapshot()); }
  if (url.pathname === '/api/scenario' && req.method === 'POST') { const body = await parseBody(req); state.scenario = body.scenario === 'outage' ? 'outage' : 'normal'; if (state.scenario === 'normal') state.assets.forEach((a) => { if (a.id === 'gw-central') a.consecutiveFailures = 0; }); await runPollCycle(); return json(res, 200, snapshot()); }
  if (url.pathname === '/api/assets' && req.method === 'POST') { const body = await parseBody(req); if (!body.name || !safeAddress(body.address)) return json(res, 400, { error: 'Endereço inválido: use URL autorizada ou IPv4 privado.' }); const asset = { id: `asset-${Date.now()}`, name: String(body.name).slice(0, 80), address: body.address, category: body.category || 'Rede', tier: body.tier || 'medium', location: body.location || 'Não informado', parentId: body.parentId || null, protocol: /^https?:/i.test(body.address) ? 'HTTP' : 'ICMP', status: 'online', latency: 0, consecutiveFailures: 0, uptime: 100, createdAt: new Date().toISOString() }; state.assets.push(asset); return json(res, 201, asset); }
  if (url.pathname.startsWith('/api/assets/') && req.method === 'DELETE') { const id = url.pathname.split('/').pop(); state.assets = state.assets.filter((a) => a.id !== id); return json(res, 200, { ok: true }); }
  const requested = normalize(join(PUBLIC, url.pathname === '/' ? 'index.html' : url.pathname)); if (!requested.startsWith(PUBLIC)) return json(res, 403, { error: 'forbidden' }); try { const data = await readFile(requested); res.writeHead(200, { 'content-type': contentType(requested) }); res.end(data); } catch { const data = await readFile(join(PUBLIC, 'index.html')); res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(data); }
}
setInterval(() => runPollCycle().catch(() => {}), POLL_INTERVAL_MS);
http.createServer((req, res) => serve(req, res).catch((error) => json(res, 400, { error: error.message }))).listen(PORT, '0.0.0.0', () => console.log(`CityPulse NOC running on http://0.0.0.0:${PORT}`));
