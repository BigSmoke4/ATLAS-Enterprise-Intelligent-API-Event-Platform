/**
 * Command Centre page module: refreshes the headline telemetry, redraws the
 * traffic chart, follows incident changes over SignalR (with polling
 * fallback) and drives the AI operations dock.
 */

import { get, post, readJsonIsland } from './../core/api-client.js';
import { notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler, refreshAll } from './../components/command-panel.js';
import { renderChart } from './../components/charts.js';
import { initInstruments } from './../components/gauges.js';
import * as signalr from './../core/signalr-client.js';

const organisationId = document.body.dataset.organizationId ?? '';
const windowMinutes = Number(document.body.dataset.windowMinutes ?? 60);

function setMetric(name, value, tone = null) {
  document.querySelectorAll(`[data-metric="${name}"]`).forEach((element) => {
    element.textContent = value;
    element.classList.remove('atlas-metric__value--ok', 'atlas-metric__value--warn', 'atlas-metric__value--bad');
    if (tone) element.classList.add(`atlas-metric__value--${tone}`);
  });
}

const formatInt = (value) => (Number.isFinite(value) ? Math.round(value).toLocaleString() : '—');
const formatMs = (value) => (Number.isFinite(value) ? `${value.toFixed(1)} ms` : '—');
const formatPercent = (ratio) => (Number.isFinite(ratio) ? `${(ratio * 100).toFixed(2)}%` : '—');

async function refreshTelemetry() {
  if (!organisationId) return;

  const summary = await get(`/api/v1/metrics/summary?organizationId=${organisationId}&windowMinutes=${windowMinutes}`);
  const hasData = summary?.hasData === true;

  setMetric('requestsPerSecond', hasData ? summary.requestsPerSecond.toFixed(1) : '—');
  setMetric('requests', hasData ? formatInt(summary.requests) : '—');
  setMetric('errorRate', hasData ? formatPercent(summary.errorRate) : '—',
    hasData ? (summary.errorRate > 0.05 ? 'bad' : summary.errorRate > 0.005 ? 'warn' : 'ok') : null);
  setMetric('p50', hasData ? formatMs(summary.p50Ms) : '—');
  setMetric('p95', hasData ? formatMs(summary.p95Ms) : '—', hasData && summary.p95Ms > 300 ? 'warn' : null);
  setMetric('p99', hasData ? formatMs(summary.p99Ms) : '—');

  document.querySelectorAll('[data-telemetry-state]').forEach((element) => {
    element.textContent = hasData
      ? `Recorded request telemetry · ${summary.serviceCount} service(s) · ${summary.routeCount} route(s)`
      : 'No telemetry available.';
    element.classList.toggle('atlas-empty__hint', !hasData);
  });

  document.querySelectorAll('[data-telemetry-first]').forEach((element) => {
    element.textContent = summary?.firstBucketUtc ? new Date(summary.firstBucketUtc).toLocaleString() : '—';
  });

  document.querySelectorAll('[data-telemetry-last]').forEach((element) => {
    element.textContent = summary?.lastBucketUtc ? new Date(summary.lastBucketUtc).toLocaleString() : '—';
  });
}

async function refreshSeries() {
  if (!organisationId) return;
  const points = await get(`/api/v1/metrics/series?organizationId=${organisationId}&windowMinutes=${windowMinutes}&bucketMinutes=5`);
  if (!Array.isArray(points) || points.length === 0) return;

  const container = document.querySelector('[data-atlas-chart="traffic"]');
  if (!container) return;

  renderChart(container, {
    labels: points.map((point) => point.bucketStartUtc),
    series: [
      { name: 'requests', tone: 'requests', kind: 'area', values: points.map((point) => point.requests) },
      { name: 'server errors', tone: 'errors', kind: 'bars', values: points.map((point) => point.serverErrors) },
      { name: 'p95', tone: 'latency', kind: 'line', unit: ' ms', values: points.map((point) => point.p95Ms) }
    ]
  });
}

