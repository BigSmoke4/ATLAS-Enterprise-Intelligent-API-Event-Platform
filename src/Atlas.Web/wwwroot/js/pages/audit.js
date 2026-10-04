/**
 * Audit module: read-only view over the append-only audit trail.
 *
 * There is deliberately nothing here that can modify an entry — the API has no
 * write action, and AuditDbContext rejects Modified/Deleted states, so a
 * console bug cannot rewrite history. Filtering re-queries the API.
 */

import { get, readJsonIsland } from './../core/api-client.js';
import { notifyFromError } from './../core/notifications.js';
import { registerRefreshHandler } from './../components/command-panel.js';

const bootstrap = readJsonIsland('audit-bootstrap', { organizationId: '' });
const organisationId = bootstrap.organizationId || (document.body.dataset.organizationId ?? '');

function filters() {
  const action = document.querySelector('[data-audit-filter="action"]')?.value?.trim() ?? '';
  const resourceType = document.querySelector('[data-audit-filter="resourceType"]')?.value?.trim() ?? '';
  const params = new URLSearchParams({ page: '1', pageSize: '100' });
  if (organisationId) params.set('organizationId', organisationId);
  if (action) params.set('action', action);
  if (resourceType) params.set('resourceType', resourceType);
  return params.toString();
}

async function refreshAudit() {
  if (!organisationId) return;
  const entries = await get(`/api/v1/audit?${filters()}`);
  const body = document.querySelector('[data-audit-body]');
  if (!body || !Array.isArray(entries)) return;

  body.replaceChildren();
  if (entries.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 7;
    cell.textContent = 'No audit entries match this filter.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  entries.forEach((entry) => {
    const row = document.createElement('tr');
    const values = [
      new Date(entry.createdAtUtc).toLocaleString(),
      entry.actorDisplay || 'system',
      entry.action,
      entry.resourceType,
      entry.resourceId,
      entry.correlationId,
      entry.ipAddress ?? '—'
    ];
    values.forEach((value, index) => {
      const cell = document.createElement('td');
      cell.textContent = String(value ?? '—');
      if (index === 5) cell.className = 'atlas-mono atlas-small';
      if (index === 2) cell.className = 'atlas-mono';
      row.appendChild(cell);
    });
    body.appendChild(row);
  });
}

export async function init() {
  registerRefreshHandler('audit', refreshAudit);
  try {
    await refreshAudit();
  } catch (error) {
    notifyFromError(error, 'Audit listing failed');
  }

  document.querySelectorAll('[data-audit-filter]').forEach((input) => {
    input.addEventListener('change', () => refreshAudit().catch((error) => notifyFromError(error, 'Audit filter failed')));
  });

  document.querySelector('[data-audit-reset]')?.addEventListener('click', () => {
    document.querySelectorAll('[data-audit-filter]').forEach((input) => { input.value = ''; });
    refreshAudit().catch((error) => notifyFromError(error, 'Audit refresh failed'));
  });
}
