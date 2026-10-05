/**
 * Observability module: traffic series, latency percentiles, SLO/error-budget
 * board and infrastructure probes. Every plate states its evidence source and
 * shows "No telemetry available." when the source is empty.
 */

import { get } from './../core/api-client.js';
import { registerRefreshHandler } from './../components/command-panel.js';
import { renderChart } from './../components/charts.js';
import { renderBar, renderMeter } from './../components/gauges.js';
import { notifyFromError } from './../core/notifications.js';

const organisationId = document.body.dataset.organizationId ?? '';
let windowMinutes = Number(document.body.dataset.windowMinutes ?? 60);

function setText(selector, value) {
  document.querySelectorAll(selector).forEach((element) => { element.textContent = value; });
}

const formatMs = (value) => (Number.isFinite(value) ? `${value.toFixed(1)} ms` : '—');
const formatRatio = (value) => (Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : '—');
const formatCount = (value) => (Number.isFinite(value) ? Math.round(value).toLocaleString() : '—');

async function refreshSeries() {
  if (!organisationId) return;
  const points = await get(`/api/v1/metrics/series?organizationId=${organisationId}&windowMinutes=${windowMinutes}&bucketMinutes=${windowMinutes <= 120 ? 5 : 30}`);
  const container = document.querySelector('[data-atlas-chart="observability-traffic"]');
  if (!container || !Array.isArray(points) || points.length === 0) return;

  renderChart(container, {
    labels: points.map((point) => point.bucketStartUtc),
    series: [
      { name: 'requests', tone: 'requests', kind: 'area', values: points.map((point) => point.requests) },
      { name: 'server errors', tone: 'errors', kind: 'bars', values: points.map((point) => point.serverErrors) },
      { name: 'client errors', tone: 'neutral', kind: 'line', values: points.map((point) => point.clientErrors) }
    ]
  });
}

async function refreshSummary() {
  if (!organisationId) return;
  const summary = await get(`/api/v1/metrics/summary?organizationId=${organisationId}&windowMinutes=${windowMinutes}`);

  setText('[data-obs="requests"]', formatCount(summary.requests));
  setText('[data-obs="rps"]', summary.hasData ? summary.requestsPerSecond.toFixed(2) : '—');
  setText('[data-obs="errorRate"]', summary.hasData ? formatRatio(summary.errorRate) : '—');
  setText('[data-obs="p50"]', summary.hasData ? formatMs(summary.p50Ms) : '—');
  setText('[data-obs="p95"]', summary.hasData ? formatMs(summary.p95Ms) : '—');
  setText('[data-obs="p99"]', summary.hasData ? formatMs(summary.p99Ms) : '—');
  setText('[data-obs="max"]', summary.hasData ? formatMs(summary.maxMs) : '—');
  setText('[data-obs="services"]', formatCount(summary.serviceCount));
  setText('[data-obs="routes"]', formatCount(summary.routeCount));
  setText('[data-obs="clientErrors"]', formatCount(summary.clientErrors));

  const state = document.querySelector('[data-obs="state"]');
  if (state) {
    state.textContent = summary.hasData
      ? `Evidence source: live request telemetry · first bucket ${new Date(summary.firstBucketUtc).toLocaleString()}`
      : 'No telemetry available.';
  }

  const errorMeter = document.querySelector('[data-obs-meter="errorRate"]');
  if (errorMeter) renderMeter(errorMeter, { fraction: summary.hasData ? Math.min(1, summary.errorRate / 0.05) : null, warnAt: 0.2, badAt: 0.6 });

  const p95Meter = document.querySelector('[data-obs-meter="p95"]');
  if (p95Meter) renderMeter(p95Meter, { fraction: summary.hasData && summary.p95Ms ? Math.min(1, summary.p95Ms / 1000) : null, warnAt: 0.3, badAt: 0.6 });
}

async function refreshRoutes() {
  if (!organisationId) return;
  const routes = await get(`/api/v1/metrics/routes?organizationId=${organisationId}&windowMinutes=${windowMinutes}&limit=25`);
  const body = document.querySelector('[data-route-metrics-body]');
  if (!body || !Array.isArray(routes)) return;

  body.replaceChildren();
  if (routes.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 6;
    cell.textContent = 'No telemetry available.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  routes.forEach((route) => {
    const row = document.createElement('tr');
    const cells = [
      route.route,
      route.httpMethod,
      formatCount(route.requests),
      formatCount(route.serverErrors),
      formatRatio(route.errorRate),
      Number.isFinite(route.p95Ms) ? formatMs(route.p95Ms) : '—'
    ];
    cells.forEach((value, index) => {
      const cell = document.createElement('td');
      cell.textContent = String(value);
      if (index === 0) cell.className = 'is-mono';
      if (index >= 2) cell.classList.add('is-numeric');
      row.appendChild(cell);
    });
    if (route.errorRate > 0.05) row.classList.add('is-alert');
    else if (route.errorRate > 0.005) row.classList.add('is-warn');
    body.appendChild(row);
  });
}

