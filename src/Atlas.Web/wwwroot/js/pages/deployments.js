/**
 * Deployment intelligence module: record deployments and run regression /
 * canary analysis. Analysis output is rendered exactly as the server computed
 * it — including "analysis unavailable" reasons, which are shown verbatim
 * instead of being replaced with a reassuring default.
 */

import { get, post, readJsonIsland } from './../core/api-client.js';
import { notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler } from './../components/command-panel.js';
import { bindForm } from './../core/validation.js';

const organisationId = document.body.dataset.organizationId ?? '';
const services = readJsonIsland('deployments-services', []);

function renderAnalysis(container, outcome, kind) {
  if (!container) return;
  container.replaceChildren();

  // The API states availability explicitly; if a response only carries a
  // result (an older shape), a present result is what makes it readable.
  const available = outcome?.analysisAvailable ?? (outcome?.result !== undefined && outcome?.result !== null);
  if (!available) {
    const empty = document.createElement('div');
    empty.className = 'atlas-empty atlas-empty--inline';
    empty.innerHTML = `<span class="atlas-empty__glyph">!</span><span class="atlas-empty__hint">${outcome?.reason ?? 'Insufficient evidence.'}</span>`;
    container.appendChild(empty);
    return;
  }

  const result = outcome.result ?? outcome;
  const grid = document.createElement('div');
  grid.className = 'atlas-inset atlas-inset--grid';

  const entries = kind === 'canary'
    ? [
        ['Verdict', result.verdict ?? result.recommendation ?? '—'],
        ['Canary error rate', formatRatio(result.canaryErrorRate)],
        ['Baseline error rate', formatRatio(result.baselineErrorRate)],
        ['Canary P95', formatMs(result.canaryP95Ms)],
        ['Baseline P95', formatMs(result.baselineP95Ms)],
        ['Recommendation', result.recommendation ?? '—']
      ]
    : [
        ['Error rate before', formatRatio(result.errorRateBefore)],
        ['Error rate after', formatRatio(result.errorRateAfter)],
        ['P95 before', formatMs(result.p95BeforeMs)],
        ['P95 after', formatMs(result.p95AfterMs)],
        ['Regression detected', result.regressionDetected === true ? 'yes' : result.regressionDetected === false ? 'no' : '—'],
        ['Summary', result.summary ?? result.conclusion ?? '—']
      ];

  entries.forEach(([label, value]) => {
    const metric = document.createElement('div');
    metric.className = 'atlas-metric';
    const caption = document.createElement('span');
    caption.className = 'atlas-metric__label';
    caption.textContent = label;
    const readout = document.createElement('span');
    readout.className = 'atlas-metric__value atlas-metric__value--small';
    readout.textContent = value;
    metric.append(caption, readout);
    grid.appendChild(metric);
  });

  container.appendChild(grid);
}

const formatRatio = (value) => (Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : '—');
const formatMs = (value) => (Number.isFinite(value) ? `${value.toFixed(1)} ms` : '—');

async function refreshDeployments() {
  if (!organisationId) return;
  const deployments = await get(`/api/v1/deployments?organizationId=${organisationId}&page=1&pageSize=50`);
  const body = document.querySelector('[data-deployments-body]');
  if (!body || !Array.isArray(deployments)) return;

  body.replaceChildren();
  if (deployments.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 8;
    cell.textContent = 'No deployments recorded.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  deployments.forEach((deployment) => {
    const row = document.createElement('tr');
    const serviceName = services.find((service) => service.id === deployment.serviceId)?.name ?? deployment.serviceId;

    const values = [
      serviceName,
      deployment.version,
      deployment.environment,
      deployment.commitSha?.slice(0, 10) ?? '—',
      deployment.author,
      new Date(deployment.deployedAtUtc).toLocaleString(),
      deployment.status
    ];

    values.forEach((value, index) => {
      const cell = document.createElement('td');
      cell.textContent = String(value ?? '—');
      if (index === 1 || index === 3) cell.classList.add('is-mono');
      row.appendChild(cell);
    });

    const actions = document.createElement('td');
    const regression = document.createElement('button');
    regression.type = 'button';
    regression.className = 'atlas-button atlas-button--ghost atlas-button--sm';
    regression.textContent = 'REGRESSION';
    regression.addEventListener('click', async () => {
      const panel = document.querySelector('[data-analysis-panel]');
      panel?.classList.remove('atlas-hidden');
      try {
        const outcome = await get(`/api/v1/deployments/${deployment.id}/regression-analysis?organizationId=${organisationId}&windowMinutes=30`);
        renderAnalysis(document.querySelector('[data-regression-result]'), outcome, 'regression');
        const title = document.querySelector('[data-analysis-title]');
        if (title) title.textContent = `${serviceName} ${deployment.version} · regression analysis`;
      } catch (error) {
        notifyFromError(error, 'Regression analysis failed');
      }
    });

    const canary = document.createElement('button');
    canary.type = 'button';
    canary.className = 'atlas-button atlas-button--ghost atlas-button--sm';
    canary.textContent = 'CANARY';
    canary.addEventListener('click', async () => {
      const panel = document.querySelector('[data-analysis-panel]');
      panel?.classList.remove('atlas-hidden');
      try {
        const outcome = await get(`/api/v1/deployments/${deployment.id}/canary-analysis?organizationId=${organisationId}&windowMinutes=30`);
        renderAnalysis(document.querySelector('[data-regression-result]'), outcome, 'canary');
        const title = document.querySelector('[data-analysis-title]');
        if (title) title.textContent = `${serviceName} ${deployment.version} · canary analysis`;
      } catch (error) {
        notifyFromError(error, 'Canary analysis failed');
      }
    });

    actions.append(regression, canary);
    row.appendChild(actions);
    body.appendChild(row);
  });
}

export async function init() {
  registerRefreshHandler('deployments', refreshDeployments);
  await refreshDeployments();

  const form = document.querySelector('[data-deployment-form]');
  if (form) {
    bindForm(form, {
      onSubmit: async (payload) => {
        const status = form.querySelector('[data-deployment-status]');
        await post('/api/v1/deployments', {
          organizationId: organisationId,
          serviceId: payload.serviceId,
          version: payload.version,
          environment: payload.environment,
          commitSha: payload.commitSha,
          author: payload.author
        });
        notifyOk('Deployment recorded', `${payload.version} → ${payload.environment}`);
        reportStatus(status, 'Recorded. Regression analysis now has a before/after boundary.', 'ok');
        form.reset();
        await refreshDeployments();
      },
      onError: (error) => notifyFromError(error, 'Deployment recording failed')
    });
  }
}
