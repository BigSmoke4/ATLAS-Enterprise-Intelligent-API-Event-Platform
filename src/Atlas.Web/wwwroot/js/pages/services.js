/**
 * Service Registry module: topology map, instance health, dependency list and
 * traffic-policy configuration — all driven by the JSON payload Razor rendered
 * from ServiceRegistry/TrafficManagement application services.
 */

import { get, post, put, readJsonIsland } from './../core/api-client.js';
import { notifyError, notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler, refreshAll } from './../components/command-panel.js';
import { renderTopology } from './../components/service-map.js';
import { confirmAction } from './../core/modal.js';

const organisationId = document.body.dataset.organizationId ?? '';
let topologyModel = readJsonIsland('services-topology', { nodes: [] });

function renderDetail(node) {
  const panel = document.querySelector('[data-service-detail]');
  if (!panel || !node) return;

  panel.querySelector('[data-detail-name]').textContent = node.name;
  panel.querySelector('[data-detail-health]').textContent = node.health;
  panel.querySelector('[data-detail-instances]').textContent = `${node.healthyInstances}/${node.instances}`;
  panel.querySelector('[data-detail-rps]').textContent = Number.isFinite(node.requestsPerSecond) ? node.requestsPerSecond.toFixed(2) : '—';
  panel.querySelector('[data-detail-p95]').textContent = Number.isFinite(node.p95Ms) ? `${node.p95Ms.toFixed(1)} ms` : 'no telemetry';
  panel.querySelector('[data-detail-version]').textContent = node.deploymentVersion || 'unversioned';

  const instanceList = panel.querySelector('[data-detail-instance-list]');
  if (instanceList) {
    instanceList.replaceChildren();
    (node.instanceDetails ?? []).forEach((instance) => {
      const row = document.createElement('div');
      row.className = 'atlas-instance';
      const host = document.createElement('span');
      host.className = 'atlas-instance__host';
      host.textContent = instance.hostAndPort;
      const lamp = document.createElement('span');
      lamp.className = `atlas-lamp atlas-lamp--${toneFor(instance.health)}`;
      const check = document.createElement('button');
      check.type = 'button';
      check.className = 'atlas-button atlas-button--ghost atlas-button--sm';
      check.textContent = 'PROBE';
      check.addEventListener('click', async () => {
        try {
          await post(`/api/v1/service-instances/${instance.instanceId}/health-check`, {});
          notifyOk('Health check recorded', `${instance.hostAndPort} probed.`);
          await refreshAll({ silent: true });
        } catch (error) {
          notifyFromError(error, 'Health check failed');
        }
      });
      row.append(lamp, host, check);
      instanceList.appendChild(row);
    });

    if ((node.instanceDetails ?? []).length === 0) {
      const empty = document.createElement('div');
      empty.className = 'atlas-empty atlas-empty--inline';
      empty.innerHTML = '<span class="atlas-empty__hint">No instances registered for this service.</span>';
      instanceList.appendChild(empty);
    }
  }

  panel.dataset.serviceId = node.id;

  renderTrafficTargets(node);
}

/**
 * Rebuilds the routing-target rows for the selected service from the same
 * registry payload the topology map uses — the operator never types an
 * instance id, so a target can only ever be a real registered instance.
 */
function renderTrafficTargets(node) {
  const form = document.querySelector('[data-traffic-form]');
  const list = form?.querySelector('[data-target-list]');
  if (!form || !list) return;

  form.querySelector('[name="serviceId"]').value = node.id ?? '';
  list.replaceChildren();

  const instances = node.instanceDetails ?? [];
  if (instances.length === 0) {
    list.innerHTML = '<span class="atlas-freshness">No instances registered — a routing policy has nothing to target yet.</span>';
    return;
  }

  instances.forEach((instance) => {
    const row = document.createElement('div');
    row.className = 'atlas-traffic-target';
    row.dataset.targetRow = '';
    row.dataset.instanceId = instance.instanceId;

    const host = document.createElement('span');
    host.className = 'atlas-instance__host atlas-mono atlas-small';
    host.textContent = instance.hostAndPort;

    const weight = document.createElement('input');
    weight.className = 'atlas-input atlas-input--sm';
    weight.type = 'number';
    weight.name = 'weight';
    weight.min = '0';
    weight.max = '100';
    weight.title = 'Weight percent (weighted / canary / blue-green strategies)';
    weight.value = '0';

    const priority = document.createElement('input');
    priority.className = 'atlas-input atlas-input--sm';
    priority.type = 'number';
    priority.name = 'priority';
    priority.min = '0';
    priority.max = '10';
    priority.title = 'Priority (priority strategy)';
    priority.value = '0';

    row.append(host, weight, priority);
    list.appendChild(row);
  });
}

