/**
 * ATLAS client bootstrap.
 *
 * Loaded as a module (type="module") from _Layout. It installs the shared
 * behaviours every page needs (instruments, charts, data tables, command bar)
 * and then lazily imports the page module named by `document.body.dataset.page`
 * — so a page only downloads the JavaScript it actually uses.
 */

import { readJsonIsland } from './core/api-client.js';
import { initCommandPanel } from './components/command-panel.js';
import { initDataTables } from './components/data-table.js';
import { initCharts } from './components/charts.js';
import { initInstruments, observeInstruments } from './components/gauges.js';

const PAGE_MODULES = {
  dashboard: () => import('./pages/dashboard.js'),
  services: () => import('./pages/services.js'),
  incidents: () => import('./pages/incidents.js'),
  observability: () => import('./pages/observability.js'),
  apis: () => import('./pages/api-management.js'),
  events: () => import('./pages/events.js'),
  deployments: () => import('./pages/deployments.js'),
  policies: () => import('./pages/policies.js'),
  ai: () => import('./pages/ai.js'),
  audit: () => import('./pages/audit.js')
};

async function boot() {
  installGlobalErrorHandling();

  initInstruments(document);
  observeInstruments(document);
  initCharts(document, (id) => readJsonIsland(id));
  initDataTables(document);
  initCommandPanel(document);

  const page = document.body.dataset.page;
  const loader = PAGE_MODULES[page];
  if (loader) {
    try {
      const module = await loader();
      await module.init?.();
    } catch (error) {
      // A page module failure must not blank the console: the server-rendered
      // readouts stay visible and the error is reported in the toast rail.
      const { notifyError } = await import('./core/notifications.js');
      notifyError('Page initialisation failed', error?.message ?? 'Unexpected error.');
    }
  }

  window.ATLAS = Object.freeze({ version: 'v1', evidenceFirst: true, page: page ?? 'unknown' });
}

function installGlobalErrorHandling() {
  window.addEventListener('unhandledrejection', async (event) => {
    const { notifyError } = await import('./core/notifications.js');
    const reason = event.reason;
    notifyError('Request failed', reason?.detail ?? reason?.message ?? 'Unexpected error.');
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