async function refreshInfrastructure() {
  if (!organisationId) return;
  const payload = await get(`/api/v1/metrics/infrastructure?organizationId=${organisationId}`);

  const database = payload?.database;
  setText('[data-infra="db-connections"]', database?.available ? `${database.totalConnections} / ${database.maxConnections}` : 'unavailable');
  setText('[data-infra="db-active"]', database?.available ? String(database.activeConnections) : '—');
  setText('[data-infra="db-longest"]', database?.available ? `${database.longestActiveQuerySeconds.toFixed(2)} s` : '—');
  setText('[data-infra="db-commits"]', database?.available ? formatCount(database.transactionsCommitted) : '—');
  setText('[data-infra="db-rollbacks"]', database?.available ? formatCount(database.transactionsRolledBack) : '—');
  setText('[data-infra="db-hit-ratio"]', database?.available ? formatRatio(database.blockCacheHitRatio) : '—');
  setText('[data-infra="db-deadlocks"]', database?.available ? formatCount(database.deadlocks) : '—');

  const utilisation = document.querySelector('[data-infra-meter="db-utilisation"]');
  if (utilisation) renderMeter(utilisation, { fraction: database?.available ? database.connectionUtilization : null, warnAt: 0.7, badAt: 0.9 });

  const cache = payload?.cache;
  setText('[data-infra="cache-hit-rate"]', cache?.available ? formatRatio(cache.hitRate) : '—');
  setText('[data-infra="cache-lookups"]', cache?.available ? formatCount(cache.lookups) : '—');
  setText('[data-infra="cache-invalidations"]', cache?.available ? formatCount(cache.invalidations) : '—');

  const cacheBar = document.querySelector('[data-infra-bar="cache-hit-rate"]');
  if (cacheBar) renderBar(cacheBar, cache?.available ? cache.hitRate : null, cache?.hitRate > 0.5 ? 'ok' : cache?.hitRate > 0.2 ? 'warn' : 'bad');

  const pipeline = payload?.telemetryPipeline;
  setText('[data-infra="telemetry-accepted"]', formatCount(pipeline?.accepted));
  setText('[data-infra="telemetry-processed"]', formatCount(pipeline?.processed));
  setText('[data-infra="telemetry-dropped"]', formatCount(pipeline?.dropped));
  setText('[data-infra="telemetry-buffered"]', formatCount(pipeline?.buffered));

  const lag = payload?.consumerLag;
  setText('[data-infra="lag-total"]', lag?.available ? formatCount(lag.totalLag) : 'unavailable');
  setText('[data-infra="lag-reason"]', lag?.reason ?? '—');

  const lagBody = document.querySelector('[data-lag-body]');
  if (lagBody) {
    lagBody.replaceChildren();
    const partitions = lag?.partitions ?? [];
    if (partitions.length === 0) {
      const row = document.createElement('tr');
      row.className = 'atlas-empty-row';
      const cell = document.createElement('td');
      cell.colSpan = 4;
      cell.textContent = lag?.reason ?? 'No consumer lag reported.';
      row.appendChild(cell);
      lagBody.appendChild(row);
    } else {
      partitions.forEach((partition) => {
        const row = document.createElement('tr');
        [partition.topic, String(partition.partition), formatCount(partition.committedOffset), formatCount(partition.lag)]
          .forEach((value, index) => {
            const cell = document.createElement('td');
            cell.textContent = value;
            if (index === 0) cell.className = 'is-mono';
            if (index > 0) cell.classList.add('is-numeric');
            row.appendChild(cell);
          });
        lagBody.appendChild(row);
      });
    }
  }
}

export async function init() {
  registerRefreshHandler('observability-summary', refreshSummary);
  registerRefreshHandler('observability-series', refreshSeries);
  registerRefreshHandler('observability-routes', refreshRoutes);
  registerRefreshHandler('observability-infrastructure', refreshInfrastructure);

  const windowSelect = document.querySelector('[data-window-select]');
  windowSelect?.addEventListener('change', async () => {
    windowMinutes = Number(windowSelect.value);
    try {
      await Promise.all([refreshSummary(), refreshSeries(), refreshRoutes()]);
    } catch (error) {
      notifyFromError(error, 'Refresh failed');
    }
  });

  try {
    await Promise.all([refreshSummary(), refreshSeries(), refreshRoutes(), refreshInfrastructure()]);
  } catch (error) {
    notifyFromError(error, 'Initial telemetry load failed');
  }
}
