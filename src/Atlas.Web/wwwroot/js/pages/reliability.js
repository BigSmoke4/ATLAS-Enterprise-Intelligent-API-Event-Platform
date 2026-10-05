/**
 * Reliability module.
 *
 * Circuit-breaker state lives in-process, so it is a snapshot rendered by the
 * server: the panel says so, and REFRESH reloads the page rather than
 * pretending a fetch would produce a newer in-process reading.
 *
 * The alert strip IS fetchable: /api/v1/alerts derives alerts from recorded
 * state (registry health, SLO compliance, breaker states, consumer lag,
 * dead-letter backlog), and every alert carries the evidence behind it.
 */

import { get, readJsonIsland } from './../core/api-client.js';
import { notifyFromError, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler } from './../components/command-panel.js';

const bootstrap = readJsonIsland('reliability-bootstrap', { organizationId: '' });
const organisationId = bootstrap.organizationId || (document.body.dataset.organizationId ?? '');

function renderAlerts(snapshot) {
  const body = document.querySelector('[data-alerts-body]');
  const summary = document.querySelector('[data-alerts-summary]');
  if (!body) return;

  const alerts = Array.isArray(snapshot?.alerts) ? snapshot.alerts : [];
  if (summary) {
    summary.textContent = snapshot?.hasEvidence
      ? (alerts.length === 0
        ? `Checked and clear · ${snapshot.evidenceSource}`
        : `${alerts.length} active alert(s) · ${snapshot.evidenceSource}`)
      : `Alert sources could not be read · ${snapshot?.evidenceSource ?? 'no evidence'}`;
  }

  body.replaceChildren();
  if (alerts.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 5;
    cell.textContent = snapshot?.hasEvidence
      ? 'No active alerts: every consulted source reported no problem.'
      : 'No alert could be evaluated — see the sources line above.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  alerts.forEach((alert) => {
    const row = document.createElement('tr');
    const severity = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `atlas-badge atlas-badge--${alert.severity === 'Critical' ? 'bad' : alert.severity === 'Warning' ? 'warn' : 'info'}`;
    badge.textContent = String(alert.severity ?? '').toUpperCase();
    severity.appendChild(badge);

    const source = document.createElement('td');
    source.className = 'atlas-mono atlas-small';
    source.textContent = alert.source ?? '—';

    const summaryCell = document.createElement('td');
    summaryCell.textContent = alert.summary ?? '—';
    const detail = document.createElement('div');
    detail.className = 'atlas-freshness';
    detail.textContent = alert.detail ?? '';
    summaryCell.appendChild(detail);

    const evidence = document.createElement('td');
    evidence.className = 'atlas-freshness';
    const evidenceEntries = Object.entries(alert.evidence ?? {});
    evidence.textContent = evidenceEntries.map(([key, value]) => `${key}=${value}`).join(' · ') || '—';

    const observed = document.createElement('td');
    observed.className = 'atlas-mono atlas-small atlas-nowrap';
    observed.textContent = alert.observedAtUtc ? new Date(alert.observedAtUtc).toLocaleString() : '—';

    row.append(severity, source, summaryCell, evidence, observed);
    body.appendChild(row);
  });
}

async function refreshAlerts() {
  if (!organisationId) {
    const summary = document.querySelector('[data-alerts-summary]');
    if (summary) summary.textContent = 'No organization context — alerts cannot be derived for this session.';
    return;
  }
  const snapshot = await get(`/api/v1/alerts?organizationId=${organisationId}`);
  renderAlerts(snapshot);
}

export async function init() {
  registerRefreshHandler('alerts', refreshAlerts);
  try {
    await refreshAlerts();
  } catch (error) {
    notifyFromError(error, 'Alert evaluation failed');
    reportStatus(document.querySelector('[data-alerts-summary]'), error?.detail ?? error?.message ?? 'Alert evaluation failed.', 'error');
  }

  // Breaker state is in-process: the only honest way to refresh it is to ask
  // the server to render it again.
  document.querySelectorAll('[data-reliability-reload]').forEach((button) => {
    button.addEventListener('click', () => window.location.reload());
  });
}
