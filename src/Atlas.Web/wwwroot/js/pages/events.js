/**
 * Event pipeline module: dead-letter inspection, dry-run validation and
 * authorised live replay.
 *
 * Replay is the canonical "explicit confirmation + authorization" flow from
 * the master prompt: dry run first (no side effects), then a modal-confirmed
 * live replay that the server audit-logs.
 */

import { get, post } from './../core/api-client.js';
import { notifyError, notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler } from './../components/command-panel.js';
import { confirmAction } from './../core/modal.js';

let filters = { topic: '', eventType: '', page: 1 };

function queryString() {
  const params = new URLSearchParams({ page: String(filters.page), pageSize: '50' });
  if (filters.topic) params.set('topic', filters.topic);
  if (filters.eventType) params.set('eventType', filters.eventType);
  return params.toString();
}

async function refreshDeadLetters() {
  const payload = await get(`/api/v1/events/dead-letters?${queryString()}`);
  const items = Array.isArray(payload) ? payload : payload?.items ?? [];
  const body = document.querySelector('[data-dlq-body]');
  if (!body) return;

  body.replaceChildren();
  if (items.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 8;
    cell.textContent = 'No dead-lettered events. Nothing has exhausted its retry budget.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  items.forEach((item) => {
    const row = document.createElement('tr');
    row.dataset.deadLetterId = item.id;

    const cells = [
      new Date(item.lastFailedAtUtc).toLocaleString(),
      item.originalTopic,
      item.eventType,
      `v${item.version}`,
      String(item.retryCount ?? item.totalFailures ?? 0),
      item.failureReason,
      item.correlationId
    ];

    cells.forEach((value, index) => {
      const cell = document.createElement('td');
      cell.textContent = value ?? '—';
      if (index === 0 || index === 6) cell.classList.add('is-mono');
      if (index === 4) cell.classList.add('is-numeric');
      if (index === 5) cell.classList.add('atlas-truncate');
      row.appendChild(cell);
    });

    const actionCell = document.createElement('td');
    const dryRun = document.createElement('button');
    dryRun.type = 'button';
    dryRun.className = 'atlas-button atlas-button--ghost atlas-button--sm';
    dryRun.textContent = 'DRY RUN';
    dryRun.addEventListener('click', async () => {
      const status = document.querySelector('[data-dlq-status]');
      try {
        // dryRun is a query parameter on the API (the body is ignored by the
        // controller), and a dry run never publishes — safe to run repeatably.
        await post(`/api/v1/events/dead-letters/${item.id}/replay?dryRun=true`, null);
        reportStatus(status, `Dry run passed for event ${item.eventId}: replayable, nothing published.`, 'ok');
      } catch (error) {
        reportStatus(status, replayFailureMessage(error), 'error');
      }
    });

    const replay = document.createElement('button');
    replay.type = 'button';
    replay.className = 'atlas-button atlas-button--danger atlas-button--sm';
    replay.textContent = 'LIVE REPLAY';
    replay.addEventListener('click', async () => {
      const confirmed = await confirmAction({
        title: 'LIVE REPLAY',
        message: `Re-publish ${item.eventType} v${item.version} (event ${item.eventId}) to ${item.originalTopic}? Consumers will process it again; the idempotency guard prevents duplicate business operations.`,
        hint: 'Authorised operation · recorded in the audit trail.',
        confirmLabel: 'REPLAY NOW'
      });
      if (!confirmed) return;

      try {
        await post(`/api/v1/events/dead-letters/${item.id}/replay?dryRun=false`, null);
        notifyOk('Event replayed', `${item.eventType} re-published to ${item.originalTopic}.`);
        await refreshDeadLetters();
      } catch (error) {
        // A 403 here is an authorization decision, not a transient fault: say
        // so plainly instead of inviting the operator to retry.
        if (error?.status === 403) {
          notifyError('Replay not authorised', 'Live replay requires the PlatformAdmin role. You can still run a dry run, or ask an SRE to mark the message replayed once fixed.');
        } else {
          notifyError('Replay failed', replayFailureMessage(error));
        }
      }
    });

    const inspect = document.createElement('button');
    inspect.type = 'button';
    inspect.className = 'atlas-button atlas-button--ghost atlas-button--sm';
    inspect.textContent = 'PAYLOAD';
    inspect.addEventListener('click', async () => {
      const panel = document.querySelector('[data-dlq-payload]');
      if (!panel) return;
      try {
        // The stored payload is fetched from the API rather than embedded in
        // the table: payloads can be large and are only needed on demand.
        const found = await get(`/api/v1/events/dead-letters?eventId=${item.eventId}&page=1&pageSize=1`);
        const record = Array.isArray(found) ? found[0] : found?.items?.[0];
        panel.textContent = record?.payloadJson ?? 'No stored payload was returned for this event.';
      } catch (error) {
        panel.textContent = replayFailureMessage(error);
      }
    });

    actionCell.append(inspect, dryRun, replay);
    row.appendChild(actionCell);
    body.appendChild(row);
  });
}

function replayFailureMessage(error) {
  if (error?.status === 404) return 'This dead letter no longer exists (it may have been replayed already).';
  if (error?.status === 409) return 'This dead letter was already marked as replayed.';
  return error?.detail ?? error?.title ?? error?.message ?? 'Replay request failed.';
}

export async function init() {
  registerRefreshHandler('dead-letters', refreshDeadLetters);
  try {
    await refreshDeadLetters();
  } catch (error) {
    notifyFromError(error, 'Dead-letter listing failed');
  }

  document.querySelectorAll('[data-dlq-filter]').forEach((input) => {
    input.addEventListener('change', async () => {
      filters = { topic: '', eventType: '', page: 1 };
      document.querySelectorAll('[data-dlq-filter]').forEach((field) => {
        if (field.name === 'topic') filters.topic = field.value.trim();
        if (field.name === 'eventType') filters.eventType = field.value.trim();
      });
      await refreshDeadLetters();
    });
  });

  document.querySelectorAll('[data-dlq-publish]').forEach((form) => {
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const status = form.querySelector('[data-dlq-publish-status]');
      const eventType = form.querySelector('[name="eventType"]')?.value?.trim();
      const payloadJson = form.querySelector('[name="payloadJson"]')?.value?.trim();
      if (!eventType || !payloadJson) {
        reportStatus(status, 'Event type and a JSON payload are required.', 'warn');
        return;
      }
      try {
        JSON.parse(payloadJson);
      } catch {
        reportStatus(status, 'Payload is not valid JSON — fix it before publishing.', 'error');
        return;
      }
      try {
        await post('/api/v1/events/publish', {
          organizationId: document.body.dataset.organizationId,
          eventType,
          payloadJson
        });
        notifyOk('Event published', `${eventType} accepted by the producer.`);
        reportStatus(status, 'Accepted by the real Kafka producer.', 'ok');
        form.reset();
      } catch (error) {
        reportStatus(status, error?.detail ?? error?.message ?? 'Publish failed.', 'error');
      }
    });
  });
}