function toneFor(health) {
  switch ((health ?? '').toLowerCase()) {
    case 'healthy': return 'ok';
    case 'degraded': return 'warn';
    case 'unhealthy':
    case 'unavailable': return 'bad';
    default: return 'idle';
  }
}

async function refreshTopology() {
  if (!organisationId) return;
  topologyModel = await get(`/api/v1/services/topology?organizationId=${organisationId}`);
  const container = document.querySelector('[data-atlas-topology]');
  if (container) {
    renderTopology(container, topologyModel, {
      searchInput: document.querySelector('[data-topology-search]'),
      onSelect: renderDetail
    });
  }

  const summary = document.querySelector('[data-topology-summary]');
  if (summary) {
    const nodes = topologyModel?.nodes ?? [];
    const healthy = nodes.filter((node) => (node.health ?? '').toLowerCase() === 'healthy').length;
    summary.textContent = nodes.length === 0
      ? 'No telemetry available.'
      : `${nodes.length} service(s) · ${healthy} healthy · generated ${new Date(topologyModel.generatedAtUtc).toLocaleTimeString()}`;
  }
}

async function configureTraffic(form) {
  const serviceId = form.querySelector('[name="serviceId"]')?.value;
  const strategy = form.querySelector('[name="strategy"]')?.value;
  const mode = form.querySelector('[name="mode"]')?.value;
  const targets = [];

  form.querySelectorAll('[data-target-row]').forEach((row) => {
    const instanceId = row.dataset.instanceId;
    const weight = Number(row.querySelector('[name="weight"]')?.value ?? 0);
    const priority = Number(row.querySelector('[name="priority"]')?.value ?? 0);
    if (instanceId) targets.push({ instanceId, weightPercent: weight, priority });
  });

  const status = form.querySelector('[data-traffic-status]');
  try {
    await put(`/api/v1/traffic/policies/${serviceId}`, { organizationId: organisationId, strategy, mode, targets });
    notifyOk('Routing policy saved', `${strategy} / ${mode} · ${targets.length} target(s)`);
    reportStatus(status, 'Saved. Routing decisions now use this configuration.', 'ok');
    await refreshAll({ silent: true });
  } catch (error) {
    reportStatus(status, error?.detail ?? error?.message ?? 'Save failed.', 'error');
    notifyFromError(error, 'Routing policy save failed');
  }
}

export async function init() {
  const container = document.querySelector('[data-atlas-topology]');
  if (container) {
    renderTopology(container, topologyModel, {
      searchInput: document.querySelector('[data-topology-search]'),
      onSelect: renderDetail
    });
  }

  if (topologyModel?.nodes?.length) renderDetail(topologyModel.nodes[0]);

  registerRefreshHandler('services-topology', refreshTopology);
  await refreshTopology();

  document.querySelector('[data-traffic-form]')?.addEventListener('submit', async (event) => {
    event.preventDefault();
    await configureTraffic(event.currentTarget);
  });

  document.querySelectorAll('[data-atlas-confirm]').forEach((button) => {
    button.addEventListener('click', async (event) => {
      event.preventDefault();
      const confirmed = await confirmAction({
        title: button.dataset.confirmTitle ?? 'CONFIRM ACTION',
        message: button.dataset.confirmMessage ?? 'Proceed with this operation?',
        hint: button.dataset.confirmHint ?? 'The action is audit-logged.'
      });
      if (!confirmed) return;

      try {
        await post(button.dataset.confirmUrl, JSON.parse(button.dataset.confirmBody ?? '{}'));
        notifyOk('Operation submitted', button.dataset.confirmTitle ?? '');
        await refreshAll({ silent: true });
      } catch (error) {
        notifyError('Operation failed', error?.detail ?? error?.message ?? 'Unknown error');
      }
    });
  });
}
