/**
 * Incident board module: declare, transition, root-cause and postmortem
 * operations against the incident application service, with explicit
 * confirmation on state changes and live updates over SignalR.
 */

import { get, post, readJsonIsland } from './../core/api-client.js';
import { notifyError, notifyFromError, notifyOk } from './../core/notifications.js';
import { registerRefreshHandler, refreshAll } from './../components/command-panel.js';
import { confirmAction } from './../core/modal.js';
import * as signalr from './../core/signalr-client.js';

const organisationId = document.body.dataset.organizationId ?? '';
const stageOrder = ['Detected', 'Investigating', 'Mitigating', 'Resolved', 'PostmortemComplete'];

function renderStages(container, status) {
  container.replaceChildren();
  const currentIndex = stageOrder.findIndex((stage) => stage.toLowerCase() === (status ?? '').toLowerCase());
  stageOrder.forEach((stage, index) => {
    const chip = document.createElement('span');
    chip.className = 'atlas-stage';
    if (currentIndex >= 0 && index < currentIndex) chip.classList.add('atlas-stage--done');
    if (index === currentIndex) chip.classList.add('atlas-stage--current');
    chip.textContent = stage;
    container.appendChild(chip);
  });
}

async function refreshIncidents() {
  if (!organisationId) return;
  const incidents = await get(`/api/v1/incidents?organizationId=${organisationId}&page=1&pageSize=50`);
  const list = document.querySelector('[data-incident-board-list]');
  if (!list || !Array.isArray(incidents)) return;

  list.replaceChildren();
  if (incidents.length === 0) {
    list.innerHTML = '<div class="atlas-empty"><span class="atlas-empty__glyph">0</span><span class="atlas-empty__title">No incidents on record</span><span class="atlas-empty__hint">Incidents raised by the policy engine, alert sinks or operators appear here.</span></div>';
    return;
  }

  incidents.forEach((incident) => {
    // Enums cross the wire as names ("Sev2"), so the CSS modifier is derived
    // from the name rather than assumed to be a number.
    const severity = String(incident.severity ?? '').toLowerCase();
    const card = document.createElement('article');
    card.className = `atlas-incident-card atlas-incident-card--${severity}`;
    card.dataset.incidentId = incident.id;

    const head = document.createElement('div');
    head.className = 'atlas-incident-card__head';
    const title = document.createElement('div');
    title.className = 'atlas-incident-card__title';
    title.textContent = incident.title;
    const badge = document.createElement('span');
    badge.className = `atlas-badge ${incident.status === 'Resolved' || incident.status === 'PostmortemComplete' ? 'atlas-badge--ok' : 'atlas-badge--bad'}`;
    badge.textContent = incident.status;
    head.append(title, badge);

    const meta = document.createElement('div');
    meta.className = 'atlas-incident-card__meta';
    meta.textContent = `${incident.severity} · started ${new Date(incident.startedAtUtc).toLocaleString()} · detected ${incident.detectedAtUtc ? new Date(incident.detectedAtUtc).toLocaleString() : 'n/a'}`;

    const stages = document.createElement('div');
    stages.className = 'atlas-stage-rail';
    renderStages(stages, incident.status);

    const actions = document.createElement('div');
    actions.className = 'atlas-incident-card__actions';

    ['Investigating', 'Mitigating', 'Resolved'].forEach((target) => {
      if (target.toLowerCase() === (incident.status ?? '').toLowerCase()) return;
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'atlas-button atlas-button--sm';
      button.textContent = `→ ${target}`;
      button.addEventListener('click', async () => {
        const confirmed = await confirmAction({
          title: `TRANSITION TO ${target.toUpperCase()}`,
          message: `Move "${incident.title}" to ${target}? The transition is written to the incident timeline and audit trail.`,
          confirmLabel: 'TRANSITION'
        });
        if (!confirmed) return;
        try {
          await post(`/api/v1/incidents/${incident.id}/transition`, { organizationId: organisationId, target, note: `Operator transition to ${target}.` });
          notifyOk('Incident updated', `${incident.title} → ${target}`);
          await refreshAll({ silent: true });
        } catch (error) {
          notifyFromError(error, 'Transition failed');
        }
      });
      actions.appendChild(button);
    });

    const analysisButton = document.createElement('button');
    analysisButton.type = 'button';
    analysisButton.className = 'atlas-button atlas-button--ghost atlas-button--sm';
    analysisButton.textContent = 'ROOT CAUSE';
    analysisButton.addEventListener('click', async () => {
      const panel = document.querySelector('[data-incident-detail]');
      try {
        const analysis = await get(`/api/v1/incidents/${incident.id}/root-cause-analysis`);
        if (panel) {
          panel.querySelector('[data-analysis-title]').textContent = incident.title;
          panel.querySelector('[data-analysis-conclusion]').textContent = analysis?.analysisAvailable ? analysis.conclusion : 'Insufficient evidence.';
          panel.querySelector('[data-analysis-confidence]').textContent = analysis?.analysisAvailable ? `${(analysis.confidence * 100).toFixed(0)}% confidence` : 'no confidence score';
          const evidence = panel.querySelector('[data-analysis-evidence]');
          evidence.replaceChildren();
          (analysis?.evidence ?? []).forEach((item) => {
            const block = document.createElement('div');
            block.className = 'atlas-ai-evidence';
            block.textContent = `${item.source}: ${item.observation}`;
            evidence.appendChild(block);
          });
        }
      } catch (error) {
        notifyFromError(error, 'Root-cause analysis failed');
      }
    });

    actions.appendChild(analysisButton);
    card.append(head, meta, stages, actions);
    list.appendChild(card);
  });
}

export async function init() {
  registerRefreshHandler('incidents', refreshIncidents);
  await refreshAll({ silent: true });

  const declared = readJsonIsland('incidents-bootstrap');
  if (declared?.canDeclare === false) {
    document.querySelectorAll('[data-incident-declare]').forEach((element) => element.classList.add('atlas-hidden'));
  }

  document.querySelector('[data-incident-form]')?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const title = form.querySelector('[name="title"]')?.value?.trim();
    const severity = form.querySelector('[name="severity"]')?.value;
    if (!title) return;

    try {
      // The API requires when the incident actually started (MTTD is computed
      // from it) and which services were affected; the console records "now"
      // and an empty affected-services set rather than inventing service ids.
      await post('/api/v1/incidents', {
        organizationId: organisationId,
        title,
        severity,
        startedAtUtc: new Date().toISOString(),
        affectedServiceIds: []
      });
      notifyOk('Incident declared', title);
      form.reset();
      await refreshAll({ silent: true });
    } catch (error) {
      notifyError('Declaration failed', error?.detail ?? error?.message ?? 'Unknown error');
    }
  });

  if (organisationId) {
    await signalr.start(organisationId);
    signalr.onIncidentUpdate(() => refreshIncidents().catch(() => {}));
  }
}