async function refreshIncidents() {
  if (!organisationId) return;
  const incidents = await get(`/api/v1/incidents?organizationId=${organisationId}&page=1&pageSize=25`);
  const list = document.querySelector('[data-incident-list]');
  if (!list || !Array.isArray(incidents)) return;

  const active = incidents.filter((incident) => !['Resolved', 'PostmortemComplete'].includes(incident.status));
  setMetric('activeIncidents', String(active.length), active.length > 0 ? 'bad' : 'ok');

  if (active.length === 0) {
    list.innerHTML = '<div class="atlas-empty atlas-empty--inline"><span class="atlas-empty__title">No active incidents</span></div>';
    return;
  }

  list.replaceChildren();
  active.forEach((incident) => {
    const row = document.createElement('div');
    row.className = 'atlas-incident-row';

    const severity = document.createElement('span');
    // Enums arrive as names ("Sev2"); the CSS modifier follows the name.
    severity.className = `atlas-severity atlas-severity--${String(incident.severity ?? '').toLowerCase()}`;
    severity.textContent = incident.severity;

    const title = document.createElement('div');
    title.className = 'atlas-incident-row__title';
    const link = document.createElement('a');
    link.href = `/Incidents?organizationId=${organisationId}`;
    link.textContent = incident.title;
    const meta = document.createElement('span');
    meta.className = 'atlas-incident-row__meta';
    meta.textContent = `${incident.status} · started ${new Date(incident.startedAtUtc).toLocaleString()}`;
    title.append(link, meta);

    const badge = document.createElement('span');
    badge.className = 'atlas-badge atlas-badge--bad';
    badge.textContent = incident.status;

    row.append(severity, title, badge);
    list.appendChild(row);
  });
}

async function askAssistant(question) {
  const answerElement = document.querySelector('[data-ai-answer]');
  const evidenceElement = document.querySelector('[data-ai-evidence]');
  if (!answerElement) return;

  answerElement.textContent = 'Querying ATLAS tools…';
  evidenceElement?.replaceChildren();

  try {
    const payload = await post('/api/v1/ai/ask', {
      organizationId: organisationId,
      question,
      toolNames: ['GetServiceHealth', 'GetIncidentHistory', 'GetSloStatus', 'GetRootCauseAnalysis']
    });

    // The assistant returns AiAnswer { hasSufficientEvidence, text, citations }.
    // When there is no evidence the server's own wording is shown verbatim —
    // the console never upgrades "insufficient evidence" into a guess.
    answerElement.textContent = payload?.text ?? 'Insufficient evidence.';
    if (payload?.hasSufficientEvidence === false) {
      answerElement.classList.add('atlas-text-dim');
    } else {
      answerElement.classList.remove('atlas-text-dim');
    }

    if (Array.isArray(payload?.citations) && evidenceElement) {
      payload.citations.forEach((item) => {
        const block = document.createElement('div');
        block.className = 'atlas-ai-evidence';
        const source = document.createElement('div');
        source.className = 'atlas-ai-evidence__source';
        source.textContent = item.toolName ?? 'tool';
        const text = document.createElement('div');
        text.textContent = item.description ?? '';
        block.append(source, text);
        evidenceElement.appendChild(block);
      });
    }
  } catch (error) {
    answerElement.textContent = 'The AI operations endpoint returned an error.';
    notifyFromError(error, 'AI query failed');
  }
}

export async function init() {
  registerRefreshHandler('dashboard-telemetry', refreshTelemetry);
  registerRefreshHandler('dashboard-series', refreshSeries);
  registerRefreshHandler('dashboard-incidents', refreshIncidents);

  await refreshAll({ silent: true });

  const askForm = document.querySelector('[data-ai-form]');
  askForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const input = askForm.querySelector('input[name="question"], textarea[name="question"]');
    const question = (input?.value ?? '').trim();
    if (!question) return;
    await askAssistant(question);
  });

  document.querySelectorAll('[data-ai-prompt]').forEach((button) => {
    button.addEventListener('click', async () => {
      const input = document.querySelector('[data-ai-form] input[name="question"], [data-ai-form] textarea[name="question"]');
      if (input) input.value = button.dataset.aiPrompt ?? '';
      await askAssistant(button.dataset.aiPrompt ?? '');
    });
  });

  if (organisationId) {
    await signalr.start(organisationId);
    signalr.onIncidentUpdate(() => {
      refreshIncidents().catch(() => {});
    });
  }

  const events = readJsonIsland('dashboard-bootstrap');
  if (events?.note) {
    const target = document.querySelector('[data-bootstrap-note]');
    if (target) target.textContent = events.note;
  }

  initInstruments(document);
  const status = document.querySelector('[data-dashboard-status]');
  if (status) reportStatus(status, `Monitoring window: last ${windowMinutes} minutes`, 'info');
  notifyOk('Command Centre online', 'Telemetry refreshes on the selected cadence.');
}
